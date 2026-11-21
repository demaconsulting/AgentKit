## CopilotTurnChannel

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotTurnChannel` unit is the seam over the GitHub Copilot SDK's sealed session types: an
interface, the two delegates that open one — by creating a session or by resuming a recorded one —
and the production implementation that wraps a real session and opens every session through the
runtime half of the confinement.

### Purpose

**This seam exists because the SDK's session types cannot be stood in for.** `CopilotSession` is
sealed, its constructor is not public, and none of its methods is virtual; `CopilotClient` is sealed
too. There is therefore no way to exercise a turn without a live Copilot runtime and credentials,
which this repository's continuous integration does not have and deliberately does not require.
Without a seam the whole of the system above the SDK — the seeding, the usage arithmetic, the
refusals, the agent's streaming and resumption, the ownership discipline — would ship with no
automated evidence at all, on the one provider the project can otherwise reach.

**It is one small interface, on purpose.** Everything worth testing lives *above* this line;
everything below it is `CopilotSessionChannel`, which forwards one turn, owns one release and opens
each session through one confining protocol, with the decisions of the last two factored into
methods the tests reach through delegates. That is the smallest surface that makes the rest
reachable, and it is the same reasoning that made `CopilotAgentFactory.BuildSessionConfig` internal.

**Every production session is opened through one confining protocol.** `CopilotAgentFactory` withholds
every channel a session's options can reach; some channels can be withheld only with the live client
and session, so the production channel withholds them, on the one path `Open`, `OpenRetained` and
`Resume` share:

1. **Discover and disable.** The runtime is asked to discover the MCP servers for the session's
   working directory — the SDK documents discovery as covering the user, workspace, plugin and
   builtin sources: the data directory's configuration, the workspace, installed plugins and its
   built-in servers — and every name is added to the session's disabled servers. The SDK
   documents a server named there as not started or authenticated on creation or cold resumption, so
   none of those is started. The session options take exact names only, which is why the names come from
   the runtime.
2. **Open.** The session is created or resumed from the updated options.
3. **Seal.** The session's installed plugins — which can contribute MCP servers, hooks, skills and
   agents — are replaced with none, the call Empty mode itself makes after every create and resume,
   made here so it does not depend on the client's mode. The session's effective MCP configuration,
   with each server's provenance as an `McpServerSource` — user, workspace, plugin, builtin, managed
   or account — including the managed and account sources discovery is not documented to cover, is
   listed; every
   server still enabled is disabled for this session only, never in the user's persistent
   configuration; and the configuration is listed again.
4. **Refuse rather than continue.** A server the runtime keeps enabled refuses the session with an
   `InvalidOperationException` naming it. A session that cannot be sealed for any reason is
   abandoned before the failure propagates: a created session, which nothing has recorded, is
   released and deleted; a resumed session is only released, because refusing to continue a
   conversation must not destroy it.

**Prevention and containment.** Servers the runtime's MCP discovery returns are named in the
session's disabled servers before the session exists, so the runtime does not start or authenticate
them: measured against the live runtime, a server in the runtime's data directory is reported by
discovery and never executed through this protocol, while the same server is executed when the
session is created without it. The built-in GitHub server is named by the factory, not by discovery,
and is never started either. A server from a source the SDK cannot enumerate before creation, such as
a managed catalog or a signed-in account, may start, and authenticate, while the session is created.
Step 3 disables it before any turn runs, and the session is refused if one stays enabled: that
contains such a server, it does not prevent it. Step 3 relies on the session's configuration
listing, which is not complete — measured against the live runtime, it does not report the built-in
GitHub server even while that server is connected — so the seal is defense in depth rather than
proof that no server runs. On the runtime AgentKit owns the user, workspace and plugin sources hold
nothing to begin with; *Runtime Ownership* in *AgentKitAgentsCopilot System Design* states the
guarantee per source.

**What it deliberately does not do.** It exposes no event subscription and no configuration. A
session's event handler and every other setting are reached through the session options at creation
or resumption, where they are decided once and can be asserted on — including the agent's streaming,
which arrives through the handler like every other event. A wider seam would be a second
configuration path, which is the drift this package exists to prevent.

**Release means one of two things, chosen when the channel is opened.** A session the engine or the
summarizer opened is finished with when it is released — a rotated conversation, a completed
consolidation — so it is released and then **deleted**. So is the session of an agent's run made
without a conversation, which nothing could resume. An agent's *conversation* continues on the next
run, possibly on another agent instance given the serialized session, so its session is released and
**kept**: the runtime preserves its state under the runtime's data directory until the host deletes
it through `CopilotRuntime.DeleteSessionAsync`.

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

- **`_client`** (`CopilotClient`) — The runtime's client the session was created on. Held only to
  delete the session on release. Invariant: never disposed here.
- **`_session`** (`CopilotSession`) — The session handle this channel owns and releases.
- **`_deleteOnRelease`** (`bool`) — Whether release also asks the runtime to delete the session's
  persisted state. Invariant: fixed by the factory method that opened the channel.

### Key Methods

#### Open(CopilotClient client)

**Purpose:** Produce the opener the session engine, the summarizer and an agent's run made without a
conversation create their sessions through.

**Algorithm:** Return a delegate that creates a session from the options on `client` and wraps it in
a channel that **deletes** the session on release. Produced once per factory rather than per session,
so the client reference is captured in exactly one place and every session a rotation creates
demonstrably runs on the same client the runtime started.

**Preconditions:** `client` is not null. Production passes the client of a `CopilotRuntime`, which is
already started; the opt-in live suite also passes hostile CodingAgent clients through this internal
seam. A client not yet started, or whose connection was lost, is started on the first creation,
through `CopilotRuntime.StartClientAsync`.

**Postconditions:** An opener over that client; every session it creates went through
`CreateConfinedSessionAsync`.

#### OpenRetained(CopilotClient client)

**Purpose:** Produce the opener an agent creates a conversation's first session through.

**Algorithm:** As `Open`, but the channel **keeps** the session on release, so the conversation can be
resumed by `Resume` until the host deletes it.

**Preconditions:** `client` is not null. A client not yet started is started on the first creation.

**Postconditions:** An opener over that client; every session it creates went through
`CreateConfinedSessionAsync`.

#### Resume(CopilotClient client)

**Purpose:** Produce the resumer an agent reopens a recorded session through on every later run.

**Algorithm:** Return a delegate that resumes the session with the given identifier and options on
`client` through `ResumeConfinedSessionAsync` and wraps it in a channel that **keeps** the session on
release.

**Preconditions:** `client` is not null; the runtime still holds the session. A client not yet started is
started on the first resumption.

**Postconditions:** A resumer over that client; a session it could not seal is released and kept,
never deleted.

#### CreateConfinedSessionAsync(CopilotClient client, CreateSessionOptions config, CancellationToken cancellationToken)

**Purpose:** Create a session through the confining protocol: the one path every production opener
shares, and the path the opt-in live suite uses to inspect a session opened exactly as production
opens one.

**Algorithm:** `OpenConfinedAsync` with discovery on `client` for the options' working directory, the
client's session creation, the live seal, and, as the abandon step, `ReleaseAsync` with the handle's
release and the client's deletion of the session.

**Preconditions:** `client` and `config` are not null.

**Postconditions:** A sealed session the caller owns, or an exception with no session left behind.

#### ResumeConfinedSessionAsync(CopilotClient client, string sessionId, ResumeSessionOptions config, CancellationToken cancellationToken)

**Purpose:** Resume a session through the confining protocol.

**Algorithm:** As `CreateConfinedSessionAsync`, resuming rather than creating, with `ReleaseAsync` of
the handle alone as the abandon step.

**Preconditions:** `client`, `sessionId` and `config` are not null.

**Postconditions:** A sealed session the caller owns, or an exception with the conversation kept.

#### OpenConfinedAsync&lt;TSession&gt;(SessionOptionsBase config, discover, open, seal, abandon, CancellationToken cancellationToken)

**Purpose:** The opening protocol's order and failure handling, factored out with the runtime's steps
as delegates so it can be tested without a live session.

**Algorithm:** Await `discover` and pass its names to `DisableMcpServers` on `config`; await `open`;
await `seal` on the opened session. If `seal` throws, await `abandon` on the session and rethrow.
Return the session.

**Preconditions:** All arguments are not null; `abandon` does not throw.

**Postconditions:** A sealed session; or, when discovery failed, nothing opened; or, when the seal
failed, the session abandoned and the seal's exception propagated.

#### SealAsync(clearInstalledPlugins, listConfigured, disable, CancellationToken cancellationToken)

**Purpose:** The sealing policy, with the session's RPC calls supplied as delegates.

**Algorithm:** Await `clearInstalledPlugins`. List the configured servers and await `disable` for each
one reported enabled. List again; if any server is still enabled, throw an
`InvalidOperationException` naming each one and the remedy.

**Preconditions:** All arguments are not null.

**Postconditions:** No configured MCP server is reported enabled, or an exception naming those that
are.

#### SealSessionAsync(CopilotSession session, CancellationToken cancellationToken)

**Purpose:** Seal a live session through its RPC surface.

**Algorithm:** Call `SealAsync` with the session's options patch carrying
`CreateNoInstalledPluginsPatch()`, its MCP configuration listing, and its session-scoped disable of
one named server.

**Why it is internal.** The opt-in live suite drives this exact seal over a session opened without
discovery (L12c), so the seal's own session-scoped disable is shown on the real runtime rather than
only the create-time disabled list.

**Preconditions:** `session` is not null.

**Postconditions:** As `SealAsync`.

#### DisableMcpServers(SessionOptionsBase config, IEnumerable&lt;string&gt; names)

**Purpose:** Add discovered server names to a session's disabled servers.

**Algorithm:** Replace `config.Mcp.DisabledServers` with its existing entries followed by every
non-blank name, without duplicates under an ordinal comparison, so the factory's entry for the
built-in GitHub server is kept once.

**Preconditions:** `config` and `names` are not null.

**Postconditions:** Every non-blank name is disabled on `config`.

#### CreateNoInstalledPluginsPatch()

**Purpose:** Build the session-options patch that replaces a session's installed plugins with none.

**Algorithm:** Return a plugin patch whose installed list is empty — not null, because an unset list
is omitted from the patch, which the runtime reads as no change.

**Preconditions:** None.

**Postconditions:** A patch carrying an empty installed-plugin list.

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
| Starting the client fails while opening | Explained by `CopilotRuntime.StartClientAsync`; nothing opened |
| MCP discovery fails | Propagates from the opener or resumer; nothing opened |
| Creating or resuming a session fails | Propagates from the opener or resumer; nothing to release |
| The seal fails | Session abandoned (see below); the failure propagates |
| An MCP server stays enabled after it was disabled | `InvalidOperationException` naming it; abandoned |
| The runtime refuses a disable, as an owned MCP installation may | `RemoteRpcException` propagates; abandoned |
| Session release fails in transit | Swallowed; the handle is abandoned either way |
| Session deletion fails on release | Swallowed; the handle is already released |

A seal fails when the runtime refuses the plugins patch, the listing or a disable, or when a server
stays enabled. The session is then abandoned before the failure propagates: a created session is
released and deleted, a resumed one only released, so a conversation is never destroyed. The SDK
documents its session-scoped disable as requiring an installation id for an owned MCP installation,
and its listing exposes no such id, so such a server can only be disabled by name; if the runtime
refuses that, or keeps the server enabled, the session is refused rather than used.

Both swallowed failures are discarded for the reason `CompactingAgentSession` discards a failed
release during a rotation: by that point the operation the caller asked for has already succeeded,
or has already failed with an error the caller must see, and failing the release would turn a
teardown problem into a conversation-ending one or hide the real error. Nothing else in this package
discards an error — everywhere the system cannot know something it needs, it refuses and says so.

### Dependencies

- **Microsoft.Copilot.SDK** — supplies `CopilotClient`, `CopilotSession`, `CreateSessionOptions`,
  `ResumeSessionOptions`, `MessageOptions` and the assistant-message event, and the RPC surfaces the
  opening protocol uses: the client's MCP discovery, the session's options patch with its plugin
  options, and the session's MCP configuration listing and disable; see
  *Microsoft.Copilot.SDK Design*.
- **CopilotRuntime** — its explained start runs before discovery, so a runtime that cannot start is
  explained on this path too; see *CopilotRuntime Unit Design*.

### Callers

`CopilotProviderSessionFactory` and `CopilotSummarizer` each hold an opener from `Open` and open one
session per rotation or per consolidation; `CopilotProviderSession` holds a channel and sends turns
through it. `CopilotAgentFactory.Create` hands `CopilotAgent` an opener from `OpenRetained` and a
resumer from `Resume` for its conversations, and an opener from `Open` for a run made without one,
and the agent opens or resumes one channel per run. Nothing outside this package sees any of it.
