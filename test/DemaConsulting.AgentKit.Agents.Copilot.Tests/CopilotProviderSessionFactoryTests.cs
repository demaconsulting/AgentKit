using DemaConsulting.AgentKit.Core;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for <see cref="CopilotProviderSessionFactory"/>: what a rotation configures on the
///     session it creates — the derived allow-list, the withheld injection channels, the
///     default-safe permission handler, the disabled runtime compaction, the registered observer and
///     the rendered conversation record — and that nothing it opens is ever left unowned.
/// </summary>
/// <remarks>
///     All of this is decided at session creation and is observable nowhere else, so the assertions
///     are made against the <c>SessionConfig</c> the factory produced. No Copilot runtime is started
///     and no credential is used.
/// </remarks>
public class CopilotProviderSessionFactoryTests
{
    /// <summary>
    ///     The safety-critical assertion on the session path: the allow-list is derived from the
    ///     same collection as the published tools, so the two cannot drift apart. A drift here would
    ///     silently re-admit the runtime's own shell, fetch and file tools to a compacting session.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_BuildSessionConfig_AvailableToolsDerivedFromSeededTools()
    {
        // Arrange: the tools a rotation seeds
        var tools = new List<AIFunction> { MakeTool("doc_read"), MakeTool("doc_write") };
        var seed = new ProviderSessionSeed("be concise", tools, []);

        // Act
        var config = Build(seed);

        // Assert: the allow-list is exactly the published tool names, in the same order
        var expected = tools.Select(tool => tool.Name).ToList();
        Assert.Equal(expected, config.AvailableTools);
        Assert.Equal(expected, config.Tools!.Select(tool => tool.Name).ToList());
    }

    /// <summary>
    ///     Proves the runtime's two other injection channels are closed on a session the engine
    ///     drives, exactly as they are on the agent path. Either reverting to the permissive value
    ///     would widen the session beyond what its host attached without changing anything the host
    ///     wrote.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_BuildSessionConfig_SuppressesSkillsAndCustomInstructions()
    {
        // Arrange / Act
        var config = Build(new ProviderSessionSeed(null, [MakeTool("doc_read")], []));

        // Assert
        Assert.False(config.EnableSkills);
        Assert.True(config.SkipCustomInstructions);
    }

    /// <summary>
    ///     Proves the default-safe permission handler is installed on the session path too: a
    ///     seeded tool is approved by name, and every other request — including every built-in — is
    ///     rejected.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSessionFactory_BuildSessionConfig_InstallsTheDefaultSafePermissionHandler()
    {
        // Arrange
        var config = Build(new ProviderSessionSeed(null, [MakeTool("doc_read")], []));
        var handler = config.OnPermissionRequest;
        Assert.NotNull(handler);

        // Act
        var seeded = await handler(
            new PermissionRequestCustomTool
            {
                ToolName = "doc_read",
                ToolCallId = "call-1",
                ToolDescription = "a tool",
            },
            null!);
        var builtIn = await handler(new PermissionRequest { Kind = "shell" }, null!);

        // Assert
        Assert.Equal(PermissionDecision.ApproveOnce().Kind, seeded.Kind);
        Assert.Equal(PermissionDecision.Reject("x").Kind, builtIn.Kind);
    }

    /// <summary>
    ///     Proves every session the factory builds carries the engine path's infinite-session
    ///     configuration rather than the runtime's default, so the runtime's compaction threshold
    ///     stays clear of the point AgentKit rotates at.
    /// </summary>
    /// <remarks>
    ///     Copilot compacts at eighty percent of its window by default and AgentKit rotates at
    ///     seventy, so left at the default both would act on one conversation and the engine's
    ///     transcript would diverge from what the provider holds. The threshold is what the runtime
    ///     honors; the enablement flag beside it is asserted only because the configuration still
    ///     states the intent.
    /// </remarks>
    [Fact]
    public void CopilotProviderSessionFactory_BuildSessionConfig_HoldsTheRuntimesOwnCompactionClearOfRotation()
    {
        // Arrange / Act
        var config = Build(new ProviderSessionSeed(null, [MakeTool("doc_read")], []));

        // Assert
        Assert.NotNull(config.InfiniteSessions);
        Assert.False(config.InfiniteSessions.Enabled);
        Assert.True(config.InfiniteSessions.BackgroundCompactionThreshold > 0.90);
    }

    /// <summary>
    ///     Proves a seeded history is rendered into the system message after the instructions, fenced
    ///     so a reader and a model can see where direction ends and the record of what already
    ///     happened begins. This is the channel chosen because Copilot offers no history field at
    ///     all.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_BuildSessionConfig_SeedsHistoryAfterTheInstructions()
    {
        // Arrange: the seed a rotation produces — a consolidated record, then a verbatim turn
        var seed = new ProviderSessionSeed(
            "You are a research assistant.",
            [MakeTool("doc_read")],
            [
                TranscriptEntry.ContextRecord("Earlier: the corpus was surveyed."),
                TranscriptEntry.User("what changed?"),
                TranscriptEntry.Assistant("Three files."),
            ]);

        // Act
        var content = Build(seed).SystemMessage!.Content!;

        // Assert: instructions first, then the fenced record carrying every entry in order. The
        // separator is a newline rather than the platform's, so a rotation renders identically on
        // every machine - which is what a provider's prompt cache and a reproducible run need.
        var expected = string.Join(
            "\n",
            "You are a research assistant.",
            string.Empty,
            CopilotProviderSessionFactory.RecordOpening,
            "RECORD: Earlier: the corpus was surveyed.",
            "USER: what changed?",
            "ASSISTANT: Three files.",
            CopilotProviderSessionFactory.RecordClosing);
        Assert.Equal(expected, content);
        Assert.Equal(SystemMessageMode.Append, Build(seed).SystemMessage!.Mode!.Value);
    }

    /// <summary>
    ///     Proves the first session of a conversation — which carries no history — is configured
    ///     with the instructions alone, byte for byte as the agent path would configure them. A
    ///     record fence around nothing would tell the model a conversation had happened when none
    ///     had.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_BuildSessionConfig_SeedWithoutHistory_CarriesInstructionsOnly()
    {
        // Arrange / Act
        var config = Build(new ProviderSessionSeed("be concise", [MakeTool("doc_read")], []));

        // Assert
        Assert.Equal("be concise", config.SystemMessage!.Content);
        Assert.DoesNotContain(
            CopilotProviderSessionFactory.RecordOpening,
            config.SystemMessage.Content!,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a seed carrying neither instructions nor history leaves the session with no system
    ///     message at all, as the runtime would default it.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_BuildSessionConfig_BareSeed_CarriesNoSystemMessage()
    {
        // Arrange / Act
        var config = Build(new ProviderSessionSeed(null, [MakeTool("doc_read")], []));

        // Assert
        Assert.Null(config.SystemMessage);
    }

    /// <summary>
    ///     Proves a seed carrying history but no instructions is still seeded with the record. An
    ///     application that configures no instructions still rotates, and a rotation that dropped the
    ///     history there would silently restart the conversation.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_BuildSessionConfig_HistoryWithoutInstructions_CarriesTheRecord()
    {
        // Arrange / Act
        var config = Build(new ProviderSessionSeed(null, [], [TranscriptEntry.User("what changed?")]));

        // Assert
        var content = config.SystemMessage!.Content!;
        Assert.StartsWith(CopilotProviderSessionFactory.RecordOpening, content, StringComparison.Ordinal);
        Assert.Contains("USER: what changed?", content, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a seeded tool result is rendered as a labeled record rather than carried under any
    ///     role. This is the whole reason the system message was chosen: the class of defect that
    ///     shipped on the ChatClient path — a tool result under a role a provider's wire mapping
    ///     discards — is unrepresentable here, and the material demonstrably still reaches the model.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_BuildSessionConfig_SeededToolResult_IsRenderedAsALabeledRecord()
    {
        // Arrange: a rotation whose verbatim tail holds a tool call and its result
        var seed = new ProviderSessionSeed(
            null,
            [],
            [
                TranscriptEntry.ToolCall("call-3", "doc_read(path: notes.md)"),
                TranscriptEntry.ToolResult("call-3", "the file said forty-two"),
            ]);

        // Act
        var content = Build(seed).SystemMessage!.Content!;

        // Assert: both are present, labeled, and paired by the runtime's identifier
        Assert.Contains("TOOL CALL [call-3]: doc_read(path: notes.md)", content, StringComparison.Ordinal);
        Assert.Contains("TOOL RESULT [call-3]: the file said forty-two", content, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a named model is carried onto the session, and that naming none leaves the choice
    ///     to the runtime — so an application that says nothing about the model gets exactly the
    ///     behavior it had before the parameter existed.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_BuildSessionConfig_ModelIsCarriedOrLeftToTheRuntime()
    {
        // Arrange
        var seed = new ProviderSessionSeed(null, [MakeTool("doc_read")], []);
        var untouched = new SessionConfig();

        // Act
        var named = CopilotProviderSessionFactory.BuildProviderSessionConfig(seed, _ => { }, "gpt-5.4-mini");
        var unnamed = CopilotProviderSessionFactory.BuildProviderSessionConfig(seed, _ => { }, model: null);

        // Assert
        Assert.Equal("gpt-5.4-mini", named.Model);
        Assert.Equal(untouched.Model, unnamed.Model);
    }

    /// <summary>
    ///     Proves the observer is registered on the configuration rather than on the session
    ///     afterwards. The SDK installs the configuration's handler before the create RPC is issued,
    ///     which is what makes the very first turn's usage reading and tool traffic observable at
    ///     all.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSessionFactory_CreateAsync_RegistersTheObserverBeforeCreation()
    {
        // Arrange: a runtime whose first turn reports occupancy the session could only have seen
        // through a handler registered at creation
        var runtime = new FakeCopilotRuntime(_ =>
        [
            new ScriptedTurn(
                [CopilotEvents.Usage(777, 9000), CopilotEvents.Assistant("ok")],
                CopilotEvents.Assistant("ok")),
        ]);
        var factory = new CopilotProviderSessionFactory(runtime.Opener, model: null);

        // Act
        await using var session = await factory.CreateAsync(
            new ProviderSessionSeed(null, [], []),
            TestContext.Current.CancellationToken);
        await session.SendAsync("first turn", TestContext.Current.CancellationToken);

        // Assert: a handler was on the configuration, and the first turn's reading reached the session
        Assert.NotNull(runtime.Configs[0].OnEvent);
        Assert.Equal(777, session.CurrentUsage.UsedTokens);
    }

    /// <summary>
    ///     Proves a missing seed is refused before any session is opened.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSessionFactory_CreateAsync_NullSeed_Throws()
    {
        // Arrange
        var runtime = new FakeCopilotRuntime();
        var factory = new CopilotProviderSessionFactory(runtime.Opener, model: null);

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => factory.CreateAsync(null!, TestContext.Current.CancellationToken));
        Assert.Empty(runtime.Channels);
    }

    /// <summary>
    ///     Proves a canceled creation opens nothing, so a canceled rotation costs no session on the
    ///     runtime.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSessionFactory_CreateAsync_Canceled_OpensNothing()
    {
        // Arrange
        var runtime = new FakeCopilotRuntime();
        var factory = new CopilotProviderSessionFactory(runtime.Opener, model: null);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => factory.CreateAsync(new ProviderSessionSeed(null, [], []), canceled.Token));
        Assert.Empty(runtime.Channels);
    }

    /// <summary>
    ///     Proves a session opened but not yet owned is released rather than orphaned. Between the
    ///     create RPC returning and this factory taking ownership there is a window in which the
    ///     runtime holds a session nothing references, and a cancellation arriving in that window
    ///     would otherwise discard the only handle to it.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSessionFactory_CreateAsync_CanceledWhileOpening_ReleasesTheOpenedSession()
    {
        // Arrange: a runtime that opens its session and only then observes the cancellation, which is
        // what a token canceled while the create RPC was in flight looks like
        using var canceled = new CancellationTokenSource();
        var runtime = new FakeCopilotRuntime(beforeOpen: _ => canceled.Cancel());
        var factory = new CopilotProviderSessionFactory(runtime.Opener, model: null);

        // Act / Assert: the creation fails, and the session it had already opened is released
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => factory.CreateAsync(new ProviderSessionSeed(null, [], []), canceled.Token));
        Assert.Equal(1, Assert.Single(runtime.Channels).DisposeCount);
    }

    /// <summary>
    ///     Proves a missing client is refused where the application composed its provider, rather
    ///     than at the first rotation.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_Constructor_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CopilotProviderSessionFactory(null!));
    }

    /// <summary>
    ///     Proves a missing channel opener is refused. The seam is internal, so this is a guard on an
    ///     internal contract rather than a message to an application.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_Constructor_NullOpener_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CopilotProviderSessionFactory((CopilotChannelOpener)null!, model: null));
    }

    /// <summary>
    ///     Builds the configuration a rotation would create a session from.
    /// </summary>
    /// <param name="seed">The seed to configure from.</param>
    /// <returns>The session configuration.</returns>
    private static SessionConfig Build(ProviderSessionSeed seed) =>
        CopilotProviderSessionFactory.BuildProviderSessionConfig(seed, _ => { }, model: null);

    /// <summary>
    ///     Builds a no-op tool carrying the given name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) => AIFunctionFactory.Create(() => "ok", name);
}
