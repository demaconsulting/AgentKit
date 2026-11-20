using Microsoft.Copilot;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     One live Copilot session, reduced to the operations AgentKit needs of it: know which session
///     it is, take a turn, and release the session.
/// </summary>
/// <remarks>
///     <para>
///     <b>This seam exists because the SDK's session types cannot be stood in for.</b>
///     <c>CopilotSession</c> is sealed, its constructor is not public, and none of its methods is
///     virtual; <c>CopilotClient</c> is sealed too. There is therefore no way to exercise a turn
///     without a live Copilot runtime and credentials, which CI does not have and which this
///     repository deliberately does not require. Without a seam the whole package — the seeding, the
///     usage arithmetic, the refusals, the agent's streaming and resumption, the ownership
///     discipline — would ship untested.
///     </para>
///     <para>
///     <b>It is one small interface, on purpose.</b> Everything worth testing lives <em>above</em>
///     this line; everything below it is <see cref="CopilotSessionChannel"/>, a pass-through that
///     forwards a call and owns one release. That is the smallest surface that makes the rest
///     reachable, and it is the same reasoning that made
///     <c>CopilotAgentFactory.BuildSessionConfig</c> internal.
///     </para>
///     <para>
///     Implementations are not safe for concurrent use: one channel serves one conversation, and
///     turns within a conversation are sequential by nature.
///     </para>
///     <para>
///     <b>Disposal must not throw.</b> It runs on the failure paths of a rotation, in a
///     summarizer's <c>finally</c>, after every agent run, and inside a factory's catch block, none
///     of which can act on a teardown error. An implementation that threw would replace a
///     consolidation that had already succeeded, or mask the very exception a caller was preserving.
///     </para>
/// </remarks>
internal interface ICopilotTurnChannel : IAsyncDisposable
{
    /// <summary>
    ///     Gets the runtime's identifier for the session this channel carries.
    /// </summary>
    /// <remarks>
    ///     The agent records it after the first run so a later run — on this agent instance or on a
    ///     new one given the serialized session — can resume the same runtime session.
    /// </remarks>
    string SessionId { get; }

    /// <summary>
    ///     Sends one message and waits for the session to finish answering it.
    /// </summary>
    /// <remarks>
    ///     Everything the turn produced along the way — tool calls, tool results, usage readings,
    ///     streamed text — is delivered to the session's event handler rather than returned here, so
    ///     a caller observes a turn through that handler and takes only the final answer from the
    ///     return value.
    /// </remarks>
    /// <param name="message">
    ///     The message to send: its prompt and any attachments. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">
    ///     Cancels the turn. This is the turn's <em>only</em> deadline; see
    ///     <see cref="CopilotSessionChannel.SendAndWaitAsync"/> for why.
    /// </param>
    /// <returns>
    ///     The final assistant message of the turn, or <see langword="null"/> when the session went
    ///     idle without producing one.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<AssistantMessageEvent?> SendAndWaitAsync(MessageOptions message, CancellationToken cancellationToken);
}

/// <summary>
///     Creates one Copilot session from a configuration and presents it as a turn channel.
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
    CreateSessionOptions config,
    CancellationToken cancellationToken);

/// <summary>
///     Resumes one existing Copilot session by its identifier and presents it as a turn channel.
/// </summary>
/// <remarks>
///     The agent's counterpart to <see cref="CopilotChannelOpener"/>. A resumed session is confined
///     by the configuration handed here, exactly as a created one is by its own, so a test's resumer
///     keeps the configuration to assert that the confinement was re-applied.
/// </remarks>
/// <param name="sessionId">The runtime's identifier for the session to resume.</param>
/// <param name="config">The configuration to resume the session with.</param>
/// <param name="cancellationToken">Cancels the resumption.</param>
/// <returns>The resumed channel. Ownership passes to the caller.</returns>
internal delegate Task<ICopilotTurnChannel> CopilotChannelResumer(
    string sessionId,
    ResumeSessionOptions config,
    CancellationToken cancellationToken);

/// <summary>
///     The production turn channel: one <see cref="CopilotSession"/> on a client the host owns.
/// </summary>
/// <remarks>
///     <para>
///     <b>Ownership is split, and the split is the whole point.</b> The <see cref="CopilotClient"/>
///     is the host's — it constructs, starts and disposes it — and this channel disposes it never.
///     The <see cref="CopilotSession"/> handle is AgentKit's: created or resumed by one of the
///     opener factories below and released by <see cref="DisposeAsync"/> on every path.
///     </para>
///     <para>
///     <b>Release means one of two things, chosen when the channel is opened.</b> A session the
///     engine or the summarizer opened is finished with when it is released — a rotated
///     conversation, a completed consolidation — so it is released and then deleted
///     (<see cref="Open"/>). So is the session of an agent's run made without a conversation,
///     which nothing could resume. An agent's <em>conversation</em> continues on the next run,
///     possibly on another agent instance given the serialized session, so its session is released
///     and <em>kept</em> (<see cref="OpenRetained"/> and <see cref="Resume"/>): the runtime preserves
///     its state under the client's data directory until the host deletes it.
///     </para>
///     <para>
///     Deliberately a pass-through of one forwarded call and one guarded release. It is the one
///     piece of this package no automated test reaches, so it is kept small enough to read rather
///     than made clever enough to need testing; the release policy itself is factored into
///     <see cref="ReleaseAsync"/>, which is tested.
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
    ///     The session handle this channel owns and releases.
    /// </summary>
    private readonly CopilotSession _session;

    /// <summary>
    ///     Whether release also asks the runtime to delete the session's persisted state.
    /// </summary>
    private readonly bool _deleteOnRelease;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotSessionChannel"/> class.
    /// </summary>
    /// <param name="client">The client the session runs on. Owned by the host, never disposed here.</param>
    /// <param name="session">The session handle this channel owns.</param>
    /// <param name="deleteOnRelease">Whether release also deletes the session's persisted state.</param>
    private CopilotSessionChannel(CopilotClient client, CopilotSession session, bool deleteOnRelease)
    {
        _client = client;
        _session = session;
        _deleteOnRelease = deleteOnRelease;
    }

    /// <inheritdoc/>
    public string SessionId => _session.SessionId;

    /// <summary>
    ///     Returns an opener that creates sessions on one client and deletes each when it is released.
    /// </summary>
    /// <remarks>
    ///     For the session engine, the summarizer and an agent's run made without a conversation,
    ///     whose sessions are finished with when released. Produced once per factory rather than per
    ///     session, so the client reference is captured in exactly one place and every session a
    ///     rotation creates demonstrably runs on the same client the host started.
    /// </remarks>
    /// <param name="client">The client to create sessions on. Must not be <see langword="null"/>.</param>
    /// <returns>An opener over <paramref name="client"/>.</returns>
    internal static CopilotChannelOpener Open(CopilotClient client) =>
        async (config, cancellationToken) =>
        {
            var session = await client.CreateSessionAsync(config, cancellationToken).ConfigureAwait(false);
            return new CopilotSessionChannel(client, session, deleteOnRelease: true);
        };

    /// <summary>
    ///     Returns an opener that creates sessions on one client and keeps each when it is released.
    /// </summary>
    /// <remarks>
    ///     For an agent's conversation, which continues on the next run. The session's state stays
    ///     under the client's data directory, resumable by <see cref="Resume"/>, until the host
    ///     deletes it.
    /// </remarks>
    /// <param name="client">The client to create sessions on. Must not be <see langword="null"/>.</param>
    /// <returns>An opener over <paramref name="client"/>.</returns>
    internal static CopilotChannelOpener OpenRetained(CopilotClient client) =>
        async (config, cancellationToken) =>
        {
            var session = await client.CreateSessionAsync(config, cancellationToken).ConfigureAwait(false);
            return new CopilotSessionChannel(client, session, deleteOnRelease: false);
        };

    /// <summary>
    ///     Returns a resumer that reopens existing sessions on one client and keeps each when it is
    ///     released.
    /// </summary>
    /// <param name="client">The client to resume sessions on. Must not be <see langword="null"/>.</param>
    /// <returns>A resumer over <paramref name="client"/>.</returns>
    internal static CopilotChannelResumer Resume(CopilotClient client) =>
        async (sessionId, config, cancellationToken) =>
        {
            var session = await client
                .ResumeSessionAsync(sessionId, config, cancellationToken)
                .ConfigureAwait(false);
            return new CopilotSessionChannel(client, session, deleteOnRelease: false);
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
    ///     then the caller's token, which <c>IProviderSession.SendAsync</c> and the agent's run
    ///     methods already take — so the application governs it, rather than this library inventing
    ///     a number or offering a knob nobody asked for.
    ///     </para>
    /// </remarks>
    public Task<AssistantMessageEvent?> SendAndWaitAsync(MessageOptions message, CancellationToken cancellationToken) =>
        _session.SendAndWaitAsync(message, Timeout.InfiniteTimeSpan, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>
    ///     Releases the session handle, and for a channel opened by <see cref="Open"/> then asks the
    ///     runtime to delete the session. See <see cref="ReleaseAsync"/> for why neither step may
    ///     throw.
    /// </remarks>
    public ValueTask DisposeAsync() =>
        ReleaseAsync(
            _session.DisposeAsync,
            _deleteOnRelease
                ? () => _client.DeleteSessionAsync(_session.SessionId, CancellationToken.None)
                : null);

    /// <summary>
    ///     Releases a session handle and, when asked, deletes the session's persisted state — never
    ///     throwing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Releasing a handle is documented to <em>preserve</em> a session's state on disk so it can
    ///     be resumed later. That is exactly right for an agent's conversation and exactly wrong for a
    ///     rotated or consolidation session, which would otherwise leave one preserved session behind
    ///     per rotation — so the deletion is a separate, optional step rather than part of release.
    ///     </para>
    ///     <para>
    ///     <b>Neither step may throw.</b> Release runs on the failure paths of a rotation, after every
    ///     agent run, and on the caller's own release, and none is a place a failure can be acted on.
    ///     Releasing a handle is not local teardown — it detaches over the runtime's transport, so a
    ///     connection going away underneath it throws, and at shutdown that is the ordinary case
    ///     rather than an exotic one. Left unguarded it would replace a consolidation that had
    ///     already succeeded with a detach failure, or mask the very exception a caller's catch block
    ///     was preserving. The deletion is best-effort for the same reason: the handle is already
    ///     released by then, so the worst outcome is recoverable disk state.
    ///     </para>
    ///     <para>
    ///     Factored out of <see cref="DisposeAsync"/> so the policy can be tested without a live
    ///     session: the SDK's session type cannot be constructed in a test.
    ///     </para>
    /// </remarks>
    /// <param name="detach">Releases the session handle.</param>
    /// <param name="delete">Deletes the session's persisted state, or <see langword="null"/> to keep it.</param>
    /// <returns>A task that completes when both steps have been attempted.</returns>
    internal static async ValueTask ReleaseAsync(Func<ValueTask> detach, Func<Task>? delete)
    {
        try
        {
            await detach().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Intentionally swallowed: see the remarks. Detaching reaches the runtime over its
            // transport, so this throws when the connection is already gone - which is exactly
            // when release is most likely to be running.
        }

        if (delete is null)
        {
            return;
        }

        try
        {
            await delete().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Intentionally swallowed: see the remarks. The handle is already released, and a
            // release that threw here would fail a rotation that has otherwise succeeded, or a
            // caller's release that has otherwise done its job.
        }
    }
}
