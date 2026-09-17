using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant;

/// <summary>
///     Records the size of the prompt the provider actually answered, for a session that has to
///     know how full its window is.
/// </summary>
/// <remarks>
///     <para>
///     <b>This exists because of where the tool-calling loop sits.</b> A tool-using turn is not one
///     request: <c>FunctionInvokingChatClient</c> sends the conversation, receives tool calls, runs
///     them, and sends the conversation again — as many times as the model keeps calling tools. The
///     response it hands back reports <em>summed</em> usage across all of those requests, which is
///     the right figure for billing and the wrong figure for occupancy. A turn with six tool calls
///     reports something near six times the conversation's actual size.
///     </para>
///     <para>
///     <b>And occupancy is exactly what a compacting session reads.</b>
///     <c>ChatClientProviderSession.CurrentUsage</c> takes the input-token count of the response it
///     was handed as the conversation's size, and rotates when it approaches the window. Handed the
///     summed figure, it concludes the window is full during the first tool-using turn of a fresh
///     conversation and rotates on every turn thereafter, escalating the compaction level until it
///     is discarding history — to reclaim room that was never occupied.
///     </para>
///     <para>
///     So this client is installed <b>beneath</b> the tool-calling loop, where every request is one
///     real prompt, and remembers the last one. <see cref="TurnReportingChatClient"/> sits above the
///     loop and puts that figure back on the turn's response. Two decorators to correct one number
///     is more than an application should have to write, and it is written here plainly rather than
///     hidden, because the alternative is a session that mis-measures every tool-using agent —
///     which is every agent this library is for.
///     </para>
///     <para>
///     Instances are not safe for concurrent use: the recorded size belongs to one conversation.
///     </para>
/// </remarks>
public sealed class PromptSizeRecorder : DelegatingChatClient
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PromptSizeRecorder"/> class.
    /// </summary>
    /// <param name="innerClient">
    ///     The provider client each request is forwarded to. Must not be <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> is <see langword="null"/>.</exception>
    public PromptSizeRecorder(IChatClient innerClient)
        : base(innerClient)
    {
    }

    /// <summary>
    ///     Gets the input-token count of the most recent request the provider answered, or
    ///     <see langword="null"/> when none has reported one.
    /// </summary>
    /// <remarks>
    ///     The size of one real prompt, because this client sits beneath the tool-calling loop. It
    ///     is the conversation's occupancy as the provider counted it, in the provider's own
    ///     tokenizer.
    /// </remarks>
    public long? LastPromptTokens { get; private set; }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        // A provider that reported nothing leaves the previous reading in place rather than
        // clearing it: the last figure that was true is closer to the truth than no figure at all,
        // and the session refuses a turn that reports none.
        if (response.Usage?.InputTokenCount is { } tokens)
        {
            LastPromptTokens = tokens;
        }

        return response;
    }
}
