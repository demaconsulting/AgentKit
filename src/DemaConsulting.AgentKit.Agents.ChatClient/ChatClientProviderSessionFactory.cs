// Copyright (c) DEMA Consulting
// SPDX-License-Identifier: MIT

using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient;

/// <summary>
///     Creates <see cref="ChatClientProviderSession"/> instances, once at the start of a
///     conversation and again at every rotation.
/// </summary>
/// <remarks>
///     <para>
///     Holds the client and the window, so the application states both once - where it configures
///     its provider - and a rotating session asks for neither again. The client is shared by every
///     session this factory creates and is never disposed by them: it outlives the conversation.
///     </para>
///     <para>
///     <b>Where the window comes from.</b> Read it from the provider wherever the provider will say.
///     Ollama publishes the loaded model's context length, and an application that sets the context
///     size itself already knows the number it chose. For a hosted model the window is a published
///     property of the model the application selected. Passing it here is what lets the session
///     engine ask one question - how full, out of how much - and believe the answer.
///     </para>
///     <para>
///     Safe for concurrent use: creating a session touches nothing this factory owns beyond reading
///     the client reference and the window, and each session gets a pipeline of its own.
///     </para>
/// </remarks>
public sealed class ChatClientProviderSessionFactory : IProviderSessionFactory
{
    /// <summary>
    ///     The client the application talks to its provider with — wrapped in the selection's
    ///     settings decorator when one was supplied — which every pipeline this factory builds is
    ///     wrapped around and which none of them disposes.
    /// </summary>
    /// <remarks>
    ///     The settings decorator is never disposed either: disposing it would dispose the
    ///     application's client, which outlives the conversation, and it holds nothing of its own.
    /// </remarks>
    private readonly IChatClient _client;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChatClientProviderSessionFactory"/> class.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Hand it the client that talks to the provider, not a pipeline.</b> This factory builds
    ///     the pipeline itself, one per session it creates: a recorder directly around the supplied
    ///     client, an <see cref="ImagePromotingChatClient"/> above that, and the function-invoking
    ///     layer above that again. All three placements matter.
    ///     </para>
    ///     <para>
    ///     The recorder must sit underneath, because a turn that calls tools makes several requests
    ///     and the response finally returned carries usage summed across all of them - which read as
    ///     occupancy would have a tool-using agent believe its window was full on its first turn.
    ///     Underneath, each request is seen separately and the last one is the conversation.
    ///     </para>
    ///     <para>
    ///     The image promoter must sit between the two: beneath the function-invocation loop, so
    ///     there is a tool result for it to promote, and above the recorder, so the occupancy read
    ///     includes any message it promoted.
    ///     </para>
    ///     <para>
    ///     The function-invoking layer must sit above, because a session seeds its tools into every
    ///     request and a bare client will happily emit tool calls that nothing answers. Owning all
    ///     three placements here is what stops a correct-looking composition being silently wrong.
    ///     </para>
    /// </remarks>
    /// <param name="client">
    ///     The client that talks to the provider. Must not be <see langword="null"/>. Decorators of
    ///     your own are fine; do not add function invocation, which this factory installs.
    /// </param>
    /// <param name="windowTokens">
    ///     The provider's context window in tokens. Must be positive.
    /// </param>
    /// <param name="selection">
    ///     The validated model selection to apply to every request each session sends, or
    ///     <see langword="null"/> to send requests as the client is configured. When supplied, a
    ///     <see cref="ModelSettingsChatClient"/> is placed directly over the supplied client, beneath
    ///     the recorder. The window is still stated here rather than taken from the selection, because
    ///     the figure a session accounts against is the provider's to report and the application's
    ///     to state.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="windowTokens"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="selection"/> carries a context tier or an effort with no provider-neutral
    ///     equivalent, which an <see cref="IChatClient"/> cannot apply.
    /// </exception>
    public ChatClientProviderSessionFactory(IChatClient client, int windowTokens, ModelSelection? selection = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowTokens);

        // Wrapped once here rather than per session: the decorator holds no per-conversation state,
        // and building it now refuses a selection the client cannot apply where the host composed it.
        _client = selection is null ? client : new ModelSettingsChatClient(client, selection);
        WindowTokens = windowTokens;
    }

    /// <summary>
    ///     Gets the provider's context window in tokens, as supplied at construction.
    /// </summary>
    public int WindowTokens { get; }

    /// <inheritdoc/>
    /// <remarks>
    ///     <para>
    ///     The pipeline is built here rather than once at construction, because the recorder holds
    ///     the occupancy of one conversation. Shared between sessions it would hand a replacement
    ///     the figure its predecessor left behind - so a session that had sent nothing would report
    ///     a full window and rotate again immediately - and two sessions run at once would overwrite
    ///     each other's reading, which the concurrency this contract promises does not allow.
    ///     </para>
    ///     <para>
    ///     <see cref="ImagePromotingChatClient"/> is installed unconditionally, for the same reason
    ///     <see cref="ChatClientAgentFactory"/> installs it: a provider whose tool-result channel
    ///     cannot carry an image drops one silently, and the model answers anyway. A session is as
    ///     exposed to that as an agent is. It sits beneath the function-invocation loop so it
    ///     observes the conversation after tool results have been appended, and above the recorder
    ///     so the occupancy read includes any message it promoted.
    ///     </para>
    /// </remarks>
    public Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        var recorder = new PromptSizeRecordingChatClient(_client);
        var pipeline = new FunctionInvokingChatClient(new ImagePromotingChatClient(recorder));

        return Task.FromResult<IProviderSession>(
            new ChatClientProviderSession(pipeline, recorder, seed, WindowTokens));
    }
}
