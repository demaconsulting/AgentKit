// Copyright (c) DEMA Consulting
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient;

/// <summary>
///     Records the prompt size of each individual request a turn makes, so occupancy is read from
///     the last one rather than the sum of them all.
/// </summary>
/// <remarks>
///     <para>
///     <b>A turn is not one request.</b> When the model calls tools, the function-invoking layer
///     answers them and asks again, several times over, and the <see cref="ChatResponse"/> it
///     finally returns carries usage <em>aggregated across every request it made</em>. A turn with
///     six tool calls therefore reports roughly six times the conversation.
///     </para>
///     <para>
///     Read as occupancy that is badly wrong, and wrong in the direction that does harm: a
///     tool-using agent - which is every agent this library exists for - would conclude its window
///     was full on its first turn, rotate on every turn after it, compact harder each time, and
///     eventually discard history to reclaim room it never occupied.
///     </para>
///     <para>
///     What the session actually needs is how much of the window the conversation occupies, and that
///     is the prompt of the <em>last</em> request the turn made: by then the tool calls and their
///     results are part of the conversation being sent. So this sits underneath the function-invoking
///     layer, sees each request separately, and keeps the most recent figure.
///     </para>
/// </remarks>
/// <param name="innerClient">The client each request is passed to.</param>
internal sealed class PromptSizeRecordingChatClient(IChatClient innerClient)
    : DelegatingChatClient(innerClient)
{
    /// <summary>
    ///     Gets the prompt tokens the most recent request reported, or <see langword="null"/> when no
    ///     request has reported any.
    /// </summary>
    public long? LastPromptTokens { get; private set; }

    /// <summary>
    ///     Forgets the recorded figure, so the next reading can only come from a request made after
    ///     this call.
    /// </summary>
    /// <remarks>
    ///     Called at the start of each turn. Without it the recorded figure outlives the turn that
    ///     produced it, and a provider that reports usage once and then stops would hold occupancy
    ///     frozen at that first reading - never reaching the rotation threshold again while the
    ///     conversation grew without limit behind it.
    /// </remarks>
    public void Forget() => LastPromptTokens = null;

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false);

        if (response.Usage?.InputTokenCount is { } tokens)
        {
            LastPromptTokens = tokens;
        }

        return response;
    }

    /// <inheritdoc/>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .ConfigureAwait(false))
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent usage && usage.Details.InputTokenCount is { } tokens)
                {
                    LastPromptTokens = tokens;
                }
            }

            yield return update;
        }
    }
}
