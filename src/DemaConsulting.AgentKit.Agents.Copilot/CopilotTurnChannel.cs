using GitHub.Copilot;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     One live Copilot session, reduced to the two operations an AgentKit provider session needs
///     of it: take a turn, and release the session.
/// </summary>
/// <remarks>
///     <para>
///     <b>This seam exists because the SDK's session types cannot be stood in for.</b>
///     <c>CopilotSession</c> is sealed, its constructor is not public, and none of its methods is
///     virtual; <c>CopilotClient</c> is sealed too. There is therefore no way to exercise a turn
///     without a live Copilot runtime and credentials, which CI does not have and which this
///     repository deliberately does not require. Without a seam the whole adapter — the seeding, the
///     usage arithmetic, the refusals, the ownership discipline — would ship untested.
///     </para>
///     <para>
///     <b>It is one interface with one method, on purpose.</b> Everything worth testing lives
///     <em>above</em> this line; everything below it is
///     <see cref="CopilotSessionChannel"/>, a pass-through that forwards two calls and owns one
///     disposal. That is the smallest surface that makes the rest reachable, and it is the same
///     reasoning that made <c>CopilotAgentFactory.BuildSessionConfig</c> internal.
///     </para>
///     <para>
///     Implementations are not safe for concurrent use: one channel serves one conversation, and
///     turns within a conversation are sequential by nature.
///     </para>
///     <para>
///     <b>Disposal must not throw.</b> It runs on the failure paths of a rotation, in a
///     summarizer's <c>finally</c>, and inside a factory's catch block, none of which can act on a
///     teardown error. An implementation that threw would replace a consolidation that had already
///     succeeded, or mask the very exception a caller was preserving.
///     </para>
/// </remarks>
internal interface ICopilotTurnChannel : IAsyncDisposable
{
    /// <summary>
    ///     Sends one prompt and waits for the session to finish answering it.
    /// </summary>
    /// <remarks>
    ///     Everything the turn produced along the way — tool calls, tool results, usage readings —
    ///     is delivered to the session's event handler rather than returned here, so a caller
    ///     observes a turn through <see cref="CopilotSessionObserver"/> and takes only the final
    ///     answer from the return value.
    /// </remarks>
    /// <param name="prompt">The message to send. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">
    ///     Cancels the turn. This is the turn's <em>only</em> deadline; see
    ///     <see cref="CopilotSessionChannel.SendAndWaitAsync"/> for why.
    /// </param>
    /// <returns>
    ///     The final assistant message of the turn, or <see langword="null"/> when the session went
    ///     idle without producing one.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<AssistantMessageEvent?> SendAndWaitAsync(string prompt, CancellationToken cancellationToken);
}

/// <summary>
///     Opens one Copilot session from a configuration and presents it as a turn channel.
/// </summary>
/// <remarks>
///     The seam's other half. A production opener creates a real <c>CopilotSession</c> on a real
///     <c>CopilotClient</c>; a test's opener returns a scripted channel and keeps the configuration
///     it was handed, which is how the seeding and the confinement are asserted without a runtime.
/// </remarks>
/// <param name="config">The session configuration to create the session from.</param>
/// <param name="cancellationToken">Cancels the creation.</param>
/// <returns>The opened channel. Ownership passes to the caller.</returns>
internal delegate Task<ICopilotTurnChannel> CopilotChannelOpener(
    SessionConfig config,
    CancellationToken cancellationToken);

/// <summary>
///     The production turn channel: one <see cref="CopilotSession"/> on a client the host owns.
/// </summary>
/// <remarks>
///     <para>
///     <b>Ownership is split, and the split is the whole point.</b> The <see cref="CopilotClient"/>
///     is the host's — it constructs, starts and disposes it — and this channel disposes it never.
///     The <see cref="CopilotSession"/> is AgentKit's: one per rotation, created by
///     <see cref="Open"/> and released by <see cref="DisposeAsync"/> on every path.
///     </para>
///     <para>
///     Deliberately a pass-through of two statements and one guarded disposal. It is the one piece
///     of this package no automated test reaches, so it is kept small enough to read rather than
///     made clever enough to need testing.
///     </para>
/// </remarks>
internal sealed class CopilotSessionChannel : ICopilotTurnChannel
{
    /// <summary>
    ///     The client the session was created on. Held only to delete the session on release; never
    ///     disposed here.
    /// </summary>
    private readonly CopilotClient _client;

    /// <summary>
    ///     The session this channel owns and releases.
    /// </summary>
    private readonly CopilotSession _session;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotSessionChannel"/> class.
    /// </summary>
    /// <param name="client">The client the session runs on. Owned by the host, never disposed here.</param>
    /// <param name="session">The session this channel owns.</param>
    private CopilotSessionChannel(CopilotClient client, CopilotSession session)
    {
        _client = client;
        _session = session;
    }

    /// <summary>
    ///     Returns an opener that creates sessions on one client.
    /// </summary>
    /// <remarks>
    ///     Produced once per factory rather than per session, so the client reference is captured in
    ///     exactly one place and every session a rotation creates demonstrably runs on the same
    ///     client the host started.
    /// </remarks>
    /// <param name="client">The client to create sessions on. Must not be <see langword="null"/>.</param>
    /// <returns>An opener over <paramref name="client"/>.</returns>
    internal static CopilotChannelOpener Open(CopilotClient client) =>
        async (config, cancellationToken) =>
        {
            var session = await client.CreateSessionAsync(config, cancellationToken).ConfigureAwait(false);
            return new CopilotSessionChannel(client, session);
        };

    /// <inheritdoc/>
    /// <remarks>
    ///     <para>
    ///     <b>The cancellation token is the turn's only deadline, deliberately.</b>
    ///     <c>SendAndWaitAsync</c> applies a sixty-second timeout when told none, which a
    ///     tool-using research turn exceeds routinely — and the resulting
    ///     <c>TimeoutException</c> would read as a model or tool fault rather than as a deadline
    ///     nobody chose. <c>Timeout.InfiniteTimeSpan</c> is passed instead, which the SDK carries
    ///     into <c>CancellationTokenSource.CancelAfter</c>, where it means "never". The deadline is
    ///     then the caller's token, which <c>IProviderSession.SendAsync</c> already takes and
    ///     <c>CompactingAgentSession</c> already flows — so the application governs it, rather than
    ///     this library inventing a number or offering a knob nobody asked for.
    ///     </para>
    /// </remarks>
    public Task<AssistantMessageEvent?> SendAndWaitAsync(string prompt, CancellationToken cancellationToken) =>
        _session.SendAndWaitAsync(prompt, Timeout.InfiniteTimeSpan, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    ///     <para>
    ///     Releases the session and then asks the runtime to delete it. Disposal alone is documented
    ///     to <em>preserve</em> a session's state on disk so it can be resumed later, which is the
    ///     wrong outcome here: a rotated session is finished with by definition, and a long
    ///     conversation would otherwise leave one preserved session behind per rotation. Deleting is
    ///     the runtime's only irreversible removal, so it is asked for.
    ///     </para>
    ///     <para>
    ///     <b>Neither call may throw.</b> Disposal runs on the failure paths of a rotation and on
    ///     the caller's own release, and neither is a place a failure can be acted on. The release
    ///     is not local teardown — it detaches over the runtime's transport, so a connection going
    ///     away underneath it throws, and at shutdown that is the ordinary case rather than an
    ///     exotic one. Left unguarded it would replace a consolidation that had already succeeded
    ///     with a detach failure, or mask the very exception a caller's catch block was preserving.
    ///     The delete is best-effort for the same reason: the session is already released by then,
    ///     so the worst outcome is recoverable disk state the runtime expires on its own.
    ///     </para>
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Intentionally swallowed: see the remarks. Detaching reaches the runtime over its
            // transport, so this throws when the connection is already gone - which is exactly
            // when disposal is most likely to be running.
        }

        try
        {
            await _client.DeleteSessionAsync(_session.SessionId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Intentionally swallowed: see the remarks. The session is already released, and a
            // disposal that threw here would fail a rotation that has otherwise succeeded, or a
            // caller's release that has otherwise done its job.
        }
    }
}
