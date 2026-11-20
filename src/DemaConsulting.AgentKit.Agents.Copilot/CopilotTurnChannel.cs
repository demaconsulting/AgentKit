using Microsoft.Copilot;
using Microsoft.Copilot.Rpc;

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
///     this line; everything below it is <see cref="CopilotSessionChannel"/>, which forwards a
///     turn, owns one release and opens each session through one confining protocol, with the
///     decisions of the last two factored out where the tests reach them. That is the smallest
///     surface that makes the rest reachable, and it is the same reasoning that made
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
///     <b>Every production session is opened through one protocol.</b> <see cref="Open"/>,
///     <see cref="OpenRetained"/> and <see cref="Resume"/> all go through
///     <see cref="OpenConfinedAsync{TSession}"/>, the runtime half of the confinement
///     <c>CopilotAgentFactory</c> configures: before a session is created or resumed, every MCP
///     server the runtime discovers is disabled, so none is started; once it exists, its
///     installed plugins are cleared and any MCP server still enabled is disabled; and a session
///     in which a server stays enabled is refused — a created one released and deleted, a resumed
///     one only detached, so a conversation is never destroyed. These steps need the live client
///     and session, which the factory's configuration builders deliberately do not have, so they
///     live here.
///     </para>
///     <para>
///     Deliberately a forwarded turn, one guarded release and one opening protocol. It is the one
///     piece of this package no automated test reaches directly, so it is kept small enough to
///     read rather than made clever enough to need testing; the decisions — the release policy
///     and the opening protocol — are factored into <see cref="ReleaseAsync"/>,
///     <see cref="OpenConfinedAsync{TSession}"/> and <see cref="SealAsync"/>, which are tested with the
///     runtime's steps supplied as delegates.
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
            var session = await CreateConfinedSessionAsync(client, config, cancellationToken).ConfigureAwait(false);
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
            var session = await CreateConfinedSessionAsync(client, config, cancellationToken).ConfigureAwait(false);
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
            var session = await ResumeConfinedSessionAsync(client, sessionId, config, cancellationToken)
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
    ///     Disables, on a session configuration, every MCP server named, keeping every server it
    ///     already disables.
    /// </summary>
    /// <remarks>
    ///     <c>SessionMcpOptions.DisabledServers</c> takes exact names only — the SDK offers no
    ///     wildcard and no "disable all" switch — so the names come from the runtime's own discovery.
    ///     The factory's entry for the built-in GitHub server is kept, a name already present is not
    ///     added twice (compared ordinally, as the runtime keys its configuration), and a blank name,
    ///     which cannot name a server, is skipped.
    /// </remarks>
    /// <param name="config">The session configuration to update.</param>
    /// <param name="names">The names of the servers to disable.</param>
    internal static void DisableMcpServers(SessionOptionsBase config, IEnumerable<string> names)
    {
        config.Mcp.DisabledServers =
        [
            .. (config.Mcp.DisabledServers ?? [])
                .Concat(names.Where(name => !string.IsNullOrWhiteSpace(name)))
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    ///     Opens a session through the confinement's runtime half: discovers the ambient MCP servers
    ///     and disables them, opens the session, then seals it — abandoning it if the seal fails.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Why discovery comes first.</b> A server named in <c>DisabledServers</c> when the session
    ///     is created or cold-resumed is, per the SDK, "not started or authenticated" — so naming
    ///     every discovered server here keeps a server from ambient configuration from ever running
    ///     a process with the user's environment or identity. Disabling it only afterwards would start
    ///     it first.
    ///     </para>
    ///     <para>
    ///     <b>Why sealing follows anyway.</b> The runtime's discovery documents the user, workspace,
    ///     plugin and built-in sources, not the managed or account sources a session's configuration
    ///     can also carry, and the session's installed plugins can only be cleared once it exists. The
    ///     seal closes both and verifies the result.
    ///     </para>
    ///     <para>
    ///     <b>An unsealed session never reaches a caller.</b> If the seal fails — a server stays
    ///     enabled, or the runtime refuses a call — the session is abandoned by the supplied policy
    ///     and the failure propagates, so the run fails closed rather than continuing with a channel
    ///     open. A discovery that fails opens nothing.
    ///     </para>
    ///     <para>
    ///     Factored out with the runtime's steps as delegates for the same reason as
    ///     <see cref="ReleaseAsync"/>: the SDK's session type cannot be constructed in a test.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TSession">The session handle type.</typeparam>
    /// <param name="config">The session configuration, updated with the discovered servers before it is used.</param>
    /// <param name="discover">Discovers the names of the MCP servers the runtime would start.</param>
    /// <param name="open">Creates or resumes the session from <paramref name="config"/>.</param>
    /// <param name="seal">Seals the opened session; throws if it cannot.</param>
    /// <param name="abandon">Releases a session whose seal failed. Must not throw.</param>
    /// <param name="cancellationToken">Cancels the opening.</param>
    /// <returns>The opened, sealed session. Ownership passes to the caller.</returns>
    internal static async Task<TSession> OpenConfinedAsync<TSession>(
        SessionOptionsBase config,
        Func<CancellationToken, Task<IEnumerable<string>>> discover,
        Func<CancellationToken, Task<TSession>> open,
        Func<TSession, CancellationToken, Task> seal,
        Func<TSession, ValueTask> abandon,
        CancellationToken cancellationToken)
    {
        DisableMcpServers(config, await discover(cancellationToken).ConfigureAwait(false));

        var session = await open(cancellationToken).ConfigureAwait(false);
        try
        {
            await seal(session, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await abandon(session).ConfigureAwait(false);
            throw;
        }

        return session;
    }

    /// <summary>
    ///     Seals an opened session: clears its installed plugins, disables every MCP server it still
    ///     has enabled, and refuses it if any server remains enabled.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Plugins first.</b> An installed plugin can contribute MCP servers, hooks, skills and
    ///     agents. No create-time option removes installed plugins; the session-options patch that
    ///     replaces the list with an empty one is the call <c>CopilotClientMode.Empty</c> itself makes
    ///     after every create and resume, made here so it holds whatever the client's mode. Clearing
    ///     them before listing means a server a plugin contributed is gone before the listing.
    ///     </para>
    ///     <para>
    ///     <b>Disable, then verify.</b> The listing reports each server's enablement "after session
    ///     configuration and policy filtering", across every source — including the managed and
    ///     account sources discovery does not cover. Each server still enabled is disabled for this
    ///     session only (never in the user's persistent configuration), and the configuration is
    ///     listed again: a server the runtime keeps enabled refuses the session, naming it, because a
    ///     confined agent receives no MCP server.
    ///     </para>
    /// </remarks>
    /// <param name="clearInstalledPlugins">Replaces the session's installed plugins with none.</param>
    /// <param name="listConfigured">Lists the session's effective MCP configuration.</param>
    /// <param name="disable">Disables one named MCP server for the session.</param>
    /// <param name="cancellationToken">Cancels the seal.</param>
    /// <returns>A task that completes when the session is sealed.</returns>
    /// <exception cref="InvalidOperationException">An MCP server remained enabled after it was disabled.</exception>
    internal static async Task SealAsync(
        Func<CancellationToken, Task> clearInstalledPlugins,
        Func<CancellationToken, Task<IList<McpConfiguredServer>>> listConfigured,
        Func<string, CancellationToken, Task> disable,
        CancellationToken cancellationToken)
    {
        await clearInstalledPlugins(cancellationToken).ConfigureAwait(false);

        foreach (var name in EnabledServerNames(await listConfigured(cancellationToken).ConfigureAwait(false)))
        {
            await disable(name, cancellationToken).ConfigureAwait(false);
        }

        var stillEnabled = EnabledServerNames(await listConfigured(cancellationToken).ConfigureAwait(false));
        if (stillEnabled.Count > 0)
        {
            throw new InvalidOperationException(
                "The Copilot runtime kept the MCP server(s) "
                + string.Join(", ", stillEnabled.Select(name => $"'{name}'"))
                + " enabled for this session after AgentKit disabled them, so the session was refused: a "
                + "confined agent receives no MCP server. Run the Copilot client in CopilotClientMode.Empty on a "
                + "data directory the host owns and that configures no MCP servers, and check for MCP servers "
                + "the signed-in account or its organization provides.");
        }
    }

    /// <summary>
    ///     Creates the session-options patch that replaces a session's installed plugins with none.
    /// </summary>
    /// <remarks>
    ///     The list must be empty rather than <see langword="null"/>: an unset list is omitted from
    ///     the patch, which the runtime reads as "no change".
    /// </remarks>
    /// <returns>The patch.</returns>
    internal static SessionUpdatePluginOptions CreateNoInstalledPluginsPatch() => new() { Installed = [] };

    /// <summary>
    ///     Creates a session on a client through the confinement's runtime half, deleting it if it
    ///     cannot be sealed.
    /// </summary>
    /// <remarks>
    ///     The one path every production opener shares, and the path the opt-in live suite uses to
    ///     inspect a session opened exactly as production opens one. A session nothing has recorded
    ///     yet is released and deleted when its seal fails.
    /// </remarks>
    /// <param name="client">The client to create the session on.</param>
    /// <param name="config">The session configuration; updated with the discovered MCP servers.</param>
    /// <param name="cancellationToken">Cancels the creation.</param>
    /// <returns>The created, sealed session. Ownership passes to the caller.</returns>
    internal static Task<CopilotSession> CreateConfinedSessionAsync(
        CopilotClient client,
        CreateSessionOptions config,
        CancellationToken cancellationToken) =>
        OpenConfinedAsync(
            config,
            token => DiscoverMcpServerNamesAsync(client, config.WorkingDirectory, token),
            token => client.CreateSessionAsync(config, token),
            SealSessionAsync,
            session => ReleaseAsync(
                session.DisposeAsync,
                () => client.DeleteSessionAsync(session.SessionId, CancellationToken.None)),
            cancellationToken);

    /// <summary>
    ///     Resumes a session on a client through the confinement's runtime half, detaching it — never
    ///     deleting it — if it cannot be sealed.
    /// </summary>
    /// <remarks>
    ///     A resumed session is the user's conversation: refusing to continue it must not destroy
    ///     it, so a failed seal only releases the handle.
    /// </remarks>
    /// <param name="client">The client to resume the session on.</param>
    /// <param name="sessionId">The runtime's identifier for the session.</param>
    /// <param name="config">The resume configuration; updated with the discovered MCP servers.</param>
    /// <param name="cancellationToken">Cancels the resumption.</param>
    /// <returns>The resumed, sealed session. Ownership passes to the caller.</returns>
    internal static Task<CopilotSession> ResumeConfinedSessionAsync(
        CopilotClient client,
        string sessionId,
        ResumeSessionOptions config,
        CancellationToken cancellationToken) =>
        OpenConfinedAsync(
            config,
            token => DiscoverMcpServerNamesAsync(client, config.WorkingDirectory, token),
            token => client.ResumeSessionAsync(sessionId, config, token),
            SealSessionAsync,
            session => ReleaseAsync(session.DisposeAsync, delete: null),
            cancellationToken);

    /// <summary>
    ///     Discovers the names of the MCP servers the runtime would start for a session.
    /// </summary>
    /// <remarks>
    ///     The client's RPC surface exists only once the client has started, so the client is
    ///     started first. Starting is idempotent on a started client, and creating a session would
    ///     start one not yet started anyway; it goes through <see cref="CopilotClientStarter"/> so a
    ///     runtime that cannot load still reports why, as it would for a host that started the
    ///     client itself.
    /// </remarks>
    /// <param name="client">The client to discover on.</param>
    /// <param name="workingDirectory">The session's working directory, whose workspace configuration is included.</param>
    /// <param name="cancellationToken">Cancels the discovery.</param>
    /// <returns>The discovered server names.</returns>
    private static async Task<IEnumerable<string>> DiscoverMcpServerNamesAsync(
        CopilotClient client,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        await CopilotClientStarter.StartAsync(client, cancellationToken).ConfigureAwait(false);

        var discovered = await client.Rpc.Mcp
            .DiscoverAsync(workingDirectory, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return [.. discovered.Servers.Select(server => server.Name)];
    }

    /// <summary>
    ///     Seals a live session through its RPC surface; see <see cref="SealAsync"/> for the policy.
    /// </summary>
    /// <param name="session">The session to seal.</param>
    /// <param name="cancellationToken">Cancels the seal.</param>
    /// <returns>A task that completes when the session is sealed.</returns>
    private static Task SealSessionAsync(CopilotSession session, CancellationToken cancellationToken) =>
        SealAsync(
            token => session.Rpc.Options.UpdateAsync(plugins: CreateNoInstalledPluginsPatch(), cancellationToken: token),
            async token => (await session.Rpc.Mcp.ListConfiguredAsync(token).ConfigureAwait(false)).Servers,
            (name, token) => session.Rpc.Mcp.DisableAsync(name, cancellationToken: token),
            cancellationToken);

    /// <summary>
    ///     Names the servers a listing reports enabled.
    /// </summary>
    /// <param name="servers">The session's effective MCP configuration.</param>
    /// <returns>The names of the enabled servers, in listing order.</returns>
    private static List<string> EnabledServerNames(IEnumerable<McpConfiguredServer> servers) =>
        [.. servers.Where(server => server.Enabled).Select(server => server.Name)];

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
