### FileMoveTool Unit Verification Design

This document describes the unit-level verification strategy for the `FileMoveTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses real `PathPolicy` and real files; removed sources,
protected destinations and write-governed endpoints are checked through `InvokeAsync`. This keeps
verification at the same boundary the runtime or composing application uses, rather than proving a
substitute behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `File/FileMoveToolTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary file tree containing the files and directories it
  needs
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 6 requirement scenarios below, covering 6 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, wrong capability or tool order, ignored policy
decision, leaked path, unsafe file mutation, malformed request thrown as a framework error, or
returned content that violates a configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-File-MoveTool-ToolName: Tool Name

**Test**: `FileMoveTool_ToolName_Constant_IsTheFamilyQualifiedName`

The listed tests prove the published tool name is the family-qualified name the pack claims.

##### AgentKitTools-File-MoveTool-GuardedConstruction: Guarded Construction

**Test**: `FileMoveTool_Create_NullPolicy_ThrowsArgumentNullException`

The listed tests prove a missing policy is a programming error rather than a denial.

##### AgentKitTools-File-MoveTool-Moves: Moves

**Test**: `FileMoveTool_Move_RelativePaths_MovesTheFileAndRemovesTheSource`

The listed tests prove a permitted move relocates the file and removes the source.

##### AgentKitTools-File-MoveTool-PolicyGoverned: Policy Governed

**Test**: `FileMoveTool_Move_ReadOnlySource_ReturnsDenialAndLeavesItInPlace`

The listed tests prove a move of a read-only source is refused: a move needs write permission on the
source it removes.

##### AgentKitTools-File-MoveTool-NoClobber: No Clobber

**Test**: `FileMoveTool_Move_ExistingDestinationWithoutOverwrite_ReturnsDenialAndLeavesBoth`

The listed tests prove a move refuses to overwrite an existing destination unless overwrite is true.

##### AgentKitTools-File-MoveTool-MissingSource: Missing Source

**Test**: `FileMoveTool_Move_MissingSource_ReturnsDenialNamingNoTool`

The listed tests prove a missing source is refused with a plain fact that names no other tool.
