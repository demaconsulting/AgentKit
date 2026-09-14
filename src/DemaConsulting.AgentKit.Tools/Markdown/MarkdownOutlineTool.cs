using System.ComponentModel;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Markdown;

/// <summary>
///     The <c>markdown_outline</c> tool: reports the heading structure of a Markdown file the access
///     policy permits the agent to read, as a list of sections with their line ranges.
/// </summary>
/// <remarks>
///     <para>
///     <b>The outline composes into the text tools rather than duplicating them.</b> Each section
///     reports its heading level, its title, and the 1-based start and end line of the section it
///     heads. Those line numbers feed straight into <see cref="Core.PathPolicy"/>-governed text tools:
///     a model reads a section's range with <c>text_file_read</c>, or removes it with
///     <c>text_file_cut_lines</c>, without this family needing a read or edit tool of its own. That
///     is the whole reason the markdown family is a single outline tool — the text family already
///     reads and edits by line, so the markdown family only has to describe <em>where</em> the
///     sections are.
///     </para>
///     <para>
///     <b>A section spans from its heading to the line before the next heading of the same or a
///     higher level.</b> A level-two heading's section therefore includes every deeper subsection
///     beneath it and ends where the next level-two (or level-one) heading begins, or at the end of
///     the file. <c>maxDepth</c> filters which heading levels are reported without changing where a
///     section ends, so a shallow outline still describes whole sections.
///     </para>
///     <para>
///     <b>Only real ATX headings count.</b> A run of one to six leading hash characters followed by a
///     space is a heading; a hash inside a fenced code block, or a line indented four or more spaces
///     (an indented code block), is content and is skipped. This is the same fence-aware, indent-aware
///     reading a Markdown renderer applies, so the outline matches what a person sees.
///     </para>
///     <para>
///     <b>A large document is outlined by streaming its headings, not refused.</b> The file is
///     read line by line so only the small heading list and a running line count are held, never
///     the whole document; <see cref="ToolLimits.MaxReadBytes"/> therefore does not gate this
///     tool, so a document far larger than the read ceiling is still navigable. The tool consults
///     <see cref="PathPolicy.TryResolveRead"/> and bounds only the serialized outline by
///     <see cref="ToolLimits.MaxResultCharacters"/>, refusing an oversized outline with the
///     ceiling named rather than truncating it. The delegate is declared to
///     return <see cref="object"/> deliberately — see the remarks on
///     <see cref="GuardedToolFactory"/> — and every refusal is returned rather than thrown.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class MarkdownOutlineTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "markdown_outline";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Reports the heading structure of a Markdown file the agent may read, as sections carrying a "
        + "level, a title, and the 1-based start and end line of each section. Those line ranges feed "
        + "text_file_read and text_file_cut_lines. Optionally limit the reported heading depth with "
        + "maxDepth. Returns a structured outline, or a denial explaining why the request was "
        + "refused.";

    /// <summary>
    ///     The greatest ATX heading level Markdown defines.
    /// </summary>
    private const int MaxHeadingLevel = 6;

    /// <summary>
    ///     The greatest number of leading spaces an ATX heading may carry and still be a heading.
    /// </summary>
    /// <remarks>
    ///     Four or more leading spaces begin an indented code block in CommonMark, so a line that
    ///     starts that far in is content, not a heading, however many hashes follow.
    /// </remarks>
    private const int MaxHeadingIndent = 3;

    /// <summary>
    ///     The character introducing an ATX heading.
    /// </summary>
    private const char HeadingMarker = '#';

    /// <summary>
    ///     Creates the <c>markdown_outline</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="MarkdownPack"/>. The policy is captured by the
    ///     returned tool's delegate, so the tool cannot later be pointed at a different policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every outline this tool produces.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy)
    {
        // A missing policy is a programming error in the composing application.
        ArgumentNullException.ThrowIfNull(policy);

        // Declared to return object on purpose; the guard applies identically to a synchronous tool.
        var outline = (
                [Description(
                    "The path of the Markdown file to outline, relative to the workspace root, "
                    + "for example 'guide.md'.")]
                string? path = null,
                [Description(
                    "The greatest heading level to report, from 1 to 6. Optional: omit it to report "
                    + "every level.")]
                int? maxDepth = null) =>
            Outline(policy, path, maxDepth);

        return GuardedToolFactory.Create(outline, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Produces the outline, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <param name="policy">The access policy governing the request.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="maxDepth">The greatest heading level to report, or null for every level.</param>
    /// <returns>The structured outline, or a refusal naming its reason.</returns>
    private static object Outline(PathPolicy policy, string? path, int? maxDepth)
    {
        // Malformed requests are refused rather than thrown; the model supplied them.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "A file path is required. Supply the path of a Markdown file relative to the "
                + "workspace root, for example 'guide.md'.");
        }

        if (maxDepth is < 1 or > MaxHeadingLevel)
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "The maxDepth must be between 1 and 6, or omitted to report every level.");
        }

        // The single read decision. Resolution and containment both happen inside the policy, so a
        // path outside the permitted location, or one a deny pattern excludes, is refused here
        // without this tool making any path judgment of its own.
        if (!policy.TryResolveRead(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // A directory is not a Markdown file; a missing file is reported rather than assumed.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "The requested path is a directory, not a Markdown file.");
        }

        if (!System.IO.File.Exists(realPath))
        {
            return ToolResult.Denied(
                DenialReason.TargetNotFound,
                "The requested Markdown file does not exist.");
        }

        return ReadOutline(policy, path, realPath, maxDepth);
    }

    /// <summary>
    ///     Reads a permitted Markdown file, bounding only the outline result, and returns its outline.
    /// </summary>
    /// <param name="policy">The access policy whose result ceiling and dialect apply.</param>
    /// <param name="requested">The path the model requested, used to mirror its dialect.</param>
    /// <param name="realPath">The resolved real location of the permitted file.</param>
    /// <param name="maxDepth">The greatest heading level to report, or null for every level.</param>
    /// <returns>The structured outline, or a refusal naming the ceiling it exceeded.</returns>
    private static object ReadOutline(
        PathPolicy policy,
        string requested,
        string realPath,
        int? maxDepth)
    {
        List<Heading> headings;
        int totalLines;
        try
        {
            // The document is streamed line by line so a file larger than the read ceiling is still
            // outlined: only the heading list and a running line count are held, never the whole
            // file. MaxReadBytes therefore does not gate this tool.
            using var reader = new StreamReader(
                new FileStream(realPath, FileMode.Open, FileAccess.Read, FileShare.Read),
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            headings = ParseHeadings(reader, out totalLines);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "The requested Markdown file could not be read.");
        }

        var sections = BuildSections(headings, totalLines, maxDepth);

        var reportedPath = policy.EmitRelative(realPath, requested)
            ? ToForwardSlash(Path.GetRelativePath(policy.WorkingDirectory, realPath))
            : ToForwardSlash(realPath);

        var result = new
        {
            path = reportedPath,
            sectionCount = sections.Count,
            sections = sections
                .Select(section => new
                {
                    level = section.Level,
                    title = section.Title,
                    startLine = section.StartLine,
                    endLine = section.EndLine,
                })
                .ToList(),
        };

        // The result ceiling bounds an outline exactly as it bounds a file's text.
        var serialized = JsonSerializer.Serialize(result);
        if (serialized.Length > policy.Limits.MaxResultCharacters)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The outline exceeds the "
                + policy.Limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture)
                + "-character result limit.");
        }

        return ToolResult.Structured(result);
    }

    /// <summary>
    ///     Builds the reported sections, computing each section's end line and applying the depth
    ///     filter.
    /// </summary>
    /// <remarks>
    ///     A section ends at the line before the next heading of the same or a higher level, computed
    ///     against every heading so that a shallow outline still describes whole sections; the depth
    ///     filter is applied only to which sections are emitted.
    /// </remarks>
    /// <param name="headings">The parsed headings, in document order.</param>
    /// <param name="totalLines">The file's total line count, used for the last section's end.</param>
    /// <param name="maxDepth">The greatest heading level to report, or null for every level.</param>
    /// <returns>The reported sections.</returns>
    private static List<Section> BuildSections(
        List<Heading> headings,
        int totalLines,
        int? maxDepth)
    {
        var sections = new List<Section>();
        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];
            if (maxDepth is { } depth && heading.Level > depth)
            {
                continue;
            }

            // The section ends just before the next heading of the same or a higher level, or at the
            // end of the file when there is none.
            var endLine = totalLines;
            for (var next = index + 1; next < headings.Count; next++)
            {
                if (headings[next].Level <= heading.Level)
                {
                    endLine = headings[next].Line - 1;
                    break;
                }
            }

            sections.Add(new Section(heading.Level, heading.Title, heading.Line, endLine));
        }

        return sections;
    }

    /// <summary>
    ///     Parses the ATX headings out of streamed Markdown text, with 1-based line numbers.
    /// </summary>
    /// <remarks>
    ///     Only ATX headings outside fenced code blocks are recognized, and a line indented four or
    ///     more spaces is content. The text is streamed line by line so a document larger than the
    ///     read ceiling is still outlined; only the heading list and the running line count are held.
    ///     The total line count is reported alongside so the last section's end line can reach the
    ///     end of the file.
    /// </remarks>
    /// <param name="reader">The reader over the Markdown text to scan.</param>
    /// <param name="totalLines">On return, the file's total line count.</param>
    /// <returns>The headings in document order.</returns>
    private static List<Heading> ParseHeadings(TextReader reader, out int totalLines)
    {
        var headings = new List<Heading>();
        totalLines = 0;

        var inFence = false;
        foreach (var line in TextLines.EnumerateLines(reader))
        {
            totalLines++;
            var rawLine = TextLines.Content(line);
            var trimmed = rawLine.TrimStart();

            // A fence toggles a code block within which a leading hash is content, not a heading.
            if (trimmed.StartsWith("```", StringComparison.Ordinal)
                || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                continue;
            }

            // An indented code block (four or more leading spaces) is content, never a heading.
            var indent = rawLine.Length - trimmed.Length;
            if (indent > MaxHeadingIndent)
            {
                continue;
            }

            if (TryReadHeading(trimmed, out var level, out var title))
            {
                headings.Add(new Heading(level, title, totalLines));
            }
        }

        return headings;
    }

    /// <summary>
    ///     Reads one ATX heading from an already-left-trimmed line.
    /// </summary>
    /// <param name="line">The left-trimmed line to inspect.</param>
    /// <param name="level">On success, the heading level; otherwise zero.</param>
    /// <param name="title">On success, the heading title; otherwise empty.</param>
    /// <returns>
    ///     <see langword="true"/> when the line is an ATX heading; otherwise <see langword="false"/>.
    /// </returns>
    private static bool TryReadHeading(string line, out int level, out string title)
    {
        level = 0;
        title = string.Empty;

        var hashes = 0;
        while (hashes < line.Length && line[hashes] == HeadingMarker)
        {
            hashes++;
        }

        // One to six hashes, and the run must end the line or be followed by a space.
        if (hashes is < 1 or > MaxHeadingLevel)
        {
            return false;
        }

        if (hashes < line.Length && line[hashes] != ' ')
        {
            return false;
        }

        level = hashes;
        title = line[hashes..].Trim().TrimEnd(HeadingMarker).Trim();
        return true;
    }

    /// <summary>
    ///     Normalizes a path's separators to a forward slash on every platform.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The path with every separator rendered as a forward slash.</returns>
    private static string ToForwardSlash(string path)
    {
        return path
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
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

    /// <summary>
    ///     One parsed ATX heading: its level, title, and 1-based line number.
    /// </summary>
    /// <param name="Level">The heading level, its hash count.</param>
    /// <param name="Title">The heading title.</param>
    /// <param name="Line">The 1-based line the heading is on.</param>
    private readonly record struct Heading(int Level, string Title, int Line);

    /// <summary>
    ///     One reported section: its heading level, title, and 1-based line range.
    /// </summary>
    /// <param name="Level">The heading level of the section.</param>
    /// <param name="Title">The heading title of the section.</param>
    /// <param name="StartLine">The 1-based line the section's heading is on.</param>
    /// <param name="EndLine">The 1-based last line of the section.</param>
    private readonly record struct Section(int Level, string Title, int StartLine, int EndLine);
}
