## CompactionPolicy Unit Verification Design

This document describes the unit-level verification strategy for the `CompactionPolicy` class.

### Verification Approach

`CompactionPolicy` is a validated immutable value type, so it is verified by construction alone:
every scenario builds a policy and asserts either what it carries or what it refused. Nothing is
mocked, because the class depends on nothing.

The published defaults are asserted by value rather than merely for non-emptiness. Those numbers —
four tiers of 2,000, 1,200, 900 and 700 tokens, a 70 percent rotation threshold and a 90 percent
saturation ratio — are the documented design, and a test that only checked they were "some numbers"
would let a silent change to any of them pass while every document still described the old ones.

Unit tests reside in `CompactionPolicyTests.cs` within the `DemaConsulting.AgentKit.Sessions.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; **no network access is used**
- **Mocking**: None required
- **Isolation**: Each test constructs its own policy; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any default that has drifted from the documented design, any unworkable
configuration accepted, or any supplied budget list that can still be mutated through the caller's
reference constitutes a failure.

### Test Scenarios

#### AgentKitSessions-CompactionPolicy-TierBudgets: The Defaults Are the Documented Design

**Test**: `CompactionPolicy_Default_CarriesThePublishedTierBudgets`

Asserts the default policy carries four tiers, the exact budgets 2,000, 1,200, 900 and 700, a total
of 4,800 tokens, and the published rotation threshold and saturation ratio. This is the scenario
that keeps the documents and the code from diverging.

#### AgentKitSessions-CompactionPolicy-TierBudgets: A Supplied Budget List Cannot Be Mutated Afterwards

**Tests**: `CompactionPolicy_Construct_CopiesTheSuppliedBudgets`,
`CompactionPolicy_TierBudgetTokens_CannotBeCastAndMutated`

Hands the constructor a mutable list, mutates it afterwards, and asserts the policy is unaffected. A
caller that could change the budgets a session is already running under would silently alter the
bound mid-conversation, which is the one property the arrangement is supposed to guarantee. The
second scenario closes the other route to the same outcome: the published list is asserted not to be
the backing array and to refuse a write through an `IList` cast, for the policy's own budgets and
for the published defaults, because `TotalTierBudgetTokens` is summed once and would go stale.

#### AgentKitSessions-CompactionPolicy-ValidatedDefaults: One Control Can Be Replaced Alone

**Tests**: `CompactionPolicy_Default_IsShared`,
`CompactionPolicy_Construct_SingleOverride_KeepsOtherDefaults`

Asserts the default is a single shared instance — so a caller can establish by reference that no
host configuration was applied — and that replacing only the rotation threshold leaves the tier
budgets at their defaults. This is the convention the rest of AgentKit's option types follow.

#### AgentKitSessions-CompactionPolicy-RejectsUnworkableConfiguration: Unworkable Policies Are Refused

**Tests**: `CompactionPolicy_Construct_SingleTier_Throws`,
`CompactionPolicy_Construct_NonPositiveBudget_Throws`,
`CompactionPolicy_Construct_GrowingBudgets_Throws`,
`CompactionPolicy_Construct_ThresholdOutOfRange_Throws`,
`CompactionPolicy_Construct_SaturationRatioOutOfRange_Throws`,
`CompactionPolicy_Construct_NaNControl_Throws`

Boundary and error paths. A single tier is refused because with nowhere for overflowing history to
age into the arrangement degenerates to dropping the oldest turns outright. A non-positive budget is
refused because a tier that could hold nothing is not a tier. A coarser budget larger than the tier
it ages from is refused because it is the opposite of what consolidation is for and would never
reduce. The threshold and ratio are probed at zero, below zero and above one, because a threshold at
or below zero would rotate on every turn and one above one could never fire.
