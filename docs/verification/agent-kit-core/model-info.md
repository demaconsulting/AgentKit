## ModelInfo Unit Verification Design

This document describes the unit-level verification strategy for the `ModelInfo` record, the
`ModelCapabilities` and `ModelAdjustments` flag enumerations, and the
`ModelCapabilitiesExtensions.ToHostCapabilities` conversion.

### Verification Approach

The unit is plain data and two pure functions, so it is verified directly: tests build descriptions
by hand, call `Supports` and `ToHostCapabilities`, and assert the results. Nothing is mocked,
because nothing is reached beyond the unit itself.

The descriptions come from the shared `ModelTestData` helper — a vision model, a text-only model and
an unusable model — built exactly as an application on a provider no catalog serves would build
them, so the Core rules are exercised without any provider's mapping in the way. Provider mappings
are verified in *OllamaModelCatalog Unit Verification Design* and *CopilotModelCatalog Unit
Verification Design*.

One scenario is **reflective rather than literal**: the zero-or-single-bit property is asserted over
every declared member of both flag enumerations, so a member added later that overlapped another
would fail the run rather than silently satisfy a requirement nobody stated.

Unit tests reside in `ModelInfoTests.cs`, with the shared descriptions in `ModelTestData.cs`, within
the `DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, file system access or network access required
- **Mocking**: None; descriptions are built by hand
- **Isolation**: Each test builds its own descriptions; no state is shared

### Acceptance Criteria

A unit test run passes when all six scenarios below pass without error or exception beyond those
explicitly asserted. A hand-built description that is not usable by default or carries a missing
list, a combined requirement satisfied by a model reporting only part of it, a requirement of
nothing that some model fails, a flag member that is neither zero nor a single bit, a vision model
contributing anything but the vision host capability, or a text model contributing any host
capability each constitute a failure.

### Test Scenarios

#### AgentKitCore-ModelInfo-DescribesAModel: A Hand-Built Description Defaults to Usable With Empty Lists

**Test**: `ModelInfo_Construct_ByHand_DefaultsToUsableWithEmptyLists`

Normal operation. A description given only its required members is usable, reports no capability,
and carries empty effort, tier and notice lists rather than missing ones.

#### AgentKitCore-ModelInfo-CapabilitiesCombine: A Combined Requirement Needs Every Flag

**Test**: `ModelInfo_Supports_EveryRequiredFlagReported_IsSatisfiedOtherwiseNot`

A vision-and-tools requirement is satisfied by the vision model and not by the text model, which
still satisfies a tools-only requirement. This is the property a vision application's listing rests
on.

#### AgentKitCore-ModelInfo-CapabilitiesCombine: Requiring Nothing Is Always Satisfied

**Test**: `ModelInfo_Supports_NothingRequired_IsAlwaysSatisfied`

Boundary condition: a model reporting no capability at all still satisfies a requirement of
nothing, so "no requirement" needs no special case.

#### AgentKitCore-ModelInfo-CapabilitiesCombine: Every Flag Member Is Zero or a Single Bit

**Test**: `ModelInfo_FlagEnums_EveryMember_IsZeroOrASingleBit`

The guard on future members. Iterates every declared member of `ModelCapabilities` and
`ModelAdjustments` reflectively and asserts each is zero or a power of two.

#### AgentKitCore-ModelInfo-HostCapabilitiesFromModel: A Vision Model Yields Vision Only

**Test**: `ModelInfo_ToHostCapabilities_VisionModel_YieldsVisionOnly`

A model reporting every capability contributes exactly `HostCapabilities.Vision`, confirming that
delegation stays the application's to declare.

#### AgentKitCore-ModelInfo-HostCapabilitiesFromModel: A Text Model Yields Nothing

**Test**: `ModelInfo_ToHostCapabilities_TextModel_YieldsNone`

Error path for the image tool family: a model that calls tools and reasons but cannot see, and a
model reporting nothing, each contribute `HostCapabilities.None`, so the image family is not offered
to a model that cannot see what it returns.
