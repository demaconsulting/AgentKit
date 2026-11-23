# System Verification Design

This document describes the system-level verification strategy for the AgentKitAgentsCopilot system.

## Verification Approach

The AgentKitAgentsCopilot system is verified through system-level integration tests that exercise the
factory's session-options path, its permission handling, the agent it builds, the runtime AgentKit
starts and owns, and a whole compacting conversation **without a live Copilot runtime and without any credential**.
The safety-critical property of the original increment — that the session allow-list is derived from
the same collection as the published tools, so the two cannot drift apart — is asserted directly
against constructed session options, which are plain constructable objects requiring no client. The
default permission handler is exercised by invoking it with constructed permission requests and
asserting on the decisions it returns, on the agent path and on the session path alike.

**A real `CompactingAgentSession` is run to a rotation, and a real `CopilotAgent` is run, serialized
and resumed.** The session engine, the rotation engine, the summarizer and the agent are the genuine
ones; what stands in for the runtime is a scripted runtime that opens and resumes sessions, replays
the SDK's **own** session event objects into the package's own event handlers, and answers with the
SDK's own assistant-message type. So the seeding, the occupancy accounting, the consolidation, the
agent's streaming and resumption, and the ownership discipline are all exercised end to end, offline.

**Why that is possible at all, and where the boundary is.** The SDK's session and client types are sealed with
non-public constructors and no virtual members, so neither can be faked, subclassed or constructed in a test. AgentKit
therefore reaches them through one internal seam — `ICopilotTurnChannel` with its opener and resumer delegates — whose
production implementation forwards a turn, owns one release and opens every session through one confining protocol, the
protocol's order and failure handling factored out and tested with the runtime's steps as delegates; see
*CopilotTurnChannel Unit Verification Design*, which states plainly what that leaves unexercised. The SDK's options,
permission and event types, by contrast, are all constructable, so nothing about their shapes is simulated. The runtime
AgentKit owns is reached through pure internal seams: its client options and the runtime's environment are built from a
host environment the test supplies, its data-directory rules run against temporary directories, and its start is a
delegate, so no runtime process is started.

**The live runtime is exercised by a separate, opt-in suite.** What a scripted runtime cannot
establish — what the real runtime does with the options it is handed — is measured by the live
Copilot suite described under *Supplementary Live Verification* below. It is supplementary evidence:
it never runs in the pipeline that builds the trace matrix, so no requirement is traced to it, and
the traced scenarios stand on their own.

**What is out of automated scope, stated honestly.**

- **The traced suite never reaches a live runtime.** The agent's and the engine's session lifecycle
  calls reach the Copilot runtime only through the seam, so the runtime's actual suppression of a
  shell or file-editing tool at execution time is outside the traced suite. The offline evidence is
  that the options carrying the allow-list are built correctly and that the permission handler
  adjudicates as required — the two places where the suppression is decided. The live suite shows
  the suppression holding on the real runtime.
- **Whether the runtime honors a request to disable its own infinite-session compaction is verified,
  by manual measurement against the live runtime, and the answer is no.** Three sessions were run on
  the Copilot SDK the package was first built on, on one model (`gpt-5.4-mini`), each filled with the
  same roughly fifty thousand tokens:

  | Session configuration | Compaction events observed |
  | --- | --- |
  | `Enabled = false`, background threshold 0.05 — crossed | **2 — it compacted anyway** |
  | `Enabled = false`, background threshold 0.50 — not crossed | 0 |
  | `Enabled = false` alone, at the runtime's default threshold of 0.80 — never reached | 0 |

  The measurement was repeated when the package moved to `Microsoft.Copilot.SDK`, with the same
  result: with the flag false and a threshold far below the session's occupancy, the runtime started
  and completed a compaction on each turn, and with a threshold above the occupancy it did not; the
  flag only suppresses the session's workspace. The enablement flag is ignored and the
  background-compaction threshold is honored: a session compacts exactly when its threshold is
  crossed, whatever the flag says. AgentKit therefore raises the threshold to 0.95 on every session
  its engine drives, a quarter of the window clear of the engine's rotation point at 0.70, and relies
  on that. The flag is still set, as a statement of intent and in case the runtime ever begins
  honoring it, but nothing depends on it. The events observed were `SessionCompactionStartEvent` and
  `SessionCompactionCompleteEvent`, which are event types the adapter's observer matches on — so the
  detection path is confirmed working against the live runtime too. This measurement is not
  automated and is not part of the live suite: it is a property of the runtime, recorded here because
  the design depends on it.
- **Whether a live conversation ever crosses 0.95 before the engine rotates it at 0.70 is
  unverified.** The threshold is a margin, not a guarantee, and the runtime's last-resort behavior is
  deliberately left in place. The session observer watches for the runtime's own compaction,
  truncation, context-clearing and snapshot-rewind events, and the provider session refuses the next
  turn if any arrives — before a turn is sent as well as after one returns. What the tests prove is
  that the raised threshold is carried on every session and that the refusal fires; what only a live
  run can settle is whether the refusal ever needs to.
- **That a seeded record charged as overhead makes the engine's accounting drift is measured, not
  argued.** On a live run with the record placed in the system message, the overhead the engine
  accounted against — the system message and the tool declarations — climbed across three rotations:

  | Rotation | Overhead accounted (tokens) |
  | --- | --- |
  | First | 8,694 |
  | Second | 10,708 |
  | Third | 12,032 |

  Overhead grew at every rotation while the conversation appeared small, so the engine's picture of
  its own window drifted further from the runtime's the longer the conversation ran. Carried on the
  first user message the record is charged to the conversation instead, which is the region that gets
  compacted. This measurement is not automated and is not re-run by either suite; it is recorded here
  because it is the reason the record travels where it does.
- **Whether the model weights a seeded record delivered as one conversation message as it would
  weight the turns it replaces is unverified**, and unverifiable offline. The tests prove the record
  is rendered and ordered exactly as designed, that every entry kind a rotation produces survives it,
  and that it reaches the model on the conversation channel rather than the system one. The live
  suite's rotation scenario recalls a codeword stated only before the rotations, which is consistent
  with the record being used but is one observation on one model rather than a measurement of
  weighting.
- **That a live runtime emits its usage event before it goes idle is shown only on the live
  runtime.** The adapter refuses a turn that reported no usage, so a runtime that reported occupancy
  only after going idle would surface as a refusal rather than as a wrong figure. In the live suite's
  rotation scenario every turn was accepted and the session rotated on the runtime's own figures,
  which such a runtime would have made impossible.
- **The runtime's own delivery of a tool-returned image** — the reason the image-promoting decorator
  is deliberately absent — is not proven offline. It is shown on the live runtime by the live suite's
  image-delivery scenario.

System tests reside in `AgentKitAgentsCopilotTests.cs`, with the scripted runtime in
`FakeCopilotTurnChannel.cs`, both within the `DemaConsulting.AgentKit.Agents.Copilot.Tests` project.
The runtime's other injection channels — its skills, its custom instructions, its built-in GitHub MCP
server and the rest — are withheld on the same session options and are verified at the unit level
against the built options, where each value is set.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. **No Copilot runtime is started, no credential is used, and no network
  access is made.** The session options and the permission requests are plain constructable objects,
  the runtime session is reached only through the package's own internal turn-channel seam, and the
  runtime's start is supplied as a delegate
- **Mocking**: A hand-written scripted runtime replaying the SDK's own event types; no mocking
  framework
- **File system**: Temporary directories under the system temporary folder for the runtime's
  data-directory scenarios, deleted afterwards
- **Isolation**: Each test constructs its own tools, options, runtime, agent and session; no state is
  shared

The supplementary live suite has an environment of its own, described under *Supplementary Live
Verification*.

## System-Level Test Scenarios

### Tool Suppression: The Allow-List Is Derived From the Supplied Tools

**Test**: `AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools`

The safety-critical scenario. Builds the session options from a set of supplied tools and asserts
the available-tools allow-list is exactly the names of the published tools, each qualified as a
custom tool, in the same order, and that the published tool set is the same size — the two derived
from one collection. A drift here would silently re-admit a built-in tool an application meant to
withhold, and an unqualified entry would admit a built-in that happened to share its name.

### Tool Suppression: A Tool Named Like a Built-In Is Refused Before Any Session Exists

**Test**: `AgentKitAgentsCopilot_BuildSessionConfig_BuiltInToolName_IsRefused`

Composes a tool set in which one tool is named `view`, like the runtime's file viewer, and builds an
agent from it. Asserts the build fails with an `ArgumentException` naming `'view'`, and that the
scripted runtime was never asked for a session. The runtime itself refuses to create a session that
registers a custom tool under a built-in's name, but only on the agent's first run; refusing it when
the application composes its tools moves the failure to the place the application can fix it. The
same scenario is the system-level evidence that agent construction rejects such a tool list.

### Tool Suppression: Ambient MCP Servers Disabled and Plugins Cleared Before Any Turn on Every Session

**Test**: `AgentKitAgentsCopilot_EverySession_AmbientMcpServersAreClosed`

Opens both the engine's options and an agent's options through the package's confining protocol,
with a runtime whose discovery reports a non-GitHub MCP server and whose session keeps reporting it
enabled however often it is disabled. Asserts, for each, that the server and the built-in GitHub
server were both named in the options' disabled servers when the session was opened, that the
session's installed plugins were cleared, that the server was disabled for the session, and that the
session was then refused with an error naming the server and abandoned. A confined agent receives no
MCP server: the allow-list keeps a server's tools from the model, but a started server still runs a
process with the user's environment and can contribute instructions, so a server that stays enabled
refuses the session rather than letting the run continue. Discovered servers and the built-in GitHub
server are named disabled before the session exists; any other server the session lists is disabled
before the first turn, and the session is refused if one stays enabled. This scenario evidences the
protocol, not which servers the runtime starts: a server the SDK cannot enumerate before creation,
such as a managed catalog's or a signed-in account's, may still start during session creation and is
then contained rather than prevented, and the session's listing does not report the built-in server
at all. That the runtime honors the create-time disabled list (L12a), the seal's session-scoped
disable (L12c) and the resume-time disabled list (L12d) is shown by the live suite's ambient-server
scenarios, and which sources never start on the runtime AgentKit owns by its L14 scenarios.

### Permission Handling: A Supplied Tool Is Approved

**Test**: `AgentKitAgentsCopilot_DefaultPermissionHandler_SuppliedTool_IsApproved`

Verifies the default handler approves a custom-tool request naming one of the supplied tools, by
asserting the returned decision is an approval.

### Permission Handling: An Unlisted Custom Tool Is Rejected

**Test**: `AgentKitAgentsCopilot_DefaultPermissionHandler_UnlistedCustomTool_IsRejected`

Verifies the default handler rejects a custom-tool request whose name was not supplied, by asserting
the returned decision is a rejection.

### Permission Handling: A Built-In Tool Is Rejected

**Test**: `AgentKitAgentsCopilot_DefaultPermissionHandler_BuiltInTool_IsRejected`

Verifies the default handler rejects a built-in request — one that is not a supplied custom tool — by
asserting the returned decision is a rejection. This is the runtime-tool suppression the package
exists for, enforced at the permission boundary as well as in the allow-list. That every request
reaches the handler at all, rather than being approved by the runtime without asking it, is asserted
at the unit level, where the permission mode is pinned.

### Permission Handling: A Built-In Tool Is Rejected on the Session Path Too

**Test**: `AgentKitAgentsCopilot_Session_BuiltInToolRequest_IsRejectedOnTheSessionPath`

The same assertion against the options a **rotation** produces. It is stated separately because the
session path was added after the agent path and takes no host-supplied handler at all: a session
that was confined only by inheriting the agent path's code, without a test saying so, would be
confined by accident rather than by design.

### Agent Construction: A Missing Runtime Is Refused

**Test**: `CopilotAgentFactory_Create_NullRuntime_Throws`

Verifies the public entry point refuses a null runtime with `ArgumentNullException` before any session
is constructed. No overload takes a client of the host's, so a runtime AgentKit started is the only
thing an agent can be built on. Together with the built-in name scenario above, this is the system-level evidence
that agent construction is validated; the remaining validation — the tool list, the instructions,
the model and the working directory — is asserted at the unit level in *CopilotAgentFactory Unit
Verification Design*.

### Session: A Conversation Runs on Copilot and Reports the Runtime's Occupancy

**Test**: `AgentKitAgentsCopilot_Session_AnswersAndReportsTheRuntimesOccupancy`

Runs a real `CompactingAgentSession` over a scripted Copilot runtime for one turn, and asserts the
answer reaches the caller and that the occupancy, the limit and the conversation split the engine
accounts against are exactly the three figures the runtime reported. This is the scenario the whole
increment exists for — the session engine running on the one provider this project's continuous
integration can reach — and it is also the assertion that pins the difference from the stateless
adapter: nothing about the window is supplied by the application.

### Session: A Full Rotation, Seeded From the Consolidated Record

**Test**: `AgentKitAgentsCopilot_Session_RotatesOnTheRuntimesUsage_AndSeedsTheReplacement`

The end-to-end scenario. The runtime reports an occupancy past the rotation threshold; the engine
consolidates out of session, creates a replacement, and releases the session it replaced. The test
asserts every observable consequence together: the rotation happened, the summarizer was given the
turn that preceded it, the replacement's system message is the instructions **and nothing else**, the
consolidated record then arrives on the replacement's first prompt ahead of the caller's own message,
and the superseded session was released exactly once while the replacement was not. It also asserts
the replacement reports nothing occupied — a replacement that inherited its predecessor's figure would
cross the threshold again on adoption and rotate forever.

The two assertions about the record are one scenario because they state one decision: the record is
charged to the conversation rather than to the engine's overhead. Asserting the system message by
**equality** is what rules out a copy being left behind, and asserting the record arrives on the first
prompt, ending with the caller's own message, is what proves a rotation still costs one request rather
than two: the record rides the turn the engine was already taking rather than a priming turn of its
own.

### Session: The Engine's Compaction Configuration Is Carried on Every Session

**Test**: `AgentKitAgentsCopilot_Session_RuntimeCompactionIsHeldClearOfRotationOnEverySessionItBuilds`

Runs a conversation that rotates, so a first session and a replacement are both created, and asserts
**both** carry the engine path's infinite-session configuration rather than the runtime's default.
Asserting only the first would leave a rotation free to hand the conversation back to the runtime's
compactor at the runtime's own threshold, which is precisely the divergence this configuration exists
to prevent and precisely the kind of defect a single-session assertion would miss. The value that
configuration carries — a background-compaction threshold well above the engine's rotation point — is
asserted at the unit level, in *CopilotAgentFactory Unit Verification Design* and
*CopilotProviderSessionFactory Unit Verification Design*, because it is set in one place for all
three engine paths.

### Agent: A Conversation Runs and Streams on the Runtime

**Test**: `AgentKitAgentsCopilot_Agent_RunsAndStreamsOnTheRuntime`

Builds an agent through the factory over a scripted runtime whose turn calls a tool and streams its
answer in two deltas, and runs it streaming. Asserts the one session created carries the
custom-qualified allow-list of the supplied tool; that the updates carry the function call, then the
function result, then text that concatenates to exactly the answer, so the final message did not
repeat what the deltas carried; and that the runtime session was released exactly once when the run
ended. This is the agent an application receives, exercised through the `AIAgent` surface it uses.

### Agent: A Serialized Conversation Resumes Under the Same Confinement

**Test**: `AgentKitAgentsCopilot_Agent_SerializedSession_ResumesUnderTheSameConfinement`

Runs a conversation once on a first agent, serializes it, restores it on a **second** agent and runs
there. Asserts the second agent resumed the very runtime session the first created, and that it did
so under options carrying the custom-qualified allow-list and a default-deny handler that rejects a
built-in request. The confinement is re-applied on resumption rather than trusted to the runtime's
memory of the session, so a conversation that outlives the agent instance that began it is confined
exactly as it was. The unit-level scenarios in *CopilotAgent Unit Verification Design*,
*CopilotTurnChannel Unit Verification Design* and *CopilotAgentFactory Unit Verification Design*
cover the remaining parts of the agent's promise: release after every run, streaming of every event
kind, attachments, serialization of concurrent runs and the refusal of a foreign session.

### Tool Results: Every Session Delivers Results Inline

**Test**: `AgentKitAgentsCopilot_EverySession_DeliversToolResultsInline`

Drives one scripted runtime through every kind of session the package builds — an agent's first run,
its resumption on a second run, a compacting session's creation and a consolidation — and asserts
that every set of options the runtime was handed, three created and one resumed, switches off the
runtime's diversion of a large tool result to a temporary file. A diverted result would reach the
model as a path that no confined tool may read, which turns a large answer into a failure; checking
every path at once is what stops one path being missed.

### Large Tool Outputs: The Readers Are Published, Allow-Listed and Approved on Every Session

**Test**: `AgentKitAgentsCopilot_ToolOutput_ReadersArePublishedAllowListedAndApproved`

Builds the options of an agent's created session, its resumed session and an engine session over a
tool list composed with retention of large outputs. On each it asserts the following:
`tool_output_read` and `tool_output_search` follow the supplied tool in the published definitions
and, as `custom:`-qualified entries, in the allow-list; the default permission handler approves a
custom-tool request for each; and the runtime's own large-output diversion is still switched off.
Retention is the large-output mechanism on Copilot, so the readers must be reachable on every path,
and composing it must not turn the runtime's diversion back on.

### Large Tool Outputs: The Configured Tools Retain and Page Across Runs

**Test**: `AgentKitAgentsCopilot_ToolOutput_ConfiguredToolsDeliverPreviewAndPage`

Runs an agent built over an oversized tool composed with retention twice on the scripted runtime, so
one session is created and then resumed. The scripted runtime replays events but does not invoke
tools. The scenario therefore takes the tools from the options the runtime was handed and invokes
them through the same entry point the runtime would: the oversized tool from the created session's
options, and the reader from the resumed session's. It asserts that the tool returns a bounded
preview naming `output-1` and that the reader returns its first two lines exactly. The agent hands
every run the same tool instances, so a reference outlives the run that produced it.

### Runtime Startup: A Failed Start Is Explained

**Test**: `AgentKitAgentsCopilot_Runtime_FailedStart_IsExplained`

Starts the bundled runtime with a start that fails the way the SDK does when no runtime was
provisioned — an error naming the runtime wrapper it did not find. Asserts the failure reaching the host carries the
runtime's error, names the remedy, a `PackageReference` to `Microsoft.Copilot.SDK`, and keeps a
redacted copy of the original failure, carrying its type name and message, as its inner exception. The pass-through of
a cancellation and the disposal of a
client whose start failed are asserted in *CopilotRuntime Unit Verification Design*.

### Runtime Startup: An Unsupported Installed CLI Is Refused With Upgrade Guidance

**Test**: `AgentKitAgentsCopilot_Runtime_UnsupportedCli_IsRefusedWithUpgradeGuidance`

On a fake Windows machine whose `copilot.exe` on `PATH` prints the version banner of a CLI older than
the minimum, resolving the default launch is refused before any client exists, with the CLI's path,
its version, the minimum version, the `copilot --no-auto-update --version` check, the WinGet upgrade
command and the bundled alternative. The refusal of the real too-old CLI on the recording machine is
part of the live evidence below.

### Runtime Startup: A Runtime That Is Not Signed In Is Refused

**Tests**:

- `AgentKitAgentsCopilot_Runtime_Unauthenticated_IsRefusedWithSignInGuidance`
- `AgentKitAgentsCopilot_Runtime_NoSourceSignsIn_RefusalListsWhatEachSourceFound`

A started runtime whose authentication status reports it is not signed in is refused, with the ways
to sign it in — `copilot login`, `gh auth login` and `GH_TOKEN` among them. With the Copilot CLI
sign-in turned off, no token anywhere and no GitHub CLI on `PATH`, the refusal lists the four sources
in order with what each found — not supplied, none set, turned off by the host, the GitHub CLI not
found on `PATH` — and carries no token.

### Runtime: The Runtime Signs In as the User, on the Source's Host

**Test**: `AgentKitAgentsCopilot_Runtime_SignIn_FollowsTheSourceOrderWithTheSourcesHost`

Verifies `AgentKitAgentsCopilot-Runtime-SignsInAsTheUser`. On a fake machine whose user signed in to
the Copilot CLI on `example.ghe.com`, with a fake helper runtime: a host token with `github.com` wins
over `GH_TOKEN` and gives the runtime `github.com`; `GH_TOKEN` wins over the Copilot CLI sign-in and
hands over no token; with neither, the Copilot CLI's token is used; with the Copilot CLI sign-in
turned off, the runtime's own fallbacks decide; and the helper ran exactly once. The confined
runtime's client options then carry the Copilot CLI's token, `COPILOT_GH_HOST=example.ghe.com`, Empty
mode and AgentKit's data directory, and the helper's home held exactly the account entry, though the
user's configuration also configures an MCP server.

### Runtime: No Token Reaches an Exception

**Test**: `AgentKitAgentsCopilot_Runtime_FailureQuotingAToken_ReachesNoException`

Verifies `AgentKitAgentsCopilot-Runtime-KeepsTokensOutOfFailures`. With a sign-in whose token is not
shaped like a GitHub token, a start failure quoting it, chaining an aggregate of two exceptions that
quote it and a `gho_` token, is explained through `StartClientAsync`; and an agent built with the same
sign-in runs a turn on a scripted runtime that reports a session error quoting both values. Each
failure reaching the host is walked through its whole chain of inner exceptions: no exception carries
either value in its string form, no aggregate survives (each is copied), the start failure's cause is
a redacted copy, and the session error reads `<redacted>` in their places. The redaction of the
provider session's refusals and the model listing's cause is asserted in their unit verification.

### Runtime: The Installed Copilot CLI Is the Default

**Test**: `AgentKitAgentsCopilot_Runtime_DefaultsToTheInstalledCli`

With default options on a fake machine with a supported CLI on `PATH`, the runtime source is the
installed CLI, that CLI is probed with `--no-auto-update --version`, and the client options spawn
exactly that CLI rather than the SDK's bundled runtime.

### Runtime: The Runtime AgentKit Owns Is Confined Before It Starts

**Test**: `AgentKitAgentsCopilot_Runtime_ClientOptionsAreConfined`

Prepares a fresh data directory and builds the client options from a hostile host environment — a
runtime path override, an approve-all switch, a skills directory and an injected MCP configuration —
for both runtime sources, and asserts that the installed-CLI options spawn the resolved CLI and the
bundled options spawn the provisioned runtime (no path), and that both are in Empty mode on a spawned
stdio connection with no arguments, on the prepared data directory, with a replacement environment
that carries no `COPILOT_` or `GITHUB_COPILOT_` variable but keeps `GH_TOKEN`, and with no plugin
directory, session filesystem or extension launcher. This is the client-level half of the
confinement, which no session option can reach, and it is identical for either executable. That the
runtime honors it is shown by the live suite (L11, L14, L15), on both executables.

### Runtime: A Foreign Data Directory Is Refused

**Test**: `AgentKitAgentsCopilot_Runtime_ForeignDataDirectory_IsRefused`

Starts the runtime on a non-empty directory AgentKit did not create, one holding an
`mcp-config.json`, and asserts the start is refused with an `InvalidOperationException` naming the
directory and saying it was not created by AgentKit — before any runtime process exists, so the
directory's configuration is never read.

### Model Selection: The Agent Runs on the Selected Model and Settings

**Test**: `AgentKitAgentsCopilot_ModelSelection_AgentRunsOnTheSelectedModelAndSettings`

Verifies `AgentKitAgentsCopilot-ModelCatalog-ListsTheServicesModels` and
`AgentKitAgentsCopilot-ModelSelection-AppliedToEverySession`. A `CopilotModelCatalog` over a scripted
listing holding the automatic choice, a vision model and a text-only model lists, under
`Vision | ToolCalls`, only the vision model. `SelectAsync` selects it with the effort `high` and the
tier `long_context`, and an agent built with that selection runs one turn on a scripted runtime. The
selection's host capabilities are `Vision`, and the session's options name the model, the effort
`high` and the long-context tier.

## Acceptance Criteria

A system-level test run passes when every scenario above passes without error or exception beyond those explicitly
asserted. Any allow-list that diverges from the published tools or carries an unqualified entry, any tool named like a
built-in that reaches the runtime, any session opened with a discovered MCP server left enabled on its options or
accepted while one stays enabled, any supplied tool that is rejected, any unlisted or built-in request that is approved
on either path, any missing runtime that is accepted, any runtime client options not confined as asserted for either
runtime source, any default that does not run the installed CLI, any unsupported CLI or unauthenticated runtime that is
accepted or refused without its guidance, any sign-in source that wins out of order or with another source's host, any
helper home holding more than the account entry, any refusal that omits a source's finding or carries a token, any
exception reaching the host, or cause it keeps, that carries a token the sign-in knows, any
foreign data directory that is accepted, any occupancy figure that is not
the one the runtime reported, any rotation that fails to seed the consolidated record, any part of a seeded record
appearing in a session's system message, any rotation that costs more than the one request the engine was already
making, any superseded session left unreleased, any session created with the runtime's own compaction left at the
runtime's default threshold, any agent run that is not confined, streamed or released as asserted, any resumption under
a different confinement, any session that diverts a large tool result, any failed start whose explanation omits the
runtime's error or the remedy, any reader of a retaining composition missing from a session's published tools, its
custom-qualified allow-list or the requests its default handler approves, or any configured retaining tool that does not
deliver its preview and the page it names constitutes a failure.
So does a model listed without a capability the application required, the automatic choice listed, or a
session configured without the chosen model, effort or tier.

## Supplementary Live Verification

The traced scenarios above stop at the seam. The opt-in live Copilot suite crosses it: it runs the
package's confinement, its agent and AgentKit's session engine against the **real** Copilot runtime
and a real model, and asserts what the runtime actually did. It re-measures, on every run, the
runtime properties the package's design depends on — which tools a confined session exposes, which
names the runtime reserves for its built-ins, what the permission handler is asked, how large and
image results are delivered, whether an agent's conversation streams and resumes, whether the host's
Copilot variables reach the runtime AgentKit starts, whether an ambient MCP server is disabled on a
session opened through the package's channel, which MCP sources actually start on the runtime
AgentKit owns, and whether agent definitions and the other ambient channels reach such a session.

**It is supplementary evidence and is not linked to ReqStream.** The suite never runs in the pipeline
that builds the trace matrix — that pipeline is hermetic and holds no credential — so a requirement
traced to a live test would have no result there and would fail enforcement. The requirement evidence
therefore stays with the hermetic tests above and in the unit chapters; the live suite is evidence
that what those tests assert about the options is what the runtime honors.

### Live Suite Location and Gating

- **Project**: `test/DemaConsulting.AgentKit.Agents.Copilot.LiveTests/`, an xUnit v3 project targeting
  .NET 10 that references the Copilot package, AgentKit's core and tools packages, and
  `Microsoft.Copilot.SDK` directly, so the bundled runtime can be provisioned beside it for the
  bundled variant; the installed-CLI variants build it with `CopilotSkipCliDownload` set, proving they
  need no provisioned runtime. It is **not** part
  of `AgentKit.slnx`, so `build.ps1` and a `dotnet test` of the solution never build or run it. The
  merge gate compiles it, so an internal change that breaks it fails the pull request, but never runs
  it.
- **Gate**: every test calls `Assert.SkipUnless` on the environment variable
  `AGENTKIT_COPILOT_LIVE_MODEL`, which also names the model every scenario uses. Unset, the fixture
  starts no runtime and every test reports itself skipped, naming the variable, so an accidental run
  costs nothing.
- **Log**: the optional environment variable `AGENTKIT_COPILOT_LIVE_LOG_DIRECTORY` names the folder a
  `live-*.log` of the run is written to, recording what the runtime reported and decided, including
  the runtime source, the CLI path, and the version and protocol the started runtime reported.
- **Runtime selection**: `AGENTKIT_COPILOT_LIVE_RUNTIME` chooses `installed` (the default, as for a
  host) or `bundled`; any other value fails the fixture. For the installed CLI,
  `AGENTKIT_COPILOT_LIVE_CLI_PATH` names the CLI to run; unset, discovery chooses it exactly as for a
  host. Every runtime and client the suite starts runs that one executable.
- **Fixture**: one shared `CopilotRuntime` per run, started exactly as a host starts one — Empty
  mode, the selected runtime spawned with the host's Copilot variables withheld bar the
  pass-through set, a data directory AgentKit created under the run's temporary root —
  signed in from the user's sources in AgentKit's order: in CI from `GH_TOKEN`/`GITHUB_TOKEN`, on a
  developer machine usually as the Copilot CLI account, else through the GitHub CLI; the run's log
  records the source, host and login. A sandbox folder holds a `notes.txt` carrying the codeword `PERIWINKLE-42` and a
  generated PNG split blue and yellow; a file outside the sandbox carries a second codeword no confined
  tool may return. The tests run sequentially in one collection, to control cost and because one
  scenario changes the process environment. The ambient-server scenario, L12, starts a second,
  short-lived client of its own per test in CodingAgent mode — the hostile client a host could have
  built before AgentKit owned the runtime, reached through the channel's internal seam — spawned on the
  run's executable with the same withheld environment, whose data directory under the run's
  temporary root configures one non-GitHub MCP server whose command does not exist — never the user's
  own Copilot home. The agent-definitions scenario, L13, starts one more such client whose data
  directory under the run's root defines a user agent, and gives its session a workspace under the
  run's root that defines a project agent. The MCP-source scenarios, L14c to L14f, each start a
  `CopilotRuntime` of their own on a data directory under the run's root, which they stop, change and
  start again — AgentKit accepts it because it marked it — and configure a **marker probe**: an MCP
  server whose command appends a line to a marker file under the run's root and then waits. The
  marker is the observation that separates a server the runtime executed from one it merely
  configured; each prevention scenario is paired with a control showing the same probe does execute
  without the protection.

### Running the Live Suite

**Manually**, from the repository root in PowerShell. `COPILOT_CLI_PATH` no longer needs to be unset:
the runtime AgentKit starts never receives it, discovery ignores it, and L11 sets a bogus value
itself. Unsetting it is still harmless, and is what the recorded runs did.

On the installed CLI — the machine's own, found by discovery, or another named by path:

```pwsh
Remove-Item Env:COPILOT_CLI_PATH -ErrorAction SilentlyContinue
$env:AGENTKIT_COPILOT_LIVE_MODEL = 'claude-haiku-5.5'
$env:AGENTKIT_COPILOT_LIVE_RUNTIME = 'installed'
# Optional: $env:AGENTKIT_COPILOT_LIVE_CLI_PATH = 'C:\path\to\copilot.exe'
dotnet build test/DemaConsulting.AgentKit.Agents.Copilot.LiveTests -c Release -p:CopilotSkipCliDownload=true
dotnet test --project test/DemaConsulting.AgentKit.Agents.Copilot.LiveTests -c Release --no-build `
    --report-trx --results-directory artifacts/live/installed
```

On the bundled runtime, build without the property, so the SDK provisions its runtime into the
suite's output, and set `AGENTKIT_COPILOT_LIVE_RUNTIME` to `bundled`.

The suite signs in as AgentKit does for a host, so on a machine signed in to the Copilot CLI it runs
as that account. When that account does not offer the model named, choose a low-cost model it does
offer (the catalog scenario, L16, logs the account's models).

To try a CLI version without touching the machine's own installation, download the standalone CLI
release archive for the platform (`gh release download v<version> --repo github/copilot-cli`), check it
against the release's `SHA256SUMS.txt`, extract it to a scratch folder and name it with
`AGENTKIT_COPILOT_LIVE_CLI_PATH`. Point `LOCALAPPDATA` (Windows) or `XDG_CACHE_HOME` (elsewhere) at
a scratch folder in that shell only, **after** the build: the CLI unpacks its package into that cache,
which the user's interactive `copilot` shares, and an unpacked newer version there would change what
the user's own CLI runs. Delete the scratch folders afterwards.

**In CI**, by the `live-copilot-suite` job of `.github/workflows/live_samples.yaml`. It runs only when
the workflow is dispatched and on its weekly schedule, never on a pull request or an ordinary push.
It runs on `ubuntu-latest`, authenticates from the `COPILOT_GH_TOKEN` secret, and uses the dispatch's
`model` input or `claude-haiku-5.5` by default. When no credential is configured it reports that it
was skipped and succeeds, so it cannot fail a repository that holds no credential. It is a matrix of
three variants, none of which stops the others: `installed-minimum` installs the standalone CLI at
the minimum version AgentKit supports, `installed-latest` the latest release — the early warning for
a CLI that has moved to a protocol this release does not speak — and both put it on `PATH` and let
discovery find it; `bundled` runs the SDK's provisioned runtime. Each installed variant verifies the
downloaded archive against the release's checksums.

**Evidence**: each CI variant uploads `artifacts/live/` as the workflow artifact
`live-copilot-suite-<variant>`, holding the TRX test results and the run's `live-*.log`. A manual run
leaves the TRX in `artifacts/live/` too, and the log beside it when
`AGENTKIT_COPILOT_LIVE_LOG_DIRECTORY` points there.

### Live Scenarios

Every test name carries the prefix `AgentKitAgentsCopilotLive_`, omitted in the table. Thirty-five tests cover
twenty-four scenarios; the image-delivery scenario, L8, is two tests, the agent scenario, L10, is three, the
ambient-server scenario, L12, is four, and the MCP-source scenario, L14, is six. L16 to L18 are in
`CopilotModelCatalogLiveTests`, and L-S1 to L-S5 in `CopilotSignInLiveTests`.

| # | Test | Recorded result |
| --- | --- | --- |
| L0 | `Runtime_ReportsASupportedRuntime` | Pass (installed and bundled) |
| L1 | `Confinement_EmptyModeCustomAllowList_ExposesOnlySuppliedTools` | Pass |
| L2 | `Confinement_BareAllowList_WouldAdmitBuiltIns` | Pass (admitted `powershell`, `view`) |
| L3 | `BuiltInToolNames_AreAllReserved` | Pass (15 Windows built-ins) |
| L4 | `CollisionGuard_ToolNamedLikeABuiltIn_IsRefusedBeforeSessionCreation` | Pass |
| L5 | `CustomTool_ReadsInsideAndIsDeniedOutside` | Pass |
| L6 | `DefaultDeny_OnlyCustomToolRequestsAreApproved` | Pass |
| L7 | `LargeOutput_ThirtyKilobyteResult_IsDeliveredInline` | Pass |
| L8 | `ImageDelivery_ImageReadResult_ReachesTheModel` | Pass |
| L8b | `DiagramRender_ImageReachesTheModel` | Pass (installed; added 2026-11-22) |
| L9 | `Usage_RotationOnTheRuntimesUsage_PreservesTheCodeword` | Pass (3 rotations) |
| L10a | `Agent_Streams_TextDeltas` | Pass |
| L10b | `Agent_SerializedSession_ResumesOnANewAgent` | Pass |
| L10c | `Agent_ImageAttachment_ReachesTheModel` | Pass |
| L11 | `Runtime_AmbientCopilotEnvironment_DoesNotReachTheRuntime` | Pass |
| L12a | `Confinement_AmbientMcpServerOnACodingAgentClient_IsDisabled` | Pass |
| L12b | `Confinement_AmbientMcpServerWithoutTheChannel_IsEnabled` | Pass |
| L12c | `Confinement_AmbientMcpServerEnabledAtCreation_IsDisabledByTheSeal` | Pass |
| L12d | `Confinement_AmbientMcpServerOnAResumedSession_IsDisabled` | Pass |
| L13 | `Confinement_AgentDefinitionsOnACodingAgentClient_AreWithheld` | Pass |
| L14a | `OwnedRuntime_BuiltInGitHubMcpServer_IsNeverStarted` | Pass (no server started) |
| L14b | `OwnedRuntime_BuiltInGitHubMcpServer_StartsWhenNotNamed` | Pass (connected; not listed) |
| L14c | `OwnedRuntime_UserMcpServerInItsDataDirectory_IsNeverExecuted` | Pass (no marker) |
| L14d | `OwnedRuntime_UserMcpServerWithoutTheChannel_IsExecuted` | Pass (marker written) |
| L14e | `OwnedRuntime_WorkspaceMcpServer_IsNeverExecuted` | Pass (no marker) |
| L14f | `OwnedRuntime_WorkspaceMcpServerWithConfigDiscovery_IsExecuted` | Pass (marker written) |
| L15 | `OwnedRuntime_AmbientChannels_AreAbsent` | Pass |
| L16 | `Catalog_ListsFiltersAndRefreshes` | Pass (installed and bundled; 24 entries; `auto` skipped) |
| L17 | `Selection_SessionRunsOnTheChosenEffortAndTier` | Pass (installed and bundled; limits 100,000 and 872,000) |
| L18 | `Selection_UnofferedEffort_IsRefusedBeforeAnySession` | Pass (installed and bundled) |
| L-S1 | `SignIn_DefaultOptions_UseTheCopilotCliAccount` | Pass (installed; GitHub Enterprise Cloud account) |
| L-S2 | `SignIn_GitHubCliHidden_StillUsesTheCopilotCliAccount` | Pass (installed) |
| L-S3 | `SignIn_ExplicitTokenAndHost_AreUsed` | Pass (installed; without the host: refused with the host hint) |
| L-S4 | `SignIn_TokenProvider_SignsInTheRuntimeAndItsSessions` | Pass (installed; 2 calls: start, session) |
| L-S5 | `SignIn_NoSource_IsRefusedListingEachSource` | Pass (installed) |

What each asserts. Assertions are made on the runtime's events and metadata wherever possible; the
few that rest on the model's prose are marked.

- **L0**: the fixture's runtime reports the protocol version this release supports and a version;
  on the installed CLI it reports the CLI it runs — the named one when a path was given — and a
  version at least the minimum, read from the started runtime; on the bundled runtime it reports no
  CLI path.
- **L1**: a session built from the package's confinement reports, through the runtime's own account
  of its current tools, exactly the supplied tools and no built-in.
- **L2**: a control. The same options with bare names in the allow-list admit a built-in sharing a
  listed name, proving the custom-tool qualifier is load-bearing.
- **L3**: every built-in tool the runtime lists for the model on this platform is in the package's
  reserved-name set, so a runtime that adds a built-in is caught here rather than by an application.
- **L4**: the factory refuses a tool named `view` with `ArgumentException`; the control, a session
  configured by hand with that tool, is refused by the runtime with an error naming `view`.
- **L5**: on an engine session, an AgentKit tool's result contains the sandbox codeword, a read
  outside the sandbox returns a denial, and the outside codeword is never returned.
- **L6**: every approval the default handler gives is for a custom-tool request naming a supplied
  tool; the runtime raises such a request for every custom-tool call.
- **L7**: a tool result of thirty kilobytes, over the runtime's diversion limit of 20,480 bytes,
  reaches the model inline, complete to its end marker, rather than as a temporary file reference.
- **L8**: an image an AgentKit tool returns is delivered to the model as binary content; the answer
  names blue and yellow (prose).
- **L8b**: a picture `diagram_render` draws from inline Mermaid source, in the image preview
  `DiagramPack.For(Vision)` chooses and with the pack's `Instructions` placed in the session's
  instructions, is delivered to the model as binary content; the answer names the last node,
  `Charlie` (prose).
- **L9**: a `CompactingAgentSession` with a 2,500-token window ceiling rotates at least once on the
  runtime's own usage figures, the figures are positive, and the final answer recalls the codeword
  (prose).
- **L10a**: a tool call streams as a function call followed by text deltas, and the answer carries the
  codeword.
- **L10b**: a conversation serialized from one agent and restored on a new agent instance resumes the
  runtime session and recalls a fact from before the serialization (prose).
- **L10c**: an image passed to the agent as inline data reaches the model as an attachment; the answer
  names blue and yellow (prose).
- **L11**: with a bogus `COPILOT_CLI_PATH` and `COPILOT_ALLOW_ALL` set in the process environment,
  the control — an in-process Empty client, the SDK's default, which inherits the environment — fails
  to start with an error naming the bogus folder, while a second `CopilotRuntime` started under the
  same environment starts and opens a confined session exposing exactly the supplied tools. Both
  variables are restored afterwards.
- **L12a**: on a CodingAgent-mode client whose data directory configures a non-GitHub MCP server —
  which the runtime's discovery is first asserted to report, so the scenario cannot pass on a fixture
  the runtime ignores — a session opened through the package's production channel named the server
  in its disabled servers, reports no MCP server enabled in its effective configuration, and exposes
  exactly the supplied tools.
- **L12b**: a control. The same options handed to the same kind of client directly, bypassing the
  channel, leave the server enabled, proving the channel's disabling of the server is load-bearing.
- **L12c**: the production channel's opening protocol with its discovery step replaced by one that
  finds nothing, so the server is not on the create-time disabled list. The server is asserted
  enabled in the listing taken just before the production seal runs, and after the seal it is listed
  disabled, no MCP server is enabled, and the session exposes exactly the supplied tools — so the
  seal's own session-scoped disable turned it off. The server's command does not exist, so this shows
  the runtime's enablement withdrawn and re-listed, not a running process terminated.
- **L12d**: a session created on the same kind of client directly, with the server listed enabled,
  takes one short turn so the runtime persists it, and is released; it is then resumed through the
  production channel's resume path. The resumed session keeps its identifier, the server is named in
  the resume options' disabled servers, no MCP server is enabled in its effective configuration, and
  it exposes exactly the supplied tools.
- **L13**: on a CodingAgent-mode client whose data directory defines a user agent, with a workspace
  that defines a project agent — both of which the runtime's own discovery is first asserted to
  report, so the scenario cannot pass on a fixture the runtime ignores — a session opened through the
  package's production channel lists no agent, built-in sub-agents included, and exposes exactly the
  supplied tools, so no agent can be selected or delegated to. No model turn is taken.
- **L14a**: on the fixture's runtime, a tool-free session opened through the production channel takes
  one turn. The runtime's loaded-servers event lists the built-in GitHub MCP server as disabled, no
  status event reports any server pending or connected, and no configured server is enabled. Because
  no server of any source started, this is also the evidence available for managed-catalog and
  account servers — for the account the run uses.
- **L14b**: a control for L14a. The same session with the built-in server no longer named disabled,
  created directly, connects the GitHub MCP server on its first turn, so Empty mode alone does not
  prevent it; and the session's configuration listing does not report the server while it is
  connected, which is why the seal cannot be relied on for it. A failure of the listing assertion
  means the runtime now lists built-in servers, and the documentation's statement of that gap should
  be revisited.
- **L14c**: a runtime whose data directory gained an `mcp-config.json` probe after AgentKit created
  it. A session opened through the production channel takes one turn; the probe's marker never
  appears within thirty seconds, the probe was named on the create-time disabled list and is listed
  disabled from the user source, and no server reaches pending or connected.
- **L14d**: a control for L14c. The same configuration, with the session created from AgentKit's own
  options but without the channel, executes the probe: Empty mode and the session options alone do
  not prevent a user server.
- **L14e**: a workspace whose `.mcp.json` configures a probe, on a runtime whose data directory was
  changed to trust that workspace. A session opened through the channel with that working directory
  takes one turn; the marker never appears within thirty seconds, no configured server is enabled and
  no server reaches pending or connected.
- **L14f**: a control for L14e. The same trusted workspace, with configuration discovery switched back
  on and the session created directly, executes the probe.
- **L15**: a session opened through the channel on the fixture's runtime lists no plugin, skill,
  instruction source or agent, and no enabled MCP server, and its system-prompt attribution is
  smaller than a control session's whose system message keeps the environment-context section. Hooks
  have no listing; they are covered by repository hooks being off, no installed plugin and a fresh
  data directory.
- **L16**: the catalog lists the service's models through the runtime's uncached query, the same
  count as the client's cached listing, with the automatic choice skipped; the vision filter matches
  the service's own vision flags; and a refresh reads again.
- **L17**: a session created on the selected model in each context tier, with a chosen effort,
  answers, and the runtime reports a usage limit for it. The limit and the catalog's tier window are
  both logged; the limit is the tier's prompt budget, not the tier's total window. See
  *CopilotModelCatalog Unit Design*, "Context Tiers and the Reported Limit".
- **L18**: an effort the model does not offer is refused by selection, with the reason listing the
  efforts it does offer, before any session exists.
- **L-S1**: with no token variable set, the fixture's runtime — started with default options, as a
  host starts one — reports `CopilotSignInSource.CopilotCli` and the host and login of the account in
  the user's Copilot CLI `config.json`, and answers a turn. Skipped when the user is not signed in to
  the Copilot CLI or a token variable is set.
- **L-S2**: with the GitHub CLI hidden from the runtimes the scenario starts (`PATH` without its
  folder, `GH_CONFIG_DIR` an empty folder under the run's root, for that start only), a new runtime
  still signs in as the Copilot CLI account. The user's own GitHub CLI sign-in is never changed.
- **L-S3**: the Copilot CLI account's token, obtained in-process through the same helper and never
  logged, passed as `gitHubToken` with the account's host as `gitHubHost`, signs the runtime in as
  `GitHubToken` on that host. For a GitHub Enterprise Cloud account the same token without the host
  must be refused, with the host hint and not the token; a runtime that starts with it fails the
  scenario. For a `github.com` account the no-host case does not apply and is logged as such.
- **L-S4**: a token provider returning that token, with the account's host, signs the runtime in as
  `GitHubTokenProvider`, is asked exactly once at start, and is asked again for the session a turn
  runs on — so a client-level token from the provider and a session-level provider work together —
  every request for the account's host.
- **L-S5**: with the Copilot CLI sign-in turned off, the token variables cleared and the GitHub CLI
  hidden, the start is refused with the four sources listed in order with their findings, and the
  message holds no `gho_`, `ghu_`, `ghp_`, `ghs_`, `ghr_` or `github_pat_` text and not the token.
  The secret checks run before any assertion that would echo the message.

Every L-S scenario clears `COPILOT_GITHUB_TOKEN`, `GH_TOKEN` and `GITHUB_TOKEN` in the process environment for its own
runtimes and restores them, and logs hosts, logins, sources, and token and refusal lengths only. A refusal's text is
never logged, and is checked for secrets with assertions that do not echo it before any assertion that would. L-S1 to
L-S4 need a Copilot CLI sign-in and skip without one, as in CI; L-S5 runs everywhere. They have been run on Windows with
the installed CLI only; macOS, Linux and the bundled runtime as the helper are untested. A token refresh (`IsRefresh`
true) was not observed live, because a session does not outlive its token in a run; it is covered by the unit tests.

### Live Acceptance Criteria

A live run passes when every test that is not skipped passes. A run in which every test was skipped because the gate
variable was unset is not evidence of anything and is not recorded as a pass. A fixture that refuses to start the
selected runtime fails every test with the refusal; on the installed variants that is the version check or discovery
working as designed against a machine without a supported CLI, and the refusal's text, which the fixture writes to the
run's log as "Runtime start refused: …", is the evidence. A failure of L0 means the runtime that started is not the one
selected or not a supported version. A failure of L1, L2, L5 or L6 means a confined session is not confined on the real
runtime; of L3 or L4, that the reserved-name set or the guard no longer matches the runtime; of L7, L8 or L8b, that a
tool's result does not reach the model intact; of L9, that the session engine
does not hold on the runtime's own usage; of L10a to L10c, that the agent does not stream, resume or deliver attachments
as designed; of L11, that a host's Copilot variable reaches the runtime AgentKit starts, or that the control no longer
fails, so L11 proves nothing; of L12a, that an ambient MCP server survives the confinement; of L12b, that the fixture no
longer injects one, so L12a proves nothing; of L12c, that the seal's session-scoped disable does not take effect on the
real runtime; of L12d, that the resume-time disabled list is not honored on a session resumed through the channel; of
L13, that an agent definition or built-in sub-agent reaches a confined session; of L14a, L14c or L14e, that an MCP
server of that source starts on the runtime AgentKit owns, or, for L14a, that some server of any source started — a
renamed built-in server, or a managed-catalog or account server for the run's account; of L14b, L14d or L14f, that the
probe no longer runs without the protection, so the paired scenario proves nothing; of L15, that an ambient channel
reaches a session; of L16, that the catalog cannot list the service's models for the signed-in user, or lists the
automatic choice; of L17, that a chosen effort or tier is not accepted by the runtime, or that no limit is reported
for it; of L18, that an unoffered effort reaches a session; of L-S1 or L-S2, that the Copilot CLI sign-in is not used,
or is used on another host or account; of L-S3 or L-S4, that a host's token or token provider, or its host, is not
used as given, or that a session does not ask the provider, or, for a GitHub Enterprise Cloud account, that L-S3's
token without its host is not refused; of L-S5, that a refusal omits a source's finding or carries
a secret. Any such failure is investigated before a release.

### Recorded Run

The suite was run manually on 2026-11-20, on Windows 11 (build 26100), with the model `claude-haiku-5.5`. All 13 tests
then in the suite passed, in 1 minute 48 seconds. Two behaviors for which the design kept a fallback in reserve held on
the real runtime: a released session resumed on a new agent instance, and an in-memory image attachment reached the
model.

When the turn channel gained its confining opening protocol, the scenarios that go through it were
re-run the same day, on the same machine and model, after the change: L12a and L12b, which are new,
and L9 and L10a to L10c, whose sessions are opened and resumed through the protocol. All six passed.
L12a's discovery reported the probe server from the user source, enabled; the session opened through
the channel listed it disabled and exposed exactly the two supplied tools, while L12b's session,
created directly, listed it enabled. L9 rotated three times. The other scenarios open no session
through the channel and were not re-run.

When L12c and L12d were added, the four ambient-server tests were run again on 2026-11-20, on the
same machine and model, filtered to `*Confinement_AmbientMcpServer*`; all four passed, in 52
seconds. Discovery reported the probe server from the user source, enabled, in every test. L12a's
session named it on the create-time disabled list and listed it disabled; L12b's, created directly,
listed it enabled. L12c's create-time disabled list held only the built-in GitHub server; the
listing taken before the seal showed the probe enabled, and the listing after it showed the probe
disabled. L12d's directly created session listed the probe enabled; its first attempt to resume a
session that had taken no turn was refused by the runtime with "Session not found", so the scenario
takes one short turn before releasing it, after which the resume through the channel named the
probe on the disabled list and listed it disabled. Every session opened or resumed through the
channel exposed exactly the two supplied tools.

When L13 was added and the client starter, since replaced by `CopilotRuntime`, came to report the runtime's error and to
blame `COPILOT_CLI_PATH` only when that error names the variable's location, L13 and L11 were run on 2026-11-20, on the
same machine and model, filtered to `*AgentDefinitions*` and `*ClientStarter*`; both passed, in 10 seconds. L13's
discovery reported the user agent from the user source and the workspace agent from the project source; the session
opened through the channel listed no agent, built-ins included, and exposed exactly the two supplied tools. L11's
runtime error named the three paths it searched in the folder `COPILOT_CLI_PATH` points into, so the explanation carried
that error and named the variable, its value and the fix.

When AgentKit came to start and own the runtime, the whole suite was run once on 2026-11-20, on the
same machine and model, with `COPILOT_CLI_PATH` removed from the shell first; all 25 tests passed, in
5 minutes 14 seconds, every one of them on a `CopilotRuntime` or, for L12 and L13, on a CodingAgent
client spawned with the same withheld environment. The table above shows this run's results; L2
admitted `powershell` and `view`, L3 listed 15 Windows built-ins, and L9 rotated three times, as in
the earlier runs. What the MCP-source scenarios observed:

- **Built-in GitHub server.** On L14a's session the loaded-servers event listed `github-mcp-server`
  from the builtin source as disabled, the turn answered, and no status event reported any server
  pending or connected, so no server of any source started for this account. On L14b's session,
  with the name removed, the server went pending and then connected three seconds later, from the
  builtin source, during the first turn — and the session's configuration listing taken while it was
  connected was empty.
- **User source.** L14c's discovery reported the probe from the user source; the session named it on
  the create-time disabled list, the loaded-servers event listed it disabled from the user source,
  the listing showed it from the user source with enablement false, and no marker appeared in the
  thirty seconds after a turn. In L14d the probe went pending at session creation and its marker was
  present when creation returned; the runtime reported it failed some thirty seconds later, when the
  probe exited without completing a handshake, and creation waited for that.
- **Workspace source.** L14e's session listed no configured server; the loaded-servers event listed
  the probe as disabled with **no** source — the entry for a name on the disabled list that no loaded
  configuration supplies, which is what configuration discovery being off leaves — and no marker
  appeared in the thirty seconds after a turn. In L14f, with configuration discovery on, the probe
  went pending at session creation from the workspace source and its marker was present when
  creation returned.
- **Other channels.** L15's session listed no plugin, skill, instruction source, agent or configured
  server, and its system prompt was attributed 3,252 tokens against 3,464 for the control that keeps
  the environment context.
- **Host environment.** L11's in-process control failed with `FFI runtime library not found`, naming
  the paths it searched in the bogus folder, while the `CopilotRuntime` started under the same
  environment opened a session exposing exactly the two supplied tools.

Nothing else started where it was observed: in the scenarios that record MCP status events, L14a to L14f, the only
servers that reached pending or connected were the probe or the built-in server of a control scenario (L14b, L14d,
L14f); the other scenarios do not record those events. Managed-catalog and account servers could not be configured for
the run, so for those sources the evidence is L14a's observation that none started for this account. After this run the
agent scenarios' cleanup moved from the client's session deletion to `CopilotRuntime.DeleteSessionAsync`, which forwards
to the same SDK call. After the branch was rebased onto large tool output retention, the whole suite was
run again on 2026-11-21, on the same machine and model, with `COPILOT_CLI_PATH` removed from the shell first and no
filter; all 25 tests passed, in 5 minutes 22 seconds, including L10a to L10c, so the agent scenarios' cleanup through
`CopilotRuntime.DeleteSessionAsync` held on the real runtime.

When the runtime came to sign in from the user's Copilot CLI sign-in, the whole suite was run on 2026-11-22, on the same
machine, installed CLI, with `COPILOT_CLI_PATH` removed from the shell first and no filter; all 34 tests passed. The
machine's Copilot CLI is signed in to a GitHub Enterprise Cloud host and the GitHub CLI to that host and `github.com`,
with no token variable set, and the fixture's runtime now signed in as the Copilot CLI account on its host. That account
does not offer `claude-haiku-5.5` (a session on it is refused with "Model is not available"), so the run used its
lightweight, low-cost `gpt-5.6-luna`. L-S1 and L-S2 reported `CopilotCli` on the account's host; L-S3 reported
`GitHubToken` on that host, and the same token without the host was refused with the host hint; L-S4 reported
`GitHubTokenProvider` with two provider calls, one at start and one for the session's first token, both for the
account's host; L-S5 was refused with the four sources listed. Before and after the run, the names, users, last-written
times and sizes of the machine's credential-store entries for GitHub and Copilot were identical, and no temporary
sign-in folder was left behind.

When L-S3 came to require the refusal of the token without its host, and L-S3 and L-S5 to check a refusal for secrets
before any assertion that echoes it and to log only its length, the two were re-run on 2026-11-22, on the same machine,
installed CLI 1.0.114 and model `gpt-5.6-luna`, with `COPILOT_CLI_PATH` removed from the shell first, filtered to those
two tests; both passed. The account was on the machine's GitHub Enterprise Cloud host, as before. L-S3 signed in as
`GitHubToken` on that host, and the same token without the host was refused with the host hint and without the token;
L-S5 was refused listing all four sources and carrying no secret.

Before the helper was built, it was confirmed on the same machine that the confined runtime cannot read the credential
store itself: an Empty-mode runtime on a home holding only the account entry, with the variable that disables the system
keychain set to `0` in its replacement environment and the GitHub CLI hidden from `PATH`, reported `Not authenticated`
(with the GitHub CLI visible it signed in through it, on `github.com`), while the same home in CodingAgent mode reported
the account signed in through its stored sign-in. With the GitHub CLI visible to the CodingAgent-mode runtime, its
account list failed to deserialize, which is why the helper hides the GitHub CLI from its `PATH`.

When the installed Copilot CLI became the default runtime, the suite — now 26 tests with L0 — was run
on 2026-11-21, on the same machine and model, once on each runtime source, with `COPILOT_CLI_PATH`
removed from the shell first and each run's log written to its own folder through
`AGENTKIT_COPILOT_LIVE_LOG_DIRECTORY`. Before the runs, the machine's WinGet installation reported
1.0.114 both to `copilot --version` and to `copilot --no-auto-update --version`.

- **Installed CLI, found by discovery.** Built with `CopilotSkipCliDownload` set, so the output held no
  bundled runtime, and run with `AGENTKIT_COPILOT_LIVE_RUNTIME` set to `installed` and no CLI path.
  Discovery chose the WinGet `copilot.exe` under
  `%LOCALAPPDATA%\Microsoft\WinGet\Packages\GitHub.Copilot_…`, the first on `PATH`, ahead of the
  Visual Studio Code launcher that follows it. All 26 tests passed, in 8 minutes 17 seconds. L0
  reported that CLI, version 1.0.114 and protocol 4; the start, including discovery and the version
  probe, took 4 seconds. L3 listed 15 Windows built-ins; L9 rotated three times; L11's in-process
  control failed with `FFI runtime library not found` on the bogus folder while the owned runtime
  exposed exactly the two supplied tools; L15 listed no plugin, skill, instruction source or agent,
  with a system prompt of 3,252 tokens against 3,461 for the control.
- **Bundled runtime.** Built without the property, so the SDK provisioned its runtime into the
  suite's output, and run with `AGENTKIT_COPILOT_LIVE_RUNTIME` set to `bundled`. All 26 tests passed,
  in 6 minutes 23 seconds; L0 reported no CLI path, version 1.0.114 and protocol 4, and the start took
  1 second. L3, L9, L11 and L15 observed the same as on the installed CLI, with 3,463 tokens for L15's
  control.
- **Refusal of a CLI older than the minimum.** The standalone CLI 1.0.106 release for Windows, from a
  scratch folder, named with `AGENTKIT_COPILOT_LIVE_CLI_PATH`, with `LOCALAPPDATA` pointed at a
  scratch folder in that shell only, so the CLI unpacked its package there rather than into the
  user's cache; only L0 was run. The probe, including that first unpacking, took 37 seconds, and the
  start was refused before any runtime started. The fixture's log recorded: "Runtime start refused:
  InvalidOperationException: The Copilot CLI at '…\106\copilot.exe' is version 1.0.106, which is too
  old for AgentKit: it needs version 1.0.113 or later, which speaks SDK protocol version 4. AgentKit
  starts the CLI with --no-auto-update, which runs the version built into the installed executable;
  check that version with 'copilot --no-auto-update --version'. A newer version that auto-update or
  'copilot update' downloaded is kept in the CLI's package cache, and 'copilot --version' can report
  it, but AgentKit does not run it; only upgrading the installation itself changes the version
  AgentKit runs. Upgrade the installation itself: …", followed by the upgrade command per
  installation method (`winget upgrade GitHub.Copilot`, `brew upgrade --cask copilot-cli`,
  `npm install -g @github/copilot@latest`, the install script again) and the `cliPath` and
  `CopilotRuntimeSource.Bundled` alternatives. The elided folder is the scratch folder.
- **Refusal of the last version before the minimum.** Re-recorded later on 2026-11-21 with a stand-in
  console program, `copilot.exe`, built in a scratch folder, that prints `GitHub Copilot CLI 1.0.112.`
  and exits, named the same way; only L0 was run. The start was refused 0.2 seconds after it began,
  before any runtime started, with the same text naming the stand-in's path and version 1.0.112.
- **Refusal of a batch file.** An empty `copilot.cmd` in a scratch folder, named the same way; only L0
  was run. The start was refused when the options were built, before any process started, with
  "ArgumentException: The Copilot CLI path '…\copilot.cmd' is a batch file, which Windows runs through
  cmd.exe; AgentKit runs the Copilot CLI without a shell. Pass the full path of copilot.exe - such as
  the one CopilotCli.FindInstalled() returns - or, for an npm installation, the package's
  npm-loader.js (under %APPDATA%\npm\node_modules\@github\copilot), which AgentKit runs with node.
  (Parameter 'cliPath')". The file was never run.
- **The version that runs.** Measured beforehand with scratch caches, leaving the user's cache
  untouched: the standalone 1.0.106 executable, with a cache that already held an unpacked 1.0.114,
  reported 1.0.106 to `--no-auto-update --version` and 1.0.114 without the switch — the newest cached
  package. The machine's WinGet executable had itself been upgraded earlier that day, so its built-in
  version was 1.0.114 and both commands reported 1.0.114. The installation, not a downloaded package,
  decides the version AgentKit runs.
- **Earlier evidence.** Earlier on 2026-11-21, before the refusal text took its present wording and
  with the same discovery, probe and launch code, the standalone CLI 1.0.113 release (checked against
  the release's `SHA256SUMS.txt`), named by `AGENTKIT_COPILOT_LIVE_CLI_PATH` with a scratch
  `LOCALAPPDATA`, passed all 26 tests, and `@github/copilot` 1.0.113 installed with npm into a scratch
  prefix and named by its `npm-loader.js`, which AgentKit probes and the SDK runs under `node`, passed
  L0 and L1, reporting version 1.0.113 and protocol 4 and exactly the two supplied tools.

Afterwards the scratch folders, including those of the re-recorded refusals, were deleted, and the
machine's `copilot --version` and `copilot --no-auto-update --version` still reported 1.0.114 and its
package cache held the same versions as before the runs. Later runs of the CI job record their results
in the `live-copilot-suite-<variant>` artifacts of each workflow run.

When the model catalog was added, before the installed CLI became the default, its three tests, L16 to L18, were run on
2026-11-21 on the same machine, on the bundled runtime, the only source then, with the model `claude-haiku-5.5` and SDK
2.0.1; only those three were run that day, and all three passed. The uncached query and the client's cached listing both
returned 24 entries, including the automatic choice, which the catalog skipped. The catalog described `claude-haiku-5.5`
with tier windows of 228,000 (`default`) and 1,000,000 (`long_context`). Sessions created at the effort `low` reported a
usage limit of 100,000 in `default` and 872,000 in `long_context`: the prompt budget the runtime enforces, which is
smaller than the tier's total window. This is the observation recorded under "Context Tiers and the Reported Limit" in
*CopilotModelCatalog Unit Design*. The effort `not-an-effort` was refused with "Model 'claude-haiku-5.5' does not
support reasoning effort 'not-an-effort'. Supported: low, medium, high, xhigh, max."

After the branch was rebased onto the installed CLI default, L16 to L18 were run again on 2026-11-22,
on the same machine and model, on the installed CLI found by discovery: built with
`CopilotSkipCliDownload` set, run with `AGENTKIT_COPILOT_LIVE_RUNTIME` set to `installed`, no CLI path
and `COPILOT_CLI_PATH` removed from the shell first, and filtered to `CopilotModelCatalogLiveTests`.
The fixture started the WinGet `copilot.exe`, version 1.0.114 and protocol 4. All three tests passed,
in 36 seconds. The uncached query and the client's cached listing again both returned 24 entries,
and the catalog skipped the automatic choice. It described `claude-haiku-5.5` as usable, with tool
calls, vision and reasoning, tier windows of 228,000 (`default`) and 1,000,000 (`long_context`), and
the efforts `low`, `medium`, `high`, `xhigh` and `max`. Sessions created at the effort `low` reported
a usage limit of 100,000 in `default` and 872,000 in `long_context`, as on the bundled runtime, and
`not-an-effort` was refused with the same message before any session existed.

L8b was added on 2026-11-22 and run alone on the same machine with `claude-haiku-5.5`, with
`AGENTKIT_COPILOT_LIVE_RUNTIME` set to `installed` and discovery choosing the CLI. It passed in 43
seconds: the tool result carried the drawn picture as binary content for the model, and the answer
named the node labels.

**Platform coverage.** Every recorded live run of the owned runtime ran on Windows; the spawned
runtime has not yet run live on Linux, and discovery's macOS and Linux locations are verified only by
the hermetic tests. The `live-copilot-suite` job, which runs on `ubuntu-latest` weekly and on
dispatch, gives the first Linux evidence, including discovery of a CLI on `PATH`, and records it in
its artifacts. The hermetic
tests, including those for the client options, the withheld environment and the data-directory
marking, run in the CI matrix on Linux.
