using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The <c>text_file_cut_lines</c> tool: removes a range of lines from a permitted file and
///     captures them into a named buffer slot so the removal can always be undone.
/// </summary>
/// <remarks>
///     <para>
///     <b>This is the only way to remove a range of lines, and it always captures what it removes.</b>
///     A deletion of a whole range is inherently harder to undo than a content substitution, so the
///     tool that performs it never simply discards the lines: it copies them into a named slot of the
///     family's cut/paste buffer first, so a mistaken cut is recovered by pasting the slot back with
///     <see cref="TextFilePasteLinesTool"/>. "Cut" here means exactly what it means in an editor — a
///     move to the clipboard, not a destruction. The spike found that when a model wanted to delete a
///     section it reached for this tool rather than replacing the section with empty text.
///     </para>
///     <para>
///     <b>Lines are addressed by number.</b> The range is a 1-based, inclusive start and end, the
///     same numbering <see cref="TextFileReadTool"/> prints, so a model reads a numbered window and
///     then cuts a range it saw. Addressing a removal by line number is the natural complement to
///     addressing a substitution by content: navigation is by number, editing-in-place is by content.
///     </para>
///     <para>
///     <b>A later capture into the same slot replaces it.</b> Slots are named so several fragments
///     can be held at once; the default slot serves the common cut-then-paste-straight-back case with
///     no name to invent. There is deliberately no peek or clear tool: the buffer holds only text and
///     lives only as long as the composition.
///     </para>
///     <para>
///     This tool consults <see cref="PathPolicy.TryResolveWrite"/> and nothing else. The delegate is
///     declared to return <c>Task&lt;object&gt;</c> deliberately — see the remarks on
///     <see cref="GuardedToolFactory"/> — and every refusal is returned rather than thrown. On
///     success the confirmation reports how many lines were captured and the first and last of them.
///     </para>
///     <para>
///     The class is stateless and holds no buffer of its own; the buffer is supplied by the pack and
///     is the shared state cut and paste operate on. The tool is safe for concurrent use because the
///     buffer guards its own access.
///     </para>
/// </remarks>
public static class TextFileCutLinesTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "text_file_cut_lines";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Removes a range of lines from a permitted file and captures them into a named buffer so the "
        + "removal can be undone by pasting them back. The range is 1-based and inclusive. Omit the "
        + "name to use the default buffer. Returns a confirmation naming the captured lines, or a "
        + "denial explaining why the request was refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    private const string PathRequired =
        "A file path is required. Supply a path relative to the workspace root, "
        + "for example 'notes.txt'.";

    /// <summary>
    ///     The refusal used when the request names a directory rather than a file.
    /// </summary>
    private const string PathIsDirectory =
        "The requested path is a directory, not a file. Name the file to cut lines from.";

    /// <summary>
    ///     The refusal used when the requested file does not exist.
    /// </summary>
    private const string FileNotFound = "The requested file does not exist.";

    /// <summary>
    ///     The refusal used when a permitted file cannot be edited for a file-system reason.
    /// </summary>
    private const string FileUneditable = "The requested file could not be edited.";

    /// <summary>
    ///     Creates the <c>text_file_cut_lines</c> tool governed by an access policy, sharing the
    ///     supplied buffer with the paste tool.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="TextFilePack"/>, which allocates one buffer per
    ///     composition and hands it to both this tool and the paste tool. The policy and the buffer
    ///     are captured by the returned tool's delegate.
    /// </remarks>
    /// <param name="policy">The access policy governing every cut this tool performs.</param>
    /// <param name="buffers">The cut/paste buffer shared with the paste tool.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> or <paramref name="buffers"/> is
    ///     <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy, TextFileLineBuffers buffers)
    {
        // A missing policy or buffer is a programming error in the composing application.
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(buffers);

        // Declared to return Task<object> on purpose; see the type remarks before changing this.
        var cut = (
                [Description(
                    "The path of the file to cut lines from, relative to the workspace root, "
                    + "for example 'notes.txt'.")]
                string? path = null,
                [Description("The 1-based line to begin cutting at, inclusive.")]
                int startLine = 0,
                [Description("The 1-based line to stop cutting at, inclusive.")]
                int endLine = 0,
                [Description(
                    "The buffer name to capture the cut lines into. Optional: omit it to use the "
                    + "default buffer.")]
                string? name = null,
                CancellationToken cancellationToken = default) =>
            CutAsync(policy, buffers, path, startLine, endLine, name, cancellationToken);

        return GuardedToolFactory.Create(cut, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Cuts a range of lines, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <param name="policy">The access policy governing the cut.</param>
    /// <param name="buffers">The cut/paste buffer to capture into.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="startLine">The 1-based first line to cut.</param>
    /// <param name="endLine">The 1-based last line to cut.</param>
    /// <param name="name">The buffer slot to capture into, or null for the default.</param>
    /// <param name="cancellationToken">A token that cancels the edit.</param>
    /// <returns>A confirmation naming the captured lines, or a refusal naming its reason.</returns>
    private static async Task<object> CutAsync(
        PathPolicy policy,
        TextFileLineBuffers buffers,
        string? path,
        int startLine,
        int endLine,
        string? name,
        CancellationToken cancellationToken)
    {
        // A malformed request is refused rather than thrown; the model supplied it.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        // The write decision alone. A path a read-only grant permits is not thereby editable.
        if (!policy.TryResolveWrite(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // Editing a directory is not a meaningful operation.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsDirectory);
        }

        // A missing file is reported rather than created.
        if (!System.IO.File.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.TargetNotFound, FileNotFound);
        }

        var slot = string.IsNullOrWhiteSpace(name) ? TextFileLineBuffers.DefaultSlot : name;

        return await CutPermittedFileAsync(
            buffers, realPath, startLine, endLine, slot, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Cuts a range from a permitted file, capturing it into the buffer before removing it.
    /// </summary>
    /// <param name="buffers">The cut/paste buffer to capture into.</param>
    /// <param name="realPath">The real location the policy permitted.</param>
    /// <param name="startLine">The 1-based first line to cut.</param>
    /// <param name="endLine">The 1-based last line to cut.</param>
    /// <param name="slot">The buffer slot to capture into.</param>
    /// <param name="cancellationToken">A token that cancels the edit.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> CutPermittedFileAsync(
        TextFileLineBuffers buffers,
        string realPath,
        int startLine,
        int endLine,
        string slot,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = await System.IO.File.ReadAllTextAsync(realPath, cancellationToken)
                .ConfigureAwait(false);

            var lines = TextLines.Split(text);
            var total = lines.Count;

            // The range is validated against the file's real line count before anything is captured
            // or removed, so an out-of-range cut changes nothing.
            if (startLine < 1 || endLine < startLine || startLine > total)
            {
                return ToolResult.Denied(
                    DenialReason.InvalidRequest,
                    "The line range is out of bounds. The file has "
                    + total.ToString(CultureInfo.InvariantCulture)
                    + " lines; supply a 1-based start and end within it, with start no greater than "
                    + "end.");
            }

            var end = Math.Min(endLine, total);

            // Capture the exact raw slice — terminators and all — so pasting it back reproduces the
            // removed lines byte for byte.
            var startOffset = TextLines.OffsetOfLine(lines, startLine);
            var endOffset = TextLines.OffsetOfLine(lines, end + 1);
            var slice = text[startOffset..endOffset];

            buffers.Capture(slot, slice);

            var updated = string.Concat(text.AsSpan(0, startOffset), text.AsSpan(endOffset));
            await System.IO.File.WriteAllTextAsync(realPath, updated, cancellationToken)
                .ConfigureAwait(false);

            var count = end - startLine + 1;
            return ToolResult.Text(
                "Cut " + count.ToString(CultureInfo.InvariantCulture) + " lines ("
                + startLine.ToString(CultureInfo.InvariantCulture) + "-"
                + end.ToString(CultureInfo.InvariantCulture) + ") into buffer '" + slot
                + "'. First: " + TextLines.Content(lines[startLine - 1])
                + " Last: " + TextLines.Content(lines[end - 1]));
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, FileUneditable);
        }
    }

    /// <summary>
    ///     Determines whether an exception represents a file-system failure rather than a defect
    ///     that should be allowed to propagate.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the file could not be edited; otherwise
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
