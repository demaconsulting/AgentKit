### ImagePack Unit Verification Design

This document describes the unit-level verification strategy for the `ImagePack` class.

#### Verification Approach

Nothing is mocked or stubbed. The pack has no dependency worth substituting: it reads no state,
calls one factory in this same subsystem, and passes on the access policy it was given. What must be
verified is what a composing application can observe — the prefix it claims, the capability it
requires, the tool it produces, and that the policy supplied is the one governing it.

The last of those is asserted **behaviorally** rather than by reference comparison: the scenario
creates the pack's tool from a policy rooted at one location, then reads one permitted path and one
refused path through the created tool. A pack that captured a policy and then ignored it would pass
a reference check and fail this one.

Unit tests reside in `Image/ImagePackTests.cs`, reusing the shared temporary-directory fixture from
`TextFile/TempDirectoryFixture.cs`, within the `DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services or network access required
- **File system**: Only the policy-governance scenario touches the file system, using a temporary
  tree it creates and deletes
- **Isolation**: Each test constructs its own pack and policy; no state is shared

#### Acceptance Criteria

A unit test run passes when all eight scenarios below pass without error or exception beyond those
explicitly asserted. A prefix that differs between the constant and the contract, a capability
requirement other than vision, a tool count other than one, a null element, a tool outside the
family prefix, an accepted null policy, and a created tool that does not observe the supplied policy
each constitute a failure.

#### Test Scenarios

##### AgentKitTools-Image-Pack-FamilyPrefix: The Constant Is the Family Name

**Test**: `ImagePack_FamilyPrefix_Constant_IsImage`

Asserts the published constant is `image` — the prefix every tool name in the family is qualified
by.

##### AgentKitTools-Image-Pack-FamilyPrefix: The Contract Reports the Declared Constant

**Test**: `ImagePack_FamilyPrefix_Contract_ReportsTheDeclaredConstant`

Reads the prefix through the pack contract a composer uses and asserts it equals the constant, so
the two cannot drift apart.

##### AgentKitTools-Image-Pack-RequiresVisionCapability: The Pack Requires Vision

**Test**: `ImagePack_RequiredCapabilities_Pack_RequiresVision`

Normal operation: requiring the vision capability is what lets the composition withhold the family
from a host that cannot present its content.

##### AgentKitTools-Image-Pack-RegistersReadTool: The Read Tool Is Created

**Test**: `ImagePack_CreateTools_Policy_CreatesTheReadTool`

Asserts a single tool named `image_read`, confirming the pack produces the family's tool for an
application to receive.

##### AgentKitTools-Image-Pack-RegistersReadTool: No Null Tool Is Returned

**Test**: `ImagePack_CreateTools_Policy_ReturnsNoNullTool`

Boundary condition on the pack contract's obligation: a null element would fail the composition for
a reason no caller could act on.

##### AgentKitTools-Image-Pack-ToolsCarryFamilyPrefix: Every Tool Carries the Family Prefix

**Test**: `ImagePack_CreateTools_EveryTool_CarriesTheFamilyPrefix`

Asserts over every created tool, confirming the declaration the composition's prefix-collision check
depends on is actually true of the names published.

##### AgentKitTools-Image-Pack-RequiresPolicy: A Null Policy Throws

**Test**: `ImagePack_CreateTools_NullPolicy_ThrowsArgumentNullException`

Error path: a family created without a policy would hand an agent unrestricted file access while
appearing correctly composed, so the mistake is reported at the line that made it.

##### AgentKitTools-Image-Pack-PolicyGovernsCreatedTools: The Supplied Policy Governs the Tools

**Test**: `ImagePack_CreateTools_SuppliedPolicy_GovernsTheCreatedTools`

Normal operation and error path together: one permitted path returns its content and one path
outside the policy's location is refused, proving behaviorally that the supplied policy — not one
the pack invented — is in force.
