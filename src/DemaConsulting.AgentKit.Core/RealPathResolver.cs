namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Resolves a path to an absolute, normalized location.
/// </summary>
/// <remarks>
///     <para>
///     Every path-containment decision in AgentKit compares a permitted location with the
///     location a request names. Those two can only be compared when they are spelled the same
///     way, so this type exists to put any path a caller supplies into one form: absolute, with
///     <c>.</c> and <c>..</c> segments collapsed. Containment is then a comparison between two
///     values of that form.
///     </para>
///     <para>
///     <b>Two resolutions, for two kinds of decision.</b> <see cref="Resolve"/> is lexical: it
///     neither follows nor detects a symbolic link, a directory junction or any other reparse
///     point, so a path that leaves a granted location through such a link is judged on the
///     location it is spelled as. That is the decision most tools make; see the note in the
///     README. <see cref="ResolveLinks"/> additionally follows every link on the path, so a
///     decision that must not be steered through a link can judge the location the path
///     actually reaches. <see cref="PathPolicy"/> offers both decisions.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
public static class RealPathResolver
{
    /// <summary>
    ///     The largest number of links <see cref="ResolveLinks"/> follows before it reports a
    ///     cycle.
    /// </summary>
    /// <remarks>
    ///     Forty is the limit Linux applies when it resolves a path itself. A longer chain is far
    ///     beyond any layout an application author would build and is taken to be a cycle.
    /// </remarks>
    private const int MaxLinkHops = 40;

    /// <summary>
    ///     The prefix Windows uses for a local path written in its device form, which a link target
    ///     can carry.
    /// </summary>
    private const string DevicePrefix = @"\\?\";

    /// <summary>
    ///     The prefix Windows uses for a network path written in its device form.
    /// </summary>
    private const string DeviceUncPrefix = @"\\?\UNC\";

    /// <summary>
    ///     The prefix the Windows object manager uses for a local path, which a junction stores
    ///     its target under. It has the same length as <see cref="DevicePrefix"/>.
    /// </summary>
    private const string NtObjectPrefix = @"\??\";

    /// <summary>
    ///     Resolves a path to its absolute, normalized location.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A relative path is made absolute against the current working directory, and <c>.</c>
    ///     and <c>..</c> segments are collapsed, which is what makes a containment comparison
    ///     between two paths meaningful and what denies a <c>..</c> escape. The path need not
    ///     exist: a write decision has to be made before the file is created.
    ///     </para>
    ///     <para>
    ///     Resolution is lexical. It does not consult the file system, and it neither follows
    ///     nor detects a symbolic link, a directory junction or any other reparse point; use
    ///     <see cref="ResolveLinks"/> for the location a path reaches through its links.
    ///     </para>
    /// </remarks>
    /// <param name="path">
    ///     The path to resolve. May be absolute or relative to the current working directory,
    ///     and need not exist. Must be non-null and non-empty.
    /// </param>
    /// <returns>The absolute, normalized location of <paramref name="path"/>.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="path"/> is an empty string or is not a valid path.
    /// </exception>
    /// <exception cref="IOException">
    ///     Thrown when the platform cannot express the normalized result, for example because it
    ///     is longer than the platform permits. Callers that must not fail on a caller-supplied
    ///     path should catch this and treat it as a denial, which is the fail-safe reading.
    /// </exception>
    public static string Resolve(string path)
    {
        // Reject a missing path outright: this is a programming error in the caller, not a
        // policy decision, so it is surfaced as an exception rather than a resolved value.
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Make the path absolute and collapse relative segments, which is the whole of what
        // this unit promises.
        return Path.GetFullPath(path);
    }

    /// <summary>
    ///     Resolves a path to the location it reaches once every symbolic link and directory
    ///     junction on it has been followed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Why it exists.</b> A lexical containment decision can be steered: a link spelled
    ///     inside a granted location may lead anywhere. A decision that has to judge the location a
    ///     read or a write will actually reach resolves the path with this method first and judges
    ///     the result; see <see cref="PathPolicy.TryResolveReadFollowingLinks"/>.
    ///     </para>
    ///     <para>
    ///     <b>How the path is walked.</b> The path is first made absolute and normalized as
    ///     <see cref="Resolve"/> does. It is then walked one component at a time from the volume
    ///     root. A component that is a link is replaced by its target — a relative target is
    ///     resolved against the folder holding the link — and the walk starts again over the
    ///     target followed by the components not yet walked, so a link in the target's own
    ///     ancestry is followed too. A dangling link is followed like any other.
    ///     </para>
    ///     <para>
    ///     <b>A path that does not exist is still resolved.</b> A component that does not exist is
    ///     not a link, so the components from there on are kept as spelled. A file that is about
    ///     to be created beneath an existing folder therefore resolves to that folder's real
    ///     location followed by the file's name, which is what a write decision needs.
    ///     </para>
    ///     <para>
    ///     <b>What it does not do.</b> The case of each name is kept as stored, and a Windows short
    ///     (8.3) name is not expanded. A hard link cannot be told apart from an ordinary file. A
    ///     reparse point that reports no link target, such as a cloud-file placeholder, is treated
    ///     as an ordinary entry. On macOS, <c>/var</c>, <c>/tmp</c> and <c>/etc</c> are symbolic
    ///     links and are followed; firm links need nothing, because their spelled path is already
    ///     their real path.
    ///     </para>
    ///     <para>
    ///     <b>The answer describes the file system at the moment of the call.</b> The file system
    ///     is consulted, so the result can change as soon as anything on the path is replaced. A
    ///     decision based on it is a pre-flight check, not a guarantee about a later open; see
    ///     <see cref="PathPolicy.TryResolveReadFollowingLinks"/>.
    ///     </para>
    ///     <para>
    ///     Reads link metadata only; it never opens, creates or modifies a file. Safe for
    ///     concurrent use.
    ///     </para>
    /// </remarks>
    /// <param name="path">
    ///     The path to resolve. May be absolute or relative to the current working directory,
    ///     and need not exist. Must be non-null and non-empty.
    /// </param>
    /// <returns>
    ///     The absolute, normalized location <paramref name="path"/> reaches with every link
    ///     followed. It ends in a directory separator only when it is a volume root.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="path"/> is an empty string or is not a valid path, or when
    ///     a link's target is not a valid path.
    /// </exception>
    /// <exception cref="IOException">
    ///     Thrown when more than forty links are met, which is taken to be a cycle, or when the
    ///     platform cannot express a location on the way. Callers that must not fail on a
    ///     caller-supplied path should catch this and treat it as a denial.
    /// </exception>
    public static string ResolveLinks(string path)
    {
        // Start from the lexical location, so the walk only ever sees absolute, normalized text.
        var current = Resolve(path);

        // Each pass walks the current location from its root. Meeting a link replaces the
        // location with the link's target plus the components not yet walked, and starts over.
        // The pass after the last permitted link may only confirm that no link remains.
        for (var hops = 0; hops <= MaxLinkHops; hops++)
        {
            var linked = FollowFirstLink(current);
            if (linked is null)
            {
                // No link remains. A trailing separator is dropped, except on a volume root, so
                // two spellings of one folder compare equal.
                return Path.TrimEndingDirectorySeparator(current);
            }

            current = linked;
        }

        // A chain longer than any real layout is a cycle; refusing it is the only answer.
        throw new IOException(
            $"More than {MaxLinkHops} links were met while resolving the path; the links form a cycle.");
    }

    /// <summary>
    ///     Finds the first link on an absolute path and returns the path with that link replaced
    ///     by its target.
    /// </summary>
    /// <remarks>
    ///     Components are inspected from the volume root downwards, so the first link found is the
    ///     outermost one. The root itself is never inspected, because a volume root is not a link.
    /// </remarks>
    /// <param name="current">The absolute, normalized path to inspect.</param>
    /// <returns>
    ///     The path with its first link replaced by the link's target, normalized; or
    ///     <see langword="null"/> when no component of the path is a link.
    /// </returns>
    private static string? FollowFirstLink(string current)
    {
        var root = Path.GetPathRoot(current) ?? string.Empty;
        var components = current[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var prefix = root;
        for (var index = 0; index < components.Length; index++)
        {
            var next = Path.Join(prefix, components[index]);

            // A null target means an ordinary entry, a missing entry, or one that could not be
            // inspected; in each case the walk carries on with the component as spelled.
            var target = new FileInfo(next).LinkTarget;
            if (target is not null)
            {
                // A relative target is relative to the folder holding the link, which is the
                // already-resolved prefix; the rest of the path follows the target unchanged.
                var resolvedTarget = Path.GetFullPath(WithoutDevicePrefix(target), prefix);
                var rest = string.Join(Path.DirectorySeparatorChar, components[(index + 1)..]);
                return Path.GetFullPath(rest.Length == 0 ? resolvedTarget : Path.Join(resolvedTarget, rest));
            }

            prefix = next;
        }

        return null;
    }

    /// <summary>
    ///     Rewrites a Windows device-form link target (<c>\\?\C:\...</c>, <c>\??\C:\...</c> or
    ///     <c>\\?\UNC\server\share\...</c>) into the ordinary form the rest of the library uses.
    /// </summary>
    /// <remarks>
    ///     A target in device form names the same location as its ordinary form but would never
    ///     compare equal to a grant root spelled the ordinary way, so a link within a grant would
    ///     be refused. On every other platform the text is returned unchanged.
    /// </remarks>
    /// <param name="target">The link target as the platform reported it.</param>
    /// <returns>The target in ordinary form.</returns>
    private static string WithoutDevicePrefix(string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            return target;
        }

        if (target.StartsWith(DeviceUncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + target[DeviceUncPrefix.Length..];
        }

        return target.StartsWith(DevicePrefix, StringComparison.Ordinal)
               || target.StartsWith(NtObjectPrefix, StringComparison.Ordinal)
            ? target[DevicePrefix.Length..]
            : target;
    }
}
