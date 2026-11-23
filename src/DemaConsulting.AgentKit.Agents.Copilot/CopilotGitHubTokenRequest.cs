namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     What AgentKit asks a host's <see cref="CopilotRuntimeOptions.GitHubTokenProvider"/> for: a GitHub
///     token for one host.
/// </summary>
/// <remarks>
///     <para>
///     AgentKit asks once when the runtime starts, for the host the runtime will use, with
///     <see cref="IsRefresh"/> <see langword="false"/>. The runtime then asks again for each session
///     AgentKit opens on it — <see cref="IsRefresh"/> <see langword="false"/> for the session's first
///     token, <see langword="true"/> when the token it holds is about to expire.
///     </para>
///     <para>
///     Instances are immutable and carry no secret.
///     </para>
/// </remarks>
public sealed class CopilotGitHubTokenRequest
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotGitHubTokenRequest"/> class.
    /// </summary>
    /// <remarks>
    ///     Internal: only AgentKit asks a provider for a token.
    /// </remarks>
    /// <param name="host">The GitHub host the token is for, such as <c>https://github.com</c>.</param>
    /// <param name="isRefresh">Whether the runtime is replacing a token that is about to expire.</param>
    internal CopilotGitHubTokenRequest(string host, bool isRefresh)
    {
        Host = host;
        IsRefresh = isRefresh;
    }

    /// <summary>
    ///     Gets the GitHub host the token is for, as an https URL without a path: <c>https://github.com</c>,
    ///     or a GitHub Enterprise Cloud host such as <c>https://example.ghe.com</c>.
    /// </summary>
    public string Host { get; }

    /// <summary>
    ///     Gets a value indicating whether the runtime is replacing a token that is about to expire
    ///     (<see langword="true"/>) rather than asking for the first one (<see langword="false"/>).
    /// </summary>
    public bool IsRefresh { get; }

    /// <summary>
    ///     Returns a description of the request.
    /// </summary>
    /// <returns>The host and whether it is a refresh.</returns>
    public override string ToString() => $"CopilotGitHubTokenRequest {{ Host = {Host}, IsRefresh = {IsRefresh} }}";
}
