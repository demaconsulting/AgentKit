using DemaConsulting.AgentKit.Core;
using GitHub.Copilot;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     One reading of how full a Copilot session is, as the runtime reported it.
/// </summary>
/// <remarks>
///     A snapshot rather than a reference to the SDK's own event data, because that data is mutable
///     and belongs to the runtime. The figures are kept in the runtime's own currency —
///     <see cref="long"/> — and narrowed only where they are handed to Core, so the narrowing
///     happens in one visible place.
/// </remarks>
/// <param name="CurrentTokens">The tokens the runtime says the session currently occupies.</param>
/// <param name="TokenLimit">The window the runtime says it will hold.</param>
/// <param name="ConversationTokens">
///     The part of <paramref name="CurrentTokens"/> the runtime attributes to the conversation, or
///     <see langword="null"/> when it reported no split.
/// </param>
internal sealed record CopilotUsageReading(long CurrentTokens, long TokenLimit, long? ConversationTokens);

/// <summary>
///     Watches one Copilot session's event stream, holding what the adapter above it cannot
///     otherwise see: how full the session is, what the current turn did, and whether the runtime
///     rewrote history behind AgentKit's back.
/// </summary>
/// <remarks>
///     <para>
///     <b>A Copilot turn reveals itself through events, not through a return value.</b>
///     <c>SendAndWaitAsync</c> hands back the final assistant message and nothing else; the tool
///     calls, the tool results, and — decisively — the session's occupancy and window arrive as
///     <c>session.usage_info</c>, <c>tool.execution_start</c> and <c>tool.execution_complete</c>
///     events. This class is where they are caught. It is handed to
///     <c>SessionConfig.OnEvent</c> at configuration time, which the SDK registers on the session
///     <em>before</em> the create RPC is issued, so the very first turn is covered.
///     </para>
///     <para>
///     <b>It is the Copilot analogue of the ChatClient adapter's prompt-size recorder, with one
///     deliberate difference.</b> That recorder is cleared before every turn, because an
///     <c>IChatClient</c> reports a figure for one request. Copilot's <c>usage_info</c> is
///     cumulative session state, so the latest reading is simply the truth and is
///     <em>not</em> cleared per turn — a turn that reported none would otherwise be indistinguishable
///     from a session that has never reported one. What <see cref="BeginTurn"/> clears is the entry
///     buffer, which really is per-turn.
///     </para>
///     <para>
///     <b>Thread safety.</b> The SDK invokes handlers serially, in arrival order, on a background
///     thread, and never concurrently with each other on one session. So no handler can run while
///     another is — but the reads happen on the caller's thread while that background thread may
///     be writing, so every field is guarded by one lock.
///     </para>
/// </remarks>
internal sealed class CopilotSessionObserver
{
    /// <summary>
    ///     Guards every field: events arrive on a background thread, reads happen on the caller's.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     What the current turn produced, in arrival order, drained when the turn is recorded.
    /// </summary>
    private readonly List<TranscriptEntry> _entries = [];

    /// <summary>
    ///     The most recent usage reading, or <see langword="null"/> when the runtime has reported
    ///     none. Deliberately never cleared: it is cumulative session state, not a per-turn figure.
    /// </summary>
    private CopilotUsageReading? _latestUsage;

    /// <summary>
    ///     Whether the runtime has compacted or truncated this session's history.
    /// </summary>
    private bool _providerRewroteHistory;

    /// <summary>
    ///     The message of the last session error seen, for a refusal that would otherwise name no
    ///     cause.
    /// </summary>
    private string? _lastErrorMessage;

    /// <summary>
    ///     Gets the most recent usage reading, or <see langword="null"/> when the runtime has
    ///     reported none.
    /// </summary>
    internal CopilotUsageReading? LatestUsage
    {
        get
        {
            lock (_gate)
            {
                return _latestUsage;
            }
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the runtime has rewritten this session's history.
    /// </summary>
    /// <remarks>
    ///     True once the runtime has compacted or truncated the conversation. AgentKit holds the
    ///     runtime's compaction threshold clear of the engine's rotation point on every session its
    ///     engine drives, so this being true means the runtime rewrote history anyway — and that the
    ///     transcript the engine believes it owns no longer matches what the provider holds. Never
    ///     returns to false: a rewrite cannot be undone, and a session that has diverged once stays
    ///     diverged.
    /// </remarks>
    internal bool ProviderRewroteHistory
    {
        get
        {
            lock (_gate)
            {
                return _providerRewroteHistory;
            }
        }
    }

    /// <summary>
    ///     Gets the message of the last session error seen, or <see langword="null"/> when none has
    ///     been reported.
    /// </summary>
    internal string? LastErrorMessage
    {
        get
        {
            lock (_gate)
            {
                return _lastErrorMessage;
            }
        }
    }

    /// <summary>
    ///     Records one event from the session's stream.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The handler given to <c>SessionConfig.OnEvent</c>. Every event kind this adapter has a
    ///     use for is matched; everything else is ignored rather than recorded, because a transcript
    ///     is a record of the conversation and the runtime emits a great deal that is not part of
    ///     one.
    ///     </para>
    ///     <para>
    ///     <b>Events carrying a parent tool call are skipped.</b> A nested agent the runtime ran on
    ///     this session's behalf has its own thread of tool traffic, tagged with the call that
    ///     started it. Those messages were never part of this conversation and the model was never
    ///     shown them as such, so recording them would seed a future session with a history the
    ///     model does not recognize. The call that started the nested agent <em>is</em> recorded,
    ///     because that one happened in this conversation.
    ///     </para>
    ///     <para>
    ///     Never throws. It runs on the runtime's dispatch thread, where an exception would be
    ///     attributed to the SDK rather than to this library, so the matching is total by
    ///     construction: every branch either records something well-formed or does nothing.
    ///     </para>
    /// </remarks>
    /// <param name="sessionEvent">The event the runtime dispatched.</param>
    internal void OnEvent(SessionEvent sessionEvent)
    {
        switch (sessionEvent)
        {
            case SessionUsageInfoEvent { Data: { } usage }:
                Record(() => _latestUsage = new CopilotUsageReading(
                    usage.CurrentTokens,
                    usage.TokenLimit,
                    usage.ConversationTokens));
                break;

            case ToolExecutionStartEvent { Data: { ParentToolCallId: null, ToolCallId: { } callId } start }
                when !string.IsNullOrWhiteSpace(callId):
                Record(() => _entries.Add(TranscriptEntry.ToolCall(callId, DescribeCall(start))));
                break;

            case ToolExecutionCompleteEvent
            { Data: { ParentToolCallId: null, ToolCallId: { } resultId } complete }
                when !string.IsNullOrWhiteSpace(resultId):
                Record(() => _entries.Add(TranscriptEntry.ToolResult(resultId, DescribeResult(complete))));
                break;

            case AssistantMessageEvent { Data: { ParentToolCallId: null, Content: { Length: > 0 } content } }:
                Record(() => _entries.Add(TranscriptEntry.Assistant(content)));
                break;

            // Any of the three means the runtime reshaped the conversation itself, on a session its
            // own engine compacts and whose compaction threshold AgentKit raised to keep it out.
            case SessionCompactionStartEvent:
            case SessionCompactionCompleteEvent:
            case SessionTruncationEvent:
                Record(() => _providerRewroteHistory = true);
                break;

            case SessionErrorEvent { Data: { } error }:
                Record(() => _lastErrorMessage = error.Message);
                break;

            default:
                break;
        }
    }

    /// <summary>
    ///     Starts a turn, discarding anything the previous one left behind.
    /// </summary>
    /// <remarks>
    ///     Called before the prompt is sent, so <see cref="DrainEntries"/> afterwards returns this
    ///     turn's work and no other's. A turn that failed mid-flight leaves entries here; clearing
    ///     at the start rather than the end is what stops them being attributed to the next turn.
    ///     The last reported error is cleared for the same reason and is the more important of the
    ///     two: it is read only to name the cause when a turn goes idle without an answer, so an
    ///     error carried over from a previous turn would name the wrong cause in the one message
    ///     whose entire job is to name the right one.
    /// </remarks>
    internal void BeginTurn()
    {
        lock (_gate)
        {
            _entries.Clear();
            _lastErrorMessage = null;
        }
    }

    /// <summary>
    ///     Takes what this turn produced, in arrival order, and empties the buffer.
    /// </summary>
    /// <remarks>
    ///     Draining rather than reading is what guarantees a turn's work is recorded exactly once
    ///     even if a caller asks twice.
    /// </remarks>
    /// <returns>The entries the current turn produced, oldest first.</returns>
    internal IReadOnlyList<TranscriptEntry> DrainEntries()
    {
        lock (_gate)
        {
            var drained = _entries.ToArray();
            _entries.Clear();
            return drained;
        }
    }

    /// <summary>
    ///     Renders a tool call for the transcript, by name and arguments.
    /// </summary>
    /// <remarks>
    ///     The transcript is read by a summarizer rather than executed, so the arguments are
    ///     recorded for what they say about the call rather than to be parsed back. The name falls
    ///     back to a fixed placeholder because a call with no name is still a call that happened and
    ///     dropping it would leave its result paired with nothing.
    /// </remarks>
    /// <param name="start">The tool-execution start the runtime reported.</param>
    /// <returns>The call rendered as one line.</returns>
    private static string DescribeCall(ToolExecutionStartData start) =>
        $"{start.ToolName ?? "(unnamed tool)"}({start.Arguments?.ToString() ?? string.Empty})";

    /// <summary>
    ///     Renders a tool result for the transcript, including a failure as the result it was.
    /// </summary>
    /// <remarks>
    ///     A tool that failed is recorded with the runtime's error message rather than omitted: the
    ///     model saw the failure and reasoned from it, so a history that showed the call succeeding —
    ///     or showed no result at all — would seed a future session with a conversation that did not
    ///     happen. An error with no message at all records as empty, which the transcript permits,
    ///     because a tool that returned nothing still occupied a turn.
    /// </remarks>
    /// <param name="complete">The tool-execution completion the runtime reported.</param>
    /// <returns>The result rendered as one line.</returns>
    private static string DescribeResult(ToolExecutionCompleteData complete) =>
        complete.Result?.Content ?? complete.Error?.Message ?? string.Empty;

    /// <summary>
    ///     Applies a mutation under the lock.
    /// </summary>
    /// <remarks>
    ///     A named helper rather than a lock statement in each branch, so the switch above reads as
    ///     the mapping it is and no branch can be added that forgets to take the lock.
    /// </remarks>
    /// <param name="mutation">The state change to apply.</param>
    private void Record(Action mutation)
    {
        lock (_gate)
        {
            mutation();
        }
    }
}
