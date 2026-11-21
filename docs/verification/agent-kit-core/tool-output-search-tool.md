## ToolOutputSearchTool Unit Verification Design

This document describes the unit-level verification strategy for the `ToolOutputSearchTool` class.

### Verification Approach

`ToolOutputSearchTool` is verified through unit tests that retain an output in a real
`ToolOutputStore`, create the tool over it, and invoke it as a runtime would. The store,
`GuardedToolFactory`, `ToolResult` and `ToolLimits` are real dependencies, all documented in
*ToolOutputSearchTool Unit Design*. Results are asserted as exact text, so the grep-style shape is
pinned.

The timeout scenario uses a nested quantifier against a line that makes it backtrack
catastrophically; it therefore takes about one second by design, which is the bound under test.
The total search budget is verified without waiting, by passing `SearchEntry` a budget already
spent.

Unit tests reside in `ToolOutputSearchToolTests.cs` within the `DemaConsulting.AgentKit.Core.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: `Microsoft.Extensions.AI.Abstractions`; no file system or network access
- **Mocking**: A counting double for a retained output's lines (`ProbeLines`), so a scenario can
  assert how many lines a search reads before it stops
- **Isolation**: Each test builds its own store and tool

### Acceptance Criteria

A unit test run passes when all twelve scenarios below pass. Any match in the wrong shape or with the
wrong number, any ignored option, any pathological expression that is not refused, any result over
the ceiling returned, or any malformed request answered other than by a refusal constitutes a
failure.

### Test Scenarios

#### AgentKitCore-ToolOutputSearchTool-ToolNameNoPath: The Tool Carries Its Name and No Path

**Test**: `ToolOutputSearchTool_Create_PublishesTheDocumentedNameWithNoPathParameter`

Security: the tool is named `tool_output_search` and has `id` and `pattern` parameters but no
`path`.

#### AgentKitCore-ToolOutputSearchTool-LiteralByDefault: A Literal Search Reports Lines by Number

**Test**: `ToolOutputSearchTool_Search_Literal_ReportsMatchingLinesByNumber`

Normal operation: only the exact-case line matches, reported as `2:beta ERROR one`.

#### AgentKitCore-ToolOutputSearchTool-RegularExpression: An Expression Ignoring Case Finds Every Match

**Test**: `ToolOutputSearchTool_Search_RegexIgnoringCase_FindsEveryMatch`

Normal operation: both differently cased lines match.

#### AgentKitCore-ToolOutputSearchTool-ContextAndCap: Context Is Shown Grep-Style

**Test**: `ToolOutputSearchTool_Search_ContextLines_AreShownGrepStyle`

Normal operation: matches use `:`, context uses `-`, and a `--` line separates distant runs.

#### AgentKitCore-ToolOutputSearchTool-ContextAndCap: The Cap and No Matches Are Honored

**Test**: `ToolOutputSearchTool_Search_MaxMatchesAndNoMatches_AreHonored`

Boundary values: a cap of one returns the first match only; an absent pattern returns `No matches.`.

#### AgentKitCore-ToolOutputSearchTool-PathologicalPatternRefused: A Timeout Is Refused

**Test**: `ToolOutputSearchTool_Search_PathologicalRegex_IsRefusedOnTimeout`

Error path: a catastrophically backtracking expression is the exact `InvalidRequest` refusal
`The search pattern took longer than 1 second to evaluate against one line of {id}.`.

#### AgentKitCore-ToolOutputSearchTool-PathologicalPatternRefused: A Spent Search Budget Is Refused

**Test**: `ToolOutputSearchTool_SearchEntry_SpentSearchBudget_IsRefusedOnTimeout`

Error path: a literal search of a million-line output through `SearchEntry` with a zero budget is
the exact `InvalidRequest` refusal `The search took longer than 0 second to run against output-1.`
— worded for the search as a whole, not for a regular expression — after reading at most one line,
though no single line is slow to evaluate.

#### AgentKitCore-ToolOutputSearchTool-InvalidRequestsRefused: Malformed Requests Are Refused

**Test**: `ToolOutputSearchTool_Search_InvalidRequests_AreRefused`

Error path: an unbalanced expression, a missing pattern and negative context are `InvalidRequest`
refusals.

#### AgentKitCore-ToolOutputSearchTool-InvalidRequestsRefused: An Unknown Identifier Is Refused

**Test**: `ToolOutputSearchTool_Search_UnknownId_IsRefused`

Error path: an identifier never issued is a `TargetNotFound` refusal.

#### AgentKitCore-ToolOutputSearchTool-BoundedResult: A Result Over the Ceiling Is Refused

**Test**: `ToolOutputSearchTool_Search_ResultOverCeiling_IsRefused`

Error path: matching every line of a large output under a small ceiling is a `ResourceTooLarge`
refusal naming it.

#### AgentKitCore-ToolOutputSearchTool-BoundedResult: An Oversized Result Reads Only What the Ceiling Admits

**Test**: `ToolOutputSearchTool_SearchEntry_ResultOverCeiling_ReadsOnlyWhatTheCeilingAdmits`

Boundary value: matching every line of a million-line output through `SearchEntry` under a
1,000-character ceiling is the unchanged `ResourceTooLarge` refusal after reading no more than
1,000 lines through the counting double, not the whole output.

#### AgentKitCore-ToolOutputSearchTool-BoundedResult: A Result Exactly at the Ceiling Is Returned

**Test**: `ToolOutputSearchTool_Search_ResultExactlyAtCeiling_IsReturnedAndOneOverIsRefused`

Boundary value: a listing with context and a `--` separator is returned when the ceiling equals its
exact size, and refused when the ceiling is one character less.
