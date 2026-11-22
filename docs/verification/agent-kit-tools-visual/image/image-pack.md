### ImagePack Unit Verification Design

This document describes the unit-level verification strategy for the `ImagePack` class.

#### Verification Approach

Nothing is mocked or stubbed. The pack has no dependency worth substituting: it reads no state,
calls the two tool factories in this same subsystem, and passes on the access policy it was given.
What must be
verified is what a composing application can observe — the prefix it claims, the capability it
requires, the tools it produces, and that the policy supplied is the one governing them.

The last of those is asserted **behaviorally** rather than by reference comparison: the scenario
creates the pack's tool from a policy rooted at one location, then reads one permitted path and one
refused path through the created tool. A pack that captured a policy and then ignored it would pass
a reference check and fail this one.

Unit tests reside in `Image/ImagePackTests.cs`, reusing the shared temporary-directory fixture from
`TempDirectoryFixture.cs`, within the `DemaConsulting.AgentKit.Tools.Visual.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services or network access required
- **File system**: Only the policy-governance scenario touches the file system, using a temporary
  tree it creates and deletes
- **Isolation**: Each test constructs its own pack and policy; no state is shared

#### Acceptance Criteria

A unit test run passes when all sixteen scenarios below pass without error or exception beyond those
explicitly asserted. A prefix that differs between the constant and the contract, a capability
requirement other than vision, a tool list other than `image_read`, `image_crop`, `image_auto_crop`
and `image_view` in that order, a null element, a tool outside the family prefix,
an accepted null
policy, a created tool that does not observe the supplied policy, options that are not applied,
contradictory options that are accepted, and a warm-up that fails or leaves the pack unable to draw
each constitute a failure.

#### Test Scenarios

##### AgentKitToolsVisual-Image-Pack-FamilyPrefix: The Constant Is the Family Name

**Test**: `ImagePack_FamilyPrefix_Constant_IsImage`

Asserts the published constant is `image` — the prefix every tool name in the family is qualified
by.

##### AgentKitToolsVisual-Image-Pack-FamilyPrefix: The Contract Reports the Declared Constant

**Test**: `ImagePack_FamilyPrefix_Contract_ReportsTheDeclaredConstant`

Reads the prefix through the pack contract a composer uses and asserts it equals the constant, so
the two cannot drift apart.

##### AgentKitToolsVisual-Image-Pack-RequiresVisionCapability: The Pack Requires Vision

**Test**: `ImagePack_RequiredCapabilities_Pack_RequiresVision`

Normal operation: requiring the vision capability is what lets the composition withhold the family
from a host that cannot present its content.

##### AgentKitToolsVisual-Image-Pack-RegistersReadTool: Every Tool Is Created

**Test**: `ImagePack_CreateTools_Policy_CreatesEveryTool`

Asserts exactly four tools, named `image_read`, `image_crop`, `image_auto_crop` and `image_view` in
that order, confirming the
pack produces the
family's tools for an application to receive.

##### AgentKitToolsVisual-Image-Pack-RegistersCropTool: The Crop Tool Is Created Alongside the Read Tool

**Test**: `ImagePack_CreateTools_Policy_CreatesEveryTool`

The same scenario read from the other side: the read tool and the crop tool are one capability, so
the pack creating
both in the one place the family prefix is claimed is what makes that pairing a property of the
pack rather than of each application's composition code.

##### AgentKitToolsVisual-Image-Pack-RegistersAutoCropTool: The Auto-Crop Tool Is Created Alongside Its Siblings

**Test**: `ImagePack_CreateTools_Policy_CreatesEveryTool`

The same scenario read from the third side. A model that can see a picture is mostly margin cannot
measure where the margin stops, so the region it would name in the crop tool's coordinate space is
a guess; publishing the auto-crop tool beside its siblings is what gives it the one region request
it can make without measuring. Creating all four in the one place the family prefix is claimed is
what stops an application shipping without the third.

##### AgentKitToolsVisual-Image-Pack-PublishedUnderEveryPolicy: Every Tool Survives a Read-Only Policy

**Test**: `ImagePack_CreateTools_ReadOnlyPolicy_StillPublishesEveryTool`

Asserts that a policy whose every grant is read-only still yields exactly four tools, `image_read`,
`image_crop`, `image_auto_crop` and `image_view`. Confirms the family is never narrowed by the access policy,
which is a decision
rather than an incidental outcome and so is asserted rather than merely described.

Every tool can succeed where nothing may be written: reading an image is a read, and taking or
trimming a region
without a destination returns image content rather than producing a file. The optional
`destination` both region tools accept does need a write grant, but the rule the packs apply is
whether a tool could ever
succeed, not whether every argument could — so a destination named under this policy earns the
ordinary write denial, which enumerates the writable locations, instead of the whole tool
disappearing.

##### AgentKitToolsVisual-Image-Pack-RegistersReadTool: No Null Tool Is Returned

**Test**: `ImagePack_CreateTools_Policy_ReturnsNoNullTool`

Boundary condition on the pack contract's obligation: a null element would fail the composition for
a reason no caller could act on.

##### AgentKitToolsVisual-Image-Pack-ToolsCarryFamilyPrefix: Every Tool Carries the Family Prefix

**Test**: `ImagePack_CreateTools_EveryTool_CarriesTheFamilyPrefix`

Asserts over every created tool, confirming the declaration the composition's prefix-collision check
depends on is actually true of the names published.

##### AgentKitToolsVisual-Image-Pack-RequiresPolicy: A Null Policy Throws

**Test**: `ImagePack_CreateTools_NullPolicy_ThrowsArgumentNullException`

Error path: a family created without a policy would hand an agent unrestricted file access while
appearing correctly composed, so the mistake is reported at the line that made it.

##### AgentKitToolsVisual-Image-Pack-PolicyGovernsCreatedTools: The Supplied Policy Governs the Tools

**Test**: `ImagePack_CreateTools_SuppliedPolicy_GovernsTheCreatedTools`

Normal operation and error path together: one permitted path returns its content and one path
outside the policy's location is refused, proving behaviorally that the supplied policy — not one
the pack invented — is in force. The refusal is asserted through the crop tool and the auto-crop
tool as well as the read
tool, because a tool that quietly observed a different policy from its neighbor would make the
configured containment unverifiable.

##### AgentKitToolsVisual-Image-Pack-RegistersViewTool: The View Tool Is Created Last

**Test**: `ImagePack_CreateTools_Policy_CreatesEveryTool`

The same scenario read for the view tool: it is present, so the read tool's refusal of an `.svg`
names a tool the model has, and it is last, so the order of the existing tools is unchanged.

##### AgentKitToolsVisual-Image-Pack-AcceptsVisualOptions: The Options Given Are Applied

**Test**: `ImagePack_Constructor_Options_AreApplied`

Asserts that the options instance a host passes is the one the pack exposes, and that a pack built
with none exposes `VisualOptions.Default`, so `new ImagePack()` keeps its meaning.

##### AgentKitToolsVisual-Image-Pack-AcceptsVisualOptions: The View Tool Observes the Pack's Options

**Test**: `ImagePack_CreateTools_ViewTool_ObservesThePackOptions`

Behavioral rather than by reference: a pack whose smallest preview side is 300 draws a 200-pixel SVG
at 300 by 150 pixels, which the default of 768 would not, so the view tool is shown to observe the
options of the pack that created it.

##### AgentKitToolsVisual-VisualOptions-MinNotAboveMax: Contradictory Options Are Refused

**Test**: `ImagePack_Constructor_MinPreviewSideAboveMaxRenderSide_ThrowsArgumentException`

Error path: options whose smallest preview side (768) exceeds the largest render side (512) are
refused with an `ArgumentException` naming `options`, at the constructor that received them.

##### AgentKitToolsVisual-Image-Pack-WarmUp: Warming Up Completes and the Pack Still Draws

**Test**: `ImagePack_WarmUpAsync_Pack_CompletesAndTheViewToolStillDraws`

Warms the pack up, then views an SVG through its view tool and asserts a picture is returned, so the
warm-up neither fails nor leaves the render gate held.
