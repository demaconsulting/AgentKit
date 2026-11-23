## CopilotAgent

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotAgent` unit is AgentKit's own Microsoft Agent Framework `AIAgent` over confined GitHub
Copilot sessions: the internal `CopilotAgent` class, and the public `CopilotAgentSession` that names
one conversation with it.

### Purpose

**AgentKit carries its own agent because the SDK ships none.** The Copilot SDK offers no Agent
Framework agent, and the adapter package that once supplied one does not support the current SDK. An
application that asks `CopilotAgentFactory` for an agent therefore receives this one, typed as
`AIAgent`.

**It adds lifecycle, streaming and serialization, and changes none of the confinement.** Every
session it opens is configured by `CopilotAgentFactory`, through builder delegates the factory hands
it; this unit never touches a session option beyond registering its own event handler. Its job is
what happens around a turn:

- **Open or resume per run, release after it.** The first run on a `CopilotAgentSession` creates a
  runtime session and records its identifier; every later run resumes that session with freshly
  built, identically confined options. After every run — successful, failed or canceled — the
  runtime session handle is released. A conversation's session is **kept**, not deleted, so it stays
  resumable under the runtime's data directory. A run made without a conversation deletes its
  session, because nothing could resume it. No handle outlives a run. The runtime is never disposed.
- **Stream the run.** The runtime's events for the root agent become `AgentResponseUpdate` items as
  they arrive.
- **Carry the input.** The text of the input messages becomes one prompt, and inline data becomes
  in-memory attachments.
- **Serialize the conversation**, so it can outlive the process and be resumed by another agent
  instance over the same runtime data directory.

**Thread safety.** The agent holds no mutable state and may be shared. Runs on one
`CopilotAgentSession` are serialized, so two concurrent first runs cannot fork a conversation into two
runtime sessions; runs on different sessions proceed independently. Serializing a session does not
wait for a run in progress on it: it returns a snapshot, because a run holds the gate across its whole
streamed enumeration and waiting on it would deadlock a host that serializes from inside its own
streaming loop.

`CopilotAgent` is internal; an application sees it only as an `AIAgent`. `CopilotAgentSession` is
public, because a host needs its `SessionId` to delete a finished conversation.

### Data Model

**`CopilotAgent`** (internal sealed class, derives from `AIAgent`):

- **`_createOptions`** (`Func<CreateSessionOptions>`) — Builds the confined options a first run
  creates its runtime session from. Supplied by `CopilotAgentFactory`; called once per run.
- **`_resumeOptions`** (`Func<ResumeSessionOptions>`) — Builds the identically confined options a
  later run resumes its runtime session with. Supplied by `CopilotAgentFactory`; called once per run.
- **`_opener`** (`CopilotChannelOpener`) — Creates a conversation's runtime session on its first run
  and presents it as a turn channel. In production it keeps the session on release.
- **`_ephemeralOpener`** (`CopilotChannelOpener`) — Creates the runtime session for a run made
  without a conversation. In production it deletes the session on release, because nothing could
  resume it.
- **`_resumer`** (`CopilotChannelResumer`) — Resumes a recorded runtime session and presents it as a
  turn channel. In production it keeps the session on release.
- **`_name`** (`string?`) — The host's name for the agent, or null when the host supplied none or a
  blank one.
- **`_signIn`** (`CopilotSignInPlan?`) — The sign-in of the runtime the sessions run on, whose tokens
  are redacted from a session error and from a turn's relayed failure, or null to redact
  token-shaped text only. Supplied by `CopilotAgentFactory` (`runtime.SignIn` in production), passed
  to each run's `UpdateMapper` and `SendAsync`.
- **`DefaultName`** (`const string`) — `GitHub Copilot Agent`, reported when the host named none.
- **`DefaultDescription`** (`const string`) — `An AI agent powered by GitHub Copilot`, reported by
  every agent.
- **`ToolFailedMessage`** (`const string`) — `Tool execution failed`, the result of a failed tool
  call the runtime gave no reason for.

Invariant: options objects are never held, reused or cloned. The SDK mutates the options it is
handed and its clone is shallow, so each run builds its own through the delegates.

**`CopilotAgentSession`** (public sealed class, derives from `AgentSession`):

- **`SessionId`** (`string?`, public get, internal set) — The identifier of the runtime session that
  holds the conversation, or null before the first run. Invariant: written only while a run holds the
  session's gate, and set as soon as a runtime session exists, before any turn is taken. Read by
  serialization without the gate: serialization is a snapshot.
- **`Gate`** (`SemaphoreSlim`, internal, one slot) — Keeps runs on this conversation sequential. Never
  disposed: no wait handle is ever requested from it, so it holds no unmanaged resource.
- **`StateBag`** (`AgentSessionStateBag`, inherited) — The Agent Framework's state bag, carried
  through serialization.

**The runtime holds the conversation; the session object only names it.** A Copilot session keeps
its own history, so nothing is replayed from here.

**Serialized form.** A `CopilotAgentSession` serializes as one JSON object:

```json
{ "sessionId": "<runtime session identifier, or null>", "stateBag": { } }
```

The shape is the one the earlier Agent Framework Copilot adapter wrote, so a conversation it
serialized remains readable. Both properties are optional when reading, and either may be `null`, so an
object written before a first run, or by a writer that carried no state bag, is accepted.

**`UpdateMapper`** (private nested class, one per run) — Turns the run's session events into updates
and writes them to the run's update stream.

- **`_agentId`**, **`_authorName`** (`string`, `string?`) — The agent's identifier and name, stamped
  on every update.
- **`_writer`** (`ChannelWriter<AgentResponseUpdate>`) — The run's unbounded, single-reader update
  stream.
- **`_streamed`** (`HashSet<string>`) — The assistant messages whose text already arrived as deltas,
  so the final message does not repeat it.

### Key Methods

#### Name and Description

`Name` reports the host's name, or `DefaultName` when the host supplied none. `Description` reports
`DefaultDescription`. Both defaults are those of the earlier adapter, kept for parity.

#### CreateSessionCoreAsync(CancellationToken)

**Purpose:** Start a conversation.

**Algorithm:** Return a new `CopilotAgentSession` with no `SessionId`. Nothing is opened on the
runtime: the runtime session is created by the conversation's first run.

**Postconditions:** A session whose first run will create a runtime session.

#### SerializeSessionCoreAsync(AgentSession session, …) and DeserializeSessionCoreAsync(JsonElement, …)

**Purpose:** Let a conversation outlive the process.

**Algorithm:** Serializing accepts only a `CopilotAgentSession` and writes `sessionId` — the string, or
`null` — and `stateBag`. It does not take the gate, so it never waits for a run in progress and
returns a snapshot. Deserializing refuses anything but a JSON object, refuses a `sessionId` that
is neither a string nor `null`, takes the state bag when it is an object, starts an empty one when it
is absent or `null`, and refuses any other kind. It returns a `CopilotAgentSession` whose next run
resumes the recorded runtime session.

**Resume-by-identifier goes through deserialization.** A host holding only a runtime session
identifier writes the serialized form around it and deserializes it; there is no typed
resume-by-identifier method (see *Intentional Gaps*).

**Preconditions:** The serialized form was produced over the same runtime data directory, so the
runtime still holds the session it names.

**Postconditions:** Serializing returns the object above; deserializing returns a session that
resumes the named runtime session on its next run.

#### RunCoreStreamingAsync(IEnumerable&lt;ChatMessage&gt; messages, AgentSession? session, AgentRunOptions? options, CancellationToken)

**Purpose:** Run one turn and stream what it produces.

**Algorithm:**

1. Reject null `messages`.
2. Resolve the conversation — the supplied session, which must be a `CopilotAgentSession`, or a fresh
   one when none is supplied — and compose the message (see `ComposeMessage`). Both happen before the
   iterator starts, so a foreign session or a malformed input fails at the call, without touching the
   runtime.
3. Return the turn's update stream (see `RunTurnAsync`), marked ephemeral when no session was
   supplied.

`options` is ignored, as it was by the earlier adapter.

**Postconditions:** An asynchronous sequence of updates, in the order the runtime produced them.

#### RunCoreAsync(IEnumerable&lt;ChatMessage&gt; messages, AgentSession? session, AgentRunOptions? options, CancellationToken)

**Purpose:** Run one turn and return its whole response.

**Algorithm:** Gather the streamed run into one `AgentResponse` with the Agent Framework's
`ToAgentResponseAsync`, so the two run methods cannot disagree.

#### RunTurnAsync(CopilotAgentSession conversation, bool ephemeral, MessageOptions message, CancellationToken)

**Purpose:** Run one turn on a conversation, holding its gate, and release the runtime session after
it on every path.

`ephemeral` is true when the caller supplied no session.

**Algorithm:**

1. Wait for the conversation's gate under the caller's token.
2. Create a run-scoped cancellation linked to the caller's token, an unbounded single-reader update
   stream, and an `UpdateMapper` over it.
3. Open the runtime session (see `OpenAsync`), passing `ephemeral`, registering the mapper as the
   event handler.
4. Start the turn (see `SendAsync`) and yield every update from the stream until it completes; then
   await the turn.
5. In a `finally`: bring the turn to rest (see `FinishAsync`), then release the channel, which
   releases the session handle and keeps a conversation's session or deletes the session of a run
   made without one.
6. In an outer `finally`: release the gate.

The release cannot throw, so it cannot mask a run's own failure; see *CopilotTurnChannel Unit Design*.

**Postconditions:** The run's updates have been yielded or its failure propagated; the runtime session
handle is released, the session kept for a conversation and deleted for a run made without one; the
gate is free.

#### OpenAsync(CopilotAgentSession conversation, bool ephemeral, Action&lt;SessionEvent&gt; onEvent, CancellationToken)

**Purpose:** Open the runtime session a run takes its turn on: created on the first run, resumed
after.

**Algorithm:** When the conversation has a `SessionId`, build fresh resume options, set the run's
handler as `Events.OnEvent`, and resume the session through the resumer. Otherwise build fresh create
options, set the handler the same way, create the session through the opener, and record the new
channel's `SessionId` on the conversation at once. When `ephemeral`, the session is created through
the ephemeral opener instead, whose sessions are deleted when released: nothing could
ever resume that session, so keeping it would only accumulate persisted state under the runtime's data
directory. A null channel from either delegate is refused.

The handler is set on the options before the session is opened because the SDK registers it before
the create or resume request is issued, which is what makes the run's first events observable. The
identifier is recorded before any turn is taken, so a run whose turn fails still leaves the
conversation naming its runtime session rather than an orphaned one. The runtime has been observed
to refuse resuming a session that never took a turn ("Session not found"), so after a first run
that failed or was canceled before its turn reached the runtime, a later run's resume fails.

**Postconditions:** An open channel, owned by the caller; on a first run, `SessionId` recorded.

#### SendAsync(ICopilotTurnChannel channel, MessageOptions message, ChannelWriter, CopilotSignInPlan? signIn, CancellationToken)

**Purpose:** Take the turn and complete the update stream when it ends.

**Algorithm:** Send the message and wait for the session to become idle, then complete the stream. On
a failure, relay it through `CopilotSignIn.RedactRelayed(failure, signIn)` — a new exception of the
same kind with a redacted message and the failure's redacted copy as its cause, or the failure itself
for a cancellation carrying nothing to redact — complete the stream with the relayed exception, which
is what surfaces it to the reader, and throw it.

#### FinishAsync(Task? send, CancellationTokenSource runCancellation)

**Purpose:** Bring a run's turn to rest before its channel is released.

**Algorithm:** When no turn was started, return. When the turn is still in flight — a reader that
stopped early, or a failure surfaced through the stream — cancel the run's own token. Await the turn
and observe its outcome without rethrowing it: whatever went wrong already reached the caller through
the stream, or the caller stopped listening.

**Postconditions:** The turn has ended, so the channel is never released underneath it.

#### ComposeMessage(IEnumerable&lt;ChatMessage&gt; messages)

**Purpose:** Compose what a run sends.

**Algorithm:** The prompt is the non-empty text of every input message, joined with a newline. Every
`DataContent` among the messages' contents becomes an `AttachmentBlob` carrying the data as base64,
its media type, and its name — or, when it has none, `attachment-<n>` with the media subtype as an
extension when the subtype is plain letters and digits (`attachment-1.png`). Attachments are set only
when there is at least one. Earlier messages are not replayed: the runtime holds the conversation.

**Blob attachments, not files.** The data travels in memory. No temporary file is written, so the
runtime never needs a file-read permission the default-deny handler would refuse, and nothing is left
on disk. Measured against the live runtime, and re-verified by the opt-in live suite, an image sent
this way reaches the model.

**Postconditions:** One `MessageOptions` carrying the prompt and any attachments.

#### AsCopilotSession(AgentSession session)

**Purpose:** Accept only a conversation an agent of this kind created.

**Algorithm:** Return the session as a `CopilotAgentSession`, or throw `InvalidOperationException`
naming its type.

#### UpdateMapper.OnEvent(SessionEvent sessionEvent)

**Purpose:** Turn one session event into an update, or nothing.

**Algorithm:** Skip every event a sub-agent produced — one whose `AgentId` is set — because it was
never part of this conversation. A root-agent session error completes the stream with an
`InvalidOperationException` reading `Session error: <message>`, so the run fails naming the error
rather than ending as though the turn had succeeded. The message is redacted with
`CopilotSignIn.Redact(signIn, message)` — every token the runtime's sign-in knows, and any text shaped
like a GitHub token — and no cause is attached. Every other root-agent event is mapped as in
*Streaming* below and written to the stream; an event with no mapping writes nothing.

**It never throws.** It runs on the runtime's dispatch thread, serially, where an exception would be
attributed to the SDK rather than to this agent.

### Streaming

The runtime streams only when told to, and `CopilotAgentFactory` turns streaming on for every agent
session, created and resumed. The mapping, for root-agent events only:

| Runtime event | Update content | Role | Notes |
| --- | --- | --- | --- |
| Assistant message delta, non-empty | `TextContent` of the delta | Assistant | carries `MessageId` |
| Final assistant message, streamed or empty | raw-only `AIContent` | Assistant | sets `ResponseId` |
| Final assistant message, not streamed | `TextContent` of the message | Assistant | sets `ResponseId` |
| Tool execution start, with call id and name | `FunctionCallContent` | Assistant | arguments parsed |
| Tool execution complete, with call id | `FunctionResultContent` | Tool | see below |
| Assistant usage | `UsageContent` | Assistant | see below |
| Session error | stream fails | — | `InvalidOperationException`, redacted |

A delta marks its message as streamed, so the final message for it is carried raw and its text is not
repeated. A tool call's arguments are parsed from the JSON object the runtime reported, and are absent
when it reported none.

Every update carries the agent's identifier as `AgentId`, its name as `AuthorName`, the event's
timestamp as `CreatedAt`, and the event itself as `RawRepresentation`; every content item carries the
event as its own `RawRepresentation` too.

**A tool result** is the result's content when the call succeeded, the runtime's error message when it
failed, or `Tool execution failed` when it failed without one. Tool results, like the event objects
carried as `RawRepresentation`, are passed on as the runtime produced them and not redacted: the model
already received that text, so rewriting the recorded copy would make the host's transcript disagree
with the runtime's conversation, and the event object travels with the update anyway. Built-in tools
are suppressed, so a tool error comes from the host's own tool.

**Usage** maps the input, output, cached-input and reasoning token counts onto `UsageDetails`, with the
total as input plus output. The cache-write token count and the request's duration in milliseconds
have no field there and are kept in `AdditionalCounts` as `CacheWriteTokens` and
`DurationMilliseconds`. Fractional figures, such as the request's cost, stay on the raw event the
update carries, because `AdditionalCounts` holds whole numbers.

**A final message is carried as text when nothing streamed it**, so a runtime that sent no deltas for
a message still loses nothing.

### Parity With the Earlier Adapter

Before AgentKit carried its own agent, `CopilotAgentFactory` built one through the
`Microsoft.Agents.AI.GitHub.Copilot` adapter package. The table records how this unit compares with
that adapter's behavior, which defines what a host already relied on.

| Behavior | Earlier adapter | `CopilotAgent` |
| --- | --- | --- |
| Runtime session per run | created or resumed; released after; never deleted | same for a conversation (see below) |
| Conversation type | `GitHubCopilotAgentSession`, public, with `SessionId` | `CopilotAgentSession`, same shape |
| Serialized form | `{ "sessionId", "stateBag" }` | same; earlier output readable |
| Resume by identifier | a typed method on the concrete agent | through deserialization only |
| Text deltas | `TextContent` | same, carrying `MessageId` |
| Final assistant message | raw-only `AIContent` | raw-only if streamed; text if not |
| Tool start and completion | `FunctionCallContent`, `FunctionResultContent` | same, same failure text |
| Usage | `UsageContent` | same; cost on the raw event only |
| Session error | `InvalidOperationException` reading `Session error: …` | same, with tokens redacted |
| Every other event | passed through as empty raw `AIContent` | not emitted |
| Prompt | text of all input messages joined by newline | same |
| `DataContent` | written to an owner-only temporary file | in-memory blob attachment |
| `UriContent`, `HostedFileContent` | not sent | not sent |
| `AgentRunOptions` | ignored | ignored |
| `RunAsync` | the stream gathered into one response | same |
| Default name and description | `GitHub Copilot Agent`, `An AI agent powered by GitHub Copilot` | same |
| Client disposal | only when the agent owned the client, which AgentKit never let it | never |

Unlike the earlier adapter, a run made without a conversation deletes its runtime session when it
ends, because nothing could ever resume it.

Three behaviors are additions rather than parity: runs on one conversation are serialized by the
session's gate, every run's options are built fresh, and events produced by a sub-agent are skipped.

### Intentional Gaps

Each is deliberate and recorded so it is not mistaken for an omission:

- **No typed resume-by-identifier method.** The earlier adapter's concrete agent offered one, but
  `CopilotAgentFactory` has always returned a bare `AIAgent`, so it was never part of AgentKit's
  surface. A host resumes a conversation by identifier through `DeserializeSessionAsync`.
- **No raw passthrough of other events.** The earlier adapter emitted every other session event —
  usage information, permission requests, MCP activity and more — as an empty raw `AIContent`. This
  agent emits only the mapped kinds, so a response carries no empty content items.
- **`UriContent` and `HostedFileContent` are not sent.** Only inline data becomes an attachment, as
  before.
- **`AgentRunOptions` is ignored**, as before: the session's options are decided by the factory, and a
  per-run option could not change them without forking the confinement path.
- **The request's cost is not in `UsageDetails`.** It is fractional, and `AdditionalCounts` holds
  whole numbers; it stays on the raw event each usage update carries.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `messages` | `ArgumentNullException` at the call |
| Session created by another kind of agent | `InvalidOperationException` at the call; nothing is opened |
| Serializing another kind of agent's session | `InvalidOperationException` propagates |
| Serialized form not an object; `sessionId` not string/null; `stateBag` not object/null | `ArgumentException` |
| Opener or resumer returned null | `InvalidOperationException` propagates |
| Creating or resuming the runtime session fails | Propagates; nothing to release; gate released |
| Root-agent session error during the turn | Run fails with `InvalidOperationException`, its text redacted |
| The turn fails | Relayed as a redacted copy of the same kind through the stream; session handle released (see below) |
| Caller cancels, or stops reading early | Turn canceled and awaited; session handle released (see below) |
| Releasing the session handle fails | Swallowed by the channel; the run's own outcome stands |

On both of those paths the session is kept for a conversation and deleted for a run made without
one. A failed or canceled run on a conversation keeps its identifier and its session: the identifier
was recorded before the turn, and a conversation's session is never deleted. The runtime refuses to
resume a session that never took a turn ("Session not found", observed by the live suite's L12d
run), so after a first run that failed or was canceled before its turn reached the runtime, a later
run's resume fails and propagates, as in the table above; the host starts a new conversation.

A turn's own failure is the SDK's exception, whose message, chain and RPC data can quote a token, so
it is relayed through `CopilotSignIn.RedactRelayed`: a new exception of the same kind, with a
redacted message and a redacted copy as its cause, which a host still catches as before; a type
outside the base class library arrives as `InvalidOperationException`, and a cancellation carrying
nothing to redact passes unchanged. The session error, whose text AgentKit composes into its own
exception, is redacted with no cause. A failure creating or resuming the session is the SDK's
exception and is passed on unchanged.

### Dependencies

- **CopilotAgentFactory** — supplies the builder delegates for the create and resume options, so
  every session this agent opens is confined; see *CopilotAgentFactory Unit Design*.
- **CopilotSignIn** — redacts a session error's text with the runtime's sign-in
  (`Redact(plan, text)`), and relays a turn's runtime or SDK failure redacted
  (`RedactRelayed(failure, plan)`); see *CopilotSignIn Unit Design*.
- **CopilotTurnChannel** — supplies `ICopilotTurnChannel`, `CopilotChannelOpener` and
  `CopilotChannelResumer`, and in production the channels that keep a conversation's session on
  release and delete the session of a run made without one; see *CopilotTurnChannel Unit Design*.
- **Microsoft.Agents.AI.Abstractions** — supplies `AIAgent`, `AgentSession` and its state bag,
  `AgentResponse`, `AgentResponseUpdate` and the gathering of a stream into a response; see
  *Microsoft.Agents.AI.Abstractions Design*.
- **Microsoft.Copilot.SDK** — supplies the create and resume options, `MessageOptions`,
  `AttachmentBlob` and the session event types; see *Microsoft.Copilot.SDK Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies `ChatMessage` and the content and usage types
  the updates carry; reached transitively.

### Callers

`CopilotAgentFactory.CreateAgent` constructs the agent, passing the runtime's sign-in when
`CopilotAgentFactory.Create` builds it over a runtime, and `CopilotAgentFactory.Create` returns it
to the application as an `AIAgent`. The application then drives it through the Agent Framework's own
surface — creating, running, serializing and deserializing sessions — and deletes a finished
conversation's persisted state by passing `CopilotAgentSession.SessionId` to
`CopilotRuntime.DeleteSessionAsync`.
