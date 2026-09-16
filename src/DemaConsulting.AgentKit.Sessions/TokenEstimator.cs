using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     Estimates the token cost of session material so the engine can decide without asking a
///     provider.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why an estimator exists at all.</b> Two provider shapes must be served by the same
///     engine, and they disagree about what they will tell us. One reports current and maximum
///     token counts after every turn; the other reports nothing and leaves the window size to be
///     configured. The engine cannot have two behaviors, so it needs one arithmetic it can always
///     perform, and uses a provider's own numbers in preference when they are offered. See
///     <see cref="ContextUsage"/> for which of the two a given number came from.
///     </para>
///     <para>
///     <b>Why a character ratio rather than a tokenizer.</b> A real tokenizer is provider- and
///     model-specific, changes with model releases, and would make this package's behavior
///     non-deterministic across upgrades. Every number this engine computes feeds a
///     <em>threshold</em> decision — rotate, or do not rotate yet — taken at roughly 70 percent
///     of the effective window, which leaves around 30 percent of headroom for the estimate to be
///     wrong in. A cheap, stable, deterministic ratio is therefore the right instrument, and being
///     deterministic is what lets the rotation engine be unit-tested without a model at all.
///     </para>
///     <para>
///     <b>Where the ratio comes from.</b> <see cref="CharactersPerToken"/> is 4. The same ratio is
///     recorded in AgentKit Core's tool ceilings, which reason from the observation that common
///     tokenizers encode ordinary English prose and source code at roughly four characters per
///     token. It is a rule of thumb rather than a measurement of this library, and it is stated
///     as one.
///     </para>
///     <para>
///     This class is static, holds no state, and is safe for concurrent use.
///     </para>
/// </remarks>
public static class TokenEstimator
{
    /// <summary>
    ///     The number of characters treated as one token.
    /// </summary>
    /// <remarks>
    ///     A rule of thumb for English prose and source code under common tokenizers, not a
    ///     measurement of any particular model. It is deliberately a whole number so that every
    ///     estimate in this package is reproducible by hand when reviewing a test.
    /// </remarks>
    public const int CharactersPerToken = 4;

    /// <summary>
    ///     The tokens charged for one transcript entry beyond the characters it carries.
    /// </summary>
    /// <remarks>
    ///     Every entry a provider receives is framed — a role marker, message delimiters, and for
    ///     a tool call or result an identifier tying the pair together. That framing is charged to
    ///     the window even when the entry's own text is short, so an estimator that counted only
    ///     characters would under-count a long run of small entries, which is exactly the shape a
    ///     tool-using agent produces. Four tokens is a deliberately small, flat allowance: large
    ///     enough that the under-count does not accumulate, small enough that it never dominates.
    /// </remarks>
    public const int PerEntryOverheadTokens = 4;

    /// <summary>
    ///     The tokens charged for one tool declaration beyond the text of its name, description
    ///     and schema.
    /// </summary>
    /// <remarks>
    ///     A declaration is wrapped in provider-specific structure around the three pieces of text
    ///     it carries. The allowance is larger than <see cref="PerEntryOverheadTokens"/> because
    ///     that structure is heavier than a message envelope, and because tool declarations are
    ///     fixed overhead subtracted before any percentage is applied — under-counting them would
    ///     inflate the effective window and delay rotation.
    /// </remarks>
    public const int PerToolOverheadTokens = 8;

    /// <summary>
    ///     Estimates the tokens a run of text occupies in a context window.
    /// </summary>
    /// <remarks>
    ///     Rounds up, so a non-empty string never estimates as zero tokens: a budget comparison
    ///     that treated short content as free would let an unbounded number of short entries
    ///     accumulate. Null and empty text estimate as zero, which is the honest answer for
    ///     absent content.
    /// </remarks>
    /// <param name="text">
    ///     The text to estimate. May be <see langword="null"/> or empty, both of which estimate as
    ///     zero.
    /// </param>
    /// <returns>The estimated token count, never negative.</returns>
    public static int EstimateTokens(string? text)
    {
        // Absent content costs nothing. Handled first so the rounding below never divides an
        // empty string up to one token.
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        // Round up so short-but-present content is charged at least one token.
        return (text.Length + CharactersPerToken - 1) / CharactersPerToken;
    }

    /// <summary>
    ///     Estimates the tokens one transcript entry occupies, including its framing.
    /// </summary>
    /// <remarks>
    ///     Used by <see cref="SessionTranscript"/> to decide where a tier boundary falls, so the
    ///     framing allowance is included here rather than left to each caller to remember.
    /// </remarks>
    /// <param name="entry">The entry to estimate. Must not be <see langword="null"/>.</param>
    /// <returns>The estimated token count, always at least <see cref="PerEntryOverheadTokens"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    public static int EstimateEntryTokens(TranscriptEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return EstimateTokens(entry.Text) + PerEntryOverheadTokens;
    }

    /// <summary>
    ///     Estimates the fixed overhead the supplied tool declarations impose on every turn.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Tool declarations are sent with every request and are not conversation: they are
    ///     <em>fixed overhead</em>, which is why this figure is subtracted from the provider window
    ///     before any rotation percentage is applied — see
    ///     <see cref="AgentSessionOptions.EffectiveWindowTokens"/>. Treating them as conversation
    ///     would make the rotation threshold drift with how many tools an application attached.
    ///     </para>
    ///     <para>
    ///     A representative figure: the compaction spike that preceded this package measured a
    ///     declaration block of 2,589 tokens for a set of 11 tools (n = 11 tools, one measurement,
    ///     recorded in that spike). It is quoted only to show the order of magnitude involved —
    ///     thousands of tokens, not tens — and not as a value this method reproduces.
    ///     </para>
    /// </remarks>
    /// <param name="tools">
    ///     The tools whose declarations are estimated. May be <see langword="null"/> or empty, both
    ///     of which estimate as zero. Must contain no <see langword="null"/> entry.
    /// </param>
    /// <returns>The estimated fixed declaration overhead in tokens, never negative.</returns>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> contains a <see langword="null"/> entry, or the declarations sum
    ///     to more tokens than a token count can represent.
    /// </exception>
    public static int EstimateToolDeclarationTokens(IReadOnlyList<AIFunction>? tools)
    {
        // No tools means no declaration block, and therefore no fixed overhead to subtract.
        if (tools is null || tools.Count == 0)
        {
            return 0;
        }

        // Charge each declaration for the three pieces of text a provider is given - the name a
        // model selects by, the description it selects on, and the schema it fills in - plus the
        // structure wrapped around them.
        //
        // Accumulated in a wider type than a token count, and range-tested before it is narrowed.
        // Each declaration fits an int on its own, because a string cannot be longer than
        // int.MaxValue characters and the ratio only divides; their sum need not, and an int
        // accumulator would wrap it to a small or negative figure that every caller downstream
        // would then treat as a real measurement of the overhead. The declarations are the point at
        // which this figure first becomes computable, so it is rejected here and no later site has
        // to ask again.
        long total = 0;
        foreach (var tool in tools)
        {
            if (tool is null)
            {
                throw new ArgumentException("A tool in the list is null.", nameof(tools));
            }

            total += EstimateTokens(tool.Name)
                + EstimateTokens(tool.Description)
                + EstimateTokens(tool.JsonSchema.ToString())
                + PerToolOverheadTokens;
        }

        if (total > int.MaxValue)
        {
            throw new ArgumentException(
                $"The tool declarations come to {total} tokens, which no context window could hold "
                + "and no token count can represent.",
                nameof(tools));
        }

        return (int)total;
    }
}
