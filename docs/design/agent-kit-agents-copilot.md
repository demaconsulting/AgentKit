# AgentKitAgentsCopilot System Design

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The AgentKitAgentsCopilot system gives a GitHub Copilot `CopilotClient` an agent of AgentKit's own —
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
Handling Is Default-Deny*. And it fixes the ownership contract: the host constructs, starts, and
disposes the `CopilotClient`; nothing in this system takes ownership of it.

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

The block does four things, each set explicitly on every session rather than inherited from the
client's mode, because the host chooses the mode and AgentKit cannot read it back:

- **Derives the allow-list.** The tool definitions and the available-tools allow-list are assigned
  from one collection in two adjacent statements. Each allow-list entry is **source-qualified** as
  `custom:<name>`: measured against the live runtime, and re-verified by the opt-in live suite, a
  bare-name allow-list also admits a built-in tool whose name matches, so the qualifier is what makes
  the list name only the tools this package registered. The runtime classifies a tool by where it was
  registered rather than by parsing its name, so a colliding name cannot spoof the qualifier.
- **Closes every other channel of injected capability.** Skills and the built-in skills, discovered
  custom instructions and on-demand instruction discovery, repository hooks, host git operations,
  the cross-session store and memory, the built-in sub-agents and non-local agent discovery,
  extensions, configuration discovery, tool search, schedule management, image generation, and the
  runtime's diversion of large tool results to a file are all switched off. The runtime's built-in
  GitHub MCP server is disabled by name: measured against the live runtime, it is started and
  authenticated with the user's GitHub identity on every session even in the client's Empty mode,
  and while the allow-list already keeps its tools out of reach, a server holding the user's
  identity is disabled rather than merely filtered. The full list, with the reason for each, is in
  *CopilotAgentFactory Unit Design*.
- **Pins the permission mode to manual.** In the runtime's allow-all mode tool, path and URL requests
  are approved without asking the handler, and in its assisted mode a client may approve them
  itself; either would bypass the handler without anything in this package changing. Pinning the
  manual mode keeps the handler the only authority, whatever mode ambient configuration would choose.
- **Installs the permission handler** — the host's, or the default-deny one; see below.

**Options are built fresh for every session and every run.** The SDK mutates the options object it
is handed when it applies its mode defaults, and its clone is shallow — it shares the infinite-session
configuration, the hooks and the delegates — so two sessions created from one object, or from a clone
of it, would share state the SDK has changed. No options object is ever reused or cloned: the agent
holds builder delegates and calls them on every run.

The host's own preferences — the model, the working directory and the instructions as an appended
system message — are applied beside the confinement, each only when the host supplied a value, so a
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
application chose to deliver, and it is delivered whole. AgentKit offers no provider-neutral
equivalent of spilling a large result to a file: bounding a result is the tool's responsibility on
every provider, and on this one the bound is honored rather than second-guessed. The opt-in live suite
re-verifies that a result well past the runtime's diversion size arrives inline.

## Why a Turn-Channel Seam Exists

`CopilotSession` is sealed, its constructor is not public, and none of its methods is virtual;
`CopilotClient` is sealed too. Neither can be faked, subclassed or constructed in a test, and
exercising a turn otherwise needs a live runtime and credentials, which CI does not have and which
this repository deliberately does not require.

Without a seam, the whole of the system above the SDK — the seeding, the usage arithmetic, the
refusals, the agent's streaming and resumption, the ownership discipline — would ship with no
automated evidence at all, on the one provider the project can otherwise reach. `ICopilotTurnChannel`
is therefore one small interface — the session's identity and one turn — opened through one of two
delegates, create and resume, and the production implementation behind it forwards one call and owns
one release. Every decision worth testing lives above that line; what lies below it is stated plainly
as untested in *CopilotTurnChannel Unit Verification Design* rather than implied to be covered.

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
transitively, through this package, provisions nothing, and its client then fails to start. **A host
application must therefore add its own direct reference to `Microsoft.Copilot.SDK`**, at the version
this package depends on or a later compatible one. `CopilotClientStarter` names this cause when a
start fails; see *External Interfaces*.

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

The system holds eight units: the agent, its factory and the starter that a host composing a Copilot
agent uses, and the five that carry a Core session over the runtime.

- **CopilotAgentFactory (Unit)** — the static factory that validates its arguments, refuses a tool
  named like a built-in, derives the allow-list from the supplied tools, closes the runtime's
  injection channels, pins the permission mode, installs a default-deny permission handler, and
  builds the agent without taking ownership of the client. It is also the **single** place a Copilot
  session's options are built, for an agent's created and resumed sessions and for both
  session-engine paths.
- **CopilotAgent (Unit)** — AgentKit's own `AIAgent` over confined Copilot sessions, and the public
  `CopilotAgentSession` that names one conversation: creates a runtime session on a conversation's first run and resumes
  it on every later one, streams the runtime's events as agent updates, sends inline data as in-memory attachments,
  releases a conversation's runtime session without deleting it after every run (deleting only the session of a run made
  without a conversation), serializes a conversation so it can outlive the process, and serializes runs on one
  conversation.
- **CopilotClientStarter (Unit)** — a host-facing helper that starts the host's client and, when the
  start fails, explains the likely cause — an ambient `COPILOT_CLI_PATH`, or a host that does not
  reference the SDK directly — without changing the connection or the environment.
- **CopilotProviderSession (Unit)** — an `IProviderSession` over one Copilot session: sends a turn,
  carries the seeded record ahead of the first message it sends, refuses a turn that reported no
  usage or whose history the runtime rewrote, ends the session on any turn the runtime processed that
  it could not record, records tool traffic as identified pairs, reports the runtime's occupancy, and
  owns and releases its session.
- **CopilotProviderSessionFactory (Unit)** — an `IProviderSessionFactory`: configures the session's
  system message with the application's instructions alone, composes the seeded history into a
  labeled record for the first message to carry, reuses the one confinement path, carries the host's
  working directory, holds the runtime's compaction clear of the engine's rotation point, registers
  the observer before the session is created, and guards the ownership window.
- **CopilotSessionObserver (Unit)** — watches the runtime's event stream and holds the latest usage
  reading, the current turn's entries, and whether the runtime rewrote history.
- **CopilotSummarizer (Unit)** — an `ISummarizer` on a short-lived, tool-free Copilot session, using
  Core's consolidation prompt and releasing its session on every path.
- **CopilotTurnChannel (Unit)** — the internal seam over the sealed SDK session types, and the
  production implementation that owns one runtime session handle and releases it — deleting the
  session for the engine, the summarizer and an agent's run made without a conversation, keeping it
  for an agent's conversation.

The agent and the engine paths share the factory's confinement and the turn channel, and nothing
else: the agent streams its own events and keeps its conversations' sessions, while the engine path
reads occupancy through the observer and deletes each rotated session.

## External Interfaces

The system's public API is five types. Four take a `CopilotClient` the host constructed, started and
will dispose; the fifth, `CopilotAgentSession`, is what the agent hands back for a conversation.
Everything else in the package is internal.

| Interface | Direction | Format | Constraints |
| --- | --- | --- | --- |
| `CopilotAgentFactory.Create` | Inbound | Client, tools, settings | Tools non-empty, unique, not built-in |
| `CopilotAgentSession` | Outbound | Agent session, JSON | Runtime session kept until deleted |
| `CopilotProviderSessionFactory` | Outbound | Core session factory | One session per rotation |
| `CopilotSummarizer` | Outbound | Core summarizer | Separate tool-free session |
| `CopilotClientStarter.StartAsync` | Inbound | Host's client | Explains a failed start |
| `CopilotClient` | Inbound/Outbound | Copilot SDK | Host-owned; never disposed here |
| Session event stream | Inbound | Copilot SDK events | Registered before session creation |

`Create` takes the client, the tools, and optionally the instructions, a permission handler, the
agent's name, the model and the working directory. `CopilotAgentSession` serializes as a JSON object
carrying `sessionId` and `stateBag`, and its runtime session is kept until the host deletes it.
`CopilotClientStarter` changes neither the connection nor the environment.

The application supplies no context window: occupancy and limit come from the runtime's own event
stream, as recorded in *Occupancy Comes From the Runtime*.

### Host Client Construction

The host constructs the client, and three of its choices bear on this system:

- **Mode.** `CopilotClientMode.Empty` is recommended. Every opt-out this system depends on is set on
  each session regardless of the mode, so no mode weakens the confinement; Empty mode adds the
  protections that exist only at client level — no installed plugins, and no host environment
  context in the system prompt.
- **Runtime connection.** In-process hosting through `RuntimeConnection.ForInProcess` is the usual
  choice. Empty mode requires a base directory — the runtime's data directory, under which a kept
  session's state is preserved — and the host chooses it. A spawned-process connection is the
  alternative where the in-process runtime cannot be used; see the `COPILOT_CLI_PATH` pitfall below.
- **Identity.** `ClientGitHubOptions` names the GitHub identity the runtime uses: the logged-in user,
  in which case the runtime also honors the `COPILOT_GITHUB_TOKEN`, `GH_TOKEN` and `GITHUB_TOKEN`
  environment variables, or an explicit token.

**There is no client-level working directory.** The runtime takes a working directory per session,
and only this system builds a session's options, so the working directory is a parameter of
`CopilotAgentFactory.Create` and of the `CopilotProviderSessionFactory` constructor, applied only when
supplied — otherwise the runtime uses the host process's working directory. A host that built a
session by hand to reach the setting would forfeit the confinement. The working directory grants
nothing: what an agent may read or write is decided by its tools' own path policy. `CopilotSummarizer`
takes none, because a consolidation session offers no tools.

### The `COPILOT_CLI_PATH` Pitfall

In-process hosting loads the runtime library from the application's output, where the SDK provisioned
it — unless the `COPILOT_CLI_PATH` environment variable is set, in which case it loads the library
from beside that path instead. There is no per-client override: the variable is read from the
process environment. Every shell the Copilot CLI starts sets it to the CLI's own installation, which
does not ship the in-process runtime library, so an application launched from such a shell fails to
start with an error about a missing library that names neither the variable nor the remedy. A
spawned-process connection reads the variable from the replacement environment when one is supplied,
so an environment that omits it avoids the problem.

`CopilotClientStarter.StartAsync` starts the client and, on a failure, throws an
`InvalidOperationException` carrying the original failure and naming the likely cause: the variable
and its value, with the three remedies — unset it, point it at a runtime that ships the in-process
library, or connect through a spawned process with an environment that omits it — or, when the
variable is not set, the host's missing direct reference to the SDK. The helper never changes the
connection or the environment: pointing the variable at a valid runtime is a supported configuration,
and changing the process environment from a library is a global side effect. Cancellation propagates
unchanged. The helper is optional; a host that starts the client itself loses only the diagnosis.

## Data Flow

The agent path:

```text
Application
        │  messages (text, DataContent)
        ▼
CopilotAgent ──► CopilotAgentFactory.BuildSessionConfig / BuildResumeSessionConfig
        │                    (allow-list, channels closed, Manual mode, handler, streaming on)
        │  fresh CreateSessionOptions or ResumeSessionOptions (+ run's mapper as Events.OnEvent)
        ▼
CopilotTurnChannel (create or resume; kept on release) ──► CopilotSession (runtime)
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
CopilotTurnChannel (deleted on release) ──► CopilotSession (runtime)
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
├── CopilotClientStarter.cs           — starts the host's client, explaining a failed start
├── CopilotProviderSession.cs         — an AgentKit session over one Copilot session
├── CopilotProviderSessionFactory.cs  — creates one seeded Copilot session per rotation
├── CopilotSessionObserver.cs         — watches the runtime's event stream
├── CopilotSummarizer.cs              — consolidates on a separate tool-free session
└── CopilotTurnChannel.cs             — the seam over the sealed SDK session types
```

`CopilotAgent.cs` and `CopilotAgentSession.cs` together form the `CopilotAgent` unit.

## Dependencies

- **AgentKitCore** — supplies the family-prefix tool convention and the `AIFunction` currency the
  supplied tools are built from, and the session contracts the system implements —
  `IProviderSession`, `IProviderSessionFactory`, `ISummarizer`, `ProviderSessionSeed`,
  `ProviderTurn`, `TranscriptEntry`, `ContextUsage` and `ConsolidationPrompt`; see *AgentKitCore
  System Design*.
- **Microsoft.Copilot.SDK** — supplies `CopilotClient`, `CopilotSession`, the create and resume
  session options and their shared base, the message options and blob attachments, the permission
  RPC, the published set of isolated built-in tool names, and the session event stream; see
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
that take that capability back. All of them are applied in one place — `CopilotAgentFactory`'s
single confinement block — for every session the package builds, created or resumed, so no path can
acquire capability another path withholds.

- **The source-qualified, derived allow-list.** The session's available-tools allow-list is assigned
  from the same collection published as the session's tools, so the two cannot drift apart, and each
  entry is qualified as a custom tool, so no built-in tool is admitted even under a matching name. This
  is the segregation the package exists for; see *Purpose*.
- **The default-deny permission handler, under a pinned manual mode.** With no host handler supplied,
  a request is approved, once, only when it names one of the supplied tools; everything else —
  including every built-in request — is rejected without asking a user who could not adjudicate it.
  The manual mode ensures every request reaches the handler. A host handler replaces this default
  outright; see *Permission Handling Is Default-Deny*.
- **The closed injection channels.** Skills, discovered instructions, hooks, git operations, memory
  and the cross-session store, sub-agents, extensions, configuration discovery, tool search, schedule
  management and image generation are closed on every session, and the built-in GitHub MCP server is
  disabled, because each would attach capability, direction or identity the composing application
  never granted.
- **The refused built-in names.** A supplied tool cannot carry a built-in tool's name, and AgentKit
  never marks a tool as overriding a built-in, so no supplied tool can stand in for one the model
  associates with other behavior.
- **Inline tool results.** The runtime's diversion of large results to a file is off, so no result
  is turned into a path the confined agent cannot read; see *Tool Results Are Delivered Inline*.

Two further measures protect the conversation rather than the machine: the runtime's own compaction
threshold is held clear of the engine's rotation point, with every runtime rewrite of the
conversation detected and refused, and a session whose conversation the engine can no longer account
for is ended rather than reused. Both are argued above.

## Design Constraints

- **No ownership of the client**: the host constructs, starts and disposes the `CopilotClient`; this
  system releases only the session handles it opens, and deletes only the sessions the engine and
  the summarizer are finished with
- **One confinement path**: every session — agent created, agent resumed, engine and consolidation —
  is built through one derivation of the allow-list and one confinement block; a second builder would
  be the drift this package prevents
- **Fresh options per session**: no session options object is reused or cloned, because the SDK
  mutates the options it is handed and its clone is shallow
- **No window from the application**: occupancy and limit come from the runtime, which is what makes
  the session engine's accounting the runtime's own
- **No change to the host's environment or connection**: the system diagnoses a runtime that cannot
  load, and never sets or clears an environment variable or selects a connection on the host's behalf
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
