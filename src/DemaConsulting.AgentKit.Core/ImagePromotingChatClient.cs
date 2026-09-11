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
    ///     tool result that has any.
    /// </summary>
    /// <remarks>
    ///     Every original message is kept, in its original order, and a promoted message is
    ///     inserted immediately after the tool result it came from. Order is the contract: the
    ///     model reads the exchange in sequence, so an image promoted to the end of the
    ///     conversation would be read as belonging to whatever turn happened to be last.
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
            rewritten.Add(message);

            // Only a tool message can carry a function result, and only a function result can
            // carry content the provider is about to discard.
            if (message.Role != ChatRole.Tool)
            {
                continue;
            }

            var images = message.Contents
                .OfType<FunctionResultContent>()
                .SelectMany(ExtractImages)
                .ToList();

            // A text-only result needs nothing; adding an empty announcement would spend
            // context on a message saying that no image was attached.
            if (images.Count == 0)
            {
                continue;
            }

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
    ///     Extracts the image content a function result carries, in either of the two shapes a
    ///     guarded tool produces.
    /// </summary>
    /// <remarks>
    ///     A tool returns an image either on its own or as a caption followed by the image, so
    ///     both shapes are read. Non-image content is left where it is: a document a provider
    ///     accepts in a tool result should not be duplicated onto a user message, and one it
    ///     does not accept is not made readable by moving it.
    /// </remarks>
    /// <param name="result">The function result to inspect.</param>
    /// <returns>The image content the result carries, which may be empty.</returns>
    private static IEnumerable<DataContent> ExtractImages(FunctionResultContent result)
    {
        return result.Result switch
        {
            DataContent single when single.HasTopLevelMediaType(ImageMediaType) => [single],
            IEnumerable<AIContent> parts => parts
                .OfType<DataContent>()
                .Where(part => part.HasTopLevelMediaType(ImageMediaType)),
            _ => []
        };
    }
}
