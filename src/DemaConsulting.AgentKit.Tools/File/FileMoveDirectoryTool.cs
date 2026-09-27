using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.File;

/// <summary>
///     The <c>file_move_directory</c> tool: moves a directory, with everything beneath it, from a
///     source the access policy permits the agent to write to a destination it permits it to
///     write.
/// </summary>
/// <remarks>
///     <para>
///     <b>Both endpoints are judged by the write decision, because a move changes both.</b> The
///     move takes the directory away from its source and creates it at its destination, so it
///     needs write permission at the source as well as the destination. The source is judged
///     first, so a tree an agent may read but not write can never be taken away from where its
///     operator put it.
///     </para>
///     <para>
///     <b>A destination in the same parent renames the directory.</b> There is no separate rename
///     tool, because a rename is the same operation with a destination that happens to share the
///     source's parent. The description says so, since a model that does not know it would have no
///     way of finding the capability.
///     </para>
///     <para>
///     <b>There is no overwrite flag, and any existing destination is refused.</b> The analogy to
///     <see cref="FileMoveTool"/> does not hold: that tool's overwrite replaces one named file
///     with one named file, whereas replacing a directory is a recursive destruction of an
///     unbounded tree performed under a verb the model reads as "move". That would smuggle the
///     single most dangerous operation in the family past both of the controls built for it — the
///     entry ceiling and the link guard, neither of which a move applies. The capability is not
///     lost, only made explicit: a model that means to replace a directory removes the destination
///     first, bounded and reported, and then moves into the space.
///     </para>
///     <para>
///     <b>A destination inside the source is refused.</b> Moving a directory into itself is not an
///     operation the file system can honor; it is caught by comparing the resolved destination
///     against the resolved source rather than left to produce a partial result or an opaque
///     error.
///     </para>
///     <para>
///     <b>A missing destination parent is refused, never materialized</b>, which is the same rule
///     the copy and move tools already state: creating a directory tree is a side effect this tool
///     was not asked for and a separate tool exists to perform.
///     </para>
///     <para>
///     <b>A move between volumes is refused rather than emulated.</b> The framework move cannot
///     cross a volume boundary, and a policy may well grant a workspace on one volume and a
///     session location on another. Emulating it would mean a recursive copy followed by a
///     recursive delete wearing a move's name — double the blast radius, and a deletion that
///     applied neither the entry ceiling nor the link guard. The limitation is stated rather than
///     hidden behind a fallback.
///     </para>
///     <para>
///     <b>Neither endpoint may be reached through a link inside the grant.</b> Path resolution is
///     lexical — see <see cref="RealPathResolver"/> — so <c>grant/link/sub</c> satisfies the write
///     decision whenever <c>grant/link</c> is spelled inside the grant, however far outside the
///     grant the link actually leads. Moving a tree out of, or into, a location reached that way
///     would take or place an entire tree where nothing was granted, so both endpoints are
///     classified by the shared rule in <see cref="LinkGuard"/> and either one refuses the whole
///     request. This matches <see cref="FileDeleteDirectoryTool"/>, and for the same reason: the
///     narrowing is applied exactly where the blast radius is a whole tree rather than a single
///     entry, and the rest of the library still resolves lexically. The grant root itself, and
///     everything above it, is not classified — that ancestry is the application author's choice.
///     </para>
///     <para>
///     <b>Every entry the move would relocate is judged by the policy, not only the two
///     directory paths.</b> <see cref="Directory.Move(string, string)"/> takes the whole subtree,
///     so judging the source and the destination alone answers a different question from the one
///     the operation asks. A grant permits a location and may exclude names within it: under
///     <c>ReadWrite(root, ["*.key"])</c> the policy permits <c>root/drafts</c> and refuses
///     <c>root/drafts/secret.key</c>, so a move judged by the two named paths would relocate
///     content the policy excludes — and land it at a path the policy was never asked about.
///     Both sides are therefore pre-flighted: every entry beneath the source is judged where it
///     stands, and again at the location it would land, through the one walk and the one
///     predicate in <see cref="SubtreeGuard"/> that <see cref="FileDeleteDirectoryTool"/> uses.
///     One refused entry refuses the whole request. Unlike the removal there is no partial state
///     to reason about — a move is one framework call that either happens or does not — so the
///     check is purely pre-flight and its cost is one metadata-only walk of the tree.
///     </para>
///     <para>
///     <b>The endpoint classification is pre-flight, and promises only that.</b> It answers for
///     the paths as they stand when the request is judged, before anything is moved. It is not a
///     defense against a process racing the tool: one that can write inside a location the
///     operator already granted may replace a component between the check and the
///     <see cref="Directory.Move(string, string)"/>, and re-checking the path would only move
///     that window rather than close it, because portable .NET offers no handle-relative,
///     no-follow directory move. An adversary already writing inside a granted location is
///     outside what a path-based API can defend against, and this unit does not claim otherwise.
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
public static class FileMoveDirectoryTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "file_move_directory";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Moves a directory, with everything beneath it, from a source the agent may write to a "
        + "destination it may write. A destination in the same parent renames the directory. "
        + "Paths are relative to the workspace root. Refuses any destination that already exists, "
        + "because replacing a directory would destroy everything beneath it, and refuses a "
        + "destination inside the directory being moved. Refuses either path when it is reached "
        + "through a link that leads out of the permitted location, and refuses the move when "
        + "any entry beneath the directory, or the place that entry would land, is one the agent "
        + "is not permitted to write. Returns a confirmation, or a "
        + "denial explaining why the request was refused.";

    /// <summary>
    ///     The refusal used when the request carries no source path.
    /// </summary>
    private const string SourceRequired =
        "A source path is required. Supply the path of the directory to move, relative to the "
        + "workspace root, for example 'drafts'.";

    /// <summary>
    ///     The refusal used when the request carries no destination path.
    /// </summary>
    private const string DestinationRequired =
        "A destination path is required. Supply the path to move the directory to, relative to "
        + "the workspace root, for example 'archive/drafts'. A destination in the same parent "
        + "renames the directory.";

    /// <summary>
    ///     The refusal used when the source names a file rather than a directory.
    /// </summary>
    private const string SourceIsFile =
        "The source path is a file, not a directory. This tool moves a directory with everything "
        + "beneath it; name the directory to move.";

    /// <summary>
    ///     The refusal used when the source directory does not exist.
    /// </summary>
    private const string SourceNotFound = "The source directory does not exist.";

    /// <summary>
    ///     The refusal used when the destination is an existing directory.
    /// </summary>
    private const string DestinationExists =
        "The destination already exists. This tool never replaces a directory, because replacing "
        + "one would destroy everything beneath it; move to a name that is free, or remove the "
        + "destination first.";

    /// <summary>
    ///     The refusal used when the destination is an existing file.
    /// </summary>
    private const string DestinationIsFile =
        "The destination is an existing file. This tool moves a directory and never replaces a "
        + "file; name a destination that is free.";

    /// <summary>
    ///     The refusal used when the destination lies inside the directory being moved.
    /// </summary>
    private const string DestinationBeneathSource =
        "The destination lies inside the directory being moved. A directory cannot be moved into "
        + "itself; name a destination outside it.";

    /// <summary>
    ///     The refusal used when the destination's parent directory does not exist.
    /// </summary>
    private const string ParentMissing =
        "The parent directory of the destination does not exist.";

    /// <summary>
    ///     The refusal used when the source is reached through a link inside the grant.
    /// </summary>
    /// <remarks>
    ///     The placeholder receives the offending component as the model spelled it — never the
    ///     resolved host path, and never the link's target, which is the location outside the
    ///     grant that this guard exists to protect.
    /// </remarks>
    private const string SourceLeadsThroughALink =
        "The source path is reached through a link, at '{0}', that leads out of the permitted "
        + "location, so nothing was moved. Moving a tree out of a location reached that way "
        + "would take content from outside everything the operator granted, which this request "
        + "never named. Name a source that does not pass through that link.";

    /// <summary>
    ///     The refusal used when the destination is reached through a link inside the grant.
    /// </summary>
    /// <remarks>
    ///     Reported separately from the source because the two are different mistakes and the
    ///     model has to know which of its two paths to re-address. The placeholder carries the
    ///     same model-supplied spelling and discloses the link's target no more than the source
    ///     refusal does.
    /// </remarks>
    private const string DestinationLeadsThroughALink =
        "The destination path is reached through a link, at '{0}', that leads out of the "
        + "permitted location, so nothing was moved. Moving a tree into a location reached that "
        + "way would place it outside everything the operator granted, which this request never "
        + "named. Name a destination that does not pass through that link.";

    /// <summary>
    ///     The refusal used when a permitted move cannot be completed for a file-system reason.
    /// </summary>
    /// <remarks>
    ///     This is the refusal a cross-volume move receives, among others. It names no host
    ///     detail, because the exception text behind it is developer-facing and may disclose a
    ///     path the policy never showed the model.
    /// </remarks>
    private const string MoveFailed = "The directory could not be moved.";

    /// <summary>
    ///     The refusal used when an entry beneath the source is one the policy withholds.
    /// </summary>
    /// <remarks>
    ///     The placeholder receives the source path the model supplied plus the relative
    ///     sub-path the walk reached — never a resolved host path. The refusal names the entry
    ///     rather than the rule that withheld it: the pattern is the operator's configuration,
    ///     and a model told which pattern matched learns the shape of the policy rather than
    ///     what it may do.
    /// </remarks>
    private const string SourceContainsADeniedEntry =
        "The source directory contains an entry, at '{0}', that this agent is not permitted to "
        + "write, so nothing was moved. Moving the directory would relocate content the access "
        + "policy withholds, which this request never named. Name a directory that does not "
        + "contain it.";

    /// <summary>
    ///     The refusal used when an entry would land where the policy permits no writing.
    /// </summary>
    /// <remarks>
    ///     Reported separately from the source because it is a different fact: the entry may be
    ///     perfectly writable where it stands and refused where it would go, which is the case a
    ///     policy carrying a narrower grant at the destination produces. The placeholder carries
    ///     the destination path the model supplied plus the relative sub-path, so the model can
    ///     see which of its two paths to re-address.
    /// </remarks>
    private const string DestinationWouldHoldADeniedEntry =
        "The move would place an entry at '{0}', which this agent is not permitted to write, so "
        + "nothing was moved. Moving the directory would land content where the access policy "
        + "permits none, at a location this request never named. Name a destination that "
        + "permits everything the directory holds.";

    /// <summary>
    ///     Creates the <c>file_move_directory</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="FilePack"/>. The policy is captured by the
    ///     returned tool's delegate, so the tool cannot later be pointed at a different policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every move this tool performs.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy)
    {
        // A missing policy is a programming error in the composing application.
        ArgumentNullException.ThrowIfNull(policy);

        // Declared to return object on purpose; see the type remarks before changing this.
        // Every parameter carries a default so that an omitted argument becomes a refusal this
        // tool composes, rather than a framework error raised before the body is reached. There
        // is deliberately no overwrite parameter; see the type remarks.
        var move = (
                [Description(
                    "The path of the directory to move, relative to the workspace root, "
                    + "for example 'drafts'.")]
                string? source = null,
                [Description(
                    "The path to move the directory to, relative to the workspace root, for "
                    + "example 'archive/drafts'. A destination in the same parent renames the "
                    + "directory. It must not already exist.")]
                string? destination = null) =>
            Move(policy, source, destination);

        return GuardedToolFactory.Create(move, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Moves one directory tree, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <remarks>
    ///     Both write decisions are taken first and before anything is learned about the file
    ///     system, so a refused source or destination discloses nothing about what exists beyond
    ///     the permitted locations.
    /// </remarks>
    /// <param name="policy">The access policy governing the move.</param>
    /// <param name="source">The directory to move, or null when the model supplied none.</param>
    /// <param name="destination">The path to move to, or null when it supplied none.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static object Move(PathPolicy policy, string? source, string? destination)
    {
        // Malformed requests are refused rather than thrown; the model supplied them.
        if (string.IsNullOrWhiteSpace(source))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, SourceRequired);
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, DestinationRequired);
        }

        // A move removes the source and creates the destination, so both are judged by the write
        // decision. The source is judged first so a read-only original cannot be taken away.
        if (!policy.TryResolveWrite(source, out var realSource, out var sourceDenial))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, sourceDenial);
        }

        if (!policy.TryResolveWrite(destination, out var realDestination, out var destinationDenial))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, destinationDenial);
        }

        // The write decisions are lexical, so neither can see that a named component leads out
        // of the grant. Both endpoints are classified here, still before anything is learned
        // about the file system, and in the same order the write decisions were taken.
        var sourceLink = LinkGuard.FindLinkedAncestor(policy, realSource);
        if (sourceLink is not null)
        {
            return ToolResult.Denied(
                DenialReason.PathNotPermitted,
                string.Format(
                    CultureInfo.InvariantCulture,
                    SourceLeadsThroughALink,
                    LinkGuard.DescribeAncestor(source, realSource, sourceLink)));
        }

        var destinationLink = LinkGuard.FindLinkedAncestor(policy, realDestination);
        if (destinationLink is not null)
        {
            return ToolResult.Denied(
                DenialReason.PathNotPermitted,
                string.Format(
                    CultureInfo.InvariantCulture,
                    DestinationLeadsThroughALink,
                    LinkGuard.DescribeAncestor(destination, realDestination, destinationLink)));
        }

        // A file source has no tree to move; this tool is not a second route to file_move.
        if (System.IO.File.Exists(realSource))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, SourceIsFile);
        }

        // A missing source is a target-not-found refusal stating only what was wrong.
        if (!Directory.Exists(realSource))
        {
            return ToolResult.Denied(DenialReason.TargetNotFound, SourceNotFound);
        }

        // Any existing destination is refused — there is no overwrite; see the type remarks. The
        // file case is reported separately because the two mistakes are different mistakes.
        if (System.IO.File.Exists(realDestination))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, DestinationIsFile);
        }

        if (Directory.Exists(realDestination))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, DestinationExists);
        }

        // A destination inside the source would move the tree into itself.
        if (IsBeneath(realSource, realDestination))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, DestinationBeneathSource);
        }

        try
        {
            // A missing parent is refused, never created.
            var parent = Path.GetDirectoryName(realDestination);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                return ToolResult.Denied(DenialReason.TargetNotFound, ParentMissing);
            }

            // The two write decisions covered the two paths the model named. The move relocates
            // everything beneath the source as well, so every entry is judged where it stands
            // and again where it would land, before anything is touched.
            var denial = FindDeniedEntry(
                policy, source, realSource, destination, realDestination);
            if (denial is not null)
            {
                return denial;
            }

            Directory.Move(realSource, realDestination);

            return ToolResult.Text("Moved the directory and everything beneath it.");
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, MoveFailed);
        }
    }

    /// <summary>
    ///     Finds the first entry beneath the source that the policy refuses, either where it
    ///     stands or at the location the move would land it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Why the walk is needed at all.</b> The write decisions above judged two paths;
    ///     <see cref="Directory.Move(string, string)"/> relocates a whole subtree. A grant that
    ///     permits a directory while excluding a name inside it — <c>ReadWrite(root, ["*.key"])</c>
    ///     permits <c>root/drafts</c> and refuses <c>root/drafts/secret.key</c> — would otherwise
    ///     have its exclusion honored for a path the model spells and ignored for the same file
    ///     reached as part of a tree.
    ///     </para>
    ///     <para>
    ///     <b>Why both sides are asked.</b> Where an entry stands and where it would land are
    ///     different locations, and a policy may judge them differently: a destination covered by
    ///     a narrower grant refuses content the source's grant permits. Taking the source alone
    ///     would place content where the operator allowed none; taking the destination alone
    ///     would let a tree be taken from where the policy withholds it. The source is asked
    ///     first, matching the order the two write decisions were taken.
    ///     </para>
    ///     <para>
    ///     The walk never follows a link, so a link beneath the source is judged as the entry it
    ///     is — which is exactly what the move relocates, since a directory move carries a link
    ///     without disturbing what it points at.
    ///     </para>
    ///     <para>
    ///     The walk enumerates an arbitrary tree, so it can fail for a file-system reason. Those
    ///     failures are caught by the caller's classification and reported as a move that could
    ///     not be completed, which is the whole truth: nothing has been moved.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy every entry is judged by.</param>
    /// <param name="source">The source path exactly as the model supplied it.</param>
    /// <param name="realSource">The resolved location of the source.</param>
    /// <param name="destination">The destination path exactly as the model supplied it.</param>
    /// <param name="realDestination">The resolved location of the destination.</param>
    /// <returns>
    ///     The refusal naming the offending entry, or <see langword="null"/> when the policy
    ///     permits every entry at both ends.
    /// </returns>
    private static object? FindDeniedEntry(
        PathPolicy policy,
        string source,
        string realSource,
        string destination,
        string realDestination)
    {
        // Only the location of each entry matters here: a move relocates a link as the link it
        // is, so what kind of entry it is changes nothing about the question being asked.
        foreach (var realEntry in SubtreeGuard.Descend(realSource).Select(entry => entry.FullName))
        {
            if (!SubtreeGuard.PermitsWrite(policy, realEntry))
            {
                return ToolResult.Denied(
                    DenialReason.PathNotPermitted,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        SourceContainsADeniedEntry,
                        SubtreeGuard.DescribeEntry(source, realSource, realEntry)));
            }

            // Where the entry would land: the same sub-path, rooted at the destination. The
            // named directory itself lands at the destination, which resolves to "." and is
            // spelled as the destination path.
            var relative = Path.GetRelativePath(realSource, realEntry);
            var landing = relative == "."
                ? realDestination
                : Path.Combine(realDestination, relative);

            if (!SubtreeGuard.PermitsWrite(policy, landing))
            {
                return ToolResult.Denied(
                    DenialReason.PathNotPermitted,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        DestinationWouldHoldADeniedEntry,
                        SubtreeGuard.DescribeEntry(destination, realDestination, landing)));
            }
        }

        return null;
    }

    /// <summary>
    ///     Determines whether one resolved path lies inside another.
    /// </summary>
    /// <remarks>
    ///     Compared as paths rather than as raw strings, so that a sibling whose name merely
    ///     begins with the source's name — <c>drafts-archive</c> beside <c>drafts</c> — is not
    ///     mistaken for a child. The comparison follows the host's own case rules, because two
    ///     spellings that differ only in case name the same directory on Windows and different
    ///     directories elsewhere.
    ///     <para>
    ///     The "climbs out" test is separator-aware for the same reason the prefix test is: a
    ///     genuine child named <c>..foo</c> produces the relative path <c>..foo</c>, and a bare
    ///     <c>StartsWith("..")</c> would read that real child as lying outside the source — so a
    ///     move into it would escape this refusal and fail later with an opaque file-system error
    ///     the model cannot act on. Only the exact segment <c>..</c>, alone or followed by a
    ///     separator, means the destination sits above or beside the source.
    ///     </para>
    /// </remarks>
    /// <param name="candidateParent">The resolved path that may contain the other.</param>
    /// <param name="candidateChild">The resolved path that may lie inside the other.</param>
    /// <returns>
    ///     <see langword="true"/> when the child lies inside the parent; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static bool IsBeneath(string candidateParent, string candidateChild)
    {
        var relative = Path.GetRelativePath(candidateParent, candidateChild);

        // GetRelativePath returns the destination unchanged when the two share no root, and a
        // path whose first segment is exactly ".." when the destination sits above or beside the
        // source. Comparing the segment, rather than the first two characters, is what keeps a
        // real child named "..foo" from being read as an escape.
        return !Path.IsPathRooted(relative)
            && relative != "."
            && !ClimbsOut(relative);
    }

    /// <summary>
    ///     Determines whether a relative path leaves the location it was computed against.
    /// </summary>
    /// <remarks>
    ///     A relative path leaves its anchor exactly when its first segment is the parent
    ///     segment <c>..</c> — either the whole path, or followed by a separator. A name that
    ///     merely begins with two dots is an ordinary segment and stays inside.
    /// </remarks>
    /// <param name="relative">The relative path to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the path climbs out of its anchor; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static bool ClimbsOut(string relative)
    {
        if (relative == "..")
        {
            return true;
        }

        return relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Determines whether an exception represents a file-system failure rather than a defect
    ///     that should be allowed to propagate.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the move could not be completed;
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
