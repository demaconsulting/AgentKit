using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.File;

/// <summary>
///     The <c>file_delete_directory</c> tool: removes a directory, and everything beneath it, in a
///     location the access policy permits the agent to write.
/// </summary>
/// <remarks>
///     <para>
///     <b>Removal is judged by the write decision.</b> Taking away a directory is a write in the
///     strongest sense, so a tree an agent may read but not write cannot be removed. Write access
///     is never inferred from read access.
///     </para>
///     <para>
///     <b>The removal happens in two phases, and the first phase changes nothing.</b> The tool
///     first walks the tree and builds the complete list of entries the removal would take; only
///     if that whole list is acceptable does it remove anything. Deciding before acting is what
///     makes both guards below enforceable, and what it guarantees is precise: no removal ever
///     <em>begins</em> until the whole plan is approved. It does not guarantee that a removal,
///     once begun, completes — the file system can refuse an entry part way through, and a file
///     another process holds open is the ordinary case. What the design adds there is that the
///     outcome is <em>reported</em>: a failure mid-removal names how many entries had already
///     gone, so a partly removed tree is one the model knows it is looking at rather than one it
///     cannot see.
///     </para>
///     <para>
///     <b>The walk is iterative, over an explicit stack, and shared.</b> A recursive walk spends
///     one stack frame per directory level, and a tree deep enough to exhaust the thread's stack
///     raises <c>StackOverflowException</c> — which cannot be caught and takes the host process
///     with it, before the entry ceiling below is ever consulted. An explicit stack moves the
///     depth onto the heap, so depth costs the same bounded resource breadth already does and the
///     ceiling is reached rather than overtaken by a failure the tool cannot report. The walk
///     itself lives in <see cref="SubtreeGuard"/> rather than here, because
///     <see cref="FileMoveDirectoryTool"/> has to ask the same question of the same entries and
///     two walks would be two answers.
///     </para>
///     <para>
///     <b>The walk never follows a link, and a link found inside the tree refuses the whole
///     request.</b> A link — a symbolic link or a directory junction — can point anywhere,
///     including outside everything the policy granted. Following one would remove content the
///     request never named; removing the link entry itself would destroy a connection the request
///     never mentioned. Neither is something a request to delete a directory asked for, so the
///     tool refuses, names the offending entry as the model spelled the request, and removes
///     nothing. The refusal names the entry but never the link's target: the target is the host
///     location the guard exists to protect, and disclosing it would leak exactly what the refusal
///     is defending.
///     </para>
///     <para>
///     <b>A directory that is itself a link is removed as the link alone.</b> Without that rule
///     the refusal above would be a dead end: a workspace containing a link would be permanently
///     undeletable, because <see cref="FileDeleteTool"/> refuses a directory and a link is a
///     directory. So a link the request names directly is removed, unfollowed, and the result says
///     plainly that what it pointed at was not touched. That gives a model met with the refusal a
///     concrete way forward — name the link, then ask for the parent again. The rule holds even
///     when the link's target has since been removed: such a link is classified from its own
///     metadata rather than from what it leads to, because a dangling link is reported as a
///     directory on Windows and as a file on the POSIX hosts, and a rule that asked what the
///     path leads to first would offer the way forward on one platform of three.
///     </para>
///     <para>
///     <b>Every entry the plan would take is judged by the policy, not only the directory the
///     request named.</b> A grant permits a location and may exclude names within it: under
///     <c>ReadWrite(root, ["*.key"])</c> the policy permits <c>root</c> and refuses
///     <c>root/secret.key</c>. A removal judged by the path it was <em>given</em> would then
///     destroy exactly the content the operator's policy names as off-limits, because the path
///     the tool validated is not what the operation touches. The planning walk therefore asks
///     the write decision about every entry it intends to remove — through the one predicate in
///     <see cref="SubtreeGuard"/>, so this tool and <see cref="FileMoveDirectoryTool"/> ask it
///     the same way — and a single refused entry refuses the whole request, named in the
///     model's own spelling. The permitted subset is deliberately <em>not</em> removed: a
///     partial deletion the request never asked for is the outcome the two phases exist to
///     avoid, and it would leave a workspace no one chose.
///     </para>
///     <para>
///     <b>A recursive framework delete is never issued.</b> Measured on Windows, deleting a tree
///     recursively throws when the tree contains a junction and leaves the tree <em>partly
///     deleted</em>, which is both unsafe and the one outcome this design exists to avoid. The
///     walk is therefore hand-rolled, and the removal takes files first and then directories
///     deepest-first, one entry at a time.
///     </para>
///     <para>
///     <b>The entry ceiling bounds a mistake, not an intention.</b> A tree holding more entries
///     than <see cref="ToolLimits.MaxDeleteEntries"/> is refused, naming the real count and the
///     configured limit. This is <em>not</em> protection against a determined agent: one that
///     means to destroy a tree can remove it a file at a time with <see cref="FileDeleteTool"/>
///     and this ceiling will not stop it. What it buys is that a mistake — a wrong path, a model
///     confusion, an off-by-one in a constructed path — is survivable and observable rather than
///     total in one call. The count is exact rather than approximate, because "more than a
///     thousand" tells a model nothing about whether one subdivision would suffice while a real
///     figure in the tens of thousands tells it to take a different approach. That exactness
///     costs a full metadata-only enumeration even of a tree that is about to be refused — a walk
///     the approved case was going to make anyway — but it does not cost the memory to hold that
///     tree. Two things bound what is held, and both are needed: the walk streams each directory
///     an entry at a time and keeps one enumerator per level of <em>depth</em> rather than one
///     path per directory still to visit, so neither a very wide directory nor a tree of very
///     many directories is ever materialized; and once the running count passes the ceiling this
///     tool stops retaining paths altogether and only keeps counting, since a plan past the
///     ceiling can never be approved. What a refused request holds is therefore bounded by how
///     deep the tree is, whatever its breadth, while the figure the refusal names stays exact.
///     </para>
///     <para>
///     <b>A path the model <em>names</em> that traverses a link is refused too.</b> Path
///     resolution is lexical — see <see cref="RealPathResolver"/> — so <c>grant/link/sub</c>
///     satisfies the write decision whenever <c>grant/link</c> is spelled inside the grant,
///     however far outside the grant the link actually leads, and the discovered-entry guard
///     above never fires because the walk starts past the link. This tool therefore applies its
///     own rule to the components a path is reached <em>through</em> as well as to the entries
///     the walk finds, using the one predicate in <see cref="LinkGuard"/>. It is a deliberate
///     narrowing of what the caller may spell, made here and for
///     <see cref="FileMoveDirectoryTool"/> because the blast radius of getting it wrong is a
///     whole tree rather than a single entry; the rest of the library still resolves lexically.
///     The grant root itself, and everything above it, is not classified: an author who roots a
///     grant at a link has made that choice, while a link inside the grant has been vetted by
///     nobody.
///     </para>
///     <para>
///     <b>Both link rules are pre-flight, and that is the whole of what they promise.</b> The path
///     the caller <em>names</em> is classified before anything is touched, and the planning walk
///     refuses every link it <em>discovers</em> before a single entry is removed. Neither is a
///     defense against a process racing the tool: one that can write inside a location the
///     operator already granted may replace a component between the plan and the removal, and a
///     second path check would only move that window rather than close it, because portable .NET
///     offers no handle-relative, no-follow directory removal. What bounds the race is that no
///     removal this tool issues follows a link — measured, <see cref="System.IO.File.Delete"/>
///     unlinks a symbolic link rather than its target and refuses a Windows junction outright,
///     and <c>Directory.Delete(recursive: false)</c> removes a junction while leaving what it
///     points at whole — so a component swapped after planning is removed as the link it is. An
///     adversary already writing inside a granted location is outside what a path-based API can
///     defend against, and this unit does not claim otherwise.
///     </para>
///     <para>
///     The delegate is declared to return <c>Task&lt;object&gt;</c> deliberately — see the remarks
///     on <see cref="GuardedToolFactory"/> — and every refusal is returned rather than thrown.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class FileDeleteDirectoryTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     The name spells out the directory rather than adding a flag to <c>file_delete</c>,
    ///     because the separation between removing one named file and removing a tree is the
    ///     safety property: it is what stops "delete this" ever meaning "delete this tree".
    /// </remarks>
    public const string ToolName = "file_delete_directory";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Deletes a directory and everything beneath it, in a location the agent is permitted to "
        + "write. Paths are relative to the workspace root. Never follows a link out of the "
        + "directory; a directory containing such a link is refused rather than partly removed, "
        + "and so is a path that is itself reached through one. "
        + "Refuses a directory holding any entry the agent is not permitted to write, naming "
        + "that entry, and removes nothing in that case. "
        + "Refuses a directory holding more entries than the configured limit, naming the count "
        + "and the limit. Returns a confirmation reporting how many entries were removed, or a "
        + "denial explaining why the request was refused; a removal that fails part way through "
        + "reports how many entries had already gone.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    private const string PathRequired =
        "A directory path is required. Supply the path of the directory to delete, relative to "
        + "the workspace root, for example 'build/output'.";

    /// <summary>
    ///     The refusal used when the request names a file rather than a directory.
    /// </summary>
    private const string PathIsFile =
        "The requested path is a file, not a directory. This tool deletes a directory and "
        + "everything beneath it; name a directory.";

    /// <summary>
    ///     The refusal used when the requested directory does not exist.
    /// </summary>
    private const string DirectoryNotFound = "The requested directory does not exist.";

    /// <summary>
    ///     The refusal used when the walk finds a link beneath the named directory.
    /// </summary>
    /// <remarks>
    ///     The placeholder receives the path the model supplied plus the relative sub-path the
    ///     walk reached — never the resolved host path, and never the link's target.
    /// </remarks>
    private const string LinkWithinTheTree =
        "The directory contains a link, at '{0}', that leads outside it. Following it would "
        + "remove content this request never named, and removing the link itself would destroy a "
        + "connection the request did not mention, so nothing was deleted. Name that link "
        + "directly to remove the link alone, then ask again.";

    /// <summary>
    ///     The refusal used when the walk finds an entry the policy does not permit writing.
    /// </summary>
    /// <remarks>
    ///     The placeholder receives the path the model supplied plus the relative sub-path the
    ///     walk reached — the same non-disclosing spelling <see cref="LinkWithinTheTree"/> uses.
    ///     The refusal names the entry rather than the rule that withheld it: the pattern is the
    ///     operator's configuration, and a model told which pattern matched learns the shape of
    ///     the policy rather than what it may do.
    /// </remarks>
    private const string DeniedEntryWithinTheTree =
        "The directory contains an entry, at '{0}', that this agent is not permitted to write, "
        + "so nothing was deleted. Removing the directory would take content the access policy "
        + "withholds, which this request never named. Name a directory that does not contain "
        + "it.";

    /// <summary>
    ///     The refusal used when the named path is reached through a link inside the grant.
    /// </summary>
    /// <remarks>
    ///     The placeholder receives the offending component as the model spelled it — never the
    ///     resolved host path, and never the link's target, which is the location outside the
    ///     grant that this guard exists to protect.
    /// </remarks>
    private const string PathLeadsThroughALink =
        "The path is reached through a link, at '{0}', that leads out of the permitted location, "
        + "so nothing was deleted. Removing a tree through a link would destroy content outside "
        + "everything the operator granted, which this request never named. Name a path that "
        + "does not pass through that link.";

    /// <summary>
    ///     The refusal used when the tree holds more entries than the configured ceiling.
    /// </summary>
    /// <remarks>
    ///     The placeholders receive the exact entry count and the host's configured ceiling, in
    ///     that order, so the model can judge whether subdividing the request would help.
    /// </remarks>
    private const string TooManyEntries =
        "The directory holds {0} entries and this tool removes at most {1} in one call, so "
        + "nothing was deleted. Delete part of it first, or have the application raise the limit.";

    /// <summary>
    ///     The refusal used when a permitted removal cannot be completed for a file-system reason
    ///     before anything has been removed.
    /// </summary>
    /// <remarks>
    ///     Used for the planning walk, which mutates nothing, and for the removal of a link the
    ///     request named directly, which is one entry and so either happens or does not. A
    ///     removal that fails part way through a tree reports the figure instead; see
    ///     <see cref="PartialDeleteFailed"/>.
    /// </remarks>
    private const string DeleteFailed = "The directory could not be deleted.";

    /// <summary>
    ///     The refusal used when a removal fails part way through the approved plan.
    /// </summary>
    /// <remarks>
    ///     The placeholders receive the number of entries already removed and the number the
    ///     approved plan covered, in that order. Every other refusal this tool composes says that
    ///     nothing was deleted, so this one has to say the opposite plainly: a model told only
    ///     that the removal failed would reasonably conclude the tree is intact, and act on a
    ///     tree that is no longer there.
    /// </remarks>
    private const string PartialDeleteFailed =
        "The directory could not be fully deleted: {0} of {1} entries were removed before the "
        + "removal failed. List the directory to see what remains.";

    /// <summary>
    ///     The confirmation used when the named path was itself a link.
    /// </summary>
    private const string LinkRemoved = "Removed the link. What it pointed at was not touched.";

    /// <summary>
    ///     The confirmation used when a whole tree was removed, reporting the entry count.
    /// </summary>
    private const string TreeRemoved =
        "Deleted the directory and everything beneath it: {0} entries removed.";

    /// <summary>
    ///     Creates the <c>file_delete_directory</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="FilePack"/>. The policy is captured by the
    ///     returned tool's delegate, so the tool cannot later be pointed at a different policy,
    ///     and the removal ceiling is read from that policy's limits on every call.
    /// </remarks>
    /// <param name="policy">The access policy governing every removal this tool performs.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy)
    {
        // A missing policy is a programming error in the composing application.
        ArgumentNullException.ThrowIfNull(policy);

        // Declared to return object on purpose; see the type remarks before changing this.
        // The path carries a default so an omitted argument becomes a refusal this tool composes,
        // rather than a framework error raised before the body is reached.
        var delete = (
                [Description(
                    "The path of the directory to delete, relative to the workspace root, "
                    + "for example 'build/output'.")]
                string? path = null) =>
            Delete(policy, path);

        return GuardedToolFactory.Create(delete, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Removes one directory tree, refusing rather than throwing whenever the request cannot
    ///     be honored.
    /// </summary>
    /// <remarks>
    ///     The write decision is taken before anything is learned about the file system, so a
    ///     refused path discloses nothing about what does or does not exist beyond the permitted
    ///     write location.
    /// </remarks>
    /// <param name="policy">The access policy governing the removal.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static object Delete(PathPolicy policy, string? path)
    {
        // A malformed request is refused rather than thrown; the model supplied it.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        // Removal is a write, so the write decision alone governs it. Read access never implies
        // permission to take something away.
        if (!policy.TryResolveWrite(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // The write decision is lexical, so it cannot see that a named component leads out of
        // the grant. Classifying the path the target was reached through is what closes that,
        // and it is done before anything else is learned about the tree.
        var linkedAncestor = LinkGuard.FindLinkedAncestor(policy, realPath);
        if (linkedAncestor is not null)
        {
            return ToolResult.Denied(
                DenialReason.PathNotPermitted,
                string.Format(
                    CultureInfo.InvariantCulture,
                    PathLeadsThroughALink,
                    LinkGuard.DescribeAncestor(path, realPath, linkedAncestor)));
        }

        // A link the request named directly is removed as the link alone, never followed, and it
        // is classified before the questions below, which all judge the path by what it leads to.
        // A link whose target has been removed is still an entry, and the two platform families
        // report it differently: a Windows junction still answers Directory.Exists, while a POSIX
        // symbolic link answers File.Exists instead, because Directory.Exists follows it. Judging
        // the target first would therefore make this escape hatch a Windows-only one.
        var namedLinkRemoval = TryDeleteNamedLink(realPath);
        if (namedLinkRemoval is not null)
        {
            return namedLinkRemoval;
        }

        // A file is never removed by the directory tool; keeping the single-file and whole-tree
        // capabilities apart is what bounds each one's blast radius.
        if (System.IO.File.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsFile);
        }

        // A missing directory is reported rather than treated as a silent success, so a mistyped
        // path is never mistaken for an accomplished removal.
        if (!Directory.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.TargetNotFound, DirectoryNotFound);
        }

        // Phase one: plan the whole removal, touching nothing. The walk refuses on the first
        // link it meets and on the first entry the policy withholds, rather than descending or
        // recording past either, and it stops retaining paths once the running count passes the
        // ceiling, since a plan past the ceiling can never be approved.
        //
        // The walk is guarded by exactly the classification the removal is. Enumeration is a file
        // system operation on an arbitrary tree: a sub-directory whose permissions forbid
        // enumeration, or a child that vanishes between one step and the next, is the plan and
        // execute race this design exists to reason about, not a defect. Leaving it to escape as
        // a thrown exception would break the family's rule that a refusal is a result.
        List<string> directories = [];
        List<string> files = [];
        PlanOutcome outcome;

        try
        {
            outcome = Plan(policy, realPath, directories, files);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            // Phase one mutates nothing, so the plain refusal is the whole truth here.
            return ToolResult.Denied(DenialReason.InvalidRequest, DeleteFailed);
        }

        if (outcome.LinkEntry is not null)
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                string.Format(
                    CultureInfo.InvariantCulture,
                    LinkWithinTheTree,
                    SubtreeGuard.DescribeEntry(path, realPath, outcome.LinkEntry)));
        }

        // An entry the policy withholds refuses the whole request. The permitted subset is not
        // removed: nothing this request did not ask for should be destroyed, and a partial
        // removal is what the two phases exist to avoid.
        if (outcome.DeniedEntry is not null)
        {
            return ToolResult.Denied(
                DenialReason.PathNotPermitted,
                string.Format(
                    CultureInfo.InvariantCulture,
                    DeniedEntryWithinTheTree,
                    SubtreeGuard.DescribeEntry(path, realPath, outcome.DeniedEntry)));
        }

        // The named directory counts as an entry, so an empty directory costs one and a ceiling
        // of zero forbids recursive removal entirely.
        if (outcome.EntryCount > policy.Limits.MaxDeleteEntries)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                string.Format(
                    CultureInfo.InvariantCulture,
                    TooManyEntries,
                    outcome.EntryCount,
                    policy.Limits.MaxDeleteEntries));
        }

        // Phase two: carry out exactly the plan phase one approved.
        return Execute(directories, files, outcome.EntryCount);
    }

    /// <summary>
    ///     Removes a link the request named directly, or reports that the path is not one this
    ///     tool removes as a link.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Why the classification comes before the existence questions.</b> A link is
    ///     recognized from the entry's own metadata, which costs nothing to read and says nothing
    ///     about what the link leads to. Asking <see cref="Directory.Exists(string)"/> first
    ///     would ask a question about the <em>target</em>, and a link whose target has been
    ///     removed answers it differently on each platform family: measured, a Windows junction
    ///     still reports a directory, while a POSIX symbolic link reports no directory and a file
    ///     instead, because <see cref="Directory.Exists(string)"/> follows it. Ordering the target
    ///     question first therefore left a dangling link removable on Windows and, on the POSIX
    ///     hosts, refused by this tool as though it were a file — so the escape hatch the type
    ///     remarks promise held on one platform of three.
    ///     </para>
    ///     <para>
    ///     <b>Why a file link is still left alone.</b> A link that still resolves to a file is a
    ///     file as far as a request to delete a <em>directory</em> is concerned, and
    ///     <see cref="FileDeleteTool"/> removes it — the link alone — perfectly well. Returning
    ///     it unhandled keeps the two capabilities apart, which is the property that stops this
    ///     tool being a second route to removing one named file.
    ///     </para>
    /// </remarks>
    /// <param name="realPath">The resolved path the request named.</param>
    /// <returns>
    ///     A confirmation or a refusal when the path was a link this tool removes; otherwise
    ///     <see langword="null"/>, meaning the caller should go on judging the path normally.
    /// </returns>
    private static object? TryDeleteNamedLink(string realPath)
    {
        // A link the host reports as a directory — every live directory link, and on Windows a
        // junction whose target has gone as well — is taken by the non-recursive directory
        // delete, which removes the link and leaves the target's contents intact.
        if (Directory.Exists(realPath))
        {
            return LinkGuard.IsLink(new DirectoryInfo(realPath))
                ? DeleteLink(realPath, asDirectory: true)
                : null;
        }

        // Nothing resolves through the path as a directory. A link here either still leads to a
        // file, which is the file tool's business, or leads nowhere at all — and a link leading
        // nowhere has no target left to disturb, so removing the entry is the whole of the act.
        return LinkGuard.IsLink(new FileInfo(realPath)) && !LeadsToSomething(realPath)
            ? DeleteLink(realPath, asDirectory: false)
            : null;
    }

    /// <summary>
    ///     Determines whether a link still leads to something that exists.
    /// </summary>
    /// <remarks>
    ///     The final target is resolved rather than the first hop, because a chain of links is
    ///     dangling only if the end of it is. A chain that cannot be resolved at all — a cycle,
    ///     or a component the process may not traverse — is reported as leading nowhere, since
    ///     there is no reachable entry for this tool to be deferring to.
    /// </remarks>
    /// <param name="realPath">The resolved path of the link.</param>
    /// <returns>
    ///     <see langword="true"/> when the link's final target exists; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static bool LeadsToSomething(string realPath)
    {
        try
        {
            return new FileInfo(realPath).ResolveLinkTarget(returnFinalTarget: true)?.Exists
                ?? false;
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return false;
        }
    }

    /// <summary>
    ///     Removes a link entry without following it.
    /// </summary>
    /// <remarks>
    ///     A non-recursive delete of a link removes the link and leaves the target's contents
    ///     intact — measured, not assumed — which is what makes naming a link directly a safe and
    ///     useful thing for a model to do. Which call performs the removal has to match what the
    ///     host reports the entry to be: also measured, a directory delete of a POSIX symbolic
    ///     link whose target has gone fails with <see cref="DirectoryNotFoundException"/>, and a
    ///     file delete of a Windows junction fails with
    ///     <see cref="UnauthorizedAccessException"/>.
    /// </remarks>
    /// <param name="realPath">The resolved path of the link.</param>
    /// <param name="asDirectory">
    ///     Whether the host reports the entry as a directory, and therefore whether the directory
    ///     delete or the file delete is the call that removes it.
    /// </param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static object DeleteLink(string realPath, bool asDirectory)
    {
        try
        {
            if (asDirectory)
            {
                Directory.Delete(realPath, recursive: false);
            }
            else
            {
                System.IO.File.Delete(realPath);
            }

            return ToolResult.Text(LinkRemoved);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, DeleteFailed);
        }
    }

    /// <summary>
    ///     Removes every entry in an approved plan, files first and directories deepest-first.
    /// </summary>
    /// <remarks>
    ///     Directories are removed in reverse collection order, and the collection is built
    ///     shallowest-first, so every directory is empty by the time it is removed. No recursive
    ///     delete is ever issued, so no call can walk into something the plan did not approve.
    ///     <para>
    ///     A failure here cannot be undone — the file system is the thing that failed — so the
    ///     refusal reports how far the removal got. Saying only that the removal failed would
    ///     read, against every other refusal this tool composes, as though the tree were still
    ///     whole.
    ///     </para>
    /// </remarks>
    /// <param name="directories">The planned directories, shallowest first.</param>
    /// <param name="files">The planned files.</param>
    /// <param name="entryCount">The number of entries the plan covers.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static object Execute(List<string> directories, List<string> files, int entryCount)
    {
        var removed = 0;

        try
        {
            foreach (var file in files)
            {
                System.IO.File.Delete(file);
                removed++;
            }

            for (var index = directories.Count - 1; index >= 0; index--)
            {
                Directory.Delete(directories[index], recursive: false);
                removed++;
            }

            return ToolResult.Text(
                string.Format(CultureInfo.InvariantCulture, TreeRemoved, entryCount));
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                string.Format(
                    CultureInfo.InvariantCulture, PartialDeleteFailed, removed, entryCount));
        }
    }

    /// <summary>
    ///     Walks the tree, counting every entry the removal would take, recording the entries a
    ///     plan within the ceiling would need, and stopping at the first entry the request cannot
    ///     honor — a link, or content the policy withholds.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Nothing is mutated here: the walk is what makes deciding before acting affordable,
    ///     which is why the link guard, the policy question and the entry ceiling can all act
    ///     before any removal.
    ///     </para>
    ///     <para>
    ///     The traversal itself belongs to <see cref="SubtreeGuard"/>, which visits the named
    ///     directory, then its files in the order the host reported them, then each child
    ///     directory depth first, and never descends into a link. That order decides which entry
    ///     a tree holding several offending ones is refused for, so it is fixed there rather than
    ///     restated here.
    ///     </para>
    ///     <para>
    ///     <b>Both questions are asked of every entry, and both stop the walk.</b> A link would
    ///     lead the removal out of the tree; an entry the write decision refuses is content the
    ///     operator's policy withholds, and taking it away would be exactly the containment
    ///     violation the policy exists to state. Neither is something the request asked for, so
    ///     the first of either ends the walk and refuses the whole plan.
    ///     </para>
    ///     <para>
    ///     Counting continues past the ceiling but recording does not. A plan past the ceiling
    ///     can only be refused, and the refusal needs the exact figure rather than the paths, so
    ///     retaining them would buy nothing and cost the whole tree's worth of strings on
    ///     precisely the request that is too large. The policy question is still asked past that
    ///     point, because it costs nothing to keep and a walk that stopped asking would make the
    ///     answer depend on how large the tree happened to be.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy every entry is judged by.</param>
    /// <param name="root">The directory to walk. Never itself a link.</param>
    /// <param name="directories">The directories recorded so far, shallowest first.</param>
    /// <param name="files">The files recorded so far.</param>
    /// <returns>The exact entry count, and the entry that refuses the plan when there is one.</returns>
    private static PlanOutcome Plan(
        PathPolicy policy,
        string root,
        List<string> directories,
        List<string> files)
    {
        var ceiling = policy.Limits.MaxDeleteEntries;
        var entryCount = 0;

        foreach (var entry in SubtreeGuard.Descend(root))
        {
            // A link — file or directory — would take the removal somewhere the request never
            // named, and removing the link entry itself would destroy a connection it never
            // mentioned.
            if (LinkGuard.IsLink(entry))
            {
                return new PlanOutcome(entryCount, entry.FullName, null);
            }

            // The path the model named passed the write decision; the entries beneath it are a
            // separate question, and this is where it is asked.
            if (!SubtreeGuard.PermitsWrite(policy, entry.FullName))
            {
                return new PlanOutcome(entryCount, null, entry.FullName);
            }

            Record(entry, directories, files, ceiling, ref entryCount);
        }

        return new PlanOutcome(entryCount, null, null);
    }

    /// <summary>
    ///     Counts one planned entry, and records its path while a plan is still within the
    ///     ceiling.
    /// </summary>
    /// <remarks>
    ///     The count is what the refusal names and is therefore always exact; the path is what
    ///     the removal needs and is therefore only worth keeping while a removal is still
    ///     possible. Which collection the entry joins is what orders phase two: files are removed
    ///     first, and directories in reverse of the shallowest-first order they are recorded in,
    ///     so each is empty when it goes.
    /// </remarks>
    /// <param name="entry">The entry the walk reached.</param>
    /// <param name="directories">The planned directories, shallowest first.</param>
    /// <param name="files">The planned files.</param>
    /// <param name="ceiling">The largest number of entries a plan may record.</param>
    /// <param name="entryCount">The running count, updated in place.</param>
    private static void Record(
        FileSystemInfo entry,
        List<string> directories,
        List<string> files,
        int ceiling,
        ref int entryCount)
    {
        entryCount++;

        if (entryCount > ceiling)
        {
            return;
        }

        if (entry is DirectoryInfo)
        {
            directories.Add(entry.FullName);
        }
        else
        {
            files.Add(entry.FullName);
        }
    }

    /// <summary>
    ///     The result of the planning walk: how many entries the removal would take, and the one
    ///     entry that refuses it, if any.
    /// </summary>
    /// <remarks>
    ///     The two refusals are carried separately rather than as one entry and a flag, because
    ///     they are different facts with different messages: a link leads out of the tree, while
    ///     a withheld entry is inside it and permitted to nobody. At most one is ever set.
    /// </remarks>
    /// <param name="EntryCount">
    ///     The exact number of entries the walk counted, whether or not they were all recorded.
    ///     On a refusal it is the count reached so far, which nothing reports.
    /// </param>
    /// <param name="LinkEntry">The resolved path of the first link found, or null.</param>
    /// <param name="DeniedEntry">
    ///     The resolved path of the first entry the policy withholds, or null.
    /// </param>
    private readonly record struct PlanOutcome(
        int EntryCount, string? LinkEntry, string? DeniedEntry);

    /// <summary>
    ///     Determines whether an exception represents a file-system failure rather than a defect
    ///     that should be allowed to propagate.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the directory could not be deleted;
    ///     otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsAccessFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException;
    }
}
