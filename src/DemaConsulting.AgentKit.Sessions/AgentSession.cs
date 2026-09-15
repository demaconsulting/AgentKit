namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     What one turn of a session produced: the answer, what the window looks like afterwards, and
///     whether the session had to rotate to make room.
/// </summary>
/// <remarks>
///     <para>
///     Compaction is reported rather than hidden. An application that never looks will never
///     notice, which is the point — the session keeps working either way — but an application that
///     does look can log a rotation, act on a saturation signal, or show a user why an answer took
///     longer than the last one. Hiding it would make a saturated agent indistinguishable from a
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
    /// <param name="saturations">
    ///     Any saturation the rotation reported. <see langword="null"/> means none. Must contain no
    ///     <see langword="null"/> entry.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="text"/> or <paramref name="usage"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="saturations"/> contains a <see langword="null"/> entry.</exception>
    public AgentSessionResponse(
        string text,
        ContextUsage usage,
        bool rotationOccurred,
        IReadOnlyList<SaturationSignal>? saturations = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(usage);

        if (saturations is not null && saturations.Any(signal => signal is null))
        {
            throw new ArgumentException("A saturation signal in the list is null.", nameof(saturations));
        }

        Text = text;
        Usage = usage;
        RotationOccurred = rotationOccurred;

        // Copy the signals into storage this response owns, exposed only as a read-only view. A
        // response is documented as immutable, and a caller that retained the list it supplied - or
        // that cast this one back to an array - could otherwise change IsSaturated after the fact.
        Saturations = Array.AsReadOnly<SaturationSignal>([.. saturations ?? []]);
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
    ///     A rotation consolidated older history, disposed the provider session, and created a fresh
    ///     one seeded from the preserved content. The answer in <see cref="Text"/> was produced
    ///     before that happened, by the session being replaced.
    /// </remarks>
    public bool RotationOccurred { get; }

    /// <summary>
    ///     Gets any saturation the rotation reported, empty when there was none.
    /// </summary>
    public IReadOnlyList<SaturationSignal> Saturations { get; }

    /// <summary>
    ///     Gets a value indicating whether the rotation reported saturation.
    /// </summary>
    /// <remarks>
    ///     True means a consolidation could not reduce what it was given: the agent's context holds
    ///     no redundancy left to remove, and further rotations will cost summarizer tokens without
    ///     buying room. What to do about it is the application's decision.
    /// </remarks>
    public bool IsSaturated => Saturations.Count > 0;
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
    ///     long-running agent's behavior, and because a saturation signal means much more alongside
    ///     the rotation count that produced it.
    /// </remarks>
    int RotationCount { get; }

    /// <summary>
    ///     Sends one message and returns the answer, compacting first if the window requires it.
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
    /// <returns>The answer, the resulting usage, and whether the session rotated.</returns>
    /// <exception cref="ArgumentException"><paramref name="message"/> is <see langword="null"/>, empty, or blank.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<AgentSessionResponse> SendAsync(string message, CancellationToken cancellationToken = default);
}
