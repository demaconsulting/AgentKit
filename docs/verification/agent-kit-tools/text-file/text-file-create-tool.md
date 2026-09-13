### TextFileCreateTool Unit Verification Design

This document describes the unit-level verification strategy for the `TextFileCreateTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses real `PathPolicy` and real files; created content,
refused existing files and read-only denials are checked through `InvokeAsync`. This keeps
verification at the same boundary the runtime or composing application uses, rather than proving a
substitute behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `TextFile/TextFileCreateToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

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

##### AgentKitTools-TextFile-CreateTool-ToolName: Tool Name

**Test**: `TextFileCreateTool_ToolName_Constant_IsTheFamilyQualifiedName`

The listed tests prove the published tool name is the family-qualified name the pack claims.

##### AgentKitTools-TextFile-CreateTool-GuardedConstruction: Guarded Construction

**Test**: `TextFileCreateTool_Create_NullPolicy_ThrowsArgumentNullException`

The listed tests prove a missing policy is a programming error rather than a denial.

##### AgentKitTools-TextFile-CreateTool-CreatesNew: Creates New

**Test**: `TextFileCreateTool_Create_NewFile_WritesTheContent`

**Test**: `TextFileCreateTool_Create_EmptyContent_CreatesAnEmptyFile`

The listed tests prove a new file is created with the given content, addressed by a bare relative
name; an empty content string creates an empty file rather than being refused.

##### AgentKitTools-TextFile-CreateTool-RefusesExisting: Refuses Existing

**Test**: `TextFileCreateTool_Create_ExistingFile_ReturnsDenialAndLeavesItUnchanged`

**Test**: `TextFileCreateTool_Create_ExistingFile_DenialPrescribesNoRemedy`

**Test**: `TextFileCreateTool_Create_MissingParentDirectory_ReturnsDenial`

The listed tests prove create refuses to replace a file that already exists, stating the fact and
naming no other tool; a missing parent directory is refused rather than materialized.

##### AgentKitTools-TextFile-CreateTool-PolicyGoverned: Policy Governed

**Test**: `TextFileCreateTool_Create_ReadOnlyLocation_ReturnsDenial`

The listed tests prove creation in a read-only location is refused.

##### AgentKitTools-TextFile-CreateTool-MalformedRequest: Malformed Request

**Test**: `TextFileCreateTool_Create_MissingContent_ReturnsDenialWithoutThrowing`

The listed tests prove a missing content argument is refused rather than throwing.
