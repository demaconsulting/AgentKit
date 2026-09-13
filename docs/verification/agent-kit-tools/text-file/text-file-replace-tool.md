### TextFileReplaceTool Unit Verification Design

This document describes the unit-level verification strategy for the `TextFileReplaceTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses real `PathPolicy` and real files; unique
replacement, ambiguity, insertions, deletions and denials are checked through `InvokeAsync`. This
keeps verification at the same boundary the runtime or composing application uses, rather than
proving a substitute behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `TextFile/TextFileReplaceToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary file tree containing the files and directories it
  needs
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 7 requirement scenarios below, covering 9 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, wrong capability or tool order, ignored policy
decision, leaked path, unsafe file mutation, malformed request thrown as a framework error, or
returned content that violates a configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-TextFile-ReplaceTool-ToolName: Tool Name

**Test**: `TextFileReplaceTool_ToolName_Constant_IsTheFamilyQualifiedName`

The listed tests prove the published tool name is the family-qualified name the pack claims.

##### AgentKitTools-TextFile-ReplaceTool-GuardedConstruction: Guarded Construction

**Test**: `TextFileReplaceTool_Create_NullPolicy_ThrowsArgumentNullException`

The listed tests prove a missing policy is a programming error rather than a denial.

##### AgentKitTools-TextFile-ReplaceTool-UniqueMatch: Unique Match

**Test**: `TextFileReplaceTool_Replace_UniqueMatch_ReplacesItAndReportsDelta`

The listed tests prove a unique match is replaced and the confirmation names the line-count change.

##### AgentKitTools-TextFile-ReplaceTool-Ambiguity: Ambiguity

**Test**: `TextFileReplaceTool_Replace_NotFound_ReturnsDenialSayingNotFound`

**Test**: `TextFileReplaceTool_Replace_AppearsMultipleTimes_ReturnsDenialNamingCountAndAsksForContext`

The listed tests prove a not-found match is refused, and the refusal says the text was not found; an
ambiguous match is refused, naming the count and asking for more surrounding lines, and the file is
left unchanged.

##### AgentKitTools-TextFile-ReplaceTool-InsertAndDelete: Insert And Delete

**Test**: `TextFileReplaceTool_Replace_EmptyNewText_DeletesTheMatchedText`

**Test**: `TextFileReplaceTool_Replace_SurroundingContext_InsertsBetweenLines`

The listed tests prove an empty replacement deletes the matched text; an insertion is expressed by
including surrounding text in both old and new text.

##### AgentKitTools-TextFile-ReplaceTool-PolicyGoverned: Policy Governed

**Test**: `TextFileReplaceTool_Replace_ReadOnlyLocation_ReturnsDenial`

The listed tests prove an edit in a read-only location is refused.

##### AgentKitTools-TextFile-ReplaceTool-MissingFile: Missing File

**Test**: `TextFileReplaceTool_Replace_MissingFile_ReturnsDenialNamingNoTool`

The listed tests prove a missing file is refused with a plain fact that names no other tool.
