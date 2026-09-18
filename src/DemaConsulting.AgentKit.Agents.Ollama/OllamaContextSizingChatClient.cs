using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Ollama;

/// <summary>
///     Asks Ollama to run the model at a chosen context length, on every request, so the window a
///     session accounts against is the window the instance is actually using.
/// </summary>
/// <remarks>
///     <para>
///     <b>An Ollama model has two context lengths and only one of them is enforced.</b> The model
///     file publishes a maximum, and the server decides at load time what the running instance will
///     use, which is smaller. A session accounted against the published maximum keeps talking long
///     after the instance has begun discarding the start of the conversation, and nothing in the
///     exchange reports that it happened.
///     </para>
///     <para>
///     <b>The instance's length is decided by <c>num_ctx</c>.</b> A request naming it loads — or
///     reloads — the model at that length, which turns the window from something an application
///     guesses into something it asks for. That is why this decorator exists: it puts the
///     application's chosen length on every request, so the figure handed to a compacting session is
///     true by construction rather than inferred.
///     </para>
///     <para>
///     <b>Every request, not merely the first.</b> Ollama reloads a model whenever a request names a
///     different context length, so one un-annotated request would silently resize the instance
///     beneath a session still accounting against the old figure. A decorator that annotated only
///     the opening request would therefore be worse than none, because the mistake would be
///     invisible.
///     </para>
///     <para>
///     <b>A window that was only read is no more durable than one that was only claimed.</b> Ollama
///     does not remember the length an instance was loaded at, and an idle model is evicted after
///     minutes, so a figure discovered at startup stops describing the instance the moment it
///     reloads at the server's default. An assumed figure is weaker still: it was never a
///     measurement of anything, because Ollama's own default is chosen from available memory or set
///     server-wide with <c>OLLAMA_CONTEXT_LENGTH</c>. Compose this decorator around a discovered or
///     assumed window as readily as a chosen one: asking for the figure already in hand re-reads
///     nothing and forces no load that the first chat request would not force anyway.
///     </para>
///     <para>
///     <b>A summarizer needs the same treatment, whichever model it runs on.</b> A consolidation is
///     a single request carrying the whole conversation being rotated, so a summarizer left at the
///     server's default would silently truncate the transcript it was asked to consolidate. Its
///     need comes from the size of what it must read rather than from sharing an instance with the
///     conversation. Asking for a size is a property of the request and not a promise about the
///     model, so choose a summary model whose trained context and memory can host the figure.
///     </para>
///     <para>
///     <b>Only the clients that carry the conversation.</b> The decorator belongs on the client the
///     conversation runs through and on the summarizer's; a client doing unrelated work on the same
///     server — generating embeddings, say — has its own model and its own window, and
///     <c>num_ctx</c> means nothing to it.
///     </para>
///     <para>
///     <b>The caller's request is preserved.</b> Only the one option is added; every other option
///     reaches the provider exactly as the caller set it, and a caller who named the context length
///     itself is forwarded as it named it rather than overruled. The caller's own options object is
///     never written to — one instance is commonly reused for every request of a session and shared
///     with sibling clients, so mutating it would leak this decorator's choice outward.
///     </para>
///     <para>
///     <b>Disposing this decorator disposes the client it wraps.</b> That is the delegating client's
///     ownership rule, and it matters here because the wrapped client is typically shared — the same
///     Ollama client is the natural one to read the window from, and a summarizer often runs over a
///     sibling of it. A composition that releases this decorator while still holding the inner client
///     for other work has released that work's transport too. Where the inner client outlives the
///     decorator, dispose the inner client directly and let the decorator be collected.
///     </para>
///     <para>
///     The class holds no mutable state of its own and is as safe for concurrent use as the client
///     it wraps.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // One figure, chosen by the application, used twice: asked of the server on every request,
///     // and handed to the session that accounts against it. The Ollama client is shared between
///     // the two, so it is disposed directly rather than through the decorator.
///     var ollama = new OllamaApiClient(new Uri("http://localhost:11434"), "qwen3:8b");
///     IChatClient client = new OllamaContextSizingChatClient(ollama, 8192);
///     var window = await OllamaContextWindow.ReadAsync(
///         ollama,
///         "qwen3:8b",
///         stated: 8192,
///         CancellationToken.None);
///
///     Console.WriteLine($"{window.Tokens} tokens, from {window.Source}");
///     </code>
/// </example>
public sealed class OllamaContextSizingChatClient : DelegatingChatClient
{
    /// <summary>
    ///     The Ollama option naming the context length a model is loaded with.
    /// </summary>
    /// <remarks>
    ///     Carried as an additional property because it is Ollama's own option rather than one the
    ///     provider-neutral chat abstraction models; the Ollama client copies it into the request's
    ///     options block on the wire.
    /// </remarks>
    private const string ContextLengthOption = "num_ctx";

    /// <summary>
    ///     The context length every forwarded request asks for.
    /// </summary>
    private readonly int _contextLength;

    /// <summary>
    ///     Builds a decorator that asks for the given context length on every request it forwards.
    /// </summary>
    /// <remarks>
    ///     The length is validated here rather than at the first request, so a composing mistake is
    ///     reported at the line that made it rather than at a request some time later.
    /// </remarks>
    /// <param name="innerClient">
    ///     The client this decorator forwards to, and disposes when this decorator is disposed. Must
    ///     not be <see langword="null"/>.
    /// </param>
    /// <param name="contextLength">
    ///     The context length to ask the server for, in tokens. Must be greater than zero.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="innerClient"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="contextLength"/> is zero or less, which no instance could run.
    /// </exception>
    public OllamaContextSizingChatClient(IChatClient innerClient, int contextLength)
        : base(innerClient)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contextLength);

        _contextLength = contextLength;
    }

    /// <summary>
    ///     Forwards a request, having asked for the chosen context length.
    /// </summary>
    /// <param name="messages">The conversation, forwarded unchanged.</param>
    /// <param name="options">The request options, or <see langword="null"/> when the caller set none.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The response the wrapped client produced.</returns>
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetResponseAsync(messages, WithContextLength(options), cancellationToken);
    }

    /// <summary>
    ///     Forwards a streaming request, having asked for the chosen context length.
    /// </summary>
    /// <remarks>
    ///     Streaming and non-streaming are annotated identically. A host that streams would
    ///     otherwise resize the instance on its first streamed turn, which is exactly the silent
    ///     failure this decorator exists to prevent.
    /// </remarks>
    /// <param name="messages">The conversation, forwarded unchanged.</param>
    /// <param name="options">The request options, or <see langword="null"/> when the caller set none.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The response updates the wrapped client produced.</returns>
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingResponseAsync(messages, WithContextLength(options), cancellationToken);
    }

    /// <summary>
    ///     Returns options carrying the chosen context length, without disturbing the caller's.
    /// </summary>
    /// <remarks>
    ///     Cloned rather than mutated, because one options instance is commonly reused across every
    ///     request of a session and shared with sibling clients; writing into it would leak this
    ///     decorator's choice back to the caller.
    /// </remarks>
    /// <param name="options">The caller's options, or <see langword="null"/> when it set none.</param>
    /// <returns>The options to forward. Never <see langword="null"/>.</returns>
    private ChatOptions WithContextLength(ChatOptions? options)
    {
        // A caller that named the option has already decided what the instance should run at, and
        // overruling it would leave that caller with no way to say so at all.
        if (options?.AdditionalProperties?.ContainsKey(ContextLengthOption) == true)
        {
            return options;
        }

        var sized = options?.Clone() ?? new ChatOptions();
        sized.AdditionalProperties ??= [];
        sized.AdditionalProperties[ContextLengthOption] = _contextLength;

        return sized;
    }
}
