### TodoSetTool Unit Verification Design

This document describes the unit-level verification strategy for the `TodoSetTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses a fresh `TodoStore` and the real tool; the tool's
name, guarded construction, default-status behavior, in-place update, every published status, and
refusal of an unknown status or a missing mandatory argument are exercised through `InvokeAsync`.
This keeps verification at the same boundary the runtime or composing application uses, rather
than proving a substitute behaves consistently with itself.

Unit tests reside in `Todo/TodoSetToolTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates a fresh in-memory store
- **Isolation**: Each test constructs its own store and tool; no state is shared

#### Acceptance Criteria

A unit test run passes when all 7 requirement scenarios below, covering 7 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, an omitted status that is not recorded as
`pending`, an existing identifier that appends rather than updates in place, a status the family
does not publish being silently accepted, a mandatory argument being thrown rather than refused,
or a refusal that mutates the list constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Todo-SetTool-ToolName: Tool Name

**Test**: `TodoSetTool_Create_CarriesTheNameAndDescription`

The listed tests prove the published tool name is the family-qualified name the pack claims and
that a non-empty description is carried.

##### AgentKitTools-Todo-SetTool-GuardedConstruction: Guarded Construction

**Test**: `TodoSetTool_Create_NullStore_ThrowsArgumentNullException`

The listed tests prove a missing store is a programming error rather than a denial, because a
tool with no list to write to cannot be built.

##### AgentKitTools-Todo-SetTool-DefaultStatus: Default Status

**Test**: `TodoSetTool_Set_OmittedStatus_RecordsPendingAndReportsTheCount`

The listed tests prove an omitted status records `pending` — writing a step down is planning it,
not starting it — and the result states the identifier, the recorded status and the size of the
list.

##### AgentKitTools-Todo-SetTool-ExistingIdentifierUpdates: Existing Identifier Updates

**Test**: `TodoSetTool_Set_ExistingIdentifier_UpsertsAndKeepsTheCount`

The listed tests prove a write against an identifier the list already holds updates that task in
place, keeping the reported count the same and preserving the note the caller supplied.

##### AgentKitTools-Todo-SetTool-EveryStatusAccepted: Every Status Accepted

**Test**: `TodoSetTool_Set_EveryPublishedStatus_IsRecorded`

The listed tests prove every one of the four published statuses can be recorded, so a step can
run its whole life cycle from `pending` through `in_progress` and `done`, or into `blocked` and
back out again.

##### AgentKitTools-Todo-SetTool-UnknownStatusRefused: Unknown Status Refused

**Test**: `TodoSetTool_Set_UnknownStatus_IsRefusedAndLeavesTheListUnchanged`

The listed tests prove a status outside the published vocabulary is returned as an
`InvalidRequest` refusal naming the four permitted statuses, and that the list is not written to.

##### AgentKitTools-Todo-SetTool-MissingArgumentsRefused: Missing Arguments Refused

**Test**: `TodoSetTool_Set_MissingIdentifierOrTitle_IsRefused`

The listed tests prove a request missing an identifier or a title is returned as an
`InvalidRequest` refusal rather than an exception that would end the agent's turn, and that
neither malformed request writes to the list.
