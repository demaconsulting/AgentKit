using DemaConsulting.AgentKit.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     System-level integration tests for the AgentKitAgentsChatClient system.
/// </summary>
/// <remarks>
///     These tests exercise the behavioral guarantee the package exists for: an agent built by
///     the factory talks to its provider through an <see cref="ImagePromotingChatClient"/>, so a
///     tool-returned image reaches the model on a user message even on a provider that would
///     otherwise drop it. The decorator installation is asserted, not assumed.
/// </remarks>
public class AgentKitAgentsChatClientTests
{
    /// <summary>
    ///     A small image payload; its bytes are unimportant, only that the same instance is carried
    ///     through unchanged.
    /// </summary>
    private static readonly byte[] ImageBytes = [0x89, 0x50, 0x4E, 0x47];

    /// <summary>
    ///     Proves that the factory builds a usable agent carrying the supplied name and instructions.
    /// </summary>
    [Fact]
    public void AgentKitAgentsChatClient_Create_BuildsAgentCarryingSuppliedName()
    {
        // Arrange: a scripted client and a single tool
        var client = new ScriptedChatClient();
        var tools = new List<AIFunction> { MakeTool("tool_one") };

        // Act: build the agent through the factory
        var agent = ChatClientAgentFactory.Create(client, tools, instructions: "be concise", name: "assistant");

        // Assert: an agent was built and carries the supplied name
        Assert.NotNull(agent);
        Assert.Equal("assistant", agent.Name);
    }

    /// <summary>
    ///     Proves the whole point of the package: an agent built by the factory installs the
    ///     image-promoting decorator, so a tool-returned image is carried onto a user message the
    ///     provider receives. The factory wraps the supplied client through the same seam exercised
    ///     here, so the conversation the provider observes is promoted.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsChatClient_ImagePromotion_ToolReturnedImage_ReachesProviderOnUserMessage()
    {
        // Arrange: the client an agent built by the factory would talk through
        var provider = new ScriptedChatClient();
        var promotingClient = ChatClientAgentFactory.WrapWithImagePromotion(provider);

        var image = new DataContent(ImageBytes, "image/png");
        var conversation = new List<ChatMessage>
        {
            new(ChatRole.User, "look at the diagram"),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", image)]),
        };

        // Act: send the conversation as the function-invocation loop would, after the tool ran
        await promotingClient.GetResponseAsync(conversation, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the provider received an extra user message carrying the very same image instance
        Assert.Equal(3, provider.ReceivedMessages.Count);
        var promoted = provider.ReceivedMessages[2];
        Assert.Equal(ChatRole.User, promoted.Role);
        Assert.Contains(promoted.Contents.OfType<DataContent>(), part => ReferenceEquals(part, image));
    }

    /// <summary>
    ///     Proves that an agent the factory builds actually talks through the image-promoting
    ///     decorator: the decorator is found in the chat client chain of the constructed agent.
    /// </summary>
    /// <remarks>
    ///     The image-promotion scenario exercises the wrap seam directly and so cannot observe
    ///     whether <see cref="ChatClientAgentFactory.Create"/> still calls it. This scenario closes
    ///     that gap by asking the constructed agent itself for the decorator, so removing the wrap
    ///     from the construction path fails here.
    /// </remarks>
    [Fact]
    public void AgentKitAgentsChatClient_Create_InstallsImagePromotingDecoratorOnTheAgent()
    {
        // Arrange: a scripted client and a single tool
        var client = new ScriptedChatClient();
        var tools = new List<AIFunction> { MakeTool("tool_one") };

        // Act: build the agent through the factory and ask it for the decorator
        var agent = ChatClientAgentFactory.Create(client, tools);
        var decorator = agent.GetService(typeof(ImagePromotingChatClient));

        // Assert: the agent's chat client chain carries the decorator this package exists to install
        Assert.IsType<ImagePromotingChatClient>(decorator);
    }

    /// <summary>
    ///     Builds a no-op tool carrying the given name, for exercising the factory.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) =>
        AIFunctionFactory.Create(() => "ok", name);
}
