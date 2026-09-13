## ToolLimits Unit Verification Design

This document describes the unit-level verification strategy for the `ToolLimits` class.

### Verification Approach

`ToolLimits` is verified through unit tests that construct the type directly and read back its
properties. There are no dependencies to substitute — the unit depends only on the base class
library — so every scenario exercises the real type.

The default-value scenario asserts against **literal numbers** rather than against the published
constants. Asserting a constant against itself is vacuous, and those five values appear in
_ToolLimits Unit Design_, in the requirement text and in the public API surface; this scenario is
what stops them drifting silently.

Unit tests reside in `ToolLimitsTests.cs` within the `DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, file system access or network access required
- **Mocking**: None
- **Isolation**: Each test constructs its own instance; no state is shared

### Acceptance Criteria

A unit test run passes when all eleven scenarios below pass without error or exception beyond those
explicitly asserted. Any published default that has drifted, any ceiling that fails to take
effect, any negative ceiling that is accepted, and any zero ceiling that is rejected constitutes
a failure.

### Test Scenarios

#### AgentKitCore-ToolLimits-Defaults: The Shared Default Exposes the Published Ceilings

**Test**: `ToolLimits_Default_AllCeilings_MatchPublishedDefaults`

Reads all five ceilings from the shared default instance and asserts the literal values 65,536,
32,000, 8,388,608, 4 and 2. Pins the published API surface against silent drift.

#### AgentKitCore-ToolLimits-Defaults: Construction With No Arguments Matches the Default

**Test**: `ToolLimits_Constructor_NoArguments_MatchesDefault`

Constructs with no arguments and asserts every ceiling agrees with the shared default, confirming
that the default instance and the default construction path cannot diverge.

#### AgentKitCore-ToolLimits-Customization: Replacing One Ceiling Retains the Others

**Test**: `ToolLimits_Constructor_SingleCeilingOverridden_RetainsOtherDefaults`

Supplies only the binary-content ceiling by name and asserts the other four remain at their
published defaults. This is the behavior that delivers per-ceiling customization without a
builder.

#### AgentKitCore-ToolLimits-Customization: Every Supplied Ceiling Is Exposed

**Test**: `ToolLimits_Constructor_AllCeilingsSupplied_ExposesSuppliedValues`

Supplies five mutually distinguishable values positionally and asserts each lands on its own
property, so a transposed parameter order cannot pass.

#### AgentKitCore-ToolLimits-RejectNegative: A Negative Read Ceiling Is Refused

**Test**: `ToolLimits_Constructor_NegativeMaxReadBytes_ThrowsArgumentOutOfRangeException`

Error path: a negative ceiling is a programming error in the host's configuration code and is
surfaced rather than clamped.

#### AgentKitCore-ToolLimits-RejectNegative: A Negative Result-Character Ceiling Is Refused

**Test**: `ToolLimits_Constructor_NegativeMaxResultCharacters_ThrowsArgumentOutOfRangeException`

Asserts every ceiling is validated, not merely the first.

#### AgentKitCore-ToolLimits-RejectNegative: A Negative Binary-Content Ceiling Is Refused

**Test**: `ToolLimits_Constructor_NegativeMaxBinaryBytes_ThrowsArgumentOutOfRangeException`

Asserts every ceiling is validated.

#### AgentKitCore-ToolLimits-RejectNegative: A Negative Attachment Ceiling Is Refused

**Test**: `ToolLimits_Constructor_NegativeMaxAttachmentsPerTurn_ThrowsArgumentOutOfRangeException`

Asserts every ceiling is validated.

#### AgentKitCore-ToolLimits-RejectNegative: A Negative Delegation-Depth Ceiling Is Refused

**Test**: `ToolLimits_Constructor_NegativeMaxAgentDepth_ThrowsArgumentOutOfRangeException`

Asserts every ceiling is validated, including the last.

#### AgentKitCore-ToolLimits-DelegationDepth: The Delegation Ceiling Is Carried With the Others

**Tests**: `ToolLimits_Default_AllCeilings_MatchPublishedDefaults`,
`ToolLimits_Constructor_AllCeilingsSupplied_ExposesSuppliedValues`

Asserts the delegation-depth ceiling is published with a default of 2 and is exposed as supplied
when a host replaces it, so a family that delegates reads its budget from the same object as every
other ceiling rather than from a constant of its own.

#### AgentKitCore-ToolLimits-RejectNegative: A Ceiling of Zero Is Accepted

**Test**: `ToolLimits_Constructor_ZeroCeiling_IsAccepted`

Boundary condition: zero is the expressible way for a host to disable an operation entirely, so
it must not be rejected alongside a negative value. Asserts all five zero ceilings are accepted
and reported back unchanged.
