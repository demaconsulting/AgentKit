using DemaConsulting.AgentKit.Core;
using Microsoft.Copilot;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for <see cref="CopilotSummarizer"/>: that a consolidation is sent as the composed
///     prompt, on a fresh tool-free session of its own, and that the session is released on every
///     path.
/// </summary>
/// <remarks>
///     No Copilot runtime is started and no credential is used; the summarizer is constructed over
///     the internal channel-opener seam, for the reason <c>ICopilotTurnChannel</c> records.
/// </remarks>
public class CopilotSummarizerTests
{
    /// <summary>
    ///     Proves the summarizer sends exactly the prompt Core composes — the base instruction, the
    ///     level's aggressiveness clause, and the material — so what a Copilot consolidation is asked
    ///     for is what every other provider is asked for.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_SendsTheComposedConsolidationPrompt()
    {
        // Arrange
        var runtime = Runtime("a record");
        var summarizer = new CopilotSummarizer(runtime.Opener, model: null);
        var request = new ConsolidationRequest(1, "USER: hello", ConsolidationPrompt.MediumInstruction);

        // Act
        var record = await summarizer.ConsolidateAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("a record", record);
        Assert.Equal(
            ConsolidationPrompt.Compose(request),
            Assert.Single(Assert.Single(runtime.Channels).Prompts));
    }

    /// <summary>
    ///     Proves the session a consolidation runs on carries no tools and an empty allow-list. There
    ///     is nothing for a tool to do in a consolidation, and a tool that could act would be acting
    ///     outside everything the conversation's own confinement was reasoned about.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_CarriesNoToolsAndAnEmptyAllowList()
    {
        // Arrange
        var runtime = Runtime("a record");
        var summarizer = new CopilotSummarizer(runtime.Opener, model: null);

        // Act
        await summarizer.ConsolidateAsync(Request(), TestContext.Current.CancellationToken);

        // Assert: nothing published, nothing allowed, and the runtime's other injection channels shut
        var config = Assert.Single(runtime.Configs);
        Assert.Empty(config.Tools.Definitions!);
        Assert.Empty(config.Tools.AvailableTools!);
        Assert.False(config.Skills.Enabled);
        Assert.True(config.Instructions.SkipCustomInstructions);
    }

    /// <summary>
    ///     Proves a consolidation runs on the selected model with the chosen effort, and a model named
    ///     twice is refused.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_Selection_RunsOnTheSelectedModel()
    {
        // Arrange
        var runtime = Runtime("a record");
        var selection = CopilotModelCatalogTests.Selection(effort: "low");
        var summarizer = new CopilotSummarizer(runtime.Opener, model: null, selection);

        // Act
        await summarizer.ConsolidateAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        var config = Assert.Single(runtime.Configs);
        Assert.Equal("claude-haiku-5.5", config.Model);
        Assert.Equal("low", config.Inference?.ReasoningEffort);
        Assert.Null(config.Inference?.ContextTier);
        Assert.Throws<ArgumentException>(() => new CopilotSummarizer(runtime.Opener, "gpt-6", selection));
    }

    /// <summary>
    ///     Proves a selection carrying a context window or an output limit is refused, because a
    ///     consolidation session is accounted against no window and the runtime takes no output
    ///     limit, so neither would be applied.
    /// </summary>
    [Fact]
    public void CopilotSummarizer_Constructor_SelectionWithAWindowOrOutputLimit_Throws()
    {
        // Arrange
        var runtime = Runtime("a record");
        var windowed = CopilotModelCatalogTests.Selection(window: 4000);
        var limited = CopilotAgentFactoryTests.HandBuiltSelectionWithAnOutputLimit();

        // Act
        var window = Assert.Throws<ArgumentException>(() => new CopilotSummarizer(runtime.Opener, null, windowed));
        var output = Assert.Throws<ArgumentException>(() => new CopilotSummarizer(runtime.Opener, null, limited));

        // Assert: each refusal names the setting it could not apply
        Assert.Contains("ContextWindowTokens cannot be applied here", window.Message, StringComparison.Ordinal);
        Assert.Contains("MaxOutputTokens cannot be applied on Copilot", output.Message, StringComparison.Ordinal);
        Assert.Empty(runtime.Configs);
    }

    /// <summary>
    ///     Proves a consolidation session carries the engine path's infinite-session configuration
    ///     too, rather than the runtime's default. A consolidation is one prompt and one answer; a
    ///     runtime that reshaped the material mid-consolidation would produce a record of something
    ///     other than what it was given.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_HoldsTheRuntimesOwnCompactionClearOfRotation()
    {
        // Arrange
        var runtime = Runtime("a record");
        var summarizer = new CopilotSummarizer(runtime.Opener, model: null);

        // Act
        await summarizer.ConsolidateAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(Assert.Single(runtime.Configs).State.InfiniteSessions!.Enabled);
    }

    /// <summary>
    ///     Proves each consolidation runs on a session of its own. Sent through the session being
    ///     compacted, a consolidation would spend the very context it exists to reclaim and would
    ///     itself count toward the occupancy that triggered the rotation.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_RunsOutOfTheSessionBeingCompacted()
    {
        // Arrange
        var runtime = Runtime("a record");
        var summarizer = new CopilotSummarizer(runtime.Opener, model: null);

        // Act: two consolidations, as two rotations would perform
        await summarizer.ConsolidateAsync(Request(), TestContext.Current.CancellationToken);
        await summarizer.ConsolidateAsync(Request(), TestContext.Current.CancellationToken);

        // Assert: a fresh session each time, neither carrying the other's material
        Assert.Equal(2, runtime.Channels.Count);
        Assert.All(runtime.Channels, channel => Assert.Single(channel.Prompts));
    }

    /// <summary>
    ///     Proves a named model is carried onto the consolidation session, so an application can run
    ///     consolidation on a smaller and cheaper model than the conversation.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_NamedModel_IsCarried()
    {
        // Arrange
        var runtime = Runtime("a record");
        var summarizer = new CopilotSummarizer(runtime.Opener, "gpt-5.4-mini");

        // Act
        await summarizer.ConsolidateAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("gpt-5.4-mini", Assert.Single(runtime.Configs).Model);
    }

    /// <summary>
    ///     Proves a session that answered nothing produces an empty record rather than an exception.
    ///     The engine treats a consolidation it could not obtain as material to keep rather than
    ///     material to lose, and a model declining to answer is a thing that happens.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_EmptyAnswer_IsAnEmptyRecord()
    {
        // Arrange: a session that goes idle without producing an assistant message
        var runtime = new FakeCopilotRuntime(_ => [new ScriptedTurn([])]);
        var summarizer = new CopilotSummarizer(runtime.Opener, model: null);

        // Act
        var record = await summarizer.ConsolidateAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(string.Empty, record);
    }

    /// <summary>
    ///     Proves the consolidation session is released on the successful path. A long conversation
    ///     performs one of these per rotation, so a session leaked here would accumulate for exactly
    ///     as long as the conversation this exists to prolong.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_Succeeds_DisposesTheSession()
    {
        // Arrange
        var runtime = Runtime("a record");
        var summarizer = new CopilotSummarizer(runtime.Opener, model: null);

        // Act
        await summarizer.ConsolidateAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, Assert.Single(runtime.Channels).DisposeCount);
    }

    /// <summary>
    ///     Proves the consolidation session is released when the consolidation fails, which is the
    ///     path a leak actually hides on.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_Fails_DisposesTheSession()
    {
        // Arrange: a runtime that drops the connection mid-consolidation
        var runtime = new FakeCopilotRuntime(_ =>
        [
            new ScriptedTurn([], Failure: new InvalidOperationException("the runtime dropped the connection")),
        ]);
        var summarizer = new CopilotSummarizer(runtime.Opener, model: null);

        // Act / Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => summarizer.ConsolidateAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal(1, Assert.Single(runtime.Channels).DisposeCount);
    }

    /// <summary>
    ///     Proves a consolidation the runtime or the SDK fails with a session error quoting a token is
    ///     relayed as a new <see cref="InvalidOperationException"/> whose chain carries no secret, and
    ///     the consolidation session is still released.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_FailureQuotingAToken_IsRelayedRedacted()
    {
        // Arrange: the SDK's session-error shape, over a cause that quotes the secret too
        const string secret = "s3cr3t-summary-xyz";
        const string shaped = "gho_SummarySecret123";
        var cause = new InvalidOperationException(
            $"Session error: refused {secret} {shaped}",
            new IOException($"pipe {secret}"));
        var runtime = new FakeCopilotRuntime(_ => [new ScriptedTurn([], Failure: cause)]);
        var summarizer = new CopilotSummarizer(runtime.Opener, model: null, signIn: CopilotSignInTests.PlanKnowing(secret));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => summarizer.ConsolidateAsync(Request(), TestContext.Current.CancellationToken));

        // Assert
        Assert.NotSame(cause, failure);
        Assert.Equal("Session error: refused <redacted> gho_<redacted>", failure.Message);
        Assert.IsType<CopilotRedactedException>(failure.InnerException);
        SecretAssert.NoSecret(failure, secret, shaped);
        Assert.Equal(1, Assert.Single(runtime.Channels).DisposeCount);
    }

    /// <summary>
    ///     Proves a failure opening a consolidation session — a lost connection quoting the runtime's
    ///     standard error — is relayed as a new <see cref="IOException"/> whose chain carries no
    ///     secret.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_OpenFailureQuotingAToken_IsRelayedRedacted()
    {
        // Arrange: an opener that fails as the SDK does, quoting both values
        const string secret = "s3cr3t-summary-open-xyz";
        const string shaped = "gho_SummaryOpenSecret123";
        var lost = new IOException($"CLI process exited unexpectedly.\nstderr: {secret} {shaped}");
        var summarizer = new CopilotSummarizer(
            (_, _) => Task.FromException<ICopilotTurnChannel>(lost),
            model: null,
            signIn: CopilotSignInTests.PlanKnowing(secret));

        // Act
        var failure = await Assert.ThrowsAsync<IOException>(
            () => summarizer.ConsolidateAsync(Request(), TestContext.Current.CancellationToken));

        // Assert
        Assert.NotSame(lost, failure);
        Assert.Equal("CLI process exited unexpectedly.\nstderr: <redacted> gho_<redacted>", failure.Message);
        Assert.IsType<CopilotRedactedException>(failure.InnerException);
        SecretAssert.NoSecret(failure, secret, shaped);
    }

    /// <summary>
    ///     Proves a missing request is refused where the engine composed it.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_NullRequest_Throws()
    {
        // Arrange
        var runtime = new FakeCopilotRuntime();
        var summarizer = new CopilotSummarizer(runtime.Opener, model: null);

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => summarizer.ConsolidateAsync(null!, TestContext.Current.CancellationToken));
        Assert.Empty(runtime.Channels);
    }

    /// <summary>
    ///     Proves a canceled consolidation opens nothing, so a canceled rotation costs no session on
    ///     the runtime.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_Canceled_OpensNothing()
    {
        // Arrange
        var runtime = new FakeCopilotRuntime();
        var summarizer = new CopilotSummarizer(runtime.Opener, model: null);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => summarizer.ConsolidateAsync(Request(), canceled.Token));
        Assert.Empty(runtime.Channels);
    }

    /// <summary>
    ///     Proves a missing runtime is refused where the application composed its provider.
    /// </summary>
    [Fact]
    public void CopilotSummarizer_Constructor_NullRuntime_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CopilotSummarizer(null!));
    }

    /// <summary>
    ///     Proves a summarizer never starts its runtime again once the host has disposed it. The SDK
    ///     restarts a disposed client on demand, so a consolidation that reached the client would
    ///     bring a runtime the host had stopped back to life.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_ConsolidateAsync_RuntimeDisposedAfterConstruction_FailsWithoutStartingTheRuntime()
    {
        // Arrange: a summarizer on a runtime whose client can never start, so an attempt to start it
        // would fail as a start failure rather than as the runtime's disposal
        var runtime = RuntimeThatCannotStart();
        var summarizer = new CopilotSummarizer(runtime);
        await runtime.DisposeAsync();

        // Act
        var refused = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => summarizer.ConsolidateAsync(Request(), TestContext.Current.CancellationToken));

        // Assert: refused by the runtime itself, before any start was attempted
        Assert.Equal(nameof(CopilotRuntime), refused.ObjectName);
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
    ///     Builds a fake runtime whose every session answers with the given text.
    /// </summary>
    /// <param name="answer">The record each consolidation session returns.</param>
    /// <returns>The fake runtime.</returns>
    private static FakeCopilotRuntime Runtime(string answer) =>
        new(_ => [new ScriptedTurn([], CopilotEvents.Assistant(answer))]);

    /// <summary>
    ///     Builds a consolidation request standing in for one a rotation produces.
    /// </summary>
    /// <returns>The request.</returns>
    private static ConsolidationRequest Request() =>
        new(1, "USER: hello\nASSISTANT: hi", ConsolidationPrompt.LowInstruction);
}
