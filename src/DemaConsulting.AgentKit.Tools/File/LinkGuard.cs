using DemaConsulting.AgentKit.Core;

namespace DemaConsulting.AgentKit.Tools.File;

/// <summary>
///     The shared link rule the destructive directory tools apply: what counts as a link, and
///     whether the path a request named reaches its target through one.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why the rule is shared rather than restated.</b> <see cref="FileDeleteDirectoryTool"/>
///     already refused any link its walk <em>discovered</em>, and the same question has to be
///     asked of the components a caller-named path is reached <em>through</em>. Two copies of
///     "what is a link" would be two rules that could drift apart, on a decision whose failure
///     mode is content outside every grant destroyed by a request that never named it. This
///     helper holds the single predicate and the single ancestry walk, and both directory tools
///     call it.
///     </para>
///     <para>
///     <b>Why path resolution alone cannot answer this.</b> <see cref="RealPathResolver"/>
///     resolves lexically and documents that it neither follows nor detects a link, so
///     <c>grant/link/victim</c> satisfies <see cref="PathPolicy.TryResolveWrite"/> whenever
///     <c>grant/link</c> is spelled inside the grant — however far outside the grant the link
///     actually leads. The discovered-entry guard does not catch it either, because the walk
///     starts past the link and every entry it then meets is an ordinary file. Only the path
///     traversed <em>to</em> the starting point can answer it, which is what
///     <see cref="FindLinkedAncestor"/> inspects.
///     </para>
///     <para>
///     <b>Why only the destructive directory tools use it.</b> This is a narrowing of what a
///     caller may spell, and it is applied where the blast radius of getting it wrong is a whole
///     tree: recursive deletion and a directory move. It is deliberately not applied
///     library-wide — every other tool still resolves lexically, and a single-entry read or
///     write through a link remains the documented, bounded behavior described in the README.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
internal static class LinkGuard
{
    /// <summary>
    ///     Whether two real paths are compared case-insensitively, following the host's own
    ///     rules.
    /// </summary>
    /// <remarks>
    ///     Two spellings differing only in case name the same directory on Windows and on macOS
    ///     and different directories elsewhere, so the boundary comparison follows the same rule
    ///     <see cref="PathRule"/> applies to containment. A stricter or looser comparison here
    ///     would make the guard disagree with the grant it is enforcing.
    /// </remarks>
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    ///     Determines whether a file-system entry is a link rather than ordinary content.
    /// </summary>
    /// <remarks>
    ///     A non-null link target is what identifies an entry as a reparse point. It is non-null
    ///     for a POSIX symbolic link and for a Windows directory junction alike — verified, not
    ///     assumed — which matters because a junction needs no privilege to create and is
    ///     therefore the form an escape most easily takes on Windows.
    /// </remarks>
    /// <param name="info">The entry to classify. Must be non-null.</param>
    /// <returns>
    ///     <see langword="true"/> when the entry is a link; otherwise <see langword="false"/>.
    /// </returns>
    internal static bool IsLink(FileSystemInfo info)
    {
        return info.LinkTarget is not null;
    }

    /// <summary>
    ///     Finds the first link on the path traversed from a policy's grant boundary down to a
    ///     resolved target.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The walk runs from the target upwards and stops at the grant root.</b> The grant
    ///     root itself is never classified: an application author who roots a grant at a link, or
    ///     beneath one, has made that choice deliberately and refusing it would make the whole
    ///     grant unusable. A link <em>inside</em> the grant has been vetted by nobody, so every
    ///     component strictly between the grant root and the target is classified.
    ///     </para>
    ///     <para>
    ///     <b>The boundary is the deepest read-write grant containing the target.</b> A policy
    ///     may grant a location and, separately, a location beneath it that is reached through a
    ///     link; the deeper grant is the author's explicit statement about that location, so it
    ///     is the boundary and the link above it is not reconsidered. Taking the shallowest grant
    ///     instead would make the deeper grant impossible to use.
    ///     </para>
    ///     <para>
    ///     <b>An unrestricted write grant has no boundary, so nothing is classified.</b> Under a
    ///     grant that permits writing anywhere there is no outside to escape to, and a refusal
    ///     would be pure noise — on POSIX hosts it would also refuse ordinary temporary
    ///     locations, which are routinely reached through a link.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy whose write grants bound the walk. Must be non-null.</param>
    /// <param name="realPath">
    ///     The resolved, absolute location the request named. Must be non-null and non-empty.
    /// </param>
    /// <returns>
    ///     The resolved location of the first link found between the grant boundary and
    ///     <paramref name="realPath"/>, or <see langword="null"/> when the path traverses none.
    /// </returns>
    internal static string? FindLinkedAncestor(PathPolicy policy, string realPath)
    {
        // Establish the boundary first. With no rooted read-write grant containing the target —
        // an unrestricted grant, or none at all — there is no grant interior to protect and
        // nothing to classify.
        var boundary = DeepestWriteRootContaining(policy, realPath);
        if (boundary is null)
        {
            return null;
        }

        // A request naming the grant root itself traverses nothing inside the grant, and the
        // root's own ancestry is the author's choice rather than the agent's.
        if (string.Equals(realPath, boundary, PathComparison))
        {
            return null;
        }

        // Classify every component the path was reached through, stopping at the grant root so
        // that the root and everything above it are left to the author who spelled them.
        for (var ancestor = Path.GetDirectoryName(realPath);
             !string.IsNullOrEmpty(ancestor);
             ancestor = Path.GetDirectoryName(ancestor))
        {
            if (string.Equals(ancestor, boundary, PathComparison))
            {
                return null;
            }

            if (IsLink(new DirectoryInfo(ancestor)))
            {
                return ancestor;
            }
        }

        // The walk reached the volume root without meeting the boundary, which a contained path
        // cannot do; denying nothing here leaves the decision to the containment test that
        // already permitted the path.
        return null;
    }

    /// <summary>
    ///     Names a linked ancestor in the dialect the caller used, without disclosing a host
    ///     path.
    /// </summary>
    /// <remarks>
    ///     A refusal has to be actionable — the model must be able to see which component of its
    ///     own request is the offending one — and must not leak a location the policy never
    ///     showed it. The ancestor is a prefix of what the caller spelled, so the description is
    ///     built by removing as many trailing components from the caller's own text as separate
    ///     the ancestor from the target. Every character of the result therefore came from the
    ///     request, in the separator dialect the request used. A spelling that cannot be trimmed
    ///     that way — one carrying relative segments that collapse — falls back to the link's own
    ///     name, which is a single segment inside the grant and discloses no location.
    /// </remarks>
    /// <param name="requested">The path exactly as the model supplied it. Must be non-null.</param>
    /// <param name="realPath">The resolved location the request named. Must be non-null.</param>
    /// <param name="realAncestor">The resolved location of the linked ancestor. Must be non-null.</param>
    /// <returns>The linked ancestor named in the caller's dialect.</returns>
    internal static string DescribeAncestor(string requested, string realPath, string realAncestor)
    {
        // How many components separate the link from the target is how many components of the
        // caller's spelling name locations beneath the link.
        var depth = Path.GetRelativePath(realAncestor, realPath)
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Length;

        var trimmed = requested.TrimEnd('/', '\\');

        for (var removed = 0; removed < depth; removed++)
        {
            var separator = trimmed.LastIndexOfAny(['/', '\\']);
            if (separator <= 0)
            {
                // The caller's spelling ran out before the link was reached, so it cannot name
                // the link; the link's own segment name is the truthful, non-disclosing answer.
                return Path.GetFileName(realAncestor);
            }

            trimmed = trimmed[..separator];
        }

        return trimmed;
    }

    /// <summary>
    ///     Finds the deepest rooted read-write grant that contains a resolved path.
    /// </summary>
    /// <remarks>
    ///     Only read-write grants are consulted, because this guard serves operations governed by
    ///     the write decision and a read-only grant never permits them. An unrestricted grant
    ///     carries no root and therefore no boundary, so it is skipped: it is reported as the
    ///     absence of a boundary, which is what suppresses the walk entirely.
    /// </remarks>
    /// <param name="policy">The access policy to consult.</param>
    /// <param name="realPath">The resolved, absolute location to locate.</param>
    /// <returns>
    ///     The resolved root of the deepest containing read-write grant, or
    ///     <see langword="null"/> when no rooted read-write grant contains the path.
    /// </returns>
    private static string? DeepestWriteRootContaining(PathPolicy policy, string realPath)
    {
        // An unrestricted read-write grant permits writing anywhere, so there is no outside for
        // a link to escape to and no boundary this guard could enforce.
        if (policy.Grants.Any(grant => grant.Access == AccessLevel.ReadWrite && grant.Root is null))
        {
            return null;
        }

        string? deepest = null;

        foreach (var grant in policy.Grants)
        {
            if (grant.Access != AccessLevel.ReadWrite || grant.Root is null)
            {
                continue;
            }

            if (!Contains(grant.Root, realPath))
            {
                continue;
            }

            // The deepest containing grant wins, because a grant rooted beneath another is the
            // author's explicit statement about that deeper location.
            if (deepest is null || grant.Root.Length > deepest.Length)
            {
                deepest = grant.Root;
            }
        }

        return deepest;
    }

    /// <summary>
    ///     Determines whether a resolved path is a grant root or lies beneath it.
    /// </summary>
    /// <remarks>
    ///     The separator is required for the "beneath" case, so a sibling that merely shares the
    ///     root's name prefix is not treated as contained — the same rule
    ///     <see cref="PathRule.Allows"/> applies.
    /// </remarks>
    /// <param name="root">The resolved grant root.</param>
    /// <param name="realPath">The resolved, absolute location to test.</param>
    /// <returns>
    ///     <see langword="true"/> when the path is the root or lies beneath it; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static bool Contains(string root, string realPath)
    {
        if (string.Equals(root, realPath, PathComparison))
        {
            return true;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return realPath.StartsWith(prefix, PathComparison);
    }
}
