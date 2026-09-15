## ContextLayout Unit Verification Design

This document describes the unit-level verification strategy for `ContextTier` and `ContextLayout`.

### Verification Approach

Both types are immutable and compute their figures arithmetically, so they are verified by direct
construction and direct property reads. Nothing is mocked.

The accounting scenarios use a layout with a deliberately non-zero fixed overhead — a measured
system prompt and tool declarations — because the distinction that matters is between conversation
tokens and total tokens, and a layout with zero overhead would make the two identical and the
assertion vacuous.

The seed scenario asserts ordering by position rather than by set membership. Most-stable-first is
the whole reason the seed exists, and a test that only checked the right records were present would
pass on a seed emitted in exactly the wrong order.

Unit tests reside in `ContextLayoutTests.cs`, with the shared small policy and exact-size builders
in `SessionTestData.cs`, both within the `DemaConsulting.AgentKit.Sessions.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; **no network access is used**
- **Mocking**: None required
- **Isolation**: Each test builds its own layout; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any tier hierarchy that does not match its policy, any bound that does not
follow from the configuration, any mutation of an existing layout, or any seed emitted out of order
constitutes a failure.

### Test Scenarios

#### AgentKitSessions-ContextLayout-TierModel: The Hierarchy Matches the Policy From the Outset

**Tests**: `ContextLayout_Create_AllocatesOneCoarseTierPerBudgetAboveTierZero`,
`ContextLayout_WithTiers_WrongTierCount_Throws`,
`ContextTier_Construct_TierZero_Throws`

Asserts a new layout allocates one coarse tier per non-verbatim budget, numbered from one and
carrying the policy's budget for each index, all empty. Asserts a replacement tier list of the wrong
length is refused rather than silently truncating the hierarchy, and that tier zero cannot be
constructed as a coarse tier at all — it holds verbatim history and is a transcript. A tier
appearing or vanishing mid-session would make the bound unverifiable at the moment it mattered most.

#### AgentKitSessions-ContextLayout-PublishesTheBound: The Accounting Follows From the Configuration

**Tests**: `ContextLayout_MaximumBoundTokens_IsOverheadPlusEveryTierBudget`,
`ContextLayout_ConversationTokens_ExcludeTheFixedOverhead`,
`ContextTier_IsWithinBudget_ReflectsTheRecordSize`

Asserts the bound is exactly the fixed overhead plus every tier budget and that a fresh layout sits
within it; that conversation tokens count the transcript and tiers only, while the total adds the
overhead back; and that a tier knows whether its record still fits, which is the test the rotation
engine makes after every consolidation. The bound depending only on the configuration is what makes
it something an application can reason about before a session starts.

#### AgentKitSessions-ContextLayout-Immutable: A Layout Is Never Modified in Place

**Test**: `ContextLayout_WithTranscript_LeavesTheOriginalUnchanged`

Gives an empty layout a transcript and asserts the original is still empty. Immutability is what
makes the rotation engine a pure function and lets a test compare a before and an after; a mutating
update would still produce correct-looking layouts while breaking every such comparison.

#### AgentKitSessions-ContextLayout-SeedsMostStableFirst: The Seed Is Emitted Coarsest First

**Tests**: `ContextLayout_BuildSeed_EmitsCoarsestRecordsFirstThenVerbatimHistory`,
`ContextLayout_BuildSeed_EmptyLayout_EmitsNothing`

Builds a layout with records in tiers one and two, an empty tier three and one verbatim turn, then
asserts by position that tier two's record comes first, tier one's second, the empty tier is skipped
entirely, and the verbatim turn comes last. Stability decreasing from left to right is what allows a
provider's prompt cache to match the longest possible prefix; seeding an empty record would spend
framing tokens to say nothing. A layout that has held no conversation seeds nothing at all.
