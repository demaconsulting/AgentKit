using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant;

/// <summary>
///     Reports a compacting turn's tool activity, and restores the occupancy figure the session
///     beneath it depends on.
/// </summary>
/// <remarks>
///     <para>
///     Installed <b>above</b> the tool-calling loop, which is the only place a whole turn is
///     visible. It does two things a compacting conversation cannot do for itself.
///     </para>
///     <para>
///     <b>It shows the tool calls.</b> A session turn returns an answer, the resulting usage, and
///     whether the session rotated — and nothing about what the agent did to produce the answer.
///     The session records every tool call and result internally, into the transcript it later
///     consolidates, but hands none of them back. The sample's whole demonstration is watching the
///     agent plan, file, recall and delegate, so the calls are read here from the messages the turn
///     produced instead.
///     </para>
///     <para>
///     <b>It repairs the usage.</b> The tool-calling loop sums input tokens across every request it
///     made, and the session beneath reads that sum as the conversation's size. See
///     <see cref="PromptSizeRecorder"/> for what that costs. The last real prompt size recorded
///     beneath the loop is put back here, so the session rotates when the window is genuinely
///     filling rather than when the agent has merely used a lot of tools.
///     </para>
///     <para>
///     Streaming is forwarded untouched. The compacting conversation never streams — a session turn
///     is one call that returns one answer — so there is nothing to observe on that path, and
///     pretending otherwise would be decoration.
///     </para>
///     <para>
///     Instances are not safe for concurrent use: they serve one conversation.
///     </para>
/// </remarks>
public sealed class TurnReportingChatClient : DelegatingChatClient
{
    /// <summary>
    ///     The recorder beneath the tool-calling loop, holding the last real prompt size.
    /// </summary>
    private readonly PromptSizeRecorder _promptSize;

    /// <summary>
    ///     Called once per tool call the turn made, in the order the turn made them.
    /// </summary>
    private readonly Action<FunctionCallContent> _onCall;

    /// <summary>
    ///     Called once per tool result, with the call it answers when that call was seen.
    /// </summary>
    private readonly Action<FunctionResultContent, FunctionCallContent?> _onResult;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TurnReportingChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">
    ///     The tool-calling loop this client wraps. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="promptSize">
    ///     The recorder installed beneath that loop. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="onCall">Receives each tool call the turn made. Must not be <see langword="null"/>.</param>
    /// <param name="onResult">Receives each tool result. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public TurnReportingChatClient(
        IChatClient innerClient,
        PromptSizeRecorder promptSize,
        Action<FunctionCallContent> onCall,
        Action<FunctionResultContent, FunctionCallContent?> onResult)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(promptSize);
        ArgumentNullException.ThrowIfNull(onCall);
        ArgumentNullException.ThrowIfNull(onResult);

        _promptSize = promptSize;
        _onCall = onCall;
        _onResult = onResult;
    }

    /// <inheritdoc/>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);

        Report(response);
        Repair(response);

        return response;
    }

    /// <summary>
    ///     Hands every tool call and result the turn produced to the observers, in order.
    /// </summary>
    /// <remarks>
    ///     Calls and results arrive as separate messages — an assistant message carrying the calls,
    ///     then a tool message carrying the results — so each call is remembered by its identifier
    ///     and handed back with its result. Without that, a result printed on its own would say
    ///     nothing about which call produced it, which is the difference between a reader following
    ///     the run and a reader watching interchangeable lines go past.
    /// </remarks>
    /// <param name="response">The turn's response.</param>
    private void Report(ChatResponse response)
    {
        var callsInFlight = new Dictionary<string, FunctionCallContent>(StringComparer.Ordinal);

        foreach (var content in response.Messages.SelectMany(message => message.Contents))
        {
            switch (content)
            {
                case FunctionCallContent call:
                    callsInFlight[call.CallId] = call;
                    _onCall(call);
                    break;

                case FunctionResultContent result:
                    _onResult(result, callsInFlight.GetValueOrDefault(result.CallId));
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    ///     Replaces the loop's summed input-token count with the size of the last real prompt.
    /// </summary>
    /// <remarks>
    ///     Only the input count is replaced. The output count is a genuine total — the model really
    ///     did produce that much text across the turn — and it is not what the session reads.
    ///     Nothing is replaced when the recorder holds no figure, so a provider reporting no usage
    ///     still reaches the session as a provider reporting no usage, and is refused there rather
    ///     than silently invented here.
    /// </remarks>
    /// <param name="response">The turn's response, modified in place.</param>
    private void Repair(ChatResponse response)
    {
        if (_promptSize.LastPromptTokens is not { } tokens)
        {
            return;
        }

        response.Usage = new UsageDetails
        {
            InputTokenCount = tokens,
            OutputTokenCount = response.Usage?.OutputTokenCount,
            TotalTokenCount = tokens + (response.Usage?.OutputTokenCount ?? 0),
        };
    }
}
