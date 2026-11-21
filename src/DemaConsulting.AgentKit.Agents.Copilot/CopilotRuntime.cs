using System.Collections;
using System.Collections.Frozen;
using Microsoft.Copilot;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     The GitHub Copilot runtime AgentKit starts, confines and owns: a runtime process in
///     <c>CopilotClientMode.Empty</c> whose environment carries none of the host's Copilot
///     configuration variables, on a data directory AgentKit created.
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
///         The bundled runtime that <c>Microsoft.Copilot.SDK</c> provisions into the application's
///         output, spawned as a child process over standard input and output. No runtime path,
///         runtime arguments, in-process hosting, TCP connection or external runtime can be
///         chosen: in-process hosting gives the runtime the host's whole environment, and an
///         external runtime's mode, data directory and environment are not this client's.
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
///         missing or empty directory is created and marked as AgentKit's; a directory AgentKit
///         marked earlier is reused, so conversations persist across restarts; any other
///         non-empty directory — a Copilot CLI home, a shared folder — is refused. Keep the
///         directory private to the application, and give each concurrently running runtime a
///         directory of its own.
///       </description></item>
///       <item><description>
///         No plugin directories, extensions, installation handler, model request handler, session
///         filesystem or test options at client level.
///       </description></item>
///     </list>
///     <para>
///     <b>What this guarantees, per source, on every session AgentKit opens on this runtime.</b>
///     Before any session exists, the built-in GitHub MCP server is named disabled, so it is not
///     started or authenticated; Empty mode alone would start it on the first turn. No
///     user-configured MCP server can exist in a fresh data directory, and one added later is found
///     by the runtime's discovery and disabled before the session is created; Empty mode alone would
///     run it. Workspace MCP configuration is not loaded, because configuration discovery is off and
///     the data directory trusts no folder, and a workspace server that discovery reports is disabled
///     as well. No plugin is installed in a fresh data directory, and plugin directories cannot be
///     configured. These sources are <em>prevented</em>: the opt-in live suite shows that their probe
///     servers never execute. Servers from a managed catalog or a signed-in account follow
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
///     Safe for concurrent use: sessions may be opened on one runtime from several threads, and
///     disposal is idempotent.
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
    ///     Its presence is what lets a non-empty directory be reused across restarts; its absence in a
    ///     non-empty directory is what refuses a directory another tool populated.
    /// </remarks>
    internal const string OwnershipMarkerFileName = ".agentkit-copilot-runtime";

    /// <summary>
    ///     The content written to the ownership marker, for a person who finds the file.
    /// </summary>
    private const string OwnershipMarkerContent =
        "This directory is the data directory of a GitHub Copilot runtime started by AgentKit.\n"
        + "AgentKit created it and reuses it. Do not point the Copilot CLI or another tool at it, and do\n"
        + "not add Copilot configuration to it.\n";

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
    ///     Initializes a new instance of the <see cref="CopilotRuntime"/> class over a client.
    /// </summary>
    /// <remarks>
    ///     Internal: production obtains a runtime only from <see cref="StartAsync"/>, which builds and
    ///     starts the client itself. A test passes an unstarted client, which spawns nothing.
    /// </remarks>
    /// <param name="client">The client this runtime owns and disposes.</param>
    /// <param name="dataDirectory">The prepared data directory's full path.</param>
    internal CopilotRuntime(CopilotClient client, string dataDirectory)
    {
        _client = client;
        DataDirectory = dataDirectory;
    }

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
    ///     The runtime's <c>COPILOT_HOME</c>: its credentials (the system keychain is not used in
    ///     Empty mode) and the conversations kept for resumption live here. Remains available after
    ///     disposal.
    /// </remarks>
    public string DataDirectory { get; }

    /// <summary>
    ///     Gets the client this runtime owns.
    /// </summary>
    /// <remarks>
    ///     Internal: a host never reaches the client, so nothing can reconfigure it or open a session
    ///     on it outside AgentKit's confinement. The live suite reads it through
    ///     <c>InternalsVisibleTo</c>.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
    internal CopilotClient Client =>
        Volatile.Read(ref _client) ?? throw new ObjectDisposedException(nameof(CopilotRuntime));

    /// <summary>
    ///     Starts a confined Copilot runtime.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Prepares the data directory, builds the client options — Empty mode, the bundled runtime
    ///     spawned over standard input and output, the host's Copilot configuration variables
    ///     withheld — and starts the runtime process. A start that fails or is canceled disposes the
    ///     client, so no runtime process is left behind.
    ///     </para>
    ///     <para>
    ///     Side effects: creates the data directory and its ownership marker when they do not exist,
    ///     and starts a child process that runs until the runtime is disposed. Reads the process
    ///     environment once; never changes it.
    ///     </para>
    /// </remarks>
    /// <param name="options">The runtime's options. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>The started runtime. The caller owns it and disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The data directory names a file, or is a non-empty directory AgentKit did not create — give
    ///     AgentKit a new or empty directory the application owns; or the runtime failed to start —
    ///     the message reports the runtime's own error and the likely cause, and the original failure
    ///     is the <see cref="Exception.InnerException"/>.
    /// </exception>
    /// <exception cref="IOException">The data directory or its marker could not be created.</exception>
    /// <exception cref="UnauthorizedAccessException">The data directory is not writable.</exception>
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
        var clientOptions = BuildClientOptions(options, dataDirectory, Environment.GetEnvironmentVariables());

        // Started here and disposed on any failure: a runtime that never reaches the caller must not
        // leave its process running.
        var client = await StartOwnedAsync(
            () => new CopilotClient(clientOptions),
            (created, token) => StartClientAsync(created.StartAsync, token),
            created => created.DisposeAsync(),
            cancellationToken).ConfigureAwait(false);

        return new CopilotRuntime(client, dataDirectory);
    }

    /// <summary>
    ///     Disposes the client, which stops the runtime process.
    /// </summary>
    /// <remarks>
    ///     Idempotent and safe to call concurrently: only the first call disposes. Sessions open on
    ///     the runtime end with it; their persisted state stays in the data directory.
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
    ///     Not safe against a second process preparing the same directory at the same moment; the
    ///     documentation tells hosts to give each runtime a directory of its own.
    ///     </para>
    /// </remarks>
    /// <param name="dataDirectory">The data directory requested. Must not be blank.</param>
    /// <returns>The directory's full path.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The path names an existing file, or a non-empty directory without AgentKit's marker.
    /// </exception>
    /// <exception cref="IOException">The directory or its marker could not be created.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory is not writable.</exception>
    internal static string PrepareDataDirectory(string dataDirectory)
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
            // A directory AgentKit marked is its own, whatever the runtime has written there since.
            if (File.Exists(marker))
            {
                return fullPath;
            }

            // Any other non-empty directory was populated by something else - a Copilot CLI home, a
            // shared folder - and may configure MCP servers, plugins, hooks or trusted folders.
            if (Directory.EnumerateFileSystemEntries(fullPath).Any())
            {
                throw new InvalidOperationException(
                    $"The Copilot data directory '{fullPath}' is not empty and was not created by AgentKit, so "
                    + "it may hold Copilot configuration - MCP servers, plugins, hooks, trusted folders - that a "
                    + "confined agent must not receive. Give AgentKit a new or empty directory the application "
                    + "owns, such as a folder under the application's local data, and do not share it with the "
                    + "Copilot CLI or another tool.");
            }
        }
        else
        {
            Directory.CreateDirectory(fullPath);
        }

        // Marked only once it is known to be new or empty, so a refused directory is left untouched.
        File.WriteAllText(marker, OwnershipMarkerContent);
        return fullPath;
    }

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
    ///     receive is forced here; only the token, the log level and the logger come from
    ///     <paramref name="options"/>. Everything left unset keeps the SDK's default, which for each
    ///     capability channel is none.
    /// </remarks>
    /// <param name="options">The host's options.</param>
    /// <param name="dataDirectory">The prepared data directory's full path.</param>
    /// <param name="hostEnvironment">The host's environment, from which the runtime's is built.</param>
    /// <returns>The client options.</returns>
    internal static CopilotClientOptions BuildClientOptions(
        CopilotRuntimeOptions options,
        string dataDirectory,
        IDictionary hostEnvironment)
    {
        var clientOptions = new CopilotClientOptions
        {
            // Empty mode: none of the SDK's coding-agent defaults, credentials kept in the data
            // directory rather than the system keychain, and the environment context removed.
            Mode = CopilotClientMode.Empty,

            // The bundled runtime as a child process, so its environment is this library's to
            // decide: no runtime path, no runtime arguments, no in-process or external runtime.
            Runtime = RuntimeConnection.ForSpawnedStdio(
                environment: BuildRuntimeEnvironment(hostEnvironment),
                baseDirectory: dataDirectory),
        };

        // Neutral options only: authentication and host-side diagnostics.
        clientOptions.GitHub.Token = options.GitHubToken;
        clientOptions.Observability.LogLevel = options.LogLevel;
        clientOptions.Observability.Logger = options.Logger;

        return clientOptions;
    }

    /// <summary>
    ///     Runs a client's start and explains its failure.
    /// </summary>
    /// <remarks>
    ///     Shared by <see cref="StartAsync"/> and by the session channel's discovery, which starts a
    ///     client idempotently before using its RPC surface, so a runtime that cannot start says why
    ///     on either path. The start is injected because the SDK's client cannot be made to fail on
    ///     demand without a broken runtime.
    /// </remarks>
    /// <param name="start">Starts the client.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes when the start has completed.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The start failed. The message reports the runtime's own error and the likely cause; the
    ///     original failure is the <see cref="Exception.InnerException"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">The start was canceled. It propagates unchanged.</exception>
    internal static async Task StartClientAsync(Func<CancellationToken, Task> start, CancellationToken cancellationToken)
    {
        try
        {
            await start(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw new InvalidOperationException(ExplainStartFailure(failure), failure);
        }
    }

    /// <summary>
    ///     Composes the explanation for a runtime that failed to start.
    /// </summary>
    /// <remarks>
    ///     The runtime's own error comes first. The common cause of a runtime that was not found is
    ///     named next: the SDK provisions its runtime only into a project that references the package
    ///     directly. Because AgentKit withholds the host's Copilot configuration variables, a runtime
    ///     path override cannot be the cause, and the explanation says which variables do pass so an
    ///     authentication or proxy failure can be traced. A failure whose message is null is still
    ///     explained.
    /// </remarks>
    /// <param name="failure">The failure the start raised.</param>
    /// <returns>The explanation.</returns>
    internal static string ExplainStartFailure(Exception failure) =>
        "The Copilot runtime could not start: " + failure.Message + " AgentKit runs the runtime that "
        + "Microsoft.Copilot.SDK provisions into this application's output (runtimes/<rid>/native/copilot-runtime), "
        + "as a child process. If the error is that the runtime was not found, the most common cause is a host "
        + "application that does not reference Microsoft.Copilot.SDK directly: the SDK provisions its runtime only "
        + "into a project that references the package itself, so add a PackageReference to Microsoft.Copilot.SDK to "
        + "the host application. The runtime receives none of the host's COPILOT_ or GITHUB_COPILOT_ variables "
        + "except " + string.Join(", ", PassThroughVariables.Order(StringComparer.Ordinal)) + "; it authenticates "
        + "with the token in CopilotRuntimeOptions, GH_TOKEN, GITHUB_TOKEN or the signed-in GitHub CLI user. The inner "
        + "exception carries the runtime's own error.";

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
