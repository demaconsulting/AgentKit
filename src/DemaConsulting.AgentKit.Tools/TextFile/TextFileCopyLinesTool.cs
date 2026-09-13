using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The <c>text_file_copy_lines</c> tool: captures a range of lines from a permitted file into a
///     named buffer slot while leaving the source file byte-identical.
/// </summary>
/// <remarks>
///     <para>
///     <b>This is the non-destructive sibling of <see cref="TextFileCutLinesTool"/>.</b> Where cut
///     removes a range and captures it so the removal can be undone, copy captures the same range and
///     changes nothing: the source file is left byte-for-byte identical. "Copy" here means exactly
///     what it means in an editor — a copy to the clipboard that leaves the original untouched — so a
///     model duplicates a passage by copying a range and pasting it elsewhere without the copied
///     lines ever entering the context window.
///     </para>
///     <para>
///     <b>Lines are addressed by number.</b> The range is a 1-based, inclusive start and end, the
///     same numbering <see cref="TextFileReadTool"/> prints, so a model reads a numbered window and
///     then copies a range it saw. The captured text is placed in a named slot of the family's
///     cut/paste buffer, so it is pasted back with <see cref="TextFilePasteLinesTool"/> exactly as a
///     cut is.
///     </para>
///     <para>
///     <b>Paste does not consume the slot, so a copied block can be pasted more than once.</b> Slots
///     are named so several fragments can be held at once; the default slot serves the common case
///     with no name to invent. A later capture into the same slot — by copy or by cut — replaces it.
///     There is deliberately no peek or clear tool: the buffer holds only text and lives only as long
///     as the composition.
///     </para>
///     <para>
///     <b>This tool consults <see cref="PathPolicy.TryResolveRead"/> and nothing else</b> — the
///     deliberate inverse of cut's write decision. A copy performs no mutation of the source, so it
///     does to the source exactly what <see cref="TextFileReadTool"/> does: it reads bytes.
///     Requiring the write decision would wrongly refuse a legitimate copy from a read-only grant
///     while adding no protection, because the paste side independently enforces the write decision
///     on the destination. The delegate is declared to return <c>Task&lt;object&gt;</c> deliberately
///     — see the remarks on <see cref="GuardedToolFactory"/> — and every refusal is returned rather
///     than thrown. On success the confirmation reports how many lines were captured, states the
///     source is unchanged, and names the first and last of the captured lines.
///     </para>
///     <para>
///     The class is stateless and holds no buffer of its own; the buffer is supplied by the pack and
///     is the shared state copy, cut and paste operate on. The tool is safe for concurrent use
///     because the buffer guards its own access.
///     </para>
/// </remarks>
public static class TextFileCopyLinesTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "text_file_copy_lines";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Captures a range of lines from a permitted file into a named buffer, leaving the source file "
        + "unchanged, so the lines can be pasted elsewhere. The range is 1-based and inclusive. Omit "
        + "the name to use the default buffer. Returns a confirmation naming the captured lines, or a "
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
        "The requested path is a directory, not a file. Name the file to copy lines from.";

    /// <summary>
    ///     The refusal used when the requested file does not exist.
    /// </summary>
    private const string FileNotFound = "The requested file does not exist.";

    /// <summary>
    ///     The refusal used when a permitted file cannot be read for a file-system reason.
    /// </summary>
    private const string FileUnreadable = "The requested file could not be read.";

    /// <summary>
    ///     Creates the <c>text_file_copy_lines</c> tool governed by an access policy, sharing the
    ///     supplied buffer with the cut and paste tools.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="TextFilePack"/>, which allocates one buffer per
    ///     composition and hands it to this tool alongside the cut and paste tools. The policy and
    ///     the buffer are captured by the returned tool's delegate.
    /// </remarks>
    /// <param name="policy">The access policy governing every copy this tool performs.</param>
    /// <param name="buffers">The cut/paste buffer shared with the cut and paste tools.</param>
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
        var copy = (
                [Description(
                    "The path of the file to copy lines from, relative to the workspace root, "
                    + "for example 'notes.txt'.")]
                string? path = null,
                [Description("The 1-based line to begin copying at, inclusive.")]
                int startLine = 0,
                [Description("The 1-based line to stop copying at, inclusive.")]
                int endLine = 0,
                [Description(
                    "The buffer name to capture the copied lines into. Optional: omit it to use the "
                    + "default buffer.")]
                string? name = null,
                CancellationToken cancellationToken = default) =>
            CopyAsync(policy, buffers, path, startLine, endLine, name, cancellationToken);

        return GuardedToolFactory.Create(copy, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Copies a range of lines, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <param name="policy">The access policy governing the copy.</param>
    /// <param name="buffers">The cut/paste buffer to capture into.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="startLine">The 1-based first line to copy.</param>
    /// <param name="endLine">The 1-based last line to copy.</param>
    /// <param name="name">The buffer slot to capture into, or null for the default.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>A confirmation naming the captured lines, or a refusal naming its reason.</returns>
    private static async Task<object> CopyAsync(
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

        // The read decision alone — the deliberate inverse of cut's write decision. A copy mutates
        // nothing, so it does to the source exactly what a read does; the paste side enforces the
        // write decision on the destination.
        if (!policy.TryResolveRead(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // Reading a directory is not a meaningful operation.
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

        return await CopyPermittedFileAsync(
            buffers, path, realPath, startLine, endLine, slot, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Copies a range from a permitted file, capturing it into the buffer without altering the
    ///     source.
    /// </summary>
    /// <param name="buffers">The cut/paste buffer to capture into.</param>
    /// <param name="path">The path the model requested, echoed in the confirmation.</param>
    /// <param name="realPath">The real location the policy permitted.</param>
    /// <param name="startLine">The 1-based first line to copy.</param>
    /// <param name="endLine">The 1-based last line to copy.</param>
    /// <param name="slot">The buffer slot to capture into.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> CopyPermittedFileAsync(
        TextFileLineBuffers buffers,
        string path,
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

            // The range is validated against the file's real line count before anything is captured,
            // so an out-of-range copy captures nothing and the source is untouched regardless.
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

            // Capture the exact raw slice — terminators and all — so pasting it reproduces the copied
            // lines byte for byte. The source is never written, so it stays byte-identical.
            var startOffset = TextLines.OffsetOfLine(lines, startLine);
            var endOffset = TextLines.OffsetOfLine(lines, end + 1);
            var slice = text[startOffset..endOffset];

            buffers.Capture(slot, slice);

            // Echo the model-supplied path, not the resolved host path, so no host layout leaks.
            var reportedPath = TextLines.ToForwardSlash(path);

            var count = end - startLine + 1;
            return ToolResult.Text(
                "Copied " + count.ToString(CultureInfo.InvariantCulture) + " lines ("
                + startLine.ToString(CultureInfo.InvariantCulture) + "-"
                + end.ToString(CultureInfo.InvariantCulture) + ") into buffer '" + slot
                + "'. " + reportedPath + " is unchanged."
                + " First: " + TextLines.Content(lines[startLine - 1])
                + " Last: " + TextLines.Content(lines[end - 1]));
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, FileUnreadable);
        }
    }

    /// <summary>
    ///     Determines whether an exception represents a file-system failure rather than a defect
    ///     that should be allowed to propagate.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the file could not be read; otherwise
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
