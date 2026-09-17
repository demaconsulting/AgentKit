## ContextLayout Unit Verification Design

This document describes the unit-level verification strategy for `Slot`, `Tier` and `ContextLayout`.

### Verification Approach

The layout is verified as the internal round-robin structure that backs compaction: three tiers,
four slots per tier, and a turn-granular verbatim tail. Tests assert the constants, the ring behavior
of tiers, empty layout creation, seed ordering, seeded-slot labels and malformed replacement
rejection.

No provider or summarizer is required for these tests. Rotation behavior that fills and cascades the
layout is verified in `RotationEngineTests.cs`.

Unit tests reside in `ContextLayoutTests.cs`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider or model is contacted
- **Mocking**: None required
- **Isolation**: Each test builds its own layout, tier and slot values

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any drift in the fixed shape, any blank slot accepted, any tier that grows
without ring behavior, any seed emitted in the wrong order, any unlabeled slot, or any malformed tier
replacement accepted constitutes a failure.

### Test Scenarios

#### AgentKitCore-ContextLayout-TierModel: The Hierarchy Matches the Policy From the Outset

**Tests**:

- `ContextLayout_Constants_AreTheRoundRobinShape`
- `Tier_Append_Null_Throws`
- `Tier_AppendAndDropOldest_KeepOldestFirstAndLeaveTheOriginal`
- `Tier_DropOldest_Empty_Throws`
- `Slot_Construct_Blank_Throws`

Asserts `SlotsPerTier` is four, `TierCount` is three, and the internal rotation threshold is the
published fraction. Tier tests prove the ring keeps its slots oldest first, leaves the tier it was
appended to unchanged, drops its oldest slot, refuses a null slot and refuses to drop from an empty
tier. Slot construction rejects blank records.

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
