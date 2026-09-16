## AgentSessionOptions Unit Verification Design

This document describes the unit-level verification strategy for `AgentSessionOptions`.

### Verification Approach

`AgentSessionOptions` is verified by construction. The tests assert that application-supplied
configuration is carried exactly, that the fixed overhead from instructions and tools is measured,
and that the effective window and rotation threshold are derived from the same configured provider
window. The compaction setting is the single `VerbatimTurns` control supplied by `CompactionPolicy`.

Unit tests reside in `AgentSessionOptionsTests.cs`, with a simple tool declaration built through
`Microsoft.Extensions.AI` for overhead measurement.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider or model is contacted
- **Mocking**: `FakeSummarizer` supplies the required summarizer dependency
- **Isolation**: Each test constructs its own options instance

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any option value that is not preserved, any overhead that is omitted from the
effective window, any invalid window or dependency accepted, or any drift in documented defaults
constitutes a failure.

### Test Scenarios

#### AgentKitSessions-AgentSessionOptions-CarriesConfiguration: Defaults Are What an Application Receives

**Tests**:

- `AgentSessionOptions_Construct_CarriesConfiguration`
- `AgentSessionOptions_Construct_Defaults`

Asserts a supplied summarizer, provider window and `CompactionPolicy` are carried through, and that
`VerbatimTurns`, `EffectiveWindowTokens` and `RotationThresholdTokens` are derived from them. The
default path asserts the shared default policy, default provider window and default verbatim tail.

#### AgentKitSessions-AgentSessionOptions-MeasuresFixedOverhead: Overhead Is Subtracted Before the Threshold

**Test**: `AgentSessionOptions_Construct_MeasuresFixedOverhead`

Builds options with instructions and a tool declaration, then asserts `SystemTokens`,
`ToolDeclarationTokens`, `FixedOverheadTokens` and `EffectiveWindowTokens` agree. This keeps the
conversation occupancy distinct from fixed prompt overhead.

#### AgentKitSessions-AgentSessionOptions-AssertsTheBound: The Effective Window Is Derived Consistently

**Tests**:

- `AgentSessionOptions_Construct_CarriesConfiguration`
- `AgentSessionOptions_Construct_MeasuresFixedOverhead`

Verifies the provider window, measured overhead, effective window and rotation threshold are derived
from one options object. The redesigned core no longer predicts a settlement point before a session
starts; it validates only the application's own numbers and lets runtime fitting handle pressure.

#### AgentKitSessions-AgentSessionOptions-RejectsMalformedConfiguration: Invalid Arguments Are Refused

**Tests**:

- `AgentSessionOptions_Construct_NullSummarizer_Throws`
- `AgentSessionOptions_Construct_UnworkableWindow_Throws`
- `AgentSessionOptions_Construct_NullTool_Throws`

Rejects a null summarizer, a non-positive provider window, a window consumed by fixed overhead and a
null tool declaration. Each failure is raised where the application supplied the bad configuration.
