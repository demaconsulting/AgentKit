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
    ///     Proves the package carries a Core session over an ordinary chat client: the session
    ///     answers, and the occupancy it reports is the provider's own count taken against the window
    ///     the application stated where it configured its provider.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsChatClient_Session_AnswersAndReportsTheProvidersOccupancy()
    {
        // Arrange: a chat client answering with a count no estimate would arrive at, and a session
        // over it through the adapter this package ships
        var provider = RecordingChatClient.Answering("the answer", inputTokens: 321);
        var factory = new ChatClientProviderSessionFactory(provider, windowTokens: 8000);
        var options = new AgentSessionOptions(
            new ChatClientSummarizer(RecordingChatClient.Answering("record", inputTokens: 10)),
            instructions: "be brief");
        await using var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Act: take one turn
        var response = await session.SendAsync("what changed?", TestContext.Current.CancellationToken);

        // Assert: the answer came back, and the session's account of the window is the provider's
        // figure against the stated window rather than anything this library computed
        Assert.Equal("the answer", response.Text);
        Assert.Equal(321, session.Usage.ConversationTokens);
        Assert.Equal(8000, session.Usage.WindowTokens);
    }

    /// <summary>
    ///     Proves the whole arrangement holds together over a chat client: the provider's own usage
    ///     drives the rotation, the consolidation goes out through a separate client, the record it
    ///     produces seeds the replacement session, and that replacement reports occupying nothing
    ///     until it has sent something.
    /// </summary>
    /// <remarks>
    ///     This is the scenario the session adapter exists for, and it is asserted end to end rather
    ///     than assumed from the unit behaviors: a chat client reporting a full window is the only
    ///     thing driving it, and the marker the summarizing client returns is found in the
    ///     conversation the provider is later sent.
    /// </remarks>
    [Fact]
    public async Task AgentKitAgentsChatClient_Session_RotatesOnTheProvidersUsage_AndSeedsTheReplacement()
    {
        // Arrange: a provider reporting a conversation well past the rotation threshold of its
        // window on every turn, and a separate client performing the consolidations
        var provider = RecordingChatClient.Answering("the answer", inputTokens: 800);
        var summarizing = RecordingChatClient.Answering("CONSOLIDATED-RECORD", inputTokens: 40);
        var factory = new ChatClientProviderSessionFactory(provider, windowTokens: 1000);
        var options = new AgentSessionOptions(
            new ChatClientSummarizer(summarizing),
            instructions: "be brief",
            verbatimTurns: 2);
        await using var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Act: hold a short conversation, every turn of which finds the window filling
        AgentSessionResponse? last = null;
        for (var turn = 0; turn < 4; turn++)
        {
            last = await session.SendAsync($"question {turn}", TestContext.Current.CancellationToken);
        }

        // Assert: the session rotated on what the provider reported, the consolidation went through
        // the summarizing client, the record it produced reached the provider as seeded history, and
        // the replacement reports occupying nothing because it has not been sent anything yet
        Assert.NotNull(last);
        Assert.True(last.RotationOccurred, "A provider reporting a filling window must provoke a rotation.");
        Assert.True(session.RotationCount > 0);
        Assert.NotEmpty(summarizing.Requests);
        Assert.Contains(
            provider.Requests,
            request => request.Messages.Any(
                message => message.Text.Contains("CONSOLIDATED-RECORD", StringComparison.Ordinal)));
        Assert.Equal(0, session.Usage.UsedTokens);
        Assert.Equal(1000, session.Usage.WindowTokens);
    }

    /// <summary>
    ///     Builds a no-op tool carrying the given name, for exercising the factory.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) =>
        AIFunctionFactory.Create(() => "ok", name);
}
