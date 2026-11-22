using System.Reflection;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using ModelInfo = DemaConsulting.AgentKit.Core.ModelInfo;

namespace DemaConsulting.AgentKit.Agents.Ollama.Tests;

/// <summary>
///     Unit tests for the Ollama model catalog, driven through the real client over HTTP against
///     payloads captured from a live Ollama 0.34.2 server.
/// </summary>
/// <remarks>
///     <para>
///     The listing and descriptions are the server's own, captured verbatim except that each model's
///     license and model-file text are elided for size. The three target models — two with a vision
///     projector and one whose newer engine embeds vision with no projector — and two embedding
///     models are replayed, so the mapping is exercised on what Ollama actually sends.
///     </para>
///     <para>
///     The payloads named <c>synthetic-pre-0-6-4</c> are the captured descriptions with the
///     capability list removed, standing in for a server older than the one that introduced it.
///     </para>
/// </remarks>
public sealed class OllamaModelCatalogTests
{
    /// <summary>
    ///     Proves the three vision models are described as vision, tool-calling and reasoning, with
    ///     the length each was trained for.
    /// </summary>
    [Fact]
    public async Task OllamaModelCatalog_ListAsync_CapturedServer_DescribesTheVisionModels()
    {
        // Arrange
        using var server = CapturedServer();
        var catalog = new OllamaModelCatalog(new OllamaApiClient(new Uri(server.Url!)));

        // Act
        var models = await catalog.ListAsync(
            new ModelQuery { Require = ModelCapabilities.Vision | ModelCapabilities.ToolCalls },
            TestContext.Current.CancellationToken);

        // Assert: in the server's order, every capability reported, and the trained length as the ceiling
        Assert.Equal(["gemma4:26b", "qwen3.8:27b", "gemma4:e4b"], models.Select(model => model.Id));
        Assert.All(models, model =>
        {
            Assert.Equal(OllamaModelCatalog.ProviderId, model.Provider);
            Assert.True(model.Supports(ModelCapabilities.Vision | ModelCapabilities.ToolCalls | ModelCapabilities.Reasoning));
            Assert.True(model.IsUsable);
            Assert.Null(model.DefaultReasoningEffort);
        });
        Assert.Equal([262_144, 262_144, 131_072], models.Select(model => model.ContextWindowTokens));
    }

    /// <summary>
    ///     Proves the models are listed in the order the server lists them, not re-sorted, so a
    ///     picker shows the provider's order as the catalog contract states.
    /// </summary>
    [Fact]
    public async Task OllamaModelCatalog_ListAsync_KeepsTheServersOrder()
    {
        // Arrange: the captured listing is deliberately not in name order
        using var server = CapturedServer();
        var catalog = new OllamaModelCatalog(new OllamaApiClient(new Uri(server.Url!)));

        // Act
        var models = await catalog.ListAsync(
            new ModelQuery { IncludeUnusable = true },
            TestContext.Current.CancellationToken);

        // Assert: every installed model, exactly as /api/tags listed them
        Assert.Equal(
            ["gemma4:26b", "mxbai-embed-large:latest", "nomic-embed-text:latest", "qwen3.8:27b", "gemma4:e4b"],
            models.Select(model => model.Id));
    }

    /// <summary>
    ///     Proves a model whose vision is embedded with no projector is still described as vision,
    ///     because the server's own capability report is used.
    /// </summary>
    [Fact]
    public async Task OllamaModelCatalog_GetAsync_VisionWithoutProjector_IsVision()
    {
        // Arrange
        using var server = CapturedServer();
        var catalog = new OllamaModelCatalog(new OllamaApiClient(new Uri(server.Url!)));

        // Act
        var model = await catalog.GetAsync("gemma4:e4b", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(model);
        Assert.True(model.Supports(ModelCapabilities.Vision));
        Assert.Equal("gemma4", model.AdditionalProperties?["family"]);
    }

    /// <summary>
    ///     Proves an embedding model is listed only on request, and then as unusable with the reason.
    /// </summary>
    [Fact]
    public async Task OllamaModelCatalog_ListAsync_EmbeddingModel_IsUnusable()
    {
        // Arrange
        using var server = CapturedServer();
        var catalog = new OllamaModelCatalog(new OllamaApiClient(new Uri(server.Url!)));

        // Act
        var usable = await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var nomic = await catalog.GetAsync("nomic-embed-text:latest", TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(usable, model => model.Id.StartsWith("nomic", StringComparison.Ordinal));
        Assert.NotNull(nomic);
        Assert.False(nomic.IsUsable);
        Assert.Equal("embedding-only model", nomic.UnusableReason);
        Assert.Equal(2048, nomic.ContextWindowTokens);
    }

    /// <summary>
    ///     Proves a thinking model offers off, low, medium and high, mapped to the neutral efforts.
    /// </summary>
    [Fact]
    public async Task OllamaModelCatalog_GetAsync_ThinkingModel_OffersFourEfforts()
    {
        // Arrange
        using var server = CapturedServer();
        var catalog = new OllamaModelCatalog(new OllamaApiClient(new Uri(server.Url!)));

        // Act
        var model = await catalog.GetAsync("qwen3.8:27b", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(model);
        Assert.Equal(["none", "low", "medium", "high"], model.ReasoningEfforts.Select(effort => effort.Id));
        Assert.Equal(
            [ReasoningEffort.None, ReasoningEffort.Low, ReasoningEffort.Medium, ReasoningEffort.High],
            model.ReasoningEfforts.Select(effort => effort.Standard));
        Assert.Equal(
            ModelAdjustments.ReasoningEffort | ModelAdjustments.ContextWindow | ModelAdjustments.MaxOutputTokens,
            model.Adjustments);
    }

    /// <summary>
    ///     Proves a bare name finds the model's latest tag, as the server resolves it.
    /// </summary>
    [Fact]
    public async Task OllamaModelCatalog_GetAsync_BareName_FindsTheLatestTag()
    {
        // Arrange
        using var server = CapturedServer();
        var catalog = new OllamaModelCatalog(new OllamaApiClient(new Uri(server.Url!)));

        // Act
        var model = await catalog.GetAsync("nomic-embed-text", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("nomic-embed-text:latest", model?.Id);
    }

    /// <summary>
    ///     Proves a window above the trained length is refused at selection, before any request.
    /// </summary>
    [Fact]
    public async Task OllamaModelCatalog_SelectAsync_WindowAboveTrainedLength_IsRefused()
    {
        // Arrange
        using var server = CapturedServer();
        var catalog = new OllamaModelCatalog(new OllamaApiClient(new Uri(server.Url!)));
        var settings = new ModelSettings("gemma4:e4b") { ContextWindowTokens = 131_073 };

        // Act
        var failure = await Assert.ThrowsAsync<ModelSettingsException>(
            () => catalog.SelectAsync(settings, cancellationToken: TestContext.Current.CancellationToken));

        // Assert: refused, and nothing but the catalog's own reads reached the server
        Assert.Equal(
            "Model 'gemma4:e4b' supports at most 131,072 tokens; a 131,073-token window is not supported.",
            Assert.Single(failure.Problems));
        Assert.DoesNotContain(server.LogEntries, entry => entry.RequestMessage!.Path.Contains("/api/chat", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves a model the server will not describe is listed as unusable rather than failing the
    ///     listing.
    /// </summary>
    [Fact]
    public async Task OllamaModelCatalog_ListAsync_DescriptionFails_ModelIsUnusable()
    {
        // Arrange: a server that lists the models but describes none of them
        using var server = WireMockServer.Start();
        ServeTags(server);
        server.Given(Request.Create().WithPath("/api/show").UsingPost()).RespondWith(Response.Create().WithStatusCode(500));
        var catalog = new OllamaModelCatalog(new OllamaApiClient(new Uri(server.Url!)));

        // Act
        var models = await catalog.ListAsync(new ModelQuery { IncludeUnusable = true }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, models.Count);
        Assert.All(models, model =>
        {
            Assert.False(model.IsUsable);
            Assert.Equal("Ollama did not describe this model.", model.UnusableReason);
        });
    }

    /// <summary>
    ///     Proves a server too old to report capabilities has vision inferred from a projector, is
    ///     marked unusable because tool support cannot be confirmed, and says so.
    /// </summary>
    [Fact]
    public void OllamaModelCatalog_Map_PreCapabilityServerWithProjector_InfersVision()
    {
        // Arrange
        var shown = Parse<ShowModelResponse>("synthetic-pre-0-6-4-show-projector.json");

        // Act
        var model = OllamaModelCatalog.Map(Listed("gemma4:26b"), shown);

        // Assert
        Assert.Equal(ModelCapabilities.Vision, model.Capabilities);
        Assert.False(model.IsUsable);
        Assert.Equal("Ollama 0.6.4 or later is required to confirm tool support.", model.UnusableReason);
        Assert.Contains(model.Notices, notice => notice.Message.Contains("capabilities were inferred", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves vision is inferred from the architecture's vision keys when there is no projector,
    ///     and not inferred for a model with neither.
    /// </summary>
    [Fact]
    public void OllamaModelCatalog_Map_PreCapabilityServer_InfersVisionFromVisionKeysOnly()
    {
        // Arrange
        var keys = Parse<ShowModelResponse>("synthetic-pre-0-6-4-show-vision-keys.json");
        var text = Parse<ShowModelResponse>("synthetic-pre-0-6-4-show-text-only.json");

        // Act
        var withKeys = OllamaModelCatalog.Map(Listed("gemma4:e4b"), keys);
        var withNeither = OllamaModelCatalog.Map(Listed("nomic-embed-text:latest"), text);

        // Assert
        Assert.True(withKeys.Supports(ModelCapabilities.Vision));
        Assert.False(withNeither.Supports(ModelCapabilities.Vision));
    }

    /// <summary>
    ///     Proves a model whose metadata carries no context length reports no ceiling.
    /// </summary>
    [Fact]
    public void OllamaModelCatalog_Map_NoContextLength_ReportsNoCeiling()
    {
        // Arrange
        var shown = new ShowModelResponse
        {
            Capabilities = ["completion", "tools"],
            Info = new OllamaSharp.Models.ModelInfo { Architecture = "llama", ExtraInfo = new Dictionary<string, object>() },
        };

        // Act
        var model = OllamaModelCatalog.Map(Listed("llama:latest"), shown);

        // Assert
        Assert.Null(model.ContextWindowTokens);
        Assert.True(model.IsUsable);
        Assert.Empty(model.ReasoningEfforts);
    }

    /// <summary>
    ///     Proves a GPT-OSS model, which cannot stop thinking, is not offered the off effort.
    /// </summary>
    [Fact]
    public void OllamaModelCatalog_Map_GptOss_IsNotOfferedOff()
    {
        // Arrange
        var listed = Listed("gpt-oss:20b");
        listed.Details = new Details { Family = "gptoss" };
        var shown = new ShowModelResponse { Capabilities = ["completion", "tools", "thinking"] };

        // Act
        var model = OllamaModelCatalog.Map(listed, shown);

        // Assert
        Assert.Equal(["low", "medium", "high"], model.ReasoningEfforts.Select(effort => effort.Id));
    }

    /// <summary>
    ///     Proves a completion model without tool calling is unusable, naming why.
    /// </summary>
    [Fact]
    public void OllamaModelCatalog_Map_CompletionWithoutTools_IsUnusable()
    {
        // Arrange
        var shown = new ShowModelResponse { Capabilities = ["completion", "vision"] };

        // Act
        ModelInfo model = OllamaModelCatalog.Map(Listed("old-vision:latest"), shown);

        // Assert
        Assert.False(model.IsUsable);
        Assert.Equal("Model does not support tool calling.", model.UnusableReason);
        Assert.True(model.Supports(ModelCapabilities.Vision));
    }

    /// <summary>
    ///     Starts a server replaying the captured listing and descriptions.
    /// </summary>
    /// <returns>The started server.</returns>
    internal static WireMockServer CapturedServer()
    {
        var server = WireMockServer.Start();
        ServeTags(server);

        foreach (var (model, fixture) in new[]
        {
            ("gemma4:26b", "show-gemma4-26b.json"),
            ("gemma4:e4b", "show-gemma4-e4b.json"),
            ("qwen3.8:27b", "show-qwen3-8-27b.json"),
            ("nomic-embed-text:latest", "show-nomic-embed-text.json"),
            ("mxbai-embed-large:latest", "show-mxbai-embed-large.json"),
        })
        {
            server
                .Given(Request.Create().WithPath("/api/show").UsingPost().WithBody(new WildcardMatcher($"*\"{model}\"*")))
                .RespondWith(Response.Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(Fixture(fixture)));
        }

        return server;
    }

    /// <summary>
    ///     Serves the captured model listing.
    /// </summary>
    /// <param name="server">The server.</param>
    private static void ServeTags(WireMockServer server) =>
        server
            .Given(Request.Create().WithPath("/api/tags").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture("tags-five-models.json")));

    /// <summary>
    ///     Builds a listed model with only a name.
    /// </summary>
    /// <param name="name">The model name.</param>
    /// <returns>The listed model.</returns>
    private static Model Listed(string name) => new() { Name = name, ModelName = name };

    /// <summary>
    ///     Deserializes a fixture as OllamaSharp would.
    /// </summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <param name="fixture">The fixture file name.</param>
    /// <returns>The payload.</returns>
    private static T Parse<T>(string fixture) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(Fixture(fixture))!;

    /// <summary>
    ///     Reads an embedded fixture.
    /// </summary>
    /// <param name="name">The fixture file name.</param>
    /// <returns>The payload text.</returns>
    private static string Fixture(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(entry => entry.EndsWith(name, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
