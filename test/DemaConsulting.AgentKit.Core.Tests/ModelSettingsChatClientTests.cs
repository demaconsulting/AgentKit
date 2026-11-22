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
