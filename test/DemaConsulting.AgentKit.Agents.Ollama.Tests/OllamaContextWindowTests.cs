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
    ///     Proves a stated window wins outright, so an application that asked the server to run at a
    ///     size is believed about the size it asked for.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_StatedWindow_WinsOverWhatTheServerReports()
    {
        // Arrange: a server reporting the model loaded at a different length
        var running = new[] { Loaded(Model, 8192) };

        // Act: a window stated by the application
        var window = OllamaContextWindow.Select(2048, running, Model);

        // Assert: the stated figure, named as stated
        Assert.Equal(2048, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.Stated, window.Source);
    }

    /// <summary>
    ///     Proves a stated window of zero is treated as none, so an application that passed an
    ///     unset option straight through is not told its conversation has no room.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_StatedWindowOfZero_IsTreatedAsUnstated()
    {
        // Arrange: the server reports the model as loaded
        var running = new[] { Loaded(Model, 8192) };

        // Act: zero is stated, as an unset option commonly arrives
        var window = OllamaContextWindow.Select(0, running, Model);

        // Assert: the server's figure, not a window of zero
        Assert.Equal(8192, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.LoadedModel, window.Source);
    }

    /// <summary>
    ///     Proves the length the loaded instance is running is what gets reported, because it is the
    ///     one the server will actually enforce.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_ModelLoaded_PrefersTheLengthTheServerEnforces()
    {
        // Arrange: a model loaded at a length of the server's choosing
        var running = new[] { Loaded(Model, 8192) };

        // Act
        var window = OllamaContextWindow.Select(stated: null, running, Model);

        // Assert: the loaded length, named as the loaded model's
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
        var window = OllamaContextWindow.Select(stated: null, running, "research-model");

        // Assert: matched anyway
        Assert.Equal(16384, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.LoadedModel, window.Source);
    }

    /// <summary>
    ///     Proves a loaded model the server reported without a tag is matched by a caller that
    ///     named the tag, so the normalization holds on the reported side as well as the asked one.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_UntaggedLoadedModelName_MatchesATaggedRequest()
    {
        // Arrange: the server reports the model bare, naming no tag
        var running = new[] { Loaded("research-model", 16384) };

        // Act: the caller named the tag the bare report resolves to
        var window = OllamaContextWindow.Select(stated: null, running, "research-model:latest");

        // Assert: matched anyway
        Assert.Equal(16384, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.LoadedModel, window.Source);
    }

    /// <summary>
    ///     Proves the model is found under either name a loaded-model report carries, so a report
    ///     that omits one of them still yields the length the server is enforcing.
    /// </summary>
    [Fact]
    public void OllamaContextWindow_Select_ModelNamedUnderEitherReportedField_IsMatched()
    {
        // Arrange: two reports, each naming the model in only one of the two fields
        var underName = new[]
        {
            new RunningModel { Name = Model, ModelName = null, ContextLength = 8192 },
        };
        var underModelName = new[]
        {
            new RunningModel { Name = null!, ModelName = Model, ContextLength = 16384 },
        };

        // Act
        var fromName = OllamaContextWindow.Select(stated: null, underName, Model);
        var fromModelName = OllamaContextWindow.Select(stated: null, underModelName, Model);

        // Assert: matched either way, each reporting the length its own report carried
        Assert.Multiple(
            () => Assert.Equal(8192, fromName.Tokens),
            () => Assert.Equal(OllamaContextWindowSource.LoadedModel, fromName.Source),
            () => Assert.Equal(16384, fromModelName.Tokens),
            () => Assert.Equal(OllamaContextWindowSource.LoadedModel, fromModelName.Source));
    }

    /// <summary>
    ///     Proves a loaded model belonging to some other conversation is not mistaken for this one.
    /// </summary>
    /// <remarks>
    ///     The promise is unchanged - never borrow another model's window - but the honest answer
    ///     beneath it moved. Nothing is known about a model the server did not report as loaded, and
    ///     the conservative default is the only figure that does not invent one.
    /// </remarks>
    [Fact]
    public void OllamaContextWindow_Select_DifferentModelLoaded_DoesNotBorrowItsWindow()
    {
        // Arrange: something else is loaded
        var running = new[] { Loaded("some-other-model:latest", 8192) };

        // Act
        var window = OllamaContextWindow.Select(stated: null, running, Model);

        // Assert: the conservative default rather than the other model's window
        Assert.Equal(OllamaContextWindow.AssumedTokens, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.Assumed, window.Source);
    }

    /// <summary>
    ///     Proves a server that reports nothing yields Ollama's own default, which is the
    ///     conservative choice: rotating earlier than necessary costs summarizer calls, while
    ///     rotating later loses history the provider has already discarded.
    /// </summary>
    /// <remarks>
    ///     The figure itself is asserted as well as the constant, because 4,096 is a fact about
    ///     Ollama rather than a number this library is free to choose, and it is quoted to users in
    ///     the README and the sample's own documentation.
    /// </remarks>
    [Fact]
    public void OllamaContextWindow_Select_NothingReported_AssumesTheOllamaDefault()
    {
        // Act
        var window = OllamaContextWindow.Select(stated: null, running: null, Model);

        // Assert
        Assert.Equal(4096, OllamaContextWindow.AssumedTokens);
        Assert.Equal(OllamaContextWindow.AssumedTokens, window.Tokens);
        Assert.Equal(OllamaContextWindowSource.Assumed, window.Source);
    }

    /// <summary>
    ///     Proves a model name that names nothing is refused rather than matched against, because a
    ///     window chosen without knowing which model it belongs to is not a window anyone can act on.
    /// </summary>
    /// <remarks>
    ///     The empty case is the one that mattered and was missed. A null name never reached the
    ///     ladder, but an empty one became <c>":latest"</c>, matched no loaded model, and returned
    ///     the assumed default — indistinguishable from a server that simply had not answered. That
    ///     is precisely the silent wrong answer this guard exists to prevent, arrived at by a
    ///     different route.
    /// </remarks>
    /// <param name="model">The unusable model name.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void OllamaContextWindow_Select_ModelNameThatNamesNothing_Throws(string? model)
    {
        // Arrange: a server reporting a loaded model, so there is something to match wrongly
        var running = new[] { Loaded(Model, 8192) };

        // Act / Assert: ThrowIfNullOrEmpty raises ArgumentNullException for null and
        // ArgumentException for empty, so the assertion accepts the family rather than one member
        Assert.ThrowsAny<ArgumentException>(
            () => OllamaContextWindow.Select(stated: null, running, model!));
    }

    /// <summary>
    ///     Builds a loaded-model report.
    /// </summary>
    /// <param name="name">The model name the server reports.</param>
    /// <param name="contextLength">The length it was loaded with.</param>
    /// <returns>The report.</returns>
    private static RunningModel Loaded(string name, int contextLength) =>
        new() { Name = name, ModelName = name, ContextLength = contextLength };
}
