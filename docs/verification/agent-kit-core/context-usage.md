## ContextUsage Unit Verification Design

This document describes the unit-level verification strategy for `ContextUsage`.

### Verification Approach

`ContextUsage` is the one occupancy record every provider session answers with. The tests construct
records directly, assert the split between conversation and overhead, and verify that a window a
provider reports as over-full is carried through rather than clamped.

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
explicitly asserted. An invented split, a clamped over-full figure, or a meaningless usage record
accepted constitutes a failure.

### Test Scenarios

#### AgentKitCore-ContextUsage-UsageShape: The Split a Provider Reported Survives

**Test**: `ContextUsage_FromProvider_CarriesSplit`

Asserts a record built from a provider's figures preserves the conversation split and derives the
overhead from it. This is the evidence that every rotation comparison downstream is made in the
currency the figures arrived in.

#### AgentKitCore-ContextUsage-DefaultsToAllConversation: An Unreported Split Is Not Invented

**Test**: `ContextUsage_FromProvider_NoSplit_TreatsAllAsConversation`

Builds a provider usage record without a split and asserts the whole count is treated as
conversation. That path rotates earlier rather than later and avoids inventing overhead the provider
did not report.

#### AgentKitCore-ContextUsage-ReportsOverFullHonestly: An Over-Full Window Is Not Hidden

**Test**: `ContextUsage_FromProvider_BeyondTheWindow_IsNotClamped`

Reports usage beyond the window and asserts the figures come back as the adapter stated them. The
record remains an honest account of provider pressure, which is the one condition an application most
needs to see.

#### AgentKitCore-ContextUsage-RejectsMeaninglessFigures: Impossible Figures Are Refused

**Test**: `ContextUsage_Construct_MeaninglessFigures_Throws`

Rejects a conversation count larger than total usage and a non-positive window. Those checks keep
adapter arithmetic defects from entering rotation decisions.
