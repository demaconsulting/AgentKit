## AgentSessionOptions Unit Verification Design

This document describes the unit-level verification strategy for `AgentSessionOptions`.

### Verification Approach

`AgentSessionOptions` is verified by construction. The tests assert that application-supplied
configuration is carried exactly and that the fixed overhead from instructions and tools is measured
where it is stated. No provider window is configured here, so there is none to validate: the window
is a fact the adapter answers for. The compaction setting is the single `VerbatimTurns` control
supplied by `CompactionPolicy`.

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
explicitly asserted. Any option value that is not preserved, any overhead that is not measured, any
invalid dependency accepted, or any drift in documented defaults constitutes a failure.

### Test Scenarios

#### AgentKitSessions-AgentSessionOptions-CarriesConfiguration: Defaults Are What an Application Receives

**Tests**:

- `AgentSessionOptions_Construct_CarriesConfiguration`
- `AgentSessionOptions_Construct_Defaults`

Asserts a supplied summarizer and `CompactionPolicy` are carried through and that `VerbatimTurns`
follows the policy. The default path asserts the shared default policy and the default verbatim tail,
and neither path names a provider window, because the options carry none.

#### AgentKitSessions-AgentSessionOptions-MeasuresFixedOverhead: Overhead Is Measured Where It Is Stated

**Test**: `AgentSessionOptions_Construct_MeasuresFixedOverhead`

Builds options with instructions and a tool declaration, then asserts `SystemTokens`,
`ToolDeclarationTokens` and `FixedOverheadTokens` agree. The overhead is a standing cost the context
carries on every turn, so it is measured once, against the declarations the options now own, and is
never subtracted from a figure a provider reported.

#### AgentKitSessions-AgentSessionOptions-RejectsMalformedConfiguration: Invalid Arguments Are Refused

**Tests**:

- `AgentSessionOptions_Construct_NullSummarizer_Throws`
- `AgentSessionOptions_Construct_NullTool_Throws`

Rejects a null summarizer and a null tool declaration. Each failure is raised where the application
supplied the bad configuration. Nothing here predicts a settlement point before a session starts: the
options refuse no window, because they are given none, and pressure is answered at runtime in counts.
