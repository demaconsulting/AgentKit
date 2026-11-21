using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Makes an image a tool returned visible to a provider whose tool-result channel cannot
///     carry one, by promoting it onto a following user message.
/// </summary>
/// <remarks>
///     <para>
///     <b>The asymmetry this exists for is observed, not theoretical.</b> A tool returning image
///     content reaches the model unchanged on the GitHub Copilot path, whose runtime converts a
///     binary tool result into content the model sees. On the Ollama path the identical content
///     is preserved by the framework, arrives intact in the function result, and is then dropped
///     at the wire — that API accepts images on messages, not in tool responses. The model
///     answers anyway, describing an image it never received, and nothing in the exchange
///     reports an error.
///     </para>
///     <para>
///     <b>It is a channel problem, not a capability one.</b> A control exchange placing the same
///     bytes on a user message was described correctly by the same model on the same provider.
///     Promoting the content onto the one channel every provider honors is therefore sufficient,
///     and no re-encoding, resizing or captioning of the image is involved.
///     </para>
///     <para>
///     <b>Where this sits matters.</b> The decorator must be placed beneath the
///     function-invocation loop, so that it observes the conversation <em>after</em> tool
///     results have been appended to it. Placed above the loop it would see the request before
///     any tool had run and would have nothing to promote.
///     </para>
///     <para>
///     <b>The promoted image leaves the tool result.</b> The forwarded tool result carries a short
///     placeholder naming the image's media type and size in place of each promoted image, so
///     no image bytes remain in the tool message. This matters because a provider adapter that
///     cannot carry an image in a tool response may still serialize it there as text: OllamaSharp
///     writes such content as a base64 data URI in the tool message, so a 316 KB image became a
///     421,629-character tool message that truncated the context and lost the user's question.
///     The image is sent once, on the user message, where the model can see it. The rewritten
///     tool message and function result carry no provider-native raw representation, because
///     the original's raw form may still hold the bytes the placeholder removed.
///     </para>
///     <para>
///     <b>The caller's conversation is never modified.</b> A tool message carrying an image is
///     forwarded as a copy with the rewritten result, so the history the host or the
///     function-invocation loop holds still records what the tool actually returned. Every other
///     message is forwarded as the same instance.
///     </para>
///     <para>
///     A provider that already delivers images from tool results is unaffected by a host that
///     does not install this decorator, and a host that installs it anyway pays only for one
///     extra user message carrying content the model was meant to see. The decorator adds
///     nothing when a tool result carries no image.
///     </para>
///     <para>
///     The class holds no mutable state of its own and is as safe for concurrent use as the
///     client it wraps.
///     </para>
/// </remarks>
/// <param name="innerClient">
///     The client this decorator forwards to. Must not be <see langword="null"/>.
/// </param>
public sealed class ImagePromotingChatClient(IChatClient innerClient)
    : DelegatingChatClient(innerClient)
{
    /// <summary>
    ///     The fixed text introducing a promoted image, so the transcript reads sensibly.
    /// </summary>
    /// <remarks>
    ///     A bare attachment arriving with no explanation reads to the model as an image the
    ///     user has just sent, unrelated to the tool call above it. Naming its origin is what
    ///     ties the two together. The text is a constant: it describes only the exchange, never
    ///     the host, and nothing about the tool result is interpolated into it.
    /// </remarks>
    private const string PromotionNotice =
        "The image returned by the tool is attached below for examination.";

    /// <summary>
    ///     The format of the text left in a tool result in place of a promoted image.
    /// </summary>
    /// <remarks>
    ///     The placeholder tells the model that the tool did return an image and where to find it,
    ///     so the tool result still reads as a complete answer. It names only the media type and
    ///     the size — values the image itself carries — and never the host, a path or the bytes.
    ///     Argument 0 is the media type; argument 1 is the byte count.
    /// </remarks>
    private const string PlaceholderFormat = "[Image promoted to the following user message: {0}, {1:N0} bytes.]";

    /// <summary>
    ///     The top-level media type identifying content that must be promoted.
    /// </summary>
    private const string ImageMediaType = "image";

    /// <summary>
    ///     Forwards a request, having promoted any tool-returned image onto a user message.
    /// </summary>
    /// <remarks>
    ///     The rewrite is applied to the message sequence rather than to the options, because
    ///     the messages are the only place a tool result appears and the only channel a provider
    ///     is guaranteed to read images from.
    /// </remarks>
    /// <param name="messages">The conversation so far, including any tool results.</param>
    /// <param name="options">The request options, forwarded unchanged.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The response the wrapped client produced.</returns>
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetResponseAsync(Promote(messages), options, cancellationToken);
    }

    /// <summary>
    ///     Forwards a streaming request, having promoted any tool-returned image onto a user
    ///     message.
    /// </summary>
    /// <remarks>
    ///     Streaming and non-streaming are rewritten identically. A host that streams would
    ///     otherwise silently lose the control, and the difference between the two paths is in
    ///     how the response is delivered rather than in what the request carries.
    /// </remarks>
    /// <param name="messages">The conversation so far, including any tool results.</param>
    /// <param name="options">The request options, forwarded unchanged.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The response updates the wrapped client produced.</returns>
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingResponseAsync(Promote(messages), options, cancellationToken);
    }

    /// <summary>
    ///     Rewrites a conversation, appending a user message carrying the images found in each
    ///     tool result that has any, and leaving a placeholder in the tool result in their place.
    /// </summary>
    /// <remarks>
    ///     Every original message is kept, in its original order, and a promoted message is
    ///     inserted immediately after the tool result it came from. Order is the contract: the
    ///     model reads the exchange in sequence, so an image promoted to the end of the
    ///     conversation would be read as belonging to whatever turn happened to be last. A tool
    ///     message that carries an image is forwarded as a copy, so the caller's messages are
    ///     never modified.
    /// </remarks>
    /// <param name="messages">The conversation to rewrite.</param>
    /// <returns>The rewritten conversation.</returns>
    private static List<ChatMessage> Promote(IEnumerable<ChatMessage> messages)
    {
        // A missing conversation is a programming error in the host rather than something a
        // model produced, so it is surfaced rather than treated as an empty exchange.
        ArgumentNullException.ThrowIfNull(messages);

        var rewritten = new List<ChatMessage>();

        foreach (var message in messages)
        {
            // Only a tool message can carry a function result, and only a function result can
            // carry content the provider is about to discard or flatten into text.
            if (message.Role != ChatRole.Tool)
            {
                rewritten.Add(message);
                continue;
            }

            // Rebuild the message's contents with each promoted image replaced by a placeholder,
            // collecting the images on the way so they can be sent once, on the user message.
            var images = new List<DataContent>();
            var contents = new List<AIContent>(message.Contents.Count);
            foreach (var content in message.Contents)
            {
                contents.Add(content is FunctionResultContent result ? ReplaceImages(result, images) : content);
            }

            // A text-only result needs nothing; adding an empty announcement would spend
            // context on a message saying that no image was attached, and forwarding a copy
            // would change nothing the provider sees.
            if (images.Count == 0)
            {
                rewritten.Add(message);
                continue;
            }

            // Forward a copy of the tool message with the rewritten results. A clone shares its
            // contents list with the original, so a new list is assigned rather than edited. The
            // clone's raw representation is the provider-native form of the original message and
            // may still hold the image bytes, so it is cleared rather than forwarded.
            var forwarded = message.Clone();
            forwarded.Contents = contents;
            forwarded.RawRepresentation = null;
            rewritten.Add(forwarded);

            // Announce the attachment first, then carry the bytes on the one channel every
            // provider honors. The order matches the caption-then-content shape the result
            // constructors already produce, so the model reads the two the same way.
            var promoted = new List<AIContent> { new TextContent(PromotionNotice) };
            promoted.AddRange(images);

            rewritten.Add(new ChatMessage(ChatRole.User, promoted));
        }

        return rewritten;
    }

    /// <summary>
    ///     Returns a function result with every image it carries replaced by a placeholder,
    ///     reading either of the two shapes a guarded tool produces.
    /// </summary>
    /// <remarks>
    ///     A tool returns an image either on its own or as a caption followed by the image, so
    ///     both shapes are read. Non-image content is left where it is and in its order: a
    ///     document a provider accepts in a tool result should not be duplicated onto a user
    ///     message, and one it does not accept is not made readable by moving it. The original
    ///     result is never modified; a result carrying no image is returned as the same instance.
    /// </remarks>
    /// <param name="result">The function result to inspect.</param>
    /// <param name="images">Receives the images found, in the order they appear.</param>
    /// <returns>The result to forward in place of <paramref name="result"/>.</returns>
    private static FunctionResultContent ReplaceImages(FunctionResultContent result, List<DataContent> images)
    {
        switch (result.Result)
        {
            // A lone image becomes the placeholder text alone, which is what the model reads as
            // the tool's answer.
            case DataContent single when single.HasTopLevelMediaType(ImageMediaType):
                images.Add(single);
                return CopyWithResult(result, Placeholder(single));

            // A content list keeps every part in order, with each image part swapped for its
            // placeholder so a caption still reads naturally before the reference to the image.
            case IEnumerable<AIContent> parts:
                var found = images.Count;
                var replaced = new List<AIContent>();
                foreach (var part in parts)
                {
                    if (part is DataContent data && data.HasTopLevelMediaType(ImageMediaType))
                    {
                        images.Add(data);
                        replaced.Add(new TextContent(Placeholder(data)));
                    }
                    else
                    {
                        replaced.Add(part);
                    }
                }

                return images.Count == found ? result : CopyWithResult(result, replaced);

            // Any other shape carries no image this decorator can recognize.
            default:
                return result;
        }
    }

    /// <summary>
    ///     Formats the placeholder that stands in a tool result for a promoted image.
    /// </summary>
    /// <param name="image">The promoted image.</param>
    /// <returns>The placeholder text, naming the image's media type and size.</returns>
    private static string Placeholder(DataContent image)
    {
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            PlaceholderFormat,
            image.MediaType,
            image.Data.Length);
    }

    /// <summary>
    ///     Copies a function result with a replacement result value, preserving everything else
    ///     the original carried except its raw representation.
    /// </summary>
    /// <remarks>
    ///     The call identifier ties the result to its call, and the exception, annotations and
    ///     additional properties may be read by a provider adapter or by telemetry, so all of them
    ///     travel with the copy. The raw representation deliberately does not: it is the
    ///     provider-native form of the original result and may still hold the image bytes, so an
    ///     adapter that prefers a raw object over the result value would resend them.
    /// </remarks>
    /// <param name="original">The function result being copied.</param>
    /// <param name="value">The result value the copy carries.</param>
    /// <returns>The copy.</returns>
    private static FunctionResultContent CopyWithResult(FunctionResultContent original, object value)
    {
        return new FunctionResultContent(original.CallId, value)
        {
            Exception = original.Exception,
            AdditionalProperties = original.AdditionalProperties,
            Annotations = original.Annotations
        };
    }
}
