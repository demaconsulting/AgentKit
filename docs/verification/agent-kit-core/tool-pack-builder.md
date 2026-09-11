## ToolPackBuilder Unit Verification Design

This document describes the unit-level verification strategy for the `ToolPackBuilder` class.

### Verification Approach

The builder is verified through unit tests that compose real packs and inspect the resulting tool
list. The packs are test doubles — the pack contract is an interface, so an implementation the
test controls is the only way to exercise it — but everything else is real: a real `PathPolicy`,
and real tools built through `GuardedToolFactory` with names composed to the naming convention.

The test double records **whether it was asked to create its tools**, and that recording is what
makes the central scenario meaningful. Asserting only that the composed list is empty would pass
equally for a builder that registered the pack and then filtered it out; asserting that the pack
was never asked is what verifies the decision that a tool the host cannot support is never built
at all.

Ordering scenarios assert an **exact sequence** rather than a set, because the order a model sees
is observable and a builder that returned the right tools in an incidental order would satisfy a
set assertion while changing what the model is likely to pick.

Every failing scenario asserts an **exception**, because every fault this unit detects is a
composition-time programming error in the application's own code. The non-throwing discipline this
library observes elsewhere applies to runtime policy refusals reported to a model, and nothing in
this unit is reachable from a model's tool call.

Unit tests reside in `ToolPackBuilderTests.cs`, with the shared test double in `StubToolPack.cs`,
within the `DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, file system access or network access required
- **Mocking**: A hand-written test double, `StubToolPack`, supplies the packs; the access policy
  and every tool are real
- **Isolation**: Each test constructs its own builder, policy and packs; no state is shared

### Acceptance Criteria

A unit test run passes when all twenty-four scenarios below pass without error or exception beyond
those explicitly asserted. A pack registered without its capabilities being satisfied, an
unsupported pack asked to create tools, a composition in the wrong order, an accepted collision,
an accepted foreign tool name, and any malformed pack that is not refused each constitute a
failure.

### Test Scenarios

#### AgentKitCore-ToolPackBuilder-RequiresPolicy: Constructing Without a Policy Is Refused

**Test**: `ToolPackBuilder_Constructor_NullPolicy_ThrowsArgumentNullException`

Error path: an unguarded builder would compose unguarded tools, so an unguarded composition is
unrepresentable.

#### AgentKitCore-ToolPackBuilder-RequiresPolicy: The Constructed Policy Reaches Every Pack

**Test**: `ToolPackBuilder_Build_ConstructedPolicy_IsHandedToEveryPack`

Asserts the identity of the policy instance at two packs, confirming neither invented a policy of
its own and that one budget governs the whole composition.

#### AgentKitCore-ToolPackBuilder-Composition: Supported Packs Contribute Every Tool

**Test**: `ToolPackBuilder_Build_SatisfiedPacks_ContributeEveryTool`

Normal operation: composition is the union of the packs, not a selection from them.

#### AgentKitCore-ToolPackBuilder-Composition: Add Order and Pack Order Are Preserved

**Test**: `ToolPackBuilder_Build_MultiplePacks_PreservesAddOrderAndPackOrder`

Asserts the exact sequence, not merely the set, because the order a model sees is observable.

#### AgentKitCore-ToolPackBuilder-Composition: Composing Nothing Yields an Empty List

**Test**: `ToolPackBuilder_Build_NoPacks_ReturnsEmptyList`

Boundary condition: an application that attaches no packs simply offers no tools, rather than
failing.

#### AgentKitCore-ToolPackBuilder-Composition: Adding a Pack Returns the Same Builder

**Test**: `ToolPackBuilder_Add_ValidPack_ReturnsTheSameBuilder`

Asserts the fluent contract that makes attaching a capability pack cost a consumer one line.

#### AgentKitCore-ToolPackBuilder-CapabilityGating: An Unsupported Pack Contributes No Tools

**Test**: `ToolPackBuilder_Build_PackRequiringUndeclaredCapability_ContributesNoTools`

The central scenario: the model is never offered a tool it cannot use, so it cannot waste a turn
on it or rationalize around a refusal.

#### AgentKitCore-ToolPackBuilder-CapabilityGating: An Unsupported Pack Is Not Asked for Tools

**Test**: `ToolPackBuilder_Build_PackRequiringUndeclaredCapability_IsNotAskedToCreateTools`

The scenario that distinguishes "not registered" from "registered then filtered". Asserts the pack
was never asked to create its tools, so no tool it would have produced exists at all.

#### AgentKitCore-ToolPackBuilder-CapabilityGating: A Supported Pack Contributes Its Tools

**Test**: `ToolPackBuilder_Build_PackRequiringDeclaredCapability_ContributesItsTools`

The complementary scenario, without which the gate could be satisfied by registering nothing:
gating withholds nothing the host can support.

#### AgentKitCore-ToolPackBuilder-CapabilityGating: A Pack Requiring Nothing Is Always Registered

**Test**: `ToolPackBuilder_Build_PackRequiringNoCapability_IsRegisteredByEveryHost`

Asserts that "requires none" is the natural expression of "always available", even for the barest
possible host.

#### AgentKitCore-ToolPackBuilder-DefaultCapabilitiesNone: An Undeclared Host Gets Only Undemanding Packs

**Test**: `ToolPackBuilder_Build_NoCapabilitiesDeclared_RegistersOnlyPacksRequiringNone`

Asserts the default is the closed one: a host that forgets to declare gets fewer tools, never
more.

#### AgentKitCore-ToolPackBuilder-CapabilityDeclarationReplaced: A Later Declaration Replaces the Earlier One

**Test**: `ToolPackBuilder_WithHostCapabilities_CalledTwice_LastDeclarationReplacesTheFirst`

Declares vision and then corrects the declaration to none, asserting the correction takes effect.
Combining the two would make the declaration a ratchet no caller could narrow.

#### AgentKitCore-ToolPackBuilder-CapabilityDeclarationReplaced: Declaring After Adding Still Governs

**Test**: `ToolPackBuilder_WithHostCapabilities_DeclaredAfterAdd_StillGovernsRegistration`

Asserts gating happens when the composition is built, so a builder's result never depends on the
order of two calls that each look complete on their own.

#### AgentKitCore-ToolPackBuilder-PrefixCollision: A Second Pack Claiming One Prefix Is Refused

**Test**: `ToolPackBuilder_Add_SecondPackWithSameFamilyPrefix_ThrowsArgumentException`

Error path: the collision is refused at the call that caused it, naming the pack responsible.

#### AgentKitCore-ToolPackBuilder-PrefixCollision: Adding the Same Pack Twice Is Refused

**Test**: `ToolPackBuilder_Add_SamePackInstanceTwice_ThrowsArgumentException`

Asserts that a double registration is a collision like any other; it would publish every one of
that pack's tools twice.

#### AgentKitCore-ToolPackBuilder-PrefixCollision: A Prefix Differing Only by Case Is Not a Collision

**Test**: `ToolPackBuilder_Add_PrefixDifferingOnlyByCase_IsNotACollision`

Boundary condition recorded as a deliberate decision rather than an oversight. Comparison is
ordinal, so the uppercase prefix is refused where it belongs — by the naming rules, with a message
about the name — rather than here, with a message about a collision that is not one.

#### AgentKitCore-ToolPackBuilder-PrefixAgreement: A Tool Outside the Declared Family Is Refused

**Test**: `ToolPackBuilder_Build_ToolNameOutsideDeclaredFamily_ThrowsInvalidOperationException`

Asserts the declaration is enforced rather than trusted. Without this, a pack publishing outside
its declared family would defeat the collision check silently.

#### AgentKitCore-ToolPackBuilder-PrefixAgreement: A Tool Carrying the Declared Prefix Is Accepted

**Test**: `ToolPackBuilder_Build_ToolNameCarryingDeclaredPrefix_IsAccepted`

The complementary scenario: the agreement check rejects nothing legitimate.

#### AgentKitCore-ToolPackBuilder-MalformedPackRejected: A Missing Pack Is Refused

**Test**: `ToolPackBuilder_Add_NullPack_ThrowsArgumentNullException`

Error path: a programming error in the composing application.

#### AgentKitCore-ToolPackBuilder-MalformedPackRejected: A Pack With No Family Prefix Is Refused

**Test**: `ToolPackBuilder_Add_EmptyFamilyPrefix_ThrowsArgumentException`

Error path: a pack owning no prefix could not own any of its tool names.

#### AgentKitCore-ToolPackBuilder-MalformedPackRejected: A Pack Returning No Collection Is Refused

**Test**: `ToolPackBuilder_Build_PackReturningNullCollection_ThrowsInvalidOperationException`

Asserts the contract is enforced across the package boundary, because a pack may come from a
package compiled without nullable annotations.

#### AgentKitCore-ToolPackBuilder-MalformedPackRejected: A Pack Returning a Missing Tool Is Refused

**Test**: `ToolPackBuilder_Build_PackReturningNullTool_ThrowsInvalidOperationException`

Error path: refused where the cause is still identifiable, rather than far away in the runtime's
tool list with an error naming nothing a developer could act on.

#### AgentKitCore-ToolPackBuilder-RepeatableBuild: Building Twice Yields the Same Composition

**Test**: `ToolPackBuilder_Build_CalledTwice_ReturnsTheSameComposition`

Asserts the builder neither empties nor freezes itself. A builder that emptied itself would return
an empty list on a second call, which is a trap rather than a discipline.

#### AgentKitCore-ToolPackBuilder-RepeatableBuild: A Pack Added After a Build Appears in the Next One

**Test**: `ToolPackBuilder_Build_PackAddedAfterAnEarlierBuild_AppearsInTheNextBuild`

Asserts each build reports the builder's state as it is at that call, so a cached first answer
would fail this scenario.
