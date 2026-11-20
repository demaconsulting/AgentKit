## ToolOutputSpill Unit Verification Design

This document describes the unit-level verification strategy for the `ToolOutputSpill` class and
the internal `SpillAttachment` it allocates.

### Verification Approach

`ToolOutputSpill` is verified through unit tests that compose retention over small tool lists and
invoke the resulting tools exactly as a runtime would, through `AIFunction.InvokeAsync`.
`GuardedToolFactory`, `ToolResult`, `ToolLimits`, `ToolOutputOptions`, `ToolOutputStore` and the two
reader tools are used as real dependencies, all documented in *ToolOutputSpill Unit Design*;
substituting them would remove the very wiring under test. Tool delegates are declared
`Task<object>`, the shape every real guarded tool has. Results are asserted by their observable
text, never by inspecting the store.

Unit tests reside in `ToolOutputSpillTests.cs` within the `DemaConsulting.AgentKit.Core.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: `Microsoft.Extensions.AI.Abstractions`; no file system or network access
- **Mocking**: None
- **Isolation**: Each test composes its own tool list and therefore its own store

### Acceptance Criteria

A unit test run passes when all seventeen scenarios below pass. Any result within the ceiling
altered, any oversized textual result not retained, any preview exceeding the ceiling or omitting
the reference or the readers, any binary content retained, any output truncated, any reader result
retained, any store shared between compositions, or any invalid list accepted constitutes a
failure.

### Test Scenarios

#### AgentKitCore-ToolOutputSpill-ThresholdIsResultCeiling: A Result Within the Ceiling Is Unchanged

**Test**: `ToolOutputSpill_Apply_ResultWithinCeiling_IsReturnedUnchanged`

Normal operation: a short result arrives exactly as the tool produced it.

#### AgentKitCore-ToolOutputSpill-ThresholdIsResultCeiling: An Oversized Result Becomes a Bounded Preview

**Test**: `ToolOutputSpill_Apply_OversizedText_ReturnsBoundedPreviewNamingReferenceAndReaders`

Normal operation: the result names `output-1`, the source tool, the character and line counts, and
both readers; is not a denial; and fits the ceiling.

#### AgentKitCore-ToolOutputSpill-PreviewComposition: Head and Tail Are Numbered

**Test**: `ToolOutputSpill_Apply_OversizedText_PreviewShowsNumberedHeadAndTail`

Normal operation: the first and last lines appear numbered and padded like `text_file_read`, with an
omission marker and a statement of the lines shown.

#### AgentKitCore-ToolOutputSpill-PreviewComposition: Wrapping Is Stated

**Test**: `ToolOutputSpill_Apply_WrappedOutput_HeaderNotesTheWrapping`

Boundary value: a single very long line produces a header noting continuation lines.

#### AgentKitCore-ToolOutputSpill-PreviewComposition: The Preview Is Clamped and the Header Always Delivered

**Test**: `ToolOutputSpill_Apply_SmallCeiling_PreviewIsClampedAndHeaderAlwaysDelivered`

Boundary values: a 500-character ceiling yields a preview within it; a 10-character ceiling yields
the header alone, stating that no lines fit.

#### AgentKitCore-ToolOutputSpill-TextualResultsRetained: Structured Data Is Retained Indented

**Test**: `ToolOutputSpill_Apply_OversizedStructuredResult_IsRetainedIndented`

Normal operation: a large structured result is retained, and reading its first lines shows one
value per line.

#### AgentKitCore-ToolOutputSpill-TextualResultsRetained: Text Parts Are Retained

**Test**: `ToolOutputSpill_Apply_OversizedTextContentList_IsRetained`

Normal operation: two large text parts are retained as one output of their combined lines.

#### AgentKitCore-ToolOutputSpill-TextualResultsRetained: A Tool Not Built by the Factory Is Retained

**Test**: `ToolOutputSpill_Apply_ToolNotBuiltByTheFactory_IsRetainedToo`

Normal operation: a function built directly by the underlying factory is wrapped and its oversized
result retained.

#### AgentKitCore-ToolOutputSpill-BinaryNeverRetained: Binary Content Passes Through

**Test**: `ToolOutputSpill_Apply_BinaryContent_PassesThroughUnchanged`

Normal operation: a bare image, and an image whose caption alone exceeds the ceiling, arrive as
content.

#### AgentKitCore-ToolOutputSpill-BinaryNeverRetained: An Absent Result Passes Through

**Test**: `ToolOutputSpill_Apply_NullResult_PassesThrough`

Boundary value: with a zero ceiling, a null result remains null.

#### AgentKitCore-ToolOutputSpill-DeclaredOversizedRetained: A Declared-Oversized Output Is Retained

**Test**: `ToolOutputSpill_Apply_OversizedMarker_IsRetained`

Normal operation: a small output the tool declared oversized is retained rather than refused.

#### AgentKitCore-ToolOutputSpill-OverRetentionCeilingRefused: The Tool's Own Refusal Is Kept

**Test**: `ToolOutputSpill_Apply_MarkerOverRetentionCeiling_ReturnsTheToolsRefusal`

Error path: a declared-oversized output above the retention ceiling yields byte-for-byte the tool's
refusal.

#### AgentKitCore-ToolOutputSpill-OverRetentionCeilingRefused: Any Other Output Names the Ceiling

**Test**: `ToolOutputSpill_Apply_PlainOutputOverRetentionCeiling_IsRefusedNamingTheCeiling`

Error path: an ordinary output above the retention ceiling is refused naming the tool and the
ceiling.

#### AgentKitCore-ToolOutputSpill-ReadersAppendedNotRetained: The Readers Follow the Tools

**Test**: `ToolOutputSpill_Apply_Tools_AreFollowedByTheReaders`

Normal operation: the supplied tools keep their order, name, description and schema, followed by
`tool_output_read` and `tool_output_search`.

#### AgentKitCore-ToolOutputSpill-ReadersAppendedNotRetained: A Reader's Result Is Never Retained

**Test**: `ToolOutputSpill_Apply_ReaderResults_AreNeverRetained`

Error path: a page far over the ceiling is refused and issues no second reference.

#### AgentKitCore-ToolOutputSpill-IsolatedPerApplication: Compositions Do Not Share References

**Test**: `ToolOutputSpill_Apply_TwoApplications_DoNotShareReferencesOrAlterTheSuppliedTools`

Isolation: a reference retained through one composition is unknown to another, and the original
tool still refuses.

#### AgentKitCore-ToolOutputSpill-RejectsInvalidLists: Invalid Lists Are Rejected

**Test**: `ToolOutputSpill_Apply_InvalidLists_Throw`

Error path: a missing list or limits, a null tool, a duplicate name and a reserved name each throw.
