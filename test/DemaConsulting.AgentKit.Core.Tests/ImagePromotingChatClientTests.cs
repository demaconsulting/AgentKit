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
    ///     Proves that the tool result the wrapped client receives carries a placeholder in place
    ///     of a promoted image, and no image content at all.
    /// </summary>
    /// <remarks>
    ///     A provider adapter that cannot carry an image in a tool response may serialize it into
    ///     the tool message as text. The placeholder is what stops the image being sent twice,
    ///     once as a context-flooding base64 string.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_ToolResultWithImage_ToolMessageCarriesPlaceholderNotImage()
    {
        // Arrange: a tool result holding an image
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage> { ToolMessage(new DataContent(SampleBytes, "image/png")) };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the forwarded result is the placeholder text, tied to the same call
        var forwarded = Assert.IsType<FunctionResultContent>(Assert.Single(inner.ReceivedMessages[0].Contents));
        Assert.Equal("call-1", forwarded.CallId);
        var placeholder = Assert.IsType<string>(forwarded.Result);
        Assert.Equal("[Image promoted to the following user message: image/png, 4 bytes.]", placeholder);
        Assert.DoesNotContain(inner.ReceivedMessages[0].Contents, content => content is DataContent);
    }

    /// <summary>
    ///     Proves that a large tool-returned image leaves a tool message whose text is small, while
    ///     the promoted user message carries the image intact.
    /// </summary>
    /// <remarks>
    ///     A 316 KB image once produced a 421,629-character tool message that truncated the context
    ///     window. The size of the forwarded tool message must not grow with the size of the image.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_LargeImageToolResult_ForwardedToolMessageIsSmall()
    {
        // Arrange: a tool result holding a 300 KB image
        var bytes = new byte[300_000];
        var image = new DataContent(bytes, "image/png");
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage> { ToolMessage(image) };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the tool message text is short and names the size; the image is on the user message
        var forwarded = Assert.IsType<FunctionResultContent>(Assert.Single(inner.ReceivedMessages[0].Contents));
        var text = Assert.IsType<string>(forwarded.Result);
        Assert.True(text.Length < 200, $"Tool message text was {text.Length} characters.");
        Assert.Contains("300,000 bytes", text, StringComparison.Ordinal);
        var promoted = Assert.IsType<DataContent>(inner.ReceivedMessages[1].Contents[1]);
        Assert.Same(image, promoted);
        Assert.Equal(300_000, promoted.Data.Length);
    }

    /// <summary>
    ///     Proves that a caption-plus-image result keeps its caption, in order, with the image
    ///     replaced by its placeholder.
    /// </summary>
    /// <remarks>
    ///     The caption is what the tool said about the image, so it stays in the tool result where
    ///     the model reads it; only the bytes move.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_ToolResultWithCaptionAndImage_CaptionKeptImageReplaced()
    {
        // Arrange: a tool result in the caption-then-image shape the result constructors produce
        var caption = new TextContent("A screenshot.");
        var parts = new List<AIContent> { caption, new DataContent(SampleBytes, "image/jpeg") };
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage> { ToolMessage(parts) };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the caption first, then the placeholder naming the media type; no image remains
        var forwarded = Assert.IsType<FunctionResultContent>(Assert.Single(inner.ReceivedMessages[0].Contents));
        var forwardedParts = Assert.IsAssignableFrom<IEnumerable<AIContent>>(forwarded.Result).ToList();
        Assert.Equal(2, forwardedParts.Count);
        Assert.Same(caption, forwardedParts[0]);
        var placeholder = Assert.IsType<TextContent>(forwardedParts[1]);
        Assert.Contains("image/jpeg", placeholder.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(forwardedParts, part => part is DataContent);
    }

    /// <summary>
    ///     Proves that the caller's messages still carry the image after the request is forwarded.
    /// </summary>
    /// <remarks>
    ///     The history the host or the function-invocation loop holds must record what the tool
    ///     actually returned, so the rewrite is applied to a copy rather than to the original.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_CallerMessages_AreNotModified()
    {
        // Arrange: a tool message the caller holds and will keep using
        var image = new DataContent(SampleBytes, "image/png");
        var original = ToolMessage(image);
        var originalResult = Assert.IsType<FunctionResultContent>(original.Contents[0]);
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage> { original };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the caller's list, message and result are untouched and still carry the image
        Assert.Single(messages);
        Assert.Same(originalResult, Assert.Single(original.Contents));
        Assert.Same(image, originalResult.Result);
        Assert.NotSame(original, inner.ReceivedMessages[0]);
    }

    /// <summary>
    ///     Proves that the streaming path leaves the same placeholder in the tool result as the
    ///     non-streaming path does.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetStreamingResponseAsync_ToolResultWithImage_ToolMessageCarriesPlaceholder()
    {
        // Arrange: a tool result holding an image
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage> { ToolMessage(new DataContent(SampleBytes, "image/png")) };

        // Act: consume the stream, which is what causes the request to be forwarded
        await foreach (var update in client.GetStreamingResponseAsync(
                           messages,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.NotNull(update);
        }

        // Assert: the tool result is the placeholder, exactly as on the non-streaming path
        var forwarded = Assert.IsType<FunctionResultContent>(Assert.Single(inner.ReceivedMessages[0].Contents));
        Assert.StartsWith("[Image promoted", Assert.IsType<string>(forwarded.Result), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a content list carrying two images has each replaced in place by its own
    ///     placeholder, and that both are promoted in the order they appeared.
    /// </summary>
    /// <remarks>
    ///     A tool may return several images interleaved with text. Every image must leave the
    ///     tool result, the surrounding text must keep its position, and the placeholders must
    ///     be distinguishable so the model can match each one to the image it stands for.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_ToolResultWithTwoImagesInList_EachReplacedInPlaceAndBothPromotedInOrder()
    {
        // Arrange: a tool result interleaving text with two images of different types and sizes
        var png = new DataContent(SampleBytes, "image/png");
        var jpeg = new DataContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }, "image/jpeg");
        var first = new TextContent("Before:");
        var second = new TextContent("After:");
        var parts = new List<AIContent> { first, png, second, jpeg };
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Compare them."),
            ToolMessage(parts)
        };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: each image replaced in place by its own placeholder, the text kept where it was
        Assert.Equal(3, inner.ReceivedMessages.Count);
        var forwarded = Assert.IsType<FunctionResultContent>(Assert.Single(inner.ReceivedMessages[1].Contents));
        Assert.Equal("call-1", forwarded.CallId);
        var forwardedParts = Assert.IsAssignableFrom<IEnumerable<AIContent>>(forwarded.Result).ToList();
        Assert.Equal(4, forwardedParts.Count);
        Assert.Same(first, forwardedParts[0]);
        Assert.Equal(
            "[Image promoted to the following user message: image/png, 4 bytes.]",
            Assert.IsType<TextContent>(forwardedParts[1]).Text);
        Assert.Same(second, forwardedParts[2]);
        Assert.Equal(
            "[Image promoted to the following user message: image/jpeg, 6 bytes.]",
            Assert.IsType<TextContent>(forwardedParts[3]).Text);
        Assert.DoesNotContain(forwardedParts, part => part is DataContent);

        // Assert: one user message announcing the attachments, then both images in their order
        var promoted = inner.ReceivedMessages[2];
        Assert.Equal(ChatRole.User, promoted.Role);
        Assert.Equal(3, promoted.Contents.Count);
        Assert.IsType<TextContent>(promoted.Contents[0]);
        Assert.Same(png, promoted.Contents[1]);
        Assert.Same(jpeg, promoted.Contents[2]);
    }

    /// <summary>
    ///     Proves that a tool message carrying parallel function results, each holding an image,
    ///     has every result replaced while the caller's message and results are left untouched.
    /// </summary>
    /// <remarks>
    ///     A function-invocation loop running calls in parallel appends one tool message holding
    ///     a result per call. Every result must be rewritten and keep its own call identifier,
    ///     because a result paired with the wrong call is a different answer; the images must be
    ///     promoted once, together, after that one tool message; and the caller's copy must still
    ///     record what each tool actually returned.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_ToolMessageWithParallelImageResults_EveryResultReplacedAndCallerUntouched()
    {
        // Arrange: one tool message with two results, a lone image and a caption-then-image list
        var png = new DataContent(SampleBytes, "image/png");
        var jpeg = new DataContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }, "image/jpeg");
        var caption = new TextContent("A chart.");
        var captionedParts = new List<AIContent> { caption, jpeg };
        var firstResult = new FunctionResultContent("call-1", png);
        var secondResult = new FunctionResultContent("call-2", captionedParts);
        var originalContents = new List<AIContent> { firstResult, secondResult };
        var toolMessage = new ChatMessage(ChatRole.Tool, originalContents);
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Draw both."),
            toolMessage
        };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: a forwarded copy whose results keep their call identifiers, in order
        Assert.Equal(3, inner.ReceivedMessages.Count);
        var forwarded = inner.ReceivedMessages[1];
        Assert.NotSame(toolMessage, forwarded);
        Assert.Equal(ChatRole.Tool, forwarded.Role);
        var results = forwarded.Contents.Cast<FunctionResultContent>().ToList();
        Assert.Equal(2, results.Count);
        Assert.Equal(["call-1", "call-2"], results.Select(result => result.CallId));

        // Assert: each result carries its placeholder in place of its image, and no image remains
        Assert.Equal(
            "[Image promoted to the following user message: image/png, 4 bytes.]",
            Assert.IsType<string>(results[0].Result));
        var secondParts = Assert.IsAssignableFrom<IEnumerable<AIContent>>(results[1].Result).ToList();
        Assert.Equal(2, secondParts.Count);
        Assert.Same(caption, secondParts[0]);
        Assert.Equal(
            "[Image promoted to the following user message: image/jpeg, 6 bytes.]",
            Assert.IsType<TextContent>(secondParts[1]).Text);
        Assert.DoesNotContain(forwarded.Contents, content => content is DataContent);
        Assert.DoesNotContain(results, ContainsImageBytes);

        // Assert: one user message after the tool message carrying both images in order
        var promoted = inner.ReceivedMessages[2];
        Assert.Equal(ChatRole.User, promoted.Role);
        Assert.Equal(3, promoted.Contents.Count);
        Assert.IsType<TextContent>(promoted.Contents[0]);
        Assert.Same(png, promoted.Contents[1]);
        Assert.Same(jpeg, promoted.Contents[2]);

        // Assert: the caller's message, results and images are untouched
        Assert.Equal(2, messages.Count);
        Assert.Same(originalContents, toolMessage.Contents);
        Assert.Equal(2, originalContents.Count);
        Assert.Same(firstResult, originalContents[0]);
        Assert.Same(secondResult, originalContents[1]);
        Assert.Same(png, firstResult.Result);
        Assert.Same(captionedParts, secondResult.Result);
        Assert.Equal(2, captionedParts.Count);
        Assert.Same(jpeg, captionedParts[1]);

        // Reports whether a function result still carries image bytes, alone or in a content list
        static bool ContainsImageBytes(FunctionResultContent result)
        {
            return result.Result switch
            {
                DataContent => true,
                IEnumerable<AIContent> contents => contents.Any(content => content is DataContent),
                _ => false
            };
        }
    }

    /// <summary>
    ///     Proves that a rewritten tool message and its rewritten function result carry no
    ///     provider-native raw representation, while an untouched message keeps its own.
    /// </summary>
    /// <remarks>
    ///     A raw representation is the provider-native form of the original object, so it may
    ///     still hold the image bytes. An adapter that prefers a raw object over the contents
    ///     would otherwise resend exactly what the placeholder removed. The caller's objects keep
    ///     their raw forms, because the rewrite is applied to copies.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImagePromotingChatClient_GetResponseAsync_ToolMessageWithRawRepresentation_ForwardedCopyCarriesNone()
    {
        // Arrange: an image tool message and a text tool message, each with a stand-in native form
        var nativeMessage = new object();
        var nativeResult = new object();
        var nativeText = new object();
        var result = new FunctionResultContent("call-1", new DataContent(SampleBytes, "image/png"))
        {
            RawRepresentation = nativeResult
        };
        var imageTool = new ChatMessage(ChatRole.Tool, [result]) { RawRepresentation = nativeMessage };
        var textTool = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-2", "plain")])
        {
            RawRepresentation = nativeText
        };
        using var inner = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(inner);
        var messages = new List<ChatMessage> { imageTool, textTool };

        // Act: send the conversation through the decorator
        await client.GetResponseAsync(messages, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the rewritten tool message and its result carry no raw representation
        Assert.Equal(3, inner.ReceivedMessages.Count);
        var forwarded = inner.ReceivedMessages[0];
        Assert.Null(forwarded.RawRepresentation);
        var forwardedResult = Assert.IsType<FunctionResultContent>(Assert.Single(forwarded.Contents));
        Assert.Equal("call-1", forwardedResult.CallId);
        Assert.Null(forwardedResult.RawRepresentation);

        // Assert: the promoted message follows, and the untouched message keeps its raw form
        Assert.Equal(ChatRole.User, inner.ReceivedMessages[1].Role);
        Assert.Same(textTool, inner.ReceivedMessages[2]);
        Assert.Same(nativeText, inner.ReceivedMessages[2].RawRepresentation);

        // Assert: the caller's message and result keep their raw representations
        Assert.Same(nativeMessage, imageTool.RawRepresentation);
        Assert.Same(nativeResult, result.RawRepresentation);
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
