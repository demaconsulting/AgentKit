namespace DemaConsulting.AgentKit.Agents.Copilot.LiveTests;

/// <summary>
///     Live sign-in scenarios (L-S1 to L-S5): the runtime AgentKit starts signs in as the user's
///     Copilot CLI account on that account's host, with or without the GitHub CLI; an explicit token
///     and host, and a token provider, are used as given; and a runtime no source signs in is refused
///     with what each source found and no secret.
/// </summary>
/// <remarks>
///     <para>
///     Each scenario clears <c>COPILOT_GITHUB_TOKEN</c>, <c>GH_TOKEN</c> and <c>GITHUB_TOKEN</c> in the
///     process environment for its own runtimes and restores them, so the Copilot CLI sign-in is the
///     source under test; the scenarios that need the user's Copilot CLI account skip when there is
///     none. "The GitHub CLI is hidden" means, for the runtimes the scenario starts only, that
///     <c>PATH</c> loses every folder holding <c>gh</c> and <c>GH_CONFIG_DIR</c> names an empty folder
///     under the run's root; the user's own GitHub CLI sign-in is never changed.
///     </para>
///     <para>
///     Nothing secret is logged: hosts, logins, sources, and token and refusal lengths only. A
///     refusal's text is never logged, and is checked for secrets with assertions that do not echo it
///     before any assertion that would.
///     </para>
/// </remarks>
[Collection(CopilotLiveCollection.Name)]
public sealed class CopilotSignInLiveTests
{
    /// <summary>
    ///     The token variables each scenario clears for its own runtimes.
    /// </summary>
    private static readonly string[] TokenVariables = ["COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN"];

    /// <summary>
    ///     How long one turn may take before the scenario gives up.
    /// </summary>
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromMinutes(4);

    /// <summary>
    ///     The run's shared state.
    /// </summary>
    private readonly CopilotLiveFixture _live;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotSignInLiveTests"/> class.
    /// </summary>
    /// <param name="live">The run's shared state.</param>
    public CopilotSignInLiveTests(CopilotLiveFixture live)
    {
        _live = live;
    }

    /// <summary>
    ///     L-S1: with no token in the environment, the run's runtime — started with default options,
    ///     exactly as a host starts one — signs in as the user's Copilot CLI account, on that account's
    ///     host, and answers a turn.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_SignIn_DefaultOptions_UseTheCopilotCliAccount()
    {
        // Arrange
        _live.RequireOptIn();
        var account = RequireCopilotCliAccount();
        Assert.SkipWhen(
            TokenVariables.Any(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))),
            "L-S1 needs a run without COPILOT_GITHUB_TOKEN, GH_TOKEN or GITHUB_TOKEN, which take precedence.");
        var runtime = _live.Runtime;

        // Act
        var reply = await RunOneTurnAsync(runtime);
        _live.Log($"L-S1 source={runtime.SignInSource} host={runtime.GitHubHost} login={runtime.GitHubLogin} reply-length={reply.Length}");

        // Assert
        Assert.Equal(CopilotSignInSource.CopilotCli, runtime.SignInSource);
        Assert.Equal(account.Host, runtime.GitHubHost);
        Assert.Equal(account.Login, runtime.GitHubLogin, ignoreCase: true);
        Assert.False(string.IsNullOrWhiteSpace(reply));
    }

    /// <summary>
    ///     L-S2: with the GitHub CLI hidden from the runtimes AgentKit starts, a new runtime still signs
    ///     in as the user's Copilot CLI account.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_SignIn_GitHubCliHidden_StillUsesTheCopilotCliAccount()
    {
        // Arrange
        _live.RequireOptIn();
        var account = RequireCopilotCliAccount();

        // Act
        await using var runtime = await WithEnvironmentAsync(
            hideGitHubCli: true,
            () => CopilotRuntime.StartAsync(_live.CreateOptions(DataDirectory("ls2-home")), TestContext.Current.CancellationToken));
        _live.Log($"L-S2 source={runtime.SignInSource} host={runtime.GitHubHost} login={runtime.GitHubLogin}");

        // Assert
        Assert.Equal(CopilotSignInSource.CopilotCli, runtime.SignInSource);
        Assert.Equal(account.Host, runtime.GitHubHost);
        Assert.Equal(account.Login, runtime.GitHubLogin, ignoreCase: true);
    }

    /// <summary>
    ///     L-S3: an explicit token with its host signs the runtime in with that token on that host; for a
    ///     GitHub Enterprise Cloud account, the same token without a host is refused, with the host hint
    ///     and no secret. The token is the Copilot CLI account's, obtained in-process and never logged.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_SignIn_ExplicitTokenAndHost_AreUsed()
    {
        // Arrange
        _live.RequireOptIn();
        var account = RequireCopilotCliAccount();
        var token = await ReadCopilotCliTokenAsync();
        _live.Log($"L-S3 token obtained through the Copilot CLI: length={token.Length}");

        // Act: the token with its host
        await using var runtime = await WithEnvironmentAsync(
            hideGitHubCli: true,
            () => CopilotRuntime.StartAsync(
                new CopilotRuntimeOptions(
                    DataDirectory("ls3-home"),
                    gitHubToken: token,
                    gitHubHost: account.Host,
                    runtimeSource: _live.RuntimeSource ?? CopilotRuntimeSource.InstalledCli,
                    cliPath: _live.RuntimeSource == CopilotRuntimeSource.Bundled ? null : _live.CliPath),
                TestContext.Current.CancellationToken));
        _live.Log($"L-S3 with host: source={runtime.SignInSource} host={runtime.GitHubHost} login={runtime.GitHubLogin}");

        // Assert: the token with its host
        Assert.Equal(CopilotSignInSource.GitHubToken, runtime.SignInSource);
        Assert.Equal(account.Host, runtime.GitHubHost);
        Assert.Equal(account.Login, runtime.GitHubLogin, ignoreCase: true);

        // Act / Assert: the same token without its host must be refused (GitHub Enterprise Cloud only)
        var isEnterprise = !string.Equals(account.Host, "https://github.com", StringComparison.Ordinal);
        if (!isEnterprise)
        {
            _live.Log("L-S3 without host: not applicable to a github.com account.");
            return;
        }

        string message;
        try
        {
            await using var withoutHostRuntime = await WithEnvironmentAsync(
                hideGitHubCli: true,
                () => CopilotRuntime.StartAsync(
                    new CopilotRuntimeOptions(DataDirectory("ls3-no-host-home"), gitHubToken: token, useCopilotCliSignIn: false),
                    TestContext.Current.CancellationToken));
            Assert.Fail(
                $"L-S3: a GitHub Enterprise Cloud token without its host started a runtime (source={withoutHostRuntime.SignInSource} "
                + $"host={withoutHostRuntime.GitHubHost} login={withoutHostRuntime.GitHubLogin}); it must be refused.");
            return;
        }
        catch (InvalidOperationException refused)
        {
            message = refused.Message;
        }

        // The secret check comes first and never echoes the text; only metadata is logged
        Assert.False(message.Contains(token, StringComparison.Ordinal), "L-S3: the refusal carries the token.");
        var hostHint = message.Contains("also needs its host", StringComparison.Ordinal);
        _live.Log($"L-S3 without host: refused; message-length={message.Length} host-hint={hostHint}");
        Assert.True(hostHint, "L-S3: the refusal does not say that a GitHub Enterprise Cloud token also needs its host.");
    }

    /// <summary>
    ///     L-S4: a token provider signs the runtime in, and is asked again for the session a turn runs
    ///     on — a client-level token from the provider together with a session-level provider.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_SignIn_TokenProvider_SignsInTheRuntimeAndItsSessions()
    {
        // Arrange
        _live.RequireOptIn();
        var account = RequireCopilotCliAccount();
        var token = await ReadCopilotCliTokenAsync();
        var requests = new List<CopilotGitHubTokenRequest>();
        var gate = new object();

        // Act
        await using var runtime = await WithEnvironmentAsync(
            hideGitHubCli: true,
            () => CopilotRuntime.StartAsync(
                new CopilotRuntimeOptions(
                    DataDirectory("ls4-home"),
                    gitHubHost: account.Host,
                    gitHubTokenProvider: (request, _) =>
                    {
                        lock (gate)
                        {
                            requests.Add(request);
                        }

                        return Task.FromResult(new CopilotGitHubToken(token));
                    },
                    runtimeSource: _live.RuntimeSource ?? CopilotRuntimeSource.InstalledCli,
                    cliPath: _live.RuntimeSource == CopilotRuntimeSource.Bundled ? null : _live.CliPath),
                TestContext.Current.CancellationToken));
        var afterStart = requests.Count;
        var reply = await RunOneTurnAsync(runtime);
        List<CopilotGitHubTokenRequest> seen;
        lock (gate)
        {
            seen = [.. requests];
        }

        _live.Log(
            $"L-S4 source={runtime.SignInSource} host={runtime.GitHubHost} login={runtime.GitHubLogin} "
            + $"provider-requests={seen.Count} (after start {afterStart}): "
            + string.Join("; ", seen.Select(request => $"{request.Host} refresh={request.IsRefresh}"))
            + $" reply-length={reply.Length}");

        // Assert
        Assert.Equal(CopilotSignInSource.GitHubTokenProvider, runtime.SignInSource);
        Assert.Equal(account.Login, runtime.GitHubLogin, ignoreCase: true);
        Assert.Equal(1, afterStart);
        Assert.True(seen.Count >= 2, "The session did not ask the token provider for its token.");
        Assert.All(seen, request => Assert.Equal(account.Host, request.Host));
        Assert.False(string.IsNullOrWhiteSpace(reply));
    }

    /// <summary>
    ///     L-S5: with no token anywhere, the Copilot CLI sign-in turned off and the GitHub CLI hidden, the
    ///     runtime is refused with what each source found, in order, and without any secret.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_SignIn_NoSource_IsRefusedListingEachSource()
    {
        // Arrange
        _live.RequireOptIn();
        var account = CopilotSignIn.LookUpCopilotCliAccount(Environment.GetEnvironmentVariables(), UserProfile(), _live.Root).Account;
        var token = account is null ? null : await ReadCopilotCliTokenAsync();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => WithEnvironmentAsync(
            hideGitHubCli: true,
            () => CopilotRuntime.StartAsync(
                new CopilotRuntimeOptions(
                    DataDirectory("ls5-home"),
                    useCopilotCliSignIn: false,
                    runtimeSource: _live.RuntimeSource ?? CopilotRuntimeSource.InstalledCli,
                    cliPath: _live.RuntimeSource == CopilotRuntimeSource.Bundled ? null : _live.CliPath),
                TestContext.Current.CancellationToken)));
        var message = failure.Message;

        // Assert: the secret checks come first and never echo the text
        if (token is not null)
        {
            Assert.False(message.Contains(token, StringComparison.Ordinal), "L-S5: the refusal carries the Copilot CLI token.");
        }

        foreach (var prefix in new[] { "gho_", "ghu_", "ghp_", "ghs_", "ghr_", "github_pat_" })
        {
            Assert.False(message.Contains(prefix, StringComparison.Ordinal), $"L-S5: the refusal carries a '{prefix}' token shape.");
        }

        var steps = new[]
        {
            "1. gitHubToken / gitHubTokenProvider: not supplied.",
            "2. COPILOT_GITHUB_TOKEN, GH_TOKEN, GITHUB_TOKEN: none set.",
            "3. Copilot CLI sign-in: turned off by the host",
            "4. Runtime fallbacks",
        };
        var present = steps.Select(step => message.Contains(step, StringComparison.Ordinal)).ToArray();
        _live.Log($"L-S5 refused; message-length={message.Length} steps-present={string.Join(",", present)}");
        for (var index = 0; index < steps.Length; index++)
        {
            Assert.True(present[index], $"L-S5: the refusal does not list step {index + 1} as expected.");
        }
    }

    /// <summary>
    ///     Skips the calling scenario unless the user is signed in to the Copilot CLI, and returns the
    ///     account.
    /// </summary>
    /// <returns>The user's Copilot CLI account.</returns>
    private CopilotCliAccount RequireCopilotCliAccount()
    {
        var lookup = CopilotSignIn.LookUpCopilotCliAccount(Environment.GetEnvironmentVariables(), UserProfile(), _live.Root);
        Assert.SkipWhen(lookup.Account is null, $"No Copilot CLI sign-in to test with: {lookup.Problem}");
        _live.Log($"Copilot CLI account: {lookup.Account.Host} / {lookup.Account.Login}");
        return lookup.Account;
    }

    /// <summary>
    ///     Obtains the Copilot CLI account's token in-process, through the same helper runtime
    ///     production uses, with the token variables cleared and the GitHub CLI hidden. Never logged.
    /// </summary>
    /// <returns>The token.</returns>
    private async Task<string> ReadCopilotCliTokenAsync()
    {
        var plan = await WithEnvironmentAsync(
            hideGitHubCli: true,
            () => CopilotSignIn.ResolveAsync(
                new CopilotRuntimeOptions(DataDirectory("token-home")),
                DataDirectory("token-home"),
                Environment.GetEnvironmentVariables(),
                new CopilotRuntimeLaunch(_live.RuntimeSource ?? CopilotRuntimeSource.InstalledCli, _live.RuntimeCliPath, null),
                CopilotSignInContext.Current(),
                TestContext.Current.CancellationToken));
        Assert.True(plan.Token is not null, $"The Copilot CLI sign-in did not yield a token: {plan.CopilotCliFinding}");
        return plan.Token;
    }

    /// <summary>
    ///     Runs one cheap turn on a runtime through a confined agent.
    /// </summary>
    /// <param name="runtime">The runtime.</param>
    /// <returns>The reply's text.</returns>
    private async Task<string> RunOneTurnAsync(CopilotRuntime runtime)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TurnTimeout);
        var agent = CopilotAgentFactory.Create(
            runtime,
            _live.ReadTools,
            "You are a concise assistant.",
            name: "live-signin-agent",
            model: _live.Model,
            workingDirectory: _live.Sandbox);
        var reply = await agent.RunAsync("Reply with the single word READY.", cancellationToken: timeout.Token);
        return reply.Text;
    }

    /// <summary>
    ///     Runs an action with the token variables cleared and, optionally, the GitHub CLI hidden, in the
    ///     process environment, restoring it afterwards whatever happens.
    /// </summary>
    /// <typeparam name="T">The action's result.</typeparam>
    /// <param name="hideGitHubCli">Whether to hide the GitHub CLI.</param>
    /// <param name="action">The action; AgentKit reads the process environment when it starts a runtime.</param>
    /// <returns>The action's result.</returns>
    private async Task<T> WithEnvironmentAsync<T>(bool hideGitHubCli, Func<Task<T>> action)
    {
        var changed = TokenVariables.Append("PATH").Append("GH_CONFIG_DIR")
            .ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        try
        {
            foreach (var name in TokenVariables)
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            if (hideGitHubCli)
            {
                var gh = OperatingSystem.IsWindows() ? "gh.exe" : "gh";
                var path = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Where(folder => !File.Exists(Path.Combine(folder.Trim('"'), gh)));
                var empty = Path.Combine(_live.Root, "empty-gh-config");
                Directory.CreateDirectory(empty);
                Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, path));
                Environment.SetEnvironmentVariable("GH_CONFIG_DIR", empty);
            }

            return await action();
        }
        finally
        {
            foreach (var (name, value) in changed)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    /// <summary>
    ///     Gets a data directory under the run's root, unique to this run.
    /// </summary>
    /// <param name="name">The folder's name.</param>
    /// <returns>The full path.</returns>
    private string DataDirectory(string name) => Path.Combine(_live.Root, name);

    /// <summary>
    ///     Gets the user's profile folder.
    /// </summary>
    /// <returns>The folder, or <see langword="null"/> when unknown.</returns>
    private static string? UserProfile()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile) ? null : profile;
    }
}
