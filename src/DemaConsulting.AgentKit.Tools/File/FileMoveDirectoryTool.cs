using System.ComponentModel;
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
        + "destination inside the directory being moved. Returns a confirmation, or a denial "
        + "explaining why the request was refused.";

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
    ///     The refusal used when a permitted move cannot be completed for a file-system reason.
    /// </summary>
    /// <remarks>
    ///     This is the refusal a cross-volume move receives, among others. It names no host
    ///     detail, because the exception text behind it is developer-facing and may disclose a
    ///     path the policy never showed the model.
    /// </remarks>
    private const string MoveFailed = "The directory could not be moved.";

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

            Directory.Move(realSource, realDestination);

            return ToolResult.Text("Moved the directory and everything beneath it.");
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, MoveFailed);
        }
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
        // path beginning with '..' when the destination sits above or beside the source.
        return !Path.IsPathRooted(relative)
            && relative != "."
            && !relative.StartsWith("..", StringComparison.Ordinal);
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
