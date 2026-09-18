using System.Text.Json;
using OllamaSharp.Models;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests;

/// <summary>
///     Unit tests for <see cref="OllamaContextWindow"/>: which of the figures a server can report
///     is believed, and what is assumed when it reports none.
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

        // Act: a window stated on the command line
        var window = OllamaContextWindow.Select(2048, running, published, Model);

        // Assert: the stated figure, named as stated
        Assert.Equal(2048, window.Tokens);
        Assert.Equal(ContextWindowSource.Stated, window.Source);
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
        Assert.Equal(ContextWindowSource.LoadedModel, window.Source);
    }

    /// <summary>
    ///     Proves a bare model name matches the loaded model the server resolved it to, so a run
    ///     started without a tag still finds its own window.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_UntaggedModelName_MatchesTheLoadedLatestTag()
    {
        // Arrange: the server reports the tag it resolved
        var running = new[] { Loaded("research-model:latest", 16384) };

        // Act: the command line named no tag
        var window = OllamaContextWindow.Select(stated: null, running, published: null, "research-model");

        // Assert: matched anyway
        Assert.Equal(16384, window.Tokens);
        Assert.Equal(ContextWindowSource.LoadedModel, window.Source);
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
        Assert.Equal(ContextWindowSource.PublishedModel, window.Source);
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
        Assert.Equal(ContextWindowSource.PublishedModel, window.Source);
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
        Assert.Equal(ContextWindowSource.Assumed, window.Source);
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
        Assert.Equal(ContextWindowSource.Assumed, window.Source);
    }

    /// <summary>
    ///     Proves every source describes itself, so the startup banner can never present a
    ///     published maximum as though it were the limit in force.
    /// </summary>
    /// <param name="source">The source being described.</param>
    [Theory]
    [InlineData(ContextWindowSource.Stated)]
    [InlineData(ContextWindowSource.LoadedModel)]
    [InlineData(ContextWindowSource.PublishedModel)]
    [InlineData(ContextWindowSource.Assumed)]
    [InlineData(ContextWindowSource.Ceiling)]
    public void OllamaContextWindow_Describe_EverySource_NamesTheNumberAndItsProvenance(
        ContextWindowSource source)
    {
        // Arrange
        var window = new ContextWindow(4096, source);

        // Act
        var described = window.Describe();

        // Assert: the number is there, and so is a statement about where it came from
        Assert.Contains("4096", described, StringComparison.Ordinal);
        Assert.Contains("(", described, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a ceiling is described as an upper bound rather than as the window.
    /// </summary>
    /// <remarks>
    ///     A ceiling only lowers: on a provider that reports its own window, one above the runtime's
    ///     limit is ignored entirely. A banner that stated it as the window could announce a figure
    ///     the session never accounts against - reporting 272,000 tokens while rotation happened at
    ///     8,000. Saying "at most" is what keeps the banner true whichever of the two governs.
    /// </remarks>
    [Fact]
    public void OllamaContextWindow_Describe_Ceiling_ReadsAsAnUpperBoundNotTheWindow()
    {
        // Arrange / Act
        var described = new ContextWindow(11000, ContextWindowSource.Ceiling).Describe();

        // Assert
        Assert.StartsWith("at most 11000 tokens", described, StringComparison.Ordinal);
        Assert.Contains("the lower of the two governs", described, StringComparison.Ordinal);
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
