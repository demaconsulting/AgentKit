### TextFileSearchTool Unit Verification Design

This document describes the unit-level verification strategy for the `TextFileSearchTool` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses real `PathPolicy` enumeration and real files; grep
output, search options, binary skips and link escapes are checked through `InvokeAsync`. This keeps
verification at the same boundary the runtime or composing application uses, rather than proving a
substitute behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `TextFile/TextFileSearchToolTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary file tree containing the files and directories it
  needs
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 12 requirement scenarios below, covering 17 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, wrong capability or tool order, ignored policy
decision, leaked path, unsafe file mutation, malformed request thrown as a framework error, or
returned content that violates a configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-TextFile-SearchTool-ToolName: Tool Name

**Test**: `TextFileSearchTool_ToolName_Constant_IsTheFamilyQualifiedName`

The listed tests prove the published tool name is the family-qualified name the pack claims.

##### AgentKitTools-TextFile-SearchTool-GuardedConstruction: Guarded Construction

**Test**: `TextFileSearchTool_Create_NullPolicy_ThrowsArgumentNullException`

The listed tests prove a missing policy is a programming error rather than a denial.

##### AgentKitTools-TextFile-SearchTool-PolicyFiltered: Policy Filtered

**Test**: `TextFileSearchTool_Search_FileBeneathLinkOutsideRoot_IsNeverSurfaced`

**Test**: `TextFileSearchTool_Search_BinaryFile_IsSkipped`

The listed tests prove a file reachable only through a link outside the grants is never surfaced by
a search — not its content, not its path, not its existence; a binary file is skipped rather than
searched or disclosed.

##### AgentKitTools-TextFile-SearchTool-Scoped: Scoped

**Test**: `TextFileSearchTool_Search_FilePattern_RestrictsToMatchingFiles`

**Test**: `TextFileSearchTool_Search_DirectoryOutsideGrants_ReturnsDenial`

The listed tests prove a file glob restricts the search to matching files; a directory outside the
grants is refused rather than searched silently.

##### AgentKitTools-TextFile-SearchTool-GrepOutput: Grep Output

**Test**: `TextFileSearchTool_Search_Match_ReportsPathLineContent`

The listed tests prove a match is reported grep-style as path:line:content, in the caller's dialect.

##### AgentKitTools-TextFile-SearchTool-ContextLines: Context Lines

**Test**: `TextFileSearchTool_Search_WithContextLines_ShowsSurroundingLines`

The listed tests prove context lines are shown with a dash separator around a colon-separated match.

##### AgentKitTools-TextFile-SearchTool-Matching: Matching

**Test**: `TextFileSearchTool_Search_LiteralByDefault_MatchesMetacharactersAsText`

**Test**: `TextFileSearchTool_Search_RegexMode_MatchesTheExpression`

**Test**: `TextFileSearchTool_Search_IgnoreCase_MatchesRegardlessOfCase`

The listed tests prove the search is literal by default, so a regex metacharacter matches itself; a
regular-expression search matches when literal is false; a case-insensitive search matches
regardless of case.

##### AgentKitTools-TextFile-SearchTool-MaxMatches: Max Matches

**Test**: `TextFileSearchTool_Search_MaxMatches_CapsTheReportedMatches`

The listed tests prove maxMatches caps the number of reported matches.

##### AgentKitTools-TextFile-SearchTool-EmptyResult: Empty Result

**Test**: `TextFileSearchTool_Search_NoMatch_ReturnsNoMatchesNotADenial`

**Test**: `TextFileSearchTool_Search_EmptyDirectory_ReturnsNoMatches`

The listed tests prove a search that matches nothing is a fact, not a refusal; a search over an
empty directory returns no matches without a refusal.

##### AgentKitTools-TextFile-SearchTool-InvalidRegex: Invalid Regex

**Test**: `TextFileSearchTool_Search_InvalidRegex_ReturnsDenialWithoutThrowing`

The listed tests prove an invalid regular expression is a returned refusal, not a thrown error.

##### AgentKitTools-TextFile-SearchTool-LargeFileSearchable: Large File Searchable

**Test**: `TextFileSearchTool_Search_MatchInsideLargeFile_IsReported`

The listed tests prove a match inside a file larger than the read ceiling is reported rather than the
file being silently skipped for size.

##### AgentKitTools-TextFile-SearchTool-BoundedResult: Bounded Result

**Test**: `TextFileSearchTool_Search_MaxMatches_CapsTheReportedMatches`

The listed tests prove maxMatches caps the number of reported matches.
