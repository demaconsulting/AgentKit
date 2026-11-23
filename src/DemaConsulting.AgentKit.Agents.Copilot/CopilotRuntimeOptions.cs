using Microsoft.Copilot;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     The options of the Copilot runtime AgentKit starts and owns: where it keeps its state, how it
///     authenticates, how it logs, and which runtime executable it runs.
/// </summary>
/// <remarks>
///     <para>
///     <b>Only options that cannot widen what reaches the model are expressible.</b> The runtime's
///     mode, the environment its process receives, its plugin directories, extensions, installation
///     and model handlers and its session filesystem are all decided by <see cref="CopilotRuntime"/>,
///     because each could hand a confined agent capability its host never reviewed. What remains here
///     is the data directory, how the runtime signs in to GitHub (a token or token provider, the GitHub
///     host, and whether the user's Copilot CLI sign-in may be used), host-side diagnostics, and the
///     choice of runtime
///     executable — the installed Copilot CLI (the default) or the runtime the SDK bundles. That choice
///     is the host's code, never the ambient environment: <c>COPILOT_CLI_PATH</c> is withheld from the
///     runtime and ignored by discovery, and either executable runs under the same confinement.
///     </para>
///     <para>
///     <b>Signing in.</b> <see cref="CopilotRuntime.StartAsync"/> uses the first source that applies:
///     the host's <see cref="GitHubToken"/> or <see cref="GitHubTokenProvider"/>; a token in
///     <c>COPILOT_GITHUB_TOKEN</c>, <c>GH_TOKEN</c> or <c>GITHUB_TOKEN</c>; the user's Copilot CLI
///     sign-in, unless <see cref="UseCopilotCliSignIn"/> is <see langword="false"/>; and finally the
///     runtime's own fallbacks, a sign-in stored in the data directory and the GitHub CLI. The order
///     matches the Copilot CLI's own, so the runtime signs in as <c>copilot</c> would in the same shell.
///     </para>
///     <para>
///     Instances are immutable after construction and are safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Options for a runtime that keeps its state under the application's local data and signs in
///     with a token from the application's own setting, or, when none is set, as the user signed in to
///     the Copilot CLI or the GitHub CLI. The runtime is the Copilot CLI installed on the machine.
///     </para>
///     <code>
///     var options = new CopilotRuntimeOptions(
///         Path.Combine(
///             Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
///             "MyApplication",
///             "copilot-runtime"),
///         gitHubToken: Environment.GetEnvironmentVariable("MY_APPLICATION_GITHUB_TOKEN"));
///     </code>
///     <para>
///     Options that run a specific Copilot CLI instead of the one discovery finds, and options that run
///     the runtime the SDK bundles (the host then references <c>Microsoft.Copilot.SDK</c> directly).
///     </para>
///     <code>
///     var dataDirectory = Path.Combine(
///         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
///         "MyApplication",
///         "copilot-runtime");
///     var pinned = new CopilotRuntimeOptions(dataDirectory, cliPath: @"C:\Tools\copilot\copilot.exe");
///     var bundled = new CopilotRuntimeOptions(dataDirectory, runtimeSource: CopilotRuntimeSource.Bundled);
///     </code>
///     <para>
///     Options for a GitHub Enterprise Cloud account whose tokens the application refreshes itself,
///     through a token provider the runtime calls whenever it needs a token for the host.
///     </para>
///     <code>
///     public CopilotRuntimeOptions CreateEnterpriseOptions(string dataDirectory, Func&lt;string, CancellationToken, Task&lt;string&gt;&gt; fetchToken) =>
///         new(
///             dataDirectory,
///             gitHubHost: "example.ghe.com",
///             gitHubTokenProvider: async (request, cancellationToken) =>
///                 new CopilotGitHubToken(await fetchToken(request.Host, cancellationToken)));
///     </code>
/// </example>
public sealed class CopilotRuntimeOptions
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotRuntimeOptions"/> class.
    /// </summary>
    /// <param name="dataDirectory">
    ///     The directory the runtime keeps its state in — its credentials and the conversations kept
    ///     for resumption. Must not be <see langword="null"/>, empty or whitespace; a relative path is
    ///     resolved against the current directory now. It must be missing, empty, or a directory an
    ///     earlier AgentKit runtime created: any other non-empty directory is refused when the runtime
    ///     starts. Keep it private to the application, never share it with the Copilot CLI, and give
    ///     each concurrently running runtime its own.
    /// </param>
    /// <param name="gitHubToken">
    ///     A GitHub token to sign in with. When supplied, only this token is used. When
    ///     <see langword="null"/>, empty or whitespace, the runtime signs in from the next source that
    ///     applies: <c>COPILOT_GITHUB_TOKEN</c>, <c>GH_TOKEN</c> or <c>GITHUB_TOKEN</c> in the
    ///     application's environment; the user's Copilot CLI sign-in (see
    ///     <paramref name="useCopilotCliSignIn"/>); a sign-in stored in the data directory; or the GitHub
    ///     CLI. Must not be combined with <paramref name="gitHubTokenProvider"/>.
    /// </param>
    /// <param name="logLevel">The runtime's log level, or <see langword="null"/> for the runtime's default.</param>
    /// <param name="logger">A logger for the SDK's diagnostic output, or <see langword="null"/> for none.</param>
    /// <param name="runtimeSource">
    ///     Which runtime executable runs: <see cref="CopilotRuntimeSource.InstalledCli"/>, the default,
    ///     or <see cref="CopilotRuntimeSource.Bundled"/>. Must be a defined value.
    /// </param>
    /// <param name="cliPath">
    ///     The full path of the Copilot CLI to run, for <see cref="CopilotRuntimeSource.InstalledCli"/>
    ///     only; <see langword="null"/>, empty or whitespace to run the CLI
    ///     <see cref="CopilotCli.FindInstalled()"/> finds. Must be fully qualified, and must not be a batch
    ///     file (<c>.cmd</c>, <c>.bat</c>), which Windows would run through a shell. Whether it exists is
    ///     checked when the runtime starts, and its version is checked like a discovered CLI's. A path
    ///     ending in <c>.js</c> (the npm package's <c>npm-loader.js</c>) is run with <c>node</c> from
    ///     <c>PATH</c>.
    /// </param>
    /// <param name="gitHubHost">
    ///     The GitHub host to sign in to, for a GitHub Enterprise Cloud account: <c>example.ghe.com</c> or
    ///     <c>https://example.ghe.com</c>. <see langword="null"/>, empty or whitespace leaves the host to
    ///     the sign-in source: <c>COPILOT_GH_HOST</c> in the application's environment, the Copilot CLI
    ///     account's host, or <c>github.com</c>. A token or token provider is used with this host; the
    ///     Copilot CLI sign-in is used only when its account is on this host. Only an https host without
    ///     a path, query, user name or port is accepted. <c>COPILOT_GH_HOST</c> must meet the same rule:
    ///     one that does not is refused when the runtime starts, unless this host is passed, which takes
    ///     precedence over it.
    /// </param>
    /// <param name="gitHubTokenProvider">
    ///     A callback that supplies GitHub tokens, so a host that can refresh its token (an OAuth app, a
    ///     secret store) never hands the runtime an expired one. AgentKit asks it once when the runtime
    ///     starts, and the runtime asks again for each session and whenever a session's token is about
    ///     to expire. <see langword="null"/> for none. Must not be combined with
    ///     <paramref name="gitHubToken"/>. The callback may be called concurrently, from any thread.
    /// </param>
    /// <param name="useCopilotCliSignIn">
    ///     Whether the user's Copilot CLI sign-in (<c>copilot login</c>) may be used when neither the host
    ///     nor the environment supplies a token; <see langword="true"/> by default. AgentKit then reads
    ///     only the signed-in account's host and login from the CLI's configuration, and obtains its token
    ///     through the Copilot CLI itself; the runtime's data directory stays private. Pass
    ///     <see langword="false"/> to sign in only from a token, the data directory or the GitHub CLI.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="dataDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="dataDirectory"/> is empty or whitespace, or is not a valid path; or
    ///     <paramref name="cliPath"/> is given with <see cref="CopilotRuntimeSource.Bundled"/>, is not
    ///     fully qualified, is not a valid path, or names a batch file; or <paramref name="gitHubHost"/> is
    ///     not an https host name or URL without a path, query, fragment, user name or port; or both
    ///     <paramref name="gitHubToken"/> and <paramref name="gitHubTokenProvider"/> are given.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="runtimeSource"/> is not a defined <see cref="CopilotRuntimeSource"/> value.
    /// </exception>
    public CopilotRuntimeOptions(
        string dataDirectory,
        string? gitHubToken = null,
        CopilotLogLevel? logLevel = null,
        ILogger? logger = null,
        CopilotRuntimeSource runtimeSource = CopilotRuntimeSource.InstalledCli,
        string? cliPath = null,
        string? gitHubHost = null,
        Func<CopilotGitHubTokenRequest, CancellationToken, Task<CopilotGitHubToken>>? gitHubTokenProvider = null,
        bool useCopilotCliSignIn = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Enum.IsDefined(runtimeSource))
        {
            throw new ArgumentOutOfRangeException(
                nameof(runtimeSource),
                runtimeSource,
                "The Copilot runtime source must be InstalledCli or Bundled.");
        }

        // One token source from the host: with both, one of them would be silently ignored
        if (!string.IsNullOrWhiteSpace(gitHubToken) && gitHubTokenProvider is not null)
        {
            throw new ArgumentException(
                "Pass either a GitHub token or a GitHub token provider, not both: the runtime signs in with one.",
                nameof(gitHubTokenProvider));
        }

        // Resolved now, so the directory a runtime uses does not depend on the current directory at
        // the moment it starts. A path the platform cannot represent is the caller's argument error.
        try
        {
            DataDirectory = Path.GetFullPath(dataDirectory);
        }
        catch (ArgumentException failure)
        {
            throw new ArgumentException(
                "The Copilot data directory is not a valid path; the inner exception says why.",
                nameof(dataDirectory),
                failure);
        }

        GitHubToken = string.IsNullOrWhiteSpace(gitHubToken) ? null : gitHubToken;
        LogLevel = logLevel;
        Logger = logger;
        RuntimeSource = runtimeSource;
        CliPath = NormalizeCliPath(cliPath, runtimeSource);
        GitHubHost = CopilotSignIn.NormalizeHost(gitHubHost, nameof(gitHubHost));
        GitHubTokenProvider = gitHubTokenProvider;
        UseCopilotCliSignIn = useCopilotCliSignIn;
    }

    /// <summary>
    ///     Gets the full path of the directory the runtime keeps its state in.
    /// </summary>
    public string DataDirectory { get; }

    /// <summary>
    ///     Gets the GitHub token to authenticate with, or <see langword="null"/> to authenticate as the
    ///     signed-in user.
    /// </summary>
    public string? GitHubToken { get; }

    /// <summary>
    ///     Gets the runtime's log level, or <see langword="null"/> for the runtime's default.
    /// </summary>
    public CopilotLogLevel? LogLevel { get; }

    /// <summary>
    ///     Gets the logger for the SDK's diagnostic output, or <see langword="null"/> for none.
    /// </summary>
    public ILogger? Logger { get; }

    /// <summary>
    ///     Gets which runtime executable runs: the installed Copilot CLI or the runtime the SDK bundles.
    /// </summary>
    public CopilotRuntimeSource RuntimeSource { get; }

    /// <summary>
    ///     Gets the full path of the Copilot CLI to run, or <see langword="null"/> to run the one
    ///     <see cref="CopilotCli.FindInstalled()"/> finds (always <see langword="null"/> for
    ///     <see cref="CopilotRuntimeSource.Bundled"/>).
    /// </summary>
    public string? CliPath { get; }

    /// <summary>
    ///     Gets the GitHub host to sign in to, as an https URL without a path such as
    ///     <c>https://example.ghe.com</c>, or <see langword="null"/> to leave the host to the sign-in
    ///     source.
    /// </summary>
    public string? GitHubHost { get; }

    /// <summary>
    ///     Gets the callback that supplies GitHub tokens, or <see langword="null"/> for none.
    /// </summary>
    public Func<CopilotGitHubTokenRequest, CancellationToken, Task<CopilotGitHubToken>>? GitHubTokenProvider { get; }

    /// <summary>
    ///     Gets a value indicating whether the user's Copilot CLI sign-in may be used when neither the host
    ///     nor the environment supplies a token.
    /// </summary>
    public bool UseCopilotCliSignIn { get; }

    /// <summary>
    ///     Validates and normalizes the CLI path a host passed.
    /// </summary>
    /// <remarks>
    ///     A blank path means none. A path is accepted only for the installed CLI and only fully
    ///     qualified, so the executable a runtime runs never depends on the current directory or on a
    ///     <c>PATH</c> lookup the host did not make.
    /// </remarks>
    /// <param name="cliPath">The path the host passed.</param>
    /// <param name="runtimeSource">The runtime source the host chose.</param>
    /// <returns>The normalized full path, or <see langword="null"/> for none.</returns>
    /// <exception cref="ArgumentException">
    ///     The path is given with the bundled runtime, is not fully qualified, is not a valid path, or
    ///     names a batch file.
    /// </exception>
    private static string? NormalizeCliPath(string? cliPath, CopilotRuntimeSource runtimeSource)
    {
        // A blank path is no path: discovery chooses the CLI, or the bundled runtime runs
        if (string.IsNullOrWhiteSpace(cliPath))
        {
            return null;
        }

        // The bundled runtime is the SDK's; naming an executable for it is a contradiction
        if (runtimeSource == CopilotRuntimeSource.Bundled)
        {
            throw new ArgumentException(
                "A Copilot CLI path applies only to CopilotRuntimeSource.InstalledCli; the bundled runtime is the one "
                + "Microsoft.Copilot.SDK provisions. Pass either a CLI path or CopilotRuntimeSource.Bundled, not both.",
                nameof(cliPath));
        }

        // A relative path would be resolved against whatever the current directory is at start
        if (!Path.IsPathFullyQualified(cliPath))
        {
            throw new ArgumentException(
                $"The Copilot CLI path '{cliPath}' is not fully qualified. Pass the full path of the Copilot CLI "
                + "executable, such as the one CopilotCli.FindInstalled() returns.",
                nameof(cliPath));
        }

        string full;
        try
        {
            full = Path.GetFullPath(cliPath);
        }
        catch (ArgumentException failure)
        {
            throw new ArgumentException(
                "The Copilot CLI path is not a valid path; the inner exception says why.",
                nameof(cliPath),
                failure);
        }

        // Windows runs a batch file through cmd.exe, so the CLI - probed and spawned without a shell - would
        // run under one; a batch file is never a Copilot CLI, so it is refused on every platform
        var extension = Path.GetExtension(full);
        if (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The Copilot CLI path '{full}' is a batch file, which Windows runs through cmd.exe; AgentKit runs the "
                + "Copilot CLI without a shell. Pass the full path of copilot.exe - such as the one "
                + "CopilotCli.FindInstalled() returns - or, for an npm installation, the package's npm-loader.js "
                + @"(under %APPDATA%\npm\node_modules\@github\copilot), which AgentKit runs with node.",
                nameof(cliPath));
        }

        return full;
    }
}
