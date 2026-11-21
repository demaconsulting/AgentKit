using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     Unit tests for the <see cref="ToolPackContextProvider"/> class.
/// </summary>
/// <remarks>
///     <para>
///     The provider is exercised through a real <see cref="ChatClientAgent"/> over a scripted
///     <see cref="RecordingChatClient"/>, because what it promises is only observable there: that the
///     agent offers its tools on each request, that the agent's own function-invocation loop runs
///     them, and that its retention travels with a session the agent serializes and deserializes.
///     The recording client is the provider's documented test double; the agent, the session state
///     bag and the retention are real.
///     </para>
///     <para>
///     Tools come from a small pack written here rather than from the Tools package, so the scenarios
///     depend only on this unit, Core and the Agent Framework.
///     </para>
/// </remarks>
public class ToolPackContextProviderTests
{
    /// <summary>
    ///     The instructions every scenario contributes.
    /// </summary>
    private const string Instructions = "Use the report tools.";

    /// <summary>
    ///     Proves the tools and the instructions the provider contributes reach the chat client on the
    ///     run's request.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolPackContextProvider_Run_ToolsAndInstructionsReachTheChatClient()
    {
        // Arrange: a tool-less agent whose tools come only from the provider
        var client = RecordingChatClient.Answering("done", inputTokens: 1);
        var provider = new ToolPackContextProvider(Policy(), [new ReportPack()], new ToolPackContextProviderOptions { Instructions = Instructions });
        var agent = Agent(client, provider);

        // Act: one run
        await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the request offered the pack's tools and carried the instructions
        var options = client.Requests[0].Options!;
        Assert.Equal(["report_build", "report_echo"], options.Tools!.Select(tool => tool.Name));
        Assert.Contains(Instructions, options.Instructions, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the tools are composed once per conversation, not once per run.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolPackContextProvider_TwoRunsOneSession_ComposeOnce()
    {
        // Arrange: a provider counting its compositions
        var compositions = 0;
        var provider = new ToolPackContextProvider(
            () =>
            {
                compositions++;
                return [ReportPack.Echo()];
            },
            ToolLimits.Default);
        var agent = Agent(RecordingChatClient.Answering("done", 1), provider);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act: two runs of one conversation, then one run of another
        await agent.RunAsync("one", session, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("two", session, cancellationToken: TestContext.Current.CancellationToken);
        var other = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await agent.RunAsync("three", other, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: one composition per conversation
        Assert.Equal(2, compositions);
    }

    /// <summary>
    ///     Proves two conversations have their own retention: a reference issued in one is never
    ///     readable in the other.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolPackContextProvider_TwoSessions_HaveIsolatedRetention()
    {
        // Arrange: the first conversation retains a report, the second asks for its reference
        var client = new RecordingChatClient()
            .Queue(Call("call-1", "report_build"))
            .Queue("retained", 1)
            .Queue(Call("call-2", ToolOutputReadTool.ToolName, new() { ["id"] = "output-1" }))
            .Queue("not found", 1);
        var agent = Agent(client, RetainingProvider());
        var first = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        var second = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act: one run in each conversation
        await agent.RunAsync("build", first, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("read", second, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the first retained output-1; the second never issued it
        Assert.StartsWith("[Output output-1 from report_build", ResultOf(client.Requests[1], "call-1"), StringComparison.Ordinal);
        Assert.Contains("never retained", ResultOf(client.Requests[3], "call-2"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a retained output survives the agent serializing and deserializing the session: the
    ///     reloaded conversation pages the reference it was given before.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolPackContextProvider_SerializeDeserializeSession_RetainedOutputPagesAfterRestore()
    {
        // Arrange: a conversation that retains a report, then is persisted and reloaded
        var client = new RecordingChatClient()
            .Queue(Call("call-1", "report_build"))
            .Queue("retained", 1)
            .Queue(Call("call-2", ToolOutputReadTool.ToolName, new() { ["id"] = "output-1", ["startLine"] = 300, ["lineCount"] = 1 }))
            .Queue("paged", 1);
        var agent = Agent(client, RetainingProvider());
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await agent.RunAsync("build", session, cancellationToken: TestContext.Current.CancellationToken);

        // Act: serialize to JSON text, reload, and read the earlier reference
        var json = (await agent.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken)).GetRawText();
        using var document = JsonDocument.Parse(json);
        var reloaded = await agent.DeserializeSessionAsync(document.RootElement.Clone(), cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("read", reloaded, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the state was in the serialized session, and the page is the retained content
        Assert.Contains(ToolPackContextProviderOptions.DefaultStateKey, json, StringComparison.Ordinal);
        Assert.Equal("output-1 lines 300-300 of 300\n300| report row 300", ResultOf(client.Requests[3], "call-2"));
    }

    /// <summary>
    ///     Proves a retained output is persisted even when the run that retained it fails: the
    ///     reloaded conversation still pages the reference the failed run issued.
    /// </summary>
    /// <remarks>
    ///     The framework skips a failed run by default when it notifies a context provider, yet a run
    ///     that fails after a tool retained output has already issued a reference the model may
    ///     repeat in a later run. The failure is a distinct exception type, so a scripting mistake
    ///     that made the recording client refuse an unscripted request cannot pass as the expected
    ///     failure.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolPackContextProvider_FailedRunAfterRetention_SnapshotStillPersisted()
    {
        // Arrange: a run that retains a report and then fails, followed by a run reading it
        var client = new RecordingChatClient()
            .Queue(Call("call-1", "report_build"))
            .QueueFailure(new HttpRequestException("provider unavailable"))
            .Queue(Call("call-2", ToolOutputReadTool.ToolName, new() { ["id"] = "output-1", ["startLine"] = 300, ["lineCount"] = 1 }))
            .Queue("paged", 1);
        var agent = Agent(client, RetainingProvider());
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act: the failing run, then serialize to JSON text, reload, and read the earlier reference
        await Assert.ThrowsAsync<HttpRequestException>(
            () => agent.RunAsync("build", session, cancellationToken: TestContext.Current.CancellationToken));
        var json = (await agent.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken)).GetRawText();
        using var document = JsonDocument.Parse(json);
        var reloaded = await agent.DeserializeSessionAsync(document.RootElement.Clone(), cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("read", reloaded, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the failed run retained the report, the state was in the serialized session, and
        // the page is the retained content
        Assert.StartsWith("[Output output-1 from report_build", ResultOf(client.Requests[1], "call-1"), StringComparison.Ordinal);
        Assert.Contains(ToolPackContextProviderOptions.DefaultStateKey, json, StringComparison.Ordinal);
        Assert.Equal("output-1 lines 300-300 of 300\n300| report row 300", ResultOf(client.Requests[3], "call-2"));
    }

    /// <summary>
    ///     Proves that with retained outputs not persisted, a reference made before a reload is
    ///     answered "no longer retained" and never re-issued.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolPackContextProvider_PersistDisabled_EarlierReferenceAnsweredNoLongerRetained()
    {
        // Arrange: a provider persisting only the counter
        var client = new RecordingChatClient()
            .Queue(Call("call-1", "report_build"))
            .Queue("retained", 1)
            .Queue(Call("call-2", ToolOutputReadTool.ToolName, new() { ["id"] = "output-1" }))
            .Queue("gone", 1);
        var agent = Agent(client, RetainingProvider(persist: false));
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await agent.RunAsync("build", session, cancellationToken: TestContext.Current.CancellationToken);

        // Act: reload and read the earlier reference
        var serialized = await agent.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        var reloaded = await agent.DeserializeSessionAsync(serialized, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("read", reloaded, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the provider persisted the counter and no retained output, and the reference is
        // honestly answered
        Assert.Contains("\"lastIssued\":1,\"outputs\":[]", serialized.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("no longer retained", ResultOf(client.Requests[3], "call-2"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the delegate constructor bounds a foreign tool by the supplied ceilings.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolPackContextProvider_DelegateConstructor_ForeignToolIsGuarded()
    {
        // Arrange: a foreign tool returning far more than a 1,500-character ceiling, no retention
        var foreign = AIFunctionFactory.Create(
            () => string.Join("\n", Enumerable.Range(1, 300).Select(n => "row " + n + " of the report")),
            "report_rows",
            "Returns the rows.");
        var client = new RecordingChatClient().Queue(Call("call-1", "report_rows")).Queue("done", 1);
        var provider = new ToolPackContextProvider(() => [foreign], new ToolLimits(maxResultCharacters: 1_500));
        var agent = Agent(client, provider);

        // Act: one run calling the tool
        await agent.RunAsync("rows", cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the oversized result was refused before it reached the provider
        Assert.StartsWith("Denied (ResourceTooLarge)", ResultOf(client.Requests[1], "call-1"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the provider refuses to run for an agent that would ignore the tools it contributes.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolPackContextProvider_NonChatClientAgent_ThrowsInvalidOperationException()
    {
        // Arrange: a provider and an agent that is not a chat-client agent
        var provider = new ToolPackContextProvider(() => [ReportPack.Echo()], ToolLimits.Default);
        var agent = new OtherAgent();

        // Act / Assert: the run is refused, naming the reason. Only an agent run constructs this
        // context in production; the framework marks its constructor for evaluation, and the test
        // project acknowledges that diagnostic because this test stands in for the agent.
        var context = new AIContextProvider.InvokingContext(agent, session: null, new AIContext());
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.InvokingAsync(context, TestContext.Current.CancellationToken));
        Assert.Contains("ChatClientAgent", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the provider declares the configured state key, so the framework can detect two
    ///     providers writing to one key.
    /// </summary>
    [Fact]
    public void ToolPackContextProvider_StateKeys_ContainsConfiguredKey()
    {
        // Arrange / Act: a provider with a custom key
        var provider = new ToolPackContextProvider(
            () => [ReportPack.Echo()],
            ToolLimits.Default,
            new ToolPackContextProviderOptions { StateKey = "custom.key" });

        // Assert: exactly that key
        Assert.Equal(["custom.key"], provider.StateKeys);
    }

    /// <summary>
    ///     Proves invalid construction is refused where the host wrote it.
    /// </summary>
    [Fact]
    public void ToolPackContextProvider_Constructor_InvalidArguments_Throw()
    {
        // Arrange: a valid policy and pack
        var policy = Policy();
        var tiny = new ToolLimits(maxResultCharacters: 500);

        // Act / Assert: each invalid argument is refused
        Assert.Throws<ArgumentNullException>(() => new ToolPackContextProvider(null!, [new ReportPack()]));
        Assert.Throws<ArgumentNullException>(() => new ToolPackContextProvider(policy, (IEnumerable<IToolPack>)null!));
        Assert.Throws<ArgumentException>(() => new ToolPackContextProvider(policy, []));
        Assert.Throws<ArgumentException>(() => new ToolPackContextProvider(policy, [new ReportPack(HostCapabilities.Vision)]));
        Assert.Throws<ArgumentNullException>(() => new ToolPackContextProvider(null!, ToolLimits.Default));
        Assert.Throws<ArgumentNullException>(() => new ToolPackContextProvider(() => [], null!));
        Assert.Throws<ArgumentException>(() => new ToolPackContextProvider(
            () => [], ToolLimits.Default, new ToolPackContextProviderOptions { StateKey = " " }));
        Assert.Throws<ArgumentException>(() => new ToolPackContextProvider(
            () => [], tiny, new ToolPackContextProviderOptions { ToolOutput = ToolOutputOptions.Default }));
    }

    /// <summary>
    ///     Proves no reader tools are contributed when retention is not configured.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolPackContextProvider_WithoutToolOutput_NoReaderTools()
    {
        // Arrange: a provider with no retention
        var client = RecordingChatClient.Answering("done", 1);
        var agent = Agent(client, new ToolPackContextProvider(Policy(), [new ReportPack()]));

        // Act: one run
        await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        // Assert: only the pack's tools
        Assert.DoesNotContain(client.Requests[0].Options!.Tools!, tool => tool.Name.StartsWith("tool_output", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves a tool a downstream client adds to one run's request never reaches the next run of
    ///     the conversation, because each run is contributed a fresh list.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ToolPackContextProvider_RequestToolsChangedDownstream_NextRunOffersComposedTools()
    {
        // Arrange: a client beneath the agent that records the tools offered, then adds one of its own
        var client = new ToolAddingChatClient(RecordingChatClient.Answering("done", 1));
        var agent = Agent(client, new ToolPackContextProvider(Policy(), [new ReportPack()]));
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act: two runs of one conversation
        await agent.RunAsync("one", session, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("two", session, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: both runs were offered exactly the composed tools
        Assert.Equal(2, client.Seen.Count);
        Assert.Equal(["report_build", "report_echo"], client.Seen[0]);
        Assert.Equal(["report_build", "report_echo"], client.Seen[1]);
    }

    /// <summary>
    ///     Builds a tool-less chat-client agent whose tools come from the provider.
    /// </summary>
    /// <param name="client">The chat client the agent talks to.</param>
    /// <param name="provider">The provider under test.</param>
    /// <returns>The agent.</returns>
    internal static ChatClientAgent Agent(IChatClient client, AIContextProvider provider) =>
        new(client, new ChatClientAgentOptions { Name = "test-agent", AIContextProviders = [provider] });

    /// <summary>
    ///     A policy over the temporary directory with the smallest ceiling retention accepts.
    /// </summary>
    /// <returns>The policy.</returns>
    internal static PathPolicy Policy() => new(
        Path.GetTempPath(),
        [PathRule.ReadOnly(Path.GetTempPath())],
        new ToolLimits(maxResultCharacters: ToolOutputSpill.MinimumResultCharacters));

    /// <summary>
    ///     A provider composing the report pack with retention.
    /// </summary>
    /// <param name="persist">Whether retained outputs are persisted with the session.</param>
    /// <returns>The provider.</returns>
    internal static ToolPackContextProvider RetainingProvider(bool persist = true) => new(
        Policy(),
        [new ReportPack()],
        new ToolPackContextProviderOptions { ToolOutput = ToolOutputOptions.Default, PersistRetainedOutputs = persist });

    /// <summary>
    ///     A scripted provider answer that calls one tool.
    /// </summary>
    /// <param name="callId">The call identifier.</param>
    /// <param name="name">The tool to call.</param>
    /// <param name="arguments">The call's arguments, or null for none.</param>
    /// <returns>The answer.</returns>
    internal static ChatResponse Call(string callId, string name, Dictionary<string, object?>? arguments = null) =>
        RecordingChatClient.Answer(
            [new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)])],
            inputTokens: 1);

    /// <summary>
    ///     Finds the text result a request carried back for one tool call.
    /// </summary>
    /// <param name="request">The recorded request.</param>
    /// <param name="callId">The tool call's identifier.</param>
    /// <returns>The result's text.</returns>
    internal static string ResultOf(RecordingChatClient.RecordedRequest request, string callId)
    {
        var result = request.Messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single(content => content.CallId == callId)
            .Result;
        return result switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()!,
            _ => result?.ToString() ?? string.Empty
        };
    }

    /// <summary>
    ///     A pack publishing a 300-line report tool and a small echo tool.
    /// </summary>
    /// <param name="required">The capabilities the pack requires.</param>
    internal sealed class ReportPack(HostCapabilities required = HostCapabilities.None) : IToolPack
    {
        /// <summary>
        ///     Gets the family prefix.
        /// </summary>
        public string FamilyPrefix => "report";

        /// <summary>
        ///     Gets the capabilities the pack requires.
        /// </summary>
        public HostCapabilities RequiredCapabilities => required;

        /// <summary>
        ///     Creates a small tool echoing a fixed answer.
        /// </summary>
        /// <returns>The tool.</returns>
        public static AIFunction Echo() => GuardedToolFactory.Create(
            (Func<object>)(() => ToolResult.Text("echo")),
            "report_echo",
            "Echoes.");

        /// <summary>
        ///     Creates the pack's tools afresh.
        /// </summary>
        /// <param name="policy">The policy, unused by these tools.</param>
        /// <returns>The report and echo tools.</returns>
        public IEnumerable<AIFunction> CreateTools(PathPolicy policy) =>
        [
            GuardedToolFactory.Create(
                (Func<object>)(() => ToolResult.Text(string.Join("\n", Enumerable.Range(1, 300).Select(n => "report row " + n)))),
                "report_build",
                "Builds the report."),
            Echo()
        ];
    }

    /// <summary>
    ///     A middleware-style client that records the tools each request offers, then adds a tool of
    ///     its own to that request, as a middleware extending a request's tools would.
    /// </summary>
    /// <param name="inner">The client requests are forwarded to.</param>
    private sealed class ToolAddingChatClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        /// <summary>
        ///     Gets the tool names each request offered, before this client added its own.
        /// </summary>
        public List<List<string>> Seen { get; } = [];

        /// <inheritdoc/>
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Seen.Add(options?.Tools?.Select(tool => tool.Name).ToList() ?? []);
            options?.Tools?.Add(AIFunctionFactory.Create(() => "x", "injected_tool"));
            return base.GetResponseAsync(messages, options, cancellationToken);
        }
    }

    /// <summary>
    ///     An agent that is not a chat-client agent, so it would ignore contributed tools.
    /// </summary>
    private sealed class OtherAgent : AIAgent
    {
        /// <inheritdoc/>
        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc/>
        protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc/>
        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            JsonElement serializedState,
            JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc/>
        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc/>
        protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
