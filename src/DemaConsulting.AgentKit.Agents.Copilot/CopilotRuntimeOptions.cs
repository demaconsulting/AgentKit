using Microsoft.Copilot;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     The options of the Copilot runtime AgentKit starts and owns: where it keeps its state, how it
///     authenticates, and how it logs.
/// </summary>
/// <remarks>
///     <para>
///     <b>Only options that cannot widen what reaches the model are expressible.</b> The runtime's
///     mode, its process and the environment it receives, its plugin directories, extensions,
///     installation and model handlers and its session filesystem are all decided by
///     <see cref="CopilotRuntime"/>, because each could hand a confined agent capability its host
///     never reviewed. What remains here is the data directory, a GitHub token and host-side
///     diagnostics.
///     </para>
///     <para>
///     Instances are immutable after construction and are safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Options for a runtime that keeps its state under the application's local data and
///     authenticates with a token from the environment, falling back to the signed-in GitHub CLI
///     user when none is set.
///     </para>
///     <code>
///     var options = new CopilotRuntimeOptions(
///         Path.Combine(
///             Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
///             "MyApplication",
///             "copilot-runtime"),
///         gitHubToken: Environment.GetEnvironmentVariable("MY_APPLICATION_GITHUB_TOKEN"));
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
    ///     GitHub CLI. When supplied, only this token is used.
    /// </param>
    /// <param name="logLevel">The runtime's log level, or <see langword="null"/> for the runtime's default.</param>
    /// <param name="logger">A logger for the SDK's diagnostic output, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dataDirectory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="dataDirectory"/> is empty or whitespace, or is not a valid path.
    /// </exception>
    public CopilotRuntimeOptions(
        string dataDirectory,
        string? gitHubToken = null,
        CopilotLogLevel? logLevel = null,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        // Resolved now, so the directory a runtime uses does not depend on the current directory at
        // the moment it starts.
        DataDirectory = Path.GetFullPath(dataDirectory);
        GitHubToken = string.IsNullOrWhiteSpace(gitHubToken) ? null : gitHubToken;
        LogLevel = logLevel;
        Logger = logger;
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
}
