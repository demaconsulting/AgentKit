## InMemoryProviderSession Unit Verification Design

This document describes the unit-level verification strategy for `InMemoryProviderSession` and
`InMemoryProviderSessionFactory`.

### Verification Approach

The in-memory provider is verified as a shipped deterministic provider-session implementation, not a
network adapter. Tests send messages through a responder, inspect recorded history, observe disposal,
exercise reporting and silent usage shapes, assert cancellation leaves no ghost turn, and verify the
factory records sessions in creation order.

Unit tests reside in `InMemoryProviderSessionTests.cs`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider or model is contacted
- **Mocking**: None required; responders are local delegates
- **Isolation**: Each test constructs its own seed, session or factory

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any session that contacts external services, fails to record turns, reports the
wrong usage shape, records a canceled turn, hides disposal, or fails to retain factory evidence
constitutes a failure.

### Test Scenarios

#### AgentKitSessions-InMemoryProviderSession-ContactsNothing: A Session Starts From Its Seed and Records Its Turns

**Tests**:

- `InMemoryProviderSession_Send_AnswersAndRecords`
- `InMemoryProviderSession_CanceledTurn_RecordsNothing`

Sends a message through a local responder and asserts the answer, turn count and history length. A
canceled turn throws and records nothing, proving cancellation does not leave partial transcript
state.

#### AgentKitSessions-InMemoryProviderSession-SimulatesBothProviderShapes: Usage Is Reported, or Withheld

**Test**: `InMemoryProviderSession_Usage_SimulatesBothShapes`

Constructs reporting and silent sessions. The reporting session returns provider-origin usage; the
silent session returns none so the compacting session can exercise its estimated path.

#### AgentKitSessions-InMemoryProviderSession-RecordsRotationEvidence: Disposal and Creation Are Both Observable

**Tests**:

- `InMemoryProviderSession_Dispose_IsObservable`
- `InMemoryProviderSessionFactory_Create_RecordsSessions`

Asserts disposal sets an observable flag and the factory records every created session oldest first.
System and compacting-session tests use that evidence to prove replacement sessions were created and
superseded sessions were released.
