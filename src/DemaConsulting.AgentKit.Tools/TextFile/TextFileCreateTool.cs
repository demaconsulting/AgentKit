using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The <c>text_file_create</c> tool: writes a new text file the access policy permits the agent
///     to write, refusing to overwrite one that already exists.
/// </summary>
/// <remarks>
///     <para>
///     <b>Create refuses an existing file.</b> This is the whole difference between this tool and a
///     blind write: it brings a new file into existence and will not silently replace one that is
///     already there. An agent that means to change an existing file edits it with
///     <see cref="TextFileReplaceTool"/>, whose match-exactly-once contract makes the change precise;
///     an agent that means to start a new file uses this tool, and learns immediately if a file of
///     that name already exists rather than destroying it. The spike confirmed a model understood
///     this boundary from the description alone.
///     </para>
///     <para>
///     <b>This tool consults <see cref="PathPolicy.TryResolveWrite"/> and nothing else.</b> A path an
///     agent may read is refused for creation unless a read-write grant permits it too, which is what
///     keeps a read-wide, write-narrow configuration meaningful.
///     </para>
///     <para>
///     <b>The tool creates no directory.</b> A missing parent directory is refused rather than
///     materialized, because silently creating a tree is a side effect the operator never asked for
///     and, in a mistyped path, would scatter directories an agent then believes are real. Refusing
///     is the fail-safe reading.
///     </para>
///     <para>
///     The delegate is declared to return <c>Task&lt;object&gt;</c> deliberately — see the remarks on
///     <see cref="GuardedToolFactory"/> — and every refusal is returned rather than thrown. Empty
///     content is a legitimate new empty file; only a missing content argument is refused.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class TextFileCreateTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "text_file_create";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Creates a new text file the agent is permitted to write, with the given content. Paths are "
        + "relative to the workspace root. Refuses if a file already exists at the path. Returns a "
        + "confirmation, or a denial explaining why the request was refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    private const string PathRequired =
        "A file path is required. Supply a path relative to the workspace root, "
        + "for example 'notes.txt'.";

    /// <summary>
    ///     The refusal used when the request carries no content at all.
    /// </summary>
    /// <remarks>
    ///     Absent content is a malformed request; empty content is not. An empty file is a real
    ///     outcome an agent may legitimately want, so only <see langword="null"/> is refused.
    /// </remarks>
    private const string ContentRequired =
        "File content is required. Supply the text to write, which may be an empty string when an "
        + "empty file is what is wanted.";

    /// <summary>
    ///     The refusal used when the request names a directory rather than a file.
    /// </summary>
    private const string PathIsDirectory =
        "The requested path is a directory, not a file. Name the file to create within it.";

    /// <summary>
    ///     The refusal used when a file already exists at the requested path.
    /// </summary>
    private const string FileExists =
        "A file already exists at the requested path. This tool creates a new file and does not "
        + "replace an existing one.";

    /// <summary>
    ///     The refusal used when the parent directory of the requested path does not exist.
    /// </summary>
    private const string ParentMissing =
        "The parent directory of the requested path does not exist.";

    /// <summary>
    ///     The refusal used when a permitted path cannot be written.
    /// </summary>
    private const string FileUnwritable = "The requested file could not be created.";

    /// <summary>
    ///     Creates the <c>text_file_create</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="TextFilePack"/>. The policy is captured by the
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

        // Declared to return Task<object> on purpose; see the type remarks before changing this.
        // Both parameters carry a default so that an omitted argument becomes a refusal this tool
        // composes, rather than a framework error raised before the body is reached.
        var create = (
                [Description(
                    "The path of the file to create, relative to the workspace root, "
                    + "for example 'notes.txt'.")]
                string? path = null,
                [Description("The text content of the new file. May be an empty string.")]
                string? content = null,
                CancellationToken cancellationToken = default) =>
            CreateAsync(policy, path, content, cancellationToken);

        return GuardedToolFactory.Create(create, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Creates one file, refusing rather than throwing whenever the request cannot be honored.
    /// </summary>
    /// <param name="policy">The access policy governing the creation.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="content">The text the model wishes to write, or null when it supplied none.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A confirmation naming a character count, or a refusal naming its reason.</returns>
    private static async Task<object> CreateAsync(
        PathPolicy policy,
        string? path,
        string? content,
        CancellationToken cancellationToken)
    {
        // Malformed requests are refused rather than thrown; the model supplied them.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        if (content is null)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, ContentRequired);
        }

        // The write decision alone. A path a read-only grant permits is not thereby writable.
        if (!policy.TryResolveWrite(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // Creating over a directory is not a meaningful operation.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsDirectory);
        }

        // Create refuses to replace an existing file: this is the tool's defining guarantee.
        if (System.IO.File.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, FileExists);
        }

        return await CreatePermittedFileAsync(realPath, content, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Writes a new file at a path the policy has already permitted and confirmed absent.
    /// </summary>
    /// <param name="realPath">The real location the policy permitted.</param>
    /// <param name="content">The text to write.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> CreatePermittedFileAsync(
        string realPath,
        string content,
        CancellationToken cancellationToken)
    {
        try
        {
            // A missing parent is refused, never created.
            var parent = Path.GetDirectoryName(realPath);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                return ToolResult.Denied(DenialReason.TargetNotFound, ParentMissing);
            }

            await System.IO.File.WriteAllTextAsync(realPath, content, cancellationToken)
                .ConfigureAwait(false);

            return ToolResult.Text(
                "Created the file with "
                + content.Length.ToString(CultureInfo.InvariantCulture)
                + " characters.");
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, FileUnwritable);
        }
    }

    /// <summary>
    ///     Determines whether an exception represents a file-system failure rather than a defect
    ///     that should be allowed to propagate.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the file could not be created; otherwise
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
