using System.Text.Json;
using OllamaSharp.Models;

namespace DemaConsulting.AgentKit.Agents.Ollama.Tests;

/// <summary>
///     Unit tests for <see cref="OllamaContextWindow"/>: which of the figures a server can report is
///     believed, and what is assumed when it reports none.
/// </summary>
/// <remarks>
///     The window is the one number the whole compaction arrangement turns on, so the precedence is
///     tested rather than trusted. A session told a window larger than the server enforces will not
///     rotate until the provider has already truncated the conversation, and nothing downstream can
///     detect that.
/// </remarks>
public class OllamaContextWindowTests
{
    /// <summary>
    ///     The model these tests ask about.
    /// </summary>
    private const string Model = "qwen3.5:9b";

    /// <summary>
    ///     Proves a stated window wins outright, so an application that knows better than the
    ///     server is believed.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_StatedWindow_WinsOverWhatTheServerReports()
    {
        // Arrange: a server reporting a loaded model and a published maximum, both different
        var running = new[] { Loaded(Model, 8192) };
        var published = Published("qwen3", 40960);

        // Act: a window stated by the application
        var window = OllamaContextWindow.Select(2048, running, published, Model);

        // Assert: the stated figure, named as stated
        Assert.Equal(2048, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.Stated, window.Source);
    }

    /// <summary>
    ///     Proves the loaded model's length is preferred over the model's published maximum,
    ///     because it is the one the server will actually enforce.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_ModelLoaded_PrefersTheLengthTheServerEnforces()
    {
        // Arrange: a model loaded with far less than it publishes
        var running = new[] { Loaded(Model, 8192) };
        var published = Published("qwen3", 40960);

        // Act
        var window = OllamaContextWindow.Select(stated: null, running, published, Model);

        // Assert: the loaded length, not the published maximum
        Assert.Equal(8192, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.LoadedModel, window.Source);
    }

    /// <summary>
    ///     Proves a bare model name matches the loaded model the server resolved it to, so a
    ///     conversation started without a tag still finds its own window.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_UntaggedModelName_MatchesTheLoadedLatestTag()
    {
        // Arrange: the server reports the tag it resolved
        var running = new[] { Loaded("research-model:latest", 16384) };

        // Act: the caller named no tag
        var window = OllamaContextWindow.Select(stated: null, running, published: null, "research-model");

        // Assert: matched anyway
        Assert.Equal(16384, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.LoadedModel, window.Source);
    }

    /// <summary>
    ///     Proves a loaded model belonging to some other conversation is not mistaken for this one.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_DifferentModelLoaded_DoesNotBorrowItsWindow()
    {
        // Arrange: something else is loaded, and this model publishes a maximum
        var running = new[] { Loaded("some-other-model:latest", 8192) };
        var published = Published("qwen3", 40960);

        // Act
        var window = OllamaContextWindow.Select(stated: null, running, published, Model);

        // Assert: the published maximum, reported as the maximum it is
        Assert.Equal(40960, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.PublishedModel, window.Source);
    }

    /// <summary>
    ///     Proves a published context length arriving as a JSON number is read, which is the shape
    ///     the metadata actually deserializes to.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_PublishedLengthAsJsonNumber_IsRead()
    {
        // Arrange: the value as the serializer materializes it
        var published = new ShowModelResponse
        {
            Info = new ModelInfo
            {
                Architecture = "llama",
                ExtraInfo = new Dictionary<string, object>
                {
                    ["llama.context_length"] = JsonSerializer.Deserialize<JsonElement>("131072"),
                },
            },
        };

        // Act
        var window = OllamaContextWindow.Select(stated: null, running: null, published, Model);

        // Assert
        Assert.Equal(131072, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.PublishedModel, window.Source);
    }

    /// <summary>
    ///     Proves metadata carrying no context length for this architecture falls through rather
    ///     than reporting zero, which no session could be accounted against.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_MetadataWithoutAContextLength_FallsThrough()
    {
        // Arrange: metadata naming an architecture but carrying nothing about its window
        var published = new ShowModelResponse
        {
            Info = new ModelInfo
            {
                Architecture = "llama",
                ExtraInfo = new Dictionary<string, object> { ["llama.block_count"] = 32 },
            },
        };

        // Act
        var window = OllamaContextWindow.Select(stated: null, running: null, published, Model);

        // Assert: the assumed default rather than nothing
        Assert.Equal(OllamaContextWindow.AssumedTokens, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.Assumed, window.Source);
    }

    /// <summary>
    ///     Proves a server that reports nothing yields Ollama's own default, which is the
    ///     conservative choice: rotating earlier than necessary costs summarizer calls, while
    ///     rotating later loses history the provider has already discarded.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_NothingReported_AssumesTheOllamaDefault()
    {
        // Act
        var window = OllamaContextWindow.Select(stated: null, running: null, published: null, Model);

        // Assert
        Assert.Equal(OllamaContextWindow.AssumedTokens, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.Assumed, window.Source);
    }

    /// <summary>
    ///     Proves a missing model name is refused rather than matched against, because a window
    ///     chosen without knowing which model it belongs to is not a window anyone can act on.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_NullModelName_Throws()
    {
        // Arrange: a server reporting a loaded model, so there is something to match wrongly
        var running = new[] { Loaded(Model, 8192) };

        // Act / Assert
        Assert.Throws<ArgumentNullException>(
            () => OllamaContextWindow.Select(stated: null, running, published: null, null!));
    }

    /// <summary>
    ///     Builds a loaded-model report.
    /// </summary>
    /// <param name="name">The model name the server reports.</param>
    /// <param name="contextLength">The length it was loaded with.</param>
    /// <returns>The report.</returns>
    private static RunningModel Loaded(string name, int contextLength) =>
        new() { Name = name, ModelName = name, ContextLength = contextLength };

    /// <summary>
    ///     Builds model metadata carrying a published context length.
    /// </summary>
    /// <param name="architecture">The architecture the metadata keys are prefixed with.</param>
    /// <param name="contextLength">The published length.</param>
    /// <returns>The metadata.</returns>
    private static ShowModelResponse Published(string architecture, int contextLength) =>
        new()
        {
            Info = new ModelInfo
            {
                Architecture = architecture,
                ExtraInfo = new Dictionary<string, object>
                {
                    [architecture + ".context_length"] = contextLength,
                },
            },
        };
}
