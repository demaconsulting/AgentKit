namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Builders for turns, transcripts, slots, tiers and layouts whose sizes are exact rather than
///     approximate.
/// </summary>
/// <remarks>
///     <para>
///     The engine counts turns and never tokens; the only thing that counts tokens is the provider
///     session, which answers for its own window. So a size stated here is a size the <em>provider
///     double</em> will charge, and these builders invert the accounting
///     <see cref="InMemoryProviderSession"/> performs — a whole number of characters to the token,
///     plus a flat per-entry allowance for framing — so a test can ask for a turn of a chosen cost
///     and state exactly which branch it is exercising.
///     </para>
///     <para>
///     The two figures are restated here rather than read from the session under test, which keeps
///     its accounting private. A builder that derived its sizes from the code it is sizing input
///     for could not tell a changed size from a changed count.
///     </para>
/// </remarks>
internal static class SessionTestData
{
    /// <summary>
    ///     The characters the provider double treats as one token.
    /// </summary>
    public const int CharactersPerToken = 4;

    /// <summary>
    ///     The tokens the provider double charges for one entry beyond the characters it carries.
    /// </summary>
    public const int PerEntryTokens = 4;

    /// <summary>
    ///     Counts the tokens the provider double charges for one entry, framing included.
    /// </summary>
    /// <param name="entry">The entry to count. Must not be <see langword="null"/>.</param>
    /// <returns>The entry's cost in the provider double's own accounting.</returns>
    public static int EntryTokens(TranscriptEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return (entry.Text.Length + CharactersPerToken - 1) / CharactersPerToken + PerEntryTokens;
    }

    /// <summary>
    ///     Builds a user entry occupying approximately the requested number of tokens.
    /// </summary>
    /// <param name="tokens">The tokens the entry must occupy, framing included.</param>
    /// <param name="tag">A short marker so a test can identify the entry in rendered material.</param>
    /// <returns>A user entry of about <paramref name="tokens"/> tokens.</returns>
    public static TranscriptEntry UserOfTokens(int tokens, string tag)
    {
        var characters = Math.Max(tag.Length, (tokens - PerEntryTokens) * CharactersPerToken);
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
        var characters = Math.Max(tag.Length, (tokens - PerEntryTokens) * CharactersPerToken);
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
    ///     Builds a layout carrying the supplied tail and tiers.
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

        return ContextLayout.WithTiers(tail, filled);
    }

    /// <summary>
    ///     A responder that answers with a message of a chosen token size.
    /// </summary>
    /// <param name="answerTokens">The tokens the answer should occupy.</param>
    /// <returns>A responder producing a sized answer.</returns>
    public static Func<string, ProviderTurn> SizedResponder(int answerTokens) =>
        message => new ProviderTurn(AssistantOfTokens(answerTokens, "r").Text);
}
