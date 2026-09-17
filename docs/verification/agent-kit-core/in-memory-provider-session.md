## InMemoryProviderSession Unit Verification Design

This document describes the unit-level verification strategy for `InMemoryProviderSession` and
`InMemoryProviderSessionFactory`.

### Verification Approach

The in-memory provider is verified as a shipped deterministic provider-session implementation, not a
network adapter. Tests send messages through a responder, inspect recorded history, observe disposal,
assert the session answers for its own window, assert cancellation leaves no ghost turn, and verify
the factory records sessions in creation order.

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

#### AgentKitCore-InMemoryProviderSession-ContactsNothing: A Session Starts From Its Seed and Records Its Turns

**Tests**:

- `InMemoryProviderSession_Send_AnswersAndRecords`
- `InMemoryProviderSession_CanceledTurn_RecordsNothing`

Sends a message through a local responder and asserts the answer, turn count and history length. A
canceled turn throws and records nothing, proving cancellation does not leave partial transcript
state.

#### AgentKitCore-InMemoryProviderSession-AnswersForItsOwnWindow: Usage Comes From the Session Itself

**Test**: `InMemoryProviderSession_Usage_ReportsItsOwnWindowAndSplit`

Constructs a session with a known window and asserts it reports that window with the conversation
broken out from the seeded overhead — the shape a real adapter answers in, which is what makes
exercising the engine against this session meaningful.

#### AgentKitCore-InMemoryProviderSession-RecordsRotationEvidence: Disposal and Creation Are Both Observable

**Tests**:

- `InMemoryProviderSession_Dispose_IsObservable`
- `InMemoryProviderSessionFactory_Create_RecordsSessions`

Asserts disposal sets an observable flag and the factory records every created session oldest first.
System and compacting-session tests use that evidence to prove replacement sessions were created and
superseded sessions were released.
