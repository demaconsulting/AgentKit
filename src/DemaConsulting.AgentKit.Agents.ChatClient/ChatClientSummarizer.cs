// Copyright (c) DEMA Consulting
// SPDX-License-Identifier: MIT

using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient;

/// <summary>
///     A summarizer that consolidates history through an <see cref="IChatClient"/>.
/// </summary>
/// <remarks>
///     <para>
///     <b>Shipped because the mechanism is this library's to guarantee.</b> Compaction cannot happen
///     without a summarizer, so requiring every application to write one left the promise with a
///     hole in it: the part of compaction that decides what survives was the part nobody was given.
///     This is that part, using the consolidation prompt this package publishes and the levels the
///     session drives it with. An application remains free to write its own - the contract is one
///     method - but it should not have to.
///     </para>
///     <para>
///     <b>It runs outside the session it is compacting.</b> The client here is the application's to
///     choose and is deliberately separate from the one carrying the conversation: a consolidation
///     sent through the live session would consume the very context it exists to reclaim, which was
///     measured on a real backend and is the reason this contract takes a summarizer at all rather
///     than reducing in place. A smaller and cheaper model is usually the right choice, because
///     consolidation is summarization rather than reasoning.
///     </para>
///     <para>
///     Safe for concurrent use if the underlying client is.
///     </para>
/// </remarks>
public sealed class ChatClientSummarizer : ISummarizer
{
    /// <summary>
    ///     The client each consolidation is sent through.
    /// </summary>
    private readonly IChatClient _client;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ChatClientSummarizer"/> class.
    /// </summary>
    /// <param name="client">
    ///     The chat client each consolidation is sent through. Must not be <see langword="null"/>.
    ///     Should not be the client carrying the conversation being compacted.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    public ChatClientSummarizer(IChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Sends the composed prompt as a single user message and returns what comes back. No tools
    ///     are offered and no history is carried: a consolidation is a pure function from the
    ///     material to a record of it, which is what lets a rotation be replayed and reasoned about.
    /// </remarks>
    public async Task<string> ConsolidateAsync(
        ConsolidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var response = await _client
            .GetResponseAsync(
                [new ChatMessage(ChatRole.User, ConsolidationPrompt.Compose(request))],
                options: null,
                cancellationToken)
            .ConfigureAwait(false);

        // An empty answer is returned as one rather than turned into an exception: the engine treats
        // a consolidation it could not obtain as material to keep rather than material to lose, and
        // a model declining to answer is a thing that happens rather than a defect to escalate.
        return response.Text ?? string.Empty;
    }
}
