using System.Text.Json;
using Microsoft.Agents.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     One conversation with an agent built by <see cref="CopilotAgentFactory"/>: the identity of
///     the Copilot runtime session that holds it, and the Agent Framework state bag.
/// </summary>
/// <remarks>
///     <para>
///     <b>The runtime holds the conversation; this object only names it.</b> A Copilot session keeps
///     its own history, so nothing is replayed from here. The first run creates a runtime session
///     and records its identifier in <see cref="SessionId"/>; every later run resumes that session,
///     confined exactly as it was when it was created.
///     </para>
///     <para>
///     <b>Serializable, so a conversation can outlive the process.</b>
///     <c>AIAgent.SerializeSessionAsync</c> writes the JSON object
///     <c>{ "sessionId": …, "stateBag": … }</c>, and <c>AIAgent.DeserializeSessionAsync</c> on any
///     agent built over the same runtime data directory reads it back and resumes the same runtime
///     session. The shape is the one the earlier Agent Framework Copilot adapter wrote, so a session
///     serialized by it remains readable.
///     </para>
///     <para>
///     <b>Deleting the conversation is the host's decision.</b> The agent releases the runtime
///     session after every run without deleting it, so the conversation stays resumable under the
///     runtime's data directory. A host that is finished with it passes <see cref="SessionId"/> to
///     <see cref="CopilotRuntime.DeleteSessionAsync"/>.
///     </para>
///     <para>
///     Runs on one session are serialized by the agent, and <see cref="SessionId"/> is written only
///     while a run holds that serialization, so two concurrent runs cannot fork the conversation.
///     Serialization is a snapshot and does <em>not</em> wait for a run in progress. A run records
///     the identifier as soon as it has opened its runtime session, before its first update is
///     produced, but a session serialized before that point carries no identifier. Serialize after
///     a run has returned, or after its first update, to be sure the identifier is included.
///     Waiting on the run would deadlock a host that serializes from inside its own streaming loop.
///     </para>
/// </remarks>
public sealed class CopilotAgentSession : AgentSession
{
    /// <summary>
    ///     The JSON property carrying the runtime session identifier.
    /// </summary>
    private const string SessionIdProperty = "sessionId";

    /// <summary>
    ///     The JSON property carrying the Agent Framework state bag.
    /// </summary>
    private const string StateBagProperty = "stateBag";

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotAgentSession"/> class for a
    ///     conversation that has not yet run.
    /// </summary>
    internal CopilotAgentSession()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotAgentSession"/> class for a
    ///     conversation restored from its serialized form.
    /// </summary>
    /// <param name="sessionId">The runtime session to resume, or <see langword="null"/> when none was recorded.</param>
    /// <param name="stateBag">The restored state bag.</param>
    private CopilotAgentSession(string? sessionId, AgentSessionStateBag stateBag)
        : base(stateBag)
    {
        SessionId = sessionId;
    }

    /// <summary>
    ///     Gets the identifier of the Copilot runtime session holding this conversation, or
    ///     <see langword="null"/> before the first run.
    /// </summary>
    /// <remarks>
    ///     Pass it to <see cref="CopilotRuntime.DeleteSessionAsync"/> to delete the conversation's
    ///     persisted state once the host is finished with it.
    /// </remarks>
    public string? SessionId { get; internal set; }

    /// <summary>
    ///     Gets the gate that keeps runs on this conversation sequential.
    /// </summary>
    /// <remarks>
    ///     Without it two concurrent first runs could each create a runtime session, and the
    ///     conversation would silently fork. Never disposed: no wait handle is ever requested from
    ///     it, so it holds no unmanaged resource.
    /// </remarks>
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>
    ///     Writes this conversation as the JSON object <c>{ "sessionId": …, "stateBag": … }</c>.
    /// </summary>
    /// <returns>The serialized conversation.</returns>
    internal JsonElement Serialize()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            if (SessionId is null)
            {
                writer.WriteNull(SessionIdProperty);
            }
            else
            {
                writer.WriteString(SessionIdProperty, SessionId);
            }

            writer.WritePropertyName(StateBagProperty);
            StateBag.Serialize().WriteTo(writer);

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>
    ///     Reads a conversation from the JSON object <see cref="Serialize"/> writes.
    /// </summary>
    /// <remarks>
    ///     Both properties are optional, and either may be <c>null</c>, so an object written before a
    ///     first run, or by a writer that carried no state bag, is still accepted. Anything other than
    ///     an object is refused, and so is a property that is present with any other kind, because no
    ///     well-formed serialized session has that shape — restoring it would silently drop state.
    /// </remarks>
    /// <param name="serializedState">The serialized conversation.</param>
    /// <returns>The restored conversation.</returns>
    /// <exception cref="ArgumentException">
    ///     <paramref name="serializedState"/> is not a JSON object, its <c>sessionId</c> is
    ///     neither a string nor <c>null</c>, or its <c>stateBag</c> is neither an object nor
    ///     <c>null</c>.
    /// </exception>
    internal static CopilotAgentSession Deserialize(JsonElement serializedState)
    {
        if (serializedState.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "A serialized Copilot agent session must be a JSON object.",
                nameof(serializedState));
        }

        string? sessionId = null;
        if (serializedState.TryGetProperty(SessionIdProperty, out var id))
        {
            sessionId = id.ValueKind switch
            {
                JsonValueKind.String => id.GetString(),
                JsonValueKind.Null => null,
                _ => throw new ArgumentException(
                    "The 'sessionId' of a serialized Copilot agent session must be a string or null.",
                    nameof(serializedState)),
            };
        }

        var stateBag = new AgentSessionStateBag();
        if (serializedState.TryGetProperty(StateBagProperty, out var bag))
        {
            stateBag = bag.ValueKind switch
            {
                JsonValueKind.Object => AgentSessionStateBag.Deserialize(bag),
                JsonValueKind.Null => new AgentSessionStateBag(),
                _ => throw new ArgumentException(
                    "The 'stateBag' of a serialized Copilot agent session must be an object or null.",
                    nameof(serializedState)),
            };
        }

        return new CopilotAgentSession(sessionId, stateBag);
    }
}
