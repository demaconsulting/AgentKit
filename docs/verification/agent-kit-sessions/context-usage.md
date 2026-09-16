## ContextUsage Unit Verification Design

This document describes the unit-level verification strategy for `ContextUsage`.

### Verification Approach

`ContextUsage` is the one occupancy record every provider session answers with, and it preserves
whether the adapter measured the figures or estimated them. The tests construct measured and
estimated records directly, assert the split between conversation and overhead, and verify derived
occupancy figures are honest rather than clamped.

Unit tests reside in `ContextUsageTests.cs`, with the shape's use in place evidenced by
`InMemoryProviderSessionTests.cs` and `CompactingAgentSessionTests.cs`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider or model is contacted
- **Mocking**: None required for value tests; in-memory sessions cover use in place
- **Isolation**: Each test constructs its own usage record or session

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any lost origin, invented split, clamped over-full figure, or meaningless usage
record accepted constitutes a failure.

### Test Scenarios

#### AgentKitSessions-ContextUsage-UsageShape: The Origin Survives, and the Derived Figures Follow

**Tests**:

- `ContextUsage_FromProvider_CarriesSplitAndOrigin`
- `ContextUsage_FromEstimate_MarksEstimated`

Asserts measured and estimated records carry their origin and preserve the conversation split. This
is the evidence that an application reading a usage figure can tell what a provider counted from what
an adapter derived.

#### AgentKitSessions-ContextUsage-DefaultsToAllConversation: An Unreported Split Is Not Invented

**Test**: `ContextUsage_FromProvider_NoSplit_TreatsAllAsConversation`

Builds a provider usage record without a split and asserts the whole count is treated as
conversation. That path rotates earlier rather than later and avoids inventing overhead the provider
did not report.

#### AgentKitSessions-ContextUsage-ReportsOverFullHonestly: An Over-Full Window Is Not Hidden

**Test**: `ContextUsage_DerivedFigures_AreHonest`

Asserts free tokens floor at zero while `UsedFraction` remains above one when reported usage exceeds
the window. The record remains an honest account of provider pressure.

#### AgentKitSessions-ContextUsage-RejectsMeaninglessFigures: Impossible Figures Are Refused

**Test**: `ContextUsage_Construct_MeaninglessFigures_Throws`

Rejects a conversation count larger than total usage, a non-positive window and an undefined origin.
Those checks keep adapter arithmetic defects from entering rotation decisions.
