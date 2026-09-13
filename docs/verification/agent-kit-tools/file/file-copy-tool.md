### FileCopyTool Unit Verification Design

This document describes the unit-level verification strategy for the `FileCopyTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses real `PathPolicy` and real files; source and
destination state, overwrite behavior and endpoint policy decisions are checked through
`InvokeAsync`. This keeps verification at the same boundary the runtime or composing application
uses, rather than proving a substitute behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `File/FileCopyToolTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
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

##### AgentKitTools-File-CopyTool-ToolName: Tool Name

**Test**: `FileCopyTool_ToolName_Constant_IsTheFamilyQualifiedName`

The listed tests prove the published tool name is the family-qualified name the pack claims.

##### AgentKitTools-File-CopyTool-GuardedConstruction: Guarded Construction

**Test**: `FileCopyTool_Create_NullPolicy_ThrowsArgumentNullException`

The listed tests prove a missing policy is a programming error rather than a denial.

##### AgentKitTools-File-CopyTool-Copies: Copies

**Test**: `FileCopyTool_Copy_RelativePaths_CopiesTheFileContent`

The listed tests prove a permitted copy addressed by bare relative names duplicates the file's
content.

##### AgentKitTools-File-CopyTool-PerEndpointPolicy: Per Endpoint Policy

**Test**: `FileCopyTool_Copy_ReadOnlySourceToWritableDestination_IsPermitted`

**Test**: `FileCopyTool_Copy_DestinationOutsideWriteGrant_ReturnsDenial`

The listed tests prove a source a read grant permits but a write grant does not can still be copied
into a writable location — the two decisions are judged per endpoint; a copy whose destination a
write grant does not cover is refused.

##### AgentKitTools-File-CopyTool-NoClobber: No Clobber

**Test**: `FileCopyTool_Copy_ExistingDestinationWithoutOverwrite_ReturnsDenialAndLeavesItUnchanged`

**Test**: `FileCopyTool_Copy_ExistingDestinationWithOverwrite_ReplacesIt`

The listed tests prove a copy refuses to overwrite an existing destination unless overwrite is true;
a copy overwrites an existing destination when overwrite is explicitly true.

##### AgentKitTools-File-CopyTool-MissingSource: Missing Source

**Test**: `FileCopyTool_Copy_MissingSource_ReturnsDenialNamingNoTool`

The listed tests prove a missing source is refused with a plain fact that names no other tool.
