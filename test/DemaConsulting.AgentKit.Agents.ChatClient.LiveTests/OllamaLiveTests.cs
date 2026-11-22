using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.TextFile;
using DemaConsulting.AgentKit.Tools.Visual.Image;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient.LiveTests;

/// <summary>
///     Live scenarios for AgentKit's tools in a host-built Agent Framework agent over Ollama.
/// </summary>
/// <remarks>
///     <para>
///     Supplementary verification evidence: each scenario shows one of the AITool-first building
///     blocks holding against a real model — the context provider injecting tools per conversation
///     and restoring retention across a session reload, the guard bounding a foreign tool, image
///     promotion delivering an image with a small tool message, and the chat reducer consolidating a
///     history the agent owns. Wherever possible the assertion is on what was sent on the wire rather
///     than on the model's prose, which varies between runs.
///     </para>
///     <para>
///     Every scenario skips unless <c>AGENTKIT_OLLAMA_LIVE_MODEL</c> names the model to use. The suite
///     is outside the solution and the merge gate's test run, and is not linked to the requirements
///     trace, because it never runs in the pipeline that builds the trace matrix.
///     </para>
/// </remarks>
[Collection(OllamaLiveCollection.Name)]
public sealed class OllamaLiveTests
{
    /// <summary>
    ///     The result ceiling every scenario runs under, small enough that a broad search is retained.
    /// </summary>
    private static readonly ToolLimits Limits = new(maxResultCharacters: 1_500);

    /// <summary>
    ///     The run's shared state.
    /// </summary>
    private readonly OllamaLiveFixture _live;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OllamaLiveTests"/> class.
    /// </summary>
    /// <param name="live">The run's shared state.</param>
    public OllamaLiveTests(OllamaLiveFixture live)
    {
        _live = live;
    }

    /// <summary>
    ///     L1: AgentKit tools reach a tool-less agent through the context provider; a search over a
    ///     named file is retained; a denial names no host path; and after the agent serializes and
    ///     deserializes the session, the earlier reference still pages.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsChatClientLive_ContextProvider_ToolsRetentionAndRestore()
    {
        // Arrange: a tool-less agent whose tools come from packs through the context provider
        _live.RequireOptIn();
        var policy = new PathPolicy(_live.Sandbox, [PathRule.ReadOnly(_live.Sandbox)], Limits);
        var provider = new ToolPackContextProvider(
            policy,
            [new TextFilePack(), new FilePack()],
            new ToolPackContextProviderOptions
            {
                Instructions = "File tools are available. Paths are relative to the workspace. If a tool output is "
                               + "retained with a reference, use tool_output_read or tool_output_search on it.",
                ToolOutput = new ToolOutputOptions(previewCharacters: 600)
            });
        var (ollama, wire) = _live.CreateClient();
        using var client = ollama;
        AIAgent agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            Name = "live-l1",
            ChatOptions = new ChatOptions { Instructions = "You are a concise assistant." },
            AIContextProviders = [provider]
        });
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act and assert, turn by turn
        var read = await TurnAsync(agent, session, "L1-read", "Read notes.txt and tell me the project codename.");
        Assert.Contains(OllamaLiveFixture.Codeword, read, StringComparison.OrdinalIgnoreCase);
        Assert.True(wire.ToolCounts[0] >= 4, $"The first request offered {wire.ToolCounts[0]} tools.");

        var before = wire.Requests.Count;
        var search = await TurnAsync(
            agent,
            session,
            "L1-search",
            "Call text_file_search with exactly these arguments: pattern='ERROR', path='big.log'. Then tell me the "
            + "order number on the line containing code=E07. If the result is retained, call tool_output_search "
            + "with its id and pattern 'code=E07' to find that line.");
        var retained = ToolMessagesSince(wire, before).FirstOrDefault(message => message.Contains("[Output output-1 from text_file_search", StringComparison.Ordinal));
        Assert.NotNull(retained);
        _live.Log($"L1 retained preview length={retained.Length}");
        Assert.Contains("ORD-00490", search, StringComparison.Ordinal);

        before = wire.Requests.Count;
        var denied = await TurnAsync(
            agent,
            session,
            "L1-denied",
            "Call text_file_read with path '../outside/secret.txt' and tell me exactly what the tool returned.");
        var denial = Assert.Single(ToolMessagesSince(wire, before), message => message.Contains("Denied (PathNotPermitted)", StringComparison.Ordinal));
        Assert.Contains("(relative to the working directory)", denial, StringComparison.Ordinal);
        Assert.DoesNotContain(_live.Root, denial, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(OllamaLiveFixture.Secret, denied, StringComparison.Ordinal);

        var persisted = await agent.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken);
        var reloaded = await agent.DeserializeSessionAsync(persisted, cancellationToken: TestContext.Current.CancellationToken);
        _live.Log($"L1 serialized session: {persisted.GetRawText().Length} characters");
        before = wire.Requests.Count;
        await TurnAsync(
            agent,
            reloaded,
            "L1-restored",
            "Call tool_output_read with id 'output-1', startLine 7, lineCount 1 and quote exactly what it returns.");
        Assert.Contains(ToolMessagesSince(wire, before), message => message.Contains("code=E07", StringComparison.Ordinal));
    }

    /// <summary>
    ///     L2: a foreign tool built with the plain function factory is bounded by <c>AsGuarded</c> —
    ///     refused without retention, retained and searched with it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsChatClientLive_AsGuarded_ForeignToolIsBounded()
    {
        // Arrange: a 300-row report tool that bounds nothing itself
        _live.RequireOptIn();
        var report = AIFunctionFactory.Create(
            (int rows) => Enumerable.Range(1, rows).Select(n => new { row = n, square = n * n }),
            "report_rows",
            "Returns one row per number with its square.");
        var unbounded = (JsonElement)(await report.InvokeAsync(new AIFunctionArguments { ["rows"] = 300 }, TestContext.Current.CancellationToken))!;
        _live.Log($"L2 unbounded report: {unbounded.GetRawText().Length} characters against a {Limits.MaxResultCharacters}-character limit");
        Assert.True(unbounded.GetRawText().Length > Limits.MaxResultCharacters);

        // Act (a): bounded, no retention
        var (refusingClient, refusingWire) = _live.CreateClient();
        using (refusingClient)
        {
            var refusing = new ChatClientAgent(refusingClient, instructions: "You are a concise assistant.", tools: [report.AsGuarded(Limits)]);
            await TurnAsync(refusing, null, "L2-refused", "Call report_rows with rows=300 and tell me what the tool returned.");

            // Assert (a): the provider was sent the refusal, within the ceiling
            var refusal = Assert.Single(ToolMessagesSince(refusingWire, 0));
            _live.Log($"L2 refused tool message length={refusal.Length}");
            Assert.Contains("Denied (ResourceTooLarge)", refusal, StringComparison.Ordinal);
            Assert.True(refusal.Length <= Limits.MaxResultCharacters);
        }

        // Act (b): bounded and retained
        var retention = new ToolOutputRetention(Limits, new ToolOutputOptions(previewCharacters: 600));
        var (retainingClient, retainingWire) = _live.CreateClient();
        using (retainingClient)
        {
            var retaining = new ChatClientAgent(
                retainingClient,
                instructions: "You are a concise assistant.",
                tools: [retention.Wrap(report.AsGuarded(Limits)), .. retention.ReaderTools]);
            var answer = await TurnAsync(
                retaining,
                null,
                "L2-retained",
                "Call report_rows with rows=300. Its output will be retained; then call tool_output_search with the id "
                + "from the preview, pattern '\"row\": 137,' and contextLines 1, and tell me the square of row 137.");

            // Assert (b): a preview within the ceiling was sent, and the model paged to the answer
            var preview = ToolMessagesSince(retainingWire, 0)[0];
            _live.Log($"L2 retained preview length={preview.Length}");
            Assert.Contains("[Output output-1 from report_rows", preview, StringComparison.Ordinal);
            Assert.True(preview.Length <= Limits.MaxResultCharacters + 200);
            Assert.Contains("18769", answer.Replace(",", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     L3: with <c>UseImagePromotion</c>, an image a tool returns reaches the model in a user
    ///     message's images and the tool message is a short placeholder; the model reads the number.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsChatClientLive_ImagePromotion_SmallToolMessageAndCorrectAnswer()
    {
        // Arrange: image_read over the sandbox, in a host-built agent with image promotion
        _live.RequireOptIn();
        var (ollama, wire) = _live.CreateClient();
        using var client = ollama;
        var agent = new ChatClientAgent(
            ((IChatClient)client).AsBuilder().UseImagePromotion().Build(),
            instructions: "You are a concise assistant.",
            tools: [.. ImageTools()]);

        // Act
        var answer = await TurnAsync(agent, null, "L3-promoted", ImagePrompt);

        // Assert: the tool message is small and carries no base64; the next user message carries the image
        var (tool, promoted) = ToolMessageAndNext(wire);
        _live.Log($"L3 image={_live.ImageBytes} bytes; tool message length={tool.Content.Length}; following user message images={promoted.Images}");
        Assert.False(tool.Base64InText);
        Assert.True(tool.Content.Length < 1_000, $"The tool message was {tool.Content.Length} characters.");
        Assert.Equal("user", promoted.Role);
        Assert.Equal(1, promoted.Images);
        Assert.Contains(OllamaLiveFixture.ImageNumber, answer, StringComparison.Ordinal);
    }

    /// <summary>
    ///     L3 control: without image promotion, OllamaSharp sends the tool-returned image as base64 text
    ///     in the tool message, and no image reaches the model's images.
    /// </summary>
    /// <remarks>
    ///     Records the size of the defect promotion exists to remove; the answer is logged, not asserted,
    ///     because without the image the model can only guess.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsChatClientLive_ImagePromotion_Control_WithoutPromotionToolMessageCarriesBase64()
    {
        // Arrange: the same agent without image promotion
        _live.RequireOptIn();
        var (ollama, wire) = _live.CreateClient();
        using var client = ollama;
        var agent = new ChatClientAgent(client, instructions: "You are a concise assistant.", tools: [.. ImageTools()]);

        // Act
        await TurnAsync(agent, null, "L3-control", ImagePrompt);

        // Assert: the image was sent as text in the tool message, and in no message's images
        var (tool, _) = ToolMessageAndNext(wire);
        _live.Log($"L3 control: image={_live.ImageBytes} bytes; tool message length={tool.Content.Length}; base64InText={tool.Base64InText}");
        Assert.True(tool.Base64InText);
        Assert.True(tool.Content.Length > _live.ImageBytes);
        Assert.All(wire.Requests[^1], message => Assert.Equal(0, message.Images));
    }

    /// <summary>
    ///     L4: AgentKit's chat reducer, in the Agent Framework's in-memory history provider, reduces a
    ///     conversation's stored history and the agent still recalls a fact from its first turn.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsChatClientLive_ChatReducer_ReducesAndKeepsTheFact()
    {
        // Arrange: a summarizer over a separate client, counted, and a 500-character threshold
        _live.RequireOptIn();
        var (summaryClient, _) = _live.CreateClient();
        using var summarizing = summaryClient;
        var summarizer = new CountingSummarizer(new ChatClientSummarizer(summarizing));
        var history = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
        {
            ChatReducer = new AgentKitChatReducer(summarizer, thresholdCharacters: 500, verbatimTurns: 1),
            ReducerTriggerEvent = InMemoryChatHistoryProviderOptions.ChatReducerTriggerEvent.BeforeMessagesRetrieval
        });
        var (ollama, _) = _live.CreateClient();
        using var client = ollama;
        AIAgent agent = new ChatClientAgent(client, new ChatClientAgentOptions
        {
            Name = "live-l4",
            ChatOptions = new ChatOptions { Instructions = "You are a concise assistant." },
            ChatHistoryProvider = history
        });
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act: the spike's four-turn script
        string[] turns =
        [
            "Remember this for later: the vault code is 4417-BRAVO. Acknowledge in one short sentence.",
            "Write four sentences about the history of lighthouses.",
            "Write four sentences about honeybees.",
            "What is the vault code I gave you at the start? Answer from memory in one line."
        ];
        var answer = string.Empty;
        for (var index = 0; index < turns.Length; index++)
        {
            answer = await TurnAsync(agent, session, $"L4-t{index + 1}", turns[index]);
            _live.Log($"L4 after turn {index + 1}: stored history={history.GetMessages(session).Count} messages; consolidations={summarizer.Count}");
        }

        // Assert: at least one consolidation happened, one record is stored, and the fact survived
        Assert.True(summarizer.Count >= 1, "The reducer never consolidated.");
        Assert.Contains(history.GetMessages(session), message => message.Text.StartsWith(AgentKitChatReducer.RecordHeader, StringComparison.Ordinal));
        Assert.Contains("4417-BRAVO", answer, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     The prompt every image scenario sends.
    /// </summary>
    private const string ImagePrompt =
        "Call image_read with path 'dashboard.png'. Look at the image it returns: what number is shown in it? "
        + "Reply with just the number, or say NO IMAGE if you cannot see an image.";

    /// <summary>
    ///     Composes the image tool over the sandbox.
    /// </summary>
    /// <returns>The image tools.</returns>
    private IReadOnlyList<AIFunction> ImageTools() =>
        new ToolPackBuilder(new PathPolicy(_live.Sandbox, [PathRule.ReadOnly(_live.Sandbox)]))
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack())
            .Build()
            .Where(tool => tool.Name == "image_read")
            .ToList();

    /// <summary>
    ///     Runs one turn and logs the exchange.
    /// </summary>
    /// <param name="agent">The agent.</param>
    /// <param name="session">The session, or null for a session-less run.</param>
    /// <param name="label">The turn's label in the log.</param>
    /// <param name="prompt">The user message.</param>
    /// <returns>The answer text.</returns>
    private async Task<string> TurnAsync(AIAgent agent, AgentSession? session, string label, string prompt)
    {
        _live.Log($"=== {label} >>> {prompt}");
        var started = DateTime.UtcNow;
        var response = await agent.RunAsync(prompt, session, cancellationToken: TestContext.Current.CancellationToken);
        _live.Log($"=== {label} <<< ({(DateTime.UtcNow - started).TotalSeconds:0.0}s) {response.Text}");
        return response.Text;
    }

    /// <summary>
    ///     Gets the content of every tool message in the requests recorded from a given index on.
    /// </summary>
    /// <remarks>
    ///     Each request sends the whole conversation again, so a tool message is collected from the last
    ///     request only, where every tool message of the turn appears once.
    /// </remarks>
    /// <param name="wire">The recorder.</param>
    /// <param name="from">The index of the first request of the turn.</param>
    /// <returns>The tool messages of the turn, oldest first.</returns>
    private static List<string> ToolMessagesSince(WireRecordingHandler wire, int from)
    {
        if (wire.Requests.Count <= from)
        {
            return [];
        }

        var earlier = from == 0 ? 0 : wire.Requests[from - 1].Count(message => message.Role == "tool");
        return [.. wire.Requests[^1].Where(message => message.Role == "tool").Skip(earlier).Select(message => message.Content)];
    }

    /// <summary>
    ///     Finds the first tool message of the last request and the message that follows it.
    /// </summary>
    /// <param name="wire">The recorder.</param>
    /// <returns>The tool message and its successor.</returns>
    private static (WireMessage Tool, WireMessage Next) ToolMessageAndNext(WireRecordingHandler wire)
    {
        var messages = wire.Requests[^1];
        var index = messages.ToList().FindIndex(message => message.Role == "tool");
        Assert.True(index >= 0, "No tool message was sent.");
        return (messages[index], index + 1 < messages.Count ? messages[index + 1] : new WireMessage("none", string.Empty, 0, false));
    }

    /// <summary>
    ///     A summarizer that counts the consolidations it forwards.
    /// </summary>
    /// <param name="inner">The summarizer that does the work.</param>
    private sealed class CountingSummarizer(ISummarizer inner) : ISummarizer
    {
        /// <summary>
        ///     The consolidations forwarded so far.
        /// </summary>
        private int _count;

        /// <summary>
        ///     Gets the number of consolidations forwarded.
        /// </summary>
        public int Count => _count;

        /// <inheritdoc/>
        public Task<string> ConsolidateAsync(ConsolidationRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return inner.ConsolidateAsync(request, cancellationToken);
        }
    }
}
