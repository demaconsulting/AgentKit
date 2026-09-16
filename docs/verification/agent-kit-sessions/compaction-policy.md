## CompactionPolicy Unit Verification Design

This document describes the unit-level verification strategy for `CompactionPolicy`.

### Verification Approach

`CompactionPolicy` is a validated immutable value type with one application-controlled setting:
`VerbatimTurns`. The tests construct default and custom policies and assert the value an application
receives. No collaborator is mocked because the policy has no dependencies.

The fixed round-robin structure is not configured through this type. Its slot and tier counts are
verified in `ContextLayoutTests.cs`; this unit verifies only the published verbatim-tail control and
its validation.

Unit tests reside in `CompactionPolicyTests.cs`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no network access is used
- **Mocking**: None required
- **Isolation**: Each test constructs its own policy

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any default verbatim-tail drift, any custom positive value not preserved, or
any non-positive value accepted constitutes a failure.

### Test Scenarios

#### AgentKitSessions-CompactionPolicy-VerbatimTurns: The Defaults Are the Documented Design

**Tests**:

- `CompactionPolicy_Default_CarriesThePublishedVerbatimTurns`
- `CompactionPolicy_Construct_SetsVerbatimTurns`

Asserts the default policy carries the published verbatim turn count and that a custom positive
count replaces it. The requirement identifier is retained for traceability, but the verified public
control is `VerbatimTurns`.

#### AgentKitSessions-CompactionPolicy-VerbatimTurns: No Supplied Collection Exists

N/A - the redesigned policy no longer accepts a list. There is no caller-owned collection to mutate;
`CompactionPolicy_Construct_SetsVerbatimTurns` covers the only supplied value.

#### AgentKitSessions-CompactionPolicy-ValidatedDefaults: One Control Can Be Replaced Alone

**Test**: `CompactionPolicy_Default_IsShared`

Asserts the default policy is a shared instance. A caller can compare by reference to determine that
no host configuration replaced the default.

#### AgentKitSessions-CompactionPolicy-RejectsUnworkableConfiguration: Unworkable Policies Are Refused

**Test**: `CompactionPolicy_Construct_NonPositive_Throws`

Rejects zero and negative verbatim-tail values. A policy that kept no recent turns verbatim would
immediately age every exchange and would not preserve the direct conversational tail.
