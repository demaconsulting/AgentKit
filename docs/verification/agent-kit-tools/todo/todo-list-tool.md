### TodoListTool Unit Verification Design

This document describes the unit-level verification strategy for the `TodoListTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses a fresh `TodoStore` and the real tool; the tool's
name, guarded construction, empty-list result and populated-list result are checked through
`InvokeAsync`. This keeps verification at the same boundary the runtime or composing application
uses, rather than proving a substitute behaves consistently with itself.

Unit tests reside in `Todo/TodoListToolTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates a fresh in-memory store
- **Isolation**: Each test constructs its own store and tool; no state is shared

#### Acceptance Criteria

A unit test run passes when all 4 requirement scenarios below, covering 4 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, an empty list surfaced as a refusal, or a populated
list reported in an order that differs from the order the items were written down constitutes a
failure.

#### Test Scenarios

##### AgentKitTools-Todo-ListTool-ToolName: Tool Name

**Test**: `TodoListTool_Create_CarriesTheNameAndDescription`

The listed tests prove the published tool name is the family-qualified name the pack claims and
that a non-empty description is carried.

##### AgentKitTools-Todo-ListTool-GuardedConstruction: Guarded Construction

**Test**: `TodoListTool_Create_NullStore_ThrowsArgumentNullException`

The listed tests prove a missing store is a programming error rather than a denial, because a
tool with no list to report cannot be built.

##### AgentKitTools-Todo-ListTool-EmptyList: Empty List

**Test**: `TodoListTool_List_EmptyList_ReportsNoItems`

The listed tests prove an empty list is reported as an empty structured result rather than a
refusal, because a true answer is more useful than a denial that would invite the model to
conclude the tool is broken.

##### AgentKitTools-Todo-ListTool-StructuredResult: Structured Result

**Test**: `TodoListTool_List_PopulatedList_ReportsItemsInOrder`

The listed tests prove the tool reports the item count and every field of every item — `id`,
`title`, `status` and `note` — in the order the steps were written down, keeping an in-place
update at its original position.
