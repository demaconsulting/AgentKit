using DemaConsulting.AgentKit.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     System-level integration tests for the AgentKitAgentsChatClient system.
/// </summary>
/// <remarks>
///     These tests exercise the behavioral guarantee the package exists for: an agent built by
///     the factory talks to its provider through an <see cref="ImagePromotingChatClient"/>, so a
///     tool-returned image reaches the model on a user message even on a provider that would
///     otherwise drop it. The decorator installation is asserted, not assumed.
/// </remarks>
public class AgentKitAgentsChatClientTests
{
    /// <summary>
    ///     A small image payload; its bytes are unimportant, only that the same instance is carried
    ///     through unchanged.
    /// </summary>
    private static readonly byte[] ImageBytes = [0x89, 0x50, 0x4E, 0x47];

    /// <summary>
    ///     Proves that the factory builds a usable agent carrying the supplied name and instructions.
    /// </summary>
    [Fact]
    public void AgentKitAgentsChatClient_Create_BuildsAgentCarryingSuppliedName()
    {
        // Arrange: a scripted client and a single tool
        var client = new ScriptedChatClient();
        var tools = new List<AIFunction> { MakeTool("tool_one") };

        // Act: build the agent through the factory
        var agent = ChatClientAgentFactory.Create(client, tools, instructions: "be concise", name: "assistant");

        // Assert: an agent was built and carries the supplied name
        Assert.NotNull(agent);
        Assert.Equal("assistant", agent.Name);
    }

    /// <summary>
    ///     Proves the whole point of the package: an agent built by the factory installs the
    ///     image-promoting decorator, so a tool-returned image is carried onto a user message the
    ///     provider receives. The factory wraps the supplied client through the same seam exercised
    ///     here, so the conversation the provider observes is promoted.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsChatClient_ImagePromotion_ToolReturnedImage_ReachesProviderOnUserMessage()
    {
        // Arrange: the client an agent built by the factory would talk through
        var provider = new ScriptedChatClient();
        var promotingClient = ChatClientAgentFactory.WrapWithImagePromotion(provider);

        var image = new DataContent(ImageBytes, "image/png");
        var conversation = new List<ChatMessage>
        {
            new(ChatRole.User, "look at the diagram"),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", image)]),
        };

        // Act: send the conversation as the function-invocation loop would, after the tool ran
        await promotingClient.GetResponseAsync(conversation, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the provider received an extra user message carrying the very same image instance
        Assert.Equal(3, provider.ReceivedMessages.Count);
        var promoted = provider.ReceivedMessages[2];
        Assert.Equal(ChatRole.User, promoted.Role);
        Assert.Contains(promoted.Contents.OfType<DataContent>(), part => ReferenceEquals(part, image));
    }

    /// <summary>
    ///     Proves that an agent the factory builds actually talks through the image-promoting
    ///     decorator: the decorator is found in the chat client chain of the constructed agent.
    /// </summary>
    /// <remarks>
    ///     The image-promotion scenario exercises the wrap seam directly and so cannot observe
    ///     whether <see cref="ChatClientAgentFactory.Create"/> still calls it. This scenario closes
    ///     that gap by asking the constructed agent itself for the decorator, so removing the wrap
    ///     from the construction path fails here.
    /// </remarks>
    [Fact]
    public void AgentKitAgentsChatClient_Create_InstallsImagePromotingDecoratorOnTheAgent()
    {
        // Arrange: a scripted client and a single tool
        var client = new ScriptedChatClient();
        var tools = new List<AIFunction> { MakeTool("tool_one") };

        // Act: build the agent through the factory and ask it for the decorator
        var agent = ChatClientAgentFactory.Create(client, tools);
        var decorator = agent.GetService(typeof(ImagePromotingChatClient));

        // Assert: the agent's chat client chain carries the decorator this package exists to install
        Assert.IsType<ImagePromotingChatClient>(decorator);
    }

    /// <summary>
    ///     Proves the package carries a Core session over an ordinary chat client: the session
    ///     answers, and the occupancy it reports is the provider's own count taken against the window
    ///     the application stated where it configured its provider.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsChatClient_Session_AnswersAndReportsTheProvidersOccupancy()
    {
        // Arrange: a chat client answering with a count no estimate would arrive at, and a session
        // over it through the adapter this package ships
        var provider = RecordingChatClient.Answering("the answer", inputTokens: 321);
        var factory = new ChatClientProviderSessionFactory(provider, windowTokens: 8000);
        var options = new AgentSessionOptions(
            new ChatClientSummarizer(RecordingChatClient.Answering("record", inputTokens: 10)),
            instructions: "be brief");
        await using var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Act: take one turn
        var response = await session.SendAsync("what changed?", TestContext.Current.CancellationToken);

        // Assert: the answer came back, and the session's account of the window is the provider's
        // figure against the stated window rather than anything this library computed
        Assert.Equal("the answer", response.Text);
        Assert.Equal(321, session.Usage.ConversationTokens);
        Assert.Equal(8000, session.Usage.WindowTokens);
    }

    /// <summary>
    ///     Proves the whole arrangement holds together over a chat client: the provider's own usage
    ///     drives the rotation, the consolidation goes out through a separate client, the record it
    ///     produces seeds the replacement session, and that replacement reports occupying nothing
    ///     until it has sent something.
    /// </summary>
    /// <remarks>
    ///     This is the scenario the session adapter exists for, and it is asserted end to end rather
    ///     than assumed from the unit behaviors: a chat client reporting a full window is the only
    ///     thing driving it, and the marker the summarizing client returns is found in the
    ///     conversation the provider is later sent.
    /// </remarks>
    [Fact]
    public async Task AgentKitAgentsChatClient_Session_RotatesOnTheProvidersUsage_AndSeedsTheReplacement()
    {
        // Arrange: a provider reporting a conversation well past the rotation threshold of its
        // window on every turn, and a separate client performing the consolidations
        var provider = RecordingChatClient.Answering("the answer", inputTokens: 800);
        var summarizing = RecordingChatClient.Answering("CONSOLIDATED-RECORD", inputTokens: 40);
        var factory = new ChatClientProviderSessionFactory(provider, windowTokens: 1000);
        var options = new AgentSessionOptions(
            new ChatClientSummarizer(summarizing),
            instructions: "be brief",
            verbatimTurns: 2);
        await using var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Act: hold a short conversation, every turn of which finds the window filling
        AgentSessionResponse? last = null;
        for (var turn = 0; turn < 4; turn++)
        {
            last = await session.SendAsync($"question {turn}", TestContext.Current.CancellationToken);
        }

        // Assert: the session rotated on what the provider reported, the consolidation went through
        // the summarizing client, the record it produced reached the provider as seeded history, and
        // the replacement reports occupying nothing because it has not been sent anything yet
        Assert.NotNull(last);
        Assert.True(last.RotationOccurred, "A provider reporting a filling window must provoke a rotation.");
        Assert.True(session.RotationCount > 0);
        Assert.NotEmpty(summarizing.Requests);
        Assert.Contains(
            provider.Requests,
            request => request.Messages.Any(
                message => message.Text.Contains("CONSOLIDATED-RECORD", StringComparison.Ordinal)));
        Assert.Equal(0, session.Usage.UsedTokens);
        Assert.Equal(1000, session.Usage.WindowTokens);
    }

    /// <summary>
    ///     Proves retention of large tool outputs works unchanged through a chat client: the model
    ///     receives a bounded preview in place of the oversized result, a call to the reader pages
    ///     the retained output back, and the transcript records only what the model was sent.
    /// </summary>
    /// <remarks>
    ///     Retention happens inside the tool, so nothing about it is specific to this adapter; this
    ///     scenario is the evidence that the function-invocation loop this package installs carries
    ///     the preview and the reader's page to the provider exactly as it carries any tool result.
    /// </remarks>
    [Fact]
    public async Task AgentKitAgentsChatClient_ToolOutput_PreviewReachesTheProviderAndTheReaderPagesIt()
    {
        // Arrange: an oversized tool composed with retention, and a provider that calls it, then
        // reads the last line of the retained output, then answers
        var report = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(ToolResult.Text(string.Join(
                "\n",
                Enumerable.Range(1, 5000).Select(n => "row " + n))))),
            "report_build",
            "Builds the full report.");
        var tools = ToolOutputSpill.Apply([report], ToolLimits.Default);
        var client = new RecordingChatClient()
            .Queue(RecordingChatClient.Answer(
                [new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "report_build", null)])],
                inputTokens: 100))
            .Queue(RecordingChatClient.Answer(
                [new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(
                    "call-2",
                    ToolOutputReadTool.ToolName,
                    new Dictionary<string, object?> { ["id"] = "output-1", ["startLine"] = 5000, ["lineCount"] = 1 })])],
                inputTokens: 200))
            .Queue("the report ends at row 5000", inputTokens: 300);
        var factory = new ChatClientProviderSessionFactory(client, windowTokens: 100_000);
        var seed = new ProviderSessionSeed(null, tools, []);

        // Act: take the turn
        await using var session = await factory.CreateAsync(seed, TestContext.Current.CancellationToken);
        var turn = await session.SendAsync("how does the report end?", TestContext.Current.CancellationToken);

        // Assert: the provider was sent the bounded preview, then the page, and the transcript holds
        // exactly those rather than the oversized output
        var preview = Assert.IsType<string>(ResultOf(client.Requests[1], "call-1"));
        var page = Assert.IsType<string>(ResultOf(client.Requests[2], "call-2"));
        Assert.StartsWith("[Output output-1 from report_build", preview, StringComparison.Ordinal);
        Assert.True(preview.Length <= ToolLimits.DefaultMaxResultCharacters);
        Assert.Equal("output-1 lines 5000-5000 of 5000\n5000| row 5000", page);
        var recorded = turn.Entries.Where(entry => entry.Kind == TranscriptEntryKind.ToolResult).Select(entry => entry.Text);
        Assert.Equal([preview, page], recorded);
    }

    /// <summary>
    ///     Proves a host that builds its own agent gets image promotion through the builder
    ///     extension: a <see cref="ChatClientAgent"/> over <c>client.AsBuilder().UseImagePromotion()</c>
    ///     delivers a tool-returned image to the provider on a user message, with a placeholder in
    ///     the tool result.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsChatClient_ImagePromotion_BuilderExtension_PromotesBeneathFunctionInvocation()
    {
        // Arrange: a provider that calls an image tool, then answers, behind a host-built agent
        var image = new DataContent(ImageBytes, "image/png");
        var tool = AIFunctionFactory.Create(() => image, "image_read", "Reads an image.");
        var provider = new RecordingChatClient()
            .Queue(RecordingChatClient.Answer(
                [new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "image_read")])],
                inputTokens: 10))
            .Queue("a chart", inputTokens: 20);
        var agent = new ChatClientAgent(
            provider.AsBuilder().UseImagePromotion().Build(),
            instructions: "You read images.",
            tools: [tool]);

        // Act: one run, in which the agent's own function-invocation loop calls the tool
        var response = await agent.RunAsync("What is in the image?", cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the answer came back, and the provider saw the image on a user message and only a
        // placeholder in the tool result
        Assert.Equal("a chart", response.Text);
        var second = provider.Requests[1].Messages;
        var result = second.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Single();
        Assert.StartsWith("[Image promoted", Assert.IsType<string>(result.Result), StringComparison.Ordinal);
        Assert.Contains(
            second.Where(message => message.Role == ChatRole.User).SelectMany(message => message.Contents),
            content => ReferenceEquals(content, image));
    }

    /// <summary>
    ///     Proves AgentKit tools reach a host-built agent through the context provider and run: the
    ///     provider's tools are offered on the request and the agent's loop invokes one of them.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsChatClient_ContextProvider_ToolsReachTheProviderAndRun()
    {
        // Arrange: a tool-less agent whose tools come from packs through the context provider, and a
        // provider that calls one of them
        var client = new RecordingChatClient()
            .Queue(ToolPackContextProviderTests.Call("call-1", "report_echo"))
            .Queue("the tool said echo", 1);
        var agent = ToolPackContextProviderTests.Agent(
            client,
            new ToolPackContextProvider(ToolPackContextProviderTests.Policy(), [new ToolPackContextProviderTests.ReportPack()]));

        // Act: one run
        var response = await agent.RunAsync("use the tool", cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the tools were offered, the call ran, and its result reached the provider
        Assert.Equal("the tool said echo", response.Text);
        Assert.Contains(client.Requests[0].Options!.Tools!, tool => tool.Name == "report_echo");
        Assert.Equal("echo", ToolPackContextProviderTests.ResultOf(client.Requests[1], "call-1"));
    }

    /// <summary>
    ///     Proves conversations composed through the context provider are isolated: a retained
    ///     output's reference from one conversation is unknown in another.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsChatClient_ContextProvider_ConversationsAreIsolated()
    {
        // Arrange: one agent, two conversations; the first retains a report
        var client = new RecordingChatClient()
            .Queue(ToolPackContextProviderTests.Call("call-1", "report_build"))
            .Queue("retained", 1)
            .Queue(ToolPackContextProviderTests.Call("call-2", ToolOutputReadTool.ToolName, new() { ["id"] = "output-1" }))
            .Queue("unknown", 1);
        var agent = ToolPackContextProviderTests.Agent(client, ToolPackContextProviderTests.RetainingProvider());
        var first = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        var second = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act: retain in the first, read in the second
        await agent.RunAsync("build", first, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("read", second, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the second conversation never saw the first's output
        Assert.Contains("never retained", ToolPackContextProviderTests.ResultOf(client.Requests[3], "call-2"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a conversation's retained outputs survive the agent persisting and reloading it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsChatClient_ContextProvider_DeserializedSession_PagesAnEarlierOutput()
    {
        // Arrange: a conversation that retains a report
        var client = new RecordingChatClient()
            .Queue(ToolPackContextProviderTests.Call("call-1", "report_build"))
            .Queue("retained", 1)
            .Queue(ToolPackContextProviderTests.Call(
                "call-2",
                ToolOutputSearchTool.ToolName,
                new() { ["id"] = "output-1", ["pattern"] = "report row 250" }))
            .Queue("found", 1);
        var agent = ToolPackContextProviderTests.Agent(client, ToolPackContextProviderTests.RetainingProvider());
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await agent.RunAsync("build", session, cancellationToken: TestContext.Current.CancellationToken);

        // Act: persist, reload as a new process would, and search the earlier reference
        var persisted = await agent.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        var reloaded = await agent.DeserializeSessionAsync(persisted, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("search", reloaded, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the search ran against the restored output
        Assert.Contains("250:report row 250", ToolPackContextProviderTests.ResultOf(client.Requests[3], "call-2"), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves AgentKit's chat reducer plugs into the Agent Framework's in-memory history provider
    ///     and reduces the stored history through the summarizer.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsChatClient_ChatReducer_InMemoryHistoryProvider_ReducesStoredHistory()
    {
        // Arrange: an agent whose history provider reduces before retrieval through AgentKit's reducer
        var summarizing = RecordingChatClient.Answering("The vault code is 4417-BRAVO.", inputTokens: 5);
        var history = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
        {
            ChatReducer = new AgentKitChatReducer(new ChatClientSummarizer(summarizing), thresholdCharacters: 200, verbatimTurns: 1),
            ReducerTriggerEvent = InMemoryChatHistoryProviderOptions.ChatReducerTriggerEvent.BeforeMessagesRetrieval
        });
        var agent = new ChatClientAgent(
            RecordingChatClient.Answering(new string('x', 150), inputTokens: 1),
            new ChatClientAgentOptions { Name = "reducing", ChatHistoryProvider = history });
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act: four turns, each answered at length
        foreach (var message in new[] { "Remember 4417-BRAVO.", "Topic one.", "Topic two.", "What was the code?" })
        {
            await agent.RunAsync(message, session, cancellationToken: TestContext.Current.CancellationToken);
        }

        // Assert: the summarizer consolidated, and the stored history holds one record
        var stored = history.GetMessages(session);
        Assert.NotEmpty(summarizing.Requests);
        Assert.Single(stored, message => message.Text.StartsWith(AgentKitChatReducer.RecordHeader, StringComparison.Ordinal));
        Assert.Contains("4417-BRAVO", stored.First(message => message.Text.StartsWith(AgentKitChatReducer.RecordHeader, StringComparison.Ordinal)).Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves, at the HTTP wire, that over OllamaSharp a promoted image leaves no bytes in the tool
    ///     message: the tool message is a short placeholder and the image travels once, in the
    ///     following user message's images.
    /// </summary>
    /// <remarks>
    ///     OllamaSharp serializes a tool result's image as base64 text in the tool message; without the
    ///     placeholder a 316 KB image became a 421,629-character tool message. The real OllamaSharp
    ///     chat client runs here over a capturing handler, so the request body asserted on is exactly
    ///     what an Ollama server would receive.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsChatClient_ImagePromotion_OverOllamaSharp_ToolMessageCarriesNoImageBytes()
    {
        // Arrange: a 300 KB image tool, an OllamaSharp client over a handler that answers first with a
        // call to the tool and then with text, and a host-built agent with image promotion installed
        var image = new DataContent(new byte[300_000], "image/png");
        var tool = AIFunctionFactory.Create(() => image, "image_read", "Reads an image.");
        var handler = new CapturingHttpHandler()
            .Respond("""{"model":"m","created_at":"2026-01-01T00:00:00Z","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"image_read","arguments":{}}}]},"done":true,"done_reason":"stop","prompt_eval_count":10,"eval_count":1}""")
            .Respond("""{"model":"m","created_at":"2026-01-01T00:00:01Z","message":{"role":"assistant","content":"7319"},"done":true,"done_reason":"stop","prompt_eval_count":20,"eval_count":1}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") };
        using var ollama = new OllamaSharp.OllamaApiClient(http, "m");
        var agent = new ChatClientAgent(
            ((IChatClient)ollama).AsBuilder().UseImagePromotion().Build(),
            instructions: "Read images.",
            tools: [tool]);

        // Act: one run, in which the agent calls the tool and sends its result back
        var response = await agent.RunAsync("What number is in the image?", cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the second request's tool message is a short placeholder with no base64, and the
        // following user message carries the image once
        Assert.Equal("7319", response.Text);
        using var body = System.Text.Json.JsonDocument.Parse(handler.RequestBodies[1]);
        var messages = body.RootElement.GetProperty("messages").EnumerateArray().ToList();
        var toolIndex = messages.FindIndex(message => message.GetProperty("role").GetString() == "tool");
        var toolContent = messages[toolIndex].GetProperty("content").GetString()!;
        Assert.True(toolContent.Length < 300, $"The tool message was {toolContent.Length} characters.");
        Assert.DoesNotContain("base64", toolContent, StringComparison.Ordinal);
        Assert.Contains("image/png", toolContent, StringComparison.Ordinal);
        var promoted = messages[toolIndex + 1];
        Assert.Equal("user", promoted.GetProperty("role").GetString());
        Assert.Equal(1, promoted.GetProperty("images").GetArrayLength());
    }

    /// <summary>
    ///     Finds the result a request carried back for one tool call.
    /// </summary>
    /// <param name="request">The recorded request.</param>
    /// <param name="callId">The tool call's identifier.</param>
    /// <returns>The result the function-invocation loop sent for that call.</returns>
    private static object? ResultOf(RecordingChatClient.RecordedRequest request, string callId) =>
        request.Messages
            .SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Single(content => content.CallId == callId)
            .Result;

    /// <summary>
    ///     Builds a no-op tool carrying the given name, for exercising the factory.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) =>
        AIFunctionFactory.Create(() => "ok", name);
}
