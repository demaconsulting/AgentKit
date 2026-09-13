### FileDeleteTool Unit Verification Design

This document describes the unit-level verification strategy for the `FileDeleteTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses real `PathPolicy` and real files; deletion,
directory refusal, read-only refusal and missing-file refusals are checked through `InvokeAsync`.
This keeps verification at the same boundary the runtime or composing application uses, rather than
proving a substitute behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `File/FileDeleteToolTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
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

##### AgentKitTools-File-DeleteTool-ToolName: Tool Name

**Test**: `FileDeleteTool_ToolName_Constant_IsTheFamilyQualifiedName`

The listed tests prove the published tool name is the family-qualified name the pack claims, and is
a valid name even though the bare verb 'delete' is reserved.

##### AgentKitTools-File-DeleteTool-GuardedConstruction: Guarded Construction

**Test**: `FileDeleteTool_Create_NullPolicy_ThrowsArgumentNullException`

The listed tests prove a missing policy is a programming error rather than a denial.

##### AgentKitTools-File-DeleteTool-DeletesFile: Deletes File

**Test**: `FileDeleteTool_Delete_PermittedFile_RemovesIt`

The listed tests prove a permitted single file is deleted.

##### AgentKitTools-File-DeleteTool-RefusesDirectory: Refuses Directory

**Test**: `FileDeleteTool_Delete_DirectoryPath_ReturnsDenialAndLeavesItInPlace`

The listed tests prove a directory is refused and left in place: the tool never deletes a directory
or recurses.

##### AgentKitTools-File-DeleteTool-PolicyGoverned: Policy Governed

**Test**: `FileDeleteTool_Delete_ReadOnlyLocation_ReturnsDenialAndLeavesTheFile`

The listed tests prove a deletion outside the write grant is refused.

##### AgentKitTools-File-DeleteTool-MissingFile: Missing File

**Test**: `FileDeleteTool_Delete_MissingFile_ReturnsDenialNamingNoTool`

The listed tests prove a missing file is reported rather than treated as a silent success, with a
plain fact that names no other tool.
