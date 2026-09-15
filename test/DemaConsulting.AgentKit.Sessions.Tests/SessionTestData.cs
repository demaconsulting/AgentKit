namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Builders for transcripts and layouts whose token sizes are exact rather than approximate.
/// </summary>
/// <remarks>
///     Every decision the compaction engine makes is a comparison of a token count against a
///     budget, so a test that cannot state a transcript's size exactly cannot state which branch it
///     is exercising. These builders invert the token estimate — a whole number of characters per
///     token, plus a flat per-entry allowance — so a test can ask for an entry of precisely twenty
///     tokens and reason about the boundary it falls on.
/// </remarks>
internal static class SessionTestData
{
    /// <summary>
    ///     Builds a user entry occupying exactly the requested number of tokens.
    /// </summary>
    /// <param name="tokens">
    ///     The tokens the entry must occupy, framing included. Must leave room for the framing
    ///     allowance and the tag.
    /// </param>
    /// <param name="tag">A short marker so a test can identify this entry in rendered material.</param>
    /// <returns>A user entry of exactly <paramref name="tokens"/> tokens.</returns>
    public static TranscriptEntry UserOfTokens(int tokens, string tag)
    {
        var characters = (tokens - TokenEstimator.PerEntryOverheadTokens) * TokenEstimator.CharactersPerToken;
        return TranscriptEntry.User(tag.PadRight(characters, '.'));
    }

    /// <summary>
    ///     Builds a transcript of equally sized user entries, oldest first.
    /// </summary>
    /// <param name="count">How many entries to build.</param>
    /// <param name="tokensEach">The tokens each entry occupies, framing included.</param>
    /// <returns>A transcript of <paramref name="count"/> entries.</returns>
    public static SessionTranscript TranscriptOf(int count, int tokensEach)
    {
        var transcript = SessionTranscript.Empty;
        for (var index = 0; index < count; index++)
        {
            transcript = transcript.Append(UserOfTokens(tokensEach, $"e{index}"));
        }

        return transcript;
    }

    /// <summary>
    ///     Builds a layout with no fixed overhead, carrying the supplied transcript.
    /// </summary>
    /// <remarks>
    ///     Zero fixed overhead keeps a rotation test's arithmetic about the tiers alone; the
    ///     accounting for the system prompt and tool declarations is exercised where it belongs, in
    ///     the options and layout tests.
    /// </remarks>
    /// <param name="policy">The policy whose budgets the layout observes.</param>
    /// <param name="transcript">The verbatim history the layout starts with.</param>
    /// <returns>A layout carrying <paramref name="transcript"/> and no coarse records.</returns>
    public static ContextLayout LayoutOf(CompactionPolicy policy, SessionTranscript transcript) =>
        ContextLayout.Create(policy, 0, 0).WithTranscript(transcript);

    /// <summary>
    ///     Gets a four-tier policy small enough that a test can fill it with a handful of entries.
    /// </summary>
    /// <remarks>
    ///     The same shape as the shipped defaults — a large verbatim tier and progressively smaller
    ///     coarse tiers — scaled down so that the boundary cases are reachable without building a
    ///     transcript of thousands of tokens.
    /// </remarks>
    public static CompactionPolicy SmallPolicy { get; } = new([100, 60, 40, 30]);
}
