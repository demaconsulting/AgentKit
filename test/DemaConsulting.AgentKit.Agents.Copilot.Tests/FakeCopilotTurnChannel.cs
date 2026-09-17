using GitHub.Copilot;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     What one scripted turn does: the events the runtime dispatches while it is running, and how
///     it ends.
/// </summary>
/// <remarks>
///     The events are <b>real SDK event objects</b> rather than a local imitation of them. Every
///     session event type the adapter matches on has a public parameterless constructor and a
///     settable payload, so a test can drive the genuine shapes through the genuine matching code —
///     which is what makes an offline test of this adapter worth running.
/// </remarks>
/// <param name="Events">The events dispatched to the session's handler, in arrival order.</param>
/// <param name="Answer">
///     The final assistant message the turn returns, or <see langword="null"/> to model a session
///     that went idle without answering.
/// </param>
/// <param name="Failure">The exception the turn throws instead of returning, or <see langword="null"/>.</param>
internal sealed record ScriptedTurn(
    IReadOnlyList<SessionEvent> Events,
    AssistantMessageEvent? Answer = null,
    Exception? Failure = null);

/// <summary>
///     A turn channel that contacts nothing: it records the prompts it was sent, replays scripted
///     runtime events into the session's own event handler, and answers from a script.
/// </summary>
/// <remarks>
///     <para>
///     <c>CopilotSession</c> is sealed with a non-public constructor and no virtual members, and
///     <c>CopilotClient</c> is sealed, so neither can be faked, subclassed or constructed in a test.
///     <see cref="ICopilotTurnChannel"/> exists for that reason, and this is what stands behind it
///     when no runtime is available — which, in this repository's CI, is always.
///     </para>
///     <para>
///     The events are dispatched on the calling thread rather than on a background one. The adapter
///     guards its state with a lock either way, and a test that raced its own assertions would
///     prove nothing about the adapter.
///     </para>
/// </remarks>
internal sealed class FakeCopilotTurnChannel : ICopilotTurnChannel
{
    /// <summary>
    ///     The session's event handler, as the configuration carried it.
    /// </summary>
    private readonly Action<SessionEvent>? _onEvent;

    /// <summary>
    ///     The turns this channel will perform, in order.
    /// </summary>
    private readonly Queue<ScriptedTurn> _turns;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeCopilotTurnChannel"/> class.
    /// </summary>
    /// <param name="onEvent">The session's event handler, or <see langword="null"/> when none was registered.</param>
    /// <param name="turns">The turns to perform, in order. A turn beyond the script is silent.</param>
    internal FakeCopilotTurnChannel(Action<SessionEvent>? onEvent, params ScriptedTurn[] turns)
    {
        _onEvent = onEvent;
        _turns = new Queue<ScriptedTurn>(turns);
    }

    /// <summary>
    ///     Gets the prompts this channel was sent, in order.
    /// </summary>
    internal List<string> Prompts { get; } = [];

    /// <summary>
    ///     Gets the number of times this channel has been released.
    /// </summary>
    internal int DisposeCount { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    ///     The token is deliberately not observed here. The production channel hands it to the
    ///     runtime, which is where it belongs — but a fake that refused a canceled turn of its own
    ///     accord would make the session's own pre-flight check unreachable in a test, and a check no
    ///     test can reach is a check no test can falsify.
    /// </remarks>
    public Task<AssistantMessageEvent?> SendAndWaitAsync(string prompt, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        Prompts.Add(prompt);

        // A turn beyond the script dispatches nothing and answers nothing, which is the shape a test
        // wants when it is asserting that no further turn was taken.
        var turn = _turns.Count > 0 ? _turns.Dequeue() : new ScriptedTurn([]);

        foreach (var dispatched in turn.Events)
        {
            _onEvent?.Invoke(dispatched);
        }

        return turn.Failure is null
            ? Task.FromResult(turn.Answer)
            : Task.FromException<AssistantMessageEvent?>(turn.Failure);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return default;
    }
}

/// <summary>
///     A stand-in for the Copilot runtime: opens fake channels, keeps every configuration it was
///     handed, and keeps every channel it opened.
/// </summary>
/// <remarks>
///     Keeping the configurations is the point. What a rotation seeds into a session — the derived
///     allow-list, the disabled runtime compaction, the rendered conversation record — is decided at
///     creation and is observable nowhere else, so a test asserts on the configuration this runtime
///     was asked to create a session from.
/// </remarks>
internal sealed class FakeCopilotRuntime
{
    /// <summary>
    ///     Supplies the script for the session with a given index, so a test can script a rotation's
    ///     replacement differently from its predecessor.
    /// </summary>
    private readonly Func<int, ScriptedTurn[]> _script;

    /// <summary>
    ///     Runs before a channel is opened, so a test can model a cancellation that arrives while
    ///     the create RPC is in flight.
    /// </summary>
    private readonly Action<int>? _beforeOpen;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeCopilotRuntime"/> class.
    /// </summary>
    /// <param name="script">The turns each session performs, by zero-based session index.</param>
    /// <param name="beforeOpen">Runs before each open, or <see langword="null"/> for none.</param>
    internal FakeCopilotRuntime(Func<int, ScriptedTurn[]>? script = null, Action<int>? beforeOpen = null)
    {
        _script = script ?? (_ => []);
        _beforeOpen = beforeOpen;
    }

    /// <summary>
    ///     Gets the configurations sessions were asked to be created from, in order.
    /// </summary>
    internal List<SessionConfig> Configs { get; } = [];

    /// <summary>
    ///     Gets the channels opened, in order.
    /// </summary>
    internal List<FakeCopilotTurnChannel> Channels { get; } = [];

    /// <summary>
    ///     Gets the opener to hand to a factory or a summarizer under test.
    /// </summary>
    /// <remarks>
    ///     The opener deliberately does <em>not</em> observe the cancellation token itself. A real
    ///     create RPC can complete and return a session at the moment the caller's token is canceled,
    ///     which is the window in which a session exists that nothing yet owns — and the only window
    ///     the factory's guard is there for. Modeling the opener as refusing instead would make that
    ///     window unreachable in a test.
    /// </remarks>
    internal CopilotChannelOpener Opener => (config, _) =>
    {
        _beforeOpen?.Invoke(Configs.Count);

        Configs.Add(config);
        var channel = new FakeCopilotTurnChannel(config.OnEvent, _script(Channels.Count));
        Channels.Add(channel);
        return Task.FromResult<ICopilotTurnChannel>(channel);
    };
}

/// <summary>
///     Builds the runtime events a scripted turn dispatches.
/// </summary>
/// <remarks>
///     Named builders rather than object initializers at each call site, so a test reads as the
///     sequence of runtime behavior it is describing and the payload shapes are stated once.
/// </remarks>
internal static class CopilotEvents
{
    /// <summary>
    ///     Builds the usage reading the runtime reports for a session.
    /// </summary>
    /// <param name="currentTokens">The tokens the session occupies.</param>
    /// <param name="tokenLimit">The window the runtime will hold.</param>
    /// <param name="conversationTokens">The conversation's share, or <see langword="null"/> for no split.</param>
    /// <returns>The usage event.</returns>
    internal static SessionUsageInfoEvent Usage(
        long currentTokens,
        long tokenLimit,
        long? conversationTokens = null) =>
        new()
        {
            Data = new SessionUsageInfoData
            {
                CurrentTokens = currentTokens,
                TokenLimit = tokenLimit,
                ConversationTokens = conversationTokens,
                MessagesLength = 1,
            },
        };

    /// <summary>
    ///     Builds an assistant message the runtime produced.
    /// </summary>
    /// <param name="content">The message text.</param>
    /// <param name="parentToolCallId">
    ///     The call that started a nested agent, when the message belongs to one.
    /// </param>
    /// <returns>The assistant message event.</returns>
    internal static AssistantMessageEvent Assistant(string content, string? parentToolCallId = null) =>
        new()
        {
            Data = new AssistantMessageData
            {
                Content = content,
                MessageId = $"message-{Interlocked.Increment(ref _sequence)}",
                ParentToolCallId = parentToolCallId,
            },
        };

    /// <summary>
    ///     Builds the start of a tool execution.
    /// </summary>
    /// <remarks>
    ///     <paramref name="toolCallId"/> is nullable although the SDK marks the member required,
    ///     because the runtime's events arrive by JSON deserialization, which sets members directly
    ///     and does not enforce that modifier. An identifier that is absent on the wire is therefore
    ///     representable, and the adapter's handling of it is worth proving.
    /// </remarks>
    /// <param name="toolCallId">The runtime's identifier for the call.</param>
    /// <param name="toolName">The tool being called.</param>
    /// <param name="parentToolCallId">The call that started a nested agent, when this belongs to one.</param>
    /// <returns>The tool-execution start event.</returns>
    internal static ToolExecutionStartEvent ToolStart(
        string? toolCallId,
        string toolName,
        string? parentToolCallId = null) =>
        new()
        {
            Data = new ToolExecutionStartData
            {
                ToolCallId = toolCallId!,
                ToolName = toolName,
                ParentToolCallId = parentToolCallId,
            },
        };

    /// <summary>
    ///     Builds a successful tool completion.
    /// </summary>
    /// <param name="toolCallId">The identifier of the call this answers.</param>
    /// <param name="content">The result the tool produced.</param>
    /// <param name="parentToolCallId">The call that started a nested agent, when this belongs to one.</param>
    /// <returns>The tool-execution completion event.</returns>
    internal static ToolExecutionCompleteEvent ToolComplete(
        string toolCallId,
        string content,
        string? parentToolCallId = null) =>
        new()
        {
            Data = new ToolExecutionCompleteData
            {
                ToolCallId = toolCallId,
                Success = true,
                Result = new ToolExecutionCompleteResult { Content = content },
                ParentToolCallId = parentToolCallId,
            },
        };

    /// <summary>
    ///     Builds a failed tool completion.
    /// </summary>
    /// <param name="toolCallId">The identifier of the call this answers.</param>
    /// <param name="message">The error the runtime reported.</param>
    /// <returns>The tool-execution completion event.</returns>
    internal static ToolExecutionCompleteEvent ToolFailure(string toolCallId, string message) =>
        new()
        {
            Data = new ToolExecutionCompleteData
            {
                ToolCallId = toolCallId,
                Success = false,
                Error = new ToolExecutionCompleteError { Message = message },
            },
        };

    /// <summary>
    ///     Builds the event the runtime raises when it truncates a session's history itself.
    /// </summary>
    /// <returns>The truncation event.</returns>
    internal static SessionTruncationEvent Truncation() =>
        new()
        {
            Data = new SessionTruncationData
            {
                PerformedBy = "runtime",
                TokenLimit = 8000,
                MessagesRemovedDuringTruncation = 2,
                TokensRemovedDuringTruncation = 400,
                PreTruncationMessagesLength = 10,
                PreTruncationTokensInMessages = 7600,
                PostTruncationMessagesLength = 8,
                PostTruncationTokensInMessages = 7200,
            },
        };

    /// <summary>
    ///     Builds the event the runtime raises when it starts compacting a session itself.
    /// </summary>
    /// <returns>The compaction-start event.</returns>
    internal static SessionCompactionStartEvent CompactionStart() =>
        new() { Data = new SessionCompactionStartData() };

    /// <summary>
    ///     Builds a session error the runtime reported.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>The session error event.</returns>
    internal static SessionErrorEvent Error(string message) =>
        new() { Data = new SessionErrorData { Message = message, ErrorType = "model" } };

    /// <summary>
    ///     Supplies distinct message identifiers, which the SDK requires of every assistant message.
    /// </summary>
    private static int _sequence;
}
