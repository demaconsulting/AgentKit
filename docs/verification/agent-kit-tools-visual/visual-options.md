## VisualOptions Unit Verification Design

This document describes the unit-level verification strategy for the `VisualOptions` record.

### Verification Approach

`VisualOptions` is verified through unit tests that construct the record directly and read back its
properties. There is nothing to substitute — the record depends only on the base class library — so
every scenario exercises the real type. The default-value scenario asserts against literal numbers
rather than against the record's own defaults, so a drifted default fails rather than agreeing with
itself. The one relation between two values, which the pack checks, is verified in *ImagePack Unit
Verification Design*.

Unit tests reside in `VisualOptionsTests.cs` within the `DemaConsulting.AgentKit.Tools.Visual.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, file system access or network access required
- **Mocking**: None
- **Isolation**: Each test constructs its own instance; no state is shared

### Acceptance Criteria

A unit test run passes when all seven scenarios below pass without error or exception beyond those
explicitly asserted. A default that has drifted, a value inside its range that is not kept as given,
and a value outside its range that is accepted each constitute a failure.

### Test Scenarios

#### AgentKitToolsVisual-VisualOptions-Defaults: The Defaults Are the Documented Ones

**Test**: `VisualOptions_Default_Values_AreTheDocumentedDefaults`

Asserts 30 seconds, 2048, 768, 500,000 and 1, and that `Default` equals a freshly constructed
record.

#### AgentKitToolsVisual-VisualOptions-Defaults: A Value in Range Is Kept

**Test**: `VisualOptions_Init_ValuesInRange_AreKept`

Boundary condition: every value set at a boundary of its range — ten minutes, 16, 1, 1, 4 — is read
back unchanged, so nothing in range is clamped or replaced.

#### AgentKitToolsVisual-VisualOptions-RejectsOutOfRange: A Timeout Out of Range Is Refused

**Test**: `VisualOptions_RenderTimeout_OutOfRange_Throws`

Error path, run as a theory over zero, a negative value and one millisecond over ten minutes, each
refused with an `ArgumentOutOfRangeException` naming the property.

#### AgentKitToolsVisual-VisualOptions-RejectsOutOfRange: A Largest Side Out of Range Is Refused

**Test**: `VisualOptions_MaxRenderSide_OutOfRange_Throws`

Error path, run as a theory over 15 and 8193.

#### AgentKitToolsVisual-VisualOptions-RejectsOutOfRange: A Smallest Preview Side Out of Range Is Refused

**Test**: `VisualOptions_MinPreviewSide_OutOfRange_Throws`

Error path, run as a theory over 0 and 8193.

#### AgentKitToolsVisual-VisualOptions-RejectsOutOfRange: A Triangle Limit Below One Is Refused

**Test**: `VisualOptions_ScadMaxTriangles_BelowOne_Throws`

Error path: zero is refused.

#### AgentKitToolsVisual-VisualOptions-RejectsOutOfRange: A Concurrency Bound Below One Is Refused

**Test**: `VisualOptions_MaxConcurrentRenders_BelowOne_Throws`

Error path: zero is refused.
