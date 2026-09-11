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
    ///     Builds a no-op tool carrying the given name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) =>
        AIFunctionFactory.Create(() => "ok", name);
}
