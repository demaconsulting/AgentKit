## ContextUsage Unit Verification Design

This document describes the unit-level verification strategy for `ContextUsage`.

### Verification Approach

`ContextUsage` reduces reporting and silent provider shapes to one occupancy record while preserving
where the figure came from. The tests construct provider-origin and estimated records directly,
assert the split between conversation and overhead, and verify derived occupancy figures are honest
rather than clamped. Optional reporting is verified through the in-memory provider and the compacting
session's preference for provider figures.

Unit tests reside in `ContextUsageTests.cs`, with optional-reporting evidence in
`InMemoryProviderSessionTests.cs` and `CompactingAgentSessionTests.cs`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider or model is contacted
- **Mocking**: None required for value tests; in-memory sessions cover provider shapes
- **Isolation**: Each test constructs its own usage record or session

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any lost origin, invented split, clamped over-full figure, mixed-currency
comparison path, or meaningless usage record accepted constitutes a failure.

### Test Scenarios

#### AgentKitSessions-ContextUsage-UsageShape: The Origin Survives, and the Derived Figures Follow

**Tests**:

- `ContextUsage_FromProvider_CarriesSplitAndOrigin`
- `ContextUsage_FromEstimate_MarksEstimated`

Asserts provider-origin and estimated records carry their origin and preserve the conversation split.
This is the evidence that downstream occupancy comparisons know whether they are using provider
figures or this library's estimate.

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

#### AgentKitSessions-ContextUsage-OptionalReportingContract: A Reporter May Say It Does Not Know

**Tests**:

- `InMemoryProviderSession_Usage_SimulatesBothShapes`
- `CompactingAgentSession_Usage_PrefersProviderReport`

Verifies a provider session may report usage or return none. The compacting session prefers the
provider report when present and otherwise follows the estimated path, keeping each comparison in a
single occupancy shape.
