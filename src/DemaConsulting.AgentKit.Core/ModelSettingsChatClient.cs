using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Applies a validated model selection to every request an <see cref="IChatClient"/> pipeline
///     sends: the model, the reasoning effort and the output limit.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why a decorator.</b> An <see cref="IChatClient"/> has no session to configure once; each
///     request carries its own options. A selection applied to the first request and not the rest
///     would let a later request run on the provider's defaults, so the choice rides on every
///     request, streaming and not.
///     </para>
///     <para>
///     <b>Provider-neutral.</b> The effort is applied as <see cref="ChatOptions.Reasoning"/>, which a
///     provider's client maps to its own wire option — OllamaSharp sends it as Ollama's
///     <c>think</c> — so no provider-specific option is written here. An effort with no
///     provider-neutral equivalent cannot travel this way, and a context tier has no meaning on an
///     <see cref="IChatClient"/>, so a selection carrying either is refused at construction rather
///     than silently dropped.
///     </para>
///     <para>
///     <b>The caller's request is preserved.</b> A value the caller set on the request wins; only
///     what it left unset is filled in. The caller's options object is never written to, because one
///     instance is commonly reused for every request of a session.
///     </para>
///     <para>
///     The window is not applied here. A context window is stated where the provider is configured —
///     to the provider-session factory, and on Ollama to the decorator that asks the server for it.
///     </para>
///     <para>
///     Holds no mutable state and is as safe for concurrent use as the client it wraps. Disposing it
///     disposes the wrapped client, as every delegating client does.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     public IChatClient Configure(IChatClient client, ModelSelection selection) =>
///         new ModelSettingsChatClient(client, selection);
///     </code>
/// </example>
public sealed class ModelSettingsChatClient : DelegatingChatClient
{
    /// <summary>
    ///     The selection applied to every request.
    /// </summary>
    private readonly ModelSelection _selection;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ModelSettingsChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">The client requests are forwarded to. Must not be <see langword="null"/>.</param>
    /// <param name="selection">The validated selection to apply. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="innerClient"/> or <paramref name="selection"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     The selection names a context tier, which an <see cref="IChatClient"/> cannot apply, or a
    ///     reasoning effort with no provider-neutral equivalent.
    /// </exception>
    public ModelSettingsChatClient(IChatClient innerClient, ModelSelection selection)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.Settings.ContextTier is { } tier)
        {
            throw new ArgumentException(
                $"Context tier '{tier}' cannot be applied through an IChatClient, which has no context tiers.",
                nameof(selection));
        }

        if (selection.Settings.ReasoningEffort is { } effort && selection.StandardReasoningEffort is null)
        {
            throw new ArgumentException(
                $"Effort '{effort}' has no provider-neutral equivalent and cannot be applied through an IChatClient.",
                nameof(selection));
        }

        _selection = selection;
    }

    /// <summary>
    ///     Forwards a request with the selection applied.
    /// </summary>
    /// <param name="messages">The conversation, forwarded unchanged.</param>
    /// <param name="options">The caller's options, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The wrapped client's response.</returns>
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetResponseAsync(messages, Apply(options), cancellationToken);
    }

    /// <summary>
    ///     Forwards a streaming request with the selection applied.
    /// </summary>
    /// <param name="messages">The conversation, forwarded unchanged.</param>
    /// <param name="options">The caller's options, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The wrapped client's response updates.</returns>
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return base.GetStreamingResponseAsync(messages, Apply(options), cancellationToken);
    }

    /// <summary>
    ///     Returns options carrying the selection, filling only what the caller left unset.
    /// </summary>
    /// <param name="options">The caller's options, or <see langword="null"/>.</param>
    /// <returns>A copy of the caller's options with the selection applied. Never <see langword="null"/>.</returns>
    private ChatOptions Apply(ChatOptions? options)
    {
        var applied = options?.Clone() ?? new ChatOptions();

        applied.ModelId ??= _selection.Model.Id;
        applied.MaxOutputTokens ??= _selection.Settings.MaxOutputTokens;

        if (applied.Reasoning is null && _selection.StandardReasoningEffort is { } effort)
        {
            applied.Reasoning = new ReasoningOptions { Effort = effort };
        }

        return applied;
    }
}
