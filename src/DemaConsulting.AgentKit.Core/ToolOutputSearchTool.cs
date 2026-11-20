using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     The <c>tool_output_search</c> tool: finds a pattern in a large tool output the composition
///     retained instead of returning whole, reporting matching lines by number.
/// </summary>
/// <remarks>
///     <para>
///     <b>It exists because paging alone forces blind reads.</b> A retained output is usually large
///     precisely because it holds many things, and a model that can only page must read most of it
///     to find the one it needs. Searching first and then reading around the line found is how a
///     developer reads a large log, and the same pairing <c>text_file_search</c> and
///     <c>text_file_read</c> give a model over files.
///     </para>
///     <para>
///     <b>It is offered and confined exactly as <see cref="ToolOutputReadTool"/> is.</b> It is
///     appended by <see cref="ToolOutputSpill.Apply"/>, reads only that call's store, and takes an
///     identifier and search arguments with no path parameter, so it cannot reach the file system.
///     </para>
///     <para>
///     <b>Matches are reported grep-style by line number.</b> A match line is <c>N:content</c> and
///     a context line requested with <c>contextLines</c> is <c>N-content</c>; a <c>--</c> line
///     separates one run of matches from the next. The numbers are the same numbers
///     <see cref="ToolOutputReadTool"/> pages by, so a model searches and then reads around what it
///     found. The pattern is literal by default; setting <c>literal</c> to false interprets it as a
///     regular expression, evaluated with the invariant culture and a one-second timeout so a
///     pathological expression is refused rather than stalling the agent. An invalid expression is a
///     refused request, not a thrown error.
///     </para>
///     <para>
///     The result is bounded by <see cref="ToolLimits.MaxResultCharacters"/> and refused with the
///     ceiling named when over it; it is never retained. A search that matched nothing reports that
///     plainly. The delegate is declared to return <see cref="object"/> deliberately — see the
///     remarks on <see cref="GuardedToolFactory"/> — and every refusal is returned rather than
///     thrown.
///     </para>
///     <para>
///     The class is stateless; a constructed tool captures only the lock-guarded store and the
///     immutable limits, and is safe for concurrent use.
///     </para>
/// </remarks>
public static class ToolOutputSearchTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "tool_output_search";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Searches a large tool output that was retained instead of returned whole, reporting matching "
        + "lines as 'line:content'. Identify the output by the id its preview named, for example "
        + "'output-3'. The pattern is literal by default; set literal to false for a regular "
        + "expression. Optionally add context lines, ignore case, and cap the number of matches. "
        + "Returns the matches, or a denial explaining why the request was refused.";

    /// <summary>
    ///     The result reported when the search matched nothing.
    /// </summary>
    private const string NoMatches = "No matches.";

    /// <summary>
    ///     The separator between one run of matches and the next, matching grep.
    /// </summary>
    private const string GroupSeparator = "--";

    /// <summary>
    ///     The longest a regular expression may spend on one line before the search is refused.
    /// </summary>
    /// <remarks>
    ///     The same bound <c>text_file_search</c> applies: long enough for any sensible expression
    ///     over a line of at most two thousand characters, short enough that a pathological one is
    ///     refused before a model's turn is noticeably stalled.
    /// </remarks>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Creates the <c>tool_output_search</c> tool over one composition's store.
    /// </summary>
    /// <remarks>
    ///     Internal because the store is internal: the only caller is
    ///     <see cref="ToolOutputSpill.Apply"/>, which allocates the store this tool searches.
    /// </remarks>
    /// <param name="store">The store to search. Must not be null.</param>
    /// <param name="limits">The ceilings bounding every result. Must not be null.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="store"/> or <paramref name="limits"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(ToolOutputStore store, ToolLimits limits)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(limits);

        // Declared to return object on purpose; see the type remarks before changing this.
        var search = (
                [Description("The id of the retained output, as named in its preview, for example 'output-3'.")]
                string? id = null,
                [Description("The text or regular expression to search for.")]
                string? pattern = null,
                [Description("The number of context lines to show around each match. Optional: defaults to 0.")]
                int contextLines = 0,
                [Description("Whether to match case-insensitively. Optional: defaults to false.")]
                bool ignoreCase = false,
                [Description(
                    "Whether the pattern is a literal string rather than a regular expression. "
                    + "Optional: defaults to true.")]
                bool literal = true,
                [Description("The greatest number of matches to report. Optional: omit it for no explicit cap.")]
                int? maxMatches = null,
                CancellationToken cancellationToken = default) =>
            Search(store, limits, id, pattern, contextLines, ignoreCase, literal, maxMatches, cancellationToken);

        return GuardedToolFactory.Create(search, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Searches one retained output, refusing rather than throwing whenever the request cannot
    ///     be honored.
    /// </summary>
    /// <param name="store">The store to search.</param>
    /// <param name="limits">The ceilings bounding the result.</param>
    /// <param name="id">The identifier the model supplied.</param>
    /// <param name="pattern">The text or expression to search for.</param>
    /// <param name="contextLines">The number of context lines to show around each match.</param>
    /// <param name="ignoreCase">Whether to match case-insensitively.</param>
    /// <param name="literal">Whether the pattern is literal rather than a regular expression.</param>
    /// <param name="maxMatches">The greatest number of matches to report, or null for no cap.</param>
    /// <param name="cancellationToken">A token that cancels the search.</param>
    /// <returns>The grep-style listing, or a refusal naming its reason.</returns>
    private static object Search(
        ToolOutputStore store,
        ToolLimits limits,
        string? id,
        string? pattern,
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

        var outcome = store.Find(id, out var entry);
        if (outcome != ToolOutputLookup.Found)
        {
            return ToolOutputStore.Refusal(outcome, id);
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

        // Find the matching lines. A pathological expression times out and is refused rather than
        // stalling the turn.
        List<int> matches;
        try
        {
            matches = FindMatches(entry!.Lines, regex, maxMatches ?? int.MaxValue, cancellationToken);
        }
        catch (RegexMatchTimeoutException)
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "The regular expression took longer than "
                + MatchTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)
                + " second to evaluate against a line of " + entry!.Id + ".");
        }

        if (matches.Count == 0)
        {
            return ToolResult.Text(NoMatches);
        }

        var result = Render(entry.Lines, matches, contextLines);

        // The return budget bounds a search result exactly as it bounds any result.
        if (result.Length > limits.MaxResultCharacters)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The search result exceeds the "
                + limits.MaxResultCharacters.ToString(CultureInfo.InvariantCulture)
                + "-character result limit.");
        }

        return ToolResult.Text(result);
    }

    /// <summary>
    ///     Finds the 1-based numbers of the lines that match, up to a cap.
    /// </summary>
    /// <param name="lines">The retained lines.</param>
    /// <param name="regex">The compiled matcher.</param>
    /// <param name="cap">The greatest number of matches to report.</param>
    /// <param name="cancellationToken">A token that cancels the search.</param>
    /// <returns>The matching line numbers, in ascending order.</returns>
    /// <exception cref="RegexMatchTimeoutException">
    ///     Thrown when the expression exceeds its timeout on a line.
    /// </exception>
    private static List<int> FindMatches(
        IReadOnlyList<string> lines,
        Regex regex,
        int cap,
        CancellationToken cancellationToken)
    {
        var matches = new List<int>();
        for (var index = 0; index < lines.Count && matches.Count < cap; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (regex.IsMatch(lines[index]))
            {
                matches.Add(index + 1);
            }
        }

        return matches;
    }

    /// <summary>
    ///     Renders the matches and their context as merged groups.
    /// </summary>
    /// <param name="lines">The retained lines.</param>
    /// <param name="matches">The matching line numbers, in ascending order.</param>
    /// <param name="contextLines">The number of context lines around each match.</param>
    /// <returns>The grep-style listing.</returns>
    private static string Render(IReadOnlyList<string> lines, List<int> matches, int contextLines)
    {
        var matched = new HashSet<int>(matches);
        var builder = new StringBuilder();
        var groups = 0;

        foreach (var (start, end) in MergeRanges(matches, contextLines, lines.Count))
        {
            // Separate one run of matches from the next, as grep does, only when context makes the
            // runs distinguishable from consecutive matches.
            if (groups > 0 && contextLines > 0)
            {
                builder.Append(GroupSeparator).Append('\n');
            }

            groups++;

            for (var number = start; number <= end; number++)
            {
                builder.Append(number.ToString(CultureInfo.InvariantCulture))
                    .Append(matched.Contains(number) ? ':' : '-')
                    .Append(lines[number - 1])
                    .Append('\n');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Merges each match's context window into contiguous, non-overlapping line ranges.
    /// </summary>
    /// <param name="matches">The 1-based match line numbers, in ascending order.</param>
    /// <param name="contextLines">The number of context lines around each match.</param>
    /// <param name="total">The output's line count, used to clamp the ranges.</param>
    /// <returns>The merged inclusive ranges, in order.</returns>
    private static List<(int Start, int End)> MergeRanges(List<int> matches, int contextLines, int total)
    {
        var ranges = new List<(int Start, int End)>();
        foreach (var match in matches)
        {
            var start = (int)Math.Max(1, (long)match - contextLines);
            var end = (int)Math.Min(total, (long)match + contextLines);

            // Extend the current range when this window touches or overlaps it, so adjacent matches
            // share one group with no duplicated lines.
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
    ///     Builds the matcher from the pattern and its options.
    /// </summary>
    /// <param name="pattern">The text or expression to search for.</param>
    /// <param name="ignoreCase">Whether to match case-insensitively.</param>
    /// <param name="literal">Whether the pattern is literal rather than a regular expression.</param>
    /// <returns>The matcher.</returns>
    /// <exception cref="ArgumentException">Thrown when the expression is invalid.</exception>
    private static Regex BuildMatcher(string pattern, bool ignoreCase, bool literal)
    {
        var options = RegexOptions.CultureInvariant;
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        var effectivePattern = literal ? Regex.Escape(pattern) : pattern;
        return new Regex(effectivePattern, options, MatchTimeout);
    }
}
