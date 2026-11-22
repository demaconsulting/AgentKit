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
///     is the data directory, a GitHub token, host-side diagnostics, and the choice of runtime
///     executable — the installed Copilot CLI (the default) or the runtime the SDK bundles. That choice
///     is the host's code, never the ambient environment: <c>COPILOT_CLI_PATH</c> is withheld from the
///     runtime and ignored by discovery, and either executable runs under the same confinement.
///     </para>
///     <para>
///     Instances are immutable after construction and are safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Options for a runtime that keeps its state under the application's local data and
///     authenticates with a token from the environment, falling back to the signed-in GitHub CLI
///     user when none is set. The runtime is the Copilot CLI installed on the machine.
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
    ///     A GitHub token to authenticate with. When <see langword="null"/>, empty or whitespace, the
    ///     runtime authenticates as the signed-in user: from <c>COPILOT_GITHUB_TOKEN</c>,
    ///     <c>GH_TOKEN</c> or <c>GITHUB_TOKEN</c>, credentials stored in the data directory, or the
    ///     GitHub CLI. When supplied, only this token is used. The Copilot CLI's own sign-in is not
    ///     used, whichever runtime source runs.
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
    /// <exception cref="ArgumentNullException"><paramref name="dataDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="dataDirectory"/> is empty or whitespace, or is not a valid path; or
    ///     <paramref name="cliPath"/> is given with <see cref="CopilotRuntimeSource.Bundled"/>, is not
    ///     fully qualified, is not a valid path, or names a batch file.
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
        string? cliPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Enum.IsDefined(runtimeSource))
        {
            throw new ArgumentOutOfRangeException(
                nameof(runtimeSource),
                runtimeSource,
                "The Copilot runtime source must be InstalledCli or Bundled.");
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
