### MarkdownOutlineTool Unit Verification Design

This document describes the unit-level verification strategy for the `MarkdownOutlineTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses real `PathPolicy` and real Markdown files; heading
ranges, max depth, empty outlines and denials are checked through `InvokeAsync`. This keeps
verification at the same boundary the runtime or composing application uses, rather than proving a
substitute behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `Markdown/MarkdownOutlineToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary file tree containing the files and directories it
  needs
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 8 requirement scenarios below, covering 8 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, wrong capability or tool order, ignored policy
decision, leaked path, unsafe file mutation, malformed request thrown as a framework error, or
returned content that violates a configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Markdown-OutlineTool-ToolName: Tool Name

**Test**: `MarkdownOutlineTool_ToolName_Constant_IsTheFamilyQualifiedName`

The listed tests prove the published tool name is the family-qualified name the pack claims.

##### AgentKitTools-Markdown-OutlineTool-GuardedConstruction: Guarded Construction

**Test**: `MarkdownOutlineTool_Create_NullPolicy_ThrowsArgumentNullException`

The listed tests prove a missing policy is a programming error rather than a denial.

##### AgentKitTools-Markdown-OutlineTool-Sections: Sections

**Test**: `MarkdownOutlineTool_Outline_KnownFile_ReportsSectionsWithLineRanges`

The listed tests prove the outline reports each section's level, title, and 1-based line range,
skipping a fenced hash.

##### AgentKitTools-Markdown-OutlineTool-MaxDepth: Max Depth

**Test**: `MarkdownOutlineTool_Outline_MaxDepth_FiltersDeeperHeadings`

The listed tests prove maxDepth filters which headings are reported while section ends stay correct.

##### AgentKitTools-Markdown-OutlineTool-EmptyOutline: Empty Outline

**Test**: `MarkdownOutlineTool_Outline_FileWithoutHeadings_ReturnsEmptyOutline`

The listed tests prove a file with no headings is an empty outline, not a refusal.

##### AgentKitTools-Markdown-OutlineTool-PolicyGoverned: Policy Governed

**Test**: `MarkdownOutlineTool_Outline_PathOutsideGrants_ReturnsDenial`

The listed tests prove a path outside the grants is refused with a returned denial.

##### AgentKitTools-Markdown-OutlineTool-LargeDocument: Large Document

**Test**: `MarkdownOutlineTool_Outline_LargeDocument_ReportsHeadingsNotADenial`

The listed tests prove a document larger than the read ceiling is outlined by streaming its headings
rather than refused, with the late heading reported at its correct line range.

##### AgentKitTools-Markdown-OutlineTool-MissingOrDirectory: Missing Or Directory

**Test**: `MarkdownOutlineTool_Outline_MissingFile_ReturnsDenial`

The listed tests prove a missing file is refused as target-not-found rather than throwing.
