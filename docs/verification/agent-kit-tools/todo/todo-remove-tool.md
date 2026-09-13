### TodoRemoveTool Unit Verification Design

This document describes the unit-level verification strategy for the `TodoRemoveTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses a fresh `TodoStore` and the real tool; the tool's
name, guarded construction, the drop-and-report path, the miss refusal that names the list's
contents, the empty-list refusal and the refusal of a missing identifier are exercised through
`InvokeAsync`. This keeps verification at the same boundary the runtime or composing application
uses, rather than proving a substitute behaves consistently with itself.

Unit tests reside in `Todo/TodoRemoveToolTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates a fresh in-memory store
- **Isolation**: Each test constructs its own store and tool; no state is shared

#### Acceptance Criteria

A unit test run passes when all 6 requirement scenarios below, covering 6 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, a miss reported as success, a refusal that names
another tool or an empty collection, a mandatory argument being thrown rather than refused, or a
refusal that mutates the list constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Todo-RemoveTool-ToolName: Tool Name

**Test**: `TodoRemoveTool_Create_CarriesTheNameAndDescription`

The listed tests prove the published tool name is the family-qualified name the pack claims and
that a non-empty description is carried.

##### AgentKitTools-Todo-RemoveTool-GuardedConstruction: Guarded Construction

**Test**: `TodoRemoveTool_Create_NullStore_ThrowsArgumentNullException`

The listed tests prove a missing store is a programming error rather than a denial, because a
tool with no list to remove from cannot be built.

##### AgentKitTools-Todo-RemoveTool-KnownIdentifier: Known Identifier

**Test**: `TodoRemoveTool_Remove_KnownIdentifier_DropsItAndReportsTheCount`

The listed tests prove removing a step drops it and reports the new size of the list, using the
singular noun when one task remains.

##### AgentKitTools-Todo-RemoveTool-UnknownIdentifier: Unknown Identifier

**Test**: `TodoRemoveTool_Remove_UnknownIdentifier_IsRefusedAndNamesWhatTheListHolds`

The listed tests prove an identifier the list does not hold is refused as `TargetNotFound` with a
plain statement of fact that names the identifiers the list does hold, prescribes no other tool
and guesses at no identifier — the analogue of the text family's empty-buffer refusal.

##### AgentKitTools-Todo-RemoveTool-EmptyList: Empty List

**Test**: `TodoRemoveTool_Remove_EmptyList_StatesTheListIsEmpty`

The listed tests prove a removal against an empty list is refused with a message stating that the
list is empty rather than one ending with an empty collection.

##### AgentKitTools-Todo-RemoveTool-MissingIdentifier: Missing Identifier

**Test**: `TodoRemoveTool_Remove_MissingIdentifier_IsRefused`

The listed tests prove a request that names no identifier is returned as an `InvalidRequest`
refusal rather than an exception that would end the agent's turn, and that the list is not
touched.
