using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The <c>text_file_write</c> tool: replaces the text of one file the access policy permits
///     the agent to write.
/// </summary>
/// <remarks>
///     <para>
///     <b>This is the tool that makes the read-only/read-write grant distinction observable.</b>
///     It consults <see cref="PathPolicy.TryResolveWrite"/> and nothing else, so a path the agent
///     may read is refused for writing unless a read-write grant permits it too. That is what makes
///     a read-wide, write-narrow configuration meaningful rather than decorative, and it is
///     covered by a dedicated scenario.
///     </para>
///     <para>
///     <b>The tool creates no directory.</b> A missing parent directory is refused rather than
///     materialized. Silently creating a tree is a file system side effect the operator never
///     asked for, and in a mistyped path it would scatter directories an agent then believes are
///     real. Refusing is the fail-safe reading.
///     </para>
///     <para>
///     <b>No write ceiling is invented.</b> <see cref="ToolLimits"/> publishes a read budget and
///     a result budget; neither describes a write, and repurposing one would give a host a
///     control whose name does not say what it does. The size of a write is already bounded by
///     the model's own output.
///     </para>
///     <para>
///     <b>A path the model supplies is written relative to the workspace root.</b> The access
///     policy holds the workspace a bare name is resolved against, and an absolute path remains
///     expressible and remains subject to the same containment decision. Both parameters carry a
///     default, so an omitted argument becomes a refusal stating what to supply rather than a
///     framework error the model cannot interpret.
///     </para>
///     <para>
///     The delegate is declared to return <c>Task&lt;object&gt;</c> deliberately — see the
///     remarks on <see cref="GuardedToolFactory"/> — and every refusal is returned rather than
///     thrown. The tool's own confirmation and its guard messages name only a character count or a
///     ceiling, while a refusal the access policy produces states what was requested, how a relative
///     request was interpreted, and which locations are permitted — including any read-only location,
///     shown as such, so the model learns why a write is refused there. Each refusal states what the
///     model should do next, because an agent told only "no" retries the same request.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class TextFileWriteTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     Published as a constant on the unit that defines the tool so that a caller naming it
    ///     cannot drift from the name actually registered.
    /// </remarks>
    public const string ToolName = "text_file_write";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Writes text content to a file the agent is permitted to write, replacing any existing "
        + "content. Paths are relative to the workspace root. Returns a confirmation, or a "
        + "denial explaining why the request was refused.";

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
        "File content is required. Supply the text to write, which may be an empty string when "
        + "an empty file is what is wanted.";

    /// <summary>
    ///     The refusal used when the request names a directory rather than a file.
    /// </summary>
    private const string PathIsDirectory =
        "The requested path is a directory, not a file. Name the file to write within it.";

    /// <summary>
    ///     The refusal used when the parent directory of the requested path does not exist.
    /// </summary>
    private const string ParentMissing =
        "The parent directory of the requested path does not exist. Write to a location that "
        + "already exists, such as the workspace root.";

    /// <summary>
    ///     The refusal used when a permitted path cannot be written.
    /// </summary>
    private const string FileUnwritable = "The requested file could not be written.";

    /// <summary>
    ///     Creates the <c>text_file_write</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="TextFilePack"/>. The policy is captured by
    ///     the returned tool's delegate, so the tool cannot later be pointed at a different
    ///     policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every write this tool performs.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy)
    {
        // A missing policy is a programming error in the composing application.
        ArgumentNullException.ThrowIfNull(policy);

        // Declared to return Task<object> on purpose; see the type remarks before changing this.
        // Both parameters carry a default so that an omitted argument becomes a refusal this
        // tool composes, rather than a framework error raised before the body is reached.
        var write = (
                [Description(
                    "The path of the file to write, relative to the workspace root, "
                    + "for example 'notes.txt'.")]
                string? path = null,
                [Description("The text content to write, replacing any existing content.")]
                string? content = null,
                CancellationToken cancellationToken = default) =>
            WriteAsync(policy, path, content, cancellationToken);

        return GuardedToolFactory.Create(write, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Writes one file, refusing rather than throwing whenever the request cannot be honored.
    /// </summary>
    /// <remarks>
    ///     The write decision is taken before anything is learned about the file system, so a
    ///     refused path discloses nothing about what does or does not exist beyond the permitted
    ///     write location.
    /// </remarks>
    /// <param name="policy">The access policy governing the write.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="content">The text the model wishes to write, or null when it supplied none.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A confirmation naming no location, or a refusal naming its reason.</returns>
    private static async Task<object> WriteAsync(
        PathPolicy policy,
        string? path,
        string? content,
        CancellationToken cancellationToken)
    {
        // A malformed request is refused rather than thrown: the model supplied it, so the model
        // is the one that must be told how to correct it.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        // Null content is a malformed request; an empty string is a legitimate empty file.
        if (content is null)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, ContentRequired);
        }

        // The write decision alone. A path a read-only grant permits is not thereby writable, which
        // is the whole point of letting each grant's access level decide.
        if (!policy.TryResolveWrite(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // Writing over a directory is not a meaningful operation; no other tool does it better,
        // so no redirect is offered.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsDirectory);
        }

        return await WritePermittedFileAsync(realPath, content, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Writes to a path the policy has already permitted.
    /// </summary>
    /// <remarks>
    ///     The parent directory is checked rather than created; see the type remarks for why
    ///     creating one would be a side effect nobody asked for. The confirmation names a
    ///     character count rather than a location, because the count is what the model needs to
    ///     confirm the write and the location it just named adds nothing.
    /// </remarks>
    /// <param name="realPath">The real location the policy permitted.</param>
    /// <param name="content">The text to write.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> WritePermittedFileAsync(
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

            // Existing content is replaced. This is stated in the tool's description so the
            // model is never surprised by it.
            await File.WriteAllTextAsync(realPath, content, cancellationToken)
                .ConfigureAwait(false);

            return ToolResult.Text(
                "Wrote " + content.Length.ToString(CultureInfo.InvariantCulture) + " characters.");
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            // A file system failure becomes a refusal the model can act on, while a genuine
            // defect still surfaces.
            return ToolResult.Denied(DenialReason.InvalidRequest, FileUnwritable);
        }
    }

    /// <summary>
    ///     Determines whether an exception represents a failure to reach or write a path rather
    ///     than a defect that should be allowed to propagate.
    /// </summary>
    /// <remarks>
    ///     Enumerated explicitly rather than catching everything, so that a genuine defect still
    ///     fails loudly instead of being reported to a model as an unwritable file.
    /// </remarks>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the file could not be reached or
    ///     written; otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsAccessFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException;
    }
}
