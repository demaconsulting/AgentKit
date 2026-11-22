using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the decorator that applies a model selection to every chat request.
/// </summary>
/// <remarks>
///     The wrapped client records the options each request arrived with, so what the provider would
///     have been asked for is observed directly.
/// </remarks>
public class ModelSettingsChatClientTests
{
    /// <summary>
    ///     Proves a request carries the model, the neutral effort and the output limit.
    /// </summary>
    [Fact]
    public async Task ModelSettingsChatClient_GetResponseAsync_AppliesTheSelection()
    {
        // Arrange
        var inner = new OptionsRecordingChatClient();
        var selection = new ModelSettings("vision-large") { ReasoningEffort = "high", MaxOutputTokens = 1000 }
            .Bind(ModelTestData.VisionModel());
        using var client = new ModelSettingsChatClient(inner, selection);

        // Act
        await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var options = Assert.Single(inner.Received);
        Assert.Equal("vision-large", options.ModelId);
        Assert.Equal(ReasoningEffort.High, options.Reasoning?.Effort);
        Assert.Equal(1000, options.MaxOutputTokens);
    }

    /// <summary>
    ///     Proves a streamed request is configured exactly as a non-streamed one.
    /// </summary>
    [Fact]
    public async Task ModelSettingsChatClient_GetStreamingResponseAsync_AppliesTheSelection()
    {
        // Arrange
        var inner = new OptionsRecordingChatClient();
        var selection = new ModelSettings("vision-large") { ReasoningEffort = "low" }.Bind(ModelTestData.VisionModel());
        using var client = new ModelSettingsChatClient(inner, selection);

        // Act
        await foreach (var _ in client.GetStreamingResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
        {
            // Drain the stream so the request is made.
        }

        // Assert
        var options = Assert.Single(inner.Received);
        Assert.Equal("vision-large", options.ModelId);
        Assert.Equal(ReasoningEffort.Low, options.Reasoning?.Effort);
    }

    /// <summary>
    ///     Proves a value the caller set wins, and the caller's options object is left untouched.
    /// </summary>
    [Fact]
    public async Task ModelSettingsChatClient_GetResponseAsync_CallerValuesWinAndAreNotMutated()
    {
        // Arrange
        var inner = new OptionsRecordingChatClient();
        var selection = new ModelSettings("vision-large") { ReasoningEffort = "high", MaxOutputTokens = 1000 }
            .Bind(ModelTestData.VisionModel());
        using var client = new ModelSettingsChatClient(inner, selection);
        var callers = new ChatOptions
        {
            ModelId = "caller-model",
            MaxOutputTokens = 5,
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None },
        };

        // Act
        await client.GetResponseAsync("hi", callers, TestContext.Current.CancellationToken);

        // Assert: the caller's values reached the provider, and the caller's object is a different one
        var options = Assert.Single(inner.Received);
        Assert.Equal("caller-model", options.ModelId);
        Assert.Equal(5, options.MaxOutputTokens);
        Assert.Equal(ReasoningEffort.None, options.Reasoning?.Effort);
        Assert.NotSame(callers, options);
    }

    /// <summary>
    ///     Proves no effort is sent when none was chosen, so the model's own default applies.
    /// </summary>
    [Fact]
    public async Task ModelSettingsChatClient_GetResponseAsync_NoEffortChosen_SendsNoReasoning()
    {
        // Arrange
        var inner = new OptionsRecordingChatClient();
        var selection = new ModelSettings("vision-large").Bind(ModelTestData.VisionModel());
        using var client = new ModelSettingsChatClient(inner, selection);
        var callers = new ChatOptions();

        // Act
        await client.GetResponseAsync("hi", callers, TestContext.Current.CancellationToken);

        // Assert
        var options = Assert.Single(inner.Received);
        Assert.Null(options.Reasoning);
        Assert.Null(options.MaxOutputTokens);
        Assert.Null(callers.ModelId);
    }

    /// <summary>
    ///     Proves a selection a chat client cannot apply is refused at construction.
    /// </summary>
    [Fact]
    public void ModelSettingsChatClient_Construct_InapplicableSelection_Throws()
    {
        // Arrange: a tier, and an effort with no neutral equivalent
        var tiered = new ModelSettings("vision-large") { ContextTier = "default" }.Bind(ModelTestData.VisionModel());
        var maxed = new ModelSettings("vision-large") { ReasoningEffort = "max" }.Bind(ModelTestData.VisionModel());
        using var inner = new OptionsRecordingChatClient();

        // Act / Assert
        var tierFailure = Assert.Throws<ArgumentException>(() => new ModelSettingsChatClient(inner, tiered));
        var effortFailure = Assert.Throws<ArgumentException>(() => new ModelSettingsChatClient(inner, maxed));
        Assert.Contains("has no context tiers", tierFailure.Message, StringComparison.Ordinal);
        Assert.Contains("Effort 'max' has no provider-neutral equivalent", effortFailure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a selection carrying a context window is refused when nothing beneath applies it,
    ///     rather than the window being dropped.
    /// </summary>
    [Fact]
    public void ModelSettingsChatClient_Construct_WindowNotAppliedBeneath_Throws()
    {
        // Arrange: a window, over a client that applies none
        var selection = new ModelSettings("vision-large") { ContextWindowTokens = 8192 }
            .Bind(ModelTestData.VisionModel());
        using var inner = new OptionsRecordingChatClient();

        // Act
        var failure = Assert.Throws<ArgumentException>(() => new ModelSettingsChatClient(inner, selection));

        // Assert: the setting is named, with the figure and the remedy
        Assert.Equal("selection", failure.ParamName);
        Assert.Contains(
            "ContextWindowTokens (8,192) is not applied by any client beneath",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Contains("OllamaContextSizingChatClient", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a selection whose window differs from the one applied beneath is refused, because
    ///     the provider would run at a figure the selection did not choose.
    /// </summary>
    [Fact]
    public void ModelSettingsChatClient_Construct_WindowAppliedAtAnotherSize_Throws()
    {
        // Arrange: the client beneath applies 4,096 tokens; the selection chose 8,192
        var selection = new ModelSettings("vision-large") { ContextWindowTokens = 8192 }
            .Bind(ModelTestData.VisionModel());
        using var inner = new WindowApplyingChatClient(new OptionsRecordingChatClient(), 4096);

        // Act
        var failure = Assert.Throws<ArgumentException>(() => new ModelSettingsChatClient(inner, selection));

        // Assert
        Assert.Equal("selection", failure.ParamName);
        Assert.Contains(
            "ContextWindowTokens (8,192) differs from the 4,096 tokens",
            failure.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a selection's window is accepted when a client beneath — found through delegating
    ///     clients between — applies the same window, and the rest of the selection still rides on
    ///     every request.
    /// </summary>
    [Fact]
    public async Task ModelSettingsChatClient_Construct_WindowAppliedBeneath_IsAccepted()
    {
        // Arrange: the window-applying client sits two layers down
        var recording = new OptionsRecordingChatClient();
        var selection = new ModelSettings("vision-large") { ContextWindowTokens = 8192, MaxOutputTokens = 100 }
            .Bind(ModelTestData.VisionModel());
        using var client = new ModelSettingsChatClient(
            new PassThroughChatClient(new WindowApplyingChatClient(recording, 8192)),
            selection);

        // Act
        await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var options = Assert.Single(recording.Received);
        Assert.Equal("vision-large", options.ModelId);
        Assert.Equal(100, options.MaxOutputTokens);
    }

    /// <summary>
    ///     A delegating client that reports it applies a context window, standing in for a
    ///     provider's sizing client.
    /// </summary>
    private sealed class WindowApplyingChatClient(IChatClient inner, int window)
        : DelegatingChatClient(inner), IContextWindowChatClient
    {
        /// <inheritdoc/>
        public int ContextWindowTokens { get; } = window;
    }

    /// <summary>
    ///     A delegating client that adds nothing, so a service is found only by asking beneath it.
    /// </summary>
    private sealed class PassThroughChatClient(IChatClient inner) : DelegatingChatClient(inner);

    /// <summary>
    ///     A chat client that records the options of every request and answers with a fixed reply.
    /// </summary>
    private sealed class OptionsRecordingChatClient : IChatClient
    {
        /// <summary>
        ///     Gets the options each request arrived with, in order.
        /// </summary>
        public List<ChatOptions> Received { get; } = [];

        /// <inheritdoc/>
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Received.Add(options ?? new ChatOptions());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Received.Add(options ?? new ChatOptions());
            await Task.CompletedTask;
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        /// <inheritdoc/>
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        /// <inheritdoc/>
        public void Dispose()
        {
            // Nothing is held.
        }
    }
}
