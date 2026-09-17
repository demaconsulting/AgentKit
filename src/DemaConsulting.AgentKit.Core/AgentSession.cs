namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     What one turn of a session produced: the answer, what the window looks like afterwards,
///     whether the session had to rotate to make room, how hard it is compacting, and whether it had
///     to drop history outright.
/// </summary>
/// <remarks>
///     <para>
///     Compaction is reported rather than hidden. An application that never looks will never notice,
///     which is the point — the session keeps working either way — but an application that does look
///     can log a rotation, watch the compaction level climb, or act on the honest signal that
///     material was dropped. Hiding it would make a struggling agent indistinguishable from a
///     healthy one.
///     </para>
///     <para>
///     Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
public sealed class AgentSessionResponse
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AgentSessionResponse"/> class.
    /// </summary>
    /// <param name="text">The provider's answer. Must not be <see langword="null"/>; may be empty.</param>
    /// <param name="usage">The context usage after the turn. Must not be <see langword="null"/>.</param>
    /// <param name="rotationOccurred">Whether the session rotated during this turn.</param>
    /// <param name="level">The compaction level the session is at after the turn.</param>
    /// <param name="materialDropped">
    ///     Whether the turn's rotation had to drop history outright because a fully consolidated
    ///     context still did not fit.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="text"/> or <paramref name="usage"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="level"/> is not a defined <see cref="CompactionLevel"/> member.
    /// </exception>
    public AgentSessionResponse(
        string text,
        ContextUsage usage,
        bool rotationOccurred,
        CompactionLevel level = CompactionLevel.Low,
        bool materialDropped = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(usage);

        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "The compaction level must be a defined CompactionLevel member.");
        }

        Text = text;
        Usage = usage;
        RotationOccurred = rotationOccurred;
        Level = level;
        MaterialDropped = materialDropped;
    }

    /// <summary>
    ///     Gets the provider's answer.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Gets the context usage after the turn, and whether the provider reported it or this
    ///     library estimated it.
    /// </summary>
    public ContextUsage Usage { get; }

    /// <summary>
    ///     Gets a value indicating whether the session rotated during this turn.
    /// </summary>
    /// <remarks>
    ///     A rotation consolidated older history, created a fresh provider session seeded from the
    ///     preserved content, and then disposed the one it replaced. The answer in
    ///     <see cref="Text"/> was produced before any of that happened, by the session being
    ///     replaced.
    /// </remarks>
    public bool RotationOccurred { get; }

    /// <summary>
    ///     Gets the compaction level the session is at after this turn.
    /// </summary>
    /// <remarks>
    ///     The fidelity signal. A higher level means fewer recent turns are kept verbatim and the
    ///     summarizer is told to be terser. It replaces the saturation signal the earlier design
    ///     carried: where that reported a ratio, this reports how hard the session is compacting,
    ///     which is a fact an application can act on directly.
    /// </remarks>
    public CompactionLevel Level { get; }

    /// <summary>
    ///     Gets a value indicating whether this turn's rotation dropped history outright.
    /// </summary>
    /// <remarks>
    ///     True when the session escalated as far as it could and a fully consolidated context still
    ///     did not fit, so a consolidated slot or a verbatim turn had to be discarded. It is the
    ///     honest signal that compaction bought nothing — the one thing standing between "escalated
    ///     to High and still rotating every turn" and total silence. What to do about it is the
    ///     application's decision.
    /// </remarks>
    public bool MaterialDropped { get; }
}

/// <summary>
///     A conversation with an agent that compacts its own context, so it can outlive the provider's
///     window.
/// </summary>
/// <remarks>
///     <para>
///     <b>This is the contract the rest of AgentKit's session support is written against.</b> It is
///     deliberately small: send a message, read what the window looks like, dispose when finished.
///     It says nothing about providers, tools, streaming, or how compaction is achieved, because an
///     application that simply wants a long-running agent should not have to know any of that, and
///     because a small contract can be extended later without breaking anyone.
///     </para>
///     <para>
///     <b>Disposal matters.</b> An implementation owns a live provider session, which for some
///     providers is server-side state that keeps being billed for until it is released. Disposing
///     the agent session releases whichever provider session it currently holds.
///     </para>
///     <para>
///     Implementations need not be safe for concurrent use: one session is one conversation, and
///     turns within a conversation are sequential by nature.
///     </para>
/// </remarks>
public interface IAgentSession : IAsyncDisposable
{
    /// <summary>
    ///     Gets the context usage after the most recent turn.
    /// </summary>
    /// <remarks>
    ///     Before the first turn this reports what the freshly seeded session occupies, which for a
    ///     new conversation is the fixed overhead alone.
    /// </remarks>
    ContextUsage Usage { get; }

    /// <summary>
    ///     Gets how many times this session has rotated since it was created.
    /// </summary>
    /// <remarks>
    ///     A rotation is a complete replacement of the provider session, seeded from consolidated
    ///     history. Exposed because it is the single most useful number for understanding a
    ///     long-running agent's behavior.
    /// </remarks>
    int RotationCount { get; }

    /// <summary>
    ///     Gets the compaction level the session is currently at.
    /// </summary>
    /// <remarks>
    ///     Session state that adapts under pressure. Read alongside <see cref="RotationCount"/>: a
    ///     level climbing to High over many rotations is what a struggling long-running agent looks
    ///     like.
    /// </remarks>
    CompactionLevel Level { get; }

    /// <summary>
    ///     Sends one message and returns the answer, compacting afterwards if the window requires it.
    /// </summary>
    /// <remarks>
    ///     Compaction, when it happens, happens after the answer is produced: the turn is served by
    ///     the session that was live when it arrived, and the replacement is prepared for the turn
    ///     after. That ordering means a caller never waits on a summarizer before receiving the
    ///     answer to a question the session could already answer.
    /// </remarks>
    /// <param name="message">
    ///     The message to send. Must not be <see langword="null"/>, empty, or blank — a blank turn
    ///     spends context to say nothing.
    /// </param>
    /// <param name="cancellationToken">Cancels the turn, and any compaction it triggers.</param>
    /// <returns>The answer, the resulting usage, whether the session rotated, and the compaction level.</returns>
    /// <exception cref="ArgumentException"><paramref name="message"/> is <see langword="null"/>, empty, or blank.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<AgentSessionResponse> SendAsync(string message, CancellationToken cancellationToken = default);
}
