namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Builders for turns, transcripts, slots, tiers and layouts whose sizes are exact rather than
///     approximate.
/// </summary>
/// <remarks>
///     Every decision the compaction engine makes is a comparison of a count — turns or estimated
///     tokens — so a test that cannot state a transcript's size exactly cannot state which branch it
///     is exercising. These builders invert the token estimate — a whole number of characters per
///     token, plus a flat per-entry allowance — so a test can ask for a turn of a chosen size.
/// </remarks>
internal static class SessionTestData
{
    /// <summary>
    ///     Builds a user entry occupying approximately the requested number of tokens.
    /// </summary>
    /// <param name="tokens">The tokens the entry must occupy, framing included.</param>
    /// <param name="tag">A short marker so a test can identify the entry in rendered material.</param>
    /// <returns>A user entry of about <paramref name="tokens"/> tokens.</returns>
    public static TranscriptEntry UserOfTokens(int tokens, string tag)
    {
        var characters = Math.Max(tag.Length, (tokens - TokenEstimator.PerEntryOverheadTokens) * TokenEstimator.CharactersPerToken);
        return TranscriptEntry.User(tag.PadRight(characters, '.'));
    }

    /// <summary>
    ///     Builds the entries of one turn: a user message and an assistant answer of a chosen size.
    /// </summary>
    /// <param name="tokens">The tokens the turn should occupy in total, framing included.</param>
    /// <param name="tag">A short marker identifying the turn.</param>
    /// <returns>The entries of one turn.</returns>
    public static IReadOnlyList<TranscriptEntry> TurnEntries(int tokens, string tag)
    {
        var half = Math.Max(1, tokens / 2);
        return [UserOfTokens(half, $"u{tag}"), AssistantOfTokens(tokens - half, $"a{tag}")];
    }

    /// <summary>
    ///     Builds an assistant entry occupying approximately the requested number of tokens.
    /// </summary>
    /// <param name="tokens">The tokens the entry must occupy, framing included.</param>
    /// <param name="tag">A short marker so a test can identify the entry.</param>
    /// <returns>An assistant entry of about <paramref name="tokens"/> tokens.</returns>
    public static TranscriptEntry AssistantOfTokens(int tokens, string tag)
    {
        var characters = Math.Max(tag.Length, (tokens - TokenEstimator.PerEntryOverheadTokens) * TokenEstimator.CharactersPerToken);
        return TranscriptEntry.Assistant(tag.PadRight(characters, '.'));
    }

    /// <summary>
    ///     Builds a transcript of equally sized turns, oldest first.
    /// </summary>
    /// <param name="count">How many turns to build.</param>
    /// <param name="tokensEach">The tokens each turn occupies, framing included.</param>
    /// <returns>A transcript of <paramref name="count"/> turns.</returns>
    public static SessionTranscript TranscriptOf(int count, int tokensEach)
    {
        var transcript = SessionTranscript.Empty;
        for (var index = 0; index < count; index++)
        {
            transcript = transcript.AppendTurn(TurnEntries(tokensEach, $"{index}"));
        }

        return transcript;
    }

    /// <summary>
    ///     Builds a slot carrying a record of a chosen token size.
    /// </summary>
    /// <param name="tokens">The tokens the record occupies.</param>
    /// <param name="tag">A short marker identifying the slot.</param>
    /// <returns>A slot of about <paramref name="tokens"/> tokens.</returns>
    public static Slot SlotOfTokens(int tokens, string tag)
    {
        var characters = Math.Max(tag.Length, tokens * TokenEstimator.CharactersPerToken);
        return new Slot(tag.PadRight(characters, '.'));
    }

    /// <summary>
    ///     Builds a tier holding the supplied slots, oldest first.
    /// </summary>
    /// <param name="slots">The slots, oldest first.</param>
    /// <returns>A tier holding the slots.</returns>
    public static Tier TierOf(params Slot[] slots)
    {
        var tier = Tier.Empty;
        foreach (var slot in slots)
        {
            tier = tier.Append(slot);
        }

        return tier;
    }

    /// <summary>
    ///     Builds a layout with no fixed overhead, carrying the supplied tail and tiers.
    /// </summary>
    /// <param name="tail">The verbatim tail.</param>
    /// <param name="tiers">The three coarse tiers, tier one first.</param>
    /// <returns>A layout carrying the supplied structure.</returns>
    public static ContextLayout LayoutOf(SessionTranscript tail, params Tier[] tiers)
    {
        var filled = new Tier[ContextLayout.TierCount];
        for (var index = 0; index < ContextLayout.TierCount; index++)
        {
            filled[index] = index < tiers.Length ? tiers[index] : Tier.Empty;
        }

        return ContextLayout.Create(0, 0).WithTiers(tail, filled);
    }

    /// <summary>
    ///     A responder that answers with a message of a chosen token size.
    /// </summary>
    /// <param name="answerTokens">The tokens the answer should occupy.</param>
    /// <returns>A responder producing a sized answer.</returns>
    public static Func<string, ProviderTurn> SizedResponder(int answerTokens) =>
        message => new ProviderTurn(AssistantOfTokens(answerTokens, "r").Text);
}
