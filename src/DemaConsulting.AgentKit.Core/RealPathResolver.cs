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
///     <b>Links are not resolved.</b> Symbolic links, directory junctions and other reparse
///     points are not followed and are not detected. A path that leaves a granted location
///     through such a link is judged on the location it is spelled as, so it is not a protection
///     boundary; see the note in the README.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
public static class RealPathResolver
{
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
    ///     nor detects a symbolic link, a directory junction or any other reparse point.
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
}
