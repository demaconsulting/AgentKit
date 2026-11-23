### DiagramPackOptions Unit Verification Design

This document describes the unit-level verification strategy for the `DiagramPackOptions` record.

#### Verification Approach

The record is constructed with object initializers, the way a host builds it, and its values and
refusals are asserted directly. No dependency is substituted.

Unit tests reside in `Diagram/DiagramPackOptionsTests.cs` within the
`DemaConsulting.AgentKit.Tools.Visual.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: None beyond the package
- **Isolation**: Each test builds its own instances

#### Acceptance Criteria

A unit test run passes when all five scenarios below pass without error or exception beyond those
explicitly asserted. Wrong defaults, a name not stored canonically, an invalid list or a null bound
accepted, or a stored list that follows changes to the caller's list or can be changed through
itself each constitute a failure.

#### Test Scenarios

##### AgentKitToolsVisual-Diagram-PackOptions-Defaults: The Defaults Offer Every Language

**Test**: `DiagramPackOptions_Default_OffersEveryLanguageWithTheDefaultBounds`

`Default` lists `mermaid`, `plantuml`, `svg`, `vega-lite`, `openscad` in that order and carries
`VisualOptions.Default`; a fresh instance lists the same languages.

##### AgentKitToolsVisual-Diagram-PackOptions-CanonicalNames: Names Are Stored Canonically

**Test**: `DiagramPackOptions_Formats_MixedCase_IsStoredCanonically`

`" Mermaid"` and `"OPENSCAD "` are stored as `mermaid` and `openscad`, in the given order.

##### AgentKitToolsVisual-Diagram-PackOptions-RejectsInvalidFormats: Invalid Lists Are Refused

**Test**: `DiagramPackOptions_Formats_InvalidLists_AreRefused`

A null list is refused with `ArgumentNullException`; an empty list, an unknown language (whose
message names it and lists the known ones), a null name and a language listed twice in different
case are refused with `ArgumentException` naming `Formats`.

##### AgentKitToolsVisual-Diagram-PackOptions-FormatsCopied: The List Is Copied and Exposed Read-Only

**Test**: `DiagramPackOptions_Formats_AssignedList_IsCopiedAndExposedReadOnly`

Changing the caller's array after assignment leaves the options' list unchanged; the exposed list is
not the caller's array, reports itself read-only, and refuses an element assignment with
`NotSupportedException`.

##### AgentKitToolsVisual-Diagram-PackOptions-VisualApplied: The Bounds Are Carried and Null Is Refused

**Test**: `DiagramPackOptions_Visual_IsAppliedAndNullIsRefused`

The bounds assigned are the bounds carried; a null value is refused with `ArgumentNullException`.
