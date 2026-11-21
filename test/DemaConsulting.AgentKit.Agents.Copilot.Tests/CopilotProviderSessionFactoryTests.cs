using DemaConsulting.AgentKit.Core;
using Microsoft.Copilot;
using Microsoft.Copilot.Rpc;
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
///     are made against the <c>CreateSessionOptions</c> the factory produced. No Copilot runtime is started
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
        Assert.Equal(expected.Select(name => $"custom:{name}"), config.Tools.AvailableTools);
        Assert.Equal(expected, config.Tools.Definitions!.Select(tool => tool.Name).ToList());
    }

    /// <summary>
    ///     Proves the runtime's two other injection channels are withheld on a session the engine
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
        Assert.False(config.Skills.Enabled);
        Assert.True(config.Instructions.SkipCustomInstructions);
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
        var handler = config.Permissions.OnRequest;
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
        Assert.NotNull(config.State.InfiniteSessions);
        Assert.False(config.State.InfiniteSessions.Enabled);
        Assert.True(config.State.InfiniteSessions.BackgroundCompactionThreshold > 0.90);
    }

    /// <summary>
    ///     Proves the seeded history is carried ahead of the first message rather than in the system
    ///     message, and that the system message holds the application's instructions alone.
    /// </summary>
    /// <remarks>
    ///     Copilot offers no history field at all, so a rotation's history has to arrive on some
    ///     other channel. The system message is the wrong one: the record carries tool results, and
    ///     a tool result may be the contents of a file the agent was pointed at, which nobody in
    ///     this library wrote. Placing that in the highest-trust channel and fencing it is a
    ///     prompt-level defense — asking the model not to be fooled. On the first user message the
    ///     material sits in the channel it came from, and it costs no extra request because it rides
    ///     the message the engine was already sending.
    /// </remarks>
    [Fact]
    public void CopilotProviderSessionFactory_Seed_CarriesHistoryOnTheFirstMessageNotTheSystemMessage()
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
        var config = Build(seed);
        var preamble = CopilotProviderSessionFactory.ComposeHistoryPreamble(seed)!;

        // Assert: the system message is the instructions and nothing else, byte for byte as the
        // agent path would configure them
        Assert.Equal("You are a research assistant.", config.SystemMessage!.Content);
        Assert.Equal(SystemMessageMode.Customize, config.SystemMessage.Mode!.Value);

        // Assert: the record is fenced and carries every entry in order. The separator is a newline
        // rather than the platform's, so a rotation renders identically on every machine - which is
        // what a provider's prompt cache and a reproducible run need.
        var expected = string.Join(
            "\n",
            CopilotProviderSessionFactory.RecordOpening,
            "RECORD: Earlier: the corpus was surveyed.",
            "USER: what changed?",
            "ASSISTANT: Three files.",
            CopilotProviderSessionFactory.RecordClosing);
        Assert.Equal(expected, preamble);
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
        Assert.DoesNotContain(CopilotProviderSessionFactory.RecordOpening, config.SystemMessage.Content!, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a seed carrying neither instructions nor history leaves the session's system
    ///     message with no content at all: the message carries only the confinement's removal of the
    ///     runtime's host environment context.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_BuildSessionConfig_BareSeed_CarriesNoSystemMessageContent()
    {
        // Arrange / Act
        var config = Build(new ProviderSessionSeed(null, [MakeTool("doc_read")], []));

        // Assert
        Assert.Null(config.SystemMessage!.Content);
    }

    /// <summary>
    ///     Proves a seed carrying history but no instructions is still seeded with the record. An
    ///     application that configures no instructions still rotates, and a rotation that dropped the
    ///     history there would silently restart the conversation.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_Preamble_HistoryWithoutInstructions_CarriesTheRecord()
    {
        // Arrange / Act
        var seed = new ProviderSessionSeed(null, [], [TranscriptEntry.User("what changed?")]);
        var config = Build(seed);

        // Assert: no instructions means no system-message content at all, and the record still arrives
        var content = CopilotProviderSessionFactory.ComposeHistoryPreamble(seed)!;
        Assert.Null(config.SystemMessage!.Content);
        Assert.StartsWith(CopilotProviderSessionFactory.RecordOpening, content, StringComparison.Ordinal);
        Assert.Contains("USER: what changed?", content, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a seeded tool result is rendered as a labeled record rather than carried under any
    ///     role. The record has no role vocabulary at all, so the class of defect that shipped on the
    ///     ChatClient path — a tool result under a role a provider's wire mapping discards — is
    ///     unrepresentable here, and the material demonstrably still reaches the model.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_Preamble_SeededToolResult_IsRenderedAsALabeledRecord()
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
        var content = CopilotProviderSessionFactory.ComposeHistoryPreamble(seed)!;

        // Assert: both are present, labeled, and paired by the runtime's identifier
        Assert.Contains("TOOL CALL [call-3]: doc_read(path: notes.md)", content, StringComparison.Ordinal);
        Assert.Contains("TOOL RESULT [call-3]: the file said forty-two", content, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a working directory given to the factory is carried onto every session it builds,
    ///     and that one built without it leaves the choice to the runtime.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSessionFactory_BuildSessionConfig_WorkingDirectory_IsCarried()
    {
        // Arrange: a factory over a scripted runtime, given a working directory
        var workspace = Path.Combine(Path.GetTempPath(), "corpus");
        var runtime = new FakeCopilotRuntime();
        var factory = new CopilotProviderSessionFactory(runtime.Opener, model: null, workingDirectory: workspace);

        // Act
        await using var session = await factory.CreateAsync(
            new ProviderSessionSeed(null, [MakeTool("doc_read")], []),
            TestContext.Current.CancellationToken);
        var unset = CopilotProviderSessionFactory.BuildProviderSessionConfig(
            new ProviderSessionSeed(null, [MakeTool("doc_read")], []), _ => { }, model: null);

        // Assert
        Assert.Equal(workspace, Assert.Single(runtime.Configs).WorkingDirectory);
        Assert.Null(unset.WorkingDirectory);
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
        var untouched = new CreateSessionOptions();

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
        Assert.NotNull(runtime.Configs[0].Events.OnEvent);
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
    ///     Proves a missing runtime is refused where the application composed its provider, rather
    ///     than at the first rotation.
    /// </summary>
    [Fact]
    public void CopilotProviderSessionFactory_Constructor_NullRuntime_Throws()
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
    ///     Proves a provider-session factory never starts its runtime again once the host has
    ///     disposed it. The SDK restarts a disposed client on demand, so a rotation that reached the
    ///     client would bring a runtime the host had stopped back to life.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSessionFactory_CreateAsync_RuntimeDisposedAfterConstruction_FailsWithoutStartingTheRuntime()
    {
        // Arrange: a factory on a runtime whose client can never start, so an attempt to start it
        // would fail as a start failure rather than as the runtime's disposal
        var runtime = RuntimeThatCannotStart();
        var factory = new CopilotProviderSessionFactory(runtime);
        await runtime.DisposeAsync();

        // Act
        var refused = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => factory.CreateAsync(new ProviderSessionSeed(null, [], []), TestContext.Current.CancellationToken));

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
    ///     Builds the configuration a rotation would create a session from.
    /// </summary>
    /// <param name="seed">The seed to configure from.</param>
    /// <returns>The session configuration.</returns>
    private static CreateSessionOptions Build(ProviderSessionSeed seed) =>
        CopilotProviderSessionFactory.BuildProviderSessionConfig(seed, _ => { }, model: null);

    /// <summary>
    ///     Builds a no-op tool carrying the given name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) => AIFunctionFactory.Create(() => "ok", name);
}
