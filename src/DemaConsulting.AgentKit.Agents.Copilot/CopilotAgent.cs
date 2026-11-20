using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Agents.AI;
using Microsoft.Copilot;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     A Microsoft Agent Framework <see cref="AIAgent"/> over a confined Copilot session.
/// </summary>
/// <remarks>
///     <para>
///     Deliberately minimal. Each <see cref="AgentSession"/> lazily opens one Copilot session from
///     the confined configuration <see cref="CopilotAgentFactory"/> built, and sends each run's
///     user text as one prompt. The runtime keeps the conversation, so nothing is replayed.
///     </para>
///     <para>
///     Session serialization is not supported by this version of the agent.
///     </para>
/// </remarks>
internal sealed class CopilotAgent : AIAgent
{
    /// <summary>
    ///     The host's client. Never disposed here.
    /// </summary>
    private readonly CopilotClient _client;

    /// <summary>
    ///     The confined configuration every session of this agent is created from.
    /// </summary>
    private readonly CreateSessionOptions _template;

    /// <summary>
    ///     The agent's name, if any.
    /// </summary>
    private readonly string? _name;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotAgent"/> class.
    /// </summary>
    /// <param name="client">The host's started client.</param>
    /// <param name="template">The confined session configuration.</param>
    /// <param name="name">The agent's name, if any.</param>
    internal CopilotAgent(CopilotClient client, CreateSessionOptions template, string? name)
    {
        _client = client;
        _template = template;
        _name = name;
    }

    /// <inheritdoc/>
    public override string? Name => _name;

    /// <inheritdoc/>
    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<AgentSession>(new CopilotAgentSession());

    /// <inheritdoc/>
    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Copilot agent sessions cannot be serialized.");

    /// <inheritdoc/>
    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Copilot agent sessions cannot be deserialized.");

    /// <inheritdoc/>
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var contents = new List<AIContent>();
        await foreach (var update in RunCoreStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
        {
            contents.AddRange(update.Contents);
        }

        return new AgentResponse(new ChatMessage(ChatRole.Assistant, contents)) { AgentId = Id };
    }

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agentSession = session as CopilotAgentSession ?? new CopilotAgentSession();
        var prompt = string.Join("\n", messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));

        await agentSession.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var runtime = agentSession.Runtime ??= await _client
                .CreateSessionAsync(_template.Clone(), cancellationToken)
                .ConfigureAwait(false);

            var updates = Channel.CreateUnbounded<AgentResponseUpdate>();
            using var subscription = runtime.On<SessionEvent>(evt =>
            {
                if (ToUpdate(evt) is { } update)
                {
                    updates.Writer.TryWrite(update);
                }
            });

            var send = SendAsync(runtime, prompt, updates.Writer, cancellationToken);

            await foreach (var update in updates.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }

            await send.ConfigureAwait(false);
        }
        finally
        {
            agentSession.Gate.Release();
        }
    }

    /// <summary>
    ///     Sends one prompt and completes the update channel when the turn ends.
    /// </summary>
    /// <param name="runtime">The runtime session.</param>
    /// <param name="prompt">The prompt.</param>
    /// <param name="writer">The update channel to complete.</param>
    /// <param name="cancellationToken">The run's only deadline.</param>
    /// <returns>A task that completes with the turn.</returns>
    private static async Task SendAsync(
        CopilotSession runtime,
        string prompt,
        ChannelWriter<AgentResponseUpdate> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await runtime.SendAndWaitAsync(prompt, Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (Exception failure)
        {
            writer.TryComplete(failure);
            throw;
        }
    }

    /// <summary>
    ///     Maps one root-agent session event onto an agent update, or nothing.
    /// </summary>
    /// <param name="evt">The event.</param>
    /// <returns>The update, or <see langword="null"/> for an event with no agent-visible content.</returns>
    private AgentResponseUpdate? ToUpdate(SessionEvent evt)
    {
        AIContent? content = evt switch
        {
            AssistantMessageEvent { AgentId: null, Data.Content: { Length: > 0 } text } => new TextContent(text),
            ToolExecutionStartEvent { AgentId: null, Data: { ToolCallId: { } id, ToolName: { } tool } start } =>
                new FunctionCallContent(id, tool, ToArguments(start.Arguments)),
            ToolExecutionCompleteEvent { AgentId: null, Data: { ToolCallId: { } id } complete } =>
                new FunctionResultContent(id, complete.Result?.Content ?? complete.Error?.Message),
            _ => null,
        };

        return content is null
            ? null
            : new AgentResponseUpdate(ChatRole.Assistant, [content]) { AgentId = Id, AuthorName = _name };
    }

    /// <summary>
    ///     Converts a tool call's raw JSON arguments into the dictionary shape MEAI uses.
    /// </summary>
    /// <param name="arguments">The raw arguments.</param>
    /// <returns>The arguments, or <see langword="null"/> when there were none.</returns>
    private static Dictionary<string, object?>? ToArguments(JsonElement? arguments) =>
        arguments is { ValueKind: JsonValueKind.Object } json
            ? json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone())
            : null;

    /// <summary>
    ///     One agent conversation: the lazily opened runtime session and the gate serializing runs.
    /// </summary>
    private sealed class CopilotAgentSession : AgentSession
    {
        /// <summary>
        ///     Gets the gate that keeps runs on one session sequential.
        /// </summary>
        internal SemaphoreSlim Gate { get; } = new(1, 1);

        /// <summary>
        ///     Gets or sets the runtime session, once the first run has opened it.
        /// </summary>
        internal CopilotSession? Runtime { get; set; }
    }
}
