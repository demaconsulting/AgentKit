using System.Collections;
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
    ///     Proves the plan's string form never carries the token.
    /// </summary>
    [Fact]
    public void CopilotSignIn_PlanToString_RedactsTheToken()
    {
        // Arrange
        var plan = new CopilotSignInPlan { Source = CopilotSignInSource.CopilotCli, Token = SentinelToken, HostName = "example.ghe.com" };

        // Act
        var text = plan.ToString();

        // Assert
        Assert.DoesNotContain(SentinelToken, text, StringComparison.Ordinal);
        Assert.Contains("<redacted>", text, StringComparison.Ordinal);
        Assert.Contains("example.ghe.com", text, StringComparison.Ordinal);
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
        AssertNoSecretInChain(copy, secret, token, SentinelToken);
        AssertNoSecretInChain(new InvalidOperationException("outer", copy), secret, token, SentinelToken);
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
    ///     Proves the runtime's sign-in is logged with its source, host and login, that a runtime on
    ///     another account than the Copilot CLI's is warned about, and that no token is logged.
    /// </summary>
    [Fact]
    public void CopilotSignIn_Report_LogsTheSignInAndWarnsOnAnotherAccount()
    {
        // Arrange
        var logger = new ListLogger();
        var plan = new CopilotSignInPlan { Token = SentinelToken, CliAccount = new CopilotCliAccount(AccountHost, AccountLogin) };
        var viaGh = new GetAuthStatusResponse { IsAuthenticated = true, AuthType = "gh-cli", Host = "https://github.com", Login = "web_user" };

        // Act
        CopilotSignIn.Report(logger, plan, CopilotSignInSource.GitHubCli, viaGh);

        // Assert
        Assert.Equal(2, logger.Entries.Count);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Contains("GitHubCli as https://github.com / web_user", logger.Entries[0].Message, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Warning, logger.Entries[1].Level);
        Assert.Contains(AccountHost, logger.Entries[1].Message, StringComparison.Ordinal);
        Assert.All(logger.Entries, entry => Assert.DoesNotContain(SentinelToken, entry.Message, StringComparison.Ordinal));
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
    ///     Asserts that no exception in a failure's chain — every inner exception, and every inner
    ///     exception of an aggregate — carries a secret in its message, its stack trace or its string
    ///     form.
    /// </summary>
    /// <param name="failure">The failure.</param>
    /// <param name="secrets">The secrets.</param>
    private static void AssertNoSecretInChain(Exception failure, params string[] secrets)
    {
        var pending = new Stack<Exception>([failure]);
        while (pending.TryPop(out var current))
        {
            foreach (var secret in secrets)
            {
                Assert.False(current.Message.Contains(secret, StringComparison.Ordinal), $"A {current.GetType().Name} message carries a secret.");
                Assert.False((current.StackTrace ?? string.Empty).Contains(secret, StringComparison.Ordinal), $"A {current.GetType().Name} stack trace carries a secret.");
                Assert.False(current.ToString().Contains(secret, StringComparison.Ordinal), $"A {current.GetType().Name} string form carries a secret.");
            }

            if (current is AggregateException aggregate)
            {
                foreach (var each in aggregate.InnerExceptions)
                {
                    pending.Push(each);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }
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

    /// <summary>
    ///     A logger that keeps every entry.
    /// </summary>
    private sealed class ListLogger : ILogger
    {
        /// <summary>
        ///     Gets the entries, in order.
        /// </summary>
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
