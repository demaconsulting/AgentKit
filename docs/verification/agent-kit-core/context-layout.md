## ContextLayout Unit Verification Design

This document describes the unit-level verification strategy for `Slot`, `Tier` and `ContextLayout`.

### Verification Approach

The layout is verified as the internal round-robin structure that backs compaction: three tiers,
four slots per tier, and a turn-granular verbatim tail. Tests assert the constants, the ordering and
immutability of a tier, empty layout creation, seed ordering, seeded-slot labels and malformed
replacement rejection.

No provider or summarizer is required for these tests. The rules that keep a tier at its complement —
consolidating a full tier into the next and clearing it, and making the coarsest tier a ring that
displaces its oldest — live in `RotationEngine`, which is the only writer of tiers, and are verified
in `RotationEngineTests.cs`. Nothing here appends past a tier's complement, because this unit applies
no bound and asserting one would assert a rule it does not own.

Unit tests reside in `ContextLayoutTests.cs`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider or model is contacted
- **Mocking**: None required
- **Isolation**: Each test builds its own layout, tier and slot values

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any drift in the fixed shape, any blank slot accepted, any tier that loses its
oldest-first ordering or is modified in place, any seed emitted in the wrong order, any unlabeled
slot, or any malformed tier replacement accepted constitutes a failure.

### Test Scenarios

#### AgentKitCore-ContextLayout-TierModel: The Hierarchy Matches the Policy From the Outset

**Tests**:

- `ContextLayout_Constants_AreTheRoundRobinShape`
- `Tier_Append_Null_Throws`
- `Tier_AppendAndDropOldest_KeepOldestFirstAndLeaveTheOriginal`
- `Tier_DropOldest_Empty_Throws`
- `Slot_Construct_Blank_Throws`

Asserts `SlotsPerTier` is four, `TierCount` is three, and the internal rotation threshold is the
published fraction. Tier tests prove a tier keeps its slots oldest first, appends to the newest end,
leaves the tier it was appended to unchanged, returns a copy without its oldest slot, refuses a null
slot and refuses to drop from an empty tier. Slot construction rejects blank records. None of these
appends past `SlotsPerTier`, because the complement is not this unit's rule to keep — see
`RotationEngine_Rotate_FullTier_ConsolidatesAsPeersIntoNextTier` and
`RotationEngine_Rotate_CoarsestTier_IsARing` for the tests that hold it.

#### AgentKitCore-ContextLayout-Immutable: A Layout Is Never Modified in Place

**Tests**:

- `ContextLayout_Create_IsEmpty`
- `ContextLayout_WithTail_ReplacesTheTailAndLeavesTheOriginal`
- `ContextLayout_WithTiers_Malformed_Throws`

Verifies a fresh layout has no slots or turns and builds an empty seed, that replacing the tail
returns a new layout and leaves the original untouched, and that replacement rejects the wrong number
of tiers or a null tier, so callers cannot create a malformed layout state.

#### AgentKitCore-ContextLayout-SeedsMostStableFirst: The Seed Is Emitted Coarsest First

**Tests**:

- `ContextLayout_BuildSeed_IsCoarsestFirst`
- `ContextLayout_BuildSeed_LabelsSlotsByTier`

Asserts the seed emits the coarsest tier first, then finer tiers, then the verbatim tail in order.
Seeded records are labeled by detail level so the provider receives stable context before recent
turns.
