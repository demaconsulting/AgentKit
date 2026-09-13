### FileListTool Unit Verification Design

This document describes the unit-level verification strategy for the `FileListTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses real `PathPolicy` and real files; omitted directory
discovery, glob filtering, type-agnostic listing and denials are checked through `InvokeAsync`. This
keeps verification at the same boundary the runtime or composing application uses, rather than
proving a substitute behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `File/FileListToolTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary file tree containing the files and directories it
  needs
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 6 requirement scenarios below, covering 8 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, wrong capability or tool order, ignored policy
decision, leaked path, unsafe file mutation, malformed request thrown as a framework error, or
returned content that violates a configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-File-ListTool-ToolName: Tool Name

**Test**: `FileListTool_ToolName_Constant_IsTheFamilyQualifiedName`

**Test**: `FileListTool_Create_ConstructedTool_CarriesTheToolNameAndADescription`

The listed tests prove the published tool name is the family-qualified name the pack claims; the
constructed tool carries the published name and a description.

##### AgentKitTools-File-ListTool-GuardedConstruction: Guarded Construction

**Test**: `FileListTool_Create_NullPolicy_ThrowsArgumentNullException`

The listed tests prove a missing policy is a programming error rather than a denial.

##### AgentKitTools-File-ListTool-TypeAgnostic: Type Agnostic

**Test**: `FileListTool_List_TypeAgnostic_ReportsFilesOfEveryType`

The listed tests prove a listing of any file type is returned, not only text files.

##### AgentKitTools-File-ListTool-Discovery: Discovery

**Test**: `FileListTool_List_OmittedDirectory_ListsEveryPermittedLocationIncludingEmpty`

**Test**: `FileListTool_List_EmptyDirectory_ReturnsNoFilesMatchedNotADenial`

The listed tests prove an omitted directory lists every permitted location, including an empty one;
an empty directory is reported as a fact, not refused.

##### AgentKitTools-File-ListTool-Glob: Glob

**Test**: `FileListTool_List_GlobPattern_RestrictsToMatchingFiles`

The listed tests prove a glob pattern restricts the listing, and a recursive prefix is honored.

##### AgentKitTools-File-ListTool-PolicyGoverned: Policy Governed

**Test**: `FileListTool_List_DirectoryOutsideGrants_ReturnsDenial`

The listed tests prove a directory outside the grants is refused, disclosing the permitted location.
