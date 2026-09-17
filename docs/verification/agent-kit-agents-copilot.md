# System Verification Design

This document describes the system-level verification strategy for the AgentKitAgentsCopilot system.

## Verification Approach

The AgentKitAgentsCopilot system is verified through system-level integration tests that exercise the
factory's session-configuration path, its permission handling, and a whole compacting conversation
**without a live Copilot connection and without any credential**. The safety-critical property of
the original increment — that the session allow-list is derived from the same collection as the
published tools, so the two cannot drift apart — is asserted directly against a constructed session
configuration, which is a plain constructable object requiring no client. The default permission
handler is exercised by invoking it with constructed permission requests and asserting on the
decisions it returns, on the agent path and on the session path alike.

**A real `CompactingAgentSession` is run to a rotation.** The session engine, the rotation engine and
a summarizer are the genuine ones; what stands in for the runtime is a scripted turn channel that
replays the SDK's **own** session event objects into the adapter's own event handler and answers with
the SDK's own assistant-message type. So the seeding, the occupancy accounting, the consolidation and
the ownership discipline are all exercised end to end, offline.

**Why that is possible at all, and where the boundary is.** The SDK's session and client types are
sealed with non-public constructors and no virtual members, so neither can be faked, subclassed or
constructed in a test. AgentKit therefore reaches them through one internal seam,
`ICopilotTurnChannel`, whose production implementation forwards two calls and owns one disposal; see
_CopilotTurnChannel Unit Verification Design_, which states plainly what that leaves unexercised. The
SDK's event types, by contrast, are all constructable, so nothing about the event shapes is
simulated.

**What is out of automated scope, stated honestly.**

- A live end-to-end Copilot run is not automated. The agent-construction call and the session
  lifecycle calls reach an authenticated Copilot CLI, and the runtime's actual suppression of a shell
  or file-editing tool at execution time is therefore outside automated scope. The offline evidence
  is that the allow-list-carrying session configuration is built correctly and that the permission
  handler adjudicates as required — the two places where the suppression is decided.
- **Whether the runtime honors a request to disable its own infinite-session compaction is verified,
  by manual measurement against the live runtime, and the answer is no.** Three sessions were run on
  SDK 1.0.11, on one model (`gpt-5.4-mini`), each filled with the same roughly fifty thousand tokens:

  | Session configuration | Compaction events observed |
  | --- | --- |
  | `Enabled = false`, background threshold 0.05 — crossed | **2 — it compacted anyway** |
  | `Enabled = false`, background threshold 0.50 — not crossed | 0 |
  | `Enabled = false` alone, at the runtime's default threshold of 0.80 — never reached | 0 |

  The enablement flag is ignored and the background-compaction threshold is honored: a session
  compacts exactly when its threshold is crossed, whatever the flag says. AgentKit therefore raises
  the threshold to 0.95 on every session its engine drives, a quarter of the window clear of the
  engine's rotation point at 0.70, and relies on that. The flag is still set, as a statement of
  intent and in case the runtime ever begins honoring it, but nothing depends on it. The events
  observed were `SessionCompactionStartEvent` and `SessionCompactionCompleteEvent`, which are the
  event types the adapter's observer already matches on — so the detection path is confirmed working
  against the live runtime too. This measurement is not automated and is not re-run by the suite: it
  is a property of the runtime, recorded here because the design depends on it.
- **Whether a live conversation ever crosses 0.95 before the engine rotates it at 0.70 is
  unverified.** The threshold is a margin, not a guarantee, and the runtime's last-resort behavior is
  deliberately left in place. The session observer watches for the runtime's own compaction and
  truncation events, and the provider session refuses the next turn if either arrives — before a turn
  is sent as well as after one returns. What the tests prove is that the raised threshold is carried
  on every session and that the refusal fires; what only a live run can settle is whether the refusal
  ever needs to.
- **Whether the model weights a seeded record delivered as one conversation message as it would
  weight the turns it replaces is unverified**, and unverifiable offline. The tests prove the record
  is rendered, ordered and fenced exactly as designed, that every entry kind a rotation produces
  survives it, and that it reaches the model on the conversation channel rather than the system one.
- **Whether a live runtime emits its usage event before it goes idle** is unverified. The adapter
  refuses a turn that reported no usage, so a runtime that reported occupancy only after going idle
  would surface as a refusal rather than as a wrong figure — a diagnosable failure rather than a
  silent one, but a failure a live run should be used to rule out.
- The runtime's own delivery of a tool-returned image (the reason the image-promoting decorator is
  deliberately absent) was verified in the spike and is not re-proven here.

System tests reside in `AgentKitAgentsCopilotTests.cs`, with the scripted runtime in
`FakeCopilotTurnChannel.cs`, both within the `DemaConsulting.AgentKit.Agents.Copilot.Tests` project.
The runtime's other injection channels — its skills and its discovered custom instructions — are
closed on the same session configuration and are verified at the unit level against the built
configuration, where the two values are set.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. **No Copilot CLI is started, no credential is used, and no network
  access is made.** The session configuration and the permission requests are plain constructable
  objects, and the runtime session is reached only through the package's own internal turn-channel
  seam
- **Mocking**: A hand-written scripted turn channel replaying the SDK's own event types; no mocking
  framework
- **File system**: None
- **Isolation**: Each test constructs its own tools, configuration, runtime and session; no state is
  shared

## System-Level Test Scenarios

### Tool Suppression: The Allow-List Is Derived From the Supplied Tools

**Test**: `AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools`

The safety-critical scenario. Builds the session configuration from a set of supplied tools and
asserts the available-tools allow-list is exactly the names of the published tools, in the same
order, and that the published tool set is the same size — the two derived from one collection. A
drift here would silently re-admit a built-in tool an application meant to withhold.

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
exists for, enforced at the permission boundary as well as in the allow-list.

### Permission Handling: A Built-In Tool Is Rejected on the Session Path Too

**Test**: `AgentKitAgentsCopilot_Session_BuiltInToolRequest_IsRejectedOnTheSessionPath`

The same assertion against the configuration a **rotation** produces. It is stated separately
because the session path was added after the agent path and takes no host-supplied handler at all: a
session that was confined only by inheriting the agent path's code, without a test saying so, would
be confined by accident rather than by design.

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

The two assertions about the record are one scenario because they state one decision. Untrusted
material — a record carries tool results, and a tool result may be the contents of a file the agent
was pointed at — is kept out of the provider's highest-trust channel and delivered on the
conversation channel instead. Asserting the system message by **equality** is what rules out a copy
being left behind, and asserting the record arrives on the first prompt, ending with the caller's own
message, is what proves a rotation still costs one request rather than two: the record rides the turn
the engine was already taking rather than a priming turn of its own.

### Session: The Engine's Compaction Configuration Is Carried on Every Session

**Test**: `AgentKitAgentsCopilot_Session_RuntimeCompactionIsHeldClearOfRotationOnEverySessionItBuilds`

Runs a conversation that rotates, so a first session and a replacement are both created, and asserts
**both** carry the engine path's infinite-session configuration rather than the runtime's default.
Asserting only the first would leave a rotation free to hand the conversation back to the runtime's
compactor at the runtime's own threshold, which is precisely the divergence this configuration exists
to prevent and precisely the kind of defect a single-session assertion would miss. The value that
configuration carries — a background-compaction threshold well above the engine's rotation point — is
asserted at the unit level, in _CopilotAgentFactory Unit Verification Design_ and
_CopilotProviderSessionFactory Unit Verification Design_, because it is set in one place for all
three engine paths.

## Acceptance Criteria

A system-level test run passes when every scenario above passes without error or exception beyond
those explicitly asserted. Any allow-list that diverges from the published tools, any supplied tool
that is rejected, any unlisted or built-in request that is approved on either path, any occupancy
figure that is not the one the runtime reported, any rotation that fails to seed the consolidated
record, any part of a seeded record appearing in a session's system message, any rotation that costs
more than the one request the engine was already making, any superseded session left unreleased, or
any session created with the runtime's own compaction left at the runtime's default threshold
constitutes a failure.
