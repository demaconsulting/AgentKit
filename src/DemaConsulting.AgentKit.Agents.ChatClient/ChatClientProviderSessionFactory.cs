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
///     the client reference and the window.
///     </para>
/// </remarks>
public sealed class ChatClientProviderSessionFactory : IProviderSessionFactory
{
    /// <summary>
    ///     The client every created session carries its turns on.
    /// </summary>
    private readonly IChatClient _client;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChatClientProviderSessionFactory"/> class.
    /// </summary>
    /// <param name="client">
    ///     The chat client every created session carries its turns on. Must not be
    ///     <see langword="null"/>.
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
    public Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IProviderSession>(
            new ChatClientProviderSession(_client, seed, WindowTokens));
    }
}
