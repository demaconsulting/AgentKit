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

#### AgentKitSessions-RotationEngine-ConsolidatesFullTiers: Coarse Tiers Age and Ring

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

#### AgentKitSessions-RotationEngine-ConsolidatesIntoTierOne: Older Turns Become One Tier-One Slot

**Test**: `RotationEngine_Rotate_ConsolidatesOlderIntoOneTierOneSlot`

Splits the layout at the level-adjusted verbatim tail, consolidates everything older into one slot,
and appends that slot to tier one. The newest turns remain verbatim and the request carries the low
aggressiveness instruction.

#### AgentKitSessions-RotationEngine-ConsolidatesOncePerTier: A Turn Is Consolidated Once Per Tier

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

#### AgentKitSessions-RotationEngine-ConsolidatesIntoTierOne: Rule Five Fits the Seed

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

#### AgentKitSessions-SessionTranscript-KeepsToolTrafficWithItsTurn: A Rotation Never Seeds an Orphaned Result

**Test**: `SessionTranscript_AppendTurn_GroupsEntriesAsOneTurn`

N/A for entry-level boundary adjustment - rotation now works with whole turns. The transcript test
verifies tool calls and results are grouped into one turn before rotation decides what remains
verbatim.

#### AgentKitSessions-RotationEngine-DropsUntilItFits: A Failure to Reduce Is Reported

**Tests**:

- `RotationEngine_Rotate_SeedThatDoesNotFit_Escalates`
- `RotationEngine_Rotate_TailShorterThanItsMaximum_StillMakesProgress`
- `RotationEngine_Rotate_ExpandingSummarizer_DropsAndTerminates`
- `RotationEngine_Rotate_SingleOversizedTurn_BottomsOutAtNewestTurn`
- `RotationEngine_LevelHelpers_SaturateAtTheExtremes`
- `RotationEngine_VerbatimTurnsFor_ShortensWithLevel`

The requirement identifier is retained for traceability, but the redesigned evidence is compaction
level and dropped-material reporting. These tests prove escalation, finite level changes,
tail-shortening and the final drop path that reports `MaterialDropped` when compaction buys no room.

The progress test covers the condition that fitting alone does not catch. Rule 2 triggers on the
provider's occupancy, measured in tokens, while the verbatim tail is held by a count of turns, so a
provider counting well above this library's estimate reaches its threshold while the tail is still
shorter than its configured maximum. Nothing older is then available to consolidate, the candidate is
identical to the layout it came from, and it passes a fit test taken in our own estimate. The test
gives the engine a tail of four turns against a maximum of twelve and a deliberately roomy threshold
— the shape where a fit test alone accepts a no-op — and asserts that a slot was written and the
level escalated, because shortening the tail is how progress is made.

Measured end to end before this was fixed, at five times divergence, a session rode to one hundred
and forty percent of the provider's window across nineteen turns without rotating once. That is the
state the package exists to prevent, because it is where the provider's own compactor fires and
truncates history blindly.

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

**Test**: `RotationEngine_Rotate_BlankAnswer_ProducesNoSlotAndKeepsTheMaterial`

Normalizes a blank summarizer answer to empty and creates no slot, which prevents a whitespace-only
record from occupying a ring slot while carrying no material.

Producing no slot is only half the behavior, and the test asserts both halves. The material the slot
would have held stays verbatim: retaining only the shortened tail alongside an absent slot would
discard every older turn while recording nothing in their place — a silent loss, reported as an
ordinary success, and committed to the provider as soon as the replacement session is seeded from the
shortened layout. Asserting the tier is empty does not catch that; the turn count does. The context is
then no smaller than it was, which is exactly the condition Rule 5 measures, so a session whose
summarizer goes blank escalates and, failing that, drops material and says so.

The same reasoning governs a blank answer at a cascade. A full tier cannot be cleared on the strength
of a record that was never written, so the tier stands and the arriving slot displaces its oldest —
the bounded move the coarsest tier already makes — losing one slot rather than the tier's whole
complement, and reporting it as dropped material rather than silently.
