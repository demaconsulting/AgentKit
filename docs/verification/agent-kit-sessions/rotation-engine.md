## RotationEngine Unit Verification Design

This document describes the unit-level verification strategy for `RotationEngine` and its rotation
outcome.

### Verification Approach

`RotationEngine` is verified entirely without a model. Each scenario supplies `FakeSummarizer`,
which returns deterministic records and records every `ConsolidationRequest`. That makes the engine
a reproducible function of the input layout, compaction level, verbatim-turn setting and occupancy
allowance.

The tests cover the redesigned rotation rules. Rule one appends whole turns to the verbatim tail in
the session. Rule two consolidates everything older than the level-adjusted verbatim tail into one
tier-one slot. Rule three consolidates a full tier's slots as peers into the next tier. Rule four
keeps the coarsest tier as a ring. Rule five escalates the compaction level, then drops oldest slots
and turns until the seed fits. Oversized summarizer input is chunked at entry boundaries before it is
consolidated.

Pathological cases are first-class evidence. Tests use an expanding summarizer, a single turn larger
than the allowance and a blank summarizer answer to prove termination and normalization under
conditions that must not regress.

Unit tests reside in `RotationEngineTests.cs`, with helpers in `FakeSummarizer.cs` and
`SessionTestData.cs`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider or model is contacted
- **Mocking**: A hand-written deterministic summarizer fake; no mocking framework
- **Isolation**: Each test builds its own layout and summarizer

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any rotation that splits turns incorrectly, consolidates into the wrong tier,
reuses a turn more than once per tier, grows the coarsest tier, hands oversized material to the
summarizer whole, fails to terminate under expansion or oversized turns, stores a null answer, stores
a blank slot, or ignores cancellation constitutes a failure.

### Test Scenarios

#### AgentKitSessions-RotationEngine-AgesOnlyOverflowingTiers: Coarse Tiers Age and Ring

**Tests**:

- `RotationEngine_Rotate_FullTier_ConsolidatesAsPeersIntoNextTier`
- `RotationEngine_Rotate_CoarsestTier_IsARing`

These tests verify the coarse-tier aging rules. A full tier is consolidated as peer slots into the
next tier and then cleared for the arriving slot. The coarsest tier behaves as a ring and remains at
its fixed slot count when a cascade reaches it.

#### AgentKitSessions-RotationEngine-HonorsTheTriggerCurrency: Oversized Material Is Chunked

**Test**: `RotationEngine_Rotate_OversizedMaterial_IsChunked`

Builds material larger than the summarizer input allowance and asserts the summarizer is called more
than once while the rotation still produces one tier-one slot. This verifies chunking of oversized
summarizer input.

#### AgentKitSessions-RotationEngine-FoldsOverflowIntoTiers: Older Turns Become One Tier-One Slot

**Test**: `RotationEngine_Rotate_ConsolidatesOlderIntoOneTierOneSlot`

Splits the layout at the level-adjusted verbatim tail, consolidates everything older into one slot,
and appends that slot to tier one. The newest turns remain verbatim and the request carries the low
aggressiveness instruction.

#### AgentKitSessions-RotationEngine-CascadesDegradation: A Turn Is Consolidated Once Per Tier

**Test**: `RotationEngine_ManyRotations_ConsolidatesOncePerTier`

Drives enough rotations for material to reach all three tiers and asserts consolidation requests
occur at tier one, tier two and tier three. This proves a turn's material is consolidated once per
tier, three times over its life, instead of being repeatedly reworked on every rotation.

#### AgentKitSessions-RotationEngine-FoldsOverflowIntoTiers: Rule Five Fits the Seed

**Tests**:

- `RotationEngine_Rotate_SeedThatDoesNotFit_Escalates`
- `RotationEngine_Rotate_ExpandingSummarizer_DropsAndTerminates`
- `RotationEngine_Rotate_SingleOversizedTurn_BottomsOutAtNewestTurn`
- `RotationEngine_LevelHelpers_SaturateAtTheExtremes`
- `RotationEngine_VerbatimTurnsFor_ShortensWithLevel`

These tests verify rule five. A seed that does not fit escalates the compaction level before
dropping material. At the highest level, an expanding summarizer still terminates and reports
`MaterialDropped`; a single oversized newest turn bottoms out with that turn alone; helper methods
stop at the low and high extremes; and `VerbatimTurnsFor` shortens the verbatim tail as level rises.

#### AgentKitSessions-SessionTranscript-SnapsToolBoundary: A Rotation Never Seeds an Orphaned Result

**Test**: `SessionTranscript_AppendTurn_GroupsEntriesAsOneTurn`

N/A for entry-level boundary adjustment - rotation now works with whole turns. The transcript test
verifies tool calls and results are grouped into one turn before rotation decides what remains
verbatim.

#### AgentKitSessions-RotationEngine-ReportsSaturation: A Failure to Reduce Is Reported

**Tests**:

- `RotationEngine_Rotate_SeedThatDoesNotFit_Escalates`
- `RotationEngine_Rotate_ExpandingSummarizer_DropsAndTerminates`
- `RotationEngine_Rotate_SingleOversizedTurn_BottomsOutAtNewestTurn`
- `RotationEngine_LevelHelpers_SaturateAtTheExtremes`
- `RotationEngine_VerbatimTurnsFor_ShortensWithLevel`

The requirement identifier is retained for traceability, but the redesigned evidence is compaction
level and dropped-material reporting. These tests prove escalation, finite level changes,
tail-shortening and the final drop path that reports `MaterialDropped` when compaction buys no room.

#### AgentKitSessions-RotationEngine-Deterministic: The Same Inputs Produce the Same Output

**Test**: `RotationEngine_ManyRotations_ConsolidatesOncePerTier`

Uses deterministic inputs and a deterministic summarizer across many rotations, then asserts the
observable request sequence reaches each tier exactly as the round-robin rules require. The stable
sequence is the determinism evidence for the rotation cascade.

#### AgentKitSessions-RotationEngine-RejectsMalformedConsolidation: A Null Record and Cancellation

**Tests**:

- `RotationEngine_Rotate_NullAnswer_Throws`
- `RotationEngine_Rotate_Canceled_Throws`

Rejects a null summarizer answer and honors cancellation at the engine boundary. A null record cannot
be stored as a slot, and a canceled rotation cannot continue to produce a seed.

#### AgentKitSessions-RotationEngine-NormalizesBlankRecords: A Blank Answer Becomes Empty

**Test**: `RotationEngine_Rotate_BlankAnswer_ProducesNoSlot`

Normalizes a blank summarizer answer to empty and creates no slot. This prevents a whitespace-only
record from occupying a ring slot while carrying no material.
