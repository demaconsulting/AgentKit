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
///     <b>What the guard actually guarantees, and what it cannot.</b> This is a <em>pre-flight</em>
///     check over paths: the location a caller <em>names</em> is classified before anything is
///     touched, and a walk that later <em>discovers</em> a link refuses rather than following it.
///     It is not a race-resistant control. A second process that can modify the tree — inside a
///     location the operator already granted — may replace a component between the classification
///     and the operation, and a second path check would only move the window rather than close it.
///     Portable .NET exposes no handle-relative, no-follow directory removal or move, so that
///     window cannot be closed at this layer, and the guarantee is stated as what it is rather
///     than implied to be more. An adversary already able to write inside a granted location is
///     outside what a path-based API can defend against.
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
    ///     <b>The boundary is the deepest read-write grant that contains and permits the
    ///     target.</b> A policy may grant a location and, separately, a location beneath it that
    ///     is reached through a link; the deeper grant is the author's explicit statement about
    ///     that location, so it is the boundary and the link above it is not reconsidered. Taking
    ///     the shallowest grant instead would make the deeper grant impossible to use. A grant
    ///     that merely encloses the target while its denied patterns reject it is <em>not</em> a
    ///     statement about that location and is never the boundary: the request was authorized by
    ///     some other, shallower grant, and it is that grant's interior this walk has to cover.
    ///     </para>
    ///     <para>
    ///     <b>An unrestricted write grant permitting the target has no boundary, so nothing is
    ///     classified.</b> Under a grant that permits writing anywhere there is no outside to
    ///     escape to, and a refusal would be pure noise — on POSIX hosts it would also refuse
    ///     ordinary temporary locations, which are routinely reached through a link.
    ///     </para>
    ///     <para>
    ///     <b>This is a pre-flight classification, not a race-resistant one.</b> It answers for
    ///     the path as it stands when the request is judged. A process able to write inside a
    ///     granted location may replace a component afterwards; see the type remarks for why that
    ///     window cannot be closed by a path-based API.
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
        // Establish the boundary first. With no rooted read-write grant both containing and
        // permitting the target — a permitting unrestricted grant, or none at all — there is no
        // grant interior to protect and nothing to classify.
        var boundary = DeepestPermittingWriteRoot(policy, realPath);
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
    ///     Finds the deepest rooted read-write grant that both contains and permits a resolved
    ///     path.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Only read-write grants are consulted, because this guard serves operations governed by
    ///     the write decision and a read-only grant never permits them.
    ///     </para>
    ///     <para>
    ///     <b>A grant must permit the path, not merely enclose it.</b> A grant that lexically
    ///     encloses the target but whose denied patterns reject it authorized nothing, so it is
    ///     no statement about that location and cannot be the boundary. Treating it as one moved
    ///     the boundary <em>below</em> a link and left that link unclassified, which reopened the
    ///     very escape the ancestor walk exists to close: the request was actually authorized by
    ///     a shallower grant above the link, and it is that grant's interior the walk has to
    ///     cover. Both conditions are therefore asked. They are separate questions —
    ///     <see cref="Contains"/> asks whether the grant is an <em>ancestor</em>, which is what
    ///     makes it a boundary at all, and <see cref="PathRule.Allows"/> asks whether the author
    ///     granted this location — and the walk must not depend on one implying the other.
    ///     </para>
    ///     <para>
    ///     An unrestricted grant carries no root and therefore no boundary, so a permitting one
    ///     is reported as the absence of a boundary, which is what suppresses the walk entirely.
    ///     It too must permit the path: an unrestricted grant whose denied patterns reject the
    ///     target permits no writing there, so the "there is no outside to escape to" reasoning
    ///     that justifies suppressing the walk does not apply to it.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy to consult.</param>
    /// <param name="realPath">The resolved, absolute location to locate.</param>
    /// <returns>
    ///     The resolved root of the deepest containing and permitting read-write grant, or
    ///     <see langword="null"/> when no rooted read-write grant both contains and permits the
    ///     path.
    /// </returns>
    private static string? DeepestPermittingWriteRoot(PathPolicy policy, string realPath)
    {
        // An unrestricted read-write grant that permits this path has no root, and therefore no
        // interior a link could lead out of: there is no outside for this guard to defend and a
        // refusal would be pure noise. A grant whose denied patterns reject the path authorized
        // nothing here, so it does not suppress the walk.
        if (policy.Grants.Any(grant =>
                grant.Access == AccessLevel.ReadWrite &&
                grant.Root is null &&
                grant.Allows(realPath)))
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

            // Being an ancestor is what makes a grant a candidate boundary; permitting the path
            // is what makes it the author's statement about this location. A grant failing
            // either one is not the boundary, and skipping the second test would let a grant
            // whose denied patterns reject the target hide a link above it.
            if (!Contains(grant.Root, realPath) || !grant.Allows(realPath))
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
