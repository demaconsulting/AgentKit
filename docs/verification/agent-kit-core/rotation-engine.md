## RotationEngine Unit Verification Design

This document describes the unit-level verification strategy for `RotationEngine` and its rotation
outcome.

### Verification Approach

`RotationEngine` is verified entirely without a model. Each scenario supplies `FakeSummarizer`,
which returns deterministic records and records every `ConsolidationRequest`. That makes the engine
a reproducible function of the input layout, compaction level and verbatim-turn setting.

The tests cover the redesigned rotation rules. Rule one appends whole turns to the verbatim tail in
the session. Rule two consolidates everything older than the level-adjusted verbatim tail into one
tier-one slot, keeping at most one turn fewer than the tail holds so that a rotation always moves
something. Rule three consolidates a full tier's slots as peers into the next tier. Rule four keeps
the coarsest tier as a ring. Rule five is the session's, not the engine's: nothing here measures
whether the result will fit, and the engine rotates once at the level it is handed.

Pathological cases are first-class evidence. Tests use a blank summarizer answer, a blank answer at a
full-tier cascade, and a tail already shorter than its configured maximum, to prove normalization,
retention and progress under conditions that must not regress.

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
reuses a turn more than once per tier, grows the coarsest tier, consolidates nothing while reporting a
rotation, stores a null answer, stores a blank slot, loses the material a blank answer failed to
consolidate, or ignores cancellation constitutes a failure.

### Test Scenarios

#### AgentKitCore-RotationEngine-ConsolidatesFullTiers: Coarse Tiers Age and Ring

**Tests**:

- `RotationEngine_Rotate_FullTier_ConsolidatesAsPeersIntoNextTier`
- `RotationEngine_Rotate_CoarsestTier_IsARing`

These tests verify the coarse-tier aging rules. A full tier is consolidated as peer slots into the
next tier and then cleared for the arriving slot. The coarsest tier behaves as a ring and remains at
its fixed slot count when a cascade reaches it.

#### AgentKitCore-RotationEngine-ConsolidatesIntoTierOne: Older Turns Become One Tier-One Slot

**Tests**:

- `RotationEngine_Rotate_ConsolidatesOlderIntoOneTierOneSlot`
- `RotationEngine_Rotate_HandsTheOlderTurnsOverAsRenderedMaterial`

Splits the layout at the level-adjusted verbatim tail, consolidates everything older into one slot,
and appends that slot to tier one. The newest turns remain verbatim and the request carries the low
aggressiveness instruction. The second test inspects the material the summarizer actually received:
exactly the turns older than the tail, rendered as labeled lines, oldest first, with the retained
turns absent.

#### AgentKitCore-RotationEngine-ConsolidatesOncePerTier: A Turn Is Consolidated Once Per Tier

**Test**: `RotationEngine_ManyRotations_ConsolidatesOncePerTier`

Drives twenty-one rotations, enough for material to cascade from tier one to tier three, and asserts
**how many** consolidation requests each tier receives rather than merely that it receives any.

The count is the discriminating observable, and presence is not. The design this replaced
re-consolidated each tier's standing record on every rotation — a flat ratchet, and the reason a flat
scheme's recall collapses as rotations accumulate — and that implementation would produce requests at
all three tiers exactly as this one does. What it could not produce is twenty-one requests at tier
one, four at tier two and one at tier three: batch-then-clear consolidates a tier only when it fills,
so each tier sees a request once per `SlotsPerTier` requests of the tier below it. A ratchet would put
all three counts near twenty-one.

That ratio is what makes a turn's material pass through exactly three consolidations in its whole
life, which is the property the recall of the whole arrangement rests on.

#### AgentKitCore-SessionTranscript-KeepsToolTrafficWithItsTurn: A Rotation Never Seeds an Orphaned Result

**Tests**:

- `SessionTranscript_AppendTurn_GroupsEntriesAsOneTurn`
- `SessionTranscript_SplitAtTail_KeepsNewestTurns`

The transcript groups a user message, its tool traffic and the answer into one turn, and hands the
older material to a rotation as whole turns rather than as a flat run of entries. Both halves are
needed: grouping is what makes a boundary turn-granular, and returning turns is what lets a
consolidation render each exchange whole, so a tool result never reaches a summarizer without the
call it answers.

#### AgentKitCore-RotationEngine-MovesAtLeastOneTurn: A Rotation Always Moves Something

**Tests**:

- `RotationEngine_Rotate_TailShorterThanItsMaximum_StillMakesProgress`
- `RotationEngine_LevelHelpers_SaturateAtTheExtremes`
- `RotationEngine_VerbatimTurnsFor_ShortensWithLevel`

These tests verify the guarantee that replaced rule five inside the engine. The progress test covers
the condition a fit test could not catch. Rule 2 triggers on the provider's occupancy, measured in
tokens, while the verbatim tail is held by a count of turns, so a provider whose tokenizer runs well
ahead of an ordinary conversation's growth reaches its threshold while the tail is still shorter than
its configured maximum.
Nothing older is then available to consolidate and the result is identical to the layout handed in.
The test gives the engine a tail of four turns against a maximum of twelve — the shape where keeping
the level's figure blindly does nothing — and asserts that a slot was written and exactly three turns
remain verbatim, because capping the tail at one turn fewer than it holds is how progress is made.

Measured end to end before this was fixed, at five times divergence, a session rode to one hundred
and forty percent of the provider's window across nineteen turns without rotating once. That is the
state the package exists to prevent, because it is where the provider's own compactor fires and
truncates history blindly.

The helper tests hold the level ladder itself: escalation and relaxation stop at the extremes, and
`VerbatimTurnsFor` shortens the verbatim tail as the level rises, which is what makes the session's
response to pressure a bounded ladder rather than an open-ended search.

#### AgentKitCore-RotationEngine-Deterministic: The Same Inputs Produce the Same Output

**Test**: `RotationEngine_ManyRotations_ConsolidatesOncePerTier`

Uses deterministic inputs and a deterministic summarizer across many rotations, then asserts the
observable request sequence reaches each tier exactly as the round-robin rules require. The stable
sequence is the determinism evidence for the rotation cascade.

#### AgentKitCore-RotationEngine-RejectsMalformedConsolidation: A Null Record and Cancellation

**Tests**:

- `RotationEngine_Rotate_NullAnswer_Throws`
- `RotationEngine_Rotate_Canceled_Throws`

Rejects a null summarizer answer and honors cancellation at the engine boundary. A null record cannot
be stored as a slot, and a canceled rotation cannot continue to produce a seed.

#### AgentKitCore-RotationEngine-NormalizesBlankRecords: A Blank Answer Becomes Empty

**Tests**:

- `RotationEngine_Rotate_BlankAnswer_ProducesNoSlotAndKeepsTheMaterial`
- `RotationEngine_Rotate_FullTierBlankAnswer_DisplacesOneSlotAndReportsIt`

Normalizes a blank summarizer answer to empty and creates no slot, which prevents a whitespace-only
record from occupying a ring slot while carrying no material.

Producing no slot is only half the behavior, and the test asserts both halves. The material the slot
would have held stays verbatim: retaining only the shortened tail alongside an absent slot would
discard every older turn while recording nothing in their place — a silent loss, reported as an
ordinary success, and committed to the provider as soon as the replacement session is seeded from the
shortened layout. Asserting the tier is empty does not catch that; the turn count does. The context is
then no smaller than it was, and the turn reports that material was lost, so a session whose
summarizer goes blank compacts harder on the next turn rather than pretending the rotation succeeded.

The full-tier test is the same defect one level down. A full tier cannot be cleared on the strength
of a record that was never written, so the second test makes a rule-3 cascade come back blank and
asserts that the tier keeps its complement, that exactly one slot is displaced by the arriving one,
and that the outcome reports dropped material. Clearing the tier would have lost its whole
complement, under a result that looked like an ordinary success; the bounded move the coarsest tier
already makes loses one slot instead, and says so.
