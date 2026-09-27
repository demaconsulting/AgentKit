using DemaConsulting.AgentKit.Core;

namespace DemaConsulting.AgentKit.Tools.File;

/// <summary>
///     The shared rule the destructive directory tools apply to what an operation actually
///     touches: the single walk over everything beneath a named directory, and the single
///     question asked of every entry that walk finds.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why this exists.</b> A path names one directory, but removing it or moving it reaches
///     every entry beneath it. Judging the named path alone therefore answers a different
///     question from the one the operation asks: a grant such as
///     <c>ReadWrite(root, ["*.key"])</c> permits <c>root</c> while
///     <see cref="PathRule.Allows"/> refuses <c>root/secret.key</c>, so a tool that consulted
///     the policy only about <c>root</c> would destroy, or relocate, content the operator's
///     policy names as off-limits. Both destructive directory tools therefore consult the
///     policy about every entry the operation would touch, and they do it through this one
///     walk and this one predicate so the two cannot drift apart.
///     </para>
///     <para>
///     <b>The walk never follows a link.</b> What a link leads to is not part of the tree: a
///     removal takes the link entry alone and a move relocates the link entry alone, so
///     descending into one would ask the policy about content the operation never reaches. A
///     link is reported as the entry it is — <see cref="LinkGuard.IsLink"/> classifies it — and
///     the caller decides what that means, because the two tools answer it differently: the
///     removal refuses the whole request, while a move relocates the link unfollowed.
///     </para>
///     <para>
///     <b>The walk is iterative, lazy, and bounded by the tree's depth rather than its size.</b>
///     A recursive walk spends a stack frame per directory level, and a tree deep enough to
///     exhaust the thread's stack raises <c>StackOverflowException</c>, which cannot be caught
///     and takes the host process with it. An explicit stack moves depth onto the heap. The
///     stack holds one <em>enumerator</em> per level rather than one path per directory still to
///     visit, and each directory's entries are streamed rather than materialized, so what the
///     walk retains is proportional to how deep the tree is and never to how wide or how large
///     it is. That is what lets the removal tool count an oversized tree exactly while holding
///     almost none of it; see <see cref="FileDeleteDirectoryTool"/>.
///     </para>
///     <para>
///     <b>The walk is pre-flight, and promises only that.</b> It answers for the tree as it
///     stands when the request is judged. A process able to write inside a location the operator
///     already granted may change the tree afterwards, and re-walking would only move that
///     window rather than close it; see <see cref="LinkGuard"/> for why a path-based API cannot
///     close it at all.
///     </para>
///     <para>
///     Enumeration touches an arbitrary tree, so it can fail for ordinary file-system reasons —
///     a directory the process may not enumerate, a child that vanishes between one step and the
///     next. Those failures surface from the enumerator as exceptions and are classified by the
///     calling tool, which turns them into the refusal that tool composes; nothing here is
///     swallowed, because a walk that silently skipped what it could not read would under-report
///     the very entries the policy question exists to ask about.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
internal static class SubtreeGuard
{
    /// <summary>
    ///     Walks a directory and everything beneath it, yielding every entry the walk finds,
    ///     without ever following a link.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The named directory is yielded first, then its files in the order the host reported
    ///     them, then each child directory in the same order, depth first. That order is what
    ///     decides which entry a tree holding several offending ones is refused for, so it is
    ///     part of the contract rather than an accident of the implementation.
    ///     </para>
    ///     <para>
    ///     Entries arrive as <see cref="FileSystemInfo"/> rather than as paths because every
    ///     caller has to classify them — a <see cref="DirectoryInfo"/> is a directory, a
    ///     <see cref="FileInfo"/> is a file, and <see cref="FileSystemInfo.LinkTarget"/> answers
    ///     whether either is a link — and the host's own enumeration has already paid for that
    ///     metadata. Handing back paths would make each caller ask the file system again.
    ///     </para>
    ///     <para>
    ///     Enumeration is lazy, so the sequence must be consumed before the tree is mutated: the
    ///     enumerators hold the directories they are reading open. Both callers complete or
    ///     abandon the walk before touching anything, which is the same two-phase discipline the
    ///     removal tool already states.
    ///     </para>
    ///     <para>
    ///     A link yielded from the walk ends that branch: the walk does not descend into it. What
    ///     it leads to is not part of this tree, and neither removal nor move reaches it.
    ///     </para>
    /// </remarks>
    /// <param name="realRoot">
    ///     The resolved, absolute location of the directory to walk. Must be non-null and
    ///     non-empty.
    /// </param>
    /// <returns>
    ///     The named directory followed by every entry beneath it, depth first, links included
    ///     as entries but never descended into.
    /// </returns>
    internal static IEnumerable<FileSystemInfo> Descend(string realRoot)
    {
        // One enumerator per level of depth, rather than one path per directory still to visit:
        // this is what keeps the walk's cost proportional to depth rather than to breadth.
        var levels = new Stack<IEnumerator<DirectoryInfo>>();

        try
        {
            DirectoryInfo? directory = new(realRoot);

            while (directory is not null)
            {
                yield return directory;

                // A link is an entry, never a door. Descending into one would reach content the
                // operation does not touch: a removal takes the link alone, and so does a move.
                if (!LinkGuard.IsLink(directory))
                {
                    // Files first, streamed one at a time, so a directory holding a million of
                    // them costs one entry rather than a million.
                    foreach (var file in directory.EnumerateFiles())
                    {
                        yield return file;
                    }

                    levels.Push(directory.EnumerateDirectories().GetEnumerator());
                }

                directory = NextDirectory(levels);
            }
        }
        finally
        {
            // Every enumerator holds an open directory, including on an abandoned walk — a
            // caller that stops at the first offending entry is the ordinary case here.
            while (levels.Count > 0)
            {
                levels.Pop().Dispose();
            }
        }
    }

    /// <summary>
    ///     Determines whether the access policy permits this agent to write a resolved location.
    /// </summary>
    /// <remarks>
    ///     This is the same decision the tool already took about the path the model named, asked
    ///     again about an entry the model did not name. Asking it through
    ///     <see cref="PathPolicy.TryResolveWrite"/> rather than by inspecting grants directly is
    ///     deliberate: the library has one write decision, and an entry inside a tree must be
    ///     judged by exactly the rule that judges a path a caller spells, or a tool would carry
    ///     a second, quieter notion of what the operator permitted.
    /// </remarks>
    /// <param name="policy">The access policy to consult. Must be non-null.</param>
    /// <param name="realPath">
    ///     The resolved, absolute location to test. Need not exist — a destination entry does
    ///     not.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when a read-write grant permits the location; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    internal static bool PermitsWrite(PathPolicy policy, string realPath)
    {
        return policy.TryResolveWrite(realPath, out _, out _);
    }

    /// <summary>
    ///     Names an entry beneath a directory in the dialect the caller used, without disclosing
    ///     a host path.
    /// </summary>
    /// <remarks>
    ///     A refusal must be actionable — the model has to be able to name the offending entry in
    ///     a following request — and must not leak a location the policy never showed it.
    ///     Composing the path the model supplied with the relative sub-path the walk reached
    ///     satisfies both: every component of the result was either supplied by the model or lies
    ///     inside the tree it named. The separator the model used is mirrored, so the name can be
    ///     handed straight back.
    /// </remarks>
    /// <param name="requested">The path exactly as the model supplied it. Must be non-null.</param>
    /// <param name="realRoot">The resolved location the request named. Must be non-null.</param>
    /// <param name="realEntry">The resolved location of the entry. Must be non-null.</param>
    /// <returns>The entry named in the caller's dialect.</returns>
    internal static string DescribeEntry(string requested, string realRoot, string realEntry)
    {
        var relative = Path.GetRelativePath(realRoot, realEntry);

        // The named directory itself is already spelled by the request; appending the relative
        // path's "." for it would produce a name the model cannot hand back.
        if (relative == ".")
        {
            return requested;
        }

        // Mirror the separator the model used, so the name reads as the model's own.
        var separator = requested.Contains('\\') && !requested.Contains('/') ? '\\' : '/';

        return requested.TrimEnd('/', '\\')
            + separator
            + relative
                .Replace(Path.DirectorySeparatorChar, separator)
                .Replace(Path.AltDirectorySeparatorChar, separator);
    }

    /// <summary>
    ///     Advances to the next directory the walk should visit, releasing every level that has
    ///     nothing left in it.
    /// </summary>
    /// <remarks>
    ///     The deepest unfinished level wins, which is what makes the traversal depth first. A
    ///     level with no children left is disposed as it is popped rather than at the end of the
    ///     walk, so the number of directories held open never exceeds the current depth.
    /// </remarks>
    /// <param name="levels">The enumerators for the levels still being walked.</param>
    /// <returns>
    ///     The next directory to visit, or <see langword="null"/> when the walk is complete.
    /// </returns>
    private static DirectoryInfo? NextDirectory(Stack<IEnumerator<DirectoryInfo>> levels)
    {
        while (levels.Count > 0)
        {
            var level = levels.Peek();

            if (level.MoveNext())
            {
                return level.Current;
            }

            levels.Pop().Dispose();
        }

        return null;
    }
}
