using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.CustomTools;

/// <summary>
///     A custom, author-written guarded tool that lists the section headings of a Markdown file
///     together with their line numbers.
/// </summary>
/// <remarks>
///     <para>
///     This tool is the sample's demonstration of an <b>application author writing a path-taking
///     tool of their own</b>. It deliberately mirrors, step for step, what a shipped tool does:
///     it is constructed through <see cref="GuardedToolFactory"/> (the only supported construction
///     path), it routes every path through <see cref="PathPolicy"/> for containment rather than
///     touching the file system on its own terms, it refuses with <see cref="ToolResult.Denied"/>
///     rather than throwing, and it returns its findings through <see cref="ToolResult.Structured"/>
///     so the shape is machine-readable rather than prose a model has to re-parse.
///     </para>
///     <para>
///     <b>It reports file locations in the same path dialect the shipped tools use, through the
///     public <see cref="PathPolicy"/> helpers.</b> A third-party tool that reported a path in its
///     own dialect would contradict the built-in tools in the same conversation. So this tool
///     consumes exactly the same extensibility contract: <see cref="PathPolicy.IsDiscoveryRequest"/>
///     recognizes a no-argument request, <see cref="PathPolicy.DiscoveryRoots"/> lists the locations
///     it could search, <see cref="PathPolicy.WorkingDirectoryIsGranted"/> says whether relative
///     addressing is meaningful at all, and <see cref="PathPolicy.EmitRelative"/> mirrors the
///     caller's dialect when a result path is reported. These four members are public precisely so a
///     tool outside the library can stay consistent with the ones inside it.
///     </para>
///     <para>
///     The delegate is declared to return <see cref="object"/> because the result is a union — a
///     refusal, or a structured listing — and <see cref="object"/> is the only type expressing it;
///     see the remarks on <see cref="GuardedToolFactory"/>.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads; a
///     constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class MarkdownSectionsTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     Published as a constant so the pack that claims the family prefix and any test can name
    ///     the tool without repeating a string literal that could drift from the name registered.
    ///     It carries the <c>markdown</c> family prefix, which <see cref="ToolPackBuilder.Build"/>
    ///     verifies against <see cref="MarkdownToolPack"/>.
    /// </remarks>
    public const string ToolName = "markdown_sections";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Lists the section headings of a Markdown file with their 1-based line numbers. Supply the "
        + "path of a Markdown file, relative to the workspace or absolute. Omit the path to discover "
        + "which locations may be searched. Returns a structured listing, or a denial explaining why "
        + "the request was refused.";

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
    ///     The separator reported paths are normalized to on every platform.
    /// </summary>
    /// <remarks>
    ///     A forward slash so an otherwise identical result does not differ between a developer
    ///     machine and a build agent, matching what the shipped tools report.
    /// </remarks>
    private const char ReportedSeparator = '/';

    /// <summary>
    ///     Creates the <c>markdown_sections</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     The policy is captured by the returned tool's delegate, so the tool cannot later be
    ///     pointed at a different policy. The path parameter carries a default so that a model
    ///     omitting it reaches the tool body — where it is answered as a discovery request — rather
    ///     than failing inside the function factory with an error the model cannot interpret.
    /// </remarks>
    /// <param name="policy">The access policy governing every file this tool inspects.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    public static AIFunction Create(PathPolicy policy)
    {
        // A missing policy is a programming error in the composing application, not something a
        // model supplied, so it is surfaced rather than converted into a denial.
        ArgumentNullException.ThrowIfNull(policy);

        // Declared to return object on purpose; the guard applies identically to a synchronous
        // tool. The path carries a default so an omitted argument becomes a discovery request this
        // tool answers, rather than one the function factory rejects before the body is reached.
        var sections = (
                [Description(
                    "The path of the Markdown file to inspect, relative to the workspace or an "
                    + "absolute path, for example 'sample.md'. Omit it to discover the locations "
                    + "that may be searched.")]
                string? path = null) =>
            Sections(policy, path);

        return GuardedToolFactory.Create(sections, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Lists a Markdown file's headings, refusing rather than throwing whenever the request
    ///     cannot be honored.
    /// </summary>
    /// <remarks>
    ///     The order of the checks is the contract, exactly as it is for the shipped read tool: the
    ///     policy decision comes before anything is learned about the file, so a refused path never
    ///     reveals whether it exists; the read ceiling is checked before the file is opened; and the
    ///     return ceiling is checked before any structured result is handed back.
    /// </remarks>
    /// <param name="policy">The access policy governing the request.</param>
    /// <param name="path">The path the model requested, or null for a discovery request.</param>
    /// <returns>The structured listing, or a refusal naming its reason.</returns>
    private static object Sections(PathPolicy policy, string? path)
    {
        // A no-argument request is discovery: report where the tool may look and how those
        // locations are addressed, exactly as the shipped list tool treats an omitted directory.
        if (PathPolicy.IsDiscoveryRequest(path))
        {
            return Discover(policy);
        }

        // The single read decision. Resolution happens inside the policy, so a path escaping the
        // permitted location through a link is refused here without this tool knowing links exist.
        if (!policy.TryResolveRead(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // A directory is not a Markdown file; refuse plainly rather than enumerating it.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "The requested path is a directory, not a Markdown file.");
        }

        // A missing file is a target-not-found refusal, not an exception: the model can list to
        // discover the name it should have asked for.
        if (!File.Exists(realPath))
        {
            return ToolResult.Denied(
                DenialReason.TargetNotFound,
                "The requested Markdown file does not exist.");
        }

        return ReadSections(policy, path, realPath);
    }

    /// <summary>
    ///     Answers a discovery request: the locations this tool may search and how they are
    ///     addressed.
    /// </summary>
    /// <remarks>
    ///     This is where a third-party tool consumes <see cref="PathPolicy.DiscoveryRoots"/> and
    ///     <see cref="PathPolicy.WorkingDirectoryIsGranted"/> up front — before it has any result
    ///     path to test — to describe its own reach in the same terms the shipped tools use. The
    ///     roots are reported with forward slashes to match the shipped listing dialect.
    /// </remarks>
    /// <param name="policy">The access policy whose grants define the searchable locations.</param>
    /// <returns>A structured description of the searchable locations.</returns>
    private static object Discover(PathPolicy policy)
    {
        var roots = policy.DiscoveryRoots()
            .Select(ToForwardSlash)
            .OrderBy(root => root, StringComparer.Ordinal)
            .ToList();

        return ToolResult.Structured(new
        {
            discovery = true,

            // WorkingDirectoryIsGranted decides whether a relative name refers to a location the
            // model can actually reach, which is exactly what shapes the dialect the tool reports.
            relativeAddressing = policy.WorkingDirectoryIsGranted,
            workingDirectory = ToForwardSlash(policy.WorkingDirectory),
            searchableLocations = roots,
        });
    }

    /// <summary>
    ///     Reads a permitted Markdown file, observing the read ceiling, and returns its headings.
    /// </summary>
    /// <remarks>
    ///     Two ceilings apply and both are honored, matching the shipped read tool: the read ceiling
    ///     bounds what is taken from the file system, and the tighter result ceiling bounds what is
    ///     returned to the model. A file access failure becomes an ordinary refusal rather than a
    ///     thrown exception, because the model supplied the request.
    /// </remarks>
    /// <param name="policy">The access policy whose ceilings apply.</param>
    /// <param name="requested">The path the model requested, used to mirror its dialect.</param>
    /// <param name="realPath">The resolved real location of the permitted file.</param>
    /// <returns>The structured listing, or a refusal naming the ceiling it exceeded.</returns>
    private static object ReadSections(PathPolicy policy, string? requested, string realPath)
    {
        string text;
        try
        {
            // Size is judged before the file is opened, so an oversized file is never read into
            // memory merely to discover it was oversized.
            var length = new FileInfo(realPath).Length;
            if (length > policy.Limits.MaxReadBytes)
            {
                return ToolResult.Denied(
                    DenialReason.ResourceTooLarge,
                    "The file exceeds the "
                    + policy.Limits.MaxReadBytes.ToString(CultureInfo.InvariantCulture)
                    + "-byte read limit.");
            }

            text = File.ReadAllText(realPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ToolResult.Denied(
                DenialReason.TargetNotFound,
                "The requested Markdown file could not be read.");
        }

        var headings = ParseHeadings(text);

        // Mirror the caller's dialect when reporting where the file is: a relative name only when
        // EmitRelative agrees it truthfully names a reachable location, otherwise the absolute path.
        var reportedPath = policy.EmitRelative(realPath, requested)
            ? ToForwardSlash(Path.GetRelativePath(policy.WorkingDirectory, realPath))
            : ToForwardSlash(realPath);

        var result = new
        {
            path = reportedPath,
            sectionCount = headings.Count,
            sections = headings
                .Select(heading => new
                {
                    level = heading.Level,
                    title = heading.Title,
                    line = heading.Line,
                })
                .ToList(),
        };

        // The return budget applies to a structured listing exactly as it applies to a file's text:
        // a huge table of contents would crowd out the conversation that has to follow it.
        var serialized = JsonSerializer.Serialize(result);
        if (serialized.Length > policy.Limits.MaxResultCharacters)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The section listing exceeds the "
                + policy.Limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture)
                + "-character result limit.");
        }

        return ToolResult.Structured(result);
    }

    /// <summary>
    ///     Parses the ATX headings out of Markdown text, with 1-based line numbers.
    /// </summary>
    /// <remarks>
    ///     Only ATX headings (a run of one to six leading hash characters) are recognized, and only
    ///     outside fenced code blocks, so a hash inside a code sample is not mistaken for a heading.
    ///     A line indented four or more spaces is content — an indented code block — and is skipped.
    /// </remarks>
    /// <param name="text">The Markdown text to scan.</param>
    /// <returns>The headings in document order.</returns>
    private static List<(int Level, string Title, int Line)> ParseHeadings(string text)
    {
        var headings = new List<(int Level, string Title, int Line)>();

        // A fence toggles a code block within which a leading hash is content, not a heading.
        var inFence = false;
        var lineNumber = 0;

        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            lineNumber++;

            var trimmed = rawLine.TrimStart();
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
                headings.Add((level, title, lineNumber));
            }
        }

        return headings;
    }

    /// <summary>
    ///     Reads one ATX heading from an already-left-trimmed line.
    /// </summary>
    /// <remarks>
    ///     A conforming ATX heading is one to six hashes followed by a space (or end of line), then
    ///     the title, with any trailing run of hashes removed. A run of seven or more hashes, or a
    ///     run not followed by a space, is not a heading.
    /// </remarks>
    /// <param name="line">The left-trimmed line to inspect.</param>
    /// <param name="level">On success, the heading level (its hash count); otherwise zero.</param>
    /// <param name="title">On success, the heading title; otherwise empty.</param>
    /// <returns><see langword="true"/> when the line is an ATX heading; otherwise <see langword="false"/>.</returns>
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
            .Replace(Path.DirectorySeparatorChar, ReportedSeparator)
            .Replace(Path.AltDirectorySeparatorChar, ReportedSeparator);
    }
}
