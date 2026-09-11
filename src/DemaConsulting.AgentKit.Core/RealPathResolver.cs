namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Resolves a path to its real file system location.
/// </summary>
/// <remarks>
///     <para>
///     Every path-containment decision in AgentKit is ultimately a comparison between a
///     permitted location and the location an operation will actually touch. Those two are not
///     the same thing: a reparse point (a Windows directory junction or symbolic link, or a
///     POSIX symbolic link) placed inside a permitted location redirects the actual I/O
///     somewhere else entirely, while the path <em>string</em> still looks contained. This type
///     exists so that containment is always judged on the real location, never on the string
///     the caller supplied.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
public static class RealPathResolver
{
    /// <summary>
    ///     Resolves a path to its real, absolute location, following symbolic links and
    ///     directory junctions at every path component.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Why normalization alone is not enough.</b>
    ///     <see cref="Path.GetFullPath(string)"/> makes a path absolute and collapses
    ///     <c>.</c> and <c>..</c> segments, but it does <b>not</b> follow reparse points. A
    ///     junction that lives inside a permitted location and points outside it therefore
    ///     produces a fully-qualified path string that passes any containment check while the
    ///     actual read or write lands outside.
    ///     </para>
    ///     <para>
    ///     <b>Why leaf-only resolution is insufficient.</b>
    ///     <see cref="FileSystemInfo.ResolveLinkTarget(bool)"/> reports a target only for an
    ///     entry that is <em>itself</em> a reparse point. A real file sitting <em>beneath</em>
    ///     a junction is an ordinary file, so resolving only the leaf returns
    ///     <see langword="null"/> and the escape goes undetected even though the file is
    ///     physically outside the permitted location. This has been confirmed experimentally
    ///     and is pinned by a regression test.
    ///     </para>
    ///     <para>
    ///     <b>Why deepest-existing-ancestor resolution is insufficient.</b>
    ///     It fails for exactly the same reason: the deepest existing ancestor of a path
    ///     beneath a junction is usually an ordinary directory, not the junction itself, so it
    ///     too reports no link target.
    ///     </para>
    ///     <para>
    ///     <b>Hence the per-component walk.</b> This method starts at the volume root and adds
    ///     one component at a time. At each accumulated prefix it asks the file system whether
    ///     that prefix is a reparse point and, when it is, replaces the accumulated prefix with
    ///     the link's final target before continuing. A reparse point anywhere in the chain is
    ///     therefore caught, no matter how deeply the requested path is nested beneath it.
    ///     <b>Do not replace this walk with a single resolve of the leaf or of the deepest
    ///     existing ancestor</b> — both have been proven not to detect an escape.
    ///     </para>
    ///     <para>
    ///     <b>Cost and non-existent components.</b> Resolution performs at most one metadata
    ///     probe per existing component, all served from the operating system's directory
    ///     cache. Components that do not yet exist — for example a file that is about to be
    ///     created — cannot be reparse points and are appended unchanged, so a not-yet-existing
    ///     path beneath a junction is still reported at its real destination.
    ///     </para>
    ///     <para>
    ///     This method is stateless and thread-safe. It reads file system metadata but never
    ///     creates, modifies or deletes anything.
    ///     </para>
    /// </remarks>
    /// <param name="path">
    ///     The path to resolve. May be absolute or relative to the current working directory,
    ///     and need not exist. Must be non-null and non-empty.
    /// </param>
    /// <returns>
    ///     The real, absolute, normalized location of <paramref name="path"/>, with every
    ///     reparse point along the path replaced by its final target.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="path"/> is an empty string or is not a valid path.
    /// </exception>
    /// <exception cref="IOException">
    ///     Thrown when a link chain is cyclic or exceeds the platform's resolution depth.
    ///     Callers that must not fail on a hostile path should catch this and treat it as a
    ///     denial, which is the fail-safe interpretation.
    /// </exception>
    public static string Resolve(string path)
    {
        // Reject a missing path outright: this is a programming error in the caller, not a
        // policy decision, so it is surfaced as an exception rather than a resolved value.
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Make the path absolute and collapse relative segments. This is a prerequisite for
        // the walk below, not a substitute for it: GetFullPath does not follow reparse points.
        var full = Path.GetFullPath(path);

        // Identify the volume root the walk starts from. A path with no root cannot be walked
        // component by component; returning the normalized form is the defensive fallback.
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
        {
            return full;
        }

        // Split the portion below the volume root into its individual components, accepting
        // either separator so that a caller-supplied mix of separators is handled.
        var components = full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        // Walk outward from the volume root. Whenever a component turns out to be a reparse
        // point, the accumulated prefix is replaced by the link's real target and the remaining
        // components are resolved beneath that target instead.
        var accumulated = root;
        foreach (var component in components)
        {
            accumulated = Path.Combine(accumulated, component);

            // Ask for the final target so that a chain of links is followed to its end in one
            // step rather than requiring this loop to iterate over intermediate links.
            var target = Probe(accumulated)?.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                accumulated = target.FullName;
            }
        }

        // A link target may itself be recorded as a relative path, so normalize once more
        // before returning to guarantee the documented absolute, normalized contract.
        return Path.GetFullPath(accumulated);
    }

    /// <summary>
    ///     Obtains file system metadata for an existing path, or <see langword="null"/> when
    ///     nothing exists there.
    /// </summary>
    /// <remarks>
    ///     Only an entry that exists can be a reparse point, so the walk needs a single probe
    ///     that answers "is there a directory, a file, or nothing here?". A component that
    ///     exists as neither is not an error — it is simply a part of the path that has not
    ///     been created yet, and it is carried through unchanged.
    /// </remarks>
    /// <param name="path">The absolute path to probe.</param>
    /// <returns>
    ///     Metadata for the directory or file at <paramref name="path"/>, or
    ///     <see langword="null"/> when the path does not exist.
    /// </returns>
    private static FileSystemInfo? Probe(string path)
    {
        // Probe for a directory first: on every supported platform a junction and a directory
        // symbolic link both report as directories, and those are the escape vectors that
        // matter most for containment.
        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path);
        }

        // Otherwise the component may be a file, which on POSIX platforms can equally well be
        // a symbolic link pointing outside the permitted location.
        if (File.Exists(path))
        {
            return new FileInfo(path);
        }

        return null;
    }
}
