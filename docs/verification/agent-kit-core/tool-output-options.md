## ToolOutputOptions Unit Verification Design

This document describes the unit-level verification strategy for the `ToolOutputOptions` class.

### Verification Approach

`ToolOutputOptions` is verified through unit tests that construct it directly and read its
properties. It has no dependencies, so nothing is substituted.

Unit tests reside in `ToolOutputOptionsTests.cs` within the `DemaConsulting.AgentKit.Core.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: None; no file system or network access required
- **Mocking**: None
- **Isolation**: Each test constructs its own instance

### Acceptance Criteria

A unit test run passes when all four scenarios below pass. Any default differing from its
published value, any supplied value not carried, or any invalid configuration accepted constitutes
a failure.

### Test Scenarios

#### AgentKitCore-ToolOutputOptions-Defaults: The Defaults Are the Published Values

**Test**: `ToolOutputOptions_Default_CarriesThePublishedDefaults`

Normal operation: `Default` carries the four published constants, and the constants are 4,000,
4,194,304, 16,777,216 and 256.

#### AgentKitCore-ToolOutputOptions-Customizable: Supplied Values Are Carried

**Test**: `ToolOutputOptions_Constructor_OverriddenValues_AreCarried`

Normal operation: every overridden value is the one supplied.

#### AgentKitCore-ToolOutputOptions-Customizable: A Zero Preview Is Permitted

**Test**: `ToolOutputOptions_Constructor_ZeroPreview_IsPermitted`

Boundary value: a zero preview is accepted as a legitimate configuration.

#### AgentKitCore-ToolOutputOptions-RejectsInvalid: Invalid Values Are Rejected

**Test**: `ToolOutputOptions_Constructor_InvalidValues_ThrowArgumentOutOfRangeException`

Error path: a negative preview, a zero per-output ceiling, a zero count ceiling, and a total ceiling
one below the per-output ceiling each throw `ArgumentOutOfRangeException`.
