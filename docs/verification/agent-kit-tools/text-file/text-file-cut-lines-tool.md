### TextFileCutLinesTool Unit Verification Design

This document describes the unit-level verification strategy for the `TextFileCutLinesTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses real `PathPolicy`, real files and a real line
buffer; removed ranges, buffer capture and denials are checked through `InvokeAsync`. This keeps
verification at the same boundary the runtime or composing application uses, rather than proving a
substitute behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `TextFile/TextFileCutLinesToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary file tree containing the files and directories it
  needs
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 5 requirement scenarios below, covering 5 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, wrong capability or tool order, ignored policy
decision, leaked path, unsafe file mutation, malformed request thrown as a framework error, or
returned content that violates a configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-TextFile-CutTool-ToolName: Tool Name

**Test**: `TextFileCutLinesTool_ToolName_Constant_IsTheFamilyQualifiedName`

The listed tests prove the published tool name is the family-qualified name the pack claims.

##### AgentKitTools-TextFile-CutTool-GuardedConstruction: Guarded Construction

**Test**: `TextFileCutLinesTool_Create_NullArguments_ThrowArgumentNullException`

The listed tests prove a missing policy or buffer is a programming error rather than a denial.

##### AgentKitTools-TextFile-CutTool-CapturesAndRemoves: Captures And Removes

**Test**: `TextFileCutLinesTool_Cut_Range_RemovesLinesAndReportsCountBoundsAndNewTotal`

**Test**: `TextFileCutLinesTool_Cut_ThroughEndOfFile_ReportsTheCutReachedTheEnd`

The listed tests prove a range of lines is removed and the confirmation names the count, the first
and last captured line, the file's new total line count, and which line now sits where the removal
began — or that the cut reached the end of the file, when no line does.

##### AgentKitTools-TextFile-CutTool-RangeValidation: Range Validation

**Test**: `TextFileCutLinesTool_Cut_OutOfRange_ReturnsDenialAndLeavesFileUnchanged`

The listed tests prove an out-of-range cut is refused and changes nothing.

##### AgentKitTools-TextFile-CutTool-PolicyGoverned: Policy Governed

**Test**: `TextFileCutLinesTool_Cut_ReadOnlyLocation_ReturnsDenial`

The listed tests prove a cut in a read-only location is refused.
