## CopilotSummarizer Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotSummarizer` class.

### Verification Approach

`CopilotSummarizer` is verified through unit tests that call it exactly as the rotation engine does —
hand it a `ConsolidationRequest` and take the record — against a scripted channel opener that
contacts nothing, records every configuration it was handed and every prompt it was sent, and counts
releases.

The summarizer is constructed over the internal channel-opener seam, for the reason
_CopilotTurnChannel Unit Verification Design_ records: the SDK's session and client types are sealed
with non-public constructors and no virtual members, so a consolidation cannot be exercised at all
unless the opening of a session is injectable.

**The prompt is asserted against Core's own composition, not against a substring.** A summarizer that
sent the material without the instruction, or the instruction without the aggressiveness clause,
would still contain every word a substring check looked for — and would quietly ask a Copilot model
for something other than what every other provider is asked for. Comparing against
`ConsolidationPrompt.Compose` is what makes the assertion say "exactly the prompt this library
publishes".

**Two properties are asserted through the configuration rather than the answer**, because that is
where they are decided: the session carries no tools and an empty allow-list, and it carries the
engine path's infinite-session configuration rather than the runtime's default. Both are invisible
from the record that comes back.

**What is out of automated scope, stated honestly.** No live consolidation is performed, so nothing
here proves a Copilot model produces a useful record from the prompt — that is a quality judgment a
live run makes, and the engine already treats a poor or absent record as material to keep rather than
material to lose. What is proven is that the right prompt is sent, on a session confined as designed,
and that the session is released whatever happens.

Unit tests reside in `CopilotSummarizerTests.cs`, with the scripted runtime in
`FakeCopilotTurnChannel.cs`, both within the `DemaConsulting.AgentKit.Agents.Copilot.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Copilot CLI is started, **no credential is used**, and no network
  access is made
- **Mocking**: A hand-written scripted channel opener; no mocking framework
- **File system**: None
- **Isolation**: Each test constructs its own runtime and summarizer; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any prompt that is not the composed consolidation prompt, any consolidation
session carrying a tool or a non-empty allow-list, any session built with the runtime's own
compaction left at the runtime's default, any session reused between consolidations, any session left
unreleased on either the successful or the failing path, any empty answer turned into an exception or
into invented text, or any session opened for a consolidation that was already canceled constitutes a
failure.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotSummarizer-SendsTheConsolidationPrompt: Exactly the Library's Prompt

**Tests**:

- `CopilotSummarizer_Consolidate_SendsTheComposedConsolidationPrompt`
- `CopilotSummarizer_Consolidate_NamedModel_IsCarried`

Consolidates one request and asserts the session received exactly one prompt, equal to Core's
composition of that request, and that the record came back unchanged. The second asserts a named
model is carried onto the consolidation session, so an application can run consolidation on a smaller
and cheaper model than the conversation — which is the point of naming it separately.

#### AgentKitAgentsCopilot-CopilotSummarizer-CarriesNoTools: Nothing to Call, Nothing Allowed

**Tests**:

- `CopilotSummarizer_Consolidate_CarriesNoToolsAndAnEmptyAllowList`
- `CopilotSummarizer_Consolidate_HoldsTheRuntimesOwnCompactionClearOfRotation`

Asserts the configuration carries no published tools, an empty allow-list, disabled skills and
skipped custom instructions. A consolidation is a pure function from material to a record of it;
there is nothing for a tool to do, and a tool that could act would be acting outside everything the
conversation's own confinement was reasoned about. This is also the scenario that proves the
tool-free configuration path exists and is used — the agent path refuses an empty tool list, and a
summarizer that had quietly fallen back to it would carry whatever tools it was given.

The second asserts a consolidation session takes the engine path's infinite-session configuration too,
rather than the runtime's default: a consolidation is one prompt and one answer, and a runtime that
reshaped the material mid-consolidation would produce a record of something other than what it was
given. The threshold that configuration carries is asserted where it is set, in _CopilotAgentFactory
Unit Verification Design_.

#### AgentKitAgentsCopilot-CopilotSummarizer-RunsOutsideTheCompactedSession: A Session of Its Own, Each Time

**Test**: `CopilotSummarizer_Consolidate_RunsOutOfTheSessionBeingCompacted`

Performs two consolidations, as two rotations would, and asserts two sessions were opened and each
received exactly one prompt. A consolidation sent through the live conversation would spend the very
context it exists to reclaim and would itself count toward the occupancy that triggered the rotation
— the measured reason the session contract takes a summarizer at all. Asserting a _fresh_ session
each time also pins the other half: a cached session would make the second consolidation's material
follow the first's, so a consolidation would stop being a pure function of what it was given.

#### AgentKitAgentsCopilot-CopilotSummarizer-EmptyAnswerIsAnEmptyRecord: Silence Is a Refusal, Not a Fault

**Test**: `CopilotSummarizer_Consolidate_EmptyAnswer_IsAnEmptyRecord`

Scripts a session that goes idle without producing an assistant message and asserts an empty record
comes back rather than an exception. The engine already treats a consolidation it could not obtain as
material to keep rather than material to lose, and a model declining to answer is a thing that
happens. Note the deliberate asymmetry with the provider session, which refuses the same condition: a
silent turn loses the user's question, while a silent consolidation loses only an optimization the
engine has a documented fallback for.

#### AgentKitAgentsCopilot-CopilotSummarizer-ReleasesItsSession: Released on Both Paths

**Tests**:

- `CopilotSummarizer_Consolidate_Succeeds_DisposesTheSession`
- `CopilotSummarizer_Consolidate_Fails_DisposesTheSession`
- `CopilotSummarizer_Consolidate_NullRequest_Throws`
- `CopilotSummarizer_Consolidate_Canceled_OpensNothing`
- `CopilotSummarizer_Constructor_NullClient_Throws`

The first two are the scenarios that matter, and the **second** is the one a leak hides on: a
consolidation that failed has still left a session on the runtime, and a long conversation performs
one of these per rotation, so a session leaked there would accumulate for exactly as long as the
conversation this exists to prolong. Both assert exactly one release.

The remainder are error paths. A missing request is refused where the engine composed it; a
consolidation canceled before it began opens nothing, so a canceled rotation leaves nothing behind on
the runtime; and a missing client is refused where the application composed its provider.
