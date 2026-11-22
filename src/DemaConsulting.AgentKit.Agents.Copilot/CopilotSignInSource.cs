namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     How a started Copilot runtime signed in to GitHub.
/// </summary>
/// <remarks>
///     <para>
///     <see cref="CopilotRuntime.StartAsync"/> tries the sign-in sources in a fixed order: a token or
///     token provider the host supplies, a GitHub token in the environment, the user's Copilot CLI
///     sign-in (unless the host turns it off), then the runtime's own fallbacks — a sign-in stored in
///     AgentKit's data directory and the GitHub CLI. <see cref="CopilotRuntime.SignInSource"/> reports
///     the one the started runtime actually used, read from the runtime itself.
///     </para>
///     <para>
///     The value carries no secret: it names a source, never a token.
///     </para>
/// </remarks>
public enum CopilotSignInSource
{
    /// <summary>
    ///     The source is not known: the runtime was not started by <see cref="CopilotRuntime.StartAsync"/>,
    ///     or it reported a kind of sign-in AgentKit does not recognize.
    /// </summary>
    Unknown = 0,

    /// <summary>
    ///     The token the host passed as <see cref="CopilotRuntimeOptions.GitHubToken"/>.
    /// </summary>
    GitHubToken = 1,

    /// <summary>
    ///     A token from the host's <see cref="CopilotRuntimeOptions.GitHubTokenProvider"/>.
    /// </summary>
    GitHubTokenProvider = 2,

    /// <summary>
    ///     A token in the application's environment: <c>COPILOT_GITHUB_TOKEN</c>, <c>GH_TOKEN</c> or
    ///     <c>GITHUB_TOKEN</c>.
    /// </summary>
    EnvironmentVariable = 3,

    /// <summary>
    ///     The user's Copilot CLI sign-in (<c>copilot login</c>), whose token AgentKit obtained through the
    ///     Copilot CLI itself.
    /// </summary>
    CopilotCli = 4,

    /// <summary>
    ///     A sign-in stored in AgentKit's data directory (the runtime's own fallback).
    /// </summary>
    DataDirectory = 5,

    /// <summary>
    ///     The signed-in GitHub CLI user (<c>gh auth login</c>; the runtime's own fallback).
    /// </summary>
    GitHubCli = 6,
}
