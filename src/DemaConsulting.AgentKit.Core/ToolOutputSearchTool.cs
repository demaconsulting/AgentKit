using System.ComponentModel;
using System.Diagnostics;
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
///     regular expression, evaluated with the invariant culture and a one-second timeout on each
///     line, within a one-second budget for the whole search, so a pathological expression is
///     refused rather than stalling the agent — whether it is slow on one line or slightly slow on
///     every line. An invalid expression is a refused request, not a thrown error.
///     </para>
///     <para>
///     A pattern longer than <see cref="MaxPatternCharacters"/> is refused before any matcher
///     is built, because the timeouts bound matching only: building the matcher is not covered
///     by them and costs time that grows with the pattern. The cap bounds that construction
///     cost to tens of milliseconds rather than making it a hard time limit.
///     </para>
///     <para>
///     The result is bounded by <see cref="ToolLimits.MaxResultCharacters"/> and refused with the
///     ceiling named when over it; it is never retained. The search is one pass that writes each
///     match as it is found and stops as soon as the result is known to exceed the ceiling, so an
///     oversized result costs no more than the ceiling's worth of work and memory. A search that
///     matched nothing reports that plainly. The delegate is declared to return <see cref="object"/> deliberately — see the
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
    ///     The longest search pattern accepted, in characters.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     It applies to literal and regular-expression patterns alike and is measured on the
    ///     pattern as supplied, before a literal is escaped; escaping at most doubles it.
    ///     </para>
    ///     <para>
    ///     The cap exists because building a matcher costs time that grows with the pattern's
    ///     length, and the per-line <see cref="MatchTimeout"/> covers matching only, never the
    ///     parsing and construction of the expression. Without it the model would choose how long
    ///     construction runs. Measured on .NET 8, 9 and 10, a pattern at this length builds in tens
    ///     of milliseconds even when written to be costly, where an uncapped one can take over a
    ///     second before the first timeout-guarded match begins. The cap bounds construction; it
    ///     is not a hard time limit on it, and that residual cost is accepted. A thousand
    ///     characters is far beyond any useful search and half of the longest line a retained
    ///     output holds.
    ///     </para>
    /// </remarks>
    internal const int MaxPatternCharacters = 1_000;

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
    ///     The longest a whole search may take before it is refused.
    /// </summary>
    /// <remarks>
    ///     The per-line <see cref="MatchTimeout"/> alone would let an expression that takes just
    ///     under it on every line run for one timeout per line. Checked between lines, this budget
    ///     bounds the whole search by itself plus at most one line's timeout. It equals the per-line
    ///     timeout, so a search runs for at most about two seconds in total: the one-second budget
    ///     plus the one-second timeout of the line in progress when the budget runs out.
    /// </remarks>
    private static readonly TimeSpan SearchBudget = MatchTimeout;

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
                [Description("The text or regular expression to search for, at most 1000 characters.")]
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

        // The length is checked before any matcher is built: construction cost grows with the
        // pattern and is not covered by the match timeout. The pattern is not echoed back.
        if (pattern.Length > MaxPatternCharacters)
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "The search pattern exceeds the " + MaxPatternCharacters.ToString(CultureInfo.InvariantCulture)
                + "-character pattern limit. Search for a shorter pattern.");
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

        return SearchEntry(
            entry!,
            limits,
            pattern,
            contextLines,
            ignoreCase,
            literal,
            maxMatches,
            SearchBudget,
            cancellationToken);
    }

    /// <summary>
    ///     Searches one resolved retained output in a single pass, refusing as soon as the result
    ///     is known to exceed the ceiling or the search has spent its time budget.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Internal, rather than private, because it is the search logic over one entry: the unit
    ///     tests drive it with an entry whose lines count every read and with an already-spent time
    ///     budget, proving an oversized result and a slow search each stop early.
    ///     </para>
    ///     <para>
    ///     Lines are tested in order and each match is written, grep-style, with its context as
    ///     soon as it is found, so no list of matches or ranges is ever held. A piece that would
    ///     carry the listing over the ceiling ends the search with the size refusal, so the listing
    ///     never holds more than the ceiling's worth of result. The elapsed time is checked before
    ///     every line, so the whole search is bounded by <paramref name="searchBudget"/> plus at
    ///     most one line's <see cref="MatchTimeout"/>, not by one timeout per line.
    ///     </para>
    /// </remarks>
    /// <param name="entry">The retained output.</param>
    /// <param name="limits">The ceilings bounding the result.</param>
    /// <param name="pattern">
    ///     The text or expression to search for; not empty and at most
    ///     <see cref="MaxPatternCharacters"/> characters.
    /// </param>
    /// <param name="contextLines">The number of context lines to show around each match, 0 or greater.</param>
    /// <param name="ignoreCase">Whether to match case-insensitively.</param>
    /// <param name="literal">Whether the pattern is literal rather than a regular expression.</param>
    /// <param name="maxMatches">The greatest number of matches to report, 0 or greater, or null for no cap.</param>
    /// <param name="searchBudget">The longest the whole search may take.</param>
    /// <param name="cancellationToken">A token that cancels the search.</param>
    /// <returns>The grep-style listing, or a refusal naming its reason.</returns>
    internal static object SearchEntry(
        ToolOutputEntry entry,
        ToolLimits limits,
        string pattern,
        int contextLines,
        bool ignoreCase,
        bool literal,
        int? maxMatches,
        TimeSpan searchBudget,
        CancellationToken cancellationToken)
    {
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

        var lines = entry.Lines;
        var total = lines.Count;
        var cap = maxMatches ?? int.MaxValue;
        var max = limits.MaxResultCharacters;
        var listing = new StringBuilder();
        var found = 0;
        var writtenThrough = 0;
        var contextThrough = 0;
        var started = Stopwatch.GetTimestamp();

        try
        {
            for (var number = 1; number <= total; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // A search is bounded in total, not only per line: an expression just under the
                // per-line timeout on every line of a large output must not stall the turn.
                if (Stopwatch.GetElapsedTime(started) >= searchBudget)
                {
                    return SearchTimedOut(entry, searchBudget);
                }

                if (found < cap && regex.IsMatch(lines[number - 1]))
                {
                    found++;
                    var windowStart = (int)Math.Max(1, (long)number - contextLines);

                    // Separate one run of matches from the next, as grep does, only when context
                    // makes the runs distinguishable from consecutive matches.
                    if (contextLines > 0 && writtenThrough > 0 && windowStart > writtenThrough + 1)
                    {
                        if (!Fits(listing, max, GroupSeparator.Length + 1))
                        {
                            return TooLarge(max);
                        }

                        listing.Append(GroupSeparator).Append('\n');
                    }

                    // Leading context not already written as an earlier match's trailing context.
                    for (var before = Math.Max(windowStart, writtenThrough + 1); before < number; before++)
                    {
                        if (!TryAppendLine(listing, max, before, '-', lines[before - 1]))
                        {
                            return TooLarge(max);
                        }
                    }

                    if (!TryAppendLine(listing, max, number, ':', lines[number - 1]))
                    {
                        return TooLarge(max);
                    }

                    writtenThrough = number;
                    contextThrough = (int)Math.Min(total, (long)number + contextLines);
                }
                else if (number <= contextThrough)
                {
                    // Trailing context; a line matching past the cap is shown as context, as grep
                    // shows it.
                    if (!TryAppendLine(listing, max, number, '-', lines[number - 1]))
                    {
                        return TooLarge(max);
                    }

                    writtenThrough = number;
                }
                else if (found >= cap)
                {
                    break;
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological expression times out on a line and is refused rather than stalling
            // the turn.
            return LineTimedOut(entry, MatchTimeout);
        }

        return ToolResult.Text(found == 0 ? NoMatches : listing.ToString());
    }

    /// <summary>
    ///     Appends one numbered line to the listing, unless it would carry the listing over the
    ///     ceiling.
    /// </summary>
    /// <param name="listing">The listing written so far, within the ceiling.</param>
    /// <param name="max">The result ceiling, in characters.</param>
    /// <param name="number">The 1-based line number.</param>
    /// <param name="marker"><c>:</c> for a match, <c>-</c> for context.</param>
    /// <param name="line">The line's content.</param>
    /// <returns><see langword="true"/> when the line was appended; <see langword="false"/> when it would not fit.</returns>
    private static bool TryAppendLine(StringBuilder listing, int max, int number, char marker, string line)
    {
        var digits = number.ToString(CultureInfo.InvariantCulture);
        if (!Fits(listing, max, digits.Length + 1L + line.Length + 1))
        {
            return false;
        }

        listing.Append(digits).Append(marker).Append(line).Append('\n');
        return true;
    }

    /// <summary>
    ///     Decides whether a piece of the given size still fits the ceiling.
    /// </summary>
    /// <param name="listing">The listing written so far, within the ceiling.</param>
    /// <param name="max">The result ceiling, in characters.</param>
    /// <param name="cost">The size of the piece to append.</param>
    /// <returns><see langword="true"/> when the piece fits.</returns>
    private static bool Fits(StringBuilder listing, int max, long cost) => listing.Length + cost <= max;

    /// <summary>
    ///     Composes the refusal for a result over the ceiling.
    /// </summary>
    /// <param name="max">The result ceiling, in characters.</param>
    /// <returns>The refusal naming the ceiling.</returns>
    private static object TooLarge(int max) =>
        ToolResult.Denied(
            DenialReason.ResourceTooLarge,
            "The search result exceeds the " + max.ToString(CultureInfo.InvariantCulture)
            + "-character result limit.");

    /// <summary>
    ///     Composes the refusal for a pattern that ran out of time on one line.
    /// </summary>
    /// <param name="entry">The retained output searched.</param>
    /// <param name="limit">The per-line time limit that was exceeded.</param>
    /// <returns>The refusal naming the limit and the output.</returns>
    private static object LineTimedOut(ToolOutputEntry entry, TimeSpan limit) =>
        ToolResult.Denied(
            DenialReason.InvalidRequest,
            "The search pattern took longer than "
            + limit.TotalSeconds.ToString(CultureInfo.InvariantCulture)
            + " second to evaluate against one line of " + entry.Id + ".");

    /// <summary>
    ///     Composes the refusal for a whole search that ran out of its time budget, which applies
    ///     to literal and regular-expression searches alike.
    /// </summary>
    /// <param name="entry">The retained output searched.</param>
    /// <param name="limit">The whole-search time budget that was exceeded.</param>
    /// <returns>The refusal naming the budget and the output.</returns>
    private static object SearchTimedOut(ToolOutputEntry entry, TimeSpan limit) =>
        ToolResult.Denied(
            DenialReason.InvalidRequest,
            "The search took longer than "
            + limit.TotalSeconds.ToString(CultureInfo.InvariantCulture)
            + " second to run against " + entry.Id + ".");

    /// <summary>
    ///     Builds the matcher from the pattern and its options.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The options are chosen deliberately and match <c>text_file_search</c>: the default
    ///     backtracking interpreter with <see cref="RegexOptions.CultureInvariant"/>, plus
    ///     <see cref="RegexOptions.IgnoreCase"/> when asked, so case-insensitive matching does not
    ///     depend on the host culture.
    ///     </para>
    ///     <para>
    ///     <see cref="RegexOptions.Compiled"/> is not used: each search builds a fresh matcher used
    ///     once, and compiling emits and JITs code on every call, making construction strictly
    ///     slower. <see cref="RegexOptions.NonBacktracking"/> is not used either: it refuses
    ///     backreferences, look-ahead and look-behind assertions, and large counted repetitions
    ///     that models write, refuses them with <see cref="NotSupportedException"/> rather than <see cref="ArgumentException"/>,
    ///     accepts different patterns on different target frameworks, and builds more slowly. Its
    ///     linear-time matching is not needed because the per-line timeout and the whole-search
    ///     budget already bound matching.
    ///     </para>
    ///     <para>
    ///     The match timeout does not cover construction. Construction is bounded instead by the
    ///     caller refusing any pattern over <see cref="MaxPatternCharacters"/>; the cost that
    ///     remains at that length is tens of milliseconds and is accepted.
    ///     </para>
    /// </remarks>
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
