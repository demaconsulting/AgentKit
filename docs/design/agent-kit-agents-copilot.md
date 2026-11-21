# AgentKitAgentsCopilot System Design

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The AgentKitAgentsCopilot system starts a GitHub Copilot runtime of its own and gives it an agent of
AgentKit's own —
a Microsoft Agent Framework `AIAgent` confined to a supplied tool list, with the built-in tools the
Copilot runtime would otherwise inject suppressed — and carries an AgentKit Core session, with its
tiered context compaction, over the same runtime.

## Purpose

AgentKitAgentsCopilot exists because Copilot arrives as a complete agent runtime carrying its own
tools — shell, fetch, file reading and writing, and more. Handing it an application's own tools is
not enough: unless the built-ins are suppressed, a document assistant silently retains command
execution. The essential job of this system is that suppression, and it performs it by deriving the
session's available-tools allow-list from the **same** collection it publishes as the session's
tools, so the published tool set and the allow-list cannot drift apart. That derivation is the
safety-critical behavior of the whole increment.

The system also wires a permission handler that is safe by default — approving exactly the supplied
tools and rejecting every other request — while letting a host supply its own; see *Permission
Handling Is Default-Deny*. And it starts the runtime itself — `CopilotRuntime`, in Empty mode, as a
child process with the host's `COPILOT_`/`GITHUB_COPILOT_` variables withheld apart from an
allow-listed credential, network and telemetry-off set, on a data directory it owns — because what reaches a session
depends on the client as much as on the session; see
*Runtime Ownership*. The host starts and disposes that runtime; nothing else in this system takes
ownership of it.

**The agent is AgentKit's own.** The Copilot SDK ships no Agent Framework agent, and the adapter
package that once supplied one does not support the current SDK. So the system carries its own
`AIAgent` over Copilot sessions — `CopilotAgent` — and every session that agent opens, whether
created or resumed, is configured by the same confinement path as every other session in the
package. The agent adds lifecycle, streaming and serialization around that confinement and changes
none of it. Owning the agent also removes a seam the confinement used to cross: nothing third-party
stands between the configuration AgentKit builds and the session the runtime creates from it.

**The system carries a second job: an AgentKit provider session.** Core's compaction engine is what
lets a long-running agent behave the same way on every provider, and it reaches a provider through
an adapter. Until this increment there was one adapter, for the stateless `IChatClient` family — and
the Copilot SDK exposes no `IChatClient` anywhere, so the engine could not run on Copilot at all.
That mattered more than the count suggests: Copilot is the provider this project's continuous
integration can reach, so the engine's behavior against a runnable provider had never been
exercised. `CopilotProviderSession`, its factory and `CopilotSummarizer` close that gap natively.

The system implements no agent runtime and no compaction engine of its own: the Copilot runtime runs
the conversation, and Core runs the compaction. It presents Copilot sessions through the Agent
Framework's `AIAgent` abstraction and through Core's session contracts. It shares no code with, and
takes no reference on, the ChatClient adapter.

## Seeding History Onto the First Message

A rotation produces a seed carrying instructions, tools and a rewritten history, and the adapter has
to get that history into a fresh Copilot session. **Copilot's session options have no history or
messages field of any kind** — every property of the create options and their shared base was
enumerated against the shipped assembly. So the history is rendered as a labeled record and carried
on the session's **first user message**, prepended to the message the engine was already about to
send. The session's system message carries the application's instructions and nothing else, byte for
byte as the plain agent path configures them.

**Why the first user message, and not the system message — the engine's accounting.** The engine
reasons about the window as overhead — the system message and the tool declarations — against the
conversation. A record placed in the system message is charged as overhead, so overhead grows at
every rotation while the conversation appears small, and the engine's model of its own window drifts
from the runtime's; the figures a live run produced are in *AgentKitAgentsCopilot System Verification
Design*. Compacted content belongs in the region that gets compacted. Secondarily, tool output in the
instructions channel reads with more authority than it has earned. Nothing rests on how the record is
delimited: the material was already in the model's context before it was consolidated, so seeding it
back grants nothing new, and this library's guarantee is the tool policy rather than prompt hygiene.

**It costs no extra request.** The record rides the message the engine was already sending, so a
rotation is still exactly one call, and the runtime then holds the result for the rest of the session
as it holds any other turn.

**A rotated session is briefed, not replayed.** The record is a handover document, opened and closed
by two fixed lines that mark where it ends and the current message begins.

The rendering is still a block of labeled text lines rather than a sequence of role-bearing messages,
and that remains decisive. The defect that shipped to review on the stateless path was a seeded tool
result rendered as a text-only tool-role message, a shape the OpenAI wire mapping discards without an
error — and fifty-five green tests and a live sample run never noticed. Here that whole class of
defect is unrepresentable: a seeded tool result is a labeled line inside one message, and no
provider's role mapping can drop part of a message without dropping the message.

Conversational attachments, session resumption, the session RPC's message queue, the runtime's own
prompt-section overrides, and a priming message sent on its own were each considered and rejected,
with the evidence against each recorded in *CopilotProviderSessionFactory Unit Design*. Session
resumption is rejected *for seeding* only: the agent path resumes a session to continue the same
conversation, which is exactly what resumption is for, while a rotation must discard the history a
resume would bring back.

Two consequences are stated rather than hidden. The record now lives in Copilot's **conversation**,
where the runtime's own truncation could in principle drop it — which was the decisive objection to
sending it as a separate priming message. It is admitted here because this design does not rely on
the runtime leaving the conversation alone: every session the engine drives is created with the
runtime's compaction threshold raised clear of the engine's rotation point, and a rewrite that
happens anyway is announced, detected and refused rather than silently absorbed. And the record
arrives as one message rather than as the turns it describes, so the model may weight it differently
from turns it lived through; that is recorded as unverified rather than asserted.

## A Session the Engine Cannot Account For Is Finished

Two things can leave the runtime holding a conversation the engine's transcript does not describe:
the runtime rewriting history itself, and a turn the runtime processed that the session could not
record. The consequence is identical, so there is one rule and one latch.

Once the prompt has been handed over, this session can no longer know what the runtime holds. A
failure of the send itself, or after it — no usage reported, no assistant message, a rewrite
announced — may leave the runtime a turn ahead of the transcript; and on a first turn the seeded
record has been consumed either way and will not be sent again. Neither is recoverable by retrying
on that session, and retrying an `InvalidOperationException` is the ordinary host response, which
would re-run the application's tools, with their real side effects, against a conversation the
engine may no longer describe.

So the session latches unusable, naming the original failure, and refuses every later turn **before**
sending it. The engine's answer to a session it cannot use is the one it already has: seed a
replacement from its own transcript, which is the state known to be good. Detecting the first
divergence is unavoidably after the fact; letting a second one happen is not.

## The Runtime's Own Compaction Is Held Clear of Rotation

Copilot compacts its own sessions: its infinite-session configuration defaults to enabled, with
background compaction at 0.80 of the window and buffer exhaustion at 0.95. AgentKit rotates at 0.70
of the window, against the same occupancy signal. This is therefore not a theoretical conflict.

Left at those defaults, both compactors would act on one conversation. The runtime would rewrite
history underneath a session whose transcript the engine believes it owns, and the occupancy the
engine reads afterwards would move for reasons it cannot see — so the engine would seed a replacement
from a history the provider has already discarded, and the two accounts of the conversation would
diverge silently. Ten percentage points is not a margin: a single turn returning a large tool result
can carry a session from below the engine's rotation point to past the runtime's compaction threshold
in one step.

Every session the engine drives is therefore created with the runtime's background-compaction
threshold raised to 0.95, a quarter of the window clear of the engine's rotation point.
**A future maintainer must not lower it back toward the engine's own threshold.**

**Asking the runtime to stop does not work, and nothing here depends on it.** The session
options also carry the infinite-session enablement flag set false, which states the intent and
costs nothing, but the runtime ignores it: the flag only suppresses the session's workspace.
Measured against the live runtime, a session created with the flag false compacted as soon as its
threshold was crossed, exactly as a session created with the flag true did; a session whose threshold
was not crossed did not compact whatever the flag said. The threshold is the honored setting, so the
threshold is the one relied on. The measurement itself is recorded in *AgentKitAgentsCopilot System
Verification Design*.

**The threshold is deliberately not raised to 1.0.** The runtime's last-resort behavior is worth
keeping for the case where something extraordinary happens, so a rewrite remains possible rather than
impossible. The session observer watches for every event by which the runtime reports reshaping a
conversation itself — a compaction starting, summarizing or completing, a truncation, a cleared
context window, and a rewind to a snapshot — and the provider session refuses the next turn if one
arrives — before a turn is sent as well as after one returns — naming the cause. That converts a
silent divergence into a diagnosable failure, which is the convention this library already follows
wherever it cannot know something it needs. It is also what makes it safe to carry a rotation's
record on the conversation channel; see *A Session the Engine Cannot Account For Is Finished* above.

**The agent path is deliberately asymmetric.** A plain Copilot agent has no AgentKit compactor behind
it, so changing the runtime's compaction there would remove protection rather than prevent a
conflict. The agent path leaves the setting untouched, and a test pins each side of the asymmetry so
neither can be tidied into the other by accident.

## Occupancy Comes From the Runtime

Core's session engine asks one question — how full, out of how much — and believes the answer. The
stateless adapter has to be *told* its window, because an `IChatClient` publishes none. Copilot is
the opposite: it reports the tokens it currently holds, the limit it will hold them to, and how much
of the total the conversation accounts for, all in one event and all counted with the same
tokenizer.

So this system takes **no** window from the application. Asking for a figure the provider already
knows would create a second source of truth for one fact, which is exactly what the engine's
single-reading design retired. The conversation's share is passed through rather than inferred,
which keeps every threshold comparison downstream in Copilot's own tokens. Nothing is estimated, and
a turn the runtime answered without reporting its usage is refused rather than guessed around.

Before the runtime has reported anything — which includes the moment the engine reads a freshly
adopted session — the session reports nothing occupied out of a placeholder window that cannot
trigger a rotation and cannot be mistaken for a measurement.

## Confinement Is Applied to Every Session, on One Path

Every Copilot session this system opens — an agent's first run, every later run that resumes it, a
compacting conversation's session at every rotation, and every consolidation — is configured by one
private block in `CopilotAgentFactory`. That block targets the options base a created and a resumed
session share, which is where every confinement-relevant option group lives, so a resumed
conversation is confined afresh rather than trusted to remember. A resume built without the
confinement would hand an existing conversation the runtime's defaults — every built-in tool, and
permission requests no handler answers.

The block does four things, each set explicitly on every session so the confinement does not rest on
the runtime's mode alone:

- **Derives the allow-list.** The tool definitions and the available-tools allow-list are assigned
  from one collection in two adjacent statements. Each allow-list entry is **source-qualified** as
  `custom:<name>`: measured against the live runtime, and re-verified by the opt-in live suite, a
  bare-name allow-list also admits a built-in tool whose name matches, so the qualifier is what makes
  the list name only the tools this package registered. The runtime classifies a tool by where it was
  registered rather than by parsing its name, so a colliding name cannot spoof the qualifier.
- **Withholds every other channel of injected capability.** Skills and the built-in skills, discovered
  custom instructions and on-demand instruction discovery, repository hooks, host git operations and
  commit co-author trailers,
  the cross-session store and memory, the built-in sub-agents and discovered agent definitions (with
  the delegation tool outside the allow-list),
  extensions, configuration discovery, tool search, schedule management, image generation,
  experimental features, embedding retrieval, and the runtime's diversion of large tool results to a
  file are all switched off. The runtime's host environment context — the working directory, the
  operating system, the git root, a directory listing and the available tools — is removed from the
  system prompt through the system message's section overrides, the same shape Empty mode sends. The
  runtime's built-in GitHub MCP server is disabled by name: measured against the live runtime, it is
  started and authenticated with the user's GitHub identity on a session's first turn even in Empty
  mode, and while the allow-list already keeps its tools out of reach, a server holding the user's
  identity is disabled rather than merely filtered. Naming it is the only control that reaches it:
  the session's MCP listing does not report it even while it is connected, so the seal below cannot
  see it. The full list, with the reason for each,
  is in *CopilotAgentFactory Unit Design*.
- **Pins the permission mode to manual.** In the runtime's allow-all mode tool, path and URL requests
  are approved without asking the handler, and in its assisted mode a client may approve them
  itself; either would bypass the handler without anything in this package changing. Pinning the
  manual mode keeps the handler the only authority, whatever mode ambient configuration would choose.
- **Installs the permission handler** — the host's, or the default-deny one; see below.

**The runtime half of the confinement is applied by the turn channel.** Some channels can be withheld
only with the live client and session, which the options builders deliberately do not have, so the
production turn channel applies them on the one path every opener and resumer shares:

- **Discovered MCP servers are kept from starting.** The runtime starts MCP servers from ambient
  configuration — the data directory, the workspace, installed plugins, its built-in servers, a
  managed catalog, and a signed-in account — and Empty mode does not stop them. Before a session is
  created or resumed, the channel asks the runtime to discover its MCP servers for the session and
  names every one in the session's disabled servers, so none of those is started at creation or cold
  resumption. The SDK documents that discovery as covering the user, workspace, plugin and builtin
  sources. The allow-list already keeps an MCP server's tools from the model; naming a discovered
  server before creation also keeps it from running a process with the user's environment and from
  contributing instructions.
- **The session is sealed.** Once the session exists, its installed plugins — which can contribute
  servers, hooks, skills and agents — are cleared with the same session-options patch Empty mode
  sends; every MCP server the session's configuration listing still reports enabled, including one
  from a source discovery does not cover, is disabled for that session only; and the configuration is
  listed again. A server from a source the SDK cannot enumerate before creation, such as a managed
  catalog or a signed-in account, may start, and authenticate, while the session is created; the seal
  then disables it before any turn runs. That contains such a server; it does not prevent it. The
  listing is not complete — it does not report the built-in GitHub server even while that server runs
  — so the seal is defense in depth rather than proof that no server runs. *Runtime Ownership* states
  which sources are prevented and which only contained.
- **It refuses rather than continuing.** A server the runtime keeps enabled refuses the session with an
  `InvalidOperationException` naming it, so the run fails rather than continuing with the server
  available. A created session nothing has recorded is released and deleted; a resumed session is
  only released, because refusing to continue a conversation must not destroy it. A discovery that
  fails opens nothing.

The client's RPC surface exists only once it has started, so the channel starts the client first,
through `CopilotRuntime`'s explained start — idempotent on a started client, reconnecting one whose
connection was lost, and explaining a runtime that cannot start exactly as the runtime's own start
does.

**Options are built fresh for every session and every run.** The SDK mutates the options object it
is handed when it applies its mode defaults, and its clone is shallow — it shares the infinite-session
configuration, the hooks and the delegates — so two sessions created from one object, or from a clone
of it, would share state the SDK has changed. No options object is ever reused or cloned: the agent
holds builder delegates and calls them on every run.

The host's own preferences — the model, the working directory and the instructions as the content of
the system message the confinement configured, which the runtime appends — are applied beside the
confinement, each only when the host supplied a value, so a
host that says nothing gets the runtime's own default for each.

## Permission Handling Is Default-Deny

When the host supplies no handler, every session carries one that:

- approves a request **once** — for this call only — when it is a custom-tool request naming one of
  the supplied tools, compared ordinally;
- rejects every other request with a message naming the tool or the request kind: every built-in
  shell, read, write, URL, MCP, memory, hook and extension request, and every custom tool the host
  did not supply.

Measured against the live runtime, and re-verified by the opt-in live suite, every call to a custom
tool raises a custom-tool permission request, so the handler is on the hot path of every tool call and
approving the host's own tools is its intended outcome. Those tools are safe by construction — they
enforce their own path policy — and the runtime's permission layer is a second, independent gate that
refuses everything else. A grant is approve-once rather than approve-for-session, so every call is
adjudicated individually and no grant outlives the call. A handler is always installed, because a
request no handler answers stays pending forever.

**A host handler replaces the default; it is not composed with it.** A host that supplies a handler
of its own forfeits default-deny unless that handler reproduces it: the factory installs the host's
delegate unchanged, and nothing of the default runs before or after it. The permission mode is
pinned to manual either way, so the host's handler is likewise the only authority on its sessions.
The engine and consolidation paths take no host handler at all — a session the engine drives has no
host present to adjudicate a request — so they always carry the default.

## Built-In Tool Names Are Refused

The runtime refuses to create a session that registers a custom tool under a built-in tool's name,
unless the tool is marked as overriding the built-in — and AgentKit never marks it so, because an
override would silently replace the runtime's tool with the application's under a name the model
already associates with other behavior. Left to the runtime, the collision surfaces on the first run,
as a remote error. So `CopilotAgentFactory` refuses such a name itself, with an `ArgumentException`
naming the tool, on every path — the agent at composition time, the engine at every rotation, and a
resume — before any session exists.

The reserved set is a conservative **union across platforms**, matched case-insensitively: the
built-ins the runtime lists on Windows, the shell family it uses on Linux and macOS, a few model- or
feature-gated built-ins, and the SDK's own published set of isolated built-in names. The built-in set
depends on the platform, so reserving the union means an application composed on one platform does
not fail later on another. Over-reserving costs a rename; under-reserving still fails safe, because
the runtime refuses at session creation. Drift is caught by the opt-in live suite, which lists the
runtime's built-ins on the platform it runs on and fails if any is missing from the reserved set.

## Tool Results Are Delivered Inline

The runtime can divert a tool result larger than about twenty kilobytes to a temporary file and hand
the model the file's path instead of the content. Every session this system opens switches that off.
A confined agent has no tool that can read a file outside its granted locations — and the runtime's
own read tool is suppressed — so a diverted result is a result the model can never see; measured
against the live runtime, a model handed such a path failed the task. AgentKit's tools bound their
own output through Core's tool limits, so a result that reaches the runtime is already a size the
application chose to deliver, and it is delivered whole. Bounding a result is the tool's
responsibility on every provider, and on this one the bound is honored rather than second-guessed. An
application that would rather keep a large result than have it refused composes Core's retention,
which is provider-neutral and keeps the result in memory rather than in a file; see *Large Tool
Outputs Are Retained in the Tools, Not by the Runtime*. The opt-in live suite re-verifies that a
result well past the runtime's diversion size arrives inline.

## Large Tool Outputs Are Retained in the Tools, Not by the Runtime

Core's opt-in retention of large tool outputs (see *ToolOutputSpill Unit Design*) lives inside each
`AIFunction`. The runtime invokes the very instances `CopilotAgentFactory` assigns as a session's tool
definitions, and an agent hands the same instances to every run, created or resumed, so a retained
output's reference outlives the run that produced it. Retention is therefore the large-output
mechanism on Copilot exactly as on every other provider, with no change to this system. The two
reader tools a retaining composition appends are published as definitions, named in the
source-qualified allow-list as `custom:tool_output_read` and `custom:tool_output_search`, and
approved one call at a time by the default permission handler, like any other supplied tool. Neither
name is one the runtime reserves. A host that supplies its own handler must approve them as it
approves its other tools.

The runtime's own large-output diversion (`Tools.LargeOutput`) stays switched off whether or not
retention is composed. It would hand the model the path of a temporary file no confined tool can
read, whereas a retained output is reached through readers that take a reference and no path. With
retention composed, what reaches the runtime is the tool's bounded preview, and it is delivered
inline like any other result.

## Why a Turn-Channel Seam Exists

`CopilotSession` is sealed, its constructor is not public, and none of its methods is virtual;
`CopilotClient` is sealed too. Neither can be faked, subclassed or constructed in a test, and
exercising a turn otherwise needs a live runtime and credentials, which CI does not have and which
this repository deliberately does not require.

Without a seam, the whole of the system above the SDK — the seeding, the usage arithmetic, the
refusals, the agent's streaming and resumption, the ownership discipline — would ship with no
automated evidence at all, on the one provider the project can otherwise reach. `ICopilotTurnChannel`
is therefore one small interface — the session's identity and one turn — opened through one of two
delegates, create and resume, and the production implementation behind it forwards one turn, owns
one release and opens every session through one confining protocol. Every decision worth testing
lives above that line or is factored out of it — the release policy and the opening protocol are
tested with the runtime's steps supplied as delegates; what remains below it is stated plainly as
untested in *CopilotTurnChannel Unit Verification Design* rather than implied to be covered.

Every session *event* type, by contrast, is trivially constructable, so the tests drive the SDK's own
event objects through the system's own matching code. The seam is narrow, and what crosses it is
real.

## No Image-Promoting Decorator

Unlike the ChatClient adapter, this system deliberately does **not** install Core's
`ImagePromotingChatClient`. The Copilot runtime already delivers an image a tool returns to the
model — its runtime converts a binary tool result into content the model sees — as measured against
the live runtime and re-verified by the opt-in live suite, and corroborated by the SDK's
tool-completion result carrying binary results for the model explicitly. Installing the decorator
here would carry the image a second time onto a user message, duplicating content the model already
received. This absence is intentional, applies to the session path as much as to the agent path, and
is recorded here and in the units so a future maintainer does not "helpfully" add it.

## Deployment: The Host Application Provisions the Runtime

The Copilot runtime is a native component, specific to a runtime identifier, and `Microsoft.Copilot.SDK`
does not carry it inside its package. The SDK's build targets download the runtime archive from the
Copilot CLI's GitHub releases at build time, verify it against the release's published SHA-256
checksums, extract it under the project's intermediate folder — about 270 MB per target framework —
and copy it into the output directory, about 356 MB per output in some hundred files under
`runtimes/<rid>/native`. The runtime version is pinned by the SDK version, so the two cannot be
mismatched.

**Only a project that references the SDK directly provisions the runtime.** The SDK's targets ship in
the package's `build` folder rather than its `buildTransitive` folder, so they run only in a project
holding a direct reference to `Microsoft.Copilot.SDK`. An application that reaches the SDK
transitively, through this package, provisions nothing, and its runtime then fails to start. **A host
application must therefore add its own direct reference to `Microsoft.Copilot.SDK`**, at the version
this package depends on or a later compatible one. `CopilotRuntime` names this as the likely cause
when a start fails; see *Runtime Ownership*.

**This package ships no runtime.** Its project sets `CopilotSkipCliDownload`, which the SDK's targets
honor by skipping the download, the copy to the output and the content registration alike. That
keeps the runtime out of this library's outputs, out of every project that references it — including
its own unit tests, which drive a scripted turn channel and never start a runtime — and out of the
package. The SDK reference is deliberately **not** made to flow its build assets to consumers: that
would make every project in a consumer's graph, test projects included, download and copy the
runtime, which is exactly the duplication the host-provisions rule avoids.

**The merge gate is hermetic.** The continuous-integration build workflow sets `CopilotSkipCliDownload`
for the whole workflow as an environment variable, which MSBuild reads as a property, so the build
matrix, the code analysis, the documentation build and the compile-only build of the opt-in live
suite never contact GitHub releases and never copy the runtime; no test there needs it. The
live-samples workflow builds without the property, so the
samples and the opt-in live suite get the runtime, and caches the extracted bundle between runs. A
local build downloads the runtime once per hosting project and keeps it in that project's
intermediate folder, so each hosting project's output carries its own copy.

**Mirrors and offline builds** use the SDK's own controls: `CopilotCliReleaseBaseUrl`, or the
`COPILOT_CLI_DOWNLOAD_BASE_URL` environment variable, redirects the download to a mirror;
`CopilotCliBinaryPath` supplies a runtime already on disk; `CopilotCliDownloadTimeout` bounds the
download; and `CopilotSkipCliDownload` turns provisioning off entirely.

**Runtime identifiers.** The runtime is published for `win-x64`, `win-arm64`, `linux-x64`,
`linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`, `osx-x64` and `osx-arm64`. A build with an
explicit runtime identifier provisions the runtime for that identifier; a build without one
provisions the runtime for the build machine's own platform, so a portable build deployed to a
different platform does not carry the runtime that platform needs.

These are properties of the off-the-shelf SDK rather than of AgentKit's code, and they are why the
dependency is isolated in its own package. They are recorded again in *Microsoft.Copilot.SDK Design*.

## Dependency Isolation

This system carries `Microsoft.Copilot.SDK` — and, through the host's own reference, its native
runtime — and `Microsoft.Agents.AI.Abstractions`, the Agent Framework's abstractions package, for the
`AIAgent` base type the agent derives from. Core admits none of that weight: it takes exactly one
runtime dependency, as recorded in *AgentKitCore System Design*. Isolating them in their own package
is exactly what the dependencies justify: an application that never builds a Copilot agent never
takes the SDK, its native runtime, or the agent abstractions. The system references Core,
`Microsoft.Copilot.SDK` and `Microsoft.Agents.AI.Abstractions`, and nothing else at run time.

## Architecture

The system holds eight units: the runtime, the agent and its factory that a host composing a Copilot
agent uses, and the five that carry a Core session over the runtime.

- **CopilotAgentFactory (Unit)** — the static factory that validates its arguments, refuses a tool
  named like a built-in, derives the allow-list from the supplied tools, withholds the runtime's
  injection channels, pins the permission mode, installs a default-deny permission handler, and
  builds the agent without taking ownership of the runtime. It is also the **single** place a Copilot
  session's options are built, for an agent's created and resumed sessions and for both
  session-engine paths.
- **CopilotAgent (Unit)** — AgentKit's own `AIAgent` over confined Copilot sessions, and the public
  `CopilotAgentSession` that names one conversation: creates a runtime session on a conversation's first run and resumes
  it on every later one, streams the runtime's events as agent updates, sends inline data as in-memory attachments,
  releases a conversation's runtime session without deleting it after every run (deleting only the session of a run made
  without a conversation), serializes a conversation so it can outlive the process, and serializes runs on one
  conversation.
- **CopilotRuntime (Unit)** — the runtime AgentKit starts, confines and owns, with its options type:
  builds the only Copilot client the package runs on — Empty mode, the provisioned runtime spawned as
  a child process with the host's `COPILOT_`/`GITHUB_COPILOT_` variables withheld apart from an
  allow-listed credential, network and telemetry-off set, a data directory it owns — exposes only neutral options,
  explains a runtime that cannot start, and is disposed by whoever started it.
- **CopilotProviderSession (Unit)** — an `IProviderSession` over one Copilot session: sends a turn,
  carries the seeded record ahead of the first message it sends, refuses a turn that reported no
  usage or whose history the runtime rewrote, ends the session on any turn the runtime processed that
  it could not record, records tool traffic as identified pairs, reports the runtime's occupancy, and
  owns and releases its session.
- **CopilotProviderSessionFactory (Unit)** — an `IProviderSessionFactory`: configures the session's
  system message with the application's instructions alone as its content, composes the seeded history into a
  labeled record for the first message to carry, reuses the one confinement path, carries the host's
  working directory, holds the runtime's compaction clear of the engine's rotation point, registers
  the observer before the session is created, and guards the ownership window.
- **CopilotSessionObserver (Unit)** — watches the runtime's event stream and holds the latest usage
  reading, the current turn's entries, and whether the runtime rewrote history.
- **CopilotSummarizer (Unit)** — an `ISummarizer` on a short-lived, tool-free Copilot session, using
  Core's consolidation prompt and releasing its session on every path.
- **CopilotTurnChannel (Unit)** — the internal seam over the sealed SDK session types, and the
  production implementation that opens every session through one confining protocol — every
  discovered MCP server disabled before creation, installed plugins cleared and every MCP server
  disabled and verified afterwards, the session refused if one stays enabled — and owns one runtime
  session handle and releases it — deleting the session for the engine, the summarizer and an
  agent's run made without a conversation, keeping it for an agent's conversation.

The agent and the engine paths share the factory's confinement and the turn channel, and nothing
else: the agent streams its own events and keeps its conversations' sessions, while the engine path
reads occupancy through the observer and deletes each rotated session.

## External Interfaces

The system's public API is six types. `CopilotRuntime` and `CopilotRuntimeOptions` start the runtime;
the factory, the provider-session factory and the summarizer take that runtime, which the host
started and will dispose; and `CopilotAgentSession` is what the agent hands back for a conversation.
Everything else in the package is internal, including the runtime's client.

| Interface | Direction | Format | Constraints |
| --- | --- | --- | --- |
| `CopilotRuntime.StartAsync` | Inbound | `CopilotRuntimeOptions` | Data directory new, empty or AgentKit's |
| `CopilotAgentFactory.Create` | Inbound | Runtime, tools, settings | Tools non-empty, unique, not built-in |
| `CopilotAgentSession` | Outbound | Agent session, JSON | Runtime session kept until deleted |
| `CopilotProviderSessionFactory` | Outbound | Core session factory | One session per rotation |
| `CopilotSummarizer` | Outbound | Core summarizer | Separate tool-free session |
| `CopilotRuntime` | Inbound | Started runtime | Host-owned; never disposed by what uses it |
| Runtime process | Outbound | Copilot SDK, stdio | Empty mode; Copilot variables withheld |
| Session event stream | Inbound | Copilot SDK events | Registered before session creation |

`Create` takes the runtime, the tools, and optionally the instructions, a permission handler, the
agent's name, the model and the working directory. `CopilotAgentSession` serializes as a JSON object
carrying `sessionId` and `stateBag`, and its runtime session is kept until the host deletes it.
`CopilotRuntime` never changes the host's process environment; it decides only the runtime process's.

The application supplies no context window: occupancy and limit come from the runtime's own event
stream, as recorded in *Occupancy Comes From the Runtime*.

### Runtime Ownership

**AgentKit starts the runtime itself, and offers no way to supply a client.** A host calls
`CopilotRuntime.StartAsync` with `CopilotRuntimeOptions` — a data directory, and optionally a GitHub
token, a log level and a logger — and disposes the runtime when finished. The runtime is in Empty
mode, runs as a child process whose environment carries none of the host's `COPILOT_` or
`GITHUB_COPILOT_` configuration variables apart from a credential, network and telemetry-off
pass-through set, and keeps its state in a data directory AgentKit created and marked; any other
non-empty directory, including one whose marker is not exactly the bytes AgentKit writes, is
refused. The marker guards against reusing another tool's directory by mistake; it is not a security
boundary against a process that can already write to the directory. Each of these closes a channel a
host-built client could leave open, and none can be detected on such a client, so the overloads that
took a host's `CopilotClient` were removed rather than kept as a weaker path. *CopilotRuntime Unit
Design* classifies every client option and records the alternatives rejected.

**What is guaranteed, per MCP source.** On that runtime, before any session exists:

| Source | Mechanism | Guarantee | Live evidence |
| --- | --- | --- | --- |
| Built-in GitHub MCP server | Named disabled at create and resume | Prevention | L14a; control L14b |
| User (data directory `mcp-config.json`) | Fresh directory; discovery and disable | Prevention | L14c; control L14d |
| Workspace (`.mcp.json`) | Config discovery off; no trusted folder; disable | Prevention | L14e; control L14f |
| Plugin | Fresh directory; no plugin directories; cleared | Prevention | L15 |
| Managed catalog | Not enumerable before create; seal | Containment only | Not reproducible |
| Signed-in account | Not enumerable before create; seal | Containment only | L14a, this account |
| Host `COPILOT_` environment | Spawned runtime, replacement environment | Prevention, minus pass-through | L11 |

- **Built-in GitHub MCP server.** It is named disabled, so it is not started or authenticated. Empty
  mode alone starts it on the first turn. The name is the one AgentKit knows; a renamed or added
  built-in server would not be prevented, and would be caught by the live suite rather than by the
  seal, because the session's MCP listing does not report built-in servers.
- **User-configured servers.** None can exist in a fresh data directory, and one added later is
  found by the runtime's discovery and disabled before the session is created. Empty mode alone runs
  it.
- **Workspace MCP configuration.** It is not loaded, because configuration discovery is off and the
  data directory trusts no folder. A workspace server that discovery reports is disabled as well. The
  live suite shows a `.mcp.json` server is not executed even when the data directory has been made to
  trust the folder.
- **Plugins.** None is installed in a fresh data directory, plugin directories cannot be configured,
  and the session's installed plugins are cleared after creation.
- **Managed catalog and signed-in account.** These servers follow organization policy and the
  account. The SDK cannot enumerate them before a session exists, so one may start, and
  authenticate, while the session is created. AgentKit then disables every server the session's MCP
  configuration lists, before any turn runs, and refuses the session if one stays enabled. This
  contains such a server; it does not prevent it. Neither source can be set up for a test: a managed
  catalog needs an organization or enterprise policy delivered to the runtime, and an account server
  needs a connector-enabled account, both behind a staged feature rollout. The only evidence
  available is that, for the account the live suite runs as, no MCP server reached pending or
  connected during a turn.

The session's configuration listing is not complete — it does not report the built-in GitHub server
even while that server is connected — so the post-create seal is defense in depth, not proof of
absence. In every case, no MCP tool is on a session's allow-list.

Refusing a session or turn when the runtime's MCP status events report any server pending or
connected would close the listing gap for built-in, managed-catalog and account servers. It is
deferred to a follow-up, and nothing in this release depends on it. Such a check would still detect a
server only after it started, so it would contain such a server, not prevent it.

The other ambient channels are absent from a session on this runtime: the live suite lists no
plugin, skill, instruction source or agent, and the system prompt is smaller than a control session's
that keeps the host environment context (L15). Hooks have no listing; they are covered by repository
hooks being off, no installed plugin, and a fresh data directory, which holds no user hook.

**Identity.** The runtime authenticates with the token in `CopilotRuntimeOptions` when one is given;
otherwise as the signed-in user, from `COPILOT_GITHUB_TOKEN`, `GH_TOKEN` or `GITHUB_TOKEN`, from
credentials stored in its data directory, or from the GitHub CLI.

**There is no client-level working directory.** The runtime takes a working directory per session,
and only this system builds a session's options, so the working directory is a parameter of
`CopilotAgentFactory.Create` and of the `CopilotProviderSessionFactory` constructor, applied only when
supplied — otherwise the runtime uses the host process's working directory, which the runtime process
inherits. A host that built a session by hand to reach the setting would forfeit the confinement. The
working directory grants nothing: what an agent may read or write is decided by its tools' own path
policy. `CopilotSummarizer` takes none, because a consolidation session offers no tools.

## Data Flow

The agent path:

```text
Application
        │  messages (text, DataContent)
        ▼
CopilotAgent ──► CopilotAgentFactory.BuildSessionConfig / BuildResumeSessionConfig
        │                    (allow-list, channels withheld, Manual mode, handler, streaming on)
        │  fresh CreateSessionOptions or ResumeSessionOptions (+ run's mapper as Events.OnEvent)
        ▼
CopilotTurnChannel (MCP disabled before any turn, plugins cleared; kept on release) ──► CopilotSession (runtime)
        │  MessageOptions (prompt, blob attachments)            │  events
        ▼                                                       ▼
CopilotAgent ◄──────────────────────────────────── text deltas, tool calls, results, usage
        │  AgentResponseUpdate stream; CopilotAgentSession.SessionId recorded
        ▼
Application
```

The session-engine path:

```text
CompactingAgentSession
        │  seed (instructions, tools, history)
        ▼
CopilotProviderSessionFactory ──► CopilotAgentFactory.BuildEngineSessionConfig
        │                                   (same confinement, compaction held clear)
        │  CreateSessionOptions (+ observer as Events.OnEvent)
        ▼
CopilotTurnChannel (MCP disabled before any turn, plugins cleared; deleted on release) ──► CopilotSession (runtime)
        │  turn                              │  events
        ▼                                    ▼
CopilotProviderSession ◄──────────── CopilotSessionObserver
        │  ProviderTurn + ContextUsage
        ▼
CompactingAgentSession ──► CopilotSummarizer ──► a separate, tool-free CopilotSession
```

## Folder Layout

```text
src/DemaConsulting.AgentKit.Agents.Copilot/
├── CopilotAgent.cs                   — AgentKit's AIAgent over confined Copilot sessions
├── CopilotAgentFactory.cs            — builds a Copilot agent, and every session's options
├── CopilotAgentSession.cs            — one agent conversation: its runtime session and state bag
├── CopilotProviderSession.cs         — an AgentKit session over one Copilot session
├── CopilotProviderSessionFactory.cs  — creates one seeded Copilot session per rotation
├── CopilotRuntime.cs                 — the runtime AgentKit starts, confines and owns
├── CopilotRuntimeOptions.cs          — the neutral options a host may give the runtime
├── CopilotSessionObserver.cs         — watches the runtime's event stream
├── CopilotSummarizer.cs              — consolidates on a separate tool-free session
└── CopilotTurnChannel.cs             — the seam over the sealed SDK session types
```

`CopilotAgent.cs` and `CopilotAgentSession.cs` together form the `CopilotAgent` unit;
`CopilotRuntime.cs` and `CopilotRuntimeOptions.cs` together form the `CopilotRuntime` unit.

## Dependencies

- **AgentKitCore** — supplies the family-prefix tool convention and the `AIFunction` currency the
  supplied tools are built from, and the session contracts the system implements —
  `IProviderSession`, `IProviderSessionFactory`, `ISummarizer`, `ProviderSessionSeed`,
  `ProviderTurn`, `TranscriptEntry`, `ContextUsage` and `ConsolidationPrompt`; see *AgentKitCore
  System Design*.
- **Microsoft.Copilot.SDK** — supplies `CopilotClient`, `CopilotSession`, the create and resume
  session options and their shared base, the message options and blob attachments, the permission
  RPC, the published set of isolated built-in tool names, the MCP discovery and configuration RPC
  and the session-options patch, and the session event stream; see
  *Microsoft.Copilot.SDK Design*. Its native runtime is provisioned by the host application, as
  recorded above.
- **Microsoft.Agents.AI.Abstractions** — supplies the `AIAgent` base type, `AgentSession` with its
  state bag, and the response and update types the agent produces; see
  *Microsoft.Agents.AI.Abstractions Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies `AIFunction`, the content types the agent maps
  events onto and reads input from, and `UsageDetails`; reached transitively; see
  *Microsoft.Extensions.AI.Abstractions Design*.

## Risk Control Measures

Copilot arrives able to act on the machine it runs on, so this system's risk controls are the ones
that take that capability back. They are applied on one path for every session the package builds,
created or resumed — `CopilotAgentFactory`'s single confinement block for everything a session's
options carry, and `CopilotTurnChannel`'s single opening protocol for what needs the live session —
so no path can acquire capability another path withholds.

- **The source-qualified, derived allow-list.** The session's available-tools allow-list is assigned
  from the same collection published as the session's tools, so the two cannot drift apart, and each
  entry is qualified as a custom tool, so no built-in tool is admitted even under a matching name. This
  is the segregation the package exists for; see *Purpose*.
- **The default-deny permission handler, under a pinned manual mode.** With no host handler supplied,
  a request is approved, once, only when it names one of the supplied tools; everything else —
  including every built-in request — is rejected without asking a user who could not adjudicate it.
  The manual mode ensures every request reaches the handler. A host handler replaces this default
  outright; see *Permission Handling Is Default-Deny*.
- **The withheld injection channels.** Skills, discovered instructions, hooks, git operations and
  commit co-author trailers, memory
  and the cross-session store, sub-agents, extensions, configuration discovery, tool search, schedule
  management, image generation, experimental features and embedding retrieval are disabled on every
  session, the host environment context is removed from the system prompt, the built-in GitHub MCP
  server is disabled, every other MCP server the session lists is disabled and verified, and
  the session's installed plugins are cleared, because each would attach capability, direction or
  identity the composing application never granted. Discovered servers and the built-in GitHub
  server are disabled before the session exists; any other listed server is disabled before the first
  turn, and the session is refused if one stays enabled. A server the SDK cannot enumerate before
  creation, such as a managed catalog's or a signed-in account's, may still start during session
  creation: it is contained, not prevented.
- **The runtime AgentKit owns.** Every session runs on a runtime AgentKit started in Empty mode, as a
  child process with the host's `COPILOT_`/`GITHUB_COPILOT_` variables withheld apart from an
  allow-listed credential, network and telemetry-off set, on a data directory AgentKit owns; no client of the
  host's is accepted. This is what prevents the user, workspace and plugin sources, which Empty mode
  alone does not; see *Runtime Ownership*.
- **The refused built-in names.** A supplied tool cannot carry a built-in tool's name, and AgentKit
  never marks a tool as overriding a built-in, so no supplied tool can stand in for one the model
  associates with other behavior.
- **Inline tool results.** The runtime's diversion of large results to a file is off, so no result
  is turned into a path the confined agent cannot read; see *Tool Results Are Delivered Inline*.
  Where the application opts in, a large result is kept by Core's retention inside the tools, never
  by the runtime.

Two further measures protect the conversation rather than the machine: the runtime's own compaction
threshold is held clear of the engine's rotation point, with every runtime rewrite of the
conversation detected and refused, and a session whose conversation the engine can no longer account
for is ended rather than reused. Both are argued above.

## Design Constraints

- **No ownership of the runtime by what uses it**: the host starts and disposes the
  `CopilotRuntime`; the agent, the provider-session factory and the summarizer release only the
  session handles they open, and delete only the sessions the engine and the summarizer are
  finished with
- **No client of the host's**: the runtime's client is built and owned by `CopilotRuntime` and never
  exposed, so no session can be opened on a client of unknown mode, data directory or environment
- **One confinement path**: every session — agent created, agent resumed, engine and consolidation —
  is built through one derivation of the allow-list and one confinement block, and opened through one
  confining protocol; a second builder would be the drift this package prevents
- **Refuse rather than continue**: a session the runtime will not confine — an MCP server it keeps enabled, a
  discovery or seal it refuses — is never handed to the model
- **Fresh options per session**: no session options object is reused or cloned, because the SDK
  mutates the options it is handed and its clone is shallow
- **No window from the application**: occupancy and limit come from the runtime, which is what makes
  the session engine's accounting the runtime's own
- **No change to the host's process environment**: the system decides the runtime process's
  environment through the SDK's replacement environment, and never sets or clears a variable of the
  host process
- **Isolated dependency**: `Microsoft.Copilot.SDK` and `Microsoft.Agents.AI.Abstractions` reach an
  application only through this package; the SDK's native runtime reaches it only through the host
  application's own direct reference to the SDK
- **Compliance**: all functionality must be traceable to requirements
- **Quality**: zero warnings, full test coverage, complete documentation
- **Portability**: compatible across supported .NET platforms, within the target frameworks and
  runtime identifiers the Copilot SDK itself supports

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.
