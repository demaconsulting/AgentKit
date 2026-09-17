## CopilotProviderSessionFactory Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotProviderSessionFactory`
class.

### Verification Approach

`CopilotProviderSessionFactory` decides everything about a Copilot session at the moment it is
created, because that is the only moment Copilot accepts configuration — the confinement, the model,
the raised runtime compaction threshold, the registered event handler and the instructions the
session is governed by. All of it is therefore verified by asserting on the `SessionConfig` the
factory produced, which is a plain constructable object requiring no client and no credential.

**The seeded conversation record is asserted separately, because it is no longer part of that
configuration.** It is composed by `ComposeHistoryPreamble` for the session's first message to carry,
so the tests assert on what that composes and, on the same configuration, that the system message
holds the application's instructions and nothing else. Asserting both halves in one test is
deliberate: the property under test is *where the record is charged*, and a test that only looked at
the record would pass against an implementation that also left a copy in the system message.

The assertions are made through `BuildProviderSessionConfig`, an internal seam exposed for exactly
this reason — the same reason `CopilotAgentFactory.BuildSessionConfig` is exposed — and through
`ComposeHistoryPreamble`, internal for the same purpose. The ownership and lifecycle behavior is
verified instead through `CreateAsync` over a scripted channel opener that records every
configuration it was handed and every channel it opened, and counts releases.

**The record is asserted as an exact string, not by substring.** A rendering that reordered
the entries, dropped a label or lost a tool identifier would still contain every word a substring
check looked for. Composing the expected text from Core's own transcript-line rendering is what makes
the assertion say "this exact record, in this exact order" — and it is why Core's renderer is reused
rather than a second one written. The opening and closing lines are fixed text, so the expected
string includes them exactly as written.

**The ownership window is exercised, not argued.** The scripted opener deliberately does *not*
observe the cancellation token: a real create request can complete and return a session at the moment
the caller's token is canceled, and that is the one window in which a session exists that nothing yet
owns. Modeling the opener as refusing instead would make the window unreachable, and the guard
untestable.

**What is out of automated scope, stated honestly.** No live session is created, so nothing here
proves the runtime accepts the configuration or applies the allow-list. How the runtime treats the
infinite-session configuration is not left open, however: it was settled by manual measurement and is
recorded in *AgentKitAgentsCopilot System Verification Design*, which reports that the enablement flag
is ignored and the background-compaction threshold is honored. That is why the scenario below asserts
the threshold rather than the flag. What is proven here is that the configuration this factory hands
to the runtime is the one the design says it should be, on every session a rotation creates, and that
no part of a seeded record reaches it. Whether a model reading a record delivered as one conversation
message weights it as it would weight the turns it replaces is unverifiable offline, and is stated as
such rather than implied.

Unit tests reside in `CopilotProviderSessionFactoryTests.cs`, with the scripted runtime in
`FakeCopilotTurnChannel.cs`, both within the `DemaConsulting.AgentKit.Agents.Copilot.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Copilot CLI is started, **no credential is used**, and no network
  access is made
- **Mocking**: A hand-written scripted channel opener; no mocking framework
- **File system**: None
- **Isolation**: Each test constructs its own tools, seed, runtime and factory; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any allow-list that diverges from the seeded tools, any session built with the
runtime's skills enabled or its custom instructions admitted, any session built without the
default-safe permission handler, any session built with the runtime's own compaction left at the
runtime's default threshold, any part of a seeded record appearing in a session's system message,
any seeded record rendered in the wrong order or with an entry lost, any record composed for an
empty history, any session created without the event handler registered, any session opened for a
creation that was already canceled, or any opened session left unreleased when the creation failed
constitutes a failure.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotProviderSessionFactory-DerivesAllowListFromSeededTools: The Session Path

**Tests**:

- `CopilotProviderSessionFactory_BuildSessionConfig_AvailableToolsDerivedFromSeededTools`
- `CopilotProviderSessionFactory_BuildSessionConfig_SuppressesSkillsAndCustomInstructions`

The safety-critical assertion, repeated on the session path. Builds the configuration a rotation
produces and asserts the allow-list is exactly the names of the published tools, in the same order,
and that the runtime's two other injection channels are closed. It is asserted here as well as on the
agent path because the session path is reached by a different entry point and takes no host-supplied
handler: a session that was confined only by inheriting the agent path's code, with no test saying
so, would be confined by accident.

#### AgentKitAgentsCopilot-CopilotProviderSessionFactory-DefaultSafePermissions: Default-Safe on Every Rotation

**Test**: `CopilotProviderSessionFactory_BuildSessionConfig_InstallsTheDefaultSafePermissionHandler`

Invokes the handler the configuration carries with a request naming a seeded tool and with a built-in
request, and asserts the first is approved and the second rejected. There is no host present to
adjudicate a prompt on a session the engine drives, so the default must be safe without asking.

#### AgentKitAgentsCopilot-CopilotProviderSessionFactory-HoldsRuntimeCompactionClearOfRotation: A Quarter-Window Margin

**Test**: `CopilotProviderSessionFactory_BuildSessionConfig_HoldsTheRuntimesOwnCompactionClearOfRotation`

Asserts the configuration carries the infinite-session setting deliberately, with a
background-compaction threshold above 0.90 rather than the runtime's default of 0.80 — where AgentKit
rotates at 0.70, a tenth of the window below it. The threshold is what is asserted because it is what
the runtime honors; the enablement flag is asserted alongside it only because the configuration still
states the intent. The corresponding assertion that *every* session of a rotating conversation carries
this configuration is at the system level.

#### AgentKitAgentsCopilot-CopilotProviderSessionFactory-RendersTheSeededHistoryAsALabeledRecord: The Record, Exactly

**Tests**:

- `CopilotProviderSessionFactory_Seed_CarriesHistoryOnTheFirstMessageNotTheSystemMessage`
- `CopilotProviderSessionFactory_BuildSessionConfig_SeedWithoutHistory_CarriesInstructionsOnly`
- `CopilotProviderSessionFactory_BuildSessionConfig_BareSeed_CarriesNoSystemMessage`
- `CopilotProviderSessionFactory_Preamble_HistoryWithoutInstructions_CarriesTheRecord`
- `CopilotProviderSessionFactory_Preamble_SeededToolResult_IsRenderedAsALabeledRecord`

The first seeds from the shape a rotation actually produces — a consolidated record, a user message
and an answer — and asserts the **entire** composed preamble as one exact string: the fixed opening
line, each entry rendered to its transcript line in order, and the fixed closing line.

The next three are the boundaries. A seed with no history composes no record at all, so the first
message of such a session is sent exactly as the caller wrote it and no record claims a conversation
that never happened. A seed with neither instructions nor history carries no system message at all. A
seed with history but no instructions still composes the record — an application that configures no
instructions still rotates, and a rotation that dropped the history there would silently restart the
conversation — while its configuration carries no system message, which is the same assertion from
the other side.

The last is the scenario the rendering was chosen for. It seeds a tool call and its result and
asserts both survive as labeled lines carrying their shared identifier. The defect it guards against
is the one that shipped to review on the stateless path: a seeded tool result rendered under a role a
provider's wire mapping discards without an error. Here the shape is unrepresentable — a labeled line
inside one message cannot be dropped without dropping the message — but asserting the material is
present and paired is what proves the rendering did not solve the problem by dropping it.

#### AgentKitAgentsCopilot-CopilotProviderSessionFactory-KeepsTheRecordOutOfTheSystemChannel: Instructions Only

**Tests**:

- `CopilotProviderSessionFactory_Seed_CarriesHistoryOnTheFirstMessageNotTheSystemMessage`
- `CopilotProviderSessionFactory_BuildSessionConfig_SeedWithoutHistory_CarriesInstructionsOnly`
- `CopilotProviderSessionFactory_BuildSessionConfig_BareSeed_CarriesNoSystemMessage`
- `CopilotProviderSessionFactory_Preamble_HistoryWithoutInstructions_CarriesTheRecord`

The accounting, asserted rather than argued. The first test seeds a full rotation's history and
asserts the configuration's system message is **equal to** the application's instructions — not that
it contains them — and that it is still appended to the runtime's own prompt rather than replacing
it. Equality is what makes the scenario meaningful: a containment check would pass against an
implementation that appended the record after the instructions, which is precisely the design this
one replaced.

The remaining three close the boundaries from the other side. A seed with instructions and no history
carries those instructions and no record opening anywhere in them. A seed with neither carries no
system message at all. A seed with history and no instructions carries **no system message at all**
while still composing its record, which is the sharpest statement of the rule: there is no
arrangement of a seed under which the history reaches the configuration.

#### AgentKitAgentsCopilot-CopilotProviderSessionFactory-CreatesSeededSessions: The Observer, Before the Session

**Tests**:

- `CopilotProviderSessionFactory_CreateAsync_RegistersTheObserverBeforeCreation`
- `CopilotProviderSessionFactory_BuildSessionConfig_ModelIsCarriedOrLeftToTheRuntime`

The first asserts a handler is present on the configuration the runtime was asked to create a session
from, **and** that a first turn's usage reading reaches the session. Asserting the handler alone
would pass for a registration made after creation, which is precisely the arrangement that loses the
first turn's events; asserting the reading is what makes the ordering observable.

The second asserts a named model is carried and that naming none leaves the session's model at
whatever a freshly constructed session carries — which is what makes the parameter purely additive.

#### AgentKitAgentsCopilot-CopilotProviderSessionFactory-LeavesNothingUnowned: Nothing Opened Is Orphaned

**Tests**:

- `CopilotProviderSessionFactory_Constructor_NullClient_Throws`
- `CopilotProviderSessionFactory_Constructor_NullOpener_Throws`
- `CopilotProviderSessionFactory_CreateAsync_NullSeed_Throws`
- `CopilotProviderSessionFactory_CreateAsync_Canceled_OpensNothing`
- `CopilotProviderSessionFactory_CreateAsync_CanceledWhileOpening_ReleasesTheOpenedSession`

Error paths. A missing client or opener is refused where the application composed its provider; a
missing seed is refused where the rotation asked. The cancellation scenarios assert the two states
apart: a creation canceled before the request opens **nothing**, so a canceled rotation costs no
session on the runtime; a cancellation that arrives while the request is in flight leaves a session
the runtime holds, and the test asserts that session is released exactly once rather than orphaned.
The second is the scenario the guard exists for and is the reason the scripted opener does not refuse
a canceled token itself.
