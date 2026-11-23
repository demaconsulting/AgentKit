using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Copilot;
using Microsoft.Copilot.Rpc;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for <see cref="CopilotSignIn"/>: the sign-in source order and the host that goes with
///     each source, reading the Copilot CLI's account entry, the token-fetch helper runtime and its
///     temporary home, the refusal that lists what each source found, and the session token provider.
/// </summary>
/// <remarks>
///     No test starts a runtime, reads the real user's Copilot CLI home or touches a credential store:
///     the user's profile and every helper home are folders under the system temporary folder, the
///     helper runtime is a fake, and the machine <c>PATH</c> is a <see cref="FakeCopilotCliMachine"/>.
/// </remarks>
public sealed class CopilotSignInTests : IDisposable
{
    /// <summary>
    ///     The Copilot CLI account the tests sign in with: a GitHub Enterprise Cloud host and an EMU login.
    /// </summary>
    private const string AccountHost = "https://example.ghe.com";

    /// <summary>
    ///     The account's login.
    /// </summary>
    private const string AccountLogin = "octo_user";

    /// <summary>
    ///     A sentinel token that must never appear in any message or string form.
    /// </summary>
    private const string SentinelToken = "gho_SENTINEL0123456789abcdefABCDEF0123";

    /// <summary>
    ///     A path no platform accepts: it holds a null character, so <see cref="Path.GetFullPath(string)"/>
    ///     refuses it with <see cref="ArgumentException"/>.
    /// </summary>
    private const string InvalidPath = "home\0bad";

    /// <summary>
    ///     The user's Copilot CLI configuration the tests write: comments, a trailing comma, and keys
    ///     besides the account entry that must never be copied.
    /// </summary>
    private const string UserConfiguration = """
        {
          // The Copilot CLI writes comments and trailing commas
          "firstLaunchAt": "2026-01-01T00:00:00Z",
          "mcpServers": { "probe": { "command": "probe" } },
          "trustedFolders": ["/work"],
          "loggedInUsers": [ { "host": "https://example.ghe.com", "login": "octo_user" } ],
          "lastLoggedInUser": { "host": "https://example.ghe.com", "login": "octo_user" },
        }
        """;

    /// <summary>
    ///     What the installed CLI launch looks like to the resolution.
    /// </summary>
    private static readonly CopilotRuntimeLaunch Launch =
        new(CopilotRuntimeSource.InstalledCli, Path.Combine("/fake", "bin", "copilot"), new Version(1, 0, 114));

    /// <summary>
    ///     Logs an entry in the shape the SDK logs an RPC failure with.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception?> RpcError =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(7, "RpcError"),
            "Error handling JSON-RPC method {Method}: {Error}");

    /// <summary>
    ///     Begins a scope naming a session.
    /// </summary>
    private static readonly Func<ILogger, string, IDisposable?> SessionScope =
        LoggerMessage.DefineScope<string>("Session {SessionId}");

    /// <summary>
    ///     Logs an entry whose values are of every kind the redacting logger keeps typed, and a text.
    /// </summary>
    private static readonly Action<ILogger, int, bool, TimeSpan, CopilotSignInSource, Guid, string, Exception?> ProbeLogged =
        LoggerMessage.Define<int, bool, TimeSpan, CopilotSignInSource, Guid, string>(
            LogLevel.Warning,
            new EventId(9, "Probe"),
            "Probe {Count} {Flag} {Elapsed} {Source} {Id} {Path}");

    /// <summary>
    ///     The root of this test's folders under the system temporary folder.
    /// </summary>
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "agentkit-copilot-sign-in-unit-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    ///     The helper homes the resolution created, in order.
    /// </summary>
    private readonly List<string> _helperHomes = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotSignInTests"/> class, creating the user's
    ///     profile and the data directory under the test's root.
    /// </summary>
    public CopilotSignInTests()
    {
        Directory.CreateDirectory(Profile);
        Directory.CreateDirectory(DataDirectory);
    }

    /// <summary>
    ///     Gets the fake user's profile folder.
    /// </summary>
    private string Profile => Path.Combine(_root, "profile");

    /// <summary>
    ///     Gets the data directory the resolution is told about.
    /// </summary>
    private string DataDirectory => Path.Combine(_root, "data");

    /// <summary>
    ///     Gets the fake helper runtime every resolution in the test creates.
    /// </summary>
    private FakeHelper Helper { get; } = new();

    /// <summary>
    ///     Deletes the test's folders.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    ///     Proves a token the host supplies wins: the Copilot CLI is not read and no helper runs.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_ExplicitToken_WinsWithoutRunningTheHelper()
    {
        // Arrange
        WriteUserConfiguration(UserConfiguration);
        var options = new CopilotRuntimeOptions(DataDirectory, gitHubToken: SentinelToken);

        // Act
        var plan = await ResolveAsync(options, new Hashtable { ["GH_TOKEN"] = "gho_environment" });

        // Assert
        Assert.Equal(CopilotSignInSource.GitHubToken, plan.Source);
        Assert.Equal(SentinelToken, plan.Token);
        Assert.Null(plan.HostName);
        Assert.Null(plan.CliAccount);
        Assert.Equal(0, Helper.Created);
        Assert.Contains("takes precedence", plan.EnvironmentFinding, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a host-supplied token is used with the host the host chose, given to the runtime as a
    ///     bare host name.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_ExplicitTokenWithHost_SetsTheHostName()
    {
        // Arrange
        var options = new CopilotRuntimeOptions(DataDirectory, gitHubToken: SentinelToken, gitHubHost: "https://Example.ghe.com/");

        // Act
        var plan = await ResolveAsync(options, new Hashtable());

        // Assert
        Assert.Equal("example.ghe.com", plan.HostName);
        Assert.Equal(AccountHost, plan.RequestedHost);
    }

    /// <summary>
    ///     Proves the host's token provider is asked once, for the effective host — the option's, else
    ///     <c>COPILOT_GH_HOST</c>, else github.com — as a first (not refresh) request, and that its token
    ///     is used.
    /// </summary>
    /// <param name="optionHost">The <c>gitHubHost</c> option, or <see langword="null"/>.</param>
    /// <param name="environmentHost">The environment's <c>COPILOT_GH_HOST</c>, or <see langword="null"/>.</param>
    /// <param name="expectedHost">The host the provider is asked for.</param>
    [Theory]
    [InlineData("example.ghe.com", "other.ghe.com", "https://example.ghe.com")]
    [InlineData(null, "other.ghe.com", "https://other.ghe.com")]
    [InlineData(null, null, "https://github.com")]
    public async Task CopilotSignIn_ResolveAsync_Provider_IsAskedForTheEffectiveHost(
        string? optionHost,
        string? environmentHost,
        string expectedHost)
    {
        // Arrange
        var requests = new List<CopilotGitHubTokenRequest>();
        var options = new CopilotRuntimeOptions(
            DataDirectory,
            gitHubHost: optionHost,
            gitHubTokenProvider: (request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new CopilotGitHubToken(SentinelToken));
            });
        var environment = new Hashtable();
        if (environmentHost is not null)
        {
            environment["COPILOT_GH_HOST"] = environmentHost;
        }

        // Act
        var plan = await ResolveAsync(options, environment);

        // Assert
        var request = Assert.Single(requests);
        Assert.Equal(expectedHost, request.Host);
        Assert.False(request.IsRefresh);
        Assert.Equal(CopilotSignInSource.GitHubTokenProvider, plan.Source);
        Assert.Equal(SentinelToken, plan.Token);
        Assert.Same(options.GitHubTokenProvider, plan.Provider);
        Assert.Equal(0, Helper.Created);
    }

    /// <summary>
    ///     Proves a provider that returns no token refuses the start, naming the provider.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_ProviderReturnsNull_Throws()
    {
        // Arrange
        var options = new CopilotRuntimeOptions(DataDirectory, gitHubTokenProvider: (_, _) => Task.FromResult<CopilotGitHubToken>(null!));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => ResolveAsync(options, new Hashtable()));

        // Assert
        Assert.Contains("gitHubTokenProvider", failure.Message, StringComparison.Ordinal);
        Assert.Contains("returned no token", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a provider that fails refuses the start, naming the provider and keeping the failure as
    ///     the cause.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_ProviderThrows_ThrowsWithTheCause()
    {
        // Arrange
        var cause = new HttpRequestException("token service unavailable");
        var options = new CopilotRuntimeOptions(DataDirectory, gitHubTokenProvider: (_, _) => Task.FromException<CopilotGitHubToken>(cause));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => ResolveAsync(options, new Hashtable()));

        // Assert
        Assert.Same(cause, failure.InnerException);
        Assert.Contains("gitHubTokenProvider", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a canceled provider propagates the cancellation unchanged.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_ProviderCanceled_PropagatesCancellation()
    {
        // Arrange
        var options = new CopilotRuntimeOptions(
            DataDirectory,
            gitHubTokenProvider: (_, _) => Task.FromException<CopilotGitHubToken>(new OperationCanceledException()));

        // Act / Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => ResolveAsync(options, new Hashtable()));
    }

    /// <summary>
    ///     Proves a token in any of the environment variables is left to the runtime and keeps the
    ///     Copilot CLI sign-in from being tried, while the account it found is still named.
    /// </summary>
    /// <param name="variable">The variable holding a token.</param>
    [Theory]
    [InlineData("COPILOT_GITHUB_TOKEN")]
    [InlineData("GH_TOKEN")]
    [InlineData("GITHUB_TOKEN")]
    public async Task CopilotSignIn_ResolveAsync_EnvironmentToken_SkipsTheCopilotCli(string variable)
    {
        // Arrange
        WriteUserConfiguration(UserConfiguration);

        // Act
        var plan = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable { [variable] = "gho_environment" });

        // Assert
        Assert.Equal(CopilotSignInSource.EnvironmentVariable, plan.Source);
        Assert.Null(plan.Token);
        Assert.Null(plan.HostName);
        Assert.Equal(0, Helper.Created);
        Assert.Contains(variable, plan.EnvironmentFinding, StringComparison.Ordinal);
        Assert.Contains("takes precedence", plan.CopilotCliFinding, StringComparison.Ordinal);
        Assert.Contains($"{AccountHost} / {AccountLogin}", plan.CopilotCliFinding, StringComparison.Ordinal);
        Assert.DoesNotContain("gho_environment", plan.EnvironmentFinding, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that with no token from the host or the environment the Copilot CLI's token is used,
    ///     with its account's host given to the runtime, and that the helper is disposed and its home
    ///     deleted.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_NoTokens_UsesTheCopilotCliTokenAndHost()
    {
        // Arrange
        WriteUserConfiguration(UserConfiguration);

        // Act
        var plan = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable());

        // Assert
        Assert.Equal(CopilotSignInSource.CopilotCli, plan.Source);
        Assert.Equal(SentinelToken, plan.Token);
        Assert.Equal("example.ghe.com", plan.HostName);
        Assert.Equal(new CopilotCliAccount(AccountHost, AccountLogin), plan.CliAccount);
        Assert.Equal(1, Helper.Created);
        Assert.True(Helper.Disposed);
        Assert.All(_helperHomes, home => Assert.False(Directory.Exists(home)));
        Assert.DoesNotContain(SentinelToken, plan.CopilotCliFinding, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the Copilot CLI sign-in turned off by the host is not read at all, and says so.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_CopilotCliTurnedOff_DoesNotReadTheConfiguration()
    {
        // Arrange
        WriteUserConfiguration(UserConfiguration);

        // Act
        var plan = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory, useCopilotCliSignIn: false), new Hashtable());

        // Assert
        Assert.Null(plan.Source);
        Assert.Null(plan.Token);
        Assert.Null(plan.CliAccount);
        Assert.Equal(0, Helper.Created);
        Assert.Contains("turned off by the host", plan.CopilotCliFinding, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a Copilot CLI account on another host than the one requested is not used, and says so.
    /// </summary>
    /// <param name="optionHost">The <c>gitHubHost</c> option, or <see langword="null"/>.</param>
    /// <param name="environmentHost">The environment's <c>COPILOT_GH_HOST</c>, or <see langword="null"/>.</param>
    [Theory]
    [InlineData("github.com", null)]
    [InlineData(null, "other.ghe.com")]
    public async Task CopilotSignIn_ResolveAsync_RequestedHostDiffers_SkipsTheCopilotCli(string? optionHost, string? environmentHost)
    {
        // Arrange
        WriteUserConfiguration(UserConfiguration);
        var environment = new Hashtable();
        if (environmentHost is not null)
        {
            environment["COPILOT_GH_HOST"] = environmentHost;
        }

        // Act
        var plan = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory, gitHubHost: optionHost), environment);

        // Assert
        Assert.Null(plan.Source);
        Assert.Null(plan.Token);
        Assert.Equal(0, Helper.Created);
        Assert.Contains("was requested", plan.CopilotCliFinding, StringComparison.Ordinal);
        Assert.Equal(new CopilotCliAccount(AccountHost, AccountLogin), plan.CliAccount);
    }

    /// <summary>
    ///     Proves a <c>COPILOT_GH_HOST</c> that is not a valid GitHub host refuses the start with a
    ///     message naming the variable and the remedies, without quoting the value, before any token is
    ///     resolved or helper run.
    /// </summary>
    /// <param name="environmentHost">The environment's invalid <c>COPILOT_GH_HOST</c>.</param>
    [Theory]
    [InlineData("http://x.ghe.com")]
    [InlineData("x.ghe.com/path")]
    [InlineData("https://x.ghe.com:8443")]
    [InlineData("https://user:s3cr3t-host-xyz@x.ghe.com")]
    [InlineData("not a host")]
    public async Task CopilotSignIn_ResolveAsync_InvalidEnvironmentHost_IsRefused(string environmentHost)
    {
        // Arrange: a Copilot CLI account the start would otherwise use, and a provider never to be asked
        WriteUserConfiguration(UserConfiguration);
        var asked = 0;
        var options = new CopilotRuntimeOptions(
            DataDirectory,
            gitHubTokenProvider: (_, _) =>
            {
                asked++;
                return Task.FromResult(new CopilotGitHubToken(SentinelToken));
            });

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ResolveAsync(options, new Hashtable { ["COPILOT_GH_HOST"] = environmentHost }));
        var withoutProvider = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable { ["COPILOT_GH_HOST"] = environmentHost }));

        // Assert
        Assert.Equal(CopilotSignIn.InvalidEnvironmentHostMessage, failure.Message);
        Assert.Equal(CopilotSignIn.InvalidEnvironmentHostMessage, withoutProvider.Message);
        Assert.Contains("COPILOT_GH_HOST", failure.Message, StringComparison.Ordinal);
        Assert.Contains("gitHubHost", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(environmentHost, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t-host-xyz", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, asked);
        Assert.Equal(0, Helper.Created);
    }

    /// <summary>
    ///     Proves the host's <c>gitHubHost</c> takes precedence over an invalid <c>COPILOT_GH_HOST</c>,
    ///     which is then neither consulted nor refused, and a blank one is treated as unset.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_InvalidEnvironmentHostWithTheOption_UsesTheOption()
    {
        // Act
        var plan = await ResolveAsync(
            new CopilotRuntimeOptions(DataDirectory, gitHubToken: SentinelToken, gitHubHost: "example.ghe.com"),
            new Hashtable { ["COPILOT_GH_HOST"] = "http://not-valid.example" });
        var blank = await ResolveAsync(
            new CopilotRuntimeOptions(DataDirectory, gitHubToken: SentinelToken),
            new Hashtable { ["COPILOT_GH_HOST"] = "   " });

        // Assert
        Assert.Equal("example.ghe.com", plan.HostName);
        Assert.Equal(AccountHost, plan.RequestedHost);
        Assert.Null(blank.HostName);
        Assert.Null(blank.RequestedHost);
    }

    /// <summary>
    ///     Proves a valid <c>COPILOT_GH_HOST</c> in any accepted form is handed to the runtime
    ///     normalized, as a bare lower-case host name, on every path — a host token, an environment
    ///     token and the runtime's own fallbacks — so the token and the runtime's endpoint agree.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_EnvironmentHost_IsGivenToTheRuntimeNormalized()
    {
        // Arrange
        const string environmentHost = "https://Example.GHE.com/";

        // Act
        var token = await ResolveAsync(
            new CopilotRuntimeOptions(DataDirectory, gitHubToken: SentinelToken),
            new Hashtable { ["COPILOT_GH_HOST"] = environmentHost });
        var environmentToken = await ResolveAsync(
            new CopilotRuntimeOptions(DataDirectory),
            new Hashtable { ["COPILOT_GH_HOST"] = environmentHost, ["GH_TOKEN"] = "gho_environment" });
        var fallbacks = await ResolveAsync(
            new CopilotRuntimeOptions(DataDirectory),
            new Hashtable { ["COPILOT_GH_HOST"] = environmentHost });
        var clientOptions = CopilotRuntime.BuildClientOptions(
            new CopilotRuntimeOptions(DataDirectory),
            DataDirectory,
            new Hashtable { ["COPILOT_GH_HOST"] = environmentHost },
            signIn: fallbacks);

        // Assert
        Assert.All(
            new[] { token, environmentToken, fallbacks },
            plan =>
            {
                Assert.Equal("example.ghe.com", plan.HostName);
                Assert.Equal(AccountHost, plan.RequestedHost);
            });
        Assert.Equal(
            "example.ghe.com",
            Assert.IsType<SpawnedStdioRuntimeConnection>(clientOptions.Runtime).Environment!["COPILOT_GH_HOST"]);
    }

    /// <summary>
    ///     Proves a helper that cannot start leaves a finding and falls through to the runtime's
    ///     fallbacks, with the helper disposed and its home deleted.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_HelperFails_FallsThroughWithAFinding()
    {
        // Arrange
        WriteUserConfiguration(UserConfiguration);
        Helper.StartFailure = new InvalidOperationException($"runtime exited; stray {SentinelToken}");

        // Act
        var plan = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable());

        // Assert
        Assert.Null(plan.Source);
        Assert.Null(plan.Token);
        Assert.Contains("could not be run", plan.CopilotCliFinding, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelToken, plan.CopilotCliFinding, StringComparison.Ordinal);
        Assert.True(Helper.Disposed);
        Assert.All(_helperHomes, home => Assert.False(Directory.Exists(home)));
    }

    /// <summary>
    ///     Proves a helper client that cannot be constructed leaves a redacted finding and falls
    ///     through to the runtime's fallbacks, with nothing to dispose and its home deleted.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_HelperCannotBeCreated_FallsThroughWithAFinding()
    {
        // Arrange
        WriteUserConfiguration(UserConfiguration);
        Helper.CreateFailure = new InvalidOperationException($"bad options; stray {SentinelToken}");

        // Act
        var plan = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable());

        // Assert
        Assert.Null(plan.Source);
        Assert.Null(plan.Token);
        Assert.Contains("could not be run", plan.CopilotCliFinding, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelToken, plan.CopilotCliFinding, StringComparison.Ordinal);
        Assert.Equal(1, Helper.Created);
        Assert.False(Helper.Disposed);
        Assert.All(_helperHomes, home => Assert.False(Directory.Exists(home)));
    }

    /// <summary>
    ///     Proves a helper that does not answer within its time limit leaves a finding and falls
    ///     through, with the helper disposed and its home deleted.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_HelperTimesOut_FallsThroughWithAFinding()
    {
        // Arrange
        WriteUserConfiguration(UserConfiguration);
        Helper.Hang = true;

        // Act
        var plan = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable(), helperTimeout: TimeSpan.FromMilliseconds(100));

        // Assert
        Assert.Null(plan.Source);
        Assert.Contains("did not report the account within", plan.CopilotCliFinding, StringComparison.Ordinal);
        Assert.True(Helper.Disposed);
        Assert.All(_helperHomes, home => Assert.False(Directory.Exists(home)));
    }

    /// <summary>
    ///     Proves the caller's cancellation while the helper runs propagates, with the helper disposed
    ///     and its home deleted.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_CallerCancels_PropagatesAndCleansUp()
    {
        // Arrange
        WriteUserConfiguration(UserConfiguration);
        using var cancellation = new CancellationTokenSource();
        Helper.Hang = true;
        Helper.OnStart = cancellation.Cancel;

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable(), cancellationToken: cancellation.Token));

        // Assert
        Assert.True(Helper.Disposed);
        Assert.NotEmpty(_helperHomes);
        Assert.All(_helperHomes, home => Assert.False(Directory.Exists(home)));
    }

    /// <summary>
    ///     Proves the host's chosen host is given to the runtime when its own fallbacks decide, and
    ///     that the fallbacks' step says whether the GitHub CLI is on <c>PATH</c>.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_Fallbacks_KeepTheChosenHostAndReportTheGitHubCli()
    {
        // Arrange: no Copilot CLI configuration; gh in one PATH folder
        var machine = new FakeCopilotCliMachine();
        var gh = machine.AddFile(Path.Combine("/opt/gh/bin", "gh"));

        // Act
        var withGh = await ResolveAsync(
            new CopilotRuntimeOptions(DataDirectory, gitHubHost: "example.ghe.com"),
            new Hashtable(),
            machine.Context(CopilotCliPlatform.Linux, "/opt/gh/bin"));
        var withoutGh = await ResolveAsync(
            new CopilotRuntimeOptions(DataDirectory),
            new Hashtable(),
            machine.Context(CopilotCliPlatform.Linux, "/usr/bin"));

        // Assert
        Assert.Null(withGh.Source);
        Assert.Equal("example.ghe.com", withGh.HostName);
        Assert.Contains(gh, withGh.FallbackFinding, StringComparison.Ordinal);
        Assert.Contains("no Copilot CLI configuration", withGh.CopilotCliFinding, StringComparison.Ordinal);
        Assert.Null(withoutGh.HostName);
        Assert.Contains("was not found on PATH", withoutGh.FallbackFinding, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the helper's home holds exactly the account entry — no other key of the user's
    ///     configuration — and an empty folder for the GitHub CLI's configuration.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_HelperHome_HoldsOnlyTheAccountEntry()
    {
        // Arrange
        WriteUserConfiguration(UserConfiguration);

        // Act
        await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable());

        // Assert
        Assert.Equal(
            """{"loggedInUsers":[{"host":"https://example.ghe.com","login":"octo_user"}],"lastLoggedInUser":{"host":"https://example.ghe.com","login":"octo_user"}}""",
            Helper.SeenConfiguration);
        Assert.Equal(new[] { "config.json", "gh" }, Helper.SeenEntries);
        Assert.Equal(0, Helper.SeenGitHubCliEntries);
    }

    /// <summary>
    ///     Proves the helper's environment withholds every token and host variable and the host's
    ///     Copilot configuration, isolates the GitHub CLI's configuration, and keeps only the rooted
    ///     <c>PATH</c> folders that hold no GitHub CLI.
    /// </summary>
    [Fact]
    public void CopilotSignIn_BuildHelperEnvironment_WithholdsTokensAndHidesTheGitHubCli()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        machine.AddFile(Path.Combine("/opt/gh/bin", "gh"));
        machine.AddFile(Path.Combine("/usr/bin", "node"));
        var host = new Hashtable
        {
            ["PATH"] = "/usr/bin:/opt/gh/bin:relative/bin",
            ["HOME"] = "/home/user",
            ["COPILOT_GITHUB_TOKEN"] = "a",
            ["GH_TOKEN"] = "b",
            ["GITHUB_TOKEN"] = "c",
            ["GH_ENTERPRISE_TOKEN"] = "d",
            ["GITHUB_ENTERPRISE_TOKEN"] = "e",
            ["COPILOT_GH_HOST"] = "example.ghe.com",
            ["GH_HOST"] = "example.ghe.com",
            ["COPILOT_ALLOW_ALL"] = "1",
        };

        // Act
        var environment = CopilotSignIn.BuildHelperEnvironment(host, "/tmp/helper/gh", machine.Context(CopilotCliPlatform.Linux), "/usr/bin/copilot");

        // Assert
        Assert.Equal("/usr/bin", environment["PATH"]);
        Assert.Equal("/tmp/helper/gh", environment["GH_CONFIG_DIR"]);
        Assert.Equal("/home/user", environment["HOME"]);
        foreach (var withheld in new[] { "COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN", "GH_ENTERPRISE_TOKEN", "GITHUB_ENTERPRISE_TOKEN", "COPILOT_GH_HOST", "GH_HOST", "COPILOT_ALLOW_ALL" })
        {
            Assert.False(environment.ContainsKey(withheld), withheld);
        }
    }

    /// <summary>
    ///     Proves a CLI that is a <c>.js</c> script keeps a <c>PATH</c> folder holding <c>node</c>, which
    ///     it needs, even when that folder also holds the GitHub CLI.
    /// </summary>
    [Fact]
    public void CopilotSignIn_BuildHelperEnvironment_ScriptCli_KeepsTheFolderHoldingNode()
    {
        // Arrange: gh and node share one folder
        var machine = new FakeCopilotCliMachine();
        machine.AddFile(Path.Combine("/usr/bin", "gh"));
        machine.AddFile(Path.Combine("/usr/bin", "node"));
        var host = new Hashtable { ["PATH"] = "/usr/bin" };

        // Act
        var script = CopilotSignIn.BuildHelperEnvironment(host, "/tmp/gh", machine.Context(CopilotCliPlatform.Linux), "/usr/lib/node_modules/@github/copilot/npm-loader.js");
        var native = CopilotSignIn.BuildHelperEnvironment(host, "/tmp/gh", machine.Context(CopilotCliPlatform.Linux), "/usr/local/bin/copilot");

        // Assert
        Assert.Equal("/usr/bin", script["PATH"]);
        Assert.Equal(string.Empty, native["PATH"]);
    }

    /// <summary>
    ///     Proves the helper runs the same executable in CodingAgent mode — the only mode that reads the
    ///     credential store — on its own home, with no token and no capability channel set.
    /// </summary>
    [Fact]
    public void CopilotSignIn_BuildHelperOptions_RunsTheSameExecutableWithNothingElse()
    {
        // Arrange
        var environment = new Dictionary<string, string> { ["PATH"] = "/usr/bin" };

        // Act
        var options = CopilotSignIn.BuildHelperOptions("/usr/local/bin/copilot", environment, "/tmp/helper");

        // Assert
        Assert.Equal(CopilotClientMode.CodingAgent, options.Mode);
        var connection = Assert.IsType<SpawnedStdioRuntimeConnection>(options.Runtime);
        Assert.Equal("/usr/local/bin/copilot", connection.Path);
        Assert.Equal("/tmp/helper", connection.BaseDirectory);
        Assert.Null(connection.Args);
        Assert.Same(environment, connection.Environment);
        Assert.Null(options.GitHub.Token);
        Assert.Empty(options.Plugins.BuiltinDirectories ?? []);
        Assert.Null(options.SessionFs);
        Assert.Null(options.Extensions.LaunchProvider);
        Assert.Null(options.Models.RequestHandler);
    }

    /// <summary>
    ///     Proves a helper that is not signed in through a stored sign-in as exactly the account is
    ///     never asked for its account list, so the SDK defect on other kinds of account is not reached.
    /// </summary>
    /// <param name="authenticated">Whether the helper reports being signed in.</param>
    /// <param name="authType">The kind of sign-in it reports.</param>
    /// <param name="login">The login it reports.</param>
    /// <param name="expected">Text the finding carries.</param>
    [Theory]
    [InlineData(false, null, null, "could not read that account's token")]
    [InlineData(true, "gh-cli", AccountLogin, "reported another sign-in")]
    [InlineData(true, "user", "someone_else", "reported another sign-in")]
    public async Task CopilotSignIn_ResolveAsync_HelperNotSignedInAsTheAccount_DoesNotReadTheAccountList(
        bool authenticated,
        string? authType,
        string? login,
        string expected)
    {
        // Arrange
        WriteUserConfiguration(UserConfiguration);
        Helper.Status = new GetAuthStatusResponse
        {
            IsAuthenticated = authenticated,
            AuthType = authType,
            Host = AccountHost,
            Login = login,
            StatusMessage = authenticated ? null : "Not authenticated",
        };

        // Act
        var plan = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable());

        // Assert
        Assert.Null(plan.Source);
        Assert.Equal(0, Helper.UserReads);
        Assert.Contains(expected, plan.CopilotCliFinding, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an account list the SDK cannot read (its 2.0.1 deserialization defect) is a finding,
    ///     not a failure, and the next source applies.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_AccountListFails_FallsThroughWithAFinding()
    {
        // Arrange
        WriteUserConfiguration(UserConfiguration);
        Helper.UsersFailure = new JsonException(
            "JSON deserialization for type 'Microsoft.Copilot.Rpc.AuthInfoGhCli' was missing required properties including: 'token'.");

        // Act
        var plan = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable());

        // Assert
        Assert.Null(plan.Source);
        Assert.Contains("could not read the Copilot CLI's account list", plan.CopilotCliFinding, StringComparison.Ordinal);
        Assert.True(Helper.Disposed);
    }

    /// <summary>
    ///     Proves the token is taken only from the stored-sign-in entry of exactly the account, never
    ///     from another account's entry, and that no matching entry is a finding.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_AccountList_TakesOnlyTheAccountsUserEntry()
    {
        // Arrange: another login on the same host, then the account itself
        WriteUserConfiguration(UserConfiguration);
        Helper.Users =
        [
            new AccountAllUsers { AuthInfo = new AuthInfoUser { Host = AccountHost, Login = "someone_else" }, Token = "gho_other" },
            new AccountAllUsers { AuthInfo = new AuthInfoUser { Host = AccountHost, Login = AccountLogin }, Token = SentinelToken },
        ];

        // Act
        var plan = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable());
        Helper.Users = [new AccountAllUsers { AuthInfo = new AuthInfoUser { Host = "https://github.com", Login = AccountLogin }, Token = "gho_other" }];
        var none = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable());

        // Assert
        Assert.Equal(SentinelToken, plan.Token);
        Assert.Null(none.Token);
        Assert.Contains("returned no token for that account", none.CopilotCliFinding, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the user's Copilot CLI home is <c>COPILOT_HOME</c> when set, and <c>.copilot</c> in the
    ///     profile otherwise.
    /// </summary>
    [Fact]
    public void CopilotSignIn_LookUpCopilotCliAccount_ReadsCopilotHomeOrTheProfileDefault()
    {
        // Arrange: one account in the profile's home, another in a COPILOT_HOME
        WriteUserConfiguration(UserConfiguration);
        var custom = Path.Combine(_root, "custom-home");
        Directory.CreateDirectory(custom);
        File.WriteAllText(
            Path.Combine(custom, "config.json"),
            """{ "lastLoggedInUser": { "host": "https://github.com", "login": "web_user" } }""");

        // Act
        var fromProfile = CopilotSignIn.LookUpCopilotCliAccount(new Hashtable(), Profile, DataDirectory);
        var fromVariable = CopilotSignIn.LookUpCopilotCliAccount(new Hashtable { ["COPILOT_HOME"] = custom }, Profile, DataDirectory);

        // Assert
        Assert.Equal(new CopilotCliAccount(AccountHost, AccountLogin), fromProfile.Account);
        Assert.Equal(Path.Combine(Profile, ".copilot", "config.json"), fromProfile.ConfigurationPath);
        Assert.Equal(new CopilotCliAccount("https://github.com", "web_user"), fromVariable.Account);
    }

    /// <summary>
    ///     Proves a <c>COPILOT_HOME</c> naming AgentKit's own data directory is never read as the user's
    ///     home.
    /// </summary>
    [Fact]
    public void CopilotSignIn_LookUpCopilotCliAccount_DataDirectoryAsHome_IsNotRead()
    {
        // Arrange
        File.WriteAllText(Path.Combine(DataDirectory, "config.json"), UserConfiguration);

        // Act
        var lookup = CopilotSignIn.LookUpCopilotCliAccount(new Hashtable { ["COPILOT_HOME"] = DataDirectory }, Profile, DataDirectory);

        // Assert
        Assert.Null(lookup.Account);
        Assert.Contains("AgentKit's own data directory", lookup.Problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a <c>COPILOT_HOME</c> that is not a valid path yields no account and a finding naming
    ///     the variable, without quoting its value, rather than an exception.
    /// </summary>
    [Fact]
    public void CopilotSignIn_LookUpCopilotCliAccount_InvalidCopilotHome_ReportsWhy()
    {
        // Act
        var lookup = CopilotSignIn.LookUpCopilotCliAccount(new Hashtable { ["COPILOT_HOME"] = InvalidPath }, Profile, DataDirectory);

        // Assert
        Assert.Null(lookup.Account);
        Assert.Null(lookup.ConfigurationPath);
        Assert.Contains("COPILOT_HOME is not a valid path", lookup.Problem, StringComparison.Ordinal);
        Assert.DoesNotContain(InvalidPath, lookup.Problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a <c>COPILOT_HOME</c> that is not a valid path does not fail the start: the Copilot CLI
    ///     step records why, and the next source applies.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_InvalidCopilotHome_FallsThroughWithAFinding()
    {
        // Arrange: an account in the profile's default home, which COPILOT_HOME overrides
        WriteUserConfiguration(UserConfiguration);

        // Act
        var plan = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable { ["COPILOT_HOME"] = InvalidPath });

        // Assert
        Assert.Null(plan.Source);
        Assert.Null(plan.Token);
        Assert.Null(plan.CliAccount);
        Assert.Contains("COPILOT_HOME is not a valid path", plan.CopilotCliFinding, StringComparison.Ordinal);
        Assert.Equal(0, Helper.Created);
    }

    /// <summary>
    ///     Proves each configuration AgentKit cannot use yields no account and a finding naming why.
    /// </summary>
    /// <param name="content">The configuration's content, or <see langword="null"/> for no file.</param>
    /// <param name="expected">Text the finding carries.</param>
    [Theory]
    [InlineData(null, "no Copilot CLI configuration")]
    [InlineData("{ not json", "is not valid JSON")]
    [InlineData("[]", "is not a JSON object")]
    [InlineData("""{ "firstLaunchAt": "x" }""", "no signed-in account")]
    [InlineData("""{ "loggedInUsers": [ { "host": "https://github.com", "login": "a" }, { "host": "https://example.ghe.com", "login": "b" } ] }""", "no signed-in account")]
    [InlineData("""{ "lastLoggedInUser": { "host": "http://github.com", "login": "a" } }""", "no valid host and login")]
    [InlineData("""{ "lastLoggedInUser": { "host": "https://github.com/path", "login": "a" } }""", "no valid host and login")]
    [InlineData("""{ "lastLoggedInUser": { "host": "https://github.com", "login": "" } }""", "no valid host and login")]
    [InlineData("""{ "lastLoggedInUser": { "host": "https://github.com", "login": "a\u0007b" } }""", "no valid host and login")]
    [InlineData("""{ "lastLoggedInUser": { "host": "https://github.com", "login": 42 } }""", "no valid host and login")]
    public void CopilotSignIn_ReadCopilotCliAccount_UnusableConfiguration_ReportsWhy(string? content, string expected)
    {
        // Arrange
        var path = Path.Combine(_root, "config.json");
        if (content is not null)
        {
            File.WriteAllText(path, content);
        }

        // Act
        var lookup = CopilotSignIn.ReadCopilotCliAccount(path);

        // Assert
        Assert.Null(lookup.Account);
        Assert.Contains(expected, lookup.Problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the single signed-in account is used when no last signed-in account is recorded, and
    ///     that a configuration with comments and trailing commas is read.
    /// </summary>
    [Fact]
    public void CopilotSignIn_ReadCopilotCliAccount_SingleAccountWithCommentsAndTrailingCommas_IsRead()
    {
        // Arrange
        var path = Path.Combine(_root, "config.json");
        File.WriteAllText(path, """
            {
              /* only the list */
              "loggedInUsers": [ { "host": "Example.GHE.com", "login": "octo_user", }, ],
            }
            """);

        // Act
        var lookup = CopilotSignIn.ReadCopilotCliAccount(path);

        // Assert
        Assert.Equal(new CopilotCliAccount(AccountHost, AccountLogin), lookup.Account);
        Assert.Null(lookup.Problem);
    }

    /// <summary>
    ///     Proves a configuration larger than the limit is not read.
    /// </summary>
    [Fact]
    public void CopilotSignIn_ReadCopilotCliAccount_TooLarge_IsNotRead()
    {
        // Arrange
        var path = Path.Combine(_root, "config.json");
        File.WriteAllText(path, "{\"pad\":\"" + new string('x', CopilotSignIn.ConfigurationSizeLimit) + "\"}");

        // Act
        var lookup = CopilotSignIn.ReadCopilotCliAccount(path);

        // Assert
        Assert.Null(lookup.Account);
        Assert.Contains("is larger than", lookup.Problem, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the refusal lists all four sources with what each found and every remedy, and never
    ///     carries the token.
    /// </summary>
    [Fact]
    public void CopilotSignIn_ExplainUnauthenticated_ListsEverySourceWithoutTheToken()
    {
        // Arrange
        var plan = new CopilotSignInPlan
        {
            Source = CopilotSignInSource.CopilotCli,
            Token = SentinelToken,
            HostName = "example.ghe.com",
            HostSuppliedFinding = "not supplied.",
            EnvironmentFinding = "none set.",
            CopilotCliFinding = "https://example.ghe.com / octo_user found; token obtained.",
            FallbackFinding = "not tried: an earlier source applied.",
        };
        var authentication = new GetAuthStatusResponse { IsAuthenticated = false, StatusMessage = $"Bad credentials for {SentinelToken}" };

        // Act
        var message = CopilotSignIn.ExplainUnauthenticated(plan, authentication);

        // Assert
        Assert.Contains("1. gitHubToken / gitHubTokenProvider: not supplied.", message, StringComparison.Ordinal);
        Assert.Contains("2. COPILOT_GITHUB_TOKEN, GH_TOKEN, GITHUB_TOKEN: none set.", message, StringComparison.Ordinal);
        Assert.Contains("3. Copilot CLI sign-in: https://example.ghe.com / octo_user found", message, StringComparison.Ordinal);
        Assert.Contains("4. Runtime fallbacks", message, StringComparison.Ordinal);
        Assert.Contains("did not accept the token from step 3", message, StringComparison.Ordinal);
        Assert.Contains("copilot login", message, StringComparison.Ordinal);
        Assert.Contains("gh auth login", message, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelToken, message, StringComparison.Ordinal);
        Assert.DoesNotContain(SentinelToken[4..], message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a host-supplied token the runtime did not accept, used without a host, is explained
    ///     with the host it may need, and that an environment token is explained with how to move on.
    /// </summary>
    [Fact]
    public void CopilotSignIn_ExplainUnauthenticated_RejectedTokens_SayWhatToChange()
    {
        // Arrange
        var authentication = new GetAuthStatusResponse { IsAuthenticated = false };
        var token = new CopilotSignInPlan { Source = CopilotSignInSource.GitHubToken, Token = SentinelToken };
        var tokenWithHost = new CopilotSignInPlan { Source = CopilotSignInSource.GitHubToken, Token = SentinelToken, RequestedHost = AccountHost };
        var environment = new CopilotSignInPlan { Source = CopilotSignInSource.EnvironmentVariable };

        // Act
        var withoutHost = CopilotSignIn.ExplainUnauthenticated(token, authentication);
        var withHost = CopilotSignIn.ExplainUnauthenticated(tokenWithHost, authentication);
        var fromEnvironment = CopilotSignIn.ExplainUnauthenticated(environment, authentication);

        // Assert
        Assert.Contains("also needs its host (gitHubHost or COPILOT_GH_HOST)", withoutHost, StringComparison.Ordinal);
        Assert.DoesNotContain("also needs its host", withHost, StringComparison.Ordinal);
        Assert.Contains("did not accept the environment token", fromEnvironment, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the plan's string form never carries the token, nor a known secret its host quotes.
    /// </summary>
    [Fact]
    public void CopilotSignIn_PlanToString_RedactsTheToken()
    {
        // Arrange
        const string secret = "s3cr3t-value-xyz";
        var plan = new CopilotSignInPlan { Source = CopilotSignInSource.CopilotCli, Token = SentinelToken, HostName = "example.ghe.com" };
        var secretHost = new CopilotSignInPlan { HostName = $"{secret}.ghe.com", Secrets = PlanKnowing(secret).Secrets };

        // Act
        var text = plan.ToString();
        var hostText = secretHost.ToString();

        // Assert
        Assert.DoesNotContain(SentinelToken, text, StringComparison.Ordinal);
        Assert.Contains("<redacted>", text, StringComparison.Ordinal);
        Assert.Contains("example.ghe.com", text, StringComparison.Ordinal);
        Assert.Contains("HostName = <redacted>.ghe.com", hostText, StringComparison.Ordinal);
        SecretAssert.NoSecretInText(hostText, secret);
    }

    /// <summary>
    ///     Proves each kind of sign-in the runtime reports maps to its source.
    /// </summary>
    /// <param name="authType">The runtime's <c>authType</c>.</param>
    /// <param name="planned">The source the plan handed over, or <see langword="null"/>.</param>
    /// <param name="expected">The expected source.</param>
    [Theory]
    [InlineData("token", CopilotSignInSource.GitHubToken, CopilotSignInSource.GitHubToken)]
    [InlineData("token", CopilotSignInSource.GitHubTokenProvider, CopilotSignInSource.GitHubTokenProvider)]
    [InlineData("token", CopilotSignInSource.CopilotCli, CopilotSignInSource.CopilotCli)]
    [InlineData("token", null, CopilotSignInSource.Unknown)]
    [InlineData("token-provider", null, CopilotSignInSource.GitHubTokenProvider)]
    [InlineData("env", CopilotSignInSource.EnvironmentVariable, CopilotSignInSource.EnvironmentVariable)]
    [InlineData("user", null, CopilotSignInSource.DataDirectory)]
    [InlineData("gh-cli", null, CopilotSignInSource.GitHubCli)]
    [InlineData("hmac", null, CopilotSignInSource.Unknown)]
    [InlineData(null, null, CopilotSignInSource.Unknown)]
    public void CopilotSignIn_Classify_MapsTheRuntimesKindOfSignIn(string? authType, CopilotSignInSource? planned, CopilotSignInSource expected)
    {
        // Arrange
        var plan = new CopilotSignInPlan { Source = planned };

        // Act
        var source = CopilotSignIn.Classify(authType, plan);

        // Assert
        Assert.Equal(expected, source);
    }

    /// <summary>
    ///     Proves the accepted forms of a GitHub host normalize to a lower-case https URL without a path.
    /// </summary>
    /// <param name="host">The host as given.</param>
    /// <param name="expected">The normalized host.</param>
    [Theory]
    [InlineData("example.ghe.com", "https://example.ghe.com")]
    [InlineData("https://example.ghe.com", "https://example.ghe.com")]
    [InlineData("https://Example.GHE.com/", "https://example.ghe.com")]
    [InlineData("  github.com  ", "https://github.com")]
    public void CopilotSignIn_NormalizeHost_AcceptedForms_Normalize(string host, string expected)
    {
        // Act
        var normalized = CopilotSignIn.NormalizeHost(host, "gitHubHost");

        // Assert
        Assert.Equal(expected, normalized);
        Assert.Equal(expected[8..], CopilotSignIn.HostName(normalized!));
    }

    /// <summary>
    ///     Proves every other form of a GitHub host is refused, naming the parameter.
    /// </summary>
    /// <param name="host">The host as given.</param>
    [Theory]
    [InlineData("http://example.ghe.com")]
    [InlineData("ftp://example.ghe.com")]
    [InlineData("https://example.ghe.com/path")]
    [InlineData("https://example.ghe.com?x=1")]
    [InlineData("https://example.ghe.com#x")]
    [InlineData("https://user@example.ghe.com")]
    [InlineData("https://example.ghe.com:8443")]
    [InlineData("example.ghe.com/path")]
    [InlineData("https://")]
    [InlineData("not a host")]
    public void CopilotSignIn_NormalizeHost_RejectedForms_Throw(string host)
    {
        // Act
        var failure = Assert.Throws<ArgumentException>(() => CopilotSignIn.NormalizeHost(host, "gitHubHost"));

        // Assert
        Assert.Equal("gitHubHost", failure.ParamName);
    }

    /// <summary>
    ///     Proves a rejected GitHub host is not quoted in the refusal, since a URL may carry a user name
    ///     and password.
    /// </summary>
    [Fact]
    public void CopilotSignIn_NormalizeHost_RejectedHost_IsNotQuoted()
    {
        // Act
        var failure = Assert.Throws<ArgumentException>(
            () => CopilotSignIn.NormalizeHost("https://user:s3cr3t-host@example.ghe.com", "gitHubHost"));

        // Assert
        Assert.Equal("gitHubHost", failure.ParamName);
        Assert.DoesNotContain("s3cr3t-host", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("user:", failure.Message, StringComparison.Ordinal);
        SecretAssert.NoSecret(failure, "s3cr3t-host", "user:");
    }

    /// <summary>
    ///     Proves the session token provider asks the host's provider with the session's host and
    ///     whether it is a refresh, and hands the runtime the token with its lifetime (eight hours when
    ///     not known).
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_CreateSessionTokenProvider_AsksTheHostsProvider()
    {
        // Arrange
        var requests = new List<CopilotGitHubTokenRequest>();
        var lifetime = (TimeSpan?)null;
        var sessionProvider = CopilotSignIn.CreateSessionTokenProvider(
            (request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(new CopilotGitHubToken(SentinelToken, lifetime));
            },
            new CopilotSignInSecrets());

        // Act
        var initial = await sessionProvider(new GitHubTokenProviderArgs { Host = "https://Example.ghe.com", Reason = GitHubTokenRequestReason.Initial });
        lifetime = TimeSpan.FromHours(2);
        var refresh = await sessionProvider(new GitHubTokenProviderArgs { Host = AccountHost, Reason = GitHubTokenRequestReason.Refresh });

        // Assert
        Assert.Equal(new[] { AccountHost, AccountHost }, requests.Select(request => request.Host));
        Assert.Equal(new[] { false, true }, requests.Select(request => request.IsRefresh));
        Assert.Equal(SentinelToken, initial.Token!.AccessToken);
        Assert.Equal(8 * 60 * 60, initial.Token.ExpiresIn);
        Assert.Equal(2 * 60 * 60, refresh.Token!.ExpiresIn);
    }

    /// <summary>
    ///     Proves a lifetime with a fraction of a second is reported to the runtime rounded up, so a
    ///     lifetime of more than one hour is never reported as one hour.
    /// </summary>
    /// <param name="extraTicks">The ticks beyond one hour.</param>
    /// <param name="expectedSeconds">The seconds the runtime is told.</param>
    [Theory]
    [InlineData(1L, 3601L)]
    [InlineData(5_000_000L, 3601L)]
    [InlineData(36_000_000_000L, 7200L)]
    public async Task CopilotSignIn_CreateSessionTokenProvider_FractionalLifetime_RoundsUp(long extraTicks, long expectedSeconds)
    {
        // Arrange
        var lifetime = TimeSpan.FromHours(1) + TimeSpan.FromTicks(extraTicks);
        var sessionProvider = CopilotSignIn.CreateSessionTokenProvider(
            (_, _) => Task.FromResult(new CopilotGitHubToken(SentinelToken, lifetime)),
            new CopilotSignInSecrets());

        // Act
        var result = await sessionProvider(new GitHubTokenProviderArgs { Host = AccountHost, Reason = GitHubTokenRequestReason.Initial });

        // Assert
        Assert.Equal(expectedSeconds, result.Token!.ExpiresIn);
    }

    /// <summary>
    ///     Proves each token the host's provider returns for a session is recorded, so text quoted
    ///     afterwards never carries it, whatever its shape.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_CreateSessionTokenProvider_RecordsEachTokenForRedaction()
    {
        // Arrange
        const string sessionToken = "s3cr3t-session-xyz";
        var secrets = new CopilotSignInSecrets();
        var sessionProvider = CopilotSignIn.CreateSessionTokenProvider(
            (_, _) => Task.FromResult(new CopilotGitHubToken(sessionToken)),
            secrets);

        // Act
        await sessionProvider(new GitHubTokenProviderArgs { Host = AccountHost, Reason = GitHubTokenRequestReason.Initial });
        var redacted = secrets.Redact($"session refused: {sessionToken} expired");

        // Assert
        Assert.False(redacted.Contains(sessionToken, StringComparison.Ordinal), "The session token was not redacted.");
        Assert.Equal("session refused: <redacted> expired", redacted);
    }

    /// <summary>
    ///     Proves every exact token the sign-in knows — the host's token, the provider's token, an
    ///     environment token and the Copilot CLI's token — is redacted from the runtime's status
    ///     message in the refusal, even when it is not shaped like a GitHub token.
    /// </summary>
    /// <param name="source">Where the secret comes from.</param>
    [Theory]
    [InlineData("gitHubToken")]
    [InlineData("gitHubTokenProvider")]
    [InlineData("GH_TOKEN")]
    [InlineData("CopilotCli")]
    public async Task CopilotSignIn_ExplainUnauthenticated_NonGitHubShapedTokens_AreRedacted(string source)
    {
        // Arrange: a secret no token-shape pattern recognizes
        const string secret = "s3cr3t-value-xyz";
        WriteUserConfiguration(UserConfiguration);
        var environment = new Hashtable();
        var options = source switch
        {
            "gitHubToken" => new CopilotRuntimeOptions(DataDirectory, gitHubToken: secret),
            "gitHubTokenProvider" => new CopilotRuntimeOptions(
                DataDirectory,
                gitHubTokenProvider: (_, _) => Task.FromResult(new CopilotGitHubToken(secret))),
            _ => new CopilotRuntimeOptions(DataDirectory),
        };
        if (source == "GH_TOKEN")
        {
            environment["GH_TOKEN"] = secret;
        }

        if (source == "CopilotCli")
        {
            Helper.Users = [new AccountAllUsers { AuthInfo = new AuthInfoUser { Host = AccountHost, Login = AccountLogin }, Token = secret }];
        }

        var plan = await ResolveAsync(options, environment);
        var authentication = new GetAuthStatusResponse { IsAuthenticated = false, StatusMessage = $"Bad credentials: {secret}" };

        // Act
        var message = CopilotSignIn.ExplainUnauthenticated(plan, authentication);

        // Assert
        Assert.False(message.Contains(secret, StringComparison.Ordinal), "The refusal carries the secret.");
        Assert.Contains("(the runtime reports: Bad credentials: <redacted>)", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a helper failure that quotes a known secret — here a GitHub Enterprise token from the
    ///     environment, not shaped like a GitHub token — leaves a finding without it.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_HelperFailureQuotingAKnownSecret_IsRedacted()
    {
        // Arrange
        const string secret = "s3cr3t-enterprise-xyz";
        WriteUserConfiguration(UserConfiguration);
        Helper.StartFailure = new InvalidOperationException($"runtime exited; stray {secret}");

        // Act
        var plan = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), new Hashtable { ["GH_ENTERPRISE_TOKEN"] = secret });

        // Assert
        Assert.Contains("could not be run", plan.CopilotCliFinding, StringComparison.Ordinal);
        Assert.False(plan.CopilotCliFinding.Contains(secret, StringComparison.Ordinal), "The finding carries the secret.");
        Assert.Contains("stray <redacted>", plan.CopilotCliFinding, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves every finding redacts each field it quotes from the configuration, the environment
    ///     or the host's options — the requested host, the Copilot CLI account's host and login, the
    ///     configuration's path, a lookup's problem and the GitHub CLI's folder — so neither a finding
    ///     nor the refusal built from them carries a known secret.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_FindingsQuotingASecret_AreRedacted()
    {
        // Arrange: a GitHub Enterprise token, not shaped like a GitHub token, whose value also appears
        // in COPILOT_HOME's path, the configuration's login, the requested host and the GitHub CLI's folder
        const string secret = "s3cr3t-value-xyz";
        var home = Path.Combine(_root, $"home-{secret}");
        Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(
            Path.Combine(home, "config.json"),
            $$"""{ "lastLoggedInUser": { "host": "{{AccountHost}}", "login": "octo-{{secret}}" } }""",
            TestContext.Current.CancellationToken);
        var machine = new FakeCopilotCliMachine();
        machine.AddFile(Path.Combine($"/opt/{secret}/bin", "gh"));
        var search = machine.Context(CopilotCliPlatform.Linux, $"/opt/{secret}/bin");
        var secretHost = $"{secret}.ghe.com";
        Hashtable EnvironmentWith(string copilotHome, string? ghToken = null)
        {
            var environment = new Hashtable { ["GH_ENTERPRISE_TOKEN"] = secret, ["COPILOT_HOME"] = copilotHome };
            if (ghToken is not null)
            {
                environment["GH_TOKEN"] = ghToken;
            }

            return environment;
        }

        // Act
        var hostToken = await ResolveAsync(
            new CopilotRuntimeOptions(DataDirectory, gitHubToken: SentinelToken, gitHubHost: secretHost),
            EnvironmentWith(home));
        var hostProvider = await ResolveAsync(
            new CopilotRuntimeOptions(
                DataDirectory,
                gitHubHost: secretHost,
                gitHubTokenProvider: (_, _) => Task.FromResult(new CopilotGitHubToken(SentinelToken))),
            EnvironmentWith(home));
        var environmentToken = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), EnvironmentWith(home, "gho_environment"));
        var otherHost = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory, gitHubHost: secretHost), EnvironmentWith(home), search);
        var missingHome = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), EnvironmentWith(Path.Combine(_root, $"missing-{secret}")));
        Helper.StartFailure = new IOException($"pipe broke for {secret}");
        var helperFailed = await ResolveAsync(new CopilotRuntimeOptions(DataDirectory), EnvironmentWith(home));

        // Assert: the fields were quoted, and redacted
        Assert.Contains("https://<redacted>.ghe.com", hostToken.HostSuppliedFinding, StringComparison.Ordinal);
        Assert.Contains("https://<redacted>.ghe.com", hostProvider.HostSuppliedFinding, StringComparison.Ordinal);
        Assert.Contains($"{AccountHost} / octo-<redacted> found in", environmentToken.CopilotCliFinding, StringComparison.Ordinal);
        Assert.Contains("home-<redacted>", environmentToken.CopilotCliFinding, StringComparison.Ordinal);
        Assert.Contains("but https://<redacted>.ghe.com was requested", otherHost.CopilotCliFinding, StringComparison.Ordinal);
        Assert.Contains("/opt/<redacted>/bin", otherHost.FallbackFinding, StringComparison.Ordinal);
        Assert.Contains("missing-<redacted>", missingHome.CopilotCliFinding, StringComparison.Ordinal);
        Assert.Contains("pipe broke for <redacted>", helperFailed.CopilotCliFinding, StringComparison.Ordinal);
        var unauthenticated = new GetAuthStatusResponse { IsAuthenticated = false };
        foreach (var plan in new[] { hostToken, hostProvider, environmentToken, otherHost, missingHome, helperFailed })
        {
            SecretAssert.NoSecretInText(plan.HostSuppliedFinding, secret, SentinelToken);
            SecretAssert.NoSecretInText(plan.EnvironmentFinding, secret, SentinelToken);
            SecretAssert.NoSecretInText(plan.CopilotCliFinding, secret, SentinelToken);
            SecretAssert.NoSecretInText(plan.FallbackFinding, secret, SentinelToken);
            SecretAssert.NoSecretInText(CopilotSignIn.ExplainUnauthenticated(plan, unauthenticated), secret, SentinelToken);
        }
    }

    /// <summary>
    ///     Proves the entry logged for a Copilot CLI sign-in that was found but not used carries the
    ///     finding redacted — the account's login, the configuration's path, the requested host and a
    ///     helper's failure — in its message and in its structured values alike.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_CopilotCliNotUsed_LogsTheFindingRedacted()
    {
        // Arrange
        const string secret = "s3cr3t-value-xyz";
        var home = Path.Combine(_root, $"home-{secret}");
        Directory.CreateDirectory(home);
        await File.WriteAllTextAsync(
            Path.Combine(home, "config.json"),
            $$"""{ "lastLoggedInUser": { "host": "{{AccountHost}}", "login": "octo-{{secret}}" } }""",
            TestContext.Current.CancellationToken);
        var environment = new Hashtable { ["GITHUB_ENTERPRISE_TOKEN"] = secret, ["COPILOT_HOME"] = home };
        var logger = new CapturingLogger();

        // Act
        await ResolveAsync(new CopilotRuntimeOptions(DataDirectory, logger: logger, gitHubHost: $"{secret}.ghe.com"), environment);
        Helper.StartFailure = new InvalidOperationException($"runtime exited quoting {secret} {SentinelToken}");
        await ResolveAsync(new CopilotRuntimeOptions(DataDirectory, logger: logger), environment);

        // Assert
        var entries = logger.Entries;
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry => Assert.Equal("CopilotCliSignInNotUsed", entry.EventId.Name));
        Assert.Contains("octo-<redacted> found in", entries[0].Message, StringComparison.Ordinal);
        Assert.Contains("home-<redacted>", entries[0].Message, StringComparison.Ordinal);
        Assert.Contains("https://<redacted>.ghe.com was requested", entries[0].Message, StringComparison.Ordinal);
        Assert.Contains("quoting <redacted> gho_<redacted>", entries[1].Message, StringComparison.Ordinal);
        SecretAssert.NoSecret(logger, secret, SentinelToken);
    }

    /// <summary>
    ///     Proves a helper that reports another sign-in, with a kind, host and login that each quote a
    ///     known secret or a token-shaped value, leaves a finding — and a log entry — with each field
    ///     redacted.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_HelperReportsAnotherSignInQuotingASecret_IsRedacted()
    {
        // Arrange
        const string secret = "s3cr3t-value-xyz";
        WriteUserConfiguration(UserConfiguration);
        Helper.Status = new GetAuthStatusResponse
        {
            IsAuthenticated = true,
            AuthType = $"kind-{secret}",
            Host = $"https://{secret}.ghe.com",
            Login = $"octo-{secret} {SentinelToken}",
        };
        var logger = new CapturingLogger();

        // Act
        var plan = await ResolveAsync(
            new CopilotRuntimeOptions(DataDirectory, logger: logger),
            new Hashtable { ["GH_ENTERPRISE_TOKEN"] = secret });

        // Assert
        Assert.Null(plan.Source);
        Assert.Equal(0, Helper.UserReads);
        Assert.Contains(
            "reported another sign-in (kind-<redacted>: https://<redacted>.ghe.com / octo-<redacted> gho_<redacted>)",
            plan.CopilotCliFinding,
            StringComparison.Ordinal);
        SecretAssert.NoSecretInText(plan.CopilotCliFinding, secret, SentinelToken);
        Assert.Contains("reported another sign-in", Assert.Single(logger.Entries).Message, StringComparison.Ordinal);
        SecretAssert.NoSecret(logger, secret, SentinelToken);
    }

    /// <summary>
    ///     Proves the redacting logger passes on only redacted entries — the formatted message, every
    ///     structured value, the scope and the logged exception, whose aggregate's second inner
    ///     exception and data quote the secret — while the same entries logged directly are caught by
    ///     the shared assertion.
    /// </summary>
    [Fact]
    public void CopilotSignIn_RedactingLogger_RedactsMessageValuesScopesAndExceptions()
    {
        // Arrange: an entry in the SDK's shape, under a scope, with a failure quoting the secret
        const string secret = "s3cr3t-value-xyz";
        var secrets = new CopilotSignInSecrets();
        secrets.Add(secret);
        var inner = new CapturingLogger();
        var raw = new CapturingLogger();
        var logger = CopilotRedactingLogger.Wrap(inner, text => secrets.Redact(text))!;
        var failure = Thrown(new InvalidOperationException(
            "Error handling JSON-RPC method",
            new AggregateException("both failed", new IOException("first"), new IOException($"second {secret}"))));
        failure.Data["token"] = secret;
        void Write(ILogger target)
        {
            using (SessionScope(target, secret))
            {
                RpcError(target, "session.send", $"refused {secret} {SentinelToken}", failure);
            }
        }

        // Act
        Write(logger);
        Write(raw);

        // Assert
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SecretAssert.NoSecret(raw, secret));
        SecretAssert.NoSecret(inner, secret, SentinelToken);
        var entry = Assert.Single(inner.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Error handling JSON-RPC method session.send: refused <redacted> gho_<redacted>", entry.Message);
        Assert.Equal(entry.Message, entry.StateText);
        Assert.Contains(entry.State, pair => pair.Key == "Method" && Equals(pair.Value, "session.send"));
        Assert.Contains(entry.State, pair => pair.Key == "Error" && Equals(pair.Value, "refused <redacted> gho_<redacted>"));
        Assert.Contains(entry.State, pair => pair.Key == "{OriginalFormat}" && Equals(pair.Value, "Error handling JSON-RPC method {Method}: {Error}"));
        var copy = Assert.IsType<CopilotRedactedException>(entry.Exception);
        Assert.Equal(typeof(InvalidOperationException).FullName, copy.OriginalTypeName);
        Assert.Empty(copy.Data);
        Assert.Equal("Session <redacted>", Assert.Single(inner.Scopes).Text);
    }

    /// <summary>
    ///     Proves the redacting logger keeps each entry's level and event, keeps values that cannot
    ///     hold text with their type, formats nothing for a level the host's logger does not enable,
    ///     reports the host's logger's levels, and wraps no logger as no logger.
    /// </summary>
    [Fact]
    public void CopilotSignIn_RedactingLogger_KeepsLevelsEventsAndPrimitiveValues()
    {
        // Arrange
        const string secret = "s3cr3t-value-xyz";
        var secrets = new CopilotSignInSecrets();
        secrets.Add(secret);
        var inner = new CapturingLogger { MinimumLevel = LogLevel.Information };
        var logger = CopilotRedactingLogger.Wrap(inner, text => secrets.Redact(text))!;
        var id = Guid.NewGuid();
        var formatted = 0;

        // Act
        ProbeLogged(logger, 42, true, TimeSpan.FromSeconds(3), CopilotSignInSource.CopilotCli, id, $"path {secret}", null);
        logger.Log(LogLevel.Debug, new EventId(10, "Dropped"), secret, null, (state, _) =>
        {
            formatted++;
            return state;
        });

        // Assert
        var entry = Assert.Single(inner.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(new EventId(9, "Probe"), entry.EventId);
        Assert.Equal(9, entry.EventId.Id);
        Assert.Equal("Probe", entry.EventId.Name);
        var values = entry.State.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        Assert.Equal(42, Assert.IsType<int>(values["Count"]));
        Assert.True(Assert.IsType<bool>(values["Flag"]));
        Assert.Equal(TimeSpan.FromSeconds(3), Assert.IsType<TimeSpan>(values["Elapsed"]));
        Assert.Equal(CopilotSignInSource.CopilotCli, Assert.IsType<CopilotSignInSource>(values["Source"]));
        Assert.Equal(id, Assert.IsType<Guid>(values["Id"]));
        Assert.Equal("path <redacted>", Assert.IsType<string>(values["Path"]));
        Assert.Equal(0, formatted);
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.True(logger.IsEnabled(LogLevel.Information));
        Assert.Null(CopilotRedactingLogger.Wrap(null, text => text));
        SecretAssert.NoSecret(inner, secret);
    }

    /// <summary>
    ///     Proves the caller's cancellation, when the helper's cancellation carries the helper's text
    ///     and a cause quoting a known secret, is rebuilt redacted with the same token — not passed on
    ///     as the SDK raised it — and the helper is still stopped and its home deleted.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_CallerCanceledHelperCarryingASecret_IsRebuiltRedacted()
    {
        // Arrange
        const string secret = "s3cr3t-value-xyz";
        WriteUserConfiguration(UserConfiguration);
        using var cancellation = new CancellationTokenSource();
        var raised = new OperationCanceledException($"helper stopped {secret}", new IOException($"pipe {secret}"), cancellation.Token);
        Helper.OnStart = cancellation.Cancel;
        Helper.StartFailure = raised;

        // Act
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => ResolveAsync(
            new CopilotRuntimeOptions(DataDirectory),
            new Hashtable { ["GH_ENTERPRISE_TOKEN"] = secret },
            cancellationToken: cancellation.Token));

        // Assert
        Assert.NotSame(raised, failure);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal("helper stopped <redacted>", failure.Message);
        Assert.IsType<CopilotRedactedException>(failure.InnerException);
        SecretAssert.NoSecret(failure, secret);
        Assert.True(Helper.Disposed);
        Assert.All(_helperHomes, home => Assert.False(Directory.Exists(home)));
    }

    /// <summary>
    ///     Proves a helper home that could not be deleted is logged with its path redacted, through a
    ///     logger the caller did not wrap.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ReadCopilotCliToken_HomeNotDeleted_LogsThePathRedacted()
    {
        // Arrange: a home whose path quotes a known secret, and a deletion that fails
        const string secret = "s3cr3t-value-xyz";
        var home = Path.Combine(_root, $"helper-{secret}");
        var context = new CopilotSignInContext(
            new FakeCopilotCliMachine().Context(CopilotCliPlatform.Linux),
            Profile,
            () =>
            {
                Directory.CreateDirectory(home);
                return home;
            },
            Helper.Create,
            _ => false,
            TimeSpan.FromMinutes(1));
        var logger = new CapturingLogger();

        // Act
        var (token, _) = await CopilotSignIn.ReadCopilotCliTokenAsync(
            new CopilotCliAccount(AccountHost, AccountLogin),
            new Hashtable(),
            cliPath: null,
            context,
            PlanKnowing(secret).Secrets,
            logger,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SentinelToken, token);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal("CopilotSignInHomeNotDeleted", entry.EventId.Name);
        Assert.Contains("helper-<redacted>", entry.Message, StringComparison.Ordinal);
        SecretAssert.NoSecret(logger, secret, SentinelToken);
    }

    /// <summary>
    ///     Proves the sign-in the runtime reports, and the Copilot CLI account it is warned against,
    ///     are logged with every field redacted.
    /// </summary>
    [Fact]
    public void CopilotSignIn_Report_FieldsQuotingASecret_AreLoggedRedacted()
    {
        // Arrange
        const string secret = "s3cr3t-value-xyz";
        var logger = new CapturingLogger();
        var plan = new CopilotSignInPlan
        {
            CliAccount = new CopilotCliAccount(AccountHost, $"octo-{secret}"),
            Secrets = PlanKnowing(secret).Secrets,
        };
        var authentication = new GetAuthStatusResponse
        {
            IsAuthenticated = true,
            AuthType = "gh-cli",
            Host = $"https://{secret}.ghe.com",
            Login = $"web-{secret} {SentinelToken}",
        };

        // Act
        CopilotSignIn.Report(logger, plan, CopilotSignInSource.GitHubCli, authentication);

        // Assert
        var entries = logger.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Contains("https://<redacted>.ghe.com / web-<redacted> gho_<redacted>", entries[0].Message, StringComparison.Ordinal);
        Assert.Contains("octo-<redacted>", entries[1].Message, StringComparison.Ordinal);
        SecretAssert.NoSecret(logger, secret, SentinelToken);
    }

    /// <summary>
    ///     Proves the refusal of a provider that fails or returns no token names the host redacted when
    ///     the host quotes a known secret, while the provider's own exception is kept unchanged.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_ResolveAsync_ProviderRefusalForAHostQuotingASecret_IsRedacted()
    {
        // Arrange
        const string secret = "s3cr3t-value-xyz";
        var environment = new Hashtable { ["GH_ENTERPRISE_TOKEN"] = secret, ["COPILOT_GH_HOST"] = $"{secret}.ghe.com" };
        var cause = new HttpRequestException("token service unavailable");

        // Act
        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => ResolveAsync(
            new CopilotRuntimeOptions(DataDirectory, gitHubTokenProvider: (_, _) => Task.FromException<CopilotGitHubToken>(cause)),
            environment));
        var empty = await Assert.ThrowsAsync<InvalidOperationException>(() => ResolveAsync(
            new CopilotRuntimeOptions(DataDirectory, gitHubTokenProvider: (_, _) => Task.FromResult<CopilotGitHubToken>(null!)),
            environment));
        var withoutSecrets = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotSignIn.AskProviderAsync(
            (_, _) => Task.FromResult<CopilotGitHubToken>(null!),
            AccountHost,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(cause, failed.InnerException);
        Assert.Contains("for a token for https://<redacted>.ghe.com", failed.Message, StringComparison.Ordinal);
        Assert.Contains("no token for https://<redacted>.ghe.com", empty.Message, StringComparison.Ordinal);
        Assert.Contains($"no token for {AccountHost}", withoutSecrets.Message, StringComparison.Ordinal);
        SecretAssert.NoSecret(failed, secret);
        SecretAssert.NoSecret(empty, secret);
    }

    /// <summary>
    ///     Proves a session's refusal for a host the runtime names that cannot be normalized, and that
    ///     quotes a known secret, carries the host redacted.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_CreateSessionTokenProvider_NoTokenForAHostQuotingASecret_IsRedacted()
    {
        // Arrange
        const string secret = "s3cr3t-value-xyz";
        var sessionProvider = CopilotSignIn.CreateSessionTokenProvider(
            (_, _) => Task.FromResult<CopilotGitHubToken>(null!),
            PlanKnowing(secret).Secrets);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => sessionProvider(
            new GitHubTokenProviderArgs { Host = $"https://user:{secret}@example.ghe.com", Reason = GitHubTokenRequestReason.Initial }));

        // Assert
        Assert.Contains("no token for https://user:<redacted>@example.ghe.com", failure.Message, StringComparison.Ordinal);
        SecretAssert.NoSecret(failure, secret);
    }

    /// <summary>
    ///     Proves the secrets an environment holds are the value of each secret variable, read with the
    ///     platform's name comparison, and the host's token — from a host environment and from a
    ///     spawned process's environment alike — and nothing else.
    /// </summary>
    [Fact]
    public void CopilotSignIn_SecretsFromEnvironment_RecordsEverySecretVariable()
    {
        // Arrange
        var host = new Hashtable
        {
            ["COPILOT_GITHUB_TOKEN"] = "s3cr3t-copilot",
            ["GH_TOKEN"] = "s3cr3t-gh",
            ["GITHUB_TOKEN"] = "s3cr3t-github",
            ["GH_ENTERPRISE_TOKEN"] = "s3cr3t-ghe",
            ["GITHUB_ENTERPRISE_TOKEN"] = "s3cr3t-github-enterprise",
            ["PATH"] = "/usr/bin",
        };
        IReadOnlyDictionary<string, string> spawned = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GH_ENTERPRISE_TOKEN"] = "s3cr3t-spawned",
            ["HOME"] = "/home/user",
        };
        const string text = "copilot gh github ghe github-enterprise host spawned: s3cr3t-copilot s3cr3t-gh s3cr3t-github "
            + "s3cr3t-ghe s3cr3t-github-enterprise s3cr3t-host s3cr3t-spawned /usr/bin /home/user";

        // Act
        var fromHost = CopilotSignInSecrets.FromEnvironment(host, "s3cr3t-host").Redact(text);
        var fromSpawned = CopilotSignInSecrets.FromEnvironment(spawned).Redact(text);

        // Assert
        Assert.Equal(
            "copilot gh github ghe github-enterprise host spawned: <redacted> <redacted> <redacted> <redacted> <redacted> "
            + "<redacted> s3cr3t-spawned /usr/bin /home/user",
            fromHost);
        Assert.EndsWith("s3cr3t-github-enterprise s3cr3t-host <redacted> /usr/bin /home/user", fromSpawned, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the plan's redaction removes overlapping secrets whole, longest first, ignores blank
    ///     values, and still redacts text shaped like a GitHub token.
    /// </summary>
    [Fact]
    public void CopilotSignIn_PlanRedact_LongestSecretFirstAndTokenShapes()
    {
        // Arrange
        var secrets = new CopilotSignInSecrets();
        secrets.Add("s3cr3t");
        secrets.Add("s3cr3t-value-xyz");
        secrets.Add(null);
        secrets.Add("   ");
        var plan = new CopilotSignInPlan { Token = "plan-token-abc", Secrets = secrets };

        // Act
        var redacted = plan.Redact($"a s3cr3t-value-xyz b s3cr3t c plan-token-abc d {SentinelToken} e");

        // Assert
        Assert.Equal("a <redacted> b <redacted> c <redacted> d gho_<redacted> e", redacted);
    }

    /// <summary>
    ///     Proves the redacted copy of a failure carries no token the sign-in knows, nor any text
    ///     shaped like a GitHub token, anywhere in its chain: not in a message, a stack trace or a
    ///     string form, of a nested inner exception, an aggregate or any of the aggregate's inner
    ///     exceptions.
    /// </summary>
    [Fact]
    public void CopilotSignIn_RedactFailure_ChainQuotingKnownSecrets_CarriesNoSecret()
    {
        // Arrange: a chain quoting a known secret, the plan's token and a token-shaped value, with a
        // stack trace that quotes one too
        const string secret = "s3cr3t-chain-xyz";
        const string token = "s3cr3t-token-abc";
        var secrets = new CopilotSignInSecrets();
        secrets.Add(secret);
        var plan = new CopilotSignInPlan { Token = token, Secrets = secrets };
        var aggregate = new AggregateException(
            $"both failed for {secret}",
            new StackQuotingException($"first {secret}", $"   at Fake.Connect({secret}, {token})"),
            new IOException($"second {token} {SentinelToken}"));
        var failure = Thrown(new InvalidOperationException(
            $"outer {secret}",
            new InvalidDataException($"middle {SentinelToken}", aggregate)));

        // Act
        var copy = CopilotSignIn.RedactFailure(failure, plan);

        // Assert: the whole chain is copied, and none of it carries a secret
        var middle = Assert.IsType<CopilotRedactedException>(copy.InnerException);
        var copiedAggregate = Assert.IsType<CopilotRedactedException>(middle.InnerException);
        var first = Assert.IsType<CopilotRedactedException>(copiedAggregate.InnerException);
        Assert.Equal(typeof(AggregateException).FullName, copiedAggregate.OriginalTypeName);
        Assert.Equal(typeof(StackQuotingException).FullName, first.OriginalTypeName);
        Assert.Contains("<redacted>", first.StackTrace, StringComparison.Ordinal);
        Assert.Contains("second <redacted> gho_<redacted>", copiedAggregate.Message, StringComparison.Ordinal);
        SecretAssert.NoSecret(copy, secret, token, SentinelToken);
        SecretAssert.NoSecret(new InvalidOperationException("outer", copy), secret, token, SentinelToken);
    }

    /// <summary>
    ///     Proves the redacted copy of a failure keeps its type name, message, stack trace and
    ///     HResult, lays out its string form as an exception's, never keeps the original's data, has
    ///     no stack trace for a failure never thrown, and copies at most
    ///     <see cref="CopilotRedactedException.MaximumDepth"/> exceptions of a chain.
    /// </summary>
    [Fact]
    public void CopilotSignIn_RedactFailure_KeepsTheTypeNameMessageAndStackTrace()
    {
        // Arrange
        var original = new FileNotFoundException("The runtime was not found.", "copilot");
        original.Data["detail"] = "anything";
        var failure = Thrown(original);
        Exception deep = new InvalidOperationException("0");
        for (var level = 1; level < 20; level++)
        {
            deep = new InvalidOperationException($"{level}", deep);
        }

        // Act
        var copy = CopilotSignIn.RedactFailure(failure, plan: null);
        var neverThrown = CopilotSignIn.RedactFailure(new InvalidOperationException("never thrown"), plan: null);
        var capped = CopilotSignIn.RedactFailure(deep, plan: null);

        // Assert
        Assert.Equal(typeof(FileNotFoundException).FullName, copy.OriginalTypeName);
        Assert.Equal(failure.Message, copy.Message);
        Assert.NotNull(failure.StackTrace);
        Assert.Equal(failure.StackTrace, copy.StackTrace);
        Assert.Equal(failure.HResult, copy.HResult);
        Assert.Equal($"{typeof(FileNotFoundException).FullName}: {failure.Message}{Environment.NewLine}{failure.StackTrace}", copy.ToString());
        Assert.Empty(copy.Data);
        Assert.Null(copy.InnerException);
        Assert.Null(neverThrown.StackTrace);
        Assert.Equal($"{typeof(InvalidOperationException).FullName}: never thrown", neverThrown.ToString());
        var depth = 0;
        for (Exception? current = capped; current is not null; current = current.InnerException)
        {
            depth++;
        }

        Assert.Equal(CopilotRedactedException.MaximumDepth, depth);
    }

    /// <summary>
    ///     Proves a session error the SDK raises — quoting a known secret and a token-shaped value, and
    ///     carrying an RPC error whose message and data quote the secret — is relayed as a new
    ///     <see cref="InvalidOperationException"/> with a redacted message and a redacted copy as its
    ///     cause, and that nothing in the relayed chain carries a secret.
    /// </summary>
    [Fact]
    public void CopilotSignIn_RedactRelayed_SessionErrorQuotingATokenAndRpcData_IsRelayedRedacted()
    {
        // Arrange: the SDK's shape - a session error over the RPC error the runtime returned
        const string secret = "s3cr3t-relay-xyz";
        var plan = PlanKnowing(secret);
        var rpc = new RpcErrorException($"rpc {secret}", new { token = secret });
        rpc.Data["token"] = secret;
        var failure = Thrown(new InvalidOperationException($"Session error: refused {secret} {SentinelToken}", rpc));

        // Act
        var relayed = CopilotSignIn.RedactRelayed(failure, plan);

        // Assert
        var outer = Assert.IsType<InvalidOperationException>(relayed);
        Assert.NotSame(failure, outer);
        Assert.Equal("Session error: refused <redacted> gho_<redacted>", outer.Message);
        var cause = Assert.IsType<CopilotRedactedException>(outer.InnerException);
        Assert.Equal(typeof(InvalidOperationException).FullName, cause.OriginalTypeName);
        var rpcCopy = Assert.IsType<CopilotRedactedException>(cause.InnerException);
        Assert.Equal(typeof(RpcErrorException).FullName, rpcCopy.OriginalTypeName);
        Assert.Empty(rpcCopy.Data);
        SecretAssert.NoSecret(relayed, secret, SentinelToken);
    }

    /// <summary>
    ///     Proves a relayed failure keeps the kind a host catches: an I/O failure and a timeout stay
    ///     their own types, and an <see cref="InvalidOperationException"/> or a type outside the base
    ///     class library becomes an <see cref="InvalidOperationException"/>, each a new exception with
    ///     a redacted copy as its cause.
    /// </summary>
    /// <param name="kind">The failure the SDK raised.</param>
    /// <param name="expected">The type relayed.</param>
    [Theory]
    [InlineData("io", typeof(IOException))]
    [InlineData("timeout", typeof(TimeoutException))]
    [InlineData("invalid", typeof(InvalidOperationException))]
    [InlineData("json", typeof(InvalidOperationException))]
    public void CopilotSignIn_RedactRelayed_KeepsTheKindAHostCatches(string kind, Type expected)
    {
        // Arrange
        var message = $"failed for {SentinelToken}";
        Exception failure = kind switch
        {
            "io" => new EndOfStreamException(message),
            "timeout" => new TimeoutException(message),
            "invalid" => new InvalidOperationException(message),
            _ => new JsonException(message),
        };

        // Act
        var relayed = CopilotSignIn.RedactRelayed(failure, plan: null);

        // Assert
        Assert.Equal(expected, relayed.GetType());
        Assert.NotSame(failure, relayed);
        Assert.Equal("failed for gho_<redacted>", relayed.Message);
        var cause = Assert.IsType<CopilotRedactedException>(relayed.InnerException);
        Assert.Equal(failure.GetType().FullName, cause.OriginalTypeName);
        SecretAssert.NoSecret(relayed, SentinelToken);
    }

    /// <summary>
    ///     Proves a cancellation or a disposal of exactly its base type carrying nothing to redact passes
    ///     unchanged, keeping its instance, type and token, including a thrown cancellation whose stack
    ///     trace and source the relay also checks.
    /// </summary>
    [Fact]
    public void CopilotSignIn_RedactRelayed_CleanCancellationOrDisposal_PassesUnchanged()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var canceled = new OperationCanceledException("canceled", source.Token);
        var taskCanceled = new TaskCanceledException("task canceled");
        var disposed = new ObjectDisposedException("CopilotRuntime");
        var thrown = Thrown(new OperationCanceledException("canceled", source.Token));

        // Act and assert
        Assert.Same(canceled, CopilotSignIn.RedactRelayed(canceled, PlanKnowing("s3cr3t-unused")));
        Assert.Same(taskCanceled, CopilotSignIn.RedactRelayed(taskCanceled, plan: null));
        Assert.Same(disposed, CopilotSignIn.RedactRelayed(disposed, plan: null));
        Assert.NotNull(thrown.StackTrace);
        Assert.NotNull(thrown.Source);
        Assert.Same(thrown, CopilotSignIn.RedactRelayed(thrown, PlanKnowing("s3cr3t-unused")));
    }

    /// <summary>
    ///     Proves a cancellation or a disposal whose message is clean but which carries a secret
    ///     elsewhere a host can read — a data key or value, its source or its help link — is rebuilt as
    ///     exactly the same type, keeping a cancellation's token and a disposal's object name, with no
    ///     data and nothing in its chain carrying the secret.
    /// </summary>
    /// <param name="kind">The failure the SDK raised.</param>
    /// <param name="where">Where the failure carries the secret.</param>
    [Theory]
    [InlineData("oce", "data-key")]
    [InlineData("oce", "data-value")]
    [InlineData("oce", "source")]
    [InlineData("task", "data-value")]
    [InlineData("task", "help-link")]
    [InlineData("disposed", "data-key")]
    [InlineData("disposed", "source")]
    [InlineData("disposed", "help-link")]
    public void CopilotSignIn_RedactRelayed_CancellationOrDisposalCarryingASecretOutsideItsMessage_IsRebuiltRedacted(
        string kind,
        string where)
    {
        // Arrange: a clean message, with the secret held only where the message check cannot see it
        const string secret = "s3cr3t-outside-xyz";
        var plan = PlanKnowing(secret);
        using var source = new CancellationTokenSource();
        Exception failure = kind switch
        {
            "oce" => new OperationCanceledException("canceled", source.Token),
            "task" => new TaskCanceledException("task canceled", null, source.Token),
            _ => new ObjectDisposedException("CopilotClient"),
        };
        switch (where)
        {
            case "data-key":
                failure.Data[secret] = "detail";
                break;
            case "data-value":
                failure.Data["detail"] = secret;
                break;
            case "source":
                failure.Source = secret;
                break;
            default:
                failure.HelpLink = $"https://example.invalid/{secret}";
                break;
        }

        // Act
        var relayed = CopilotSignIn.RedactRelayed(failure, plan);

        // Assert: the original exposes the secret, the relayed exception is a clean rebuild of the same type
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SecretAssert.NoSecret(failure, secret));
        Assert.Equal(failure.GetType(), relayed.GetType());
        Assert.NotSame(failure, relayed);
        if (relayed is OperationCanceledException canceled)
        {
            Assert.Equal(source.Token, canceled.CancellationToken);
            Assert.IsType<CopilotRedactedException>(relayed.InnerException);
        }
        else
        {
            var disposal = Assert.IsType<ObjectDisposedException>(relayed);
            Assert.Equal("CopilotClient", disposal.ObjectName);
            Assert.Null(disposal.InnerException);
        }

        for (Exception? current = relayed; current is not null; current = current.InnerException)
        {
            Assert.Empty(current.Data);
        }

        SecretAssert.NoSecret(relayed, secret);
    }

    /// <summary>
    ///     Proves a disposal with no inner exception whose object name quotes a secret is rebuilt as an
    ///     <see cref="ObjectDisposedException"/> keeping its object name redacted and naming it once, and
    ///     that a disposal with an inner exception is rebuilt with the redacted copy as its cause; neither
    ///     carries the secret.
    /// </summary>
    [Fact]
    public void CopilotSignIn_RedactRelayed_DisposalNamingASecret_IsRebuiltWithTheNameRedacted()
    {
        // Arrange
        const string secret = "s3cr3t-disposed-xyz";
        var plan = PlanKnowing(secret);
        var named = new ObjectDisposedException($"client-{secret}");
        var caused = new ObjectDisposedException($"Cannot access client-{secret}.", new IOException($"pipe {secret}"));

        // Act
        var relayedNamed = CopilotSignIn.RedactRelayed(named, plan);
        var relayedCaused = CopilotSignIn.RedactRelayed(caused, plan);

        // Assert: the name is kept redacted, with the message naming it once
        var disposal = Assert.IsType<ObjectDisposedException>(relayedNamed);
        Assert.NotSame(named, disposal);
        Assert.Equal("client-<redacted>", disposal.ObjectName);
        Assert.Equal(new ObjectDisposedException("client-<redacted>").Message, disposal.Message);
        Assert.Null(disposal.InnerException);
        SecretAssert.NoSecret(disposal, secret);

        // Assert: a disposal with a cause keeps the redacted copy as its cause
        var withCause = Assert.IsType<ObjectDisposedException>(relayedCaused);
        Assert.NotSame(caused, withCause);
        Assert.Equal("Cannot access client-<redacted>.", withCause.Message);
        var cause = Assert.IsType<CopilotRedactedException>(withCause.InnerException);
        Assert.Equal(typeof(ObjectDisposedException).FullName, cause.OriginalTypeName);
        SecretAssert.NoSecret(withCause, secret);
    }

    /// <summary>
    ///     Proves a cancellation AgentKit cannot fully inspect is rebuilt even when its message is clean:
    ///     a subclass holding a secret in a property of its own becomes exactly an
    ///     <see cref="OperationCanceledException"/>, and a task cancellation whose task state is a secret
    ///     stays a <see cref="TaskCanceledException"/> without the task; neither carries the secret.
    /// </summary>
    [Fact]
    public void CopilotSignIn_RedactRelayed_CancellationItCannotInspect_IsRebuilt()
    {
        // Arrange
        const string secret = "s3cr3t-hidden-xyz";
        var plan = PlanKnowing(secret);
        using var source = new CancellationTokenSource();
        var subclass = new SecretHoldingCancellationException("canceled", secret, source.Token);
        var completion = new TaskCompletionSource(secret);
        completion.SetCanceled(TestContext.Current.CancellationToken);
        var taskCanceled = new TaskCanceledException(completion.Task);

        // Act
        var relayedSubclass = CopilotSignIn.RedactRelayed(subclass, plan);
        var relayedTask = CopilotSignIn.RedactRelayed(taskCanceled, plan);

        // Assert: the subclass becomes its base kind, keeping its token
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SecretAssert.NoSecret(subclass, secret));
        Assert.Equal(typeof(OperationCanceledException), relayedSubclass.GetType());
        Assert.Equal(source.Token, ((OperationCanceledException)relayedSubclass).CancellationToken);
        Assert.IsType<CopilotRedactedException>(relayedSubclass.InnerException);
        SecretAssert.NoSecret(relayedSubclass, secret);

        // Assert: the task cancellation stays one, without the task holding the state
        var rebuilt = Assert.IsType<TaskCanceledException>(relayedTask);
        Assert.NotSame(taskCanceled, rebuilt);
        Assert.Null(rebuilt.Task);
        Assert.IsType<CopilotRedactedException>(rebuilt.InnerException);
        SecretAssert.NoSecret(rebuilt, secret);
    }

    /// <summary>
    ///     Proves a cancellation or a disposal carrying a secret — in its message or its inner
    ///     exception — is rebuilt as the same kind, keeping the cancellation's token, with nothing in
    ///     its chain carrying the secret.
    /// </summary>
    [Fact]
    public void CopilotSignIn_RedactRelayed_CancellationOrDisposalCarryingASecret_IsRebuiltRedacted()
    {
        // Arrange
        const string secret = "s3cr3t-cancel-xyz";
        var plan = PlanKnowing(secret);
        using var source = new CancellationTokenSource();
        var canceled = new OperationCanceledException("canceled", new IOException($"pipe {secret}"), source.Token);
        var disposed = new ObjectDisposedException($"closed for {secret}", new IOException($"pipe {SentinelToken}"));

        // Act
        var relayedCancel = CopilotSignIn.RedactRelayed(canceled, plan);
        var relayedDisposal = CopilotSignIn.RedactRelayed(disposed, plan);

        // Assert
        var cancel = Assert.IsType<OperationCanceledException>(relayedCancel);
        Assert.NotSame(canceled, cancel);
        Assert.Equal(source.Token, cancel.CancellationToken);
        Assert.IsType<CopilotRedactedException>(cancel.InnerException);
        SecretAssert.NoSecret(cancel, secret);
        var disposal = Assert.IsType<ObjectDisposedException>(relayedDisposal);
        Assert.NotSame(disposed, disposal);
        Assert.Contains("closed for <redacted>", disposal.Message, StringComparison.Ordinal);
        SecretAssert.NoSecret(disposal, secret, SentinelToken);
    }

    /// <summary>
    ///     Proves a task cancellation carrying a secret in its inner exception is rebuilt as a
    ///     <see cref="TaskCanceledException"/>, a new instance keeping its token, with a redacted copy
    ///     as its cause and nothing in its chain carrying the secret.
    /// </summary>
    [Fact]
    public void CopilotSignIn_RedactRelayed_TaskCancellationCarryingASecret_StaysATaskCancellation()
    {
        // Arrange
        const string secret = "s3cr3t-task-cancel-xyz";
        var plan = PlanKnowing(secret);
        using var source = new CancellationTokenSource();
        var canceled = new TaskCanceledException("canceled", new IOException($"pipe {secret}"), source.Token);

        // Act
        var relayed = CopilotSignIn.RedactRelayed(canceled, plan);

        // Assert
        var rebuilt = Assert.IsType<TaskCanceledException>(relayed);
        Assert.NotSame(canceled, rebuilt);
        Assert.Equal(source.Token, rebuilt.CancellationToken);
        Assert.Equal("canceled", rebuilt.Message);
        var cause = Assert.IsType<CopilotRedactedException>(rebuilt.InnerException);
        Assert.Equal(typeof(TaskCanceledException).FullName, cause.OriginalTypeName);
        SecretAssert.NoSecret(rebuilt, secret);
    }

    /// <summary>
    ///     Proves secrets an original failure holds only in its <see cref="Exception.Data"/> — in a key
    ///     or a value, at any level of its chain — are never copied: the shared chain walk detects such
    ///     a secret, and neither the redacted copy nor the relayed exception carries any data.
    /// </summary>
    [Fact]
    public void CopilotSignIn_RedactFailure_DataQuotingASecret_IsNotCopied()
    {
        // Arrange: messages that are clean, with the secrets held only in data
        const string secret = "s3cr3t-data-xyz";
        var plan = PlanKnowing(secret);
        var inner = new IOException("pipe broke");
        inner.Data["detail"] = $"pipe {secret}";
        var original = new InvalidOperationException("refused", inner);
        original.Data[secret] = secret;
        original.Data["token"] = SentinelToken;
        var failure = Thrown(original);

        // Act
        var copy = CopilotSignIn.RedactFailure(failure, plan);
        var relayed = CopilotSignIn.RedactRelayed(failure, plan);

        // Assert: the walk sees data, and neither result keeps any
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SecretAssert.NoSecret(failure, secret));
        foreach (var result in new Exception[] { copy, relayed })
        {
            for (Exception? current = result; current is not null; current = current.InnerException)
            {
                Assert.Empty(current.Data);
            }

            SecretAssert.NoSecret(result, secret, SentinelToken);
        }
    }

    /// <summary>
    ///     Proves a failure AgentKit already built around a redacted copy is returned unchanged, so
    ///     relaying twice changes nothing.
    /// </summary>
    [Fact]
    public void CopilotSignIn_RedactRelayed_AlreadyRelayed_IsReturnedUnchanged()
    {
        // Arrange
        const string secret = "s3cr3t-twice-xyz";
        var plan = PlanKnowing(secret);
        var explained = new InvalidOperationException("explained", CopilotSignIn.RedactFailure(new IOException(secret), plan));
        var once = CopilotSignIn.RedactRelayed(new IOException($"lost {secret}"), plan);

        // Act
        var relayedExplained = CopilotSignIn.RedactRelayed(explained, plan);
        var twice = CopilotSignIn.RedactRelayed(once, plan);

        // Assert
        Assert.Same(explained, relayedExplained);
        Assert.Same(once, twice);
        SecretAssert.NoSecret(twice, secret);
    }

    /// <summary>
    ///     Proves an operation run through the relay has its failure relayed redacted whether it throws
    ///     synchronously or through its task, passes a clean cancellation unchanged, and returns its
    ///     result when it succeeds.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_RelayAsync_FailureThrownEitherWay_IsRelayedRedacted()
    {
        // Arrange
        const string secret = "s3cr3t-relay-async-xyz";
        var plan = PlanKnowing(secret);
        var canceled = new OperationCanceledException("canceled");

        // Act
        var synchronous = await Assert.ThrowsAsync<IOException>(() => CopilotSignIn.RelayAsync<int>(
            () => throw new IOException($"lost {secret}"),
            plan));
        var asynchronous = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotSignIn.RelayAsync(
            () => Task.FromException<int>(new JsonException($"bad {secret}")),
            plan));
        var cancellation = await Assert.ThrowsAsync<OperationCanceledException>(() => CopilotSignIn.RelayAsync(
            () => Task.FromException<int>(canceled),
            plan));
        var result = await CopilotSignIn.RelayAsync(() => Task.FromResult(42), plan);

        // Assert
        Assert.Equal("lost <redacted>", synchronous.Message);
        SecretAssert.NoSecret(synchronous, secret);
        Assert.Equal("bad <redacted>", asynchronous.Message);
        SecretAssert.NoSecret(asynchronous, secret);
        Assert.Same(canceled, cancellation);
        Assert.Equal(42, result);
    }

    /// <summary>
    ///     Proves the runtime's sign-in is logged with its source, host and login, that a runtime on
    ///     another account than the Copilot CLI's is warned about, and that no token is logged.
    /// </summary>
    [Fact]
    public void CopilotSignIn_Report_LogsTheSignInAndWarnsOnAnotherAccount()
    {
        // Arrange
        var logger = new CapturingLogger();
        var plan = new CopilotSignInPlan { Token = SentinelToken, CliAccount = new CopilotCliAccount(AccountHost, AccountLogin) };
        var viaGh = new GetAuthStatusResponse { IsAuthenticated = true, AuthType = "gh-cli", Host = "https://github.com", Login = "web_user" };

        // Act
        CopilotSignIn.Report(logger, plan, CopilotSignInSource.GitHubCli, viaGh);

        // Assert
        var entries = logger.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal(LogLevel.Information, entries[0].Level);
        Assert.Contains("GitHubCli as https://github.com / web_user", entries[0].Message, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Warning, entries[1].Level);
        Assert.Contains(AccountHost, entries[1].Message, StringComparison.Ordinal);
        SecretAssert.NoSecret(logger, SentinelToken);
    }

    /// <summary>
    ///     Writes the user's Copilot CLI configuration into the fake profile.
    /// </summary>
    /// <param name="content">The configuration's content.</param>
    private void WriteUserConfiguration(string content)
    {
        var home = Path.Combine(Profile, ".copilot");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "config.json"), content);
    }

    /// <summary>
    ///     Resolves the sign-in on the fake machine.
    /// </summary>
    /// <param name="options">The host's options.</param>
    /// <param name="hostEnvironment">The host's environment.</param>
    /// <param name="search">The platform and <c>PATH</c>; a Linux machine with an empty <c>PATH</c> when <see langword="null"/>.</param>
    /// <param name="helperTimeout">The helper's time limit; one minute when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the resolution; the test's token when <see langword="null"/>.</param>
    /// <returns>The plan.</returns>
    private Task<CopilotSignInPlan> ResolveAsync(
        CopilotRuntimeOptions options,
        IDictionary hostEnvironment,
        CopilotCliSearchContext? search = null,
        TimeSpan? helperTimeout = null,
        CancellationToken? cancellationToken = null)
    {
        var context = new CopilotSignInContext(
            search ?? new FakeCopilotCliMachine().Context(CopilotCliPlatform.Linux),
            Profile,
            () =>
            {
                var home = Path.Combine(_root, $"helper-{_helperHomes.Count}");
                Directory.CreateDirectory(home);
                _helperHomes.Add(home);
                return home;
            },
            Helper.Create,
            CopilotSignIn.DeleteHelperHome,
            helperTimeout ?? TimeSpan.FromMinutes(1));
        return CopilotSignIn.ResolveAsync(
            options,
            DataDirectory,
            hostEnvironment,
            Launch,
            context,
            cancellationToken ?? TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Builds a sign-in that knows one secret, as a runtime's does once it has resolved its tokens.
    ///     Shared by every test that proves a failure is redacted of a known secret.
    /// </summary>
    /// <param name="secret">The secret.</param>
    /// <returns>The sign-in.</returns>
    internal static CopilotSignInPlan PlanKnowing(string secret)
    {
        var secrets = new CopilotSignInSecrets();
        secrets.Add(secret);
        return new CopilotSignInPlan { Secrets = secrets };
    }

    /// <summary>
    ///     Throws an exception and returns it as caught, so it carries a real stack trace.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The exception, thrown.</returns>
    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    /// <summary>
    ///     A cancellation holding a value in a property of its own, as an SDK's subclass could.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="detail">The value it holds.</param>
    /// <param name="token">The cancellation token.</param>
    public sealed class SecretHoldingCancellationException(string message, string detail, CancellationToken token)
        : OperationCanceledException(message, token)
    {
        /// <summary>
        ///     Gets the value it holds.
        /// </summary>
        public string Detail { get; } = detail;
    }

    /// <summary>
    ///     An exception whose stack trace text is supplied, as a runtime's error can quote anything.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="stackTrace">The stack trace text.</param>
    public sealed class StackQuotingException(string message, string stackTrace) : Exception(message)
    {
        /// <inheritdoc/>
        public override string StackTrace => stackTrace;
    }

    /// <summary>
    ///     An exception shaped like an RPC error of the SDK's transport: a type outside the base class
    ///     library, carrying the error's data in a property of its own.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="errorData">The error's data, which can quote anything.</param>
    public sealed class RpcErrorException(string message, object errorData) : Exception(message)
    {
        /// <summary>
        ///     Gets the error's data.
        /// </summary>
        public object ErrorData => errorData;
    }

    /// <summary>
    ///     A fake helper runtime: scripted status and account list, recording what it saw.
    /// </summary>
    private sealed class FakeHelper : ICopilotSignInHelper
    {
        /// <summary>
        ///     Gets how many helpers were created.
        /// </summary>
        public int Created { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the last helper was disposed.
        /// </summary>
        public bool Disposed { get; private set; }

        /// <summary>
        ///     Gets how many times the account list was read.
        /// </summary>
        public int UserReads { get; private set; }

        /// <summary>
        ///     Gets the helper home's <c>config.json</c> as the helper found it at start.
        /// </summary>
        public string? SeenConfiguration { get; private set; }

        /// <summary>
        ///     Gets the helper home's entries as the helper found them at start.
        /// </summary>
        public List<string> SeenEntries { get; private set; } = [];

        /// <summary>
        ///     Gets the number of entries in the GitHub CLI configuration folder at start.
        /// </summary>
        public int SeenGitHubCliEntries { get; private set; } = -1;

        /// <summary>
        ///     Gets or sets the status the helper reports.
        /// </summary>
        public GetAuthStatusResponse Status { get; set; } =
            new() { IsAuthenticated = true, AuthType = "user", Host = AccountHost, Login = AccountLogin };

        /// <summary>
        ///     Gets or sets the accounts the helper reports.
        /// </summary>
        public List<AccountAllUsers> Users { get; set; } =
            [new AccountAllUsers { AuthInfo = new AuthInfoUser { Host = AccountHost, Login = AccountLogin }, Token = SentinelToken }];

        /// <summary>
        ///     Gets or sets the failure creating the helper raises.
        /// </summary>
        public Exception? CreateFailure { get; set; }

        /// <summary>
        ///     Gets or sets the failure the start raises.
        /// </summary>
        public Exception? StartFailure { get; set; }

        /// <summary>
        ///     Gets or sets the failure the account list raises.
        /// </summary>
        public Exception? UsersFailure { get; set; }

        /// <summary>
        ///     Gets or sets a value indicating whether the start never completes until canceled.
        /// </summary>
        public bool Hang { get; set; }

        /// <summary>
        ///     Gets or sets an action run when the start begins.
        /// </summary>
        public Action? OnStart { get; set; }

        /// <summary>
        ///     The helper home of the last helper created.
        /// </summary>
        private string? _home;

        /// <summary>
        ///     Creates the helper for a set of client options.
        /// </summary>
        /// <param name="options">The client options.</param>
        /// <returns>This helper.</returns>
        public ICopilotSignInHelper Create(CopilotClientOptions options)
        {
            Created++;
            Disposed = false;
            if (CreateFailure is not null)
            {
                throw CreateFailure;
            }

            _home = Assert.IsType<SpawnedStdioRuntimeConnection>(options.Runtime).BaseDirectory;
            return this;
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            SeenConfiguration = await File.ReadAllTextAsync(Path.Combine(_home!, "config.json"), Encoding.UTF8, cancellationToken);
            SeenEntries = [.. Directory.EnumerateFileSystemEntries(_home!).Select(entry => Path.GetFileName(entry)).Order(StringComparer.Ordinal)];
            SeenGitHubCliEntries = Directory.EnumerateFileSystemEntries(Path.Combine(_home!, "gh")).Count();
            OnStart?.Invoke();
            if (StartFailure is not null)
            {
                throw StartFailure;
            }

            if (Hang)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }

        /// <inheritdoc/>
        public Task<GetAuthStatusResponse> GetAuthStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Status);

        /// <inheritdoc/>
        public Task<IList<AccountAllUsers>> GetAllUsersAsync(CancellationToken cancellationToken)
        {
            UserReads++;
            return UsersFailure is null
                ? Task.FromResult<IList<AccountAllUsers>>(Users)
                : Task.FromException<IList<AccountAllUsers>>(UsersFailure);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
///     The collection of tests that set a process environment variable: they run alone, never in
///     parallel with another test.
/// </summary>
[CollectionDefinition("ProcessEnvironment", DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
}

/// <summary>
///     Unit tests for the redaction <see cref="CopilotSignIn"/> applies when no sign-in and no
///     environment is in hand, which reads the process environment.
/// </summary>
/// <remarks>
///     The one documented exception to the rule that no unit test reads or changes the process
///     environment: the test sets <c>GITHUB_ENTERPRISE_TOKEN</c> to a value of its own, which no other
///     unit test's logic reads, restores it afterwards, and runs alone.
/// </remarks>
[Collection("ProcessEnvironment")]
public sealed class CopilotSignInProcessEnvironmentTests
{
    /// <summary>
    ///     Proves text redacted without a sign-in — a quoted runtime message, a cause's copy, a relayed
    ///     failure, a start failure's explanation and a provider's refusal — has the process
    ///     environment's secret values removed, not only the text shaped like a GitHub token.
    /// </summary>
    [Fact]
    public async Task CopilotSignIn_Redact_WithoutAPlan_RemovesTheProcessEnvironmentsSecrets()
    {
        // Arrange
        var secret = $"s3cr3t-ambient-{Guid.NewGuid():N}";
        const string shaped = "gho_AMBIENT0123456789abcdefABCDEF012345";
        var previous = Environment.GetEnvironmentVariable("GITHUB_ENTERPRISE_TOKEN");
        Environment.SetEnvironmentVariable("GITHUB_ENTERPRISE_TOKEN", secret);
        try
        {
            // Act
            var text = CopilotSignIn.Redact(plan: null, $"runtime said {secret} {shaped}");
            var copy = CopilotSignIn.RedactFailure(new IOException($"lost {secret}"), plan: null);
            var relayed = CopilotSignIn.RedactRelayed(new IOException($"lost {secret}"), plan: null);
            var explained = CopilotRuntime.ExplainStartFailure(new IOException($"exited {secret}"), launch: null, signIn: null);
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotSignIn.AskProviderAsync(
                (_, _) => Task.FromResult<CopilotGitHubToken>(null!),
                $"https://{secret}.ghe.com",
                TestContext.Current.CancellationToken));

            // Assert
            Assert.Equal("runtime said <redacted> gho_<redacted>", text);
            Assert.Equal("lost <redacted>", copy.Message);
            Assert.Equal("lost <redacted>", relayed.Message);
            Assert.Contains("could not start: exited <redacted>", explained, StringComparison.Ordinal);
            SecretAssert.NoSecret(copy, secret);
            SecretAssert.NoSecret(relayed, secret);
            SecretAssert.NoSecret(refusal, secret);
            SecretAssert.NoSecretInText(explained, secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_ENTERPRISE_TOKEN", previous);
        }
    }
}
