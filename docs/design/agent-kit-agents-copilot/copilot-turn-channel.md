## CopilotTurnChannel

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotTurnChannel` unit is the seam over the GitHub Copilot SDK's sealed session types: an
interface, the two delegates that open one — by creating a session or by resuming a recorded one —
and the production implementation that wraps a real session.

### Purpose

**This seam exists because the SDK's session types cannot be stood in for.** `CopilotSession` is
sealed, its constructor is not public, and none of its methods is virtual; `CopilotClient` is sealed
too. There is therefore no way to exercise a turn without a live Copilot runtime and credentials,
which this repository's continuous integration does not have and deliberately does not require.
Without a seam the whole of the system above the SDK — the seeding, the usage arithmetic, the
refusals, the agent's streaming and resumption, the ownership discipline — would ship with no
automated evidence at all, on the one provider the project can otherwise reach.

**It is one small interface, on purpose.** Everything worth testing lives *above* this line;
everything below it is `CopilotSessionChannel`, a pass-through that forwards one call and owns one
release. That is the smallest surface that makes the rest reachable, and it is the same reasoning that
made `CopilotAgentFactory.BuildSessionConfig` internal.

**What it deliberately does not do.** It exposes no event subscription and no configuration. A
session's event handler and every other setting are reached through the session options at creation
or resumption, where they are decided once and can be asserted on — including the agent's streaming,
which arrives through the handler like every other event. A wider seam would be a second
configuration path, which is the drift this package exists to prevent.

**Release means one of two things, chosen when the channel is opened.** A session the engine or the
summarizer opened is finished with when it is released — a rotated conversation, a completed
consolidation — so it is released and then **deleted**. An agent's session is a conversation that
continues on the next run, possibly on another agent instance given the serialized session, so it is
released and **kept**: the runtime preserves its state under the client's data directory until the
host deletes it.

The unit is internal throughout: it is an implementation detail of this package, and an application
has no use for it.

### Data Model

**`ICopilotTurnChannel`** (interface) — One live Copilot session reduced to its identity, a turn and
a release. Extends `IAsyncDisposable`, because releasing the session is one of the three.
Implementations are not safe for concurrent use: one channel serves one conversation.

- **`SessionId`** (`string`) — The runtime's identifier for the session the channel carries. The
  agent records it after opening a conversation's first session, so a later run — on the same agent
  instance or on a new one given the serialized session — can resume the same runtime session.

**`CopilotChannelOpener`** (delegate) — Creates one session from `CreateSessionOptions` and presents
it as a channel. A production opener creates a real session on a real client; a test's opener returns
a scripted channel and keeps the options it was handed, which is how the seeding and the confinement
are asserted without a runtime.

**`CopilotChannelResumer`** (delegate) — Resumes one existing session by its identifier with
`ResumeSessionOptions` and presents it as a channel. A resumed session is confined by the options
handed here, exactly as a created one is by its own, so a test's resumer keeps the options to assert
that the confinement was re-applied.

**`CopilotSessionChannel`** (sealed class) — The production implementation.

- **`_client`** (`CopilotClient`) — The client the session was created on. Held only to delete the
  session on release. Invariant: never disposed here.
- **`_session`** (`CopilotSession`) — The session handle this channel owns and releases.
- **`_deleteOnRelease`** (`bool`) — Whether release also asks the runtime to delete the session's
  persisted state. Invariant: fixed by the factory method that opened the channel.

### Key Methods

#### Open(CopilotClient client)

**Purpose:** Produce the opener the session engine and the summarizer create their sessions through.

**Algorithm:** Return a delegate that creates a session from the options on `client` and wraps it in
a channel that **deletes** the session on release. Produced once per factory rather than per session,
so the client reference is captured in exactly one place and every session a rotation creates
demonstrably runs on the same client the host started.

**Preconditions:** `client` is not null and has been started.

**Postconditions:** An opener over that client.

#### OpenRetained(CopilotClient client)

**Purpose:** Produce the opener an agent creates a conversation's first session through.

**Algorithm:** As `Open`, but the channel **keeps** the session on release, so the conversation can be
resumed by `Resume` until the host deletes it.

**Preconditions:** `client` is not null and has been started.

**Postconditions:** An opener over that client.

#### Resume(CopilotClient client)

**Purpose:** Produce the resumer an agent reopens a recorded session through on every later run.

**Algorithm:** Return a delegate that resumes the session with the given identifier and options on
`client` and wraps it in a channel that **keeps** the session on release.

**Preconditions:** `client` is not null and has been started; the runtime still holds the session.

**Postconditions:** A resumer over that client.

#### SendAndWaitAsync(MessageOptions message, CancellationToken cancellationToken)

**Purpose:** Send one message — its prompt and any attachments — and wait for the session to finish
answering it.

**Algorithm:** Forward to the SDK's wait, passing an infinite timeout and the caller's token.

**The cancellation token is the turn's only deadline, deliberately.** The SDK applies a sixty-second
timeout when told none, which a tool-using research turn exceeds routinely — and the resulting timeout
would read as a model or tool fault rather than as a deadline nobody chose. `Timeout.InfiniteTimeSpan`
is passed instead, which the SDK carries into its linked cancellation source's delayed cancel, where
it means "never"; this was checked against the SDK's own implementation rather than assumed. The
deadline is then the caller's token, which `IProviderSession.SendAsync` and the agent's run methods
already take — so the application governs it, rather than this library inventing a number or offering
a configuration knob nobody asked for.

Everything the turn produced along the way — tool calls, tool results, usage readings, streamed text —
is delivered to the session's event handler rather than returned here, so a caller observes a turn
through that handler and takes only the final answer from the return value.

**Preconditions:** `message` is not null.

**Postconditions:** The final assistant message, or null when the session went idle without
producing one.

#### DisposeAsync()

**Purpose:** Release the session handle without releasing the client, and delete the session when the
channel was opened by `Open`.

**Algorithm:** Call `ReleaseAsync` with the session handle's own release and, when `_deleteOnRelease`
is set, the client's deletion of the session.

**Preconditions:** None.

**Postconditions:** The session handle is released; the session is deleted or kept as the channel was
opened to do; the client is untouched; nothing is thrown.

#### ReleaseAsync(Func&lt;ValueTask&gt; detach, Func&lt;Task&gt;? delete)

**Purpose:** The release policy, factored out so it can be tested without a live session.

**Algorithm:** Run `detach`, swallowing any failure. Then, when `delete` is supplied, run it,
swallowing any failure.

**Why the deletion is separate.** Releasing a handle is documented to *preserve* a session's state on
disk so it can be resumed later. That is exactly right for an agent's conversation and exactly wrong
for a rotated or consolidation session, which would otherwise leave one preserved session behind per
rotation. So deletion is an optional second step rather than part of release.

**Why neither step may throw.** Release runs on the failure paths of a rotation, in a summarizer's
`finally`, after every agent run, inside a factory's failure path and on the caller's own release, and
none is a place a failure can be acted on. Releasing a handle is not local teardown but a detach over
the runtime's transport, so a connection already gone throws — which at shutdown is the ordinary case
rather than an exotic one. Left unguarded it would replace a consolidation that had already succeeded
with a detach failure, or mask the very exception a caller's catch block was preserving. The deletion
is best-effort for the same reason: the handle is already released by then, so the worst outcome is
recoverable disk state.

**Preconditions:** `detach` is not null.

**Postconditions:** Both steps have been attempted; nothing is thrown.

### Error Handling

| Condition | Handling |
| --- | --- |
| Turn canceled by the caller | `OperationCanceledException` propagates |
| Runtime failure during a turn | Propagates to the provider session, summarizer or agent |
| Creating or resuming a session fails | Propagates from the opener or resumer; nothing to release |
| Session release fails in transit | Swallowed; the handle is abandoned either way |
| Session deletion fails on release | Swallowed; the handle is already released |

Both swallowed failures are discarded for the reason `CompactingAgentSession` discards a failed
release during a rotation: by that point the operation the caller asked for has already succeeded,
or has already failed with an error the caller must see, and failing the release would turn a
teardown problem into a conversation-ending one or hide the real error. Nothing else in this package
discards an error — everywhere the system cannot know something it needs, it refuses and says so.

### Dependencies

- **Microsoft.Copilot.SDK** — supplies `CopilotClient`, `CopilotSession`, `CreateSessionOptions`,
  `ResumeSessionOptions`, `MessageOptions` and the assistant-message event; see
  *Microsoft.Copilot.SDK Design*.

### Callers

`CopilotProviderSessionFactory` and `CopilotSummarizer` each hold an opener from `Open` and open one
session per rotation or per consolidation; `CopilotProviderSession` holds a channel and sends turns
through it. `CopilotAgentFactory.Create` hands `CopilotAgent` an opener from `OpenRetained` and a
resumer from `Resume`, and the agent opens or resumes one channel per run. Nothing outside this
package sees any of it.
