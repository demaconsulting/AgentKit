## AgentSessionOptions Unit Verification Design

This document describes the unit-level verification strategy for `AgentSessionOptions`.

### Verification Approach

`AgentSessionOptions` is verified by construction. The tests assert that application-supplied
configuration is carried exactly and that the published default verbatim tail is the one an
application receives when it configures none. No provider window is configured here, so there is none
to validate: the window is a fact the adapter answers for. The compaction setting is the single
`VerbatimTurns` control.

Unit tests reside in `AgentSessionOptionsTests.cs`, with a simple tool declaration built through
`Microsoft.Extensions.AI`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider or model is contacted
- **Mocking**: `FakeSummarizer` supplies the required summarizer dependency
- **Isolation**: Each test constructs its own options instance

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any option value that is not preserved, any invalid dependency accepted, or any
drift in documented defaults constitutes a failure.

### Test Scenarios

#### AgentKitCore-AgentSessionOptions-CarriesConfiguration: What an Application States Is What It Gets

**Tests**:

- `AgentSessionOptions_Construct_CarriesConfiguration`
- `AgentSessionOptions_Construct_Defaults`

Asserts a supplied summarizer, instructions and tools are carried through unchanged. The default path
asserts that everything but the summarizer defaults, and neither path names a provider window,
because the options carry none.

#### AgentKitCore-AgentSessionOptions-VerbatimTurns: The Published Default Is the One in Force

**Tests**:

- `AgentSessionOptions_Construct_Defaults`
- `AgentSessionOptions_Construct_CarriesConfiguration`

Asserts the verbatim tail defaults to `DefaultVerbatimTurns` and that a supplied count replaces it.
This is the only compaction number an application states; the slots-per-tier and tier counts are
internal constants, verified in `ContextLayoutTests.cs`.

#### AgentKitCore-AgentSessionOptions-RejectsMalformedConfiguration: Invalid Arguments Are Refused

**Tests**:

- `AgentSessionOptions_Construct_NullSummarizer_Throws`
- `AgentSessionOptions_Construct_NullTool_Throws`
- `AgentSessionOptions_Construct_NonPositiveVerbatimTurns_Throws`

Rejects a null summarizer, a null tool declaration and a verbatim tail that is not positive. Each
failure is raised where the application supplied the bad configuration. Nothing here predicts a
settlement point before a session starts: the options refuse no window, because they are given none,
and pressure is answered at runtime in counts.
