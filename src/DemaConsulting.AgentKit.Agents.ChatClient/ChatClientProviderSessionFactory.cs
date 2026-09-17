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
    ///     The client the application talks to its provider with, which every pipeline this factory
    ///     builds is wrapped around and which none of them disposes.
    /// </summary>
    private readonly IChatClient _client;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChatClientProviderSessionFactory"/> class.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Hand it the client that talks to the provider, not a pipeline.</b> This factory builds
    ///     the pipeline itself, one per session it creates: a recorder directly around the supplied
    ///     client, and the function-invoking layer above that. Both placements matter.
    ///     </para>
    ///     <para>
    ///     The recorder must sit underneath, because a turn that calls tools makes several requests
    ///     and the response finally returned carries usage summed across all of them - which read as
    ///     occupancy would have a tool-using agent believe its window was full on its first turn.
    ///     Underneath, each request is seen separately and the last one is the conversation.
    ///     </para>
    ///     <para>
    ///     The function-invoking layer must sit above, because a session seeds its tools into every
    ///     request and a bare client will happily emit tool calls that nothing answers. Owning both
    ///     placements here is what stops a correct-looking composition being silently wrong.
    ///     </para>
    /// </remarks>
    /// <param name="client">
    ///     The client that talks to the provider. Must not be <see langword="null"/>. Decorators of
    ///     your own are fine; do not add function invocation, which this factory installs.
    /// </param>
    /// <param name="windowTokens">
    ///     The provider's context window in tokens. Must be positive.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="windowTokens"/> is not positive.</exception>
    public ChatClientProviderSessionFactory(IChatClient client, int windowTokens)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowTokens);

        _client = client;
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
