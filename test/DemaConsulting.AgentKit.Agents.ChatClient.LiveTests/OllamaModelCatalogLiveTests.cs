using System.Text.Json.Nodes;
using DemaConsulting.AgentKit.Agents.Ollama;
using DemaConsulting.AgentKit.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;
using ModelInfo = DemaConsulting.AgentKit.Core.ModelInfo;

namespace DemaConsulting.AgentKit.Agents.ChatClient.LiveTests;

/// <summary>
///     Live scenarios for the Ollama model catalog and model settings against a real server.
/// </summary>
/// <remarks>
///     <para>
///     Supplementary verification evidence for model selection on Ollama: the catalog lists the
///     server's vision models and leaves out embedding models; a chosen window and effort reach the
///     wire as Ollama's own <c>num_ctx</c> and <c>think</c> and the server loads the model at that
///     window; and a window above the trained length is refused before any request.
///     </para>
///     <para>
///     Unlike the other live scenarios these record the request body <em>as AgentKit wrote it</em>,
///     without the suite's usual rewrite of <c>think</c> and <c>num_ctx</c>, because those two
///     options are exactly what is under test. Output is capped so a thinking model answers quickly.
///     </para>
///     <para>
///     Every scenario skips unless <c>AGENTKIT_OLLAMA_LIVE_MODEL</c> names the model to use, which
///     must be a vision-capable tool-calling model on the server; the window is
///     <c>AGENTKIT_OLLAMA_LIVE_NUM_CTX</c>, which must be within the model's trained length.
///     </para>
/// </remarks>
[Collection(OllamaLiveCollection.Name)]
public sealed class OllamaModelCatalogLiveTests
{
    /// <summary>
    ///     The vision-only requirement the scenarios list and select under.
    /// </summary>
    private const ModelCapabilities Required = ModelCapabilities.Vision | ModelCapabilities.ToolCalls;

    /// <summary>
    ///     The run's shared state.
    /// </summary>
    private readonly OllamaLiveFixture _live;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OllamaModelCatalogLiveTests"/> class.
    /// </summary>
    /// <param name="live">The run's shared state.</param>
    public OllamaModelCatalogLiveTests(OllamaLiveFixture live)
    {
        _live = live;
    }

    /// <summary>
    ///     C1: the vision listing holds the run's model and no embedding model.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsOllamaLive_Catalog_VisionListing_ExcludesEmbeddingModels()
    {
        // Arrange
        _live.RequireOptIn();
        using var client = new OllamaApiClient(_live.Host);
        var catalog = new OllamaModelCatalog(client);

        // Act
        var everything = await catalog.ListAsync(new ModelQuery { IncludeUnusable = true }, TestContext.Current.CancellationToken);
        var vision = await catalog.ListAsync(new ModelQuery { Require = Required }, TestContext.Current.CancellationToken);
        foreach (var model in everything)
        {
            _live.Log(Describe(model));
        }

        // Assert
        Assert.Contains(vision, model => model.Id == _live.Model);
        Assert.DoesNotContain(vision, model => model.Id.Contains("embed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(everything, model => model.Id.StartsWith("nomic-embed-text", StringComparison.Ordinal) && !model.IsUsable);
    }

    /// <summary>
    ///     C2: a chosen window and the effort <c>none</c> reach the wire as <c>num_ctx</c> and
    ///     <c>think: false</c>, and the server loads the model at that window.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsOllamaLive_Selection_WindowAndEffortNone_ReachTheWireAndTheServer()
    {
        // Arrange
        _live.RequireOptIn();
        var (client, wire) = CreateClient();
        using var ollama = client;
        var catalog = new OllamaModelCatalog(ollama);
        var selection = await catalog.SelectAsync(
            new ModelSettings(_live.Model!) { ContextWindowTokens = _live.NumContext, ReasoningEffort = "none", MaxOutputTokens = 64 },
            Required,
            TestContext.Current.CancellationToken);
        var window = await OllamaContextWindow.ReadAsync(ollama, selection, TestContext.Current.CancellationToken);
        var agent = ChatClientAgentFactory.Create(
            new OllamaContextSizingChatClient(ollama, window.Tokens),
            [AIFunctionFactory.Create(() => "pong", "ping_tool", "Returns pong.")],
            instructions: "Answer in one word.",
            selection: selection);

        // Act
        var response = await agent.RunAsync("Reply with the single word ready.", cancellationToken: TestContext.Current.CancellationToken);
        var running = await ollama.ListRunningModelsAsync(TestContext.Current.CancellationToken);
        var loaded = running.FirstOrDefault(model => model.Name == _live.Model);
        _live.Log($"C2 answer='{response.Text}' window={window.Tokens}/{window.Source} loaded={loaded?.ContextLength}");

        // Assert: the stated window, sent as num_ctx with no thinking on every request, and the
        // instance runs at it
        Assert.NotEmpty(wire.Bodies);
        Assert.All(wire.Bodies, body =>
        {
            Assert.Equal(_live.Model, body["model"]?.GetValue<string>());
            Assert.Equal(_live.NumContext, body["options"]?["num_ctx"]?.GetValue<int>());
            Assert.False(body["think"]?.GetValue<bool>());
        });
        Assert.Equal(OllamaContextWindowSource.Stated, window.Source);
        Assert.Equal(_live.NumContext, loaded?.ContextLength);
    }

    /// <summary>
    ///     C3: the efforts <c>low</c> and <c>high</c> reach the wire as Ollama's <c>think</c> levels;
    ///     the reasoning each produced is logged, because whether a model honors the level is the
    ///     model's own.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsOllamaLive_Selection_EffortLevels_ReachTheWireAsThink()
    {
        // Arrange
        _live.RequireOptIn();
        var (client, wire) = CreateClient();
        using var ollama = client;
        var catalog = new OllamaModelCatalog(ollama);
        var model = await catalog.GetAsync(_live.Model!, TestContext.Current.CancellationToken);
        Assert.NotNull(model);

        foreach (var effort in new[] { "low", "high" })
        {
            var selection = new ModelSettings(model.Id) { ReasoningEffort = effort, ContextWindowTokens = _live.NumContext, MaxOutputTokens = 256 }
                .Bind(model, Required);
            using var configured = new ModelSettingsChatClient(new OllamaContextSizingChatClient(ollama, _live.NumContext), selection);

            // Act
            var response = await configured.GetResponseAsync("What is 17 times 23?", cancellationToken: TestContext.Current.CancellationToken);
            var reasoning = response.Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>().Sum(content => content.Text.Length);
            _live.Log($"C3 effort={effort} reasoningChars={reasoning} answerChars={response.Text.Length} finish={response.FinishReason}");

            // Assert
            Assert.Equal(effort, wire.Bodies[^1]["think"]?.GetValue<string>());
        }
    }

    /// <summary>
    ///     C4: a window one token above the trained length is refused before any chat request.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsOllamaLive_Selection_WindowAboveTrainedLength_IsRefusedBeforeAnyRequest()
    {
        // Arrange
        _live.RequireOptIn();
        var (client, wire) = CreateClient();
        using var ollama = client;
        var catalog = new OllamaModelCatalog(ollama);
        var model = await catalog.GetAsync(_live.Model!, TestContext.Current.CancellationToken);
        Assert.NotNull(model?.ContextWindowTokens);

        // Act
        var failure = await Assert.ThrowsAsync<ModelSettingsException>(() => catalog.SelectAsync(
            new ModelSettings(model.Id) { ContextWindowTokens = model.ContextWindowTokens.Value + 1 },
            Required,
            TestContext.Current.CancellationToken));
        _live.Log($"C4 refused: {failure.Problems[0]}");

        // Assert
        Assert.Empty(wire.Bodies);
    }

    /// <summary>
    ///     Renders a model for the log.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <returns>One line.</returns>
    private static string Describe(ModelInfo model) =>
        $"[catalog] {model.Id} usable={model.IsUsable} caps={model.Capabilities} trained={model.ContextWindowTokens} "
        + $"efforts=[{string.Join(",", model.ReasoningEfforts.Select(effort => effort.Id))}] reason={model.UnusableReason}";

    /// <summary>
    ///     Creates a client whose chat request bodies are recorded exactly as written.
    /// </summary>
    /// <returns>The client and the recorder.</returns>
    private (OllamaApiClient Client, RawChatRecorder Wire) CreateClient()
    {
        var wire = new RawChatRecorder(_live.Log);
        var http = new HttpClient(wire) { BaseAddress = _live.Host, Timeout = TimeSpan.FromMinutes(10) };
        return (new OllamaApiClient(http), wire);
    }

    /// <summary>
    ///     Records every chat request body unchanged.
    /// </summary>
    /// <param name="log">Writes one line to the run's log.</param>
    private sealed class RawChatRecorder(Action<string> log) : DelegatingHandler(new HttpClientHandler())
    {
        /// <summary>
        ///     Gets every chat request body sent, oldest first.
        /// </summary>
        public List<JsonObject> Bodies { get; } = [];

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null && request.RequestUri?.AbsolutePath.EndsWith("/api/chat", StringComparison.Ordinal) == true)
            {
                var body = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))!.AsObject();
                Bodies.Add(body);
                log($"[wire-raw] model={body["model"]} think={body["think"]?.ToJsonString()} options={body["options"]?.ToJsonString()}");
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
