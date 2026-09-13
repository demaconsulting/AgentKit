using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.CustomTools;

/// <summary>
///     A custom, author-written guarded tool that reports the word, line and character counts of a
///     text file.
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
///     <b>It counts document statistics rather than re-implementing a shipped tool.</b> The library
///     ships a <c>markdown_outline</c> tool of its own, so a sample that also reported Markdown
///     headings would merely duplicate it — and, worse, would collide with the shipped
///     <c>markdown</c> family prefix. A word-count tool under its own <c>docstats</c> family teaches
///     the identical extensibility contract without either problem: it is genuinely a tool the
///     library does not provide.
///     </para>
///     <para>
///     <b>It reports file locations in the same path dialect the shipped tools use, through the
///     public <see cref="PathPolicy"/> helpers.</b> A third-party tool that reported a path in its
///     own dialect would contradict the built-in tools in the same conversation. So this tool
///     consumes exactly the same extensibility contract: <see cref="PathPolicy.IsDiscoveryRequest"/>
///     recognizes a no-argument request, <see cref="PathPolicy.DiscoveryRoots"/> lists the locations
///     it could inspect, <see cref="PathPolicy.WorkingDirectoryIsGranted"/> says whether relative
///     addressing is meaningful at all, and <see cref="PathPolicy.EmitRelative"/> mirrors the
///     caller's dialect when a result path is reported. These four members are public precisely so a
///     tool outside the library can stay consistent with the ones inside it.
///     </para>
///     <para>
///     The delegate is declared to return <see cref="object"/> because the result is a union — a
///     refusal, or a structured count — and <see cref="object"/> is the only type expressing it;
///     see the remarks on <see cref="GuardedToolFactory"/>.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads; a
///     constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class DocStatsWordCountTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     Published as a constant so the pack that claims the family prefix and any test can name
    ///     the tool without repeating a string literal that could drift from the name registered.
    ///     It carries the <c>docstats</c> family prefix, which <see cref="ToolPackBuilder.Build"/>
    ///     verifies against <see cref="DocStatsToolPack"/>.
    /// </remarks>
    public const string ToolName = "docstats_wordcount";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Counts the words, lines and characters of a text file. Supply the path of a file, relative "
        + "to the workspace or absolute. Omit the path to discover which locations may be inspected. "
        + "Returns a structured count, or a denial explaining why the request was refused.";

    /// <summary>
    ///     The separator reported paths are normalized to on every platform.
    /// </summary>
    /// <remarks>
    ///     A forward slash so an otherwise identical result does not differ between a developer
    ///     machine and a build agent, matching what the shipped tools report.
    /// </remarks>
    private const char ReportedSeparator = '/';

    /// <summary>
    ///     Creates the <c>docstats_wordcount</c> tool governed by an access policy.
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
        var wordCount = (
                [Description(
                    "The path of the text file to count, relative to the workspace or an absolute "
                    + "path, for example 'sample.md'. Omit it to discover the locations that may be "
                    + "inspected.")]
                string? path = null) =>
            WordCount(policy, path);

        return GuardedToolFactory.Create(wordCount, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Counts a text file, refusing rather than throwing whenever the request cannot be honored.
    /// </summary>
    /// <remarks>
    ///     The order of the checks is the contract, exactly as it is for the shipped read tool: the
    ///     policy decision comes before anything is learned about the file, so a refused path never
    ///     reveals whether it exists; the read ceiling is checked before the file is opened; and the
    ///     return ceiling is checked before any structured result is handed back.
    /// </remarks>
    /// <param name="policy">The access policy governing the request.</param>
    /// <param name="path">The path the model requested, or null for a discovery request.</param>
    /// <returns>The structured count, or a refusal naming its reason.</returns>
    private static object WordCount(PathPolicy policy, string? path)
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

        // A directory has no text to count; refuse plainly rather than enumerating it.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "The requested path is a directory, not a file.");
        }

        // A missing file is a target-not-found refusal, not an exception: the model can list to
        // discover the name it should have asked for.
        if (!File.Exists(realPath))
        {
            return ToolResult.Denied(
                DenialReason.TargetNotFound,
                "The requested file does not exist.");
        }

        return ReadCounts(policy, path, realPath);
    }

    /// <summary>
    ///     Answers a discovery request: the locations this tool may inspect and how they are
    ///     addressed.
    /// </summary>
    /// <remarks>
    ///     This is where a third-party tool consumes <see cref="PathPolicy.DiscoveryRoots"/> and
    ///     <see cref="PathPolicy.WorkingDirectoryIsGranted"/> up front — before it has any result
    ///     path to test — to describe its own reach in the same terms the shipped tools use.
    /// </remarks>
    /// <param name="policy">The access policy whose grants define the inspectable locations.</param>
    /// <returns>A structured description of the inspectable locations.</returns>
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
            inspectableLocations = roots,
        });
    }

    /// <summary>
    ///     Reads a permitted file, observing the read ceiling, and returns its counts.
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
    /// <returns>The structured count, or a refusal naming the ceiling it exceeded.</returns>
    private static object ReadCounts(PathPolicy policy, string? requested, string realPath)
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
                "The requested file could not be read.");
        }

        // Mirror the caller's dialect when reporting where the file is: a relative name only when
        // EmitRelative agrees it truthfully names a reachable location, otherwise the absolute path.
        var reportedPath = policy.EmitRelative(realPath, requested)
            ? ToForwardSlash(Path.GetRelativePath(policy.WorkingDirectory, realPath))
            : ToForwardSlash(realPath);

        var result = new
        {
            path = reportedPath,
            characters = text.Length,
            words = CountWords(text),
            lines = CountLines(text),
        };

        // The return budget applies to a structured count exactly as it applies to a file's text.
        var serialized = JsonSerializer.Serialize(result);
        if (serialized.Length > policy.Limits.MaxResultCharacters)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The count exceeds the "
                + policy.Limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture)
                + "-character result limit.");
        }

        return ToolResult.Structured(result);
    }

    /// <summary>
    ///     Counts the whitespace-separated words in text.
    /// </summary>
    /// <param name="text">The text to count.</param>
    /// <returns>The number of words.</returns>
    private static int CountWords(string text)
    {
        return text.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    /// <summary>
    ///     Counts the lines in text, treating a trailing newline as ending the last line.
    /// </summary>
    /// <param name="text">The text to count.</param>
    /// <returns>The number of lines.</returns>
    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');

        // A trailing newline produces a trailing empty element that is not a line of its own.
        return normalized.EndsWith('\n') ? lines.Length - 1 : lines.Length;
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
