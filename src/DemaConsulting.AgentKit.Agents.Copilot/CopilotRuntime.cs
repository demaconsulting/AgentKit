using System.Collections;
using System.Collections.Frozen;
using Microsoft.Copilot;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     The GitHub Copilot runtime AgentKit starts, confines and owns: a runtime process in
///     <c>CopilotClientMode.Empty</c> whose environment withholds the host's <c>COPILOT_</c> and
///     <c>GITHUB_COPILOT_</c> variables apart from an allow-listed credential, network and telemetry-off set,
///     on a data directory AgentKit owns.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why AgentKit starts the runtime itself.</b> The confinement a Copilot session needs is
///     only partly a property of the session. The rest belongs to the client the session runs on:
///     its mode, its data directory and the environment of its runtime decide which MCP servers,
///     plugins and configuration exist before any session option is read. A client a host
///     builds can be in another mode, on a shared data directory that configures MCP servers, or
///     in-process with the host's whole environment — and the SDK offers no way to read any of
///     that back. So AgentKit does not take a client; it builds one, and only options that cannot
///     widen what reaches the model can be expressed (<see cref="CopilotRuntimeOptions"/>).
///     </para>
///     <para>
///     <b>What is forced.</b>
///     </para>
///     <list type="bullet">
///       <item><description>
///         <c>CopilotClientMode.Empty</c>, so the SDK supplies none of its coding-agent defaults.
///       </description></item>
///       <item><description>
///         A runtime spawned as a child process over standard input and output: by default the
///         Copilot CLI installed on the machine — the path the host names, or the one
///         <see cref="CopilotCli.FindInstalled()"/> finds — whose version is checked against
///         <see cref="CopilotCli.MinimumVersion"/> before it is used; or, when the host opts in with
///         <see cref="CopilotRuntimeSource.Bundled"/>, the runtime <c>Microsoft.Copilot.SDK</c>
///         provisions into the application's output. No runtime arguments, in-process hosting, TCP
///         connection or external runtime can be chosen: in-process hosting gives the runtime the
///         host's whole environment, and an external runtime's mode, data directory and environment
///         are not this client's. Everything else in this list applies to either executable alike.
///       </description></item>
///       <item><description>
///         A replacement environment for that process: the host's environment without any variable
///         whose name starts with <c>COPILOT_</c> or <c>GITHUB_COPILOT_</c>, apart from the
///         credential, network and telemetry-off variables in <see cref="PassThroughVariables"/>.
///         The runtime reads hundreds of such variables, among them approve-all switches,
///         instruction, skill, hook and plugin directories, provider rerouting and a runtime path
///         override; none of them reaches it. Every other host variable — <c>PATH</c>, the home and
///         temporary folders, proxy and certificate settings, <c>GH_TOKEN</c> and
///         <c>GITHUB_TOKEN</c> — is kept.
///       </description></item>
///       <item><description>
///         A data directory AgentKit owns, which becomes the runtime's <c>COPILOT_HOME</c>. A
///         missing or empty directory is created and marked as AgentKit's with a versioned marker
///         file; a directory whose marker is a regular file holding exactly what AgentKit writes is
///         reused, so conversations persist across restarts; any other non-empty directory — a
///         Copilot CLI home, a shared folder, or one whose marker is a link, a directory or other
///         content — is refused. The marker guards against reusing another tool's directory by
///         mistake; it is not a security boundary against a process that can already write to the
///         directory. Keep the directory private to the application, and give each concurrently
///         running runtime a directory of its own.
///       </description></item>
///       <item><description>
///         No plugin directories, extensions, installation handler, model request handler, session
///         filesystem or test options at client level.
///       </description></item>
///     </list>
///     <para>
///     <b>Signing in.</b> The runtime signs in from the host's token or token provider, a token in the
///     environment, the user's Copilot CLI sign-in, or its own fallbacks, in that order, with the GitHub
///     host that belongs to the source (see <see cref="CopilotRuntimeOptions"/>). The Copilot CLI's
///     token is obtained through a short-lived second runtime on a temporary home holding only the
///     account's host and login, and handed to this runtime as its token; nothing else from the user's
///     Copilot CLI home reaches it, and its mode, environment and data directory are unchanged.
///     <see cref="SignInSource"/>, <see cref="GitHubHost"/> and <see cref="GitHubLogin"/> report the
///     result.
///     </para>
///     <para>
///     <b>What this guarantees, per source, on every session AgentKit opens on this runtime.</b>
///     Before any session exists, the built-in GitHub MCP server is named disabled, so it is not
///     started or authenticated; Empty mode alone would start it on the first turn. No
///     user-configured MCP server can exist in a fresh data directory, and one added later is found
///     by the runtime's discovery and disabled before the session is created; Empty mode alone would
///     run it. Workspace MCP configuration is not loaded, because configuration discovery is off and
///     the data directory trusts no folder, and a workspace server that discovery reports is disabled
///     as well. No plugin is installed in a fresh data directory, and plugin directories cannot be
///     configured. These sources are <em>prevented</em>: the opt-in live suite shows the built-in
///     server never starting, probe servers in the data directory and in a workspace never executing,
///     and no plugin installed, while each of those servers does start without its protection. Servers from a managed catalog or a signed-in account follow
///     organization policy and the account; the SDK cannot enumerate them before a session exists, so
///     one may start, and authenticate, while the session is created. AgentKit then disables every
///     server the session's MCP configuration lists, before any turn runs, and refuses the session if
///     one stays enabled. That <em>contains</em> such a server; it does not prevent it. The session's
///     configuration listing is not complete — it does not report the built-in GitHub server even
///     while that server runs — so the post-create seal is defense in depth, not proof of absence. In
///     every case, no MCP tool is on a session's allow-list.
///     </para>
///     <para>
///     <b>Ownership.</b> Whoever starts the runtime disposes it. An agent, provider-session factory
///     or summarizer built on it never disposes it, and fails once it has been disposed. Disposing
///     the runtime stops the runtime process; the data directory and the conversations kept in it
///     remain.
///     </para>
///     <para>
///     <b>A disposed runtime is never started again.</b> The SDK restarts a disposed client on
///     demand — its start, create, resume and delete calls all reconnect a client that is not
///     running — so nothing built on the runtime keeps the client. Every session open, resume and
///     release reads it from the runtime when it runs, and after disposal that read fails with
///     <see cref="ObjectDisposedException"/> before the SDK is reached; an agent, provider-session
///     factory or summarizer cannot even be built on a disposed runtime.
///     </para>
///     <para>
///     Safe for concurrent use: sessions may be opened on one runtime from several threads, and
///     disposal is idempotent. Dispose the runtime only once no run is in progress on it: an open
///     that has already read the client when disposal begins races it, and the SDK may then
///     restart the client for that open. Disposal is not serialized against open runs, as is usual
///     for <see cref="IAsyncDisposable"/>.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Starting the runtime an application's Copilot agents run on, on a data directory private to
///     the application, and disposing it when the application is finished with it.
///     </para>
///     <code>
///     public async Task RunAsync(IList&lt;AIFunction&gt; tools, string workspace, CancellationToken cancellationToken)
///     {
///         var dataDirectory = Path.Combine(
///             Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
///             "MyApplication",
///             "copilot-runtime");
///
///         await using var runtime = await CopilotRuntime.StartAsync(
///             new CopilotRuntimeOptions(dataDirectory),
///             cancellationToken);
///
///         var agent = CopilotAgentFactory.Create(runtime, tools, workingDirectory: workspace);
///         var reply = await agent.RunAsync("Summarize notes.txt.", cancellationToken: cancellationToken);
///         Console.WriteLine(reply.Text);
///     }
///     </code>
/// </example>
public sealed class CopilotRuntime : IAsyncDisposable
{
    /// <summary>
    ///     The name of the file that marks a data directory as AgentKit's.
    /// </summary>
    /// <remarks>
    ///     A regular file holding exactly <see cref="OwnershipMarkerContent"/> is what lets a non-empty
    ///     directory be reused across restarts; its absence in a non-empty directory refuses a
    ///     directory another tool populated, and an entry of that name that is anything else refuses
    ///     the directory as carrying an invalid marker.
    /// </remarks>
    internal const string OwnershipMarkerFileName = ".agentkit-copilot-runtime";

    /// <summary>
    ///     The likely cause of a bundled runtime that failed to start.
    /// </summary>
    private const string BundledRuntimeCause =
        "The bundled runtime is the one Microsoft.Copilot.SDK provisions into this application's output "
        + "(runtimes/<rid>/native/copilot-runtime). If the error is that the runtime was not found, the most common "
        + "cause is a host application that does not reference Microsoft.Copilot.SDK directly: the SDK provisions its "
        + "runtime only into a project that references the package itself, so add a PackageReference to "
        + "Microsoft.Copilot.SDK to the host application, or run the installed Copilot CLI instead "
        + "(CopilotRuntimeSource.InstalledCli).";

    /// <summary>
    ///     Gets the exact bytes of the ownership marker: UTF-8 without a byte order mark, LF line
    ///     endings and a final line feed.
    /// </summary>
    /// <remarks>
    ///     The first line is a fixed signature with the marker's format version; the rest is for a
    ///     person who finds the file. It holds no secret, path or time stamp, so it is the same on every
    ///     machine, and a marker is accepted only when its bytes match exactly: AgentKit is its only
    ///     intended writer and writes one form, so a variant is not evidence that AgentKit created the
    ///     directory.
    /// </remarks>
    internal static ReadOnlySpan<byte> OwnershipMarkerContent =>
        "AgentKit Copilot runtime data directory, marker format 1\n"u8
        + "This directory is the data directory of a GitHub Copilot runtime started by AgentKit, which created\n"u8
        + "it and reuses it. Do not point the Copilot CLI or another tool at it, and do not edit this file.\n"u8;

    /// <summary>
    ///     The name prefixes of the host environment variables withheld from the runtime.
    /// </summary>
    private static readonly string[] WithheldPrefixes = ["COPILOT_", "GITHUB_COPILOT_"];

    /// <summary>
    ///     The comparison environment variable names are matched with on this platform.
    /// </summary>
    /// <remarks>
    ///     Case-insensitive on Windows, whose variable names are, and ordinal elsewhere — the same
    ///     rule the SDK applies when it reads the replacement environment.
    /// </remarks>
    private static readonly StringComparer VariableNameComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    ///     The client, or <see langword="null"/> once the runtime has been disposed.
    /// </summary>
    private CopilotClient? _client;

    /// <summary>
    ///     The session-level token provider that asks the host's provider, or <see langword="null"/>
    ///     when the runtime was not started with one.
    /// </summary>
    private readonly Func<GitHubTokenProviderArgs, Task<GitHubTokenProviderResult>>? _sessionTokenProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotRuntime"/> class over a client.
    /// </summary>
    /// <remarks>
    ///     Internal: production obtains a runtime only from <see cref="StartAsync"/>, which builds and
    ///     starts the client itself. A unit test passes a client that was never started, which spawns
    ///     nothing; the opt-in live suite passes a started client of another mode, to show the
    ///     session-level confinement holding on a client AgentKit would never build. A runtime built
    ///     without a launch or a status reports no CLI path, an empty runtime version and protocol 0;
    ///     one built without a sign-in reports <see cref="CopilotSignInSource.Unknown"/> and no host or
    ///     login.
    /// </remarks>
    /// <param name="client">The client this runtime owns and disposes.</param>
    /// <param name="dataDirectory">The prepared data directory's full path.</param>
    /// <param name="launch">What <see cref="StartAsync"/> launched, or <see langword="null"/> when it did not start this runtime.</param>
    /// <param name="status">The started runtime's status, or <see langword="null"/> when it was not read.</param>
    /// <param name="signIn">The sign-in plan the runtime started with, or <see langword="null"/> when there was none.</param>
    /// <param name="authentication">The started runtime's authentication status, or <see langword="null"/> when it was not read.</param>
    internal CopilotRuntime(
        CopilotClient client,
        string dataDirectory,
        CopilotRuntimeLaunch? launch = null,
        GetStatusResponse? status = null,
        CopilotSignInPlan? signIn = null,
        GetAuthStatusResponse? authentication = null)
    {
        _client = client;
        DataDirectory = dataDirectory;
        CliPath = launch?.CliPath;
        RuntimeVersion = status?.Version ?? string.Empty;
        ProtocolVersion = status?.ProtocolVersion ?? 0;
        SignInSource = signIn is not null && authentication is not null
            ? CopilotSignIn.Classify(authentication.AuthType, signIn)
            : CopilotSignInSource.Unknown;
        GitHubHost = authentication?.Host;
        GitHubLogin = authentication?.Login;
        SignIn = signIn;
        _sessionTokenProvider = signIn?.Provider is { } provider ? CopilotSignIn.CreateSessionTokenProvider(provider, signIn.Secrets) : null;
    }

    /// <summary>
    ///     Gets the sign-in plan the runtime started with, or <see langword="null"/> when there was none.
    /// </summary>
    /// <remarks>
    ///     Its <see cref="CopilotSignInPlan.Redact"/> removes the start token and every session token
    ///     the host's provider has returned from text AgentKit quotes, such as a failed reconnect's.
    /// </remarks>
    internal CopilotSignInPlan? SignIn { get; }

    /// <summary>
    ///     Gets the names of the <c>COPILOT_</c> variables that pass from the host's environment to the
    ///     runtime's.
    /// </summary>
    /// <remarks>
    ///     Each carries only a credential, network plumbing, or a switch that can only turn telemetry
    ///     off: <c>COPILOT_GITHUB_TOKEN</c> and <c>COPILOT_GH_HOST</c> (authentication and the GitHub
    ///     host), <c>COPILOT_SYSTEM_PROXY</c>, <c>COPILOT_PROXY_KERBEROS</c> and
    ///     <c>COPILOT_PROXY_KERBEROS_SPN</c> (proxying), and <c>COPILOT_TELEMETRY_DISABLED</c> and
    ///     <c>COPILOT_GITHUB_TELEMETRY_DISABLED</c>. None can add capability to a session.
    /// </remarks>
    internal static FrozenSet<string> PassThroughVariables { get; } = new[]
    {
        "COPILOT_GITHUB_TOKEN",
        "COPILOT_GH_HOST",
        "COPILOT_SYSTEM_PROXY",
        "COPILOT_PROXY_KERBEROS",
        "COPILOT_PROXY_KERBEROS_SPN",
        "COPILOT_TELEMETRY_DISABLED",
        "COPILOT_GITHUB_TELEMETRY_DISABLED",
    }
    .ToFrozenSet(VariableNameComparer);

    /// <summary>
    ///     Gets the full path of the data directory the runtime keeps its state in.
    /// </summary>
    /// <remarks>
    ///     The runtime's <c>COPILOT_HOME</c>: credentials the runtime stores itself (the system keychain
    ///     is not used in Empty mode) and the conversations kept for resumption live here. A token
    ///     AgentKit hands the runtime — the host's, or the Copilot CLI sign-in's — is not written here.
    ///     The runtime's caches do
    ///     not: they go to the platform's cache folder (<c>%LOCALAPPDATA%</c> on Windows,
    ///     <c>XDG_CACHE_HOME</c> or its default elsewhere). Remains available after disposal.
    /// </remarks>
    public string DataDirectory { get; }

    /// <summary>
    ///     Gets how the started runtime signed in to GitHub, as the runtime reports it.
    /// </summary>
    /// <remarks>
    ///     <see cref="CopilotSignInSource.Unknown"/> for a runtime <see cref="StartAsync"/> did not
    ///     start, or one that reported a kind of sign-in AgentKit does not know. Remains available after
    ///     disposal.
    /// </remarks>
    public CopilotSignInSource SignInSource { get; }

    /// <summary>
    ///     Gets the GitHub host the started runtime signed in to, as the runtime reports it, such as
    ///     <c>https://github.com</c> or <c>https://example.ghe.com</c>; <see langword="null"/> when not
    ///     known.
    /// </summary>
    /// <remarks>Remains available after disposal.</remarks>
    public string? GitHubHost { get; }

    /// <summary>
    ///     Gets the GitHub login the started runtime signed in as, as the runtime reports it;
    ///     <see langword="null"/> when not known.
    /// </summary>
    /// <remarks>Remains available after disposal.</remarks>
    public string? GitHubLogin { get; }

    /// <summary>
    ///     Gets the full path of the installed Copilot CLI this runtime runs, or <see langword="null"/>
    ///     when it runs the runtime <c>Microsoft.Copilot.SDK</c> bundles.
    /// </summary>
    /// <remarks>Remains available after disposal.</remarks>
    public string? CliPath { get; }

    /// <summary>
    ///     Gets the version the started runtime reports, such as <c>1.0.113</c>.
    /// </summary>
    /// <remarks>
    ///     Read from the runtime once it has started, so it is the version that actually runs — for an
    ///     installed CLI, the one built into the installed executable rather than a newer one that
    ///     auto-update or <c>copilot update</c> downloaded into the CLI's package cache. Remains available
    ///     after disposal.
    /// </remarks>
    public string RuntimeVersion { get; }

    /// <summary>
    ///     Gets the SDK protocol version the started runtime speaks.
    /// </summary>
    /// <remarks>
    ///     The SDK refuses at start a runtime whose protocol it does not support, so a started runtime
    ///     always reports a supported one. Remains available after disposal.
    /// </remarks>
    public int ProtocolVersion { get; }

    /// <summary>
    ///     Gets the client this runtime owns.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Internal: a host never reaches the client, so nothing can reconfigure it or open a session
    ///     on it outside AgentKit's confinement. The live suite reads it through
    ///     <c>InternalsVisibleTo</c>.
    ///     </para>
    ///     <para>
    ///     Read at every step that reaches the SDK, never captured: the SDK restarts a disposed
    ///     client on demand, so a captured client would outlive the runtime's disposal.
    ///     </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    internal CopilotClient Client =>
        Volatile.Read(ref _client) ?? throw new ObjectDisposedException(nameof(CopilotRuntime));

    /// <summary>
    ///     Throws if the runtime has been disposed.
    /// </summary>
    /// <remarks>
    ///     Called where something is built on the runtime — an agent, a provider-session factory, a
    ///     summarizer — so a disposed runtime is refused where the host composes its objects rather
    ///     than at their first use.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    internal void ThrowIfDisposed() => _ = Client;

    /// <summary>
    ///     Applies the runtime's session-level sign-in to a session AgentKit is about to create or
    ///     resume.
    /// </summary>
    /// <remarks>
    ///     When the runtime was started with the host's token provider, the session gets a token
    ///     provider that asks it, so the runtime can fetch the session's token and refresh it before it
    ///     expires; the SDK refuses the session if the provider fails, rather than signing it in some
    ///     other way. Otherwise the session's GitHub options are left untouched and the session uses the
    ///     runtime's sign-in.
    /// </remarks>
    /// <param name="config">The session configuration to update.</param>
    internal void ApplySessionSignIn(SessionOptionsBase config)
    {
        if (_sessionTokenProvider is not null)
        {
            config.GitHub.TokenProvider = _sessionTokenProvider;
        }
    }

    /// <summary>
    ///     Starts a confined Copilot runtime.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Prepares the data directory; resolves the runtime executable — for the installed CLI, the
    ///     path the host named or the one <see cref="CopilotCli.FindInstalled()"/> finds, whose version
    ///     is then read with <c>--no-auto-update --version</c> and refused when older than
    ///     <see cref="CopilotCli.MinimumVersion"/>; for the bundled runtime, the one the SDK
    ///     provisioned; resolves how the runtime signs in (see <see cref="CopilotRuntimeOptions"/>: the
    ///     host's token or token provider, a token in the environment, the user's Copilot CLI sign-in,
    ///     or the runtime's own fallbacks, each with the GitHub host that belongs to it); builds the
    ///     client options — Empty mode, that executable spawned over standard
    ///     input and output, the host's Copilot configuration variables withheld — and starts the
    ///     runtime process. Once it has started, its status and authentication are read: the version,
    ///     protocol and sign-in are kept on the runtime, and a runtime that is not signed in is refused. A start
    ///     that fails or is canceled at any step disposes the client, so no runtime process is left
    ///     behind.
    ///     </para>
    ///     <para>
    ///     Side effects: creates the data directory and its ownership marker when they do not exist;
    ///     for the installed CLI, runs it once to read its version (which, on the first run of a CLI
    ///     version, unpacks the CLI's package into the user's cache); may call the host's token provider
    ///     once; to use the Copilot CLI sign-in, reads the account's host and login from the CLI's
    ///     <c>config.json</c> and runs the same executable once more, briefly, on a temporary folder it
    ///     then deletes, to obtain the account's token from the system credential store; and starts a
    ///     child process that runs until the runtime is disposed. Reads the process environment once;
    ///     never changes it.
    ///     </para>
    /// </remarks>
    /// <param name="options">The runtime's options. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>The started runtime. The caller owns it and disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The data directory names a file, is a non-empty directory AgentKit did not create, or has a
    ///     marker entry that is not exactly the marker AgentKit writes — give AgentKit a new or empty
    ///     directory the application owns; or no installed Copilot CLI was found, the named one does
    ///     not exist, its version could not be read or is older than
    ///     <see cref="CopilotCli.MinimumVersion"/> — the message says where AgentKit looked, which
    ///     version it found and how to install or upgrade the CLI; or the runtime failed to start —
    ///     the message reports the runtime's own error and the likely cause, and the original failure
    ///     is the <see cref="Exception.InnerException"/>; or the started runtime is not signed in —
    ///     the message lists what each sign-in source found and how to sign in; or the host's token
    ///     provider failed or returned no token — a failure is the <see cref="Exception.InnerException"/>.
    /// </exception>
    /// <exception cref="IOException">The data directory or its marker could not be created or read.</exception>
    /// <exception cref="UnauthorizedAccessException">The data directory is not writable, or its marker is not readable.</exception>
    /// <exception cref="OperationCanceledException">The start was canceled. It propagates unchanged.</exception>
    public static async Task<CopilotRuntime> StartAsync(
        CopilotRuntimeOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        // The data directory is settled before any client exists, so a foreign directory is
        // refused without a runtime ever reading it.
        var dataDirectory = PrepareDataDirectory(options.DataDirectory);
        var hostEnvironment = Environment.GetEnvironmentVariables();

        // The executable is resolved, and an installed CLI's version checked, before any client
        // exists, so a CLI that cannot work is refused without a runtime process ever starting.
        var launch = await ResolveLaunchAsync(
            options,
            dataDirectory,
            hostEnvironment,
            CopilotCliSearchContext.Current(),
            CopilotCli.RunProcessAsync,
            cancellationToken).ConfigureAwait(false);

        // The sign-in is resolved before the confined runtime exists, so the token and host it
        // starts with are fixed by the host's options and the user's sign-ins, in a fixed order.
        var signIn = await CopilotSignIn.ResolveAsync(
            options,
            dataDirectory,
            hostEnvironment,
            launch,
            CopilotSignInContext.Current(),
            cancellationToken).ConfigureAwait(false);
        var clientOptions = BuildClientOptions(options, dataDirectory, hostEnvironment, launch.CliPath, signIn);

        // Started here and disposed on any failure: a runtime that never reaches the caller must not
        // leave its process running - including one that started but is not signed in.
        GetStatusResponse? status = null;
        GetAuthStatusResponse? authentication = null;
        var client = await StartOwnedAsync(
            () => new CopilotClient(clientOptions),
            async (created, token) =>
            {
                await StartClientAsync(created.StartAsync, launch, signIn, token).ConfigureAwait(false);
                (status, authentication) = await VerifyStartedAsync(created.GetStatusAsync, created.GetAuthStatusAsync, signIn, token)
                    .ConfigureAwait(false);
            },
            created => created.DisposeAsync(),
            cancellationToken).ConfigureAwait(false);

        var runtime = new CopilotRuntime(client, dataDirectory, launch, status, signIn, authentication);
        CopilotSignIn.Report(options.Logger, signIn, runtime.SignInSource, authentication!);
        return runtime;
    }

    /// <summary>
    ///     Deletes a conversation the runtime kept, with all of its persisted state.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     An agent's conversation is kept in the data directory after every run so it can resume, and
    ///     deleting it is the host's decision. The host passes the conversation's
    ///     <see cref="CopilotAgentSession.SessionId"/> here once it is finished with it. This is the
    ///     one session operation the runtime exposes, because the runtime's client is never handed
    ///     out: a host that could reach the client could also open a session outside AgentKit's
    ///     confinement.
    ///     </para>
    ///     <para>
    ///     Side effects: asks the runtime process to delete the session's persisted state. Safe to
    ///     call concurrently with sessions open on other conversations. The runtime reports a session
    ///     it does not hold, or a failure to delete one, through the SDK's own exception, which
    ///     propagates unchanged.
    ///     </para>
    /// </remarks>
    /// <param name="sessionId">
    ///     The runtime session to delete, from <see cref="CopilotAgentSession.SessionId"/>. Must not be
    ///     <see langword="null"/>, empty or whitespace.
    /// </param>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns>A task that completes when the runtime has deleted the session.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sessionId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> is empty or whitespace.</exception>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    /// <exception cref="OperationCanceledException">The deletion was canceled.</exception>
    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return Client.DeleteSessionAsync(sessionId, cancellationToken);
    }

    /// <summary>
    ///     Disposes the client, which stops the runtime process.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Idempotent and safe to call concurrently: only the first call disposes. Sessions open on
    ///     the runtime end with it; their persisted state stays in the data directory.
    ///     </para>
    ///     <para>
    ///     Afterwards, every agent, provider-session factory and summarizer built on the runtime fails
    ///     its next open, resume or deletion with <see cref="ObjectDisposedException"/> before the
    ///     SDK is reached, so nothing starts the runtime again. Dispose only once no run is in
    ///     progress: an open already past its read of the client can race the disposal.
    ///     </para>
    /// </remarks>
    /// <returns>A task that completes when the client has been disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        var client = Interlocked.Exchange(ref _client, null);
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Resolves a data directory and makes sure it is one AgentKit owns, creating and marking it
    ///     when it is missing or empty.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A fresh directory is what keeps user-configured MCP servers, installed plugins, trusted
    ///     folders and every other file-based setting from existing at all, so a directory another
    ///     tool populated is refused rather than inspected: a list of files to object to would go
    ///     stale with every runtime release. The runtime's own content in a directory AgentKit
    ///     marked is not policed; anything added there later is still found by discovery and
    ///     disabled before a session is created.
    ///     </para>
    ///     <para>
    ///     A directory is AgentKit's only when its <see cref="OwnershipMarkerFileName"/> entry is a
    ///     regular file — not a symbolic link, junction, other reparse point or directory — of exactly
    ///     the marker's length holding exactly <see cref="OwnershipMarkerContent"/>. Links are never
    ///     followed. A directory without the entry is refused as foreign; one whose entry is anything
    ///     else is refused as carrying an invalid marker, with a message of its own.
    ///     </para>
    ///     <para>
    ///     The marker guards against reusing a foreign Copilot home or a shared folder by mistake. It
    ///     is not a security boundary against a process that can already write to the directory: such
    ///     a process can write the exact marker, swap the entry between the check and the read, or add
    ///     configuration after the check, and a hard link is not detected. The guarantee rests on the
    ///     host keeping the directory private to the application.
    ///     </para>
    ///     <para>
    ///     A second process preparing the same directory at the same moment cannot make AgentKit
    ///     overwrite a marker: the marker is created only when absent, and one found already written
    ///     is validated like any other, so the race fails closed. A writer that populates a new or
    ///     empty directory before it is marked is caught by enumerating it again once the marker is
    ///     written; one that writes after that check is not. Hosts still give each runtime a
    ///     directory of its own.
    ///     </para>
    /// </remarks>
    /// <param name="dataDirectory">The data directory requested. Must not be blank.</param>
    /// <returns>The directory's full path.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The path names an existing file, a non-empty directory without AgentKit's marker, or a
    ///     directory whose marker entry is not exactly the marker AgentKit writes.
    /// </exception>
    /// <exception cref="IOException">The directory or its marker could not be created or read.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory is not writable, or its marker is not readable.</exception>
    internal static string PrepareDataDirectory(string dataDirectory) =>
        PrepareDataDirectory(dataDirectory, beforeMark: null);

    /// <summary>
    ///     Resolves a data directory and makes sure it is one AgentKit owns, with a hook that runs just
    ///     before a new or empty directory is marked.
    /// </summary>
    /// <remarks>
    ///     Behaves exactly as <see cref="PrepareDataDirectory(string)"/>, which passes
    ///     <see langword="null"/>.
    /// </remarks>
    /// <param name="dataDirectory">The data directory requested. Must not be blank.</param>
    /// <param name="beforeMark">
    ///     Invoked with the directory's full path once it is known to be new or empty, just before
    ///     it is marked; <see langword="null"/> outside tests, which use it to populate the directory
    ///     the way a concurrent writer could.
    /// </param>
    /// <returns>The directory's full path.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The path names an existing file, a non-empty directory without AgentKit's marker, or a
    ///     directory whose marker entry is not exactly the marker AgentKit writes.
    /// </exception>
    /// <exception cref="IOException">The directory or its marker could not be created or read.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory is not writable, or its marker is not readable.</exception>
    internal static string PrepareDataDirectory(string dataDirectory, Action<string>? beforeMark)
    {
        var fullPath = Path.GetFullPath(dataDirectory);
        var marker = Path.Combine(fullPath, OwnershipMarkerFileName);

        // A file where the directory should be cannot become a data directory.
        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"The Copilot data directory '{fullPath}' is a file. Give AgentKit a new or empty directory "
                + "the application owns.");
        }

        if (Directory.Exists(fullPath))
        {
            // A directory carrying exactly the marker AgentKit writes is its own, whatever the runtime
            // has written there since; an entry of that name that is anything else is refused.
            if (MarkerEntryExists(marker))
            {
                var reason = ValidateMarker(marker);
                return reason is null ? fullPath : throw InvalidMarker(fullPath, reason);
            }

            // Any other non-empty directory was populated by something else - a Copilot CLI home, a
            // shared folder - and may configure MCP servers, plugins, hooks or trusted folders.
            if (Directory.EnumerateFileSystemEntries(fullPath).Any())
            {
                throw Foreign(fullPath);
            }
        }
        else
        {
            Directory.CreateDirectory(fullPath);
        }

        // Marked only once it is known to be new or empty, so a refused directory is left untouched.
        beforeMark?.Invoke(fullPath);
        MarkDataDirectory(fullPath);
        return fullPath;
    }

    /// <summary>
    ///     Writes the ownership marker into a data directory known to be new or empty, never
    ///     overwriting an entry already there.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The marker is created only when absent. When another writer created the entry first, it is
    ///     validated instead: a valid marker is accepted as it is, anything else is refused. A write
    ///     that fails after the file was created deletes the partial file, as a best effort, before the
    ///     failure propagates. Internal so the concurrent-writer path can be tested directly.
    ///     </para>
    ///     <para>
    ///     When the marker was created here, the directory is enumerated again before the handle is
    ///     released; any entry besides the marker means another writer populated it, so the marker is
    ///     emptied, deleted as a best effort, and the directory refused as foreign. If the
    ///     re-enumeration itself fails, the marker is emptied through the same handle as a best effort
    ///     before the failure propagates and the delete is attempted, so a marker left behind is
    ///     invalid. A marker another writer created first is the reuse path: when valid it is accepted
    ///     without enumerating, as on a later start.
    ///     </para>
    /// </remarks>
    /// <param name="dataDirectory">The data directory's full path.</param>
    /// <exception cref="InvalidOperationException">
    ///     An entry another writer created is not the marker AgentKit writes, or the directory holds
    ///     anything besides the marker just written.
    /// </exception>
    /// <exception cref="IOException">The marker could not be created or read.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory is not writable, or the marker is not readable.</exception>
    internal static void MarkDataDirectory(string dataDirectory) =>
        MarkDataDirectory(dataDirectory, HoldsAnythingButTheMarker, TryDeleteCreatedMarker);

    /// <summary>
    ///     Writes the ownership marker into a data directory known to be new or empty, with the
    ///     re-enumeration and the best-effort delete supplied by the caller.
    /// </summary>
    /// <remarks>
    ///     The production overload passes the real checks; tests substitute a failing enumeration and
    ///     a failing delete to prove what a failed claim leaves behind.
    /// </remarks>
    /// <param name="dataDirectory">The data directory's full path.</param>
    /// <param name="holdsAnythingButTheMarker">
    ///     Reports whether the directory holds any entry other than the marker.
    /// </param>
    /// <param name="deleteCreatedMarker">Deletes the marker created here, as a best effort.</param>
    /// <exception cref="InvalidOperationException">
    ///     An entry another writer created is not the marker AgentKit writes, or the directory holds
    ///     anything besides the marker just written.
    /// </exception>
    /// <exception cref="IOException">The marker could not be created or read.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory is not writable, or the marker is not readable.</exception>
    internal static void MarkDataDirectory(
        string dataDirectory,
        Func<string, bool> holdsAnythingButTheMarker,
        Action<string> deleteCreatedMarker)
    {
        var marker = Path.Combine(dataDirectory, OwnershipMarkerFileName);

        FileStream stream;
        try
        {
            stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && MarkerEntryExists(marker))
        {
            // Another writer won the race - an existing file reports IOException, an existing
            // directory or junction UnauthorizedAccessException. Validate what it wrote; never
            // overwrite it.
            var reason = ValidateMarker(marker);
            if (reason is not null)
            {
                throw InvalidMarker(dataDirectory, reason);
            }

            return;
        }

        bool foreign;
        try
        {
            using (stream)
            {
                stream.Write(OwnershipMarkerContent);
                stream.Flush();

                // The claim is complete only if the marker is all the directory holds: a writer that
                // populated it between the emptiness check and now must not have its content
                // adopted. Checked while the handle is held, and a marker beside foreign content is
                // emptied through that handle - only the file created here - so even if the delete
                // below fails it is refused as invalid rather than trusted on the next start.
                try
                {
                    foreign = holdsAnythingButTheMarker(dataDirectory);
                }
                catch
                {
                    // Leave nothing a later start would trust if the delete below fails too.
                    TryEmptyCreatedMarker(stream);
                    throw;
                }

                if (foreign)
                {
                    stream.SetLength(0);
                }
            }
        }
        catch
        {
            deleteCreatedMarker(marker);
            throw;
        }

        if (foreign)
        {
            deleteCreatedMarker(marker);
            throw Foreign(dataDirectory);
        }
    }

    /// <summary>
    ///     Reports whether a data directory holds any entry other than the ownership marker.
    /// </summary>
    /// <param name="dataDirectory">The data directory's full path.</param>
    /// <returns><see langword="true"/> when another entry exists.</returns>
    private static bool HoldsAnythingButTheMarker(string dataDirectory) =>
        Directory.EnumerateFileSystemEntries(dataDirectory)
            .Any(entry => !string.Equals(Path.GetFileName(entry), OwnershipMarkerFileName, StringComparison.Ordinal));

    /// <summary>
    ///     Reports whether an entry of the marker's name exists, of any kind and without following a
    ///     link.
    /// </summary>
    /// <remarks>
    ///     Unlike <see cref="File.Exists(string)"/>, this is true for a directory, a junction and a
    ///     dangling link, so each of them is refused as an invalid marker rather than mistaken for
    ///     its absence.
    /// </remarks>
    /// <param name="marker">The marker's full path.</param>
    /// <returns><see langword="true"/> when an entry exists.</returns>
    private static bool MarkerEntryExists(string marker)
    {
        var info = new FileInfo(marker);
        return (int)info.Attributes != -1 || info.LinkTarget is not null;
    }

    /// <summary>
    ///     Checks that an existing marker entry is exactly the marker AgentKit writes.
    /// </summary>
    /// <remarks>
    ///     The checks run in order, and no link is followed: a symbolic link, junction or other
    ///     reparse point is refused; then a directory; then any length other than the marker's,
    ///     before the file is opened, which also refuses a FIFO or device without blocking on it.
    ///     Only then is the file read, one byte past the marker's length so a file that grew since
    ///     its length was read is refused, and compared byte for byte.
    /// </remarks>
    /// <param name="marker">The marker's full path.</param>
    /// <returns><see langword="null"/> when the marker is valid; otherwise why it is not.</returns>
    /// <exception cref="IOException">The marker could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The marker is not readable.</exception>
    private static string? ValidateMarker(string marker)
    {
        var info = new FileInfo(marker);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return "it is a symbolic link, junction or other reparse point";
        }

        if (info.Attributes.HasFlag(FileAttributes.Directory))
        {
            return "it is a directory";
        }

        var expected = OwnershipMarkerContent;
        if (info.Length != expected.Length)
        {
            return "its size is not that of the marker AgentKit writes";
        }

        var buffer = new byte[expected.Length + 1];
        int read;
        using (var stream = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        }

        return buffer.AsSpan(0, read).SequenceEqual(expected)
            ? null
            : "its content is not the marker AgentKit writes";
    }

    /// <summary>
    ///     Empties a marker this call created, through the handle still held, as a best effort.
    /// </summary>
    /// <remarks>
    ///     Used when the claim re-check fails, so a marker the delete then fails to remove is refused
    ///     as invalid on the next start rather than trusted.
    /// </remarks>
    /// <param name="stream">The marker's open write handle.</param>
    private static void TryEmptyCreatedMarker(FileStream stream)
    {
        try
        {
            stream.SetLength(0);
            stream.Flush();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The re-check failure is the one reported; the delete that follows is still attempted.
        }
    }

    /// <summary>
    ///     Deletes a marker this call created - partial, or emptied beside foreign content - as a best
    ///     effort.
    /// </summary>
    /// <param name="marker">The marker's full path.</param>
    private static void TryDeleteCreatedMarker(string marker)
    {
        try
        {
            File.Delete(marker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The original failure or refusal is the one reported; a marker left behind is empty or
            // partial, so it is refused as invalid on the next start, with a message that says how
            // to recover.
        }
    }

    /// <summary>
    ///     Builds the refusal for a non-empty directory without AgentKit's marker.
    /// </summary>
    /// <param name="fullPath">The data directory's full path.</param>
    /// <returns>The exception to throw.</returns>
    private static InvalidOperationException Foreign(string fullPath) =>
        new(
            $"The Copilot data directory '{fullPath}' is not empty and was not created by AgentKit (it has no "
            + $"'{OwnershipMarkerFileName}' marker), so it may hold Copilot configuration - MCP servers, plugins, "
            + "hooks, trusted folders - that a confined agent must not receive. Give AgentKit a new or empty "
            + "directory the application owns, such as a folder under the application's local data, and do not "
            + "share it with the Copilot CLI or another tool.");

    /// <summary>
    ///     Builds the refusal for a directory whose marker entry is not the marker AgentKit writes.
    /// </summary>
    /// <param name="fullPath">The data directory's full path.</param>
    /// <param name="reason">Why the entry is not the marker.</param>
    /// <returns>The exception to throw.</returns>
    private static InvalidOperationException InvalidMarker(string fullPath, string reason) =>
        new(
            $"The Copilot data directory '{fullPath}' has a '{OwnershipMarkerFileName}' entry that is not the "
            + $"marker AgentKit writes ({reason}), so AgentKit cannot treat the directory as its own and will "
            + "not start a runtime on it. If the application created this directory with an earlier build of "
            + "AgentKit, delete the directory and let AgentKit create it again; the conversations kept in it "
            + "are lost. Otherwise give AgentKit a new or empty directory the application owns, and do not "
            + "share it with the Copilot CLI or another tool.");

    /// <summary>
    ///     Builds the runtime process's replacement environment from the host's.
    /// </summary>
    /// <remarks>
    ///     Every variable is kept except those whose name starts with <c>COPILOT_</c> or
    ///     <c>GITHUB_COPILOT_</c>, which are withheld unless named in
    ///     <see cref="PassThroughVariables"/>. The SDK then adds its own settings — the data directory,
    ///     the keychain switch, a supplied token — so those cannot be overridden from the host either.
    ///     An entry whose name or value is not a string is skipped.
    /// </remarks>
    /// <param name="hostEnvironment">The host's environment, as <see cref="Environment.GetEnvironmentVariables()"/> returns it.</param>
    /// <returns>The runtime's environment, keyed with this platform's variable-name comparison.</returns>
    internal static Dictionary<string, string> BuildRuntimeEnvironment(IDictionary hostEnvironment)
    {
        var environment = new Dictionary<string, string>(VariableNameComparer);
        foreach (DictionaryEntry entry in hostEnvironment)
        {
            if (entry.Key is string name && entry.Value is string value && !IsWithheld(name))
            {
                environment[name] = value;
            }
        }

        return environment;
    }

    /// <summary>
    ///     Builds the options of the client AgentKit owns.
    /// </summary>
    /// <remarks>
    ///     The single place the client is configured. Every value that decides what a session can
    ///     receive is forced here; only the log level and the logger come from
    ///     <paramref name="options"/>, the sign-in from <paramref name="signIn"/>, and the executable
    ///     from <see cref="ResolveLaunchAsync"/>.
    ///     Everything left unset keeps the SDK's default, which for each capability channel is none.
    ///     The mode, environment and data directory are the same whichever executable runs; the sign-in
    ///     adds only a token, which the SDK passes in a variable of its own, and the GitHub host
    ///     (<c>COPILOT_GH_HOST</c>, one of <see cref="PassThroughVariables"/>).
    /// </remarks>
    /// <param name="options">The host's options.</param>
    /// <param name="dataDirectory">The prepared data directory's full path.</param>
    /// <param name="hostEnvironment">The host's environment, from which the runtime's is built.</param>
    /// <param name="cliPath">
    ///     The installed Copilot CLI to spawn, as <see cref="ResolveLaunchAsync"/> resolved it, or
    ///     <see langword="null"/> for the runtime the SDK provisions. Because the runtime's environment
    ///     withholds <c>COPILOT_CLI_PATH</c>, nothing else can choose the executable.
    /// </param>
    /// <param name="signIn">
    ///     How the runtime signs in, as <see cref="CopilotSignIn.ResolveAsync"/> resolved it, or
    ///     <see langword="null"/> for what <paramref name="options"/> alone describe (a test's client).
    /// </param>
    /// <returns>The client options.</returns>
    internal static CopilotClientOptions BuildClientOptions(
        CopilotRuntimeOptions options,
        string dataDirectory,
        IDictionary hostEnvironment,
        string? cliPath = null,
        CopilotSignInPlan? signIn = null)
    {
        signIn ??= CopilotSignInPlan.FromOptions(options);

        // The runtime's environment, with the GitHub host of the chosen sign-in when it names one
        var environment = BuildRuntimeEnvironment(hostEnvironment);
        if (signIn.HostName is not null)
        {
            environment["COPILOT_GH_HOST"] = signIn.HostName;
        }

        var clientOptions = new CopilotClientOptions
        {
            // Empty mode: none of the SDK's coding-agent defaults, credentials kept in the data
            // directory rather than the system keychain, and the environment context removed.
            Mode = CopilotClientMode.Empty,

            // The resolved executable as a child process, so its environment is this library's to
            // decide: no runtime arguments, no in-process or external runtime.
            Runtime = RuntimeConnection.ForSpawnedStdio(
                path: cliPath,
                environment: environment,
                baseDirectory: dataDirectory),
        };

        // Neutral options only: the resolved sign-in and host-side diagnostics.
        clientOptions.GitHub.Token = signIn.Token;
        clientOptions.Observability.LogLevel = options.LogLevel;
        clientOptions.Observability.Logger = options.Logger;

        return clientOptions;
    }

    /// <summary>
    ///     Resolves the executable a runtime runs and, for an installed CLI, checks its version.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     For <see cref="CopilotRuntimeSource.Bundled"/> nothing is searched or run: the SDK
    ///     provisioned the runtime and pins its version. For <see cref="CopilotRuntimeSource.InstalledCli"/>
    ///     the CLI is the path the host named, which must exist, or the one discovery finds; its version
    ///     is read as the SDK will run it, and a version older than
    ///     <see cref="CopilotCli.MinimumVersion"/> is refused before any client exists. Output that
    ///     names no version is left to the SDK's protocol handshake, which fails closed.
    ///     </para>
    ///     <para>
    ///     The search context and process runner are injected so every outcome is testable without a
    ///     CLI on the machine.
    ///     </para>
    /// </remarks>
    /// <param name="options">The host's options.</param>
    /// <param name="dataDirectory">The prepared data directory's full path.</param>
    /// <param name="hostEnvironment">The host's environment.</param>
    /// <param name="context">The platform and file system discovery searches.</param>
    /// <param name="runner">Runs the version probe.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>What to launch.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The named CLI does not exist, none was found, its version could not be read, or it is too old.
    /// </exception>
    /// <exception cref="OperationCanceledException">The resolution was canceled.</exception>
    internal static async Task<CopilotRuntimeLaunch> ResolveLaunchAsync(
        CopilotRuntimeOptions options,
        string dataDirectory,
        IDictionary hostEnvironment,
        CopilotCliSearchContext context,
        CopilotCliProcessRunner runner,
        CancellationToken cancellationToken)
    {
        // The bundled runtime is the SDK's, of the version the SDK was built for
        if (options.RuntimeSource == CopilotRuntimeSource.Bundled)
        {
            return new CopilotRuntimeLaunch(CopilotRuntimeSource.Bundled, null, null);
        }

        // The host's own choice wins; otherwise discovery chooses, never the ambient environment
        string cliPath;
        if (options.CliPath is not null)
        {
            cliPath = context.FileExists(options.CliPath)
                ? options.CliPath
                : throw new InvalidOperationException(CopilotCli.ExplainExplicitPathMissing(options.CliPath));
        }
        else
        {
            var found = CopilotCli.FindInstalled(context);
            cliPath = found.Path ?? throw new InvalidOperationException(CopilotCli.ExplainNotFound(found));
        }

        // The version the runtime will run, checked before a runtime process exists
        var version = await CopilotCli.ReadVersionAsync(
            cliPath,
            dataDirectory,
            BuildRuntimeEnvironment(hostEnvironment),
            context,
            runner,
            cancellationToken).ConfigureAwait(false);
        if (version is not null && !CopilotCli.IsSupported(version))
        {
            throw new InvalidOperationException(CopilotCli.ExplainTooOld(cliPath, version));
        }

        return new CopilotRuntimeLaunch(CopilotRuntimeSource.InstalledCli, cliPath, version);
    }

    /// <summary>
    ///     Reads a started runtime's status and refuses it when it is not signed in.
    /// </summary>
    /// <remarks>
    ///     Run inside the start step of <see cref="StartAsync"/>, so a refused runtime's client is
    ///     disposed. The reads are injected because the SDK's client cannot report a status on demand
    ///     without a runtime.
    /// </remarks>
    /// <param name="readStatus">Reads the runtime's version and protocol.</param>
    /// <param name="readAuthentication">Reads the runtime's authentication status.</param>
    /// <param name="signIn">The sign-in plan the runtime started with, which a refusal explains.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The runtime's status and authentication status.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The runtime is not signed in; the message lists what each sign-in source found and how to
    ///     sign in.
    /// </exception>
    /// <exception cref="OperationCanceledException">The reads were canceled.</exception>
    internal static async Task<(GetStatusResponse Status, GetAuthStatusResponse Authentication)> VerifyStartedAsync(
        Func<CancellationToken, Task<GetStatusResponse>> readStatus,
        Func<CancellationToken, Task<GetAuthStatusResponse>> readAuthentication,
        CopilotSignInPlan signIn,
        CancellationToken cancellationToken)
    {
        var status = await readStatus(cancellationToken).ConfigureAwait(false);
        var authentication = await readAuthentication(cancellationToken).ConfigureAwait(false);
        return authentication.IsAuthenticated
            ? (status, authentication)
            : throw new InvalidOperationException(CopilotSignIn.ExplainUnauthenticated(signIn, authentication));
    }

    /// <summary>
    ///     Runs a client's start and explains its failure, without knowing what was launched.
    /// </summary>
    /// <remarks>
    ///     Used by the session channel's discovery, which starts a client idempotently before using its
    ///     RPC surface, so a runtime that cannot start says why on that path too; the explanation then
    ///     covers both runtime sources.
    /// </remarks>
    /// <param name="start">Starts the client.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes when the start has completed.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The start failed. The message reports the runtime's own error and the likely cause; the
    ///     original failure is the <see cref="Exception.InnerException"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">The start was canceled. It propagates unchanged.</exception>
    internal static Task StartClientAsync(Func<CancellationToken, Task> start, CancellationToken cancellationToken) =>
        StartClientAsync(start, launch: null, signIn: null, cancellationToken);

    /// <summary>
    ///     Runs a client's start and explains its failure.
    /// </summary>
    /// <remarks>
    ///     Shared by <see cref="StartAsync"/>, which knows what it launched, and by the session
    ///     channel's discovery, which does not. The start is injected because the SDK's client cannot be
    ///     made to fail on demand without a broken runtime.
    /// </remarks>
    /// <param name="start">Starts the client.</param>
    /// <param name="launch">What was launched, or <see langword="null"/> when it is not known.</param>
    /// <param name="signIn">
    ///     The sign-in plan whose tokens are redacted from the quoted error, or <see langword="null"/>
    ///     to redact token-shaped text only.
    /// </param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes when the start has completed.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The start failed. The message reports the runtime's own error, redacted, and the likely
    ///     cause; the original failure, unmodified, is the <see cref="Exception.InnerException"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">The start was canceled. It propagates unchanged.</exception>
    internal static async Task StartClientAsync(
        Func<CancellationToken, Task> start,
        CopilotRuntimeLaunch? launch,
        CopilotSignInPlan? signIn,
        CancellationToken cancellationToken)
    {
        try
        {
            await start(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw new InvalidOperationException(ExplainStartFailure(failure, launch, signIn), failure);
        }
    }

    /// <summary>
    ///     Composes the explanation for a runtime that failed to start.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The runtime's own error comes first. A protocol-version mismatch the SDK reports is then
    ///     explained as a CLI too old (upgrade it) or too new (update AgentKit) for this release. Next
    ///     comes the cause that fits what was launched: for the bundled runtime, a host application that
    ///     does not reference <c>Microsoft.Copilot.SDK</c> directly, so the runtime was never
    ///     provisioned; for an installed CLI, the CLI that ran and its version, with no advice about
    ///     package references; when the launch is not known, both. Because AgentKit withholds the host's
    ///     Copilot configuration variables, a runtime path override cannot be the cause, and the
    ///     explanation says which variables do pass and which sources the runtime authenticates from,
    ///     so an authentication or network failure can be traced. A failure whose message is null is
    ///     still explained.
    ///     </para>
    ///     <para>
    ///     The runtime's error is quoted redacted: every token <paramref name="signIn"/> knows (the
    ///     start token, the environment's token values, and each session token the host's provider
    ///     returned), and any text shaped like a GitHub token, is removed. The protocol mismatch is
    ///     detected on the error as raised.
    ///     </para>
    /// </remarks>
    /// <param name="failure">The failure the start raised.</param>
    /// <param name="launch">What was launched, or <see langword="null"/> when it is not known.</param>
    /// <param name="signIn">The sign-in plan whose tokens are redacted, or <see langword="null"/> to redact token-shaped text only.</param>
    /// <returns>The explanation.</returns>
    internal static string ExplainStartFailure(Exception failure, CopilotRuntimeLaunch? launch, CopilotSignInPlan? signIn)
    {
        var message = failure.Message;
        var quoted = message is null ? string.Empty : signIn?.Redact(message) ?? CopilotSignIn.Redact(message);
        string cause;
        if (launch?.Source != CopilotRuntimeSource.Bundled && CopilotCli.IsProtocolMismatch(message))
        {
            cause = CopilotCli.ExplainProtocolMismatch(launch?.CliPath, launch?.CliVersion, CopilotCli.ParseServerProtocol(message));
        }
        else
        {
            cause = launch?.Source switch
            {
                CopilotRuntimeSource.Bundled => BundledRuntimeCause,
                CopilotRuntimeSource.InstalledCli =>
                    $"AgentKit ran the installed Copilot CLI at '{launch.CliPath}' "
                    + (launch.CliVersion is null ? "(its version could not be read)" : $"(version {launch.CliVersion})")
                    + " as a child process; the error above is that CLI's. Check that it runs on its own with "
                    + "'copilot --no-auto-update --version', or pass another CLI as CopilotRuntimeOptions' cliPath.",
                _ => "AgentKit runs either the installed Copilot CLI or, when the host opts in, the runtime "
                    + "Microsoft.Copilot.SDK bundles, as a child process. " + BundledRuntimeCause,
            };
        }

        return "The Copilot runtime could not start: " + quoted + " " + cause + " The runtime receives none of the "
            + "host's COPILOT_ or GITHUB_COPILOT_ variables except "
            + string.Join(", ", PassThroughVariables.Order(StringComparer.Ordinal)) + "; it signs in with, in order, "
            + "CopilotRuntimeOptions' gitHubToken or gitHubTokenProvider, COPILOT_GITHUB_TOKEN, GH_TOKEN or GITHUB_TOKEN, "
            + "the user's Copilot CLI sign-in, a sign-in stored in its data directory, or the signed-in GitHub CLI user. "
            + "The inner exception carries the runtime's own error.";
    }

    /// <summary>
    ///     Creates a resource, starts it, and disposes it if the start fails or is canceled.
    /// </summary>
    /// <remarks>
    ///     The ordering <see cref="StartAsync"/> relies on, factored out so it can be tested without a
    ///     runtime. A failure while disposing is swallowed so it cannot replace the start's own
    ///     failure, which is the one the host must see.
    /// </remarks>
    /// <typeparam name="T">The resource type.</typeparam>
    /// <param name="create">Creates the resource.</param>
    /// <param name="start">Starts the resource.</param>
    /// <param name="dispose">Disposes the resource.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>The started resource. Ownership passes to the caller.</returns>
    internal static async Task<T> StartOwnedAsync<T>(
        Func<T> create,
        Func<T, CancellationToken, Task> start,
        Func<T, ValueTask> dispose,
        CancellationToken cancellationToken)
    {
        var owned = create();
        try
        {
            await start(owned, cancellationToken).ConfigureAwait(false);
            return owned;
        }
        catch
        {
            try
            {
                await dispose(owned).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Intentionally swallowed: see the remarks. The start's failure is rethrown below.
            }

            throw;
        }
    }

    /// <summary>
    ///     Determines whether a host environment variable is withheld from the runtime.
    /// </summary>
    /// <param name="name">The variable's name.</param>
    /// <returns>
    ///     <see langword="true"/> when the name starts with a withheld prefix and is not in
    ///     <see cref="PassThroughVariables"/>.
    /// </returns>
    private static bool IsWithheld(string name)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return WithheldPrefixes.Any(prefix => name.StartsWith(prefix, comparison))
            && !PassThroughVariables.Contains(name);
    }
}

/// <summary>
///     What <see cref="CopilotRuntime.StartAsync"/> launched: the runtime source and, for an installed
///     CLI, its path and the version it reported.
/// </summary>
/// <param name="Source">The runtime source.</param>
/// <param name="CliPath">The installed CLI's full path, or <see langword="null"/> for the bundled runtime.</param>
/// <param name="CliVersion">
///     The version the installed CLI reported, or <see langword="null"/> for the bundled runtime or when
///     the CLI's output named none.
/// </param>
internal sealed record CopilotRuntimeLaunch(CopilotRuntimeSource Source, string? CliPath, Version? CliVersion);
