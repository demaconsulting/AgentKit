using System.ComponentModel;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.File;

/// <summary>
///     The <c>file_delete</c> tool: deletes a single file the access policy permits the agent to
///     write.
/// </summary>
/// <remarks>
///     <para>
///     <b>Deletion is judged by the write decision.</b> Removing a file is a write in the strongest
///     sense, so a file an agent may read but not write cannot be deleted. This is what keeps a
///     read-only grant genuinely read-only.
///     </para>
///     <para>
///     <b>The tool deletes one file and never a directory, and never recurses.</b> A path that names
///     a directory is refused rather than removed, so no tree of files an operator did not name can
///     disappear behind a single call. Deleting a directory — with everything beneath it — is a far
///     larger and less reversible act than deleting one named file, and this increment deliberately
///     does not offer it.
///     </para>
///     <para>
///     <b>There is no quarantine, by decision.</b> AgentKit does not keep a recycle bin or a shadow
///     copy of a deleted file, because the recovery mechanism it relies on is source control: an
///     agent should be run inside a repository, where a mistaken deletion is recovered the same way
///     any unwanted change is. Adding a quarantine would duplicate, less reliably, a facility the
///     surrounding repository already provides.
///     </para>
///     <para>
///     <b>Deleting a file that does not exist is reported, not silently ignored.</b> A model that
///     asked to delete a name and received a bland success could not tell an accomplished deletion
///     from a mistyped path; the tool therefore refuses a missing target and directs the model to
///     the listing tool.
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
public static class FileDeleteTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     The bare verb <c>delete</c> is reserved by the Agent Framework's file access provider, so
    ///     the family prefix here is load-bearing rather than cosmetic: <c>file_delete</c> is a
    ///     conforming, non-colliding name where a bare <c>delete</c> would be refused.
    /// </remarks>
    public const string ToolName = "file_delete";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Deletes a single file the agent is permitted to write. Paths are relative to the workspace "
        + "root. Refuses to delete a directory and never recurses. Returns a confirmation, or a "
        + "denial explaining why the request was refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    private const string PathRequired =
        "A file path is required. Supply the path of the file to delete, relative to the workspace "
        + "root, for example 'notes.txt'.";

    /// <summary>
    ///     The refusal used when the request names a directory rather than a file.
    /// </summary>
    private const string PathIsDirectory =
        "The requested path is a directory, not a file. This tool deletes a single file and never a "
        + "directory; name the file to delete.";

    /// <summary>
    ///     The refusal used when the requested file does not exist.
    /// </summary>
    private const string FileNotFound = "The requested file does not exist.";

    /// <summary>
    ///     The refusal used when a permitted file cannot be deleted for a file-system reason.
    /// </summary>
    private const string DeleteFailed = "The requested file could not be deleted.";

    /// <summary>
    ///     Creates the <c>file_delete</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="FilePack"/>. The policy is captured by the
    ///     returned tool's delegate, so the tool cannot later be pointed at a different policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every deletion this tool performs.</param>
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
                    "The path of the file to delete, relative to the workspace root, "
                    + "for example 'notes.txt'.")]
                string? path = null) =>
            Delete(policy, path);

        return GuardedToolFactory.Create(delete, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Deletes one file, refusing rather than throwing whenever the request cannot be honored.
    /// </summary>
    /// <remarks>
    ///     The write decision is taken before anything is learned about the file system, so a refused
    ///     path discloses nothing about what does or does not exist beyond the permitted write
    ///     location.
    /// </remarks>
    /// <param name="policy">The access policy governing the deletion.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static object Delete(PathPolicy policy, string? path)
    {
        // A malformed request is refused rather than thrown; the model supplied it.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        // Deletion is a write, so the write decision alone governs it.
        if (!policy.TryResolveWrite(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // A directory is never deleted, and the tool never recurses: this is the whole point of the
        // single-file guarantee, so the check precedes the missing-file check.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsDirectory);
        }

        // A missing file is reported rather than treated as a silent success, so a mistyped path is
        // never mistaken for an accomplished deletion.
        if (!System.IO.File.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.TargetNotFound, FileNotFound);
        }

        try
        {
            System.IO.File.Delete(realPath);

            return ToolResult.Text("Deleted the file.");
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, DeleteFailed);
        }
    }

    /// <summary>
    ///     Determines whether an exception represents a file-system failure rather than a defect
    ///     that should be allowed to propagate.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the file could not be deleted; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static bool IsAccessFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException;
    }
}
