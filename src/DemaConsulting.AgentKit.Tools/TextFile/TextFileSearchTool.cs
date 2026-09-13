using System.ComponentModel;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The <c>text_file_search</c> tool: finds a pattern across the text files the access policy
///     permits the agent to read, reporting matches in a grep-style listing.
/// </summary>
/// <remarks>
///     <para>
///     <b>Search is a policy call, and this is the most important security property in the family.</b>
///     Every candidate file is drawn from <see cref="PathPolicy.EnumerateFiles"/>, which filters each
///     one through the very read decision direct access uses, so a search can never disclose the
///     content, the path, or even the <em>existence</em> of a file the policy would refuse. Searching
///     the file system directly would defeat that: recursive enumeration follows links out of a
///     permitted location, and a match reported from such a file would leak both its contents and the
///     fact that it exists. The enumeration must remain a policy call — never a
///     <see cref="Directory"/> call — and that invariant is verified by a scenario whose only match
///     lies behind a link outside the grants.
///     </para>
///     <para>
///     <b>Matches are reported grep-style.</b> A match line is <c>path:line:content</c> — the file's
///     path in the caller's dialect, the 1-based line number, and the line's content — the exact form
///     a developer reads from <c>grep</c>. Context lines requested with <c>contextLines</c> are shown
///     with a <c>-</c> separator instead of the <c>:</c>, and a <c>--</c> line separates one run of
///     matches from the next, again matching <c>grep</c>. The path and line number compose directly
///     into <see cref="TextFileReadTool"/>, so a model searches, then pages to the line it found.
///     </para>
///     <para>
///     <b>The search is literal by default.</b> Most searches are for a fixed string, and treating
///     the pattern literally is both what a model most often means and what avoids a stray regular
///     expression metacharacter matching by surprise. Setting <c>literal</c> to false interprets the
///     pattern as a regular expression instead; an invalid expression is a refused request, not a
///     thrown error. <c>ignoreCase</c> applies to either mode.
///     </para>
///     <para>
///     <b>Only text is searched, and a large file is streamed, not skipped.</b> Every candidate
///     is streamed line by line so a text file larger than <see cref="ToolLimits.MaxReadBytes"/>
///     is searched rather than silently omitted for size — no readable file is invisible to a
///     search. A file whose leading bytes are binary is skipped rather than searched into garbled
///     text, detected from a leading sniff that never materializes the whole file. The assembled
///     result is bounded by <see cref="ToolLimits.MaxResultCharacters"/>: an oversized result is
///     refused with the ceiling named, never silently
///     truncated. An empty result is a fact reported plainly, because a search that found nothing
///     is not a failed request.
///     </para>
///     <para>
///     The delegate is declared to return <c>Task&lt;object&gt;</c> deliberately — see the remarks on
///     <see cref="GuardedToolFactory"/> — and every refusal is returned rather than thrown.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class TextFileSearchTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "text_file_search";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Searches the text files the agent may read for a pattern, reporting matches grep-style as "
        + "'path:line:content'. The pattern is literal by default; set literal to false for a regular "
        + "expression. Optionally scope with a directory and a file glob, add context lines, ignore "
        + "case, and cap the number of matches. Returns the matches, or a denial explaining why the "
        + "request was refused.";

    /// <summary>
    ///     The file glob used when the request supplies none.
    /// </summary>
    private const string DefaultFilePattern = "*";

    /// <summary>
    ///     The recursion prefix a file glob may carry, honored and stripped before matching.
    /// </summary>
    private const string RecursivePrefix = "**/";

    /// <summary>
    ///     The bare recursion glob, treated as "every file, recursively".
    /// </summary>
    private const string RecursiveEverything = "**";

    /// <summary>
    ///     The result reported when the search matched nothing.
    /// </summary>
    private const string NoMatches = "No matches.";

    /// <summary>
    ///     The separator between one run of matches and the next, matching grep.
    /// </summary>
    private const string GroupSeparator = "--";

    /// <summary>
    ///     The number of bytes buffered per read while streaming a file's lines.
    /// </summary>
    private const int StreamBufferSize = 16 * 1024;

    /// <summary>
    ///     Creates the <c>text_file_search</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="TextFilePack"/>. The policy is captured by the
    ///     returned tool's delegate, so the tool cannot later be pointed at a different policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every search this tool performs.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy)
    {
        // A missing policy is a programming error in the composing application.
        ArgumentNullException.ThrowIfNull(policy);

        // Declared to return Task<object> on purpose; see the type remarks before changing this.
        var search = (
                [Description("The text or regular expression to search for.")]
                string? pattern = null,
                [Description(
                    "The directory to search, relative to the workspace or an absolute path. "
                    + "Optional: omit it to search every location the agent may read.")]
                string? path = null,
                [Description(
                    "The file-name glob to restrict the search to, for example '*.md'. "
                    + "Optional: omit it to search every file.")]
                string? filePattern = null,
                [Description(
                    "The number of context lines to show around each match. Optional: defaults "
                    + "to 0.")]
                int contextLines = 0,
                [Description("Whether to match case-insensitively. Optional: defaults to false.")]
                bool ignoreCase = false,
                [Description(
                    "Whether the pattern is a literal string rather than a regular expression. "
                    + "Optional: defaults to true.")]
                bool literal = true,
                [Description(
                    "The greatest number of matches to report. Optional: omit it for no explicit "
                    + "cap.")]
                int? maxMatches = null,
                CancellationToken cancellationToken = default) =>
            SearchAsync(
                policy,
                pattern,
                path,
                filePattern,
                contextLines,
                ignoreCase,
                literal,
                maxMatches,
                cancellationToken);

        return GuardedToolFactory.Create(search, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Searches the permitted files, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <param name="policy">The access policy governing the search.</param>
    /// <param name="pattern">The text or expression to search for.</param>
    /// <param name="path">The directory to search, or null for every permitted location.</param>
    /// <param name="filePattern">The file glob to restrict to, or null for every file.</param>
    /// <param name="contextLines">The number of context lines to show around each match.</param>
    /// <param name="ignoreCase">Whether to match case-insensitively.</param>
    /// <param name="literal">Whether the pattern is literal rather than a regular expression.</param>
    /// <param name="maxMatches">The greatest number of matches to report, or null for no cap.</param>
    /// <param name="cancellationToken">A token that cancels the search.</param>
    /// <returns>The grep-style listing, or a refusal naming its reason.</returns>
    private static async Task<object> SearchAsync(
        PathPolicy policy,
        string? pattern,
        string? path,
        string? filePattern,
        int contextLines,
        bool ignoreCase,
        bool literal,
        int? maxMatches,
        CancellationToken cancellationToken)
    {
        // Malformed requests are refused rather than thrown; the model supplied them.
        if (string.IsNullOrEmpty(pattern))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "A search pattern is required. Supply the text or regular expression to search for.");
        }

        if (contextLines < 0 || maxMatches is < 0)
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "contextLines must be 0 or greater and maxMatches must be 0 or greater.");
        }

        // A named directory that the policy refuses is reported as a refusal, so the model learns
        // where it may search rather than receiving a silent empty result.
        if (!PathPolicy.IsDiscoveryRequest(path)
            && !policy.TryResolveRead(path, out _, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // Compile the matcher. An invalid regular expression is a refused request, not a throw.
        Regex regex;
        try
        {
            regex = BuildMatcher(pattern, ignoreCase, literal);
        }
        catch (ArgumentException)
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "The search pattern is not a valid regular expression. Fix the expression, or set "
                + "literal to true to search for it as plain text.");
        }

        var searchPattern = NormalizeFilePattern(filePattern);
        var cap = maxMatches ?? int.MaxValue;

        var result = await BuildResultAsync(
            policy, path, searchPattern, regex, contextLines, cap, cancellationToken)
            .ConfigureAwait(false);

        // An empty result is a fact, not a refusal.
        if (result.Length == 0)
        {
            return ToolResult.Text(NoMatches);
        }

        // The return budget bounds a search result exactly as it bounds a file's text.
        if (result.Length > policy.Limits.MaxResultCharacters)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The search result exceeds the "
                + policy.Limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture)
                + "-character result limit.");
        }

        return ToolResult.Text(result);
    }

    /// <summary>
    ///     Assembles the grep-style result across every permitted, searchable file.
    /// </summary>
    /// <param name="policy">The access policy whose enumeration and dialect are used.</param>
    /// <param name="requested">The directory the model requested, mirroring its dialect.</param>
    /// <param name="searchPattern">The .NET file-name pattern to enumerate with.</param>
    /// <param name="regex">The compiled matcher.</param>
    /// <param name="contextLines">The number of context lines to show around each match.</param>
    /// <param name="cap">The greatest number of matches to report.</param>
    /// <param name="cancellationToken">A token that cancels the search.</param>
    /// <returns>The assembled listing, empty when nothing matched.</returns>
    private static async Task<string> BuildResultAsync(
        PathPolicy policy,
        string? requested,
        string searchPattern,
        Regex regex,
        int contextLines,
        int cap,
        CancellationToken cancellationToken)
    {
        var files = EnumerateFiles(policy, requested, searchPattern)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();

        var builder = new StringBuilder();
        var progress = new SearchProgress();

        foreach (var file in files)
        {
            if (progress.MatchesSoFar >= cap)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var reportedPath = ReportedPath(policy, requested, file);
            await AppendFileMatchesAsync(
                builder,
                policy,
                reportedPath,
                file,
                regex,
                contextLines,
                cap,
                progress,
                cancellationToken).ConfigureAwait(false);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Streams one file's matches, with any requested context lines and group separators, holding
    ///     only the matched lines and their context rather than the whole file.
    /// </summary>
    /// <remarks>
    ///     The file is streamed line by line so a file larger than the read ceiling is still
    ///     searched. A leading sniff skips a binary file before any line is matched, so a binary file
    ///     never emits partial matches. Only the matched lines and the context lines around them are
    ///     retained; the rest of the file is streamed solely to keep the line numbering and the true
    ///     total exact. A single line longer than the read ceiling is matched and rendered against a
    ///     bounded prefix so no pathological line is materialized whole.
    /// </remarks>
    /// <param name="builder">The result under construction.</param>
    /// <param name="policy">The access policy whose read ceiling bounds a single line.</param>
    /// <param name="reportedPath">The file's path in the caller's dialect.</param>
    /// <param name="realPath">The real location of the permitted file.</param>
    /// <param name="regex">The compiled matcher.</param>
    /// <param name="contextLines">The number of context lines to show around each match.</param>
    /// <param name="cap">The greatest number of matches to report.</param>
    /// <param name="progress">The running match and group counts, updated as matches are appended.</param>
    /// <param name="cancellationToken">A token that cancels the search.</param>
    /// <returns>A task that completes when the file has been searched.</returns>
    private static async Task AppendFileMatchesAsync(
        StringBuilder builder,
        PathPolicy policy,
        string reportedPath,
        string realPath,
        Regex regex,
        int contextLines,
        int cap,
        SearchProgress progress,
        CancellationToken cancellationToken)
    {
        long length;
        try
        {
            length = new FileInfo(realPath).Length;

            // A binary file is skipped before any line is matched, never searched into garbled text
            // and never surfaced; the sniff reads only a leading window.
            if (await TextFileBinaryGuard.IsBinaryAsync(realPath, length, cancellationToken)
                .ConfigureAwait(false))
            {
                return;
            }
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            // A file that cannot be reached or read is skipped: it is neither disclosed nor searched.
            return;
        }

        var maxReadBytes = policy.Limits.MaxReadBytes;
        var matchLines = new List<int>();
        var retained = new Dictionary<int, string>();
        var before = new Queue<(int Line, string Content)>();
        var pendingAfter = 0;
        var total = 0;

        try
        {
            await using var stream = new FileStream(
                realPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: StreamBufferSize,
                useAsync: true);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            foreach (var line in TextLines.EnumerateLines(reader))
            {
                cancellationToken.ThrowIfCancellationRequested();
                total++;

                // Bound a single pathological line so it is never materialized whole for matching or
                // rendering; a normal line is far shorter than the read ceiling and unaffected.
                var content = TextLines.Content(line);
                if (content.Length > maxReadBytes)
                {
                    content = content[..maxReadBytes];
                }

                // Retain a line that falls within a prior match's trailing context.
                if (pendingAfter > 0)
                {
                    retained[total] = content;
                    pendingAfter--;
                }

                if (progress.MatchesSoFar + matchLines.Count < cap && regex.IsMatch(content))
                {
                    matchLines.Add(total);
                    retained[total] = content;

                    // Pull the preceding context lines the ring buffer has been holding.
                    foreach (var (line0, content0) in before)
                    {
                        retained[line0] = content0;
                    }

                    pendingAfter = contextLines;
                }

                // Keep the most recent context lines so a later match can render lines before it.
                if (contextLines > 0)
                {
                    before.Enqueue((total, content));
                    while (before.Count > contextLines)
                    {
                        before.Dequeue();
                    }
                }
            }
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            // A read that fails partway is treated as a skip: no partial matches are emitted.
            return;
        }

        if (matchLines.Count == 0)
        {
            return;
        }

        progress.MatchesSoFar += matchLines.Count;

        // Merge each match's context window into contiguous ranges, then render each range as its
        // own group separated by a grep-style '--' line.
        foreach (var (rangeStart, rangeEnd) in MergeRanges(matchLines, contextLines, total))
        {
            if (progress.GroupsWritten > 0 && contextLines > 0)
            {
                builder.Append(GroupSeparator).Append('\n');
            }

            progress.GroupsWritten++;

            for (var line = rangeStart; line <= rangeEnd; line++)
            {
                var separator = matchLines.Contains(line) ? ':' : '-';
                builder.Append(reportedPath)
                    .Append(separator)
                    .Append(line.ToString(CultureInfo.InvariantCulture))
                    .Append(separator)
                    .Append(retained.TryGetValue(line, out var content) ? content : string.Empty)
                    .Append('\n');
            }
        }
    }

    /// <summary>
    ///     Merges each match's context window into contiguous, non-overlapping line ranges.
    /// </summary>
    /// <param name="matchLines">The 1-based match line numbers, in ascending order.</param>
    /// <param name="contextLines">The number of context lines to show around each match.</param>
    /// <param name="total">The file's total line count, used to clamp the ranges.</param>
    /// <returns>The merged inclusive ranges, in order.</returns>
    private static List<(int Start, int End)> MergeRanges(
        List<int> matchLines,
        int contextLines,
        int total)
    {
        var ranges = new List<(int Start, int End)>();
        foreach (var match in matchLines)
        {
            var start = Math.Max(1, match - contextLines);
            var end = Math.Min(total, match + contextLines);

            // Extend the current range when this window touches or overlaps it, otherwise start a
            // new one, so adjacent matches share a single group with no duplicated lines.
            if (ranges.Count > 0 && start <= ranges[^1].End + 1)
            {
                ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, end));
            }
            else
            {
                ranges.Add((start, end));
            }
        }

        return ranges;
    }

    /// <summary>
    ///     Enumerates the permitted files to search, for a named directory or for every location.
    /// </summary>
    /// <remarks>
    ///     Always through <see cref="PathPolicy.EnumerateFiles"/>, so no file the policy would refuse
    ///     is ever a search candidate.
    /// </remarks>
    /// <param name="policy">The access policy whose enumeration is used.</param>
    /// <param name="requested">The directory the model requested, or null for every location.</param>
    /// <param name="searchPattern">The .NET file-name pattern to enumerate with.</param>
    /// <returns>The permitted files to search.</returns>
    private static IEnumerable<string> EnumerateFiles(
        PathPolicy policy,
        string? requested,
        string searchPattern)
    {
        if (!PathPolicy.IsDiscoveryRequest(requested))
        {
            return policy.EnumerateFiles(requested, searchPattern);
        }

        // Discovery searches every permitted location, de-duplicated so a file two grants both cover
        // is searched once.
        return policy.DiscoveryRoots()
            .SelectMany(root => policy.EnumerateFiles(root, searchPattern))
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    ///     Reports the file's path in the caller's dialect: relative when the caller addressed the
    ///     search relatively and the policy permits, otherwise the absolute path.
    /// </summary>
    /// <param name="policy">The access policy whose dialect is mirrored.</param>
    /// <param name="requested">The directory the model requested.</param>
    /// <param name="realPath">The resolved real location of the matched file.</param>
    /// <returns>The path reported for a match.</returns>
    private static string ReportedPath(PathPolicy policy, string? requested, string realPath)
    {
        return policy.EmitRelative(realPath, requested)
            ? TextLines.ToForwardSlash(Path.GetRelativePath(policy.WorkingDirectory, realPath))
            : TextLines.ToForwardSlash(realPath);
    }

    /// <summary>
    ///     Builds the compiled matcher from the pattern and its options.
    /// </summary>
    /// <remarks>
    ///     A literal pattern is escaped so its metacharacters match themselves; a regular-expression
    ///     pattern is used as given. An invalid expression surfaces as an
    ///     <see cref="ArgumentException"/> the caller converts into a refusal.
    /// </remarks>
    /// <param name="pattern">The text or expression to search for.</param>
    /// <param name="ignoreCase">Whether to match case-insensitively.</param>
    /// <param name="literal">Whether the pattern is literal rather than a regular expression.</param>
    /// <returns>The compiled matcher.</returns>
    private static Regex BuildMatcher(string pattern, bool ignoreCase, bool literal)
    {
        var options = RegexOptions.CultureInvariant;
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        var effectivePattern = literal ? Regex.Escape(pattern) : pattern;

        // A bounded match timeout keeps a pathological regular expression from stalling the search.
        return new Regex(effectivePattern, options, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    ///     Reduces a caller's file glob to the .NET file-name pattern the policy enumeration accepts.
    /// </summary>
    /// <param name="filePattern">The file glob the caller supplied, or null.</param>
    /// <returns>The .NET file-name search pattern to enumerate with.</returns>
    private static string NormalizeFilePattern(string? filePattern)
    {
        if (string.IsNullOrWhiteSpace(filePattern))
        {
            return DefaultFilePattern;
        }

        var trimmed = filePattern.Trim();
        if (string.Equals(trimmed, RecursiveEverything, StringComparison.Ordinal))
        {
            return DefaultFilePattern;
        }

        if (trimmed.StartsWith(RecursivePrefix, StringComparison.Ordinal))
        {
            trimmed = trimmed[RecursivePrefix.Length..];
        }

        var lastSeparator = trimmed.LastIndexOfAny(['/', '\\']);
        if (lastSeparator >= 0)
        {
            trimmed = trimmed[(lastSeparator + 1)..];
        }

        return string.IsNullOrEmpty(trimmed) ? DefaultFilePattern : trimmed;
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
    ///     The running match and group counts carried across the files a single search assembles.
    /// </summary>
    /// <remarks>
    ///     A mutable holder rather than <c>ref</c> parameters because the per-file search is
    ///     asynchronous while it streams, and an async method cannot take a <c>ref</c> argument.
    /// </remarks>
    private sealed class SearchProgress
    {
        /// <summary>
        ///     The number of matches reported so far, against which the cap is enforced.
        /// </summary>
        public int MatchesSoFar { get; set; }

        /// <summary>
        ///     The number of match groups written so far, used to place group separators.
        /// </summary>
        public int GroupsWritten { get; set; }
    }
}
