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

Unit tests reside in `ToolOutputSearchToolTests.cs` within the `DemaConsulting.AgentKit.Core.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: `Microsoft.Extensions.AI.Abstractions`; no file system or network access
- **Mocking**: None
- **Isolation**: Each test builds its own store and tool

### Acceptance Criteria

A unit test run passes when all nine scenarios below pass. Any match in the wrong shape or with the
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

Error path: a catastrophically backtracking expression is an `InvalidRequest` refusal naming the
timeout.

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
