### MemoryOptions Unit Verification Design

This document describes the unit-level verification strategy for the `MemoryOptions` class.

#### Verification Approach

Nothing is mocked or stubbed. The unit holds no collaborators, so each scenario constructs the type
directly and reads back what it holds. Both the published constants and the values a caller receives
are asserted, so that the numbers appearing in requirement text cannot drift from the numbers that
ship.

Unit tests reside in `Memory/MemoryOptionsTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario constructs the instances it needs
- **Isolation**: No state is shared between scenarios

#### Acceptance Criteria

A unit test run passes when all 4 requirement scenarios below, covering 7 listed test method
entries, pass without error or exception beyond those explicitly asserted. A default that has
drifted from its published constant, a boundary value rejected, an out-of-range value clamped rather
than rejected, or a NaN threshold accepted constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Memory-Options-Defaults: Defaults

**Test**: `MemoryOptions_Constructor_NoArguments_UsesThePublishedDefaults`

**Test**: `MemoryOptions_Default_CarriesThePublishedDefaults`

The listed tests prove a caller who configures nothing receives a threshold of 0.88 and a recall
count of 5, that both are the published constants, and that the shared default instance carries the
same values rather than a separate set.

##### AgentKitTools-Memory-Options-IndependentControls: Independent Controls

**Test**: `MemoryOptions_Constructor_OneArgument_LeavesTheOtherAtItsDefault`

The listed tests prove replacing one control leaves the other at its default, so an author need not
restate a value they were content with.

##### AgentKitTools-Memory-Options-BoundaryValues: Boundary Values

**Test**: `MemoryOptions_Constructor_BoundaryValues_AreAccepted`

The listed tests prove a threshold of 0.0 and of 1.0, a recall count of zero and a very large recall
count are all accepted and held as stated, because each is a legitimate author configuration rather
than a mistake.

##### AgentKitTools-Memory-Options-Validation: Validation

**Test**: `MemoryOptions_Constructor_ThresholdOutOfRange_ThrowsArgumentOutOfRangeException`

**Test**: `MemoryOptions_Constructor_NegativeRecallCount_ThrowsArgumentOutOfRangeException`

The listed tests prove a threshold below 0.0, above 1.0, NaN or infinite is rejected rather than
clamped, and that a negative recall count is rejected. The NaN case is covered explicitly because a
NaN threshold compares false against everything and would silently disable near-duplicate detection.
