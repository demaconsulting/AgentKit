using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant;

/// <summary>
///     Reports the tool calls a compacting conversation makes, which a session turn does not hand
///     back.
/// </summary>
/// <remarks>
///     <para>
///     <b>A session turn reports no tool activity.</b> It returns an answer, the resulting usage,
///     and whether the session rotated. The session records every call and result internally, into
///     the transcript it later consolidates, but hands none of them back. The sample's whole
///     demonstration is watching the agent plan, file, recall and delegate, so the calls are read
///     here from the conversation instead.
///     </para>
///     <para>
///     <b>They are read on the way down, because that is where a call and its result sit together.</b>
///     This client is handed to <c>ChatClientProviderSessionFactory</c>, which installs the
///     tool-calling loop above it — so every request passing through here carries the conversation
///     as it then stood, including the calls the model made and the results the loop produced for
///     them. Each is reported once, by call identifier, because a stateless provider is sent the
///     whole conversation again on every turn.
///     </para>
///     <para>
///     <b>Nothing else is done here, and that is new.</b> The occupancy figure a compacting session
///     reads is AgentKit's own concern: the session factory records the size of each real prompt
///     beneath the tool-calling loop it installs. The sample previously had to write a recorder and
///     a repairer of its own to undo the loop's summed usage, and no longer does.
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
public sealed class ToolCallReportingChatClient : DelegatingChatClient
{
    /// <summary>
    ///     The call identifiers already reported, so the conversation resent on every turn does not
    ///     print the same tool call again.
    /// </summary>
    private readonly HashSet<string> _reportedCalls = new(StringComparer.Ordinal);

    /// <summary>
    ///     The call identifiers whose results have already been reported.
    /// </summary>
    private readonly HashSet<string> _reportedResults = new(StringComparer.Ordinal);

    /// <summary>
    ///     Called once per tool call the conversation made, in the order it made them.
    /// </summary>
    private readonly Action<FunctionCallContent> _onCall;

    /// <summary>
    ///     Called once per tool result, with the call it answers when that call was seen.
    /// </summary>
    private readonly Action<FunctionResultContent, FunctionCallContent?> _onResult;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ToolCallReportingChatClient"/> class.
    /// </summary>
    /// <param name="innerClient">
    ///     The client this one talks to the provider through. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="onCall">Receives each tool call the conversation made. Must not be <see langword="null"/>.</param>
    /// <param name="onResult">Receives each tool result. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ToolCallReportingChatClient(
        IChatClient innerClient,
        Action<FunctionCallContent> onCall,
        Action<FunctionResultContent, FunctionCallContent?> onResult)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(onCall);
        ArgumentNullException.ThrowIfNull(onResult);

        _onCall = onCall;
        _onResult = onResult;
    }

    /// <inheritdoc/>
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // Materialized before it is read, because the conversation is reported from it here and
        // then sent from it below, and a sequence enumerated twice need not produce the same thing.
        var conversation = messages as IList<ChatMessage> ?? [.. messages];

        Report(conversation);

        return base.GetResponseAsync(conversation, options, cancellationToken);
    }

    /// <summary>
    ///     Hands every tool call and result not yet reported to the observers, in order.
    /// </summary>
    /// <remarks>
    ///     A call is remembered by its identifier so the result that follows it can be handed back
    ///     with it. Without that, a result printed on its own would say nothing about which call
    ///     produced it, which is the difference between a reader following the run and a reader
    ///     watching interchangeable lines go past.
    /// </remarks>
    /// <param name="conversation">The conversation about to be sent.</param>
    private void Report(IEnumerable<ChatMessage> conversation)
    {
        var callsSeen = new Dictionary<string, FunctionCallContent>(StringComparer.Ordinal);

        foreach (var content in conversation.SelectMany(message => message.Contents))
        {
            switch (content)
            {
                case FunctionCallContent call:
                    callsSeen[call.CallId] = call;
                    if (_reportedCalls.Add(call.CallId))
                    {
                        _onCall(call);
                    }

                    break;

                case FunctionResultContent result:
                    if (_reportedResults.Add(result.CallId))
                    {
                        _onResult(result, callsSeen.GetValueOrDefault(result.CallId));
                    }

                    break;

                default:
                    break;
            }
        }
    }
}
