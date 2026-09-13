using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The <c>text_file_replace</c> tool: replaces one exact, unique occurrence of existing text in
///     a permitted file with new text.
/// </summary>
/// <remarks>
///     <para>
///     <b>This is the family's only general editor, and it edits by content, not by line number.</b>
///     A model states the exact text it wants gone and the exact text to put in its place, and the
///     tool makes that one substitution. Editing by matching content rather than by addressing a line
///     is what lets a model change one line of a file without rewriting the whole file and without
///     first learning the file's current line numbering.
///     </para>
///     <para>
///     <b>The old text must match exactly once.</b> A match that is not found, and a match that
///     appears more than once, are both refused — and the refusal says which case occurred. A
///     not-found refusal tells the model its text does not appear; an appears-N-times refusal names
///     the count and instructs the model to include more surrounding lines, because widening the
///     matched text is exactly how a model makes an ambiguous edit unique. The spike found this
///     guidance was enough for a model to recover on its own, and also found that models include
///     enclosing context unprompted often enough that the ambiguous case rarely arises.
///     </para>
///     <para>
///     <b>Insertion and deletion are the same operation seen from two sides.</b> To insert, a model
///     supplies old text and new text that both contain the surrounding lines, differing only by the
///     inserted material; to delete, it supplies the text to remove as the old text and an empty
///     string as the new text. There is deliberately no separate insert or delete editor: one
///     match-exactly-once substitution expresses all three.
///     </para>
///     <para>
///     <b>The old text and the new text are the file's raw content.</b> They do not include the line
///     numbers or the <c>| </c> delimiters that <see cref="TextFileReadTool"/> prints, because those
///     are display furniture the read tool adds and are not in the file.
///     </para>
///     <para>
///     This tool consults <see cref="PathPolicy.TryResolveWrite"/> and nothing else. The delegate is
///     declared to return <c>Task&lt;object&gt;</c> deliberately — see the remarks on
///     <see cref="GuardedToolFactory"/> — and every refusal is returned rather than thrown. On
///     success the confirmation reports the change in the file's line count.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class TextFileReplaceTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "text_file_replace";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Replaces one exact occurrence of existing text in a permitted file with new text. The old "
        + "text must appear exactly once; include surrounding lines to make it unique. Use an empty "
        + "new text to delete the matched text. The old and new text are the file's raw content and "
        + "must not include the line-number prefixes text_file_read shows. Returns a confirmation, "
        + "or a denial explaining why the request was refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    private const string PathRequired =
        "A file path is required. Supply a path relative to the workspace root, "
        + "for example 'notes.txt'.";

    /// <summary>
    ///     The refusal used when the request carries no old text at all.
    /// </summary>
    private const string OldTextRequired =
        "The text to replace is required. Supply the exact existing text to find, including enough "
        + "surrounding lines to make it unique.";

    /// <summary>
    ///     The refusal used when the request carries no new text at all.
    /// </summary>
    /// <remarks>
    ///     Absent new text is a malformed request; empty new text is not, because an empty
    ///     replacement is how a deletion is expressed.
    /// </remarks>
    private const string NewTextRequired =
        "The replacement text is required. Supply the text to insert, which may be an empty string "
        + "to delete the matched text.";

    /// <summary>
    ///     The refusal used when the request names a directory rather than a file.
    /// </summary>
    private const string PathIsDirectory =
        "The requested path is a directory, not a file. Name the file to edit.";

    /// <summary>
    ///     The refusal used when the requested file does not exist.
    /// </summary>
    private const string FileNotFound = "The requested file does not exist.";

    /// <summary>
    ///     The refusal used when the old text does not appear in the file.
    /// </summary>
    private const string NotFound = "The text to replace was not found in the file.";

    /// <summary>
    ///     The refusal used when a permitted file cannot be edited for a file-system reason.
    /// </summary>
    private const string FileUneditable = "The requested file could not be edited.";

    /// <summary>
    ///     Creates the <c>text_file_replace</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="TextFilePack"/>. The policy is captured by the
    ///     returned tool's delegate, so the tool cannot later be pointed at a different policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every edit this tool performs.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy)
    {
        // A missing policy is a programming error in the composing application.
        ArgumentNullException.ThrowIfNull(policy);

        // Declared to return Task<object> on purpose; see the type remarks before changing this.
        // Every parameter carries a default so that an omitted argument becomes a refusal this tool
        // composes, rather than a framework error raised before the body is reached.
        var replace = (
                [Description(
                    "The path of the file to edit, relative to the workspace root, "
                    + "for example 'notes.txt'.")]
                string? path = null,
                [Description(
                    "The exact existing text to replace. Must appear exactly once; include "
                    + "surrounding lines to make it unique.")]
                string? oldText = null,
                [Description(
                    "The text to insert in its place. Use an empty string to delete the matched "
                    + "text.")]
                string? newText = null,
                CancellationToken cancellationToken = default) =>
            ReplaceAsync(policy, path, oldText, newText, cancellationToken);

        return GuardedToolFactory.Create(replace, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Replaces one occurrence, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <param name="policy">The access policy governing the edit.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="oldText">The exact text to replace, or null when it supplied none.</param>
    /// <param name="newText">The replacement text, or null when it supplied none.</param>
    /// <param name="cancellationToken">A token that cancels the edit.</param>
    /// <returns>A confirmation naming the line-count change, or a refusal naming its reason.</returns>
    private static async Task<object> ReplaceAsync(
        PathPolicy policy,
        string? path,
        string? oldText,
        string? newText,
        CancellationToken cancellationToken)
    {
        // Malformed requests are refused rather than thrown; the model supplied them.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        if (string.IsNullOrEmpty(oldText))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, OldTextRequired);
        }

        if (newText is null)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, NewTextRequired);
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

        return await ReplacePermittedFileAsync(realPath, oldText, newText, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Edits a permitted file, enforcing the match-exactly-once contract.
    /// </summary>
    /// <remarks>
    ///     The count of occurrences decides the outcome: zero is a not-found refusal, more than one
    ///     is an ambiguity refusal that names the count and asks for more surrounding lines, and
    ///     exactly one is the substitution. The occurrences are counted before any write, so an
    ///     ambiguous edit changes nothing.
    /// </remarks>
    /// <param name="realPath">The real location the policy permitted.</param>
    /// <param name="oldText">The exact text to replace.</param>
    /// <param name="newText">The replacement text.</param>
    /// <param name="cancellationToken">A token that cancels the edit.</param>
    /// <returns>A confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> ReplacePermittedFileAsync(
        string realPath,
        string oldText,
        string newText,
        CancellationToken cancellationToken)
    {
        try
        {
            var text = await System.IO.File.ReadAllTextAsync(realPath, cancellationToken)
                .ConfigureAwait(false);

            var occurrences = CountOccurrences(text, oldText);

            // Not found: the model's text does not appear in the file.
            if (occurrences == 0)
            {
                return ToolResult.Denied(DenialReason.InvalidRequest, NotFound);
            }

            // Ambiguous: name the count and tell the model to widen the match with more surrounding
            // lines, which the spike found was enough for a model to recover on its own.
            if (occurrences > 1)
            {
                return ToolResult.Denied(
                    DenialReason.InvalidRequest,
                    "The text to replace appears "
                    + occurrences.ToString(CultureInfo.InvariantCulture)
                    + " times in the file, so the edit is ambiguous. Include more surrounding lines "
                    + "in the text to replace so that it matches exactly one place.");
            }

            var index = text.IndexOf(oldText, StringComparison.Ordinal);
            var updated = string.Concat(text.AsSpan(0, index), newText, text.AsSpan(index + oldText.Length));

            var before = TextLines.Split(text).Count;
            var after = TextLines.Split(updated).Count;

            await System.IO.File.WriteAllTextAsync(realPath, updated, cancellationToken)
                .ConfigureAwait(false);

            return ToolResult.Text(ReportDelta(before, after));
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, FileUneditable);
        }
    }

    /// <summary>
    ///     Composes the success confirmation naming the change in the file's line count.
    /// </summary>
    /// <param name="before">The file's line count before the edit.</param>
    /// <param name="after">The file's line count after the edit.</param>
    /// <returns>The confirmation text.</returns>
    private static string ReportDelta(int before, int after)
    {
        var delta = after - before;
        var sign = delta >= 0 ? "+" : "-";

        return "Replaced 1 occurrence. The file changed from "
            + before.ToString(CultureInfo.InvariantCulture) + " to "
            + after.ToString(CultureInfo.InvariantCulture) + " lines ("
            + sign + Math.Abs(delta).ToString(CultureInfo.InvariantCulture) + ").";
    }

    /// <summary>
    ///     Counts the non-overlapping ordinal occurrences of a substring in text.
    /// </summary>
    /// <remarks>
    ///     Counted ordinally, because the match is against the file's raw bytes-as-text and must not
    ///     be softened by culture-sensitive comparison. Non-overlapping, because a replacement
    ///     consumes the text it matched.
    /// </remarks>
    /// <param name="text">The text to search.</param>
    /// <param name="value">The substring to count.</param>
    /// <returns>The number of non-overlapping occurrences.</returns>
    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
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
