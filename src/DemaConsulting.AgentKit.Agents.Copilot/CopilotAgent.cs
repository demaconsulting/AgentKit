using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Agents.AI;
using Microsoft.Copilot;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     A Microsoft Agent Framework <see cref="AIAgent"/> over confined Copilot sessions: AgentKit's
///     own agent for the Copilot runtime.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why AgentKit carries its own agent.</b> The Copilot SDK ships no Agent Framework agent, and
///     the adapter that once supplied one does not support the current SDK. Every session this agent
///     opens is configured by <see cref="CopilotAgentFactory"/>, so the agent adds lifecycle,
///     streaming and serialization around the factory's confinement and changes none of it.
///     </para>
///     <para>
///     <b>Lifecycle: open or resume per run, release after it.</b> The first run on a
///     <see cref="CopilotAgentSession"/> creates a runtime session and records its identifier; every
///     later run resumes that session with a freshly built, identically confined configuration.
///     After every run — successful, failed or canceled — the runtime session handle is released
///     <em>without</em> deleting the session, so its state stays resumable under the client's data
///     directory and no handle outlives a run. A run made without a conversation is the exception:
///     nothing can ever resume its session, so that session is deleted when the run ends rather than
///     left on disk. The client is never disposed here.
///     </para>
///     <para>
///     <b>Configurations are built fresh for every run</b>, by delegates the factory supplies, and
///     never reused or cloned: the SDK mutates the options it is handed, and its clone is shallow.
///     </para>
///     <para>
///     <b>Streaming.</b> Root-agent text deltas become <see cref="TextContent"/> updates; the final
///     assistant message is carried as raw content only, so its text is not duplicated, unless no
///     delta arrived for it. Tool starts and completions become <see cref="FunctionCallContent"/> and
///     <see cref="FunctionResultContent"/>; the runtime's per-request usage becomes
///     <see cref="UsageContent"/>; a session error fails the run. Events a sub-agent produced, and
///     every other kind of event, are not streamed.
///     </para>
///     <para>
///     <b>Input.</b> The text of every input message is joined with a newline into one prompt, and
///     every <see cref="DataContent"/> becomes an in-memory blob attachment — no temporary file is
///     written, so the runtime never needs a file-read permission the confinement would refuse. The
///     runtime holds the conversation, so earlier messages are never replayed. Other content kinds,
///     such as <see cref="UriContent"/>, are not sent, and <see cref="AgentRunOptions"/> is ignored.
///     </para>
///     <para>
///     <b>Thread safety.</b> The agent holds no mutable state and may be shared. Runs on one
///     <see cref="CopilotAgentSession"/> are serialized, so two concurrent first runs cannot fork a
///     conversation into two runtime sessions; runs on different sessions proceed independently.
///     Serializing a session does not wait for a run in progress on it; it returns a snapshot.
///     </para>
/// </remarks>
internal sealed class CopilotAgent : AIAgent
{
    /// <summary>
    ///     The name an agent reports when the host names none.
    /// </summary>
    internal const string DefaultName = "GitHub Copilot Agent";

    /// <summary>
    ///     The description every agent this package builds reports.
    /// </summary>
    internal const string DefaultDescription = "An AI agent powered by GitHub Copilot";

    /// <summary>
    ///     The result a failed tool call reports when the runtime gave no reason.
    /// </summary>
    private const string ToolFailedMessage = "Tool execution failed";

    /// <summary>
    ///     Builds the confined configuration a first run creates its runtime session from.
    /// </summary>
    private readonly Func<CreateSessionOptions> _createOptions;

    /// <summary>
    ///     Builds the identically confined configuration a later run resumes its runtime session with.
    /// </summary>
    private readonly Func<ResumeSessionOptions> _resumeOptions;

    /// <summary>
    ///     Creates a runtime session a conversation keeps after the run.
    /// </summary>
    private readonly CopilotChannelOpener _opener;

    /// <summary>
    ///     Creates a runtime session for a run made without a conversation, deleted after the run.
    /// </summary>
    private readonly CopilotChannelOpener _ephemeralOpener;

    /// <summary>
    ///     Resumes a recorded runtime session and presents it as a turn channel.
    /// </summary>
    private readonly CopilotChannelResumer _resumer;

    /// <summary>
    ///     The agent's name as the host supplied it, or <see langword="null"/> for the default.
    /// </summary>
    private readonly string? _name;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotAgent"/> class.
    /// </summary>
    /// <remarks>
    ///     Every collaborator is injected so the whole run — opening, resuming, streaming and
    ///     releasing — can be exercised against a scripted channel; the factory supplies the
    ///     production channels over the client of the runtime AgentKit owns.
    /// </remarks>
    /// <param name="createOptions">Builds a fresh, confined configuration for a new runtime session.</param>
    /// <param name="resumeOptions">Builds a fresh, identically confined configuration for a resumed one.</param>
    /// <param name="opener">Creates a runtime session a conversation keeps after the run.</param>
    /// <param name="ephemeralOpener">
    ///     Creates a runtime session for a run made without a conversation, released and deleted after
    ///     the run.
    /// </param>
    /// <param name="resumer">Resumes a recorded runtime session.</param>
    /// <param name="name">The agent's name, or <see langword="null"/>/blank for <see cref="DefaultName"/>.</param>
    /// <exception cref="ArgumentNullException">Any delegate is <see langword="null"/>.</exception>
    internal CopilotAgent(
        Func<CreateSessionOptions> createOptions,
        Func<ResumeSessionOptions> resumeOptions,
        CopilotChannelOpener opener,
        CopilotChannelOpener ephemeralOpener,
        CopilotChannelResumer resumer,
        string? name)
    {
        ArgumentNullException.ThrowIfNull(createOptions);
        ArgumentNullException.ThrowIfNull(resumeOptions);
        ArgumentNullException.ThrowIfNull(opener);
        ArgumentNullException.ThrowIfNull(ephemeralOpener);
        ArgumentNullException.ThrowIfNull(resumer);

        _createOptions = createOptions;
        _resumeOptions = resumeOptions;
        _opener = opener;
        _ephemeralOpener = ephemeralOpener;
        _resumer = resumer;
        _name = string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <inheritdoc/>
    public override string? Name => _name ?? DefaultName;

    /// <inheritdoc/>
    public override string? Description => DefaultDescription;

    /// <inheritdoc/>
    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AgentSession>(new CopilotAgentSession());

    /// <inheritdoc/>
    /// <remarks>
    ///     Takes a snapshot without waiting for a run in progress on the session: the run holds the
    ///     session's gate across its whole streamed enumeration, so waiting on it would deadlock a
    ///     host that serializes from inside its own streaming loop.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="session"/> was not created by an agent of this kind.
    /// </exception>
    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        return ValueTask.FromResult(AsCopilotSession(session).Serialize());
    }

    /// <inheritdoc/>
    /// <exception cref="ArgumentException">
    ///     <paramref name="serializedState"/> is not a serialized Copilot agent session.
    /// </exception>
    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AgentSession>(CopilotAgentSession.Deserialize(serializedState));

    /// <inheritdoc/>
    /// <remarks>
    ///     The streamed run, gathered into one response, so the two run methods cannot disagree.
    /// </remarks>
    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default) =>
        RunCoreStreamingAsync(messages, session, options, cancellationToken).ToAgentResponseAsync(cancellationToken);

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="session"/> was not created by an agent of this kind, or the runtime
    ///     reported a session error.
    /// </exception>
    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // Resolve the conversation and compose the message before anything is opened, and outside
        // the iterator, so a foreign session or a malformed input fails at the call rather than at
        // the first read, and without touching the runtime.
        var conversation = session is null ? new CopilotAgentSession() : AsCopilotSession(session);
        var message = ComposeMessage(messages);

        // A run made without a conversation has nothing that could resume its runtime session, so
        // that session is deleted when the run ends rather than kept.
        return RunTurnAsync(conversation, ephemeral: session is null, message, cancellationToken);
    }

    /// <summary>
    ///     Runs one turn on a conversation and streams what it produces.
    /// </summary>
    /// <param name="conversation">The conversation the run belongs to.</param>
    /// <param name="ephemeral">Whether the conversation exists only for this run, so its session is deleted after it.</param>
    /// <param name="message">The composed message to send.</param>
    /// <param name="cancellationToken">The run's deadline.</param>
    /// <returns>The run's updates, in the order the runtime produced them.</returns>
    /// <exception cref="InvalidOperationException">The runtime reported a session error.</exception>
    private async IAsyncEnumerable<AgentResponseUpdate> RunTurnAsync(
        CopilotAgentSession conversation,
        bool ephemeral,
        MessageOptions message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await conversation.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The run's own token lets an abandoned enumeration stop the turn it no longer reads.
            using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var updates = Channel.CreateUnbounded<AgentResponseUpdate>(
                new UnboundedChannelOptions { SingleReader = true });
            var mapper = new UpdateMapper(Id, Name, updates.Writer);

            var channel = await OpenAsync(conversation, ephemeral, mapper.OnEvent, runCancellation.Token).ConfigureAwait(false);
            Task? send = null;
            try
            {
                send = SendAsync(channel, message, updates.Writer, runCancellation.Token);

                await foreach (var update in updates.Reader.ReadAllAsync(runCancellation.Token).ConfigureAwait(false))
                {
                    yield return update;
                }

                await send.ConfigureAwait(false);
            }
            finally
            {
                await FinishAsync(send, runCancellation).ConfigureAwait(false);

                // Released on every path: kept for a conversation, which continues on the next run,
                // and deleted for a run made without one. The channel's release does not throw, so it
                // cannot mask a run's failure.
                await channel.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            conversation.Gate.Release();
        }
    }

    /// <summary>
    ///     Opens the runtime session a run takes its turn on: created on the first run, resumed after.
    /// </summary>
    /// <remarks>
    ///     The event handler is set on the freshly built configuration before the session is opened,
    ///     because the SDK registers it before the create or resume request is issued — which is what
    ///     makes the run's first events observable. The identifier is recorded as soon as a session
    ///     exists, before any turn is taken, so a run that fails still leaves the conversation naming
    ///     its runtime session rather than an orphaned one. The runtime has been observed to refuse
    ///     resuming a session that never took a turn ("Session not found"), so after a first run that
    ///     failed or was canceled before its turn reached the runtime, a later run's resume fails.
    /// </remarks>
    /// <param name="conversation">The conversation the run belongs to.</param>
    /// <param name="ephemeral">Whether the conversation exists only for this run.</param>
    /// <param name="onEvent">The run's event handler.</param>
    /// <param name="cancellationToken">Cancels the open.</param>
    /// <returns>The opened channel. Ownership passes to the caller.</returns>
    /// <exception cref="InvalidOperationException">The opener or resumer returned <see langword="null"/>.</exception>
    private async Task<ICopilotTurnChannel> OpenAsync(
        CopilotAgentSession conversation,
        bool ephemeral,
        Action<SessionEvent> onEvent,
        CancellationToken cancellationToken)
    {
        if (conversation.SessionId is { } sessionId)
        {
            var resume = _resumeOptions();
            resume.Events.OnEvent = onEvent;
            return await _resumer(sessionId, resume, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The Copilot channel resumer returned null.");
        }

        var create = _createOptions();
        create.Events.OnEvent = onEvent;
        var opener = ephemeral ? _ephemeralOpener : _opener;
        var channel = await opener(create, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The Copilot channel opener returned null.");

        conversation.SessionId = channel.SessionId;
        return channel;
    }

    /// <summary>
    ///     Takes one turn and completes the update stream when it ends.
    /// </summary>
    /// <param name="channel">The channel to take the turn on.</param>
    /// <param name="message">The message to send.</param>
    /// <param name="writer">The update stream to complete.</param>
    /// <param name="cancellationToken">The run's only deadline.</param>
    /// <returns>A task that completes with the turn.</returns>
    private static async Task SendAsync(
        ICopilotTurnChannel channel,
        MessageOptions message,
        ChannelWriter<AgentResponseUpdate> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await channel.SendAndWaitAsync(message, cancellationToken).ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (Exception failure)
        {
            // Completing the stream with the failure is what surfaces it to the reader; the task's
            // own exception is observed by FinishAsync.
            writer.TryComplete(failure);
            throw;
        }
    }

    /// <summary>
    ///     Brings a run's turn to rest before its channel is released.
    /// </summary>
    /// <remarks>
    ///     A reader that stops early — an abandoned enumeration, a failure surfaced through the
    ///     stream — can leave the turn still in flight. It is canceled and awaited here so the
    ///     channel is never released underneath a turn, and its outcome is observed without being
    ///     rethrown: whatever went wrong already reached the caller through the stream, or the caller
    ///     stopped listening.
    /// </remarks>
    /// <param name="send">The turn, or <see langword="null"/> when none was started.</param>
    /// <param name="runCancellation">The run's own cancellation.</param>
    /// <returns>A task that completes when the turn has ended.</returns>
    private static async Task FinishAsync(Task? send, CancellationTokenSource runCancellation)
    {
        if (send is null)
        {
            return;
        }

        if (!send.IsCompleted)
        {
            await runCancellation.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await send.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Intentionally observed and not rethrown: see the remarks.
        }
    }

    /// <summary>
    ///     Composes the message a run sends: the input text as the prompt, and its data as attachments.
    /// </summary>
    /// <param name="messages">The run's input messages.</param>
    /// <returns>The message to send.</returns>
    internal static MessageOptions ComposeMessage(IEnumerable<ChatMessage> messages)
    {
        var input = messages.ToList();

        var message = new MessageOptions
        {
            Prompt = string.Join("\n", input.Select(item => item.Text).Where(text => !string.IsNullOrEmpty(text))),
        };

        var attachments = input
            .SelectMany(item => item.Contents)
            .OfType<DataContent>()
            .Select((data, index) => (Attachment)ToAttachment(data, index + 1))
            .ToList();

        if (attachments.Count > 0)
        {
            message.Attachments = attachments;
        }

        return message;
    }

    /// <summary>
    ///     Converts a piece of inline data into an in-memory blob attachment.
    /// </summary>
    /// <param name="data">The data to attach.</param>
    /// <param name="ordinal">The attachment's one-based position, used to name an unnamed one.</param>
    /// <returns>The attachment.</returns>
    private static AttachmentBlob ToAttachment(DataContent data, int ordinal) =>
        new()
        {
            Data = data.Base64Data.ToString(),
            MimeType = data.MediaType,
            DisplayName = string.IsNullOrWhiteSpace(data.Name)
                ? $"attachment-{ordinal}{ExtensionFor(data.MediaType)}"
                : data.Name,
        };

    /// <summary>
    ///     Derives a file extension from a media type, so a model shown an unnamed attachment can
    ///     still tell what kind of data it is.
    /// </summary>
    /// <param name="mediaType">The media type, for example <c>image/png</c>.</param>
    /// <returns>The extension with its leading dot, or an empty string when none can be derived.</returns>
    private static string ExtensionFor(string mediaType)
    {
        var slash = mediaType.IndexOf('/', StringComparison.Ordinal);
        var subtype = slash < 0 ? string.Empty : mediaType[(slash + 1)..];
        return subtype.Length > 0 && subtype.All(char.IsLetterOrDigit) ? $".{subtype}" : string.Empty;
    }

    /// <summary>
    ///     Accepts only a conversation an agent of this kind created.
    /// </summary>
    /// <param name="session">The session to accept.</param>
    /// <returns>The session as a Copilot agent session.</returns>
    /// <exception cref="InvalidOperationException">The session belongs to another kind of agent.</exception>
    private static CopilotAgentSession AsCopilotSession(AgentSession session) =>
        session as CopilotAgentSession
        ?? throw new InvalidOperationException(
            $"The session is a {session.GetType().Name}; a Copilot agent accepts only a {nameof(CopilotAgentSession)} it created.");

    /// <summary>
    ///     Turns one run's session events into agent updates and writes them to the run's stream.
    /// </summary>
    /// <remarks>
    ///     One instance per run, because it remembers which assistant messages were already streamed
    ///     as deltas. Its handler runs on the runtime's dispatch thread, serially; it never throws,
    ///     because an exception there would be attributed to the SDK rather than to this agent.
    /// </remarks>
    private sealed class UpdateMapper
    {
        /// <summary>
        ///     The agent's identifier, stamped on every update.
        /// </summary>
        private readonly string _agentId;

        /// <summary>
        ///     The agent's name, stamped on every update as its author.
        /// </summary>
        private readonly string? _authorName;

        /// <summary>
        ///     The run's update stream.
        /// </summary>
        private readonly ChannelWriter<AgentResponseUpdate> _writer;

        /// <summary>
        ///     The assistant messages whose text already arrived as deltas.
        /// </summary>
        private readonly HashSet<string> _streamed = new(StringComparer.Ordinal);

        /// <summary>
        ///     Initializes a new instance of the <see cref="UpdateMapper"/> class.
        /// </summary>
        /// <param name="agentId">The agent's identifier.</param>
        /// <param name="authorName">The agent's name.</param>
        /// <param name="writer">The run's update stream.</param>
        internal UpdateMapper(string agentId, string? authorName, ChannelWriter<AgentResponseUpdate> writer)
        {
            _agentId = agentId;
            _authorName = authorName;
            _writer = writer;
        }

        /// <summary>
        ///     Handles one session event.
        /// </summary>
        /// <remarks>
        ///     A root-agent session error ends the stream with that error, so the run fails naming
        ///     it rather than ending as though the turn had succeeded. Events a sub-agent produced
        ///     are skipped: they were never part of this conversation.
        /// </remarks>
        /// <param name="sessionEvent">The event the runtime dispatched.</param>
        internal void OnEvent(SessionEvent sessionEvent)
        {
            if (sessionEvent.AgentId is not null)
            {
                return;
            }

            if (sessionEvent is SessionErrorEvent { Data: { } error })
            {
                _writer.TryComplete(new InvalidOperationException($"Session error: {error.Message}"));
                return;
            }

            if (ToUpdate(sessionEvent) is { } update)
            {
                _writer.TryWrite(update);
            }
        }

        /// <summary>
        ///     Maps one root-agent event onto an update, or nothing.
        /// </summary>
        /// <param name="sessionEvent">The event.</param>
        /// <returns>The update, or <see langword="null"/> for an event with no agent-visible content.</returns>
        private AgentResponseUpdate? ToUpdate(SessionEvent sessionEvent)
        {
            switch (sessionEvent)
            {
                case AssistantMessageDeltaEvent { Data: { DeltaContent: { Length: > 0 } delta } data }:
                    if (data.MessageId is { } streamedId)
                    {
                        _streamed.Add(streamedId);
                    }

                    return Update(sessionEvent, ChatRole.Assistant, new TextContent(delta) { RawRepresentation = sessionEvent }, data.MessageId);

                case AssistantMessageEvent { Data: { } message }:
                    // Carried raw when its text already streamed, so the text is not duplicated; as
                    // text when it did not, so a runtime that sent no deltas still loses nothing.
                    AIContent content = message.MessageId is { } id && _streamed.Contains(id)
                        || string.IsNullOrEmpty(message.Content)
                            ? new AIContent { RawRepresentation = sessionEvent }
                            : new TextContent(message.Content) { RawRepresentation = sessionEvent };
                    var final = Update(sessionEvent, ChatRole.Assistant, content, message.MessageId);
                    final.ResponseId = message.MessageId;
                    return final;

                case ToolExecutionStartEvent { Data: { ToolCallId: { Length: > 0 } callId, ToolName: { } tool } start }:
                    return Update(
                        sessionEvent,
                        ChatRole.Assistant,
                        new FunctionCallContent(callId, tool, ToArguments(start.Arguments)) { RawRepresentation = sessionEvent },
                        messageId: null);

                case ToolExecutionCompleteEvent { Data: { ToolCallId: { Length: > 0 } resultId } complete }:
                    var result = complete.Success
                        ? complete.Result?.Content
                        : complete.Error?.Message ?? ToolFailedMessage;
                    return Update(
                        sessionEvent,
                        ChatRole.Tool,
                        new FunctionResultContent(resultId, result) { RawRepresentation = sessionEvent },
                        messageId: null);

                case AssistantUsageEvent { Data: { } usage }:
                    return Update(
                        sessionEvent,
                        ChatRole.Assistant,
                        new UsageContent(ToUsageDetails(usage)) { RawRepresentation = sessionEvent },
                        messageId: null);

                default:
                    return null;
            }
        }

        /// <summary>
        ///     Wraps one content item in an update stamped with the agent and the event's time.
        /// </summary>
        /// <param name="sessionEvent">The event the content came from.</param>
        /// <param name="role">The role the content speaks with.</param>
        /// <param name="content">The content.</param>
        /// <param name="messageId">The assistant message the content belongs to, if any.</param>
        /// <returns>The update.</returns>
        private AgentResponseUpdate Update(SessionEvent sessionEvent, ChatRole role, AIContent content, string? messageId) =>
            new(role, [content])
            {
                AgentId = _agentId,
                AuthorName = _authorName,
                CreatedAt = sessionEvent.Timestamp,
                MessageId = messageId,
                RawRepresentation = sessionEvent,
            };

        /// <summary>
        ///     Converts the runtime's per-request usage into the shape Microsoft.Extensions.AI uses.
        /// </summary>
        /// <param name="usage">The runtime's usage report.</param>
        /// <returns>The usage details.</returns>
        private static UsageDetails ToUsageDetails(AssistantUsageData usage)
        {
            var details = new UsageDetails
            {
                InputTokenCount = usage.InputTokens,
                OutputTokenCount = usage.OutputTokens,
                TotalTokenCount = usage.InputTokens + usage.OutputTokens,
                CachedInputTokenCount = usage.CacheReadTokens,
                ReasoningTokenCount = usage.ReasoningTokens,
            };

            // The runtime reports a few figures Microsoft.Extensions.AI has no field for; the whole
            // numbers are kept rather than dropped. Fractional figures such as the request's cost
            // stay on the raw event the update carries, because these counts are integers.
            var additional = new AdditionalPropertiesDictionary<long>();
            if (usage.CacheWriteTokens is { } cacheWrite)
            {
                additional[nameof(AssistantUsageData.CacheWriteTokens)] = cacheWrite;
            }

            if (usage.Duration is { } duration)
            {
                additional["DurationMilliseconds"] = (long)duration.TotalMilliseconds;
            }

            if (additional.Count > 0)
            {
                details.AdditionalCounts = additional;
            }

            return details;
        }

        /// <summary>
        ///     Converts a tool call's raw JSON arguments into the dictionary shape Microsoft.Extensions.AI uses.
        /// </summary>
        /// <param name="arguments">The raw arguments.</param>
        /// <returns>The arguments, or <see langword="null"/> when there were none.</returns>
        private static Dictionary<string, object?>? ToArguments(JsonElement? arguments) =>
            arguments is { ValueKind: JsonValueKind.Object } json
                ? json.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone())
                : null;
    }
}
