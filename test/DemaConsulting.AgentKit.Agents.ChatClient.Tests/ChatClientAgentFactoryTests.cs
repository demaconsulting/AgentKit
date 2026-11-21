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
    ///     Builds a no-op tool carrying the given name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) =>
        AIFunctionFactory.Create(() => "ok", name);
}
