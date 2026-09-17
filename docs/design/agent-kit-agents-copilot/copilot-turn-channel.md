## CopilotTurnChannel

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotTurnChannel` unit is the seam over the GitHub Copilot SDK's sealed session types: an
interface, the delegate that opens one, and the production implementation that wraps a real session.

### Purpose

**This seam exists because the SDK's session types cannot be stood in for.** `CopilotSession` is
sealed, its constructor is not public, and none of its methods is virtual; `CopilotClient` is sealed
too. There is therefore no way to exercise a turn without a live Copilot runtime and credentials,
which this repository's continuous integration does not have and deliberately does not require.
Without a seam the whole adapter — the seeding, the usage arithmetic, the refusals, the ownership
discipline — would ship with no automated evidence at all, on the one provider the project can
otherwise reach.

**It is one interface with one method, on purpose.** Everything worth testing lives *above* this
line; everything below it is `CopilotSessionChannel`, a pass-through that forwards two calls and
owns one disposal. That is the smallest surface that makes the rest reachable, and it is the same
reasoning that made `CopilotAgentFactory.BuildSessionConfig` internal.

**What it deliberately does not do.** It exposes no streaming, no event subscription, no session
identifier and no resumption. The provider session needs a turn and a release; everything else the
runtime offers is reached through the session configuration at creation time, where it is decided
once and can be asserted on. A wider seam would be a second configuration path, which is the drift
this package exists to prevent.

The unit is internal throughout: it is an implementation detail of this package's session support,
and an application has no use for it.

### Data Model

**`ICopilotTurnChannel`** (interface) — One live Copilot session reduced to two operations. Extends
`IAsyncDisposable`, because releasing the session is one of the two. Implementations are not safe for
concurrent use: one channel serves one conversation.

**`CopilotChannelOpener`** (delegate) — Opens one session from a configuration and presents it as a
channel. The seam's other half: a production opener creates a real session on a real client; a test's
opener returns a scripted channel and keeps the configuration it was handed, which is how the seeding
and the confinement are asserted without a runtime.

**`CopilotSessionChannel`** (sealed class) — The production implementation.

- **`_client`** (`CopilotClient`) — The client the session was created on. Held only to delete the
  session on release. Invariant: never disposed here.
- **`_session`** (`CopilotSession`) — The session this channel owns and releases.

### Key Methods

#### Open(CopilotClient client)

**Purpose:** Produce the opener every session of one conversation is created through.

**Algorithm:** Return a delegate that creates a session from a configuration and wraps it. Produced
once per factory rather than per session, so the client reference is captured in exactly one place
and every session a rotation creates demonstrably runs on the same client the host started.

**Preconditions:** `client` is not null and has been started.

**Postconditions:** An opener over that client.

#### SendAndWaitAsync(string prompt, CancellationToken cancellationToken)

**Purpose:** Send one prompt and wait for the session to finish answering it.

**Algorithm:** Forward to the SDK's wait, passing an infinite timeout and the caller's token.

**The cancellation token is the turn's only deadline, deliberately.** The SDK applies a
sixty-second timeout when told none, which a tool-using research turn exceeds routinely — and the
resulting timeout would read as a model or tool fault rather than as a deadline nobody chose.
`Timeout.InfiniteTimeSpan` is passed instead, which the SDK carries into its linked cancellation
source's delayed cancel, where it means "never"; this was checked against the SDK's own
implementation rather than assumed. The deadline is then the caller's token, which
`IProviderSession.SendAsync` already takes and `CompactingAgentSession` already flows — so the
application governs it, rather than this library inventing a number or offering a configuration
knob nobody asked for.

Everything the turn produced along the way — tool calls, tool results, usage readings — is delivered
to the session's event handler rather than returned here, so a caller observes a turn through
`CopilotSessionObserver` and takes only the final answer from the return value.

**Preconditions:** `prompt` is not null.

**Postconditions:** The final assistant message, or null when the session went idle without
producing one.

#### DisposeAsync()

**Purpose:** Release the session without releasing the client.

**Algorithm:** Dispose the session, then ask the runtime to delete it. Both calls are guarded:
neither may throw out of disposal.

**Why both calls.** Disposal alone is documented to *preserve* a session's state on disk so the
conversation can be resumed later, which is the wrong outcome here: a rotated session is finished
with by definition, and a long conversation would otherwise leave one preserved session behind per
rotation. Deleting is the runtime's only irreversible removal, so it is asked for.

**Why neither may throw.** Disposal runs on the failure paths of a rotation and on the caller's own
release, and neither is a place a failure can be acted on. The **release** is the more important of
the two guards: it is not local teardown but a detach over the runtime's transport, so a connection
already gone throws — which at shutdown is the ordinary case rather than an exotic one. Left
unguarded it would replace a consolidation that had already succeeded with a detach failure, or mask
the very exception a caller's catch block was preserving. The delete is best-effort for the same
reason and costs less: the session is already released by then, so the worst outcome is recoverable
disk state the runtime expires on its own. Dropping the delete entirely would be a one-line change
if a maintainer judges the extra request not worth it.

**Preconditions:** None.

**Postconditions:** The session is released; the client is untouched; nothing is thrown.

### Error Handling

| Condition                          | Handling                                          |
|------------------------------------|---------------------------------------------------|
| Turn canceled by the caller        | `OperationCanceledException` propagates           |
| Runtime failure during a turn      | Propagates to the provider session                |
| Session release fails in transit   | Swallowed; the session is abandoned either way    |
| Session deletion fails on release  | Swallowed; the session is already released        |

Both swallowed failures are discarded for the reason `CompactingAgentSession` discards a failed
release during a rotation: by that point the operation the caller asked for has already succeeded,
and failing it would turn a teardown problem into a conversation-ending one. Nothing else in this
package discards an error — everywhere the adapter cannot know something it needs, it refuses and
says so.

### Dependencies

- **Microsoft.Agents.AI.GitHub.Copilot** — supplies `CopilotClient`, `CopilotSession`,
  `SessionConfig` and the assistant-message event; see *Microsoft.Agents.AI.GitHub.Copilot Design*.

### Callers

`CopilotProviderSessionFactory` and `CopilotSummarizer` each hold an opener and open one session per
rotation or per consolidation; `CopilotProviderSession` holds a channel and sends turns through it.
Nothing outside this package sees any of it.
