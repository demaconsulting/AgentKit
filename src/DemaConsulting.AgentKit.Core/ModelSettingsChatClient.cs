using System.Globalization;
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
///     <b>The window is confirmed, not applied.</b> An <see cref="IChatClient"/> has no
///     provider-neutral option for a context window, so this decorator cannot state one. A selection
///     carrying <see cref="ModelSettings.ContextWindowTokens"/> is accepted only when a client
///     beneath it reports, through <see cref="IContextWindowChatClient"/>, that it asks the provider
///     for that same window — on Ollama, an <c>OllamaContextSizingChatClient</c> at that size. A
///     window nothing beneath applies, or applies at another size, is refused at construction.
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
    /// <param name="innerClient">
    ///     The client requests are forwarded to. Must not be <see langword="null"/>. When the
    ///     selection carries a context window, a client at or beneath this one must apply it.
    /// </param>
    /// <param name="selection">The validated selection to apply. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="innerClient"/> or <paramref name="selection"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     The selection names a context tier, which an <see cref="IChatClient"/> cannot apply, or a
    ///     reasoning effort with no provider-neutral equivalent; or it carries a context window that no
    ///     <see cref="IContextWindowChatClient"/> beneath applies at the same size.
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

        if (selection.Settings.ContextWindowTokens is { } window
            && WindowProblem(innerClient, window) is { } problem)
        {
            throw new ArgumentException(problem, nameof(selection));
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

        if (_selection.StandardReasoningEffort is { } effort)
        {
            if (applied.Reasoning is null)
            {
                applied.Reasoning = new ReasoningOptions { Effort = effort };
            }
            else if (applied.Reasoning.Effort is null)
            {
                // The caller set other reasoning options but no effort: fill the effort on a copy, so
                // the caller's instance is never changed and its other fields are kept. The type's own
                // Clone is internal, so its public fields are copied here.
                applied.Reasoning = new ReasoningOptions { Effort = effort, Output = applied.Reasoning.Output };
            }
        }

        return applied;
    }

    /// <summary>
    ///     Explains why a selection's window is not in effect beneath this decorator, if it is not.
    /// </summary>
    /// <remarks>
    ///     A delegating client answers <see cref="IChatClient.GetService"/> for itself and otherwise
    ///     asks the client it wraps, so the window-applying client is found however deep it sits.
    /// </remarks>
    /// <param name="innerClient">The client beneath this decorator.</param>
    /// <param name="window">The selection's context window, in tokens.</param>
    /// <returns>
    ///     The reason to refuse the selection, or <see langword="null"/> when a client beneath applies
    ///     the same window.
    /// </returns>
    private static string? WindowProblem(IChatClient innerClient, int window)
    {
        var applying = innerClient.GetService<IContextWindowChatClient>();
        if (applying is null)
        {
            return $"The selection's ContextWindowTokens ({Tokens(window)}) is not applied by any client beneath: "
                + "an IChatClient carries no context window of its own. Compose a client that applies it at "
                + "the same size, such as OllamaContextSizingChatClient, or select without a window.";
        }

        return applying.ContextWindowTokens == window
            ? null
            : $"The selection's ContextWindowTokens ({Tokens(window)}) differs from the "
                + $"{Tokens(applying.ContextWindowTokens)} tokens the client beneath applies; "
                + "state the same window in both places.";
    }

    /// <summary>
    ///     Formats a token count for a message.
    /// </summary>
    /// <param name="tokens">The count.</param>
    /// <returns>The count with group separators, culture-invariant.</returns>
    private static string Tokens(int tokens) => tokens.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>
///     A chat client that asks its provider to run at a stated context window on every request.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why it exists.</b> An <see cref="IChatClient"/> carries no provider-neutral context window,
///     so a window a selection chose is applied by a provider-specific client instead — on Ollama,
///     <c>OllamaContextSizingChatClient</c>. Implementing this interface is how such a client reports
///     the window it applies, so <see cref="ModelSettingsChatClient"/> can confirm a selection's
///     window is in effect rather than dropping it.
///     </para>
///     <para>
///     <b>How it is found.</b> Through <see cref="ChatClientExtensions.GetService{TService}"/>: a
///     <see cref="DelegatingChatClient"/> returns itself for an interface it implements and otherwise
///     asks the client it wraps, so the implementer can sit anywhere beneath the decorator that asks.
///     </para>
///     <para>
///     A provider whose window is set some other way implements this on its own client or decorator,
///     reporting the window it asks for.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     public bool AppliesWindow(IChatClient client, int window) =>
///         client.GetService&lt;IContextWindowChatClient&gt;()?.ContextWindowTokens == window;
///     </code>
/// </example>
public interface IContextWindowChatClient : IChatClient
{
    /// <summary>
    ///     Gets the context window, in tokens, this client asks its provider for on every request.
    /// </summary>
    int ContextWindowTokens { get; }
}
