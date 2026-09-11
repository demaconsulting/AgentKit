using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="ImagePromotingChatClient"/> class.
/// </summary>
/// <remarks>
///     <para>
///     Every scenario asserts on the conversation the wrapped client received, because that is
///     the whole of this unit's behavior: it forwards a request whose message sequence has been
///     rewritten. A scripted client is therefore sufficient and a real provider would add
///     nothing — the channel asymmetry that motivates the decorator happens at a provider's wire
///     format and cannot be observed from inside this process at all.
///     </para>
///     <para>
///     The promoted content is asserted by reference where possible, so that a future change
///     re-encoding or copying the image would fail here rather than silently changing what the
///     model sees.
///     </para>
/// </remarks>
public class ImagePromotingChatClientTests
{
    /// <summary>
    ///     The bytes every scenario uses as a stand-in for a real image.
    /// </summary>
    private static readonly byte[] SampleBytes = [0x89, 0x50, 0x4E, 0x47];

    /// <summary>
    ///     Proves that an image returned by a tool is carried onto a following user message.
    /// </summary>
    /// <remarks>
    ///     This is the scenario the unit exists for. On a provider that drops images from tool
    ///     responses, the user message is the one channel that reliably carries them.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_ToolResultWithImage_AddsUserMessageCarryingTheSameDataContent()
    {
        // Arrange: a conversation whose last message is a tool result holding an image
        var image = new DataContent(SampleBytes, "image/png");
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What does the diagram show?"),
            ToolMessage(image)
        };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: one extra user message, carrying the very same content instance
        Assert.Equal(3, inner.ReceivedMessages.Count);
        var promoted = inner.ReceivedMessages[2];
        Assert.Equal(ChatRole.User, promoted.Role);
        Assert.Same(image, Assert.IsType<DataContent>(promoted.Contents[1]));
    }

    /// <summary>
    ///     Proves that only the image parts of a caption-plus-content result are promoted.
    /// </summary>
    /// <remarks>
    ///     A guarded tool returns an image as a caption followed by the content, so the list
    ///     shape must be read. The caption already reached the provider as text in the tool
    ///     result, and duplicating it would tell the model the same thing twice.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_ToolResultWithContentList_PromotesOnlyTheImageParts()
    {
        // Arrange: a tool result in the caption-then-image shape the result constructors produce
        var image = new DataContent(SampleBytes, "image/png");
        var parts = new List<AIContent> { new TextContent("A screenshot."), image };
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage> { ToolMessage(parts) };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the announcement and the image, and nothing copied from the caption
        Assert.Equal(2, inner.ReceivedMessages.Count);
        var promoted = inner.ReceivedMessages[1].Contents;
        Assert.Equal(2, promoted.Count);
        Assert.Same(image, Assert.IsType<DataContent>(promoted[1]));
        Assert.DoesNotContain(
            "A screenshot.",
            Assert.IsType<TextContent>(promoted[0]).Text,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that the promoted message says where the attachment came from.
    /// </summary>
    /// <remarks>
    ///     A bare attachment reads to the model as an image the user has just sent, unrelated to
    ///     the tool call above it. Naming its origin is what ties the two together.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_ToolResultWithImage_PromotedMessageAnnouncesTheAttachment()
    {
        // Arrange: a tool result holding an image
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage> { ToolMessage(new DataContent(SampleBytes, "image/png")) };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: text first, naming the tool as the source, then the attachment
        var promoted = inner.ReceivedMessages[1].Contents;
        var announcement = Assert.IsType<TextContent>(promoted[0]);
        Assert.Contains("tool", announcement.Text, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<DataContent>(promoted[1]);
    }

    /// <summary>
    ///     Proves that a text-only tool result adds nothing to the conversation.
    /// </summary>
    /// <remarks>
    ///     There is nothing a provider would drop, so a promoted message would spend context on
    ///     an announcement that no image was attached.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_TextOnlyToolResult_AddsNoMessage()
    {
        // Arrange: a tool result carrying the text a read tool returns
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage> { ToolMessage("the contents of a file") };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the conversation is forwarded exactly as it arrived
        Assert.Single(inner.ReceivedMessages);
    }

    /// <summary>
    ///     Proves that a tool result carrying non-image content adds nothing to the conversation.
    /// </summary>
    /// <remarks>
    ///     Only images are dropped at the wire by the providers this unit exists for, so content
    ///     of any other media type is deliberately left where the tool put it. Promoting it would
    ///     present a document to the model as though the user had just attached it.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_NonImageDataContentResult_AddsNoMessage()
    {
        // Arrange: a tool result carrying binary content that is not an image
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage> { ToolMessage(new DataContent(SampleBytes, "application/pdf")) };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the conversation is forwarded exactly as it arrived
        Assert.Single(inner.ReceivedMessages);
    }

    /// <summary>
    ///     Proves that a promoted message is inserted immediately after the tool result it came
    ///     from rather than appended to the end of the conversation.
    /// </summary>
    /// <remarks>
    ///     Promoted to the end, the image would be read as belonging to whichever turn happened
    ///     to be last, and any message the host had already appended would be displaced.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_MessageAfterToolResult_PromotedMessageIsInsertedImmediatelyAfterIt()
    {
        // Arrange: a conversation that continues past the tool result holding the image
        var image = new DataContent(SampleBytes, "image/png");
        var trailing = new ChatMessage(ChatRole.Assistant, "Let me look at that.");
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What does the diagram show?"),
            ToolMessage(image),
            trailing
        };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the promoted message sits between the tool result and the message after it
        Assert.Equal(4, inner.ReceivedMessages.Count);
        var promoted = inner.ReceivedMessages[2];
        Assert.Equal(ChatRole.User, promoted.Role);
        Assert.Same(image, Assert.IsType<DataContent>(promoted.Contents[1]));
        Assert.Same(trailing, inner.ReceivedMessages[3]);
    }

    /// <summary>
    ///     Proves that messages carrying no tool result pass through in their original order.
    /// </summary>
    /// <remarks>
    ///     Order is the contract: the model reads the exchange in sequence, so a decorator that
    ///     reordered or dropped a message would change the meaning of the conversation.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_NonToolMessages_ArePassedThroughInOrder()
    {
        // Arrange: an ordinary exchange with no tool result in it at all
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.User, "Hello."),
            new(ChatRole.Assistant, "Hello to you.")
        };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the same messages, in the same order, by reference
        Assert.Equal(messages, inner.ReceivedMessages);
    }

    /// <summary>
    ///     Proves that the streaming path promotes an image exactly as the non-streaming path
    ///     does.
    /// </summary>
    /// <remarks>
    ///     A host that streams would otherwise silently lose the control. The difference between
    ///     the two paths is in how the response is delivered, not in what the request carries.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetStreamingResponseAsync_ToolResultWithImage_PromotesTheImage()
    {
        // Arrange: a tool result holding an image
        var image = new DataContent(SampleBytes, "image/png");
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage> { ToolMessage(image) };

        // Act: consume the stream, which is what causes the request to be forwarded
        await foreach (var update in client.GetStreamingResponseAsync(
                           messages,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.NotNull(update);
        }

        // Assert: the same promotion the non-streaming path performs
        Assert.Equal(2, inner.ReceivedMessages.Count);
        Assert.Same(image, Assert.IsType<DataContent>(inner.ReceivedMessages[1].Contents[1]));
    }

    /// <summary>
    ///     Proves that the decorator cannot be created without a client to forward to.
    /// </summary>
    [Fact]
    public void ImagePromotingChatClient_Constructor_NullInnerClient_ThrowsArgumentNullException()
    {
        // Act & Assert: a decorator with nothing to decorate is a programming error
        Assert.Throws<ArgumentNullException>(() => new ImagePromotingChatClient(null!));
    }

    /// <summary>
    ///     Builds the tool message a function-invocation loop appends after a tool has run.
    /// </summary>
    /// <remarks>
    ///     The decorator reads a function result rather than a message's content directly, so
    ///     the scenarios must present the same shape the loop produces rather than a simplified
    ///     stand-in.
    /// </remarks>
    /// <param name="result">The value the tool returned.</param>
    /// <returns>The tool message carrying that result.</returns>
    private static ChatMessage ToolMessage(object result)
    {
        return new ChatMessage(
            ChatRole.Tool,
            [new FunctionResultContent("call-1", result)]);
    }
}
