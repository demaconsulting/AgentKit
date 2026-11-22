using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     Unit tests for <see cref="ChatClientAgentFactory"/>: the unconditional decorator seam and
///     construction-time validation.
/// </summary>
public class ChatClientAgentFactoryTests
{
    /// <summary>
    ///     Proves the decorator is installed unconditionally: the seam the factory uses to wrap the
    ///     supplied client returns an <see cref="ImagePromotingChatClient"/>. This is the guarantee
    ///     the package exists for, asserted directly rather than assumed.
    /// </summary>
    [Fact]
    public void ChatClientAgentFactory_WrapWithImagePromotion_ReturnsImagePromotingClient()
    {
        // Arrange: any chat client
        var client = new ScriptedChatClient();

        // Act: wrap it through the seam the factory uses on every agent it builds
        var wrapped = ChatClientAgentFactory.WrapWithImagePromotion(client);

        // Assert: the returned client is the image-promoting decorator
        Assert.IsType<ImagePromotingChatClient>(wrapped);
    }

    /// <summary>
    ///     Proves a null client is refused as a programming error before any agent is built.
    /// </summary>
    [Fact]
    public void ChatClientAgentFactory_Create_NullClient_Throws()
    {
        var tools = new List<AIFunction> { MakeTool("tool_one") };

        Assert.Throws<ArgumentNullException>(() => ChatClientAgentFactory.Create(null!, tools));
    }

    /// <summary>
    ///     Proves a null tool list is refused.
    /// </summary>
    [Fact]
    public void ChatClientAgentFactory_Create_NullTools_Throws()
    {
        var client = new ScriptedChatClient();

        Assert.Throws<ArgumentNullException>(() => ChatClientAgentFactory.Create(client, null!));
    }

    /// <summary>
    ///     Proves an empty tool list is refused: an agent with no tools is a defect in the host.
    /// </summary>
    [Fact]
    public void ChatClientAgentFactory_Create_EmptyTools_Throws()
    {
        var client = new ScriptedChatClient();

        Assert.Throws<ArgumentException>(() => ChatClientAgentFactory.Create(client, []));
    }

    /// <summary>
    ///     Proves a null entry in the tool list is refused.
    /// </summary>
    [Fact]
    public void ChatClientAgentFactory_Create_NullToolEntry_Throws()
    {
        var client = new ScriptedChatClient();
        var tools = new List<AIFunction> { MakeTool("tool_one"), null! };

        Assert.Throws<ArgumentNullException>(() => ChatClientAgentFactory.Create(client, tools));
    }

    /// <summary>
    ///     Proves two tools sharing a name are refused: which one a model would invoke is undefined.
    /// </summary>
    [Fact]
    public void ChatClientAgentFactory_Create_DuplicateToolNames_Throws()
    {
        var client = new ScriptedChatClient();
        var tools = new List<AIFunction> { MakeTool("tool_one"), MakeTool("tool_one") };

        var ex = Assert.Throws<ArgumentException>(() => ChatClientAgentFactory.Create(client, tools));
        Assert.Contains("tool_one", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the builder extension installs the decorator beneath function invocation: in a
    ///     pipeline built with <c>UseFunctionInvocation().UseImagePromotion()</c>, an image a tool
    ///     returns reaches the inner client on a user message and the tool result carries only a
    ///     placeholder.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ChatClientAgentFactory_UseImagePromotion_InstallsDecoratorBeneathFunctionInvocation()
    {
        // Arrange: a provider that calls an image tool and then answers, in a host-built pipeline
        var image = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        var tool = AIFunctionFactory.Create(() => image, "image_read", "Reads an image.");
        var provider = new RecordingChatClient()
            .Queue(RecordingChatClient.Answer(
                [new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "image_read")])],
                inputTokens: 10))
            .Queue("a chart", inputTokens: 20);
        using var pipeline = provider.AsBuilder().UseFunctionInvocation().UseImagePromotion().Build();

        // Act: one request with the tool available
        await pipeline.GetResponseAsync(
            "What is in the image?",
            new ChatOptions { Tools = [tool] },
            TestContext.Current.CancellationToken);

        // Assert: the second request carries the placeholder in the tool result and the image on a user message
        var second = provider.Requests[1].Messages;
        var result = second.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Single();
        Assert.StartsWith("[Image promoted", Assert.IsType<string>(result.Result), StringComparison.Ordinal);
        Assert.Contains(
            second.Where(message => message.Role == ChatRole.User).SelectMany(message => message.Contents),
            content => ReferenceEquals(content, image));
    }

    /// <summary>
    ///     Proves the decorator placed above function invocation, where it would never see a tool
    ///     result, is refused when the pipeline is built.
    /// </summary>
    [Fact]
    public void ChatClientAgentFactory_UseImagePromotion_AboveFunctionInvocation_Throws()
    {
        // Arrange: a builder with the calls in the wrong order
        var builder = new ScriptedChatClient().AsBuilder().UseImagePromotion().UseFunctionInvocation();

        // Act / Assert: building the misplaced pipeline is refused, naming the fix
        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("UseFunctionInvocation()", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing builder is a programming error.
    /// </summary>
    [Fact]
    public void ChatClientAgentFactory_UseImagePromotion_NullBuilder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ChatClientAgentFactory.UseImagePromotion(null!));
    }

    /// <summary>
    ///     Proves an agent built with a model selection sends the selected model and effort on its
    ///     requests.
    /// </summary>
    [Fact]
    public async Task ChatClientAgentFactory_Create_WithSelection_RequestsCarryTheSelection()
    {
        // Arrange: a client that records what it was asked, and a validated selection
        var client = RecordingChatClient.Answering("answer", inputTokens: 10);
        var selection = new ModelSettings("vision-large") { ReasoningEffort = "low", MaxOutputTokens = 500 }
            .Bind(SelectableModel());
        var agent = ChatClientAgentFactory.Create(client, [MakeTool("tool_one")], selection: selection);

        // Act
        await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var options = Assert.Single(client.Requests).Options;
        Assert.NotNull(options);
        Assert.Equal("vision-large", options.ModelId);
        Assert.Equal(ReasoningEffort.Low, options.Reasoning?.Effort);
        Assert.Equal(500, options.MaxOutputTokens);
    }

    /// <summary>
    ///     Proves a selection an <see cref="IChatClient"/> cannot apply is refused when the agent is
    ///     built rather than dropped.
    /// </summary>
    [Fact]
    public void ChatClientAgentFactory_Create_SelectionWithATier_Throws()
    {
        // Arrange
        var selection = new ModelSettings("vision-large") { ContextTier = "default" }.Bind(SelectableModel());

        // Act / Assert
        Assert.Throws<ArgumentException>(
            () => ChatClientAgentFactory.Create(new ScriptedChatClient(), [MakeTool("tool_one")], selection: selection));
    }

    /// <summary>
    ///     Proves a selection carrying a context window is refused when the client does not apply
    ///     that window, rather than the window being dropped, and accepted when it does.
    /// </summary>
    [Fact]
    public void ChatClientAgentFactory_Create_SelectionWithAnUnappliedWindow_Throws()
    {
        // Arrange
        var selection = new ModelSettings("vision-large") { ContextWindowTokens = 8192 }.Bind(SelectableModel());

        // Act
        var failure = Assert.Throws<ArgumentException>(
            () => ChatClientAgentFactory.Create(
                new ScriptedChatClient(),
                [MakeTool("tool_one")],
                selection: selection));
        var agent = ChatClientAgentFactory.Create(
            new WindowApplyingChatClient(new ScriptedChatClient(), 8192),
            [MakeTool("tool_one")],
            selection: selection);

        // Assert: the refusal names the setting; the same selection over a client applying it builds
        Assert.Contains("ContextWindowTokens", failure.Message, StringComparison.Ordinal);
        Assert.NotNull(agent);
    }

    /// <summary>
    ///     Proves the builder extension installs the selection in a host-built pipeline.
    /// </summary>
    [Fact]
    public async Task ChatClientAgentFactory_UseModelSettings_HostPipeline_RequestsCarryTheSelection()
    {
        // Arrange
        var client = RecordingChatClient.Answering("answer", inputTokens: 10);
        var selection = new ModelSettings("vision-large") { ReasoningEffort = "low" }.Bind(SelectableModel());
        using var pipeline = client.AsBuilder().UseModelSettings(selection).Build();

        // Act
        await pipeline.GetResponseAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var options = Assert.Single(client.Requests).Options;
        Assert.Equal("vision-large", options?.ModelId);
        Assert.Equal(ReasoningEffort.Low, options?.Reasoning?.Effort);
    }

    /// <summary>
    ///     Proves a host-built pipeline given a selection carrying a context window is refused when it
    ///     is built if no client beneath applies that window, and builds when one does.
    /// </summary>
    [Fact]
    public void ChatClientAgentFactory_UseModelSettings_SelectionWithAWindow_NeedsItAppliedBeneath()
    {
        // Arrange
        var selection = new ModelSettings("vision-large") { ContextWindowTokens = 8192 }.Bind(SelectableModel());

        // Act
        var failure = Assert.Throws<ArgumentException>(
            () => new ScriptedChatClient().AsBuilder().UseModelSettings(selection).Build());
        using var pipeline = new WindowApplyingChatClient(new ScriptedChatClient(), 8192)
            .AsBuilder()
            .UseModelSettings(selection)
            .Build();

        // Assert: the refusal names the setting; the pipeline over a window-applying client builds
        Assert.Contains("ContextWindowTokens", failure.Message, StringComparison.Ordinal);
        Assert.NotNull(pipeline);
    }

    /// <summary>
    ///     Proves a missing builder or selection is a programming error.
    /// </summary>
    [Fact]
    public void ChatClientAgentFactory_UseModelSettings_NullArguments_Throw()
    {
        // Arrange
        var selection = new ModelSettings("vision-large").Bind(SelectableModel());

        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => ChatClientAgentFactory.UseModelSettings(null!, selection));
        Assert.Throws<ArgumentNullException>(() => new ScriptedChatClient().AsBuilder().UseModelSettings(null!));
    }

    /// <summary>
    ///     Builds a model an application might describe for an <see cref="IChatClient"/> provider.
    /// </summary>
    /// <returns>The model.</returns>
    internal static ModelInfo SelectableModel() => new()
    {
        Provider = "test",
        Id = "vision-large",
        DisplayName = "Vision Large",
        Capabilities = ModelCapabilities.ToolCalls | ModelCapabilities.Vision,
        Adjustments = ModelAdjustments.ReasoningEffort
            | ModelAdjustments.ContextTier
            | ModelAdjustments.ContextWindow
            | ModelAdjustments.MaxOutputTokens,
        ReasoningEfforts = [new ModelReasoningEffort("low", "Low", ReasoningEffort.Low)],
        ContextTiers = [new ModelContextTier("default", "Standard", null)],
    };

    /// <summary>
    ///     A delegating client that reports it applies a context window, standing in for a
    ///     provider's sizing client such as Ollama's.
    /// </summary>
    /// <param name="inner">The client requests are forwarded to.</param>
    /// <param name="window">The window this client reports it applies.</param>
    internal sealed class WindowApplyingChatClient(IChatClient inner, int window)
        : DelegatingChatClient(inner), IContextWindowChatClient
    {
        /// <inheritdoc/>
        public int ContextWindowTokens { get; } = window;
    }

    /// <summary>
    ///     Builds a no-op tool carrying the given name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) =>
        AIFunctionFactory.Create(() => "ok", name);
}
