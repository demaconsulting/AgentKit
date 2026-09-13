### TextFileCopyLinesTool Unit Verification Design

This document describes the unit-level verification strategy for the `TextFileCopyLinesTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses real `PathPolicy`, real files and a real line
buffer; captured ranges, buffer contents and denials are checked through `InvokeAsync`. This keeps
verification at the same boundary the runtime or composing application uses, rather than proving a
substitute behaves consistently with itself.

Returned results and file system state are both checked. Because a copy must never mutate its source,
every success and every refusal asserts the source file is left byte-for-byte identical — comparing
exact bytes, not merely the line count — and a captured range is confirmed by pasting it and checking
the destination reproduces it exactly.

Unit tests reside in `TextFile/TextFileCopyLinesToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary file tree containing the files and directories it
  needs
- **Isolation**: Each test constructs its own policy, tool, buffer or helper state; no state is
  shared

#### Acceptance Criteria

A unit test run passes when all 5 requirement scenarios below, covering 11 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, ignored policy decision, leaked path, mutated source,
a copy refused from a read-only location, a malformed request thrown as a framework error, or
returned content that violates a configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-TextFile-CopyTool-ToolName: Tool Name

**Test**: `TextFileCopyLinesTool_ToolName_Constant_IsTheFamilyQualifiedName`

The listed tests prove the published tool name is the family-qualified name the pack claims.

##### AgentKitTools-TextFile-CopyTool-GuardedConstruction: Guarded Construction

**Test**: `TextFileCopyLinesTool_Create_NullArguments_ThrowArgumentNullException`

The listed tests prove a missing policy or buffer is a programming error rather than a denial.

##### AgentKitTools-TextFile-CopyTool-CapturesUnchanged: Captures And Leaves Unchanged

**Test**: `TextFileCopyLinesTool_Copy_Range_CapturesReportsAndLeavesSourceByteIdentical`

**Test**: `TextFileCopyLinesTool_Copy_WholeFile_CapturesAll`

**Test**: `TextFileCopyLinesTool_Copy_ThenPasteAtLine_ReproducesContentExactly`

**Test**: `TextFileCopyLinesTool_Copy_ThenPasteTwice_ProducesTwoCopies`

**Test**: `TextFileCopyLinesTool_Copy_RecaptureSameSlot_ReplacesContent`

**Test**: `TextFileCopyLinesTool_Copy_ThenCutSameSlot_CutReplacesTheCopiedSlot`

The listed tests prove a range is captured into a named buffer and reported with the count, the
first and last captured line and a statement that the source is unchanged, while the source file is
left byte-identical; a whole-file range captures every line; a copy then paste reproduces the block
exactly; a non-consuming slot pastes twice into two copies; a later capture into the same slot
replaces it; and a cut into a copied slot replaces the copied text on paste while the copy's source
stays byte-identical.

##### AgentKitTools-TextFile-CopyTool-RangeValidation: Range Validation

**Test**: `TextFileCopyLinesTool_Copy_OutOfRange_ReturnsDenialAndLeavesFileUnchanged`

The listed tests prove an out-of-range copy — start beyond end, past the end of the file, zero or
negative — is refused, captures nothing, and leaves the source byte-identical.

##### AgentKitTools-TextFile-CopyTool-PolicyGoverned: Policy Governed

**Test**: `TextFileCopyLinesTool_Copy_ReadOnlyLocation_Succeeds`

**Test**: `TextFileCopyLinesTool_Copy_NonPermittedPath_ReturnsDenial`

The listed tests prove the tool consults the read decision alone, so a copy from a read-only location
succeeds — the deliberate inverse of the cut tool's read-only denial — while a copy from a path
outside every grant is refused.
