using Microsoft.Copilot;
using Microsoft.Copilot.Rpc;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for the release policy and the opening protocol of
///     <see cref="CopilotSessionChannel"/>: a retained channel detaches without deleting, a deleting
///     channel detaches and then deletes, and neither step's failure escapes; every discovered MCP
///     server is disabled before a session is created, the session's installed plugins are cleared,
///     any server still enabled is disabled and verified, and a session that cannot be sealed is
///     refused and abandoned; and no production opener starts a runtime the host has disposed.
/// </summary>
/// <remarks>
///     The production channel wraps the SDK's session type, which cannot be constructed in a test,
///     so the policies are exercised through <see cref="CopilotSessionChannel.ReleaseAsync"/>,
///     <see cref="CopilotSessionChannel.OpenConfinedAsync{TSession}"/> and its sealing policy,
///     <c>CopilotSessionChannel.SealAsync</c>, with the runtime's steps supplied as delegates.
/// </remarks>
public class CopilotTurnChannelTests
{
    /// <summary>
    ///     Proves a channel opened for an agent releases its session handle and asks for no deletion,
    ///     so the conversation stays resumable.
    /// </summary>
    [Fact]
    public async Task CopilotSessionChannel_Release_RetainedChannel_DetachesWithoutDeleting()
    {
        // Arrange
        var steps = new List<string>();

        // Act: a retained channel has no deletion step
        await CopilotSessionChannel.ReleaseAsync(
            () =>
            {
                steps.Add("detach");
                return ValueTask.CompletedTask;
            },
            delete: null);

        // Assert
        Assert.Equal(["detach"], steps);
    }

    /// <summary>
    ///     Proves a channel opened for the engine or the summarizer releases its session handle and
    ///     then deletes the session, in that order.
    /// </summary>
    [Fact]
    public async Task CopilotSessionChannel_Release_DeletingChannel_DetachesThenDeletes()
    {
        // Arrange
        var steps = new List<string>();

        // Act
        await CopilotSessionChannel.ReleaseAsync(
            () =>
            {
                steps.Add("detach");
                return ValueTask.CompletedTask;
            },
            () =>
            {
                steps.Add("delete");
                return Task.CompletedTask;
            });

        // Assert
        Assert.Equal(["detach", "delete"], steps);
    }

    /// <summary>
    ///     Proves release never throws: a detach that fails does not stop the deletion, and a deletion
    ///     that fails is swallowed, because release runs where no failure can be acted on.
    /// </summary>
    [Fact]
    public async Task CopilotSessionChannel_Release_FailingSteps_DoNotThrow()
    {
        // Arrange
        var deleted = false;

        // Act: both steps fail, the second only after recording that it ran
        await CopilotSessionChannel.ReleaseAsync(
            () => ValueTask.FromException(new IOException("transport gone")),
            () =>
            {
                deleted = true;
                return Task.FromException(new IOException("runtime gone"));
            });

        // Assert: reaching here is the assertion that nothing escaped; the deletion was attempted
        Assert.True(deleted);
    }

    /// <summary>
    ///     Proves every MCP server the runtime discovers — a non-GitHub one included — is named in the
    ///     session's disabled servers <em>before</em> the session is created, so none is ever started,
    ///     and that the factory's own entry for the built-in GitHub server is kept exactly once.
    /// </summary>
    [Fact]
    public async Task CopilotSessionChannel_Open_AmbientMcpServers_AreDisabledBeforeTheSessionIsCreated()
    {
        // Arrange: a confined configuration, and a runtime that discovers three ambient servers
        var config = CopilotAgentFactory.BuildEngineSessionConfig([], instructions: null, model: null);
        var steps = new List<string>();
        List<string>? disabledAtOpen = null;

        // Act
        var session = await CopilotSessionChannel.OpenConfinedAsync(
            config,
            _ =>
            {
                steps.Add("discover");
                return Task.FromResult<IEnumerable<string>>(["docs-search", "github-mcp-server", "plugin-tools"]);
            },
            _ =>
            {
                steps.Add("open");
                disabledAtOpen = [.. config.Mcp.DisabledServers!];
                return Task.FromResult("session-1");
            },
            (_, _) =>
            {
                steps.Add("seal");
                return Task.CompletedTask;
            },
            _ =>
            {
                steps.Add("abandon");
                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        // Assert: discovered before opening, every server named when the session was created
        Assert.Equal("session-1", session);
        Assert.Equal(["discover", "open", "seal"], steps);
        Assert.NotNull(disabledAtOpen);
        Assert.Equal(1, disabledAtOpen.Count(name => name == CopilotAgentFactory.BuiltInGitHubMcpServer));
        Assert.Contains("docs-search", disabledAtOpen);
        Assert.Contains("plugin-tools", disabledAtOpen);
        Assert.Equal(3, disabledAtOpen.Count);
    }

    /// <summary>
    ///     Proves a discovery that fails opens nothing: the failure propagates and no session is
    ///     created, rather than one being created with servers left unnamed.
    /// </summary>
    [Fact]
    public async Task CopilotSessionChannel_Open_DiscoveryFails_OpensNothing()
    {
        // Arrange
        var config = CopilotAgentFactory.BuildEngineSessionConfig([], instructions: null, model: null);
        var opened = false;

        // Act
        var failure = await Assert.ThrowsAsync<IOException>(() => CopilotSessionChannel.OpenConfinedAsync(
            config,
            _ => Task.FromException<IEnumerable<string>>(new IOException("runtime gone")),
            _ =>
            {
                opened = true;
                return Task.FromResult("session-1");
            },
            (_, _) => Task.CompletedTask,
            _ => ValueTask.CompletedTask,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("runtime gone", failure.Message);
        Assert.False(opened);
    }

    /// <summary>
    ///     Proves a session that cannot be sealed is abandoned — released by the supplied policy — and
    ///     the sealing failure propagates, so no unsealed session ever reaches a caller.
    /// </summary>
    [Fact]
    public async Task CopilotSessionChannel_Open_SealFails_AbandonsTheSessionAndRethrows()
    {
        // Arrange
        var config = CopilotAgentFactory.BuildEngineSessionConfig([], instructions: null, model: null);
        var abandoned = new List<string>();
        var cause = new InvalidOperationException("still enabled");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotSessionChannel.OpenConfinedAsync(
            config,
            _ => Task.FromResult<IEnumerable<string>>([]),
            _ => Task.FromResult("session-1"),
            (_, _) => Task.FromException(cause),
            session =>
            {
                abandoned.Add(session);
                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(cause, failure);
        Assert.Equal(["session-1"], abandoned);
    }

    /// <summary>
    ///     Proves sealing clears the session's installed plugins before it inspects the session's MCP
    ///     configuration, so a server a plugin contributed is gone before the servers are listed.
    /// </summary>
    [Fact]
    public async Task CopilotSessionChannel_Seal_ClearsInstalledPluginsFirst()
    {
        // Arrange
        var steps = new List<string>();

        // Act
        await CopilotSessionChannel.SealAsync(
            _ =>
            {
                steps.Add("clear-plugins");
                return Task.CompletedTask;
            },
            _ =>
            {
                steps.Add("list");
                return Task.FromResult<IList<McpConfiguredServer>>([]);
            },
            (name, _) =>
            {
                steps.Add($"disable:{name}");
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["clear-plugins", "list", "list"], steps);
    }

    /// <summary>
    ///     Proves a server the runtime still reports enabled after creation — one that discovery is
    ///     not documented to report, such as a server from a managed catalog or one the signed-in
    ///     account or its organization provides — is disabled for the
    ///     session and then verified, and the session is accepted once none remains enabled.
    /// </summary>
    [Fact]
    public async Task CopilotSessionChannel_Seal_ServerStillEnabledAfterCreation_IsDisabledThenVerified()
    {
        // Arrange: enabled on the first listing, disabled on the second
        var listings = new Queue<IList<McpConfiguredServer>>(
        [
            [Server("docs-search", enabled: true), Server("github-mcp-server", enabled: false)],
            [Server("docs-search", enabled: false), Server("github-mcp-server", enabled: false)],
        ]);
        var disabled = new List<string>();

        // Act: reaching the end without an exception is the acceptance
        await CopilotSessionChannel.SealAsync(
            _ => Task.CompletedTask,
            _ => Task.FromResult(listings.Dequeue()),
            (name, _) =>
            {
                disabled.Add(name);
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        // Assert: only the enabled server was disabled, and the configuration was listed again
        Assert.Equal(["docs-search"], disabled);
        Assert.Empty(listings);
    }

    /// <summary>
    ///     Proves a server that stays enabled after it was disabled refuses the session with an error
    ///     that names it, and points the host to the sources a session cannot disable before
    ///     creation, so the runtime's failure to disable a server never reaches the model.
    /// </summary>
    [Fact]
    public async Task CopilotSessionChannel_Seal_ServerThatStaysEnabled_RefusesNamingIt()
    {
        // Arrange: the runtime keeps reporting the server enabled
        IList<McpConfiguredServer> listing = [Server("docs-search", enabled: true)];

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotSessionChannel.SealAsync(
            _ => Task.CompletedTask,
            _ => Task.FromResult(listing),
            (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("'docs-search'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("signed-in account or its organization", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the plugins patch replaces the session's installed plugins with an empty list rather
    ///     than leaving the list unset, which the runtime would read as "no change".
    /// </summary>
    [Fact]
    public void CopilotSessionChannel_NoInstalledPluginsPatch_ReplacesTheListWithAnEmptyOne()
    {
        // Act
        var patch = CopilotSessionChannel.CreateNoInstalledPluginsPatch();

        // Assert
        Assert.NotNull(patch.Installed);
        Assert.Empty(patch.Installed);
    }

    /// <summary>
    ///     Proves no production opener starts a runtime the host has disposed. The SDK restarts a
    ///     disposed client on demand, so each opener holds the runtime and reads its client at every
    ///     step: an opener made before the disposal, and each confining open, fails with the
    ///     runtime's disposal rather than as a start failure.
    /// </summary>
    [Fact]
    public async Task CopilotSessionChannel_OpenersOnADisposedRuntime_FailWithoutStartingTheRuntime()
    {
        // Arrange: openers on a runtime whose client can never start, so an attempt to start it
        // would fail as a start failure rather than as the runtime's disposal
        var runtime = RuntimeThatCannotStart();
        var open = CopilotSessionChannel.Open(runtime);
        var openRetained = CopilotSessionChannel.OpenRetained(runtime);
        var resume = CopilotSessionChannel.Resume(runtime);
        await runtime.DisposeAsync();
        var token = TestContext.Current.CancellationToken;

        // Act
        var refusals = new[]
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => open(new CreateSessionOptions(), token)),
            await Assert.ThrowsAsync<ObjectDisposedException>(() => openRetained(new CreateSessionOptions(), token)),
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => resume("session-1", new ResumeSessionOptions(), token)),
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => CopilotSessionChannel.CreateConfinedSessionAsync(runtime, new CreateSessionOptions(), token)),
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => CopilotSessionChannel.ResumeConfinedSessionAsync(
                    runtime,
                    "session-1",
                    new ResumeSessionOptions(),
                    token)),
        };

        // Assert: each was refused by the runtime itself, before any start was attempted
        Assert.All(refusals, refused => Assert.Equal(nameof(CopilotRuntime), refused.ObjectName));
    }

    /// <summary>
    ///     Builds a runtime over a client that can never start: its runtime path names no file, so
    ///     an attempt to start it fails, spawning nothing, rather than succeeding unseen.
    /// </summary>
    /// <returns>The runtime. Nothing has been started.</returns>
    private static CopilotRuntime RuntimeThatCannotStart()
    {
        var temporary = Path.GetTempPath();
        var unique = Guid.NewGuid().ToString("N");
        return new CopilotRuntime(
            new CopilotClient(new CopilotClientOptions
            {
                Mode = CopilotClientMode.Empty,
                Runtime = RuntimeConnection.ForSpawnedStdio(
                    path: Path.Combine(temporary, "agentkit-no-such-runtime-" + unique),
                    baseDirectory: Path.Combine(temporary, unique)),
            }),
            Path.Combine(temporary, unique));
    }

    /// <summary>
    ///     Builds an entry of a session's effective MCP configuration.
    /// </summary>
    /// <param name="name">The server's name.</param>
    /// <param name="enabled">Whether the runtime reports the server enabled.</param>
    /// <returns>The configured-server entry.</returns>
    private static McpConfiguredServer Server(string name, bool enabled) => new() { Name = name, Enabled = enabled };
}
