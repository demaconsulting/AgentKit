using System.Collections.Frozen;
using Microsoft.Agents.AI;
using Microsoft.Copilot;
using Microsoft.Copilot.Rpc;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     Builds a Microsoft Agent Framework <see cref="AIAgent"/> from a GitHub Copilot
///     <see cref="CopilotClient"/> and a supplied tool list, suppressing the built-in tools the
///     Copilot runtime would otherwise inject.
/// </summary>
/// <remarks>
///     <para>
///     Copilot arrives as a complete agent runtime carrying its own tools — shell, fetch, file
///     reading and writing, and more. Supplying an application's own tools is not enough: unless
///     the built-ins are suppressed, a document assistant silently retains command execution.
///     <c>SessionToolOptions.AvailableTools</c> is an allow-list, so this factory derives it from
///     the <em>same</em> collection it assigns to <c>SessionToolOptions.Definitions</c>, which
///     makes the tool set and its allow-list impossible to drift apart. Every entry is
///     source-qualified as <c>custom:&lt;name&gt;</c>: measured against the live runtime, a bare
///     name also admits a built-in tool of the same name, so the qualifier is what makes the
///     allow-list name only the tools this package registered. This derivation is the
///     safety-critical behavior of the package.
///     </para>
///     <para>
///     <b>Every Copilot session this package builds is configured here, on one path.</b> The agent
///     path goes through <see cref="BuildSessionConfig"/> to create a session and
///     <see cref="BuildResumeSessionConfig"/> to resume one; the session-engine paths —
///     <see cref="CopilotProviderSessionFactory"/> and <see cref="CopilotSummarizer"/> — go through
///     <see cref="BuildEngineSessionConfig"/>. All of them end in <see cref="ApplyConfinement"/>, which is
///     the single site where the allow-list is derived, every channel of runtime-injected
///     capability a session's options can reach is withheld, and the default-safe permission
///     handler is installed. A second builder with its own derivation is precisely the drift this
///     arrangement exists to prevent, so there is not one. The half of the confinement that needs
///     the live client and session — disabling every ambient MCP server for the session before any
///     turn and clearing the session's installed plugins — is applied by
///     <see cref="CopilotSessionChannel"/>, on the one path
///     every production opener and resumer shares.
///     </para>
///     <para>
///     <b>The two paths differ in exactly one respect, deliberately.</b>
///     <see cref="BuildEngineSessionConfig"/> raises the Copilot runtime's own compaction threshold
///     clear of the session engine's rotation point; <see cref="BuildSessionConfig"/> leaves the
///     runtime's compaction alone. A session the AgentKit engine drives has an AgentKit compactor
///     behind it, and two compactors reading the same occupancy signal would fight — see
///     <see cref="BuildEngineSessionConfig"/> for the full argument. A plain Copilot agent has no
///     AgentKit compactor behind it, so changing the runtime's would remove protection rather than
///     prevent a conflict. Do not "tidy" the asymmetry away.
///     </para>
///     <para>
///     <b>Permission handling is default-deny.</b> When no handler is supplied, the factory
///     installs one that approves exactly the supplied tools by name, one call at a time, and
///     rejects every other request — every built-in shell, read, write and fetch request, and
///     every custom tool the host did not supply. A host may supply its own handler, which then
///     replaces the default outright; it is not composed with it, so a host that does so forfeits
///     default-deny unless its own handler reproduces it. Either way, each session's permission
///     mode is pinned to <c>PermissionMode.Manual</c>, so every request reaches the handler: the
///     runtime's other modes would approve requests without asking it.
///     </para>
///     <para>
///     <b>Built-in tool names are refused.</b> A supplied tool that carries the name of one of the
///     runtime's built-in tools — <c>view</c>, <c>powershell</c>, <c>bash</c> and the like — is
///     refused with an <see cref="ArgumentException"/> before any session exists, because the
///     runtime would refuse the session itself, later and less legibly. AgentKit never marks a tool
///     as overriding a built-in. See <see cref="ReservedBuiltInToolNames"/>.
///     </para>
///     <para>
///     <b>Ownership contract.</b> The host constructs, starts, and disposes the
///     <see cref="CopilotClient"/>; this factory takes no ownership of it, and nothing it builds
///     ever disposes it. The rule is unambiguous: <em>whoever created the client disposes it</em>.
///     </para>
///     <para>
///     <b>Client precondition: <c>CopilotClientMode.Empty</c> on a data directory the host
///     owns.</b> The integration requires it, and AgentKit cannot enforce it: the SDK exposes no
///     way to read a client's mode back. AgentKit therefore does not rely on the mode for anything
///     that reaches the model. Independently of the mode, it applies on every session each
///     control that the session's options and the live session can reach: the custom
///     allow-list and the pinned default-deny permission handling; every runtime option channel
///     — skills, custom instructions, hooks, memory, sub-agents, git operations and commit
///     co-author trailers, extensions, the cross-session store; the host environment context in
///     the system prompt; experimental features; embedding retrieval; the session's installed
///     plugins; and every MCP server the session lists, disabled and then verified, with the
///     session refused if one stays enabled. Servers the runtime's MCP discovery returns are named
///     in the session's disabled servers before the session exists, so the runtime does not start
///     or authenticate them. The SDK documents that discovery as covering the user, workspace,
///     plugin and builtin sources. A server from a source it cannot enumerate before creation,
///     such as a managed catalog or a signed-in account, may start, and authenticate with the host
///     environment, while the session is created. The seal disables any such server before any
///     turn runs, and the session is refused if one stays enabled. This guarantees that the model
///     cannot reach an ambient MCP server; it does not prevent one from running. A host that must
///     also keep such servers from running supplies a <c>CopilotClientMode.Empty</c> client on a
///     clean data directory it owns. That removes the sources the directory holds, namely its MCP
///     configuration and installed plugins. SDK 2.0.1 does not document that Empty mode or a clean
///     data directory keeps a managed-catalog or account server from starting; an account server
///     follows the signed-in account and its organization's policy. The client therefore narrows
///     those sources rather than guaranteeing that none starts. What a client that violates the
///     precondition can still add is host-side
///     behavior no session option reaches: settings in a shared data directory such as
///     user-level hooks, credential storage in the system keychain rather than the data
///     directory, telemetry and runtime file logging, and anything the host attaches at client
///     level, such as plugin directories or extensions. Those are the host's to control.
///     </para>
///     <para>
///     <b>No image-promoting decorator.</b> Unlike the <c>IChatClient</c> adapter, this factory
///     does not install <c>ImagePromotingChatClient</c>: the Copilot runtime already delivers an
///     image a tool returns to the model — measured against the live runtime and re-verified by the
///     opt-in live suite — so promoting it onto a user message would duplicate content the model
///     already received. A future maintainer should not "helpfully" add it here.
///     </para>
///     <para>
///     <b>Model selection belongs to the host.</b> Copilot backs a session with a model, and which
///     model that is materially changes how well a confined agent uses its tools. The factory
///     therefore lets the host name one, and applies it only when the host does: a host that names
///     no model gets the runtime's own default. Reaching the setting must not require going around
///     this factory, because a session configured by hand silently forfeits the built-in
///     suppression, the withheld injection channels, and the default-deny permission handler above.
///     </para>
///     <para>
///     This factory holds no state and shares no code with the ChatClient adapter.
///     </para>
/// </remarks>
public static class CopilotAgentFactory
{
    /// <summary>
    ///     The share of the context window at which the Copilot runtime is allowed to compact a
    ///     session AgentKit's engine drives.
    /// </summary>
    /// <remarks>
    ///     Raised well above the engine's own rotation point so the engine always acts first. The
    ///     runtime's default is 0.80 and the engine rotates at 0.70, which leaves a tenth of the
    ///     window between them — close enough that a single turn returning a large tool result can
    ///     cross it in one step. It is not set to 1.0: the runtime's last-resort behavior is worth
    ///     keeping for the case where something extraordinary happens, and this engine refuses a
    ///     session the runtime has rewritten rather than silently continuing, so the outcome is
    ///     diagnosable either way.
    /// </remarks>
    private const double RuntimeCompactionThreshold = 0.95;

    /// <summary>
    ///     The name of the GitHub MCP server the Copilot runtime starts for every session unless told
    ///     not to.
    /// </summary>
    internal const string BuiltInGitHubMcpServer = "github-mcp-server";

    /// <summary>
    ///     The names of the Copilot runtime's built-in tools, which no supplied tool may carry.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Why the names are refused here.</b> The runtime refuses to create a session that
    ///     registers a custom tool under a built-in's name unless the tool is marked as overriding
    ///     the built-in — and AgentKit never marks it so, because an override would silently replace
    ///     the runtime's tool with the application's under a name the model already associates with
    ///     other behavior. Left to the runtime, the collision surfaces only when a session is
    ///     created, on the first run, as a remote error. Refused here, it surfaces at composition
    ///     time as an <see cref="ArgumentException"/> naming the tool.
    ///     </para>
    ///     <para>
    ///     <b>A conservative union across platforms, matched case-insensitively.</b> The built-in
    ///     set depends on the platform — the shell family is <c>powershell</c> on Windows and
    ///     <c>bash</c> elsewhere — so the union is reserved, and an application composed on one
    ///     platform does not fail later on another. It holds the names the runtime lists on Windows,
    ///     the SDK's own isolated built-in set, the shell family the runtime uses off Windows, and a
    ///     few model- or feature-gated built-ins. Over-reserving costs only a rename;
    ///     under-reserving still fails safe, because the runtime itself refuses at session creation.
    ///     The opt-in live suite lists the runtime's built-ins on the platform it runs on and fails
    ///     if any is missing here, so a runtime that adds one is caught by the weekly run rather than
    ///     by an application.
    ///     </para>
    /// </remarks>
    internal static readonly FrozenSet<string> ReservedBuiltInToolNames = new[]
    {
        // Listed by the runtime on Windows.
        "create", "edit", "glob", "grep", "list_agents", "list_powershell", "powershell",
        "read_agent", "read_powershell", "skill", "stop_powershell", "task", "view", "web_fetch",
        "write_agent", "write_powershell",

        // The shell family the runtime uses on Linux and macOS.
        "bash", "read_bash", "write_bash", "stop_bash", "list_bash",

        // Model- or feature-gated built-ins.
        "apply_patch", "str_replace_editor", "report_intent", "update_todo", "web_search",
        "image_generation",
    }
    .Concat(BuiltInTools.Isolated)
    .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Builds an agent from a Copilot client and a supplied tool list, with the runtime's
    ///     built-in tools suppressed.
    /// </summary>
    /// <remarks>
    ///     <paramref name="model"/> is the last parameter rather than sitting beside
    ///     <paramref name="instructions"/>, where it would read more naturally, so that every
    ///     existing positional call keeps binding to the parameter it always bound to. Inserting it
    ///     earlier would shift <paramref name="onPermissionRequest"/> and <paramref name="name"/>
    ///     and break source compatibility for a released public API; a slightly awkward position is
    ///     the cheaper price.
    ///     <para>
    ///     A run of the agent fails with an <see cref="InvalidOperationException"/> naming the server
    ///     when the runtime keeps an MCP server enabled for the run's session after AgentKit disabled
    ///     it: the session is refused rather than handed to the model with that server available.
    ///     </para>
    /// </remarks>
    /// <param name="client">
    ///     The Copilot client the agent runs on. Must not be <see langword="null"/>. The host owns
    ///     the client: it constructs, starts, and disposes it, and nothing this factory builds ever
    ///     disposes it. Must be in <c>CopilotClientMode.Empty</c> on a data directory the host
    ///     owns — a precondition AgentKit cannot detect; see the remarks on this class. A client not yet
    ///     started is started by the agent's first run, through <see cref="CopilotClientStarter"/>.
    /// </param>
    /// <param name="tools">
    ///     The tools the agent may call. Must not be <see langword="null"/> or empty, must contain
    ///     no <see langword="null"/> entry, and must carry no two tools of the same name. The
    ///     session's allow-list is derived from this collection.
    /// </param>
    /// <param name="instructions">The system instructions for the agent, if any.</param>
    /// <param name="onPermissionRequest">
    ///     The permission handler. When <see langword="null"/>, a default-deny handler is installed
    ///     that approves exactly the supplied tools by name, one call at a time, and rejects
    ///     everything else. A supplied handler replaces that default rather than being composed with
    ///     it, so it must reproduce default-deny itself if the host wants it.
    /// </param>
    /// <param name="name">
    ///     The name of the agent, if any. When <see langword="null"/> or blank the agent reports
    ///     <c>GitHub Copilot Agent</c>.
    /// </param>
    /// <param name="model">
    ///     The Copilot model to back the session — for example <c>gpt-5.4-mini</c>. When
    ///     <see langword="null"/>, empty, or whitespace, no model is set and the Copilot runtime
    ///     applies its own default, which is the behavior every caller that omits this parameter
    ///     gets. The name is not validated here: an unrecognized name is rejected by the runtime at
    ///     session time, because only the runtime knows which models the signed-in user may use.
    /// </param>
    /// <param name="workingDirectory">
    ///     The working directory the runtime gives the agent's sessions — typically the root of the
    ///     workspace the tools are confined to. When <see langword="null"/>, empty, or whitespace,
    ///     none is set and the runtime uses the host process's working directory. In-process hosting
    ///     has no client-level working directory, so this is the only place a host can set one
    ///     without configuring a session by hand, which would forfeit the confinement. It is not
    ///     validated here: the runtime resolves it when a session is created. It grants nothing —
    ///     what the agent may read or write is decided by the tools' own path policy.
    /// </param>
    /// <returns>An agent that runs on the supplied client with only the supplied tools available.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="client"/> or <paramref name="tools"/> is <see langword="null"/>, or a
    ///     tool in <paramref name="tools"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> is empty, two tools carry the same name, or a tool carries the
    ///     name of one of the Copilot runtime's built-in tools — <c>view</c>, <c>powershell</c>,
    ///     <c>bash</c> and the like, compared case-insensitively. Rename such a tool.
    /// </exception>
    /// <example>
    ///     <para>
    ///     Assembling a complete agent on the GitHub Copilot runtime. The host constructs and starts
    ///     the client and keeps ownership of it; this factory derives the session allow-list from
    ///     the same tool list it publishes, which is what suppresses the runtime's built-in shell,
    ///     fetch and file tools. In a real application <c>textFiles</c> and
    ///     <c>images</c> are <c>new TextFilePack()</c> and <c>new ImagePack()</c> from
    ///     the <c>DemaConsulting.AgentKit.Tools</c> package.
    ///     </para>
    ///     <code>
    ///     public async Task&lt;AIAgent&gt; CreateAgentAsync(
    ///         string workspace,
    ///         string session,
    ///         IToolPack textFiles,
    ///         IToolPack images,
    ///         CancellationToken cancellationToken)
    ///     {
    ///         // The anchor is the workspace; the grants say what may be read and what may be written.
    ///         var policy = new PathPolicy(
    ///             workingDirectory: workspace,
    ///             grants: [PathRule.ReadOnly(workspace), PathRule.ReadWrite(session)]);
    ///
    ///         IList&lt;AIFunction&gt; tools =
    ///         [
    ///             .. new ToolPackBuilder(policy)
    ///                 .WithHostCapabilities(HostCapabilities.Vision)
    ///                 .Add(textFiles)
    ///                 .Add(images)
    ///                 .Build()
    ///         ];
    ///
    ///         // The host owns the client: it constructs, starts, and disposes it. The integration
    ///         // requires Empty mode on a data directory the host owns; AgentKit withholds the
    ///         // runtime's ambient channels from the model on every session itself.
    ///         var client = new CopilotClient(new CopilotClientOptions
    ///         {
    ///             Mode = CopilotClientMode.Empty,
    ///             Runtime = RuntimeConnection.ForInProcess(baseDirectory: session),
    ///             GitHub = new ClientGitHubOptions { UseLoggedInUser = true },
    ///         });
    ///
    ///         // Started through the starter so a runtime that cannot load says why.
    ///         await CopilotClientStarter.StartAsync(client, cancellationToken);
    ///
    ///         return CopilotAgentFactory.Create(
    ///             client,
    ///             tools,
    ///             instructions: "You are a document assistant confined to the permitted locations.",
    ///             name: "document-assistant",
    ///             workingDirectory: workspace);
    ///     }
    ///     </code>
    /// </example>
    public static AIAgent Create(
        CopilotClient client,
        IList<AIFunction> tools,
        string? instructions = null,
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>? onPermissionRequest = null,
        string? name = null,
        string? model = null,
        string? workingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        // A conversation's sessions are kept when released, because the conversation continues on
        // the next run; a run made without a conversation deletes its session, because nothing could
        // ever resume it. The host still owns the client; nothing here disposes it.
        return CreateAgent(
            tools,
            instructions,
            onPermissionRequest,
            name,
            model,
            workingDirectory,
            CopilotSessionChannel.OpenRetained(client),
            CopilotSessionChannel.Resume(client),
            CopilotSessionChannel.Open(client));
    }

    /// <summary>
    ///     Builds an agent over supplied channel factories, with every session it opens confined to
    ///     the supplied tools.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The test seam behind <see cref="Create"/>: production passes channels over the host's
    ///     client, a test passes a scripted runtime, and everything else — validation, the snapshot of
    ///     the tool list, and the per-run configuration builders — is shared.
    ///     </para>
    ///     <para>
    ///     AgentKit carries its own <see cref="AIAgent"/> over Copilot sessions because the SDK ships
    ///     no Agent Framework agent. Each run builds a fresh configuration through the same
    ///     confinement — <see cref="BuildSessionConfig"/> to create, <see cref="BuildResumeSessionConfig"/>
    ///     to resume — so a conversation is confined identically on every run.
    ///     </para>
    /// </remarks>
    /// <param name="tools">The tools the agent may call.</param>
    /// <param name="instructions">The system instructions, if any.</param>
    /// <param name="onPermissionRequest">The permission handler, or <see langword="null"/> for the safe default.</param>
    /// <param name="name">The agent's name, if any.</param>
    /// <param name="model">The model to back the sessions, or <see langword="null"/>/blank for the runtime's default.</param>
    /// <param name="workingDirectory">The sessions' working directory, or <see langword="null"/>/blank for the runtime's default.</param>
    /// <param name="opener">Creates the runtime session for a conversation's first run.</param>
    /// <param name="resumer">Resumes the recorded runtime session on every later run.</param>
    /// <param name="ephemeralOpener">
    ///     Creates the runtime session for a run made without a conversation, which is deleted when
    ///     the run ends; <see langword="null"/> to use <paramref name="opener"/>.
    /// </param>
    /// <returns>The agent.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tools"/> is <see langword="null"/>, or contains a <see langword="null"/> entry.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> is empty, or two tools carry the same name.
    /// </exception>
    internal static CopilotAgent CreateAgent(
        IList<AIFunction> tools,
        string? instructions,
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>? onPermissionRequest,
        string? name,
        string? model,
        string? workingDirectory,
        CopilotChannelOpener opener,
        CopilotChannelResumer resumer,
        CopilotChannelOpener? ephemeralOpener = null)
    {
        ValidateTools(tools);

        // A snapshot of the host's list, so a later change to it cannot widen or narrow sessions the
        // agent opens after construction: every run is confined to the tools validated here.
        IList<AIFunction> published = [.. tools];

        return new CopilotAgent(
            () => BuildSessionConfig(published, instructions, onPermissionRequest, model, workingDirectory),
            () => BuildResumeSessionConfig(published, instructions, onPermissionRequest, model, workingDirectory),
            opener,
            ephemeralOpener ?? opener,
            resumer,
            name);
    }

    /// <summary>
    ///     Builds the session configuration that suppresses the built-in tools, deriving the
    ///     allow-list from the supplied tools.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Exposed as a seam so a test can assert the safety-critical property — that
    ///     <c>SessionToolOptions.AvailableTools</c> is derived from the same collection as
    ///     <c>SessionToolOptions.Definitions</c> — without a live <see cref="CopilotClient"/>. Both
    ///     are built from <paramref name="tools"/> in one place so the two cannot diverge.
    ///     </para>
    ///     <para>
    ///     This is the <em>agent</em> path. It deliberately leaves <c>InfiniteSessions</c> untouched,
    ///     so the Copilot runtime keeps compacting a plain agent's session as it always has, at its own
    ///     threshold: nothing else is watching that session's window. The session-engine path,
    ///     <see cref="BuildEngineSessionConfig"/>, raises that threshold for the opposite reason.
    ///     </para>
    ///     <para>
    ///     It also turns on the runtime's token streaming, which only the agent path presents: an
    ///     agent's caller reads a run as it happens, while the engine and the summarizer take only a
    ///     turn's final answer and would gain nothing but extra events.
    ///     </para>
    /// </remarks>
    /// <param name="tools">The tools to publish and allow.</param>
    /// <param name="instructions">The system instructions, if any.</param>
    /// <param name="onPermissionRequest">The permission handler, or <see langword="null"/> for the safe default.</param>
    /// <param name="model">
    ///     The Copilot model to back the session, or <see langword="null"/>/blank to leave
    ///     <c>CreateSessionOptions.Model</c> unset so the runtime applies its own default. Defaulted so a
    ///     caller that has no opinion about the model need not say so.
    /// </param>
    /// <param name="workingDirectory">
    ///     The session's working directory, or <see langword="null"/>/blank to leave it to the
    ///     runtime. Defaulted for the same reason as <paramref name="model"/>.
    /// </param>
    /// <returns>The configured session.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tools"/> is <see langword="null"/>, or contains a <see langword="null"/> entry.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> is empty, or two tools carry the same name.
    /// </exception>
    internal static CreateSessionOptions BuildSessionConfig(
        IList<AIFunction> tools,
        string? instructions,
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>? onPermissionRequest,
        string? model = null,
        string? workingDirectory = null)
    {
        ValidateTools(tools);

        var config = CreateSessionConfig(tools, instructions, onPermissionRequest, model, workingDirectory);
        config.Events.Streaming = true;

        return config;
    }

    /// <summary>
    ///     Builds the configuration an agent resumes a recorded session with: the same confinement,
    ///     model, working directory and instructions the session was created with.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>A resumed session is confined afresh, not trusted to remember.</b> The runtime applies
    ///     a resume's configuration to the session it reopens, so a resume built without the
    ///     allow-list, the withheld channels and the permission handler would hand an existing
    ///     conversation the runtime's defaults — every built-in tool, and permission requests no
    ///     handler answers. Every confinement setting lives on the options a created and a resumed
    ///     session share, so this builder reaches the same <see cref="ApplyConfinement"/> the create
    ///     path does and differs from <see cref="BuildSessionConfig"/> only in its type.
    ///     </para>
    ///     <para>
    ///     Like the create path, it leaves the runtime's own compaction alone and turns on streaming.
    ///     </para>
    /// </remarks>
    /// <param name="tools">The tools to publish and allow.</param>
    /// <param name="instructions">The system instructions, if any.</param>
    /// <param name="onPermissionRequest">The permission handler, or <see langword="null"/> for the safe default.</param>
    /// <param name="model">The model to back the session, or <see langword="null"/>/blank for the runtime's default.</param>
    /// <param name="workingDirectory">The session's working directory, or <see langword="null"/>/blank for the runtime's default.</param>
    /// <returns>The configuration to resume the session with.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tools"/> is <see langword="null"/>, or contains a <see langword="null"/> entry.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> is empty, or two tools carry the same name.
    /// </exception>
    internal static ResumeSessionOptions BuildResumeSessionConfig(
        IList<AIFunction> tools,
        string? instructions,
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>? onPermissionRequest,
        string? model = null,
        string? workingDirectory = null)
    {
        ValidateTools(tools);

        var config = new ResumeSessionOptions();

        ApplyConfinement(config, tools, onPermissionRequest);
        ApplySessionSettings(config, instructions, model, workingDirectory);
        config.Events.Streaming = true;

        return config;
    }

    /// <summary>
    ///     Builds the session configuration for a session whose context AgentKit's own session
    ///     engine manages: the same confinement the agent path receives, plus the Copilot runtime's
    ///     own compaction threshold raised clear of the engine's rotation point.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Why the runtime's compaction is held off here.</b> Copilot compacts its own session:
    ///     <c>InfiniteSessionConfig</c> defaults to enabled, with background compaction at 0.80 of
    ///     the window and buffer exhaustion at 0.95. AgentKit's session engine rotates at 0.70,
    ///     against the same occupancy signal. Left at that default, both compactors would act on one
    ///     conversation: Copilot would rewrite history underneath a session whose transcript the
    ///     engine believes it owns, and the occupancy the engine reads afterwards would move for
    ///     reasons it cannot see — so the engine would seed a replacement from a history the
    ///     provider no longer holds, and the two accounts of the conversation would diverge
    ///     silently. A tenth of the window is not a margin: one turn returning a large tool result
    ///     can cross it in a single step. <b>A future maintainer must not lower the threshold back
    ///     toward the engine's own.</b>
    ///     </para>
    ///     <para>
    ///     <b>The threshold is what the runtime honors; the enablement flag is not.</b> Measured
    ///     against the live runtime, a session created with <c>Enabled = false</c> compacted as soon
    ///     as its threshold was crossed, exactly as one created with it true did; the flag only
    ///     suppresses the session's workspace. It is still set, because it states the intent and
    ///     costs nothing if the runtime ever begins honoring it, but nothing here depends on it.
    ///     </para>
    ///     <para>
    ///     <b>The threshold is deliberately not 1.0, so a rewrite stays possible.</b> The runtime's
    ///     last-resort behavior is worth keeping, so the margin is a margin rather than a guarantee:
    ///     <see cref="CopilotSessionObserver"/> watches for every way the runtime reports reshaping
    ///     a conversation itself, and <see cref="CopilotProviderSession"/> refuses the next turn if
    ///     one arrives. That turns a silent divergence into a diagnosable failure.
    ///     </para>
    ///     <para>
    ///     <b>An empty tool list is accepted here and refused on the agent path.</b> A consolidation
    ///     runs on a session that must offer no tools at all, which is a correct engine-driven
    ///     session and an incorrect agent. The confinement is identical either way: an empty tool
    ///     list derives an empty allow-list and a permission handler that approves nothing, which is
    ///     the strongest confinement this factory can express rather than the weakest.
    ///     </para>
    /// </remarks>
    /// <param name="tools">
    ///     The tools to publish and allow. Must not be <see langword="null"/>; may be empty. Must
    ///     contain no <see langword="null"/> entry and no two tools of the same name.
    /// </param>
    /// <param name="instructions">The system instructions, if any.</param>
    /// <param name="model">
    ///     The Copilot model to back the session, or <see langword="null"/>/blank to leave
    ///     <c>CreateSessionOptions.Model</c> unset so the runtime applies its own default.
    /// </param>
    /// <param name="workingDirectory">
    ///     The session's working directory, or <see langword="null"/>/blank to leave it to the
    ///     runtime. A consolidation session, which offers no tools, passes none.
    /// </param>
    /// <returns>
    ///     The configured session, with the runtime's own compaction threshold raised clear of the
    ///     engine's rotation point.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tools"/> is <see langword="null"/>, or contains a <see langword="null"/> entry.
    /// </exception>
    /// <exception cref="ArgumentException">Two tools carry the same name.</exception>
    internal static CreateSessionOptions BuildEngineSessionConfig(
        IList<AIFunction> tools,
        string? instructions,
        string? model,
        string? workingDirectory = null)
    {
        ValidateToolNames(tools);

        var config = CreateSessionConfig(tools, instructions, onPermissionRequest: null, model, workingDirectory);

        // Enabled = false does not work, and is set anyway: measured against the live runtime, a
        // session created with it still compacted as soon as the threshold was crossed, identically
        // to one created with it true. It is kept because it states the intent and costs nothing if
        // the runtime ever starts honoring it - but nothing may depend on it.
        //
        // The threshold is honored, and is what actually holds the runtime off. It is raised rather
        // than the flag being trusted: AgentKit rotates at 0.70 of the window, so the runtime's
        // default of 0.80 leaves only a tenth of the window between the two. One turn returning a
        // large tool result can cross that gap in a single step, and then the runtime rewrites a
        // history this engine believes it owns. At 0.95 the margin is a quarter of the window, and
        // the runtime's own last-resort behavior is still there if something extraordinary happens.
        config.State.InfiniteSessions = new InfiniteSessionConfig
        {
            Enabled = false,
            BackgroundCompactionThreshold = RuntimeCompactionThreshold,
        };

        return config;
    }

    /// <summary>
    ///     Builds a confined session configuration from validated tools.
    /// </summary>
    /// <remarks>
    ///     The single place a <see cref="CreateSessionOptions"/> is constructed in this package. Both
    ///     creating builders reach it, which is what keeps the confinement, the model choice, the working
    ///     directory and the system message on one path; they differ only in which tool lists they
    ///     accept and in whether they adjust the runtime's own compaction afterwards.
    ///     <para>
    ///     A new object is built on every call and never reused: the SDK mutates the options it is
    ///     handed when it applies its mode defaults, and its <c>Clone</c> is shallow, so two sessions
    ///     created from one object — or from a clone of it — would share state the SDK has changed.
    ///     </para>
    /// </remarks>
    /// <param name="tools">The validated tools to publish and allow.</param>
    /// <param name="instructions">The system instructions, if any.</param>
    /// <param name="onPermissionRequest">The permission handler, or <see langword="null"/> for the safe default.</param>
    /// <param name="model">The model to back the session, or <see langword="null"/>/blank for the runtime's default.</param>
    /// <param name="workingDirectory">The session's working directory, or <see langword="null"/>/blank for the runtime's default.</param>
    /// <returns>The configured session.</returns>
    private static CreateSessionOptions CreateSessionConfig(
        IList<AIFunction> tools,
        string? instructions,
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>? onPermissionRequest,
        string? model,
        string? workingDirectory)
    {
        var config = new CreateSessionOptions();

        ApplyConfinement(config, tools, onPermissionRequest);
        ApplySessionSettings(config, instructions, model, workingDirectory);

        return config;
    }

    /// <summary>
    ///     Applies the host's own choices — model, working directory and instructions — to a session.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="ApplyConfinement"/> because these are the host's preferences
    ///     rather than the package's safety guarantees, and kept in one place so every session this
    ///     package builds carries them identically. Each is assigned only when the host supplied a
    ///     value: leaving a property untouched is what preserves the runtime's own default for every
    ///     caller that says nothing, so each parameter is purely additive. The instructions are the
    ///     content of the system message the confinement already configured, which the runtime
    ///     appends to its own prompt; this method never replaces that message, so the confinement's
    ///     removal of the host environment context survives every combination of settings. It must
    ///     therefore run after <see cref="ApplyConfinement"/>.
    /// </remarks>
    /// <param name="config">The session being configured.</param>
    /// <param name="instructions">The system instructions, if any.</param>
    /// <param name="model">The model to back the session, or <see langword="null"/>/blank for the runtime's default.</param>
    /// <param name="workingDirectory">The session's working directory, or <see langword="null"/>/blank for the runtime's default.</param>
    private static void ApplySessionSettings(
        SessionOptionsBase config,
        string? instructions,
        string? model,
        string? workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(model))
        {
            config.Model = model;
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            config.WorkingDirectory = workingDirectory;
        }

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            // The confinement already configured the system message to remove the runtime's host
            // environment context; the instructions are its content, which the runtime appends.
            config.SystemMessage!.Content = instructions;
        }
    }

    /// <summary>
    ///     Confines a session to the supplied tools: derives the allow-list from them, withholds the
    ///     runtime's injection channels, and installs a permission handler.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The one safety-critical block in this package, and it exists exactly once.</b>
    ///     <c>Definitions</c> and <c>AvailableTools</c> are assigned from the same collection in the
    ///     same two statements, so no reachable state has a published tool that is not allowed or an
    ///     allowed name that is not published. A second copy of this derivation — in a session
    ///     builder for the engine paths, say — is the drift the whole design exists to prevent, so
    ///     every builder calls this rather than repeating it.
    ///     </para>
    ///     <para>
    ///     It targets <see cref="SessionOptionsBase"/>, the options a created and a resumed session
    ///     share, so a session confined at creation can be confined identically when it is resumed.
    ///     </para>
    /// </remarks>
    /// <param name="config">The session being configured.</param>
    /// <param name="tools">The tools to publish and allow.</param>
    /// <param name="onPermissionRequest">The permission handler, or <see langword="null"/> for the safe default.</param>
    private static void ApplyConfinement(
        SessionOptionsBase config,
        IList<AIFunction> tools,
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>? onPermissionRequest)
    {
        // Definitions and AvailableTools are both derived from the same collection so the tool set
        // and its allow-list cannot drift apart. This is the suppression the package exists for.
        //
        // Each allow-list entry is source-qualified as custom:<name>, so the runtime matches only a
        // tool this client registered - never a built-in, MCP or plugin tool that carries the same
        // wire name. Measured against the live runtime, a bare-name allow-list admits a built-in
        // such as 'view' whenever a name matches. The runtime classifies tools at registration, not
        // by parsing names, so a colliding name cannot spoof the qualifier.
        config.Tools.Definitions = [.. tools];
        config.Tools.AvailableTools = [.. tools.Select(tool => QualifyCustomToolName(tool.Name))];

        // Every runtime-side transformation of a tool result is switched off. AgentKit's tools
        // bound their own output (ToolLimits); a runtime that diverted a large result to a temp
        // file would hand the model a path to a file no allowed tool can read. Tool search would
        // hide allowed tools behind a built-in search tool the allow-list does not grant.
        config.Tools.LargeOutput = new LargeToolOutputConfig { Enabled = false };
        config.Tools.Search = new ToolSearchConfig { Enabled = false };
        config.Tools.ManageScheduleEnabled = false;

        // The runtime's skills, instructions, hooks, memory, sub-agents, git context, MCP servers
        // and cross-session store are further channels of injected capability an untrained user
        // could not adjudicate; a confined agent receives only what its host attached. Set
        // explicitly on every session, so the confinement does not depend on the client's mode,
        // which AgentKit cannot read.
        config.Skills.Enabled = false;
        config.Skills.IncludedBuiltinSkills = [];
        config.Instructions.SkipCustomInstructions = true;
        config.Instructions.OnDemandDiscovery = false;
        config.Hooks.EnableRepositoryHooks = false;
        config.Git.EnableHostGitOperations = false;

        // Commit co-author trailers are off: an Empty-mode default, which the SDK forwards in any
        // other mode only when set, in the options patch it sends after the session opens.
        config.Git.CoauthorEnabled = false;
        config.State.EnableCrossSessionStore = false;
        config.State.Memory = new Microsoft.Copilot.MemoryConfiguration { Enabled = false };
        config.Agents.IncludedBuiltInAgents = [];
        config.Agents.LocalDiscoveryOnly = true;
        config.Extensions.Enabled = false;
        config.EnableConfigDiscovery = false;

        // The runtime's environment context - working directory, OS, git root, a directory listing
        // and the tool list - is removed from the system prompt. Empty mode removes it client-side;
        // it is removed here so no session carries host environment detail whatever mode the host's
        // client is in. For an Empty-mode client the wire payload is unchanged: the SDK promotes an
        // appended message to exactly this shape. The host's instructions, if any, become this
        // message's content in ApplySessionSettings, which the runtime appends in this mode too.
        config.SystemMessage = new SystemMessageConfig
        {
            Mode = SystemMessageMode.Customize,
            Sections = new Dictionary<SystemMessageSection, SectionOverride>
            {
                [SystemMessageSection.EnvironmentContext] = new SectionOverride { Action = SectionOverrideAction.Remove },
            },
        };

        // Experimental features and embedding retrieval are further runtime channels: the first can
        // add behavior the host never chose, the second retrieves context from a cache shared across
        // sessions. Both are Empty-mode defaults, set here so they hold in any mode.
        config.Experiments.EnableExperimentalMode = false;
        config.State.SkipEmbeddingRetrieval = true;

        // Image generation is a further capability the runtime can offer a session, and is
        // switched off for the same reason: a confined agent receives only what its host attached.
        config.Tools.ImageGeneration = new ImageGenerationConfig { Enabled = false };

        // Measured against the live runtime: even in Empty mode it starts its built-in GitHub MCP
        // server for every session. The custom: allow-list keeps that server's tools out of reach,
        // but the server is still started and authenticated with the user's GitHub identity, so it
        // is disabled outright rather than merely filtered. Every other MCP server - from the user,
        // workspace, plugin, builtin, managed or account source - is disabled before any turn by
        // the production channel, which needs the live client and session this builder does not
        // have: it adds every server the runtime discovers to this list before the session is
        // created, so none of those is started; then clears the session's installed plugins,
        // disables any server still enabled (a server discovery is not documented to report, such
        // as a managed catalog's or a signed-in account's, can start, and authenticate, during
        // creation; the seal keeps it from the model, not from running), and refuses the session
        // if one remains. See CopilotSessionChannel.
        config.Mcp.DisabledServers = [BuiltInGitHubMcpServer];

        // Every request goes to the handler. In the runtime's AllowAll mode tool, path and URL
        // requests are approved without asking it, and in Assisted mode a client may approve them
        // itself - either would bypass default-deny without anything here changing. Pinning Manual
        // keeps the handler the only authority, whatever mode ambient configuration would choose.
        config.Permissions.Mode = PermissionMode.Manual;
        config.Permissions.OnRequest = onPermissionRequest ?? CreateDefaultPermissionHandler(tools);
    }

    /// <summary>
    ///     Creates the safe-default permission handler: approve exactly the supplied tools by name,
    ///     reject everything else.
    /// </summary>
    /// <remarks>
    ///     A built-in request — shell, read, write, url and the rest — is never a
    ///     <see cref="PermissionRequestCustomTool"/>, so it can never match a supplied tool name
    ///     and is always rejected. Only a supplied custom tool, requested by its exact name, is
    ///     approved. The allow-list is captured once, so the handler cannot approve a name the
    ///     session did not publish.
    /// </remarks>
    /// <param name="tools">The tools whose names are approved.</param>
    /// <returns>A permission handler enforcing the allow-list.</returns>
    internal static Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>
        CreateDefaultPermissionHandler(IList<AIFunction> tools)
    {
        var permitted = new HashSet<string>(tools.Select(tool => tool.Name), StringComparer.Ordinal);

        return (request, _) =>
        {
            if (request is PermissionRequestCustomTool custom && permitted.Contains(custom.ToolName))
            {
                return Task.FromResult(PermissionDecision.ApproveOnce());
            }

            var requested = DescribeRequest(request);
            return Task.FromResult(PermissionDecision.Reject($"'{requested}' is not an allowed tool."));
        };
    }

    /// <summary>
    ///     Qualifies a custom tool name for the runtime's source-qualified allow-list.
    /// </summary>
    /// <param name="name">The tool's wire name.</param>
    /// <returns>The name as <c>custom:&lt;name&gt;</c>.</returns>
    internal static string QualifyCustomToolName(string name) => $"custom:{name}";

    /// <summary>
    ///     Names the tool a permission request concerns, for a rejection message and diagnostics.
    /// </summary>
    /// <param name="request">The request to describe.</param>
    /// <returns>The custom tool name, or the request kind for a built-in request.</returns>
    private static string DescribeRequest(PermissionRequest request)
    {
        return request switch
        {
            PermissionRequestCustomTool custom => custom.ToolName,
            _ => request.Kind ?? "(unknown)",
        };
    }

    /// <summary>
    ///     Rejects a tool list that a well-formed agent could not be built from.
    /// </summary>
    /// <remarks>
    ///     A missing, empty, or malformed tool list is a defect in the composing application. Two
    ///     tools sharing a name would make both the published tool set and the derived allow-list
    ///     ambiguous, so the collision is refused here.
    /// </remarks>
    /// <param name="tools">The tool list to validate.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tools"/> is <see langword="null"/>, or contains a <see langword="null"/> entry.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> is empty, or two tools carry the same name.
    /// </exception>
    internal static void ValidateTools(IList<AIFunction> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        if (tools.Count == 0)
        {
            throw new ArgumentException("At least one tool is required.", nameof(tools));
        }

        ValidateToolNames(tools);
    }

    /// <summary>
    ///     Rejects a tool list whose entries could not produce an unambiguous allow-list.
    /// </summary>
    /// <remarks>
    ///     Split out from <see cref="ValidateTools"/> because emptiness is the one rule the two
    ///     paths disagree about: an agent publishing no tools is a defect in its host, while a
    ///     session the engine drives for a consolidation must offer none. Everything else — a
    ///     missing list, a null entry, a duplicated name, a name the runtime reserves for a built-in
    ///     tool — is a defect on either path and is refused here for both, before any session
    ///     exists.
    /// </remarks>
    /// <param name="tools">The tool list to validate.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tools"/> is <see langword="null"/>, or contains a <see langword="null"/> entry.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Two tools carry the same name, or a tool carries the name of one of the runtime's built-in
    ///     tools (see <see cref="ReservedBuiltInToolNames"/>).
    /// </exception>
    internal static void ValidateToolNames(IList<AIFunction> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (tool is null)
            {
                throw new ArgumentNullException(nameof(tools), "A tool in the list is null.");
            }

            if (!seen.Add(tool.Name))
            {
                throw new ArgumentException(
                    $"Two tools carry the name '{tool.Name}'; tool names must be unique.",
                    nameof(tools));
            }

            if (ReservedBuiltInToolNames.Contains(tool.Name))
            {
                throw new ArgumentException(
                    $"The tool name '{tool.Name}' is the name of a built-in tool of the Copilot runtime; "
                    + "the runtime refuses to create a session that registers a custom tool under a "
                    + "built-in's name. Rename the tool.",
                    nameof(tools));
            }
        }
    }
}

