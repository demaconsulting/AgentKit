using DemaConsulting.AgentKit.Core;

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
        Assert.Empty(config.Tools!);
        Assert.Empty(config.AvailableTools!);
        Assert.False(config.EnableSkills);
        Assert.True(config.SkipCustomInstructions);
    }

    /// <summary>
    ///     Proves a consolidation session carries the engine path's infinite-session configuration
    ///     too, rather than the runtime's default. A consolidation is one prompt and one answer; a
    ///     runtime that reshaped the material mid-consolidation would produce a record of something
    ///     other than what it was given.
    /// </summary>
    [Fact]
    public async Task CopilotSummarizer_Consolidate_DisablesTheRuntimesOwnCompaction()
    {
        // Arrange
        var runtime = Runtime("a record");
        var summarizer = new CopilotSummarizer(runtime.Opener, model: null);

        // Act
        await summarizer.ConsolidateAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(Assert.Single(runtime.Configs).InfiniteSessions!.Enabled);
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
    ///     Proves a missing client is refused where the application composed its provider.
    /// </summary>
    [Fact]
    public void CopilotSummarizer_Constructor_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CopilotSummarizer(null!));
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
