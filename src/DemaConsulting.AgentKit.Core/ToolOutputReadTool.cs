using System.ComponentModel;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     The <c>tool_output_read</c> tool: reads a range of lines from a large tool output the
///     composition retained instead of returning whole.
/// </summary>
/// <remarks>
///     <para>
///     <b>The tool is offered only when retention is composed.</b> It is appended by
///     <see cref="ToolOutputSpill.Apply"/> — and therefore by
///     <see cref="ToolPackBuilder.WithToolOutputSpill"/> — together with
///     <see cref="ToolOutputSearchTool"/>, and reads only the store that call allocated. An
///     application never attaches it directly.
///     </para>
///     <para>
///     <b>It cannot reach the file system.</b> Its parameters are an output identifier and a line
///     range; there is no path parameter, so no request it accepts can name a file. An identifier
///     names an output in this composition's own store or nothing at all.
///     </para>
///     <para>
///     <b>Pages are shaped exactly like <c>text_file_read</c>'s.</b> A result opens with an
///     <c>output-3 lines A-B of N</c> header naming the returned range and the output's true line
///     count, and each line is prefixed with its number and <c>| </c>, so a model already knows how
///     to read one and how to page forward. A window past the end is an honest empty result naming
///     the true total. When <c>lineCount</c> is omitted, as many whole lines as fit the result
///     ceiling are returned, and the header states where they stopped. An explicit range whose page
///     exceeds <see cref="ToolLimits.MaxResultCharacters"/> is refused with the ceiling named,
///     never truncated — and the tool's own results are never retained, so a page can never become
///     another reference.
///     </para>
///     <para>
///     The delegate is declared to return <see cref="object"/> deliberately — see the remarks on
///     <see cref="GuardedToolFactory"/> — and every refusal is returned rather than thrown.
///     </para>
///     <para>
///     The class is stateless; a constructed tool captures only the lock-guarded store and the
///     immutable limits, and is safe for concurrent use.
///     </para>
/// </remarks>
public static class ToolOutputReadTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "tool_output_read";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Reads a range of lines from a large tool output that was retained instead of returned "
        + "whole. Identify the output by the id its preview named, for example 'output-3'. The result "
        + "opens with an 'output-3 lines A-B of N' header and prefixes each line with its number. "
        + "Returns the lines, or a denial explaining why the request was refused.";

    /// <summary>
    ///     The delimiter between a line number and the line's content, matching
    ///     <c>text_file_read</c>.
    /// </summary>
    private const string LineNumberDelimiter = "| ";

    /// <summary>
    ///     Creates the <c>tool_output_read</c> tool over one composition's store.
    /// </summary>
    /// <remarks>
    ///     Internal because the store is internal: the only caller is
    ///     <see cref="ToolOutputSpill.Apply"/>, which allocates the store this tool reads.
    /// </remarks>
    /// <param name="store">The store to read from. Must not be null.</param>
    /// <param name="limits">The ceilings bounding every page. Must not be null.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="store"/> or <paramref name="limits"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(ToolOutputStore store, ToolLimits limits)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(limits);

        // Declared to return object on purpose; see the type remarks before changing this. Every
        // parameter carries a default so an omitted argument reaches the body as a request this
        // tool answers rather than a framework error raised before it.
        var read = (
                [Description("The id of the retained output, as named in its preview, for example 'output-3'.")]
                string? id = null,
                [Description("The 1-based line to begin reading at. Optional: omit it to read from the first line.")]
                int? startLine = null,
                [Description(
                    "The number of lines to read. Optional: omit it to read as many lines as fit "
                    + "within the result limit.")]
                int? lineCount = null) =>
            Read(store, limits, id, startLine, lineCount);

        return GuardedToolFactory.Create(read, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Reads one page of a retained output, refusing rather than throwing whenever the request
    ///     cannot be honored.
    /// </summary>
    /// <param name="store">The store to read from.</param>
    /// <param name="limits">The ceilings bounding the page.</param>
    /// <param name="id">The identifier the model supplied.</param>
    /// <param name="startLine">The 1-based line to begin at, or null for the first line.</param>
    /// <param name="lineCount">The number of lines to read, or null for as many as fit.</param>
    /// <returns>The numbered page, or a refusal naming its reason.</returns>
    private static object Read(
        ToolOutputStore store,
        ToolLimits limits,
        string? id,
        int? startLine,
        int? lineCount)
    {
        // A paging argument outside its domain is a malformed request, refused before the lookup.
        if (startLine is < 1 || lineCount is < 0)
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "startLine must be 1 or greater and lineCount must be 0 or greater.");
        }

        var outcome = store.Find(id, out var entry);
        if (outcome != ToolOutputLookup.Found)
        {
            return ToolOutputStore.Refusal(outcome, id);
        }

        var lines = entry!.Lines;
        var total = lines.Count;
        var start = startLine ?? 1;

        // A window that begins past the last line, or a zero-count window, is an honest empty
        // result naming the true total, not an error.
        if (start > total || lineCount == 0)
        {
            return Header(entry.Id, start, start - 1, total);
        }

        return lineCount is { } count
            ? ReadRange(entry, limits, start, (int)Math.Min((long)start + count - 1, total))
            : ReadFitting(entry, limits, start);
    }

    /// <summary>
    ///     Renders an explicit range, refusing it when the page exceeds the result ceiling.
    /// </summary>
    /// <param name="entry">The retained output.</param>
    /// <param name="limits">The ceilings bounding the page.</param>
    /// <param name="start">The 1-based first line, within the output.</param>
    /// <param name="end">The 1-based last line, within the output.</param>
    /// <returns>The numbered page, or a refusal naming the ceiling.</returns>
    private static object ReadRange(ToolOutputEntry entry, ToolLimits limits, int start, int end)
    {
        var page = Render(entry, start, end);

        // The return budget bounds a page exactly as it bounds any result, and a truncated page
        // would hide lines the model believes it read.
        if (page.Length > limits.MaxResultCharacters)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "Lines " + start.ToString(CultureInfo.InvariantCulture) + "-"
                + end.ToString(CultureInfo.InvariantCulture) + " of " + entry.Id + " are "
                + page.Length.ToString(CultureInfo.InvariantCulture) + " characters, which exceeds the "
                + limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture)
                + "-character result limit. Request fewer lines.");
        }

        return ToolResult.Text(page);
    }

    /// <summary>
    ///     Renders as many whole lines from a starting line as fit the result ceiling.
    /// </summary>
    /// <remarks>
    ///     No range end was requested, so returning fewer lines than remain is not a truncation of
    ///     anything the model asked for; the header states exactly where the page stopped.
    /// </remarks>
    /// <param name="entry">The retained output.</param>
    /// <param name="limits">The ceilings bounding the page.</param>
    /// <param name="start">The 1-based first line, within the output.</param>
    /// <returns>The numbered page, or a refusal when not even one line fits.</returns>
    private static object ReadFitting(ToolOutputEntry entry, ToolLimits limits, int start)
    {
        var lines = entry.Lines;
        var total = lines.Count;
        var width = total.ToString(CultureInfo.InvariantCulture).Length;

        // Budget against the widest header the page could carry, then take whole lines.
        var budget = limits.MaxResultCharacters - Header(entry.Id, total, total, total).Length;
        var used = 0;
        var end = start - 1;
        while (end < total)
        {
            var cost = 1 + width + LineNumberDelimiter.Length + lines[end].Length;
            if (used + cost > budget)
            {
                break;
            }

            used += cost;
            end++;
        }

        if (end < start)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "Line " + start.ToString(CultureInfo.InvariantCulture) + " of " + entry.Id
                + " does not fit within the "
                + limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture)
                + "-character result limit.");
        }

        return ToolResult.Text(Render(entry, start, end));
    }

    /// <summary>
    ///     Renders a range of lines with its header.
    /// </summary>
    /// <param name="entry">The retained output.</param>
    /// <param name="start">The 1-based first line.</param>
    /// <param name="end">The 1-based last line, at least <paramref name="start"/>.</param>
    /// <returns>The rendered page.</returns>
    private static string Render(ToolOutputEntry entry, int start, int end)
    {
        var lines = entry.Lines;
        var width = lines.Count.ToString(CultureInfo.InvariantCulture).Length;

        var builder = new StringBuilder(Header(entry.Id, start, end, lines.Count));
        for (var number = start; number <= end; number++)
        {
            builder.Append('\n')
                .Append(number.ToString(CultureInfo.InvariantCulture).PadLeft(width))
                .Append(LineNumberDelimiter)
                .Append(lines[number - 1]);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Composes the range header, in the same shape <c>text_file_read</c> uses.
    /// </summary>
    /// <param name="id">The output identifier.</param>
    /// <param name="start">The first line of the range.</param>
    /// <param name="end">The last line of the range.</param>
    /// <param name="total">The output's true line count.</param>
    /// <returns>The header.</returns>
    private static string Header(string id, int start, int end, int total) =>
        id + " lines " + start.ToString(CultureInfo.InvariantCulture)
        + "-" + end.ToString(CultureInfo.InvariantCulture)
        + " of " + total.ToString(CultureInfo.InvariantCulture);
}
