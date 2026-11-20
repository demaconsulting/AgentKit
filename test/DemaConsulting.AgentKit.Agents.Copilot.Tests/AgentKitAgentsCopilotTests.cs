using DemaConsulting.AgentKit.Agents.Copilot;
using DemaConsulting.AgentKit.Core;
using Microsoft.Copilot;
using Microsoft.Copilot.Rpc;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     System-level integration tests for the AgentKitAgentsCopilot system.
/// </summary>
/// <remarks>
///     These tests exercise the behavioral guarantees the package exists for without a live
///     Copilot CLI: the allow-list is derived from the supplied tools so the two cannot drift
///     apart (the safety-critical property of the whole increment), the default permission
///     handler approves exactly the supplied tools while rejecting everything else, and a real
///     <c>CompactingAgentSession</c> runs a conversation to a rotation on a scripted Copilot
///     runtime. The session configuration is a plain constructable object and the runtime is
///     reached through the package's own turn-channel seam, so all of it is asserted offline.
/// </remarks>
public class AgentKitAgentsCopilotTests
{
    /// <summary>
    ///     The safety-critical assertion: <c>AvailableTools</c> is derived from the same collection
    ///     as <c>Tools</c>, so the published tool set and the allow-list that suppresses the
    ///     runtime's built-in tools cannot diverge. A drift here would silently re-admit shell,
    ///     fetch, or file-editing tools an application meant to withhold.
    /// </summary>
    [Fact]
    public void AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools()
    {
        // Arrange: a set of supplied tools
        var tools = new List<AIFunction>
        {
            MakeTool("doc_read"),
            MakeTool("doc_write"),
            MakeTool("doc_list"),
        };

        // Act: build the session configuration the factory would hand to the runtime
        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        // Assert: the allow-list is exactly the names of the published tools, in the same order,
        // and the published tool set is the same size — the two are derived from one collection
        var expectedNames = tools.Select(tool => tool.Name).ToList();
        Assert.Equal(expectedNames.Select(name => $"custom:{name}"), config.Tools.AvailableTools);
        Assert.Equal(tools.Count, config.Tools.Definitions!.Count);
        Assert.Equal(expectedNames, config.Tools.Definitions.Select(tool => tool.Name).ToList());
    }

    /// <summary>
    ///     Proves the default permission handler approves a supplied tool requested by its exact
    ///     name.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_DefaultPermissionHandler_SuppliedTool_IsApproved()
    {
        var tools = new List<AIFunction> { MakeTool("doc_read") };
        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        var decision = await Decide(config, MakeCustomToolRequest("doc_read"));

        Assert.Equal(PermissionDecision.ApproveOnce().Kind, decision.Kind);
    }

    /// <summary>
    ///     Proves the default permission handler rejects a custom tool that was not supplied.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_DefaultPermissionHandler_UnlistedCustomTool_IsRejected()
    {
        var tools = new List<AIFunction> { MakeTool("doc_read") };
        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        var decision = await Decide(config, MakeCustomToolRequest("doc_delete"));

        Assert.Equal(PermissionDecision.Reject("x").Kind, decision.Kind);
    }

    /// <summary>
    ///     Proves the default permission handler rejects a built-in tool request — one that is not
    ///     a supplied custom tool — because it is never in the allow-list. This is the runtime-tool
    ///     suppression the package exists for, enforced at the permission boundary as well as in the
    ///     allow-list.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_DefaultPermissionHandler_BuiltInTool_IsRejected()
    {
        var tools = new List<AIFunction> { MakeTool("doc_read") };
        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        // A built-in request (shell, read, write, url, ...) arrives as something other than a
        // custom-tool request; the base type stands in for any of them here.
        var decision = await Decide(config, new PermissionRequest { Kind = "shell" });

        Assert.Equal(PermissionDecision.Reject("x").Kind, decision.Kind);
    }

    /// <summary>
    ///     Proves a conversation runs end to end on the Copilot provider session: the answer comes
    ///     back, and the occupancy the session reports is the runtime's own, out of the runtime's own
    ///     limit. Nothing about the window is supplied by the application.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Session_AnswersAndReportsTheRuntimesOccupancy()
    {
        // Arrange: a compacting session over a scripted Copilot runtime
        var runtime = new FakeCopilotRuntime(_ =>
        [
            new ScriptedTurn(
                [CopilotEvents.Usage(1_500, 64_000, 900), CopilotEvents.Assistant("Three files changed.")],
                CopilotEvents.Assistant("Three files changed.")),
        ]);

        await using var session = await CompactingAgentSession.CreateAsync(
            new AgentSessionOptions(new RecordingSummarizer(), "You are a research assistant."),
            new CopilotProviderSessionFactory(runtime.Opener, model: null),
            TestContext.Current.CancellationToken);

        // Act
        var response = await session.SendAsync("what changed?", TestContext.Current.CancellationToken);

        // Assert: the answer, and the runtime's own figures reaching the engine
        Assert.Equal("Three files changed.", response.Text);
        Assert.Equal(64_000, response.Usage.WindowTokens);
        Assert.Equal(1_500, response.Usage.UsedTokens);
        Assert.Equal(900, response.Usage.ConversationTokens);
        Assert.False(response.RotationOccurred);
    }

    /// <summary>
    ///     The scenario this whole increment exists for: the session engine runs on Copilot, rotates
    ///     when the runtime says its window is filling, and seeds the replacement with a consolidated
    ///     record — all driven by figures the runtime reported rather than any the application
    ///     supplied.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Session_RotatesOnTheRuntimesUsage_AndSeedsTheReplacement()
    {
        // Arrange: a first session whose one turn fills the window past the rotation threshold, and a
        // replacement that reports a comfortable one. The window is small so one turn crosses it.
        var runtime = new FakeCopilotRuntime(index => index == 0
            ? [Turn("first answer", currentTokens: 900, tokenLimit: 1_000)]
            : [Turn("replacement answer", currentTokens: 100, tokenLimit: 1_000)]);

        var summarizer = new RecordingSummarizer();
        await using var session = await CompactingAgentSession.CreateAsync(
            new AgentSessionOptions(summarizer, "You are a research assistant.", verbatimTurns: 1),
            new CopilotProviderSessionFactory(runtime.Opener, model: null),
            TestContext.Current.CancellationToken);

        // Act: one turn, which the runtime reports as having filled the window
        var response = await session.SendAsync("first question", TestContext.Current.CancellationToken);

        // Assert: a rotation happened, a consolidation was performed out of session, the replacement
        // was created carrying the consolidated record, and the superseded session was released
        Assert.True(response.RotationOccurred);
        Assert.Equal(1, session.RotationCount);
        Assert.Single(summarizer.Requests);
        Assert.Contains("first question", summarizer.Requests[0].Material, StringComparison.Ordinal);

        Assert.Equal(2, runtime.Configs.Count);

        // The replacement's system message carries the application's instructions and nothing else.
        // The consolidated record is untrusted material - it can contain whatever a tool read off
        // disk - so it travels on the conversation channel rather than the system one, and it does
        // so on the next message rather than in a request of its own.
        var seeded = runtime.Configs[1].SystemMessage!.Content!;
        Assert.Equal("You are a research assistant.", seeded);
        Assert.DoesNotContain(CopilotProviderSessionFactory.RecordOpening, seeded, StringComparison.Ordinal);

        // Sending on the replacement carries the record ahead of the caller's own message, once
        await session.SendAsync("second question", TestContext.Current.CancellationToken);
        var firstPrompt = runtime.Channels[1].Prompts[0];
        Assert.Contains(CopilotProviderSessionFactory.RecordOpening, firstPrompt, StringComparison.Ordinal);
        Assert.Contains(RecordingSummarizer.Record, firstPrompt, StringComparison.Ordinal);
        Assert.EndsWith("second question", firstPrompt, StringComparison.Ordinal);

        // The replacement has sent nothing, so it occupies nothing — which is what stops a
        // rotation cascading. A replacement that inherited its predecessor's figure would cross the
        // threshold again on adoption and rotate forever.
        Assert.Equal(0, response.Usage.UsedTokens);
        Assert.Equal(0, response.Usage.ConversationTokens);

        Assert.Equal(1, runtime.Channels[0].DisposeCount);
        Assert.Equal(0, runtime.Channels[1].DisposeCount);
    }

    /// <summary>
    ///     Proves the runtime's built-in tools are suppressed on the session path exactly as they are
    ///     on the agent path: every session a rotation creates carries the default-safe permission
    ///     handler, which rejects a built-in request because none is one of the seeded tools.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Session_BuiltInToolRequest_IsRejectedOnTheSessionPath()
    {
        // Arrange
        var seed = new ProviderSessionSeed(null, [MakeTool("doc_read")], []);
        var config = CopilotProviderSessionFactory.BuildProviderSessionConfig(seed, _ => { }, model: null);

        // Act: a built-in request arrives as something other than a custom-tool request
        var decision = await Decide(config, new PermissionRequest { Kind = "shell" });

        // Assert
        Assert.Equal(PermissionDecision.Reject("x").Kind, decision.Kind);
    }

    /// <summary>
    ///     Proves every session AgentKit's engine drives on Copilot is created with the engine
    ///     path's infinite-session configuration — the first and every replacement — so no rotation
    ///     can silently hand the conversation back to the runtime's compactor at the runtime's own
    ///     threshold. The value that configuration carries is asserted where it is set, in
    ///     <c>CopilotAgentFactoryTests</c>.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Session_RuntimeCompactionIsHeldClearOfRotationOnEverySessionItBuilds()
    {
        // Arrange: a run that rotates once, so both a first session and a replacement are created
        var runtime = new FakeCopilotRuntime(index => index == 0
            ? [Turn("first answer", currentTokens: 990, tokenLimit: 1_000)]
            : [Turn("replacement answer", currentTokens: 100, tokenLimit: 1_000)]);

        await using var session = await CompactingAgentSession.CreateAsync(
            new AgentSessionOptions(new RecordingSummarizer(), "be concise", verbatimTurns: 1),
            new CopilotProviderSessionFactory(runtime.Opener, model: null),
            TestContext.Current.CancellationToken);

        // Act
        await session.SendAsync("a question", TestContext.Current.CancellationToken);

        // Assert: the threshold is what the runtime honors and therefore what holds it off, so that
        // is what every session must carry. The flag is asserted too, but only as stated intent -
        // it was measured to be inert, so a test that checked it alone would pass against a session
        // the runtime was free to compact at its default of 0.80, just below the engine's 0.70.
        Assert.Equal(2, runtime.Configs.Count);
        Assert.All(
            runtime.Configs,
            config =>
            {
                Assert.True(config.State.InfiniteSessions!.BackgroundCompactionThreshold > 0.90);
                Assert.False(config.State.InfiniteSessions.Enabled);
            });
    }

    /// <summary>
    ///     Proves an agent built by the factory runs a conversation on the Copilot runtime end to end:
    ///     the session it creates is confined to the supplied tools, the tool traffic and the answer
    ///     stream back as they happen, and the session is released when the run ends.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Agent_RunsAndStreamsOnTheRuntime()
    {
        // Arrange: an agent over a scripted runtime whose turn calls a tool and streams its answer
        var runtime = new FakeCopilotRuntime(_ =>
        [
            new ScriptedTurn(
            [
                CopilotEvents.ToolStart("call-1", "doc_read"),
                CopilotEvents.ToolComplete("call-1", "CODEWORD: PERIWINKLE-42"),
                CopilotEvents.Delta("The codeword is ", "m1"),
                CopilotEvents.Delta("PERIWINKLE-42.", "m1"),
                CopilotEvents.Assistant("The codeword is PERIWINKLE-42.", messageId: "m1"),
            ]),
        ]);
        var agent = CopilotAgentFactory.CreateAgent(
            [MakeTool("doc_read")],
            instructions: "be concise",
            onPermissionRequest: null,
            name: "document-assistant",
            model: null,
            workingDirectory: null,
            runtime.Opener,
            runtime.Resumer);

        // Act
        var updates = new List<Microsoft.Agents.AI.AgentResponseUpdate>();
        await foreach (var update in agent.RunStreamingAsync("what is the codeword?", cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        // Assert: confined, streamed in order, released
        Assert.Equal(["custom:doc_read"], Assert.Single(runtime.Configs).Tools.AvailableTools);
        var contents = updates.SelectMany(update => update.Contents).ToList();
        Assert.IsType<FunctionCallContent>(contents[0]);
        Assert.IsType<FunctionResultContent>(contents[1]);
        Assert.Equal(
            "The codeword is PERIWINKLE-42.",
            string.Concat(contents.OfType<TextContent>().Select(text => text.Text)));
        Assert.Equal(1, Assert.Single(runtime.Channels).DisposeCount);
    }

    /// <summary>
    ///     Proves a conversation serialized from one agent resumes on another under the same
    ///     confinement: the second agent reopens the same runtime session, with the allow-list and
    ///     the default-deny handler re-applied rather than trusted to the runtime's memory.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Agent_SerializedSession_ResumesUnderTheSameConfinement()
    {
        // Arrange: a conversation that ran once on a first agent, then was serialized
        var runtime = new FakeCopilotRuntime(_ => [new ScriptedTurn([CopilotEvents.Assistant("noted")])]);
        IList<AIFunction> tools = [MakeTool("doc_read")];
        var first = CopilotAgentFactory.CreateAgent(
            tools, null, null, null, null, null, runtime.Opener, runtime.Resumer);
        var session = await first.CreateSessionAsync(TestContext.Current.CancellationToken);
        await first.RunAsync("remember the codeword", session, cancellationToken: TestContext.Current.CancellationToken);
        var serialized = await first.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken);

        // Act: a second agent restores the conversation and runs on it
        var second = CopilotAgentFactory.CreateAgent(
            tools, null, null, null, null, null, runtime.Opener, runtime.Resumer);
        var restored = await second.DeserializeSessionAsync(serialized, cancellationToken: TestContext.Current.CancellationToken);
        await second.RunAsync("what was it?", restored, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the same runtime session, resumed with the confinement re-applied
        var (sessionId, config) = Assert.Single(runtime.Resumes);
        Assert.Equal(runtime.Channels[0].SessionId, sessionId);
        Assert.Equal(["custom:doc_read"], config.Tools.AvailableTools);
        var decision = await config.Permissions.OnRequest!(new PermissionRequest { Kind = "shell" }, null!);
        Assert.Equal(PermissionDecision.Reject("x").Kind, decision.Kind);
    }

    /// <summary>
    ///     Proves an application that composes a tool under the name of one of the runtime's built-in
    ///     tools is told so when it builds the agent, rather than when the runtime refuses the session
    ///     on the first run.
    /// </summary>
    [Fact]
    public void AgentKitAgentsCopilot_BuildSessionConfig_BuiltInToolName_IsRefused()
    {
        // Arrange: a composed tool set in which one tool is named like the runtime's file viewer
        var runtime = new FakeCopilotRuntime();
        IList<AIFunction> tools = [MakeTool("doc_read"), MakeTool("view")];

        // Act
        var failure = Assert.Throws<ArgumentException>(() => CopilotAgentFactory.CreateAgent(
            tools, null, null, null, null, null, runtime.Opener, runtime.Resumer));

        // Assert: refused by name, and no session was ever requested
        Assert.Contains("'view'", failure.Message, StringComparison.Ordinal);
        Assert.Empty(runtime.Configs);
    }

    /// <summary>
    ///     Proves every session the package builds — an agent's, created and resumed, a compacting
    ///     session's, and a consolidation's — switches off the runtime's diversion of a large tool
    ///     result to a temporary file no confined tool could read.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_EverySession_DeliversToolResultsInline()
    {
        // Arrange: one runtime serving an agent, a compacting session and a consolidation
        var runtime = new FakeCopilotRuntime(_ => [new ScriptedTurn([CopilotEvents.Usage(100, 64_000), CopilotEvents.Assistant("ok")], CopilotEvents.Assistant("ok"))]);
        var agent = CopilotAgentFactory.CreateAgent(
            [MakeTool("doc_read")], null, null, null, null, null, runtime.Opener, runtime.Resumer);
        var conversation = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act: an agent's first and second run, an engine session's creation, and a consolidation
        await agent.RunAsync("first", conversation, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("second", conversation, cancellationToken: TestContext.Current.CancellationToken);
        var engineSession = await new CopilotProviderSessionFactory(runtime.Opener, model: null)
            .CreateAsync(new ProviderSessionSeed(null, [MakeTool("doc_read")], []), TestContext.Current.CancellationToken);
        await engineSession.DisposeAsync();

        await new CopilotSummarizer(runtime.Opener, model: null).ConsolidateAsync(
            new ConsolidationRequest(1, "USER: hello", ConsolidationPrompt.MediumInstruction),
            TestContext.Current.CancellationToken);

        // Assert: every configuration the runtime was handed keeps results inline
        Assert.Equal(3, runtime.Configs.Count);
        Assert.All(runtime.Configs, config => Assert.False(config.Tools.LargeOutput!.Enabled));
        Assert.False(Assert.Single(runtime.Resumes).Config.Tools.LargeOutput!.Enabled);
    }

    /// <summary>
    ///     Proves a host whose Copilot client cannot start because an ambient runtime path redirects
    ///     it is told so, by name, with the original failure kept.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_ClientStarter_FailedStart_IsExplained()
    {
        // Arrange: a start that fails the way an in-process runtime does when redirected
        var cause = new InvalidOperationException("FFI runtime library not found.");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotClientStarter.StartAsync(
            _ => Task.FromException(cause),
            name => name == "COPILOT_CLI_PATH" ? "/opt/copilot/copilot" : null,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("COPILOT_CLI_PATH", failure.Message, StringComparison.Ordinal);
        Assert.Same(cause, failure.InnerException);
    }

    /// <summary>
    ///     Proves every session the package opens on a real client — an engine session and an
    ///     agent's — has every ambient MCP server closed: a server the runtime discovers, a
    ///     non-GitHub one included, is disabled before the session is created; the session's
    ///     installed plugins are cleared; and a session in which a server stays enabled is refused
    ///     and abandoned rather than handed to the model.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_EverySession_AmbientMcpServersAreClosed()
    {
        // Arrange: the configurations an engine session and an agent session are created from
        SessionOptionsBase[] configs =
        [
            CopilotAgentFactory.BuildEngineSessionConfig([MakeTool("doc_read")], instructions: null, model: null),
            CopilotAgentFactory.BuildSessionConfig([MakeTool("doc_read")], instructions: null, onPermissionRequest: null),
        ];

        foreach (var config in configs)
        {
            var steps = new List<string>();
            List<string>? disabledAtOpen = null;

            // Act: open through the production protocol, with a runtime that discovers a
            // non-GitHub server and then keeps it enabled however often it is disabled
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotSessionChannel.OpenConfinedAsync(
                config,
                _ => Task.FromResult<IEnumerable<string>>(["docs-search"]),
                _ =>
                {
                    disabledAtOpen = [.. config.Mcp.DisabledServers!];
                    return Task.FromResult("session-1");
                },
                (_, token) => CopilotSessionChannel.SealAsync(
                    _ =>
                    {
                        steps.Add("clear-plugins");
                        return Task.CompletedTask;
                    },
                    _ => Task.FromResult<IList<McpConfiguredServer>>(
                        [new McpConfiguredServer { Name = "docs-search", Enabled = true }]),
                    (name, _) =>
                    {
                        steps.Add($"disable:{name}");
                        return Task.CompletedTask;
                    },
                    token),
                _ =>
                {
                    steps.Add("abandon");
                    return ValueTask.CompletedTask;
                },
                TestContext.Current.CancellationToken));

            // Assert: named before creation, plugins cleared, disabled, and the session refused
            Assert.Contains("docs-search", disabledAtOpen!);
            Assert.Contains(CopilotAgentFactory.BuiltInGitHubMcpServer, disabledAtOpen!);
            Assert.Equal(["clear-plugins", "disable:docs-search", "abandon"], steps);
            Assert.Contains("'docs-search'", failure.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Builds a scripted turn that answers and reports the runtime's occupancy.
    /// </summary>
    /// <param name="answer">The answer the turn produces.</param>
    /// <param name="currentTokens">The occupancy the runtime reports.</param>
    /// <param name="tokenLimit">The window the runtime reports.</param>
    /// <returns>The scripted turn.</returns>
    private static ScriptedTurn Turn(string answer, long currentTokens, long tokenLimit)
    {
        var message = CopilotEvents.Assistant(answer);
        return new ScriptedTurn([CopilotEvents.Usage(currentTokens, tokenLimit), message], message);
    }

    /// <summary>
    ///     Invokes the session's permission handler, asserting one is installed.
    /// </summary>
    /// <param name="config">The session whose handler to invoke.</param>
    /// <param name="request">The permission request to adjudicate.</param>
    /// <returns>The handler's decision.</returns>
    private static Task<PermissionDecision> Decide(CreateSessionOptions config, PermissionRequest request)
    {
        var handler = config.Permissions.OnRequest;
        Assert.NotNull(handler);
        return handler(request, null!);
    }

    /// <summary>
    ///     Builds a custom-tool permission request naming the given tool, filling the members the
    ///     SDK requires.
    /// </summary>
    /// <param name="toolName">The name of the tool the request concerns.</param>
    /// <returns>A custom-tool permission request.</returns>
    private static PermissionRequestCustomTool MakeCustomToolRequest(string toolName) => new()
    {
        ToolName = toolName,
        ToolCallId = "call-1",
        ToolDescription = "a tool",
    };

    /// <summary>
    ///     Builds a no-op tool carrying the given name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) =>
        AIFunctionFactory.Create(() => "ok", name);

    /// <summary>
    ///     A summarizer that consolidates deterministically and keeps what it was asked, so a
    ///     rotation in a test is a pure function of its input and the material can be asserted on.
    /// </summary>
    private sealed class RecordingSummarizer : ISummarizer
    {
        /// <summary>
        ///     The record every consolidation returns, distinctive enough to find in a seeded system
        ///     message.
        /// </summary>
        internal const string Record = "CONSOLIDATED-RECORD";

        /// <summary>
        ///     Gets the requests this summarizer was given, in order.
        /// </summary>
        internal List<ConsolidationRequest> Requests { get; } = [];

        /// <inheritdoc/>
        public Task<string> ConsolidateAsync(
            ConsolidationRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Requests.Add(request);
            return Task.FromResult(Record);
        }
    }
}


