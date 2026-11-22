namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     A GitHub token a host's <see cref="CopilotRuntimeOptions.GitHubTokenProvider"/> returns.
/// </summary>
/// <remarks>
///     <para>
///     AgentKit hands the token to the Copilot runtime and nowhere else: it is never logged, never
///     written to disk by AgentKit and never part of an exception message. <see cref="ToString"/> never
///     contains it.
///     </para>
///     <para>
///     Instances are immutable.
///     </para>
/// </remarks>
public sealed class CopilotGitHubToken
{
    /// <summary>
    ///     The shortest lifetime a token may report: the runtime needs more than one hour left on a
    ///     token after it has validated it.
    /// </summary>
    private static readonly TimeSpan MinimumLifetime = TimeSpan.FromHours(1);

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotGitHubToken"/> class.
    /// </summary>
    /// <param name="token">The GitHub token. Must not be <see langword="null"/>, empty or whitespace.</param>
    /// <param name="expiresIn">
    ///     How long the token remains valid, or <see langword="null"/> when it is not known (AgentKit then
    ///     tells the runtime eight hours, the usual lifetime of a GitHub user token). When given it must
    ///     be more than one hour: the runtime needs more than one hour left after it has validated the
    ///     token.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="token"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="token"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="expiresIn"/> is one hour or less.</exception>
    public CopilotGitHubToken(string token, TimeSpan? expiresIn = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (expiresIn is { } lifetime && lifetime <= MinimumLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresIn),
                lifetime,
                "A GitHub token for the Copilot runtime must remain valid for more than one hour; pass null when its "
                + "lifetime is not known.");
        }

        Token = token;
        ExpiresIn = expiresIn;
    }

    /// <summary>
    ///     Gets the GitHub token.
    /// </summary>
    public string Token { get; }

    /// <summary>
    ///     Gets how long the token remains valid, or <see langword="null"/> when it is not known.
    /// </summary>
    public TimeSpan? ExpiresIn { get; }

    /// <summary>
    ///     Returns a description of the token that never contains the token itself.
    /// </summary>
    /// <returns>The token's length and lifetime, with the token redacted.</returns>
    public override string ToString() =>
        $"CopilotGitHubToken {{ Token = <redacted, {Token.Length} characters>, ExpiresIn = {ExpiresIn?.ToString() ?? "unknown"} }}";
}
