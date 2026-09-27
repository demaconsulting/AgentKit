using System.ComponentModel;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.File;

/// <summary>
///     The <c>file_create_directory</c> tool: creates a directory, and any missing directories
///     above it, in a location the access policy permits the agent to write.
/// </summary>
/// <remarks>
///     <para>
///     <b>Creation is judged by the write decision.</b> Materializing a directory changes the file
///     system, so a location an agent may read but not write cannot receive one. Write access is
///     never inferred from read access.
///     </para>
///     <para>
///     <b>Missing intermediate directories are created.</b> "Ensure this path exists, then write
///     into it" is what a model actually wants, and refusing because a middle component does not
///     exist yet would force it into a create-one-level-at-a-time loop that buys no safety: every
///     level created lies inside the same permitted location the policy already approved.
///     </para>
///     <para>
///     <b>A directory that already exists is a success, reported in its own words.</b> The
///     family's no-silent-overwrite instinct is about <em>destruction</em>, and creating a
///     directory that is already there destroys nothing — so refusing would borrow a rule from a
///     situation that does not apply. What does carry over is that the model must never be left
///     guessing which thing happened, so the two outcomes carry two distinct confirmations: one
///     says the directory was created, the other says it already existed and that nothing beneath
///     it was changed.
///     </para>
///     <para>
///     <b>An existing file at the named path is refused.</b> A file is never replaced by a
///     directory: that would be a destructive act wearing a creating verb, which is exactly the
///     kind of thing this library exists to make impossible to express.
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
public static class FileCreateDirectoryTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "file_create_directory";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Creates a directory in a location the agent is permitted to write, creating any missing "
        + "parent directories along the way. Paths are relative to the workspace root. Reports "
        + "separately whether the directory was created or already existed. Refuses when the name "
        + "is already a file. Returns a confirmation, or a denial explaining why the request was "
        + "refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    private const string PathRequired =
        "A directory path is required. Supply the path of the directory to create, relative to "
        + "the workspace root, for example 'reports/drafts'.";

    /// <summary>
    ///     The refusal used when the request names an existing file.
    /// </summary>
    private const string PathIsFile =
        "The requested path is an existing file, not a directory. A file is never replaced by a "
        + "directory; name a path that is free, or a directory that already exists.";

    /// <summary>
    ///     The refusal used when a permitted directory cannot be created for a file-system reason.
    /// </summary>
    private const string CreateFailed = "The requested directory could not be created.";

    /// <summary>
    ///     The confirmation used when the directory was created.
    /// </summary>
    private const string Created =
        "Created the directory, including any missing parent directories.";

    /// <summary>
    ///     The confirmation used when the directory was already there.
    /// </summary>
    private const string AlreadyExisted =
        "The directory already exists. Nothing was created and nothing beneath it was changed.";

    /// <summary>
    ///     Creates the <c>file_create_directory</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="FilePack"/>. The policy is captured by the
    ///     returned tool's delegate, so the tool cannot later be pointed at a different policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every creation this tool performs.</param>
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
        var create = (
                [Description(
                    "The path of the directory to create, relative to the workspace root, "
                    + "for example 'reports/drafts'.")]
                string? path = null) =>
            CreateDirectory(policy, path);

        return GuardedToolFactory.Create(create, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Creates one directory, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <remarks>
    ///     The write decision is taken before anything is learned about the file system, so a
    ///     refused path discloses nothing about what does or does not exist beyond the permitted
    ///     write location.
    /// </remarks>
    /// <param name="policy">The access policy governing the creation.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static object CreateDirectory(PathPolicy policy, string? path)
    {
        // A malformed request is refused rather than thrown; the model supplied it.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        // Creating a directory changes the file system, so the write decision alone governs it.
        if (!policy.TryResolveWrite(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // A file is never replaced by a directory, so the file check precedes everything else.
        if (System.IO.File.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsFile);
        }

        // Observed before the creation, because afterwards the two outcomes are indistinguishable
        // and the model would be told only that the path now exists.
        var existed = Directory.Exists(realPath);

        try
        {
            Directory.CreateDirectory(realPath);

            return ToolResult.Text(existed ? AlreadyExisted : Created);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, CreateFailed);
        }
    }

    /// <summary>
    ///     Determines whether an exception represents a file-system failure rather than a defect
    ///     that should be allowed to propagate.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the directory could not be created;
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
