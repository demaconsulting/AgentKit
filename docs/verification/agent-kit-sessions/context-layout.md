## ContextLayout Unit Verification Design

This document describes the unit-level verification strategy for `Slot`, `Tier` and `ContextLayout`.

### Verification Approach

The layout is verified as the internal round-robin structure that backs compaction: three tiers,
four slots per tier, and a turn-granular verbatim tail. Tests assert the constants, the ring behavior
of tiers, empty layout creation, seed ordering, seeded-slot labels, estimated occupancy and malformed
replacement rejection.

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

#### AgentKitSessions-ContextLayout-TierModel: The Hierarchy Matches the Policy From the Outset

**Tests**:

- `ContextLayout_Constants_AreTheRoundRobinShape`
- `Tier_RingOperations_Hold`
- `Slot_Construct_Blank_Throws`

Asserts `SlotsPerTier` is four, `TierCount` is three, and the internal rotation threshold is the
published fraction. Tier tests prove the ring reports full at its fixed complement, exposes the
oldest slot and drops the oldest slot. Slot construction rejects blank records.

#### AgentKitSessions-ContextLayout-EstimatesItsSize: The Accounting Follows From the Configuration

**Test**: `ContextLayout_EstimatedTokens_CountSeedAndOverhead`

Asserts estimated conversation tokens include seeded slots, their framing and the verbatim tail, and
that total estimated tokens add the fixed overhead to that same accounting.

#### AgentKitSessions-ContextLayout-Immutable: A Layout Is Never Modified in Place

**Tests**:

- `ContextLayout_Create_IsEmpty`
- `ContextLayout_WithTiers_Malformed_Throws`

Verifies a fresh layout has no slots or turns and builds an empty seed. Replacement rejects the wrong
number of tiers or a null tier, so callers cannot create a malformed layout state.

#### AgentKitSessions-ContextLayout-SeedsMostStableFirst: The Seed Is Emitted Coarsest First

**Tests**:

- `ContextLayout_BuildSeed_IsCoarsestFirst`
- `ContextLayout_BuildSeed_LabelsSlotsByTier`

Asserts the seed emits the coarsest tier first, then finer tiers, then the verbatim tail in order.
Seeded records are labeled by detail level so the provider receives stable context before recent
turns.
