using Microsoft.Copilot;

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
/// <param name="During">
///     Awaited after the events are dispatched and before the turn ends, or <see langword="null"/>
///     for a turn that ends at once. Given the turn's token, so a test can model a turn that is still
///     running when it is canceled, or one held open until the test releases it.
/// </param>
internal sealed record ScriptedTurn(
    IReadOnlyList<SessionEvent> Events,
    AssistantMessageEvent? Answer = null,
    Exception? Failure = null,
    Func<CancellationToken, Task>? During = null);

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
    /// <param name="sessionId">The identifier the runtime gave the session this channel carries.</param>
    /// <param name="onEvent">The session's event handler, or <see langword="null"/> when none was registered.</param>
    /// <param name="turns">The turns to perform, in order. A turn beyond the script is silent.</param>
    internal FakeCopilotTurnChannel(string sessionId, Action<SessionEvent>? onEvent, params ScriptedTurn[] turns)
    {
        SessionId = sessionId;
        _onEvent = onEvent;
        _turns = new Queue<ScriptedTurn>(turns);
    }

    /// <inheritdoc/>
    public string SessionId { get; }

    /// <summary>
    ///     Gets the prompts this channel was sent, in order.
    /// </summary>
    internal List<string> Prompts { get; } = [];

    /// <summary>
    ///     Gets the whole messages this channel was sent, in order — prompts and attachments.
    /// </summary>
    internal List<MessageOptions> Messages { get; } = [];

    /// <summary>
    ///     Gets the number of times this channel has been released.
    /// </summary>
    internal int DisposeCount { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    ///     The token is deliberately not observed by the channel itself. The production channel hands
    ///     it to the runtime, which is where it belongs — but a fake that refused a canceled turn of
    ///     its own accord would make the callers' own pre-flight checks unreachable in a test, and a
    ///     check no test can reach is a check no test can falsify. A scripted turn that models a
    ///     runtime still working is given the token through <see cref="ScriptedTurn.During"/>.
    /// </remarks>
    public async Task<AssistantMessageEvent?> SendAndWaitAsync(MessageOptions message, CancellationToken cancellationToken)
    {
        Messages.Add(message);
        Prompts.Add(message.Prompt);

        // A turn beyond the script dispatches nothing and answers nothing, which is the shape a test
        // wants when it is asserting that no further turn was taken.
        var turn = _turns.Count > 0 ? _turns.Dequeue() : new ScriptedTurn([]);

        foreach (var dispatched in turn.Events)
        {
            _onEvent?.Invoke(dispatched);
        }

        if (turn.During is not null)
        {
            await turn.During(cancellationToken);
        }

        return turn.Failure is null ? turn.Answer : throw turn.Failure;
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
    internal List<CreateSessionOptions> Configs { get; } = [];

    /// <summary>
    ///     Gets the sessions asked to be resumed, in order, each with the configuration it was
    ///     resumed with.
    /// </summary>
    internal List<(string SessionId, ResumeSessionOptions Config)> Resumes { get; } = [];

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
        var channel = new FakeCopilotTurnChannel($"session-{Configs.Count}", config.Events.OnEvent, _script(Channels.Count));
        Channels.Add(channel);
        return Task.FromResult<ICopilotTurnChannel>(channel);
    };

    /// <summary>
    ///     Gets the resumer to hand to an agent under test.
    /// </summary>
    /// <remarks>
    ///     A resumed channel takes its script from the same sequence as an opened one, by the order
    ///     channels are produced, so a test scripts a conversation's runs in the order they happen
    ///     whether each run created or resumed its session.
    /// </remarks>
    internal CopilotChannelResumer Resumer => (sessionId, config, _) =>
    {
        Resumes.Add((sessionId, config));
        var channel = new FakeCopilotTurnChannel(sessionId, config.Events.OnEvent, _script(Channels.Count));
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
    /// <param name="agentId">
    ///     The sub-agent instance that produced the message, when it belongs to one.
    /// </param>
    /// <param name="messageId">
    ///     The message's identifier, or <see langword="null"/> for a fresh one. Named when a test
    ///     pairs the message with the deltas that streamed it.
    /// </param>
    /// <returns>The assistant message event.</returns>
    internal static AssistantMessageEvent Assistant(string content, string? agentId = null, string? messageId = null) =>
        new()
        {
            Data = new AssistantMessageData
            {
                Content = content,
                MessageId = messageId ?? $"message-{Interlocked.Increment(ref _sequence)}",
            },
            AgentId = agentId,
        };

    /// <summary>
    ///     Builds one streamed fragment of an assistant message.
    /// </summary>
    /// <param name="content">The fragment's text.</param>
    /// <param name="messageId">The message the fragment belongs to.</param>
    /// <param name="agentId">The sub-agent instance that produced it, when it belongs to one.</param>
    /// <returns>The delta event.</returns>
    internal static AssistantMessageDeltaEvent Delta(string content, string messageId, string? agentId = null) =>
        new()
        {
            Data = new AssistantMessageDeltaData { DeltaContent = content, MessageId = messageId },
            AgentId = agentId,
        };

    /// <summary>
    ///     Builds the usage the runtime reports for one model request.
    /// </summary>
    /// <param name="inputTokens">The request's input tokens.</param>
    /// <param name="outputTokens">The request's output tokens.</param>
    /// <returns>The assistant usage event.</returns>
    internal static AssistantUsageEvent AssistantUsage(long inputTokens, long outputTokens) =>
        new()
        {
            Data = new AssistantUsageData
            {
                Model = "test-model",
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CacheReadTokens = 7,
                CacheWriteTokens = 3,
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
    /// <param name="agentId">The sub-agent instance that produced the event, when it belongs to one.</param>
    /// <returns>The tool-execution start event.</returns>
    internal static ToolExecutionStartEvent ToolStart(
        string? toolCallId,
        string toolName,
        string? agentId = null) =>
        new()
        {
            Data = new ToolExecutionStartData
            {
                ToolCallId = toolCallId!,
                ToolName = toolName,
            },
            AgentId = agentId,
        };

    /// <summary>
    ///     Builds a successful tool completion.
    /// </summary>
    /// <param name="toolCallId">The identifier of the call this answers.</param>
    /// <param name="content">The result the tool produced.</param>
    /// <param name="agentId">The sub-agent instance that produced the event, when it belongs to one.</param>
    /// <returns>The tool-execution completion event.</returns>
    internal static ToolExecutionCompleteEvent ToolComplete(
        string toolCallId,
        string content,
        string? agentId = null) =>
        new()
        {
            Data = new ToolExecutionCompleteData
            {
                ToolCallId = toolCallId,
                Success = true,
                Result = new ToolExecutionCompleteResult { Content = content },
            },
            AgentId = agentId,
        };

    /// <summary>
    ///     Builds a failed tool completion.
    /// </summary>
    /// <param name="toolCallId">The identifier of the call this answers.</param>
    /// <param name="message">The error the runtime reported, or <see langword="null"/> for none.</param>
    /// <returns>The tool-execution completion event.</returns>
    internal static ToolExecutionCompleteEvent ToolFailure(string toolCallId, string? message) =>
        new()
        {
            Data = new ToolExecutionCompleteData
            {
                ToolCallId = toolCallId,
                Success = false,
                Error = message is null ? null : new ToolExecutionCompleteError { Message = message },
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
