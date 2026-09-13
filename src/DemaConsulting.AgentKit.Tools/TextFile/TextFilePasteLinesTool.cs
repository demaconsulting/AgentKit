using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The <c>text_file_paste_lines</c> tool: inserts the text previously captured into a named
///     buffer slot into a permitted file, at a chosen line.
/// </summary>
/// <remarks>
///     <para>
///     <b>Paste is the other half of a cut.</b> It takes whatever <see cref="TextFileCutLinesTool"/>
///     captured into a named slot and inserts it into a file, which is what makes a cut a reversible
///     move rather than a destruction: cut a range, then paste it back to undo, or paste it somewhere
///     else to relocate it. The captured text is restored exactly as it was removed — the same
///     characters and the same line terminators — so a cut immediately followed by a paste at the
///     same line reproduces the original file byte for byte.
///     </para>
///     <para>
///     <b>The insertion point is a line number, and omitting it appends.</b> <c>atLine</c> is the
///     1-based line the captured text is inserted before; a value past the end of the file, or an
///     omitted one, appends to the end. Addressing the insertion by line number matches how the cut
///     tool addresses the removal.
///     </para>
///     <para>
///     <b>Paste does not consume the slot.</b> The captured text stays in its slot after a paste, so
///     the same fragment can be pasted into more than one place. An empty slot — one never captured
///     into, or one from a different composition — is a refusal naming the slot, not a silent no-op,
///     so a model learns there was nothing to paste.
///     </para>
///     <para>
///     This tool consults <see cref="PathPolicy.TryResolveWrite"/> and nothing else. The delegate is
///     declared to return <c>Task&lt;object&gt;</c> deliberately — see the remarks on
///     <see cref="GuardedToolFactory"/> — and every refusal is returned rather than thrown.
///     </para>
///     <para>
///     The class is stateless and holds no buffer of its own; the buffer is supplied by the pack and
///     is the shared state cut and paste operate on. The tool is safe for concurrent use because the
///     buffer guards its own access.
///     </para>
/// </remarks>
public static class TextFilePasteLinesTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "text_file_paste_lines";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Inserts text previously captured by text_file_copy_lines or text_file_cut_lines into a "
        + "permitted file at a chosen 1-based line, or appends it when atLine is omitted. Omit "
        + "the name to use the default buffer. Pasting does not empty the buffer, so the same "
        + "capture can be pasted more than once. Returns a confirmation, or a denial explaining "
        + "why the request was refused.";

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
        "The requested path is a directory, not a file. Name the file to paste lines into.";

    /// <summary>
    ///     The refusal used when the requested file does not exist.
    /// </summary>
    private const string FileNotFound = "The requested file does not exist.";

    /// <summary>
    ///     The refusal used when <c>atLine</c> is not a positive number.
    /// </summary>
    private const string InvalidAtLine =
        "The atLine must be 1 or greater. Line numbers are 1-based; omit atLine to append to the "
        + "end of the file.";

    /// <summary>
    ///     The refusal used when a permitted file cannot be edited for a file-system reason.
    /// </summary>
    private const string FileUneditable = "The requested file could not be edited.";

    /// <summary>
    ///     Creates the <c>text_file_paste_lines</c> tool governed by an access policy, sharing the
    ///     supplied buffer with the cut tool.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="TextFilePack"/>, which allocates one buffer per
    ///     composition and hands it to both this tool and the cut tool. The policy and the buffer are
    ///     captured by the returned tool's delegate.
    /// </remarks>
    /// <param name="policy">The access policy governing every paste this tool performs.</param>
    /// <param name="buffers">The cut/paste buffer shared with the cut tool.</param>
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
        var paste = (
                [Description(
                    "The path of the file to paste into, relative to the workspace root, "
                    + "for example 'notes.txt'.")]
                string? path = null,
                [Description(
                    "The 1-based line to insert the captured text before. Optional: omit it to "
                    + "append to the end of the file.")]
                int? atLine = null,
                [Description(
                    "The buffer name to paste from. Optional: omit it to use the default buffer.")]
                string? name = null,
                CancellationToken cancellationToken = default) =>
            PasteAsync(policy, buffers, path, atLine, name, cancellationToken);

        return GuardedToolFactory.Create(paste, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Pastes captured text, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <param name="policy">The access policy governing the paste.</param>
    /// <param name="buffers">The cut/paste buffer to paste from.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="atLine">The 1-based line to insert before, or null to append.</param>
    /// <param name="name">The buffer slot to paste from, or null for the default.</param>
    /// <param name="cancellationToken">A token that cancels the edit.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> PasteAsync(
        PathPolicy policy,
        TextFileLineBuffers buffers,
        string? path,
        int? atLine,
        string? name,
        CancellationToken cancellationToken)
    {
        // Malformed requests are refused rather than thrown; the model supplied them.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        if (atLine is < 1)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, InvalidAtLine);
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

        // An empty slot is a refusal naming the slot, not a silent no-op. When other slots do hold
        // content the refusal names them as a statement of fact, because the usual cause is a
        // capture into a named slot followed by a paste that omitted the same name.
        if (!buffers.TryPaste(slot, out var captured) || captured is null)
        {
            var populated = buffers.PopulatedSlots();
            var message = populated.Count == 0
                ? "The buffer '" + slot + "' is empty."
                : "The buffer '" + slot + "' is empty. These slots hold captured lines: '"
                  + string.Join("', '", populated)
                  + "'.";

            return ToolResult.Denied(DenialReason.TargetNotFound, message);
        }

        return await PastePermittedFileAsync(realPath, atLine, slot, captured, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Inserts captured text into a permitted file at the requested line, or appends it.
    /// </summary>
    /// <param name="realPath">The real location the policy permitted.</param>
    /// <param name="atLine">The 1-based line to insert before, or null to append.</param>
    /// <param name="slot">The buffer slot the text came from, named in the confirmation.</param>
    /// <param name="captured">The captured text to insert.</param>
    /// <param name="cancellationToken">A token that cancels the edit.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> PastePermittedFileAsync(
        string realPath,
        int? atLine,
        string slot,
        string captured,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = await System.IO.File.ReadAllTextAsync(realPath, cancellationToken)
                .ConfigureAwait(false);

            var lines = TextLines.Split(text);
            var total = lines.Count;

            // Omitted or past-the-end atLine appends; otherwise insert before the named line. The
            // offset is the exact character boundary, so the captured slice restores unchanged.
            var line = atLine ?? total + 1;
            var offset = line > total
                ? text.Length
                : TextLines.OffsetOfLine(lines, line);

            var updated = string.Concat(text.AsSpan(0, offset), captured, text.AsSpan(offset));
            await System.IO.File.WriteAllTextAsync(realPath, updated, cancellationToken)
                .ConfigureAwait(false);

            var pastedCount = TextLines.Split(captured).Count;
            var where = line > total
                ? "the end of the file"
                : "line " + line.ToString(CultureInfo.InvariantCulture);

            return ToolResult.Text(
                "Pasted " + pastedCount.ToString(CultureInfo.InvariantCulture)
                + " lines from buffer '" + slot + "' at " + where + ".");
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
