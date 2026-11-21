## ToolOutputReadTool Unit Verification Design

This document describes the unit-level verification strategy for the `ToolOutputReadTool` class.

### Verification Approach

`ToolOutputReadTool` is verified through unit tests that retain an output in a real
`ToolOutputStore`, create the tool over it, and invoke it as a runtime would. The store,
`GuardedToolFactory`, `ToolResult` and `ToolLimits` are real dependencies, all documented in
*ToolOutputReadTool Unit Design*. Arguments are supplied only when a scenario names them, so an
omitted argument reaches the tool exactly as a model's omission would.

Unit tests reside in `ToolOutputReadToolTests.cs` within the `DemaConsulting.AgentKit.Core.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: `Microsoft.Extensions.AI.Abstractions`; no file system or network access
- **Mocking**: A counting double for a retained output's lines (`ProbeLines`), so a scenario can
  assert how many lines an oversized request reads
- **Isolation**: Each test builds its own store and tool

### Acceptance Criteria

A unit test run passes when all ten scenarios below pass. Any page in the wrong shape, any page
exceeding the ceiling, any truncated page, any path parameter, or any unresolvable request answered
other than by a refusal constitutes a failure.

### Test Scenarios

#### AgentKitCore-ToolOutputReadTool-ToolName: The Tool Carries Its Documented Name

**Test**: `ToolOutputReadTool_Create_PublishesTheDocumentedName`

Normal operation: the tool is named `tool_output_read`, equal to the published constant.

#### AgentKitCore-ToolOutputReadTool-NoPathParameter: The Schema Has No Path

**Test**: `ToolOutputReadTool_Create_SchemaHasNoPathParameter`

Security: the parameters are exactly `id`, `startLine` and `lineCount`.

#### AgentKitCore-ToolOutputReadTool-NumberedPage: A Range Is Numbered Under Its Header

**Test**: `ToolOutputReadTool_Read_Range_ReturnsNumberedLinesUnderHeader`

Normal operation: lines 9-10 of 12 are returned under `output-1 lines 9-10 of 12`, padded to the
width of the total.

#### AgentKitCore-ToolOutputReadTool-NumberedPage: A Window Past the End Is Empty

**Test**: `ToolOutputReadTool_Read_PastTheEnd_ReturnsEmptyWindowNamingTheTotal`

Boundary value: reading from line 10 of a three-line output returns the header alone with the total.

#### AgentKitCore-ToolOutputReadTool-OmittedCountFitsCeiling: An Omitted Count Fills the Ceiling

**Test**: `ToolOutputReadTool_Read_OmittedLineCount_ReturnsWholeLinesWithinCeiling`

Normal operation: with no count, the page starts at line 1, fits the ceiling and stops before the end.

#### AgentKitCore-ToolOutputReadTool-BoundedPage: An Oversized Range Is Refused

**Test**: `ToolOutputReadTool_Read_RangeOverCeiling_IsRefusedNamingTheCeiling`

Error path: a range larger than the ceiling is the exact `ResourceTooLarge` refusal naming the
range and the ceiling and asking for fewer lines.

#### AgentKitCore-ToolOutputReadTool-BoundedPage: A Huge Range Reads Only What the Ceiling Admits

**Test**: `ToolOutputReadTool_ReadEntry_HugeRangeOverCeiling_ReadsOnlyWhatTheCeilingAdmits`

Boundary value: asking `ReadEntry` for `int.MaxValue` lines of a million-line output under a
1,000-character ceiling is refused after reading no more than 1,000 lines through the counting
double, not the whole output.

#### AgentKitCore-ToolOutputReadTool-BoundedPage: A Page Exactly at the Ceiling Is Returned

**Test**: `ToolOutputReadTool_Read_RangeExactlyAtCeiling_IsReturnedAndOneOverIsRefused`

Boundary value: lines 9-10 of 12 are returned when the ceiling equals the page's exact size, and
the same range is refused when the ceiling is one character less.

#### AgentKitCore-ToolOutputReadTool-InvalidRequestsRefused: An Invalid Range Is Refused

**Test**: `ToolOutputReadTool_Read_InvalidRange_IsRefused`

Error path: a zero start and a negative count are `InvalidRequest` refusals.

#### AgentKitCore-ToolOutputReadTool-InvalidRequestsRefused: Unresolvable Identifiers Are Refused

**Test**: `ToolOutputReadTool_Read_UnresolvableIds_AreRefused`

Error path: a missing and a path-shaped identifier are `InvalidRequest`; an unknown one is
`TargetNotFound`; an evicted one states it is no longer retained.
