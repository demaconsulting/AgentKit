using DemaConsulting.AgentKit.Agents.Copilot;
using DemaConsulting.AgentKit.Core;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
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
        Assert.Equal(expectedNames, config.AvailableTools);
        Assert.Equal(tools.Count, config.Tools!.Count);
        Assert.Equal(expectedNames, config.Tools.Select(tool => tool.Name).ToList());
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
                Assert.True(config.InfiniteSessions!.BackgroundCompactionThreshold > 0.90);
                Assert.False(config.InfiniteSessions.Enabled);
            });
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
    private static Task<PermissionDecision> Decide(SessionConfig config, PermissionRequest request)
    {
        var handler = config.OnPermissionRequest;
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


