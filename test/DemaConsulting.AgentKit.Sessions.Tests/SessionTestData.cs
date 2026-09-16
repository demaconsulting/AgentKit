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
    ///     Builds a tool call entry occupying exactly the requested number of tokens.
    /// </summary>
    /// <remarks>
    ///     Sized exactly, like <see cref="UserOfTokens"/>, because a test about where a tier
    ///     boundary falls can only state which case it exercises if it can place that boundary on a
    ///     chosen entry.
    /// </remarks>
    /// <param name="tokens">The tokens the entry must occupy, framing included.</param>
    /// <param name="toolCallId">The identifier the matching result will answer.</param>
    /// <returns>A tool call entry of exactly <paramref name="tokens"/> tokens.</returns>
    public static TranscriptEntry ToolCallOfTokens(int tokens, string toolCallId)
    {
        var characters = (tokens - TokenEstimator.PerEntryOverheadTokens) * TokenEstimator.CharactersPerToken;
        return TranscriptEntry.ToolCall(toolCallId, toolCallId.PadRight(characters, '.'));
    }

    /// <summary>
    ///     Builds a tool result entry occupying exactly the requested number of tokens.
    /// </summary>
    /// <param name="tokens">The tokens the entry must occupy, framing included.</param>
    /// <param name="toolCallId">The identifier of the call this answers.</param>
    /// <returns>A tool result entry of exactly <paramref name="tokens"/> tokens.</returns>
    public static TranscriptEntry ToolResultOfTokens(int tokens, string toolCallId)
    {
        var characters = (tokens - TokenEstimator.PerEntryOverheadTokens) * TokenEstimator.CharactersPerToken;
        return TranscriptEntry.ToolResult(toolCallId, toolCallId.PadRight(characters, '.'));
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

    /// <summary>
    ///     Gets a window in which a session using <see cref="SmallPolicy"/> converges.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Named rather than written as a literal, because the number is a conclusion.</b>
    ///     <see cref="SmallPolicy"/> budgets 230 tokens across its tiers and costs 81 more in the
    ///     framing their seeded records carry, so a rotated conversation occupies up to 311 tokens.
    ///     For the session to settle, that has to land below the rotation threshold — 70 percent of
    ///     the effective window — which takes at least 446 tokens. These tests carry no system
    ///     prompt and no tools, so the effective window is the whole window.
    ///     </para>
    ///     <para>
    ///     600 is chosen over the bare minimum to leave visible hysteresis: measured against a
    ///     summarizer that fills every tier to its budget, a session here rotates roughly once every
    ///     three or four turns rather than on every turn. Nine of these tests previously ran at 400,
    ///     which holds the 311-token bound and so passed the guard as it was then written, but is
    ///     below 446 and therefore rotates without ever settling.
    ///     </para>
    /// </remarks>
    public static int ConvergentWindowTokens => 600;
}
