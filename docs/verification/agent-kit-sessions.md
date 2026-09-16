# AgentKitSessions System Verification Design

This document describes the system-level verification strategy for the AgentKitSessions system.

## Verification Approach

AgentKitSessions is verified through deterministic end-to-end conversations against the shipped
`InMemoryProviderSession`, the divergent-tokenizer provider fake, and `FakeSummarizer`. The tests do
not contact a live provider or a model. They prove the redesigned compaction core by observing the
public session behavior, the provider sessions created during rotation, and the seed history carried
into each replacement session.

The redesigned core is verified as a rotation system: a session keeps a configurable verbatim tail,
then retains older context in a fixed round-robin shape of three tiers with four slots per tier. The
unit tests cover the internal rules, and the system tests cover the observable promise that a long
conversation keeps answering, rotates on both provider shapes, and preserves important early detail.

The high-pressure cases are part of system verification. `DivergentTokenizerProviderSession` reports
usage at 1x, 2x and 3x this library's estimate so the same session behavior is exercised when a
provider counts the seeded context differently. Those tests prove rule five terminates and reports
pressure instead of silently churning when the provider's tokenizer diverges.

Compaction pressure is reported as `CompactionLevel.Low`, `CompactionLevel.Medium` or
`CompactionLevel.High`. The compacting session adapts that level with hysteresis: near-repeat
rotations escalate the level, while a long quiet stretch relaxes it.

The package surface is also verified mechanically. `PublicSurfaceTests.cs` asserts the assembly
exports exactly the deliberate list of public types, and that the compaction-core internals — the
layout, the tiers, the transcript and the rotation engine — are not among them, so the API shape is
checked against the built assembly rather than maintained only by prose. The list is asserted rather
than a count: a count is a metric, and pinning one pressures whoever comes next toward the number
instead of the design, which is how a genuinely useful type ends up hidden to keep a total down.

System tests reside in `AgentKitSessionsTests.cs`, `CompactingAgentSessionTests.cs`,
`PublicSurfaceTests.cs` and `XmlDocExampleTests.cs`, with helpers in `FakeSummarizer.cs`,
`SessionTestData.cs`, `DivergentTokenizerProviderSession.cs` and `ProviderTestDoubles.cs`.

## Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider or model is contacted
- **File system**: None for the session scenarios; transcripts and provider sessions are in memory
- **Models**: None; consolidation is performed by deterministic summarizer fakes
- **Isolation**: Each test constructs its own provider factory, summarizer, options and session

## Acceptance Criteria

A system-level test run passes when every scenario below passes without error or exception beyond
those explicitly asserted. Any session that stops answering as the conversation grows, any rotation
that leaks a superseded provider session, any provider-shape path that mixes usage currencies, any
pathological compaction that fails to terminate, any missing dropped-material signal, any lost early
detail, or any public-surface drift constitutes a failure.

## Test Scenarios

### Long Conversation: The Session Outlives the Provider's Window

**Test**: `AgentKitSessions_LongConversation_RotatesRepeatedlyAndKeepsAnswering`

Runs forty turns against a small reporting provider window and asserts every turn returns text and
that at least one rotation occurred. The scenario proves the system's headline behavior: a compacting
agent session continues to answer after accumulated history exceeds the provider window.

### Rotation: Replacement Sessions Preserve Operation

**Test**: `AgentKitSessions_LongConversation_RotatesRepeatedlyAndKeepsAnswering`

The same long conversation also verifies rotation at the system boundary. The provider factory
creates replacement sessions as compaction occurs, the live session remains usable after each
replacement, and rotation is observable through `RotationCount`.

### Tiered Retention: Early Detail Survives Many Rotations

**Test**: `AgentKitSessions_AfterManyRotations_EarlyDetailIsStillCarriedInContext`

Places a distinctive marker in the first turn, drives many later turns and rotations, and asserts
the live replacement session still carries that marker in its seed history. The deterministic
summarizer preserves marker facts while reducing routine padding, so the test verifies the
round-robin retention structure rather than model memory.

### Fitting Strategy: Divergent Tokenizers Keep Answering

**Tests**:

- `AgentKitSessions_LongConversation_RotatesRepeatedlyAndKeepsAnswering`
- `CompactingAgentSession_DivergentTokenizer_KeepsAnsweringAndTerminates`
- `CompactingAgentSession_DivergentTokenizer_RotatesMoreOftenAndEscalatesHigher`
- `CompactingAgentSession_AfterAQuietStretch_RelaxesTheCompactionLevel`
- `CompactingAgentSession_TightWindow_EscalatesToHighAndReportsDroppedMaterial`

These tests cover the redesigned fitting strategy. A normal long conversation keeps answering, and a
provider fake at 1x, 2x and 3x tokenizer divergence completes every turn. Divergence is then verified
as a difference rather than asserted away: a 2x and 3x provider rotates strictly more often and
escalates to a strictly higher level than a 1x one, each collapsing and failing if reverted to 1x.
Adapting is verified in both directions — the level comes back down after a quiet stretch, so a
session that met one busy period does not pay for it in fidelity thereafter. Separately, a window too
small to hold a full structure escalates to `CompactionLevel.High` and reports `MaterialDropped`.
Together they verify escalate-until-it-fits and drop-until-it-fits without predicting in mixed
currencies.

### Out-of-Session Summarizer: Consolidation Is Deterministic

**Test**: `AgentKitSessions_AfterManyRotations_EarlyDetailIsStillCarriedInContext`

Uses an injected summarizer outside the live provider session. The marker-preserving fake records
consolidated material deterministically, proving the system can verify retention without asking the
provider to summarize itself.

### Tool Traffic: Whole Turns Are Indivisible

**Test**: `SessionTranscript_AppendTurn_GroupsEntriesAsOneTurn`

Verifies the transcript records a user message, tool traffic and assistant answer as one turn. Since
all boundaries are turn-granular, a tool call and its result are retained, consolidated or dropped
together.

### Compaction Reporting: Level and Dropped Material Are Visible

**Test**: `CompactingAgentSession_TightWindow_EscalatesToHighAndReportsDroppedMaterial`

Drives a window too small to hold a full structure, so compaction must become aggressive and then
discard history, using a non-divergent provider that counts with this library's own estimator. The
response stream is asserted to include `CompactionLevel.High` and `MaterialDropped`, which are the
application-visible signals that compaction pressure is high and history was discarded.

### Provider Neutrality: Both Provider Shapes Compact

**Test**: `AgentKitSessions_SameConversation_CompactsOnBothProviderShapes`

Runs the same conversation against a provider that reports usage and one that reports none. The
first path records `ContextUsageOrigin.Provider`; the second records `ContextUsageOrigin.Estimated`.
This verifies the engine can compact both provider shapes while keeping each occupancy comparison in
one currency.

### In-Memory Verification: No Live Model Is Required

**Test**: `AgentKitSessions_LongConversation_RotatesRepeatedlyAndKeepsAnswering`

Exercises the full session lifecycle through the shipped in-memory provider. The test proves the
same provider offered to application authors can demonstrate creation, turns, rotation and disposal
without network access.

### Public Surface: Exported Types Are Mechanical Evidence

**Tests**:

- `AgentKitSessions_PublicSurface_IsExactlyEighteenTypes`
- `AgentKitSessions_PublicSurface_ExcludesDeletedAndInternalTypes`

Reflects over the built assembly and asserts the public surface is exactly eighteen types. The
companion test asserts deleted or internal compaction-core types are not exported, keeping the
redesigned API boundary verifiable.

### Documented Examples: Every Published Example Compiles

**Test**: `AgentKitSessions_XmlDocExamplesCompile`

Compiles every shipped XML documentation example against the real API. A stale example that names a
removed member or omits the new response signals fails here before a consumer follows it.

## Platform Verification

The system's platform requirements are evidenced by running the long-conversation system test in the
CI platform matrix. That scenario exercises transcript recording, usage reporting, rotation,
consolidation, provider replacement and disposal, so a platform-specific failure in the session
lifecycle would fail the evidence run.
