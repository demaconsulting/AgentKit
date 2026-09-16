## AgentSession Unit Verification Design

This document describes the unit-level verification strategy for `IAgentSession` and
`AgentSessionResponse`.

### Verification Approach

The session contract is verified through direct construction of `AgentSessionResponse` and through a
real `CompactingAgentSession` for the send path. The value-object tests prove a response carries the
provider answer, context usage, rotation flag, `CompactionLevel` and `MaterialDropped`. The session
test proves those fields are returned from an ordinary turn through the public contract.

Unit tests reside in `AgentSessionTests.cs`; the contract-level send scenario resides in
`CompactingAgentSessionTests.cs`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; provider behavior is in memory
- **Mocking**: None required beyond the deterministic in-memory provider and summarizer fake
- **Isolation**: Each test constructs its own response or session

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any response that loses the answer, usage, rotation flag, compaction level or
dropped-material signal, any ordinary response that does not default to low compaction with no
material dropped, or any malformed response accepted constitutes a failure.

### Test Scenarios

#### AgentKitSessions-AgentSession-SessionContract: A Clean Turn Reports No Compaction

**Tests**:

- `AgentSessionResponse_Construct_CarriesTheTurnResult`
- `CompactingAgentSession_Send_AnswersTurns`

The constructor test proves the response carries all fields. The session test sends a normal message
through a compacting session and asserts the provider answer is returned with `CompactionLevel.Low`
and no rotation on that turn.

#### AgentKitSessions-AgentSession-ReportsCompaction: The Response Carries Pressure Signals

**Tests**:

- `AgentSessionResponse_Construct_CarriesTheTurnResult`
- `AgentSessionResponse_Construct_DefaultsToLowAndNotDropped`
- `CompactingAgentSession_TightWindow_EscalatesToHighAndReportsDroppedMaterial`

Direct construction proves a response can carry `CompactionLevel.High` and `MaterialDropped`. The
default test proves ordinary turns report low compaction and no dropped material. The narrow-window
session test proves the live session reports high compaction and dropped material when sustained
pressure forces it to discard a slot.

#### AgentKitSessions-AgentSession-RejectsMalformedTurnReport: A Malformed Report Is Refused

**Tests**:

- `AgentSessionResponse_Construct_NullArgument_Throws`
- `AgentSessionResponse_Construct_UndefinedLevel_Throws`

Rejects null answer text, null usage and an undefined compaction level. These checks keep malformed
adapter output from becoming a public response.

#### Supporting Corner Cases

N/A - all requirements for this unit are covered by the scenarios above.
