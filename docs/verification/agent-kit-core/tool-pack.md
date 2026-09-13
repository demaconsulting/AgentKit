## ToolPack Unit Verification Design

This document describes the unit-level verification strategy for the `IToolPack` contract and the
`HostCapabilities` enumeration.

### Verification Approach

`IToolPack` is an interface, so the only way to exercise it is through an implementation the test
controls. The unit tests use the shared `StubToolPack` test double for that purpose and assert
that what a pack declares is what a composer reads back, and that the access policy a composer
supplies is the one the pack is handed rather than one the pack invented.

`HostCapabilities` is verified by reading the enumeration itself. Two of those scenarios are
**reflective rather than literal**: the flags attribute is read through reflection, and the
zero-or-single-bit property is asserted over every declared member. Written that way, the
scenarios keep protecting members that do not exist yet — a member added later that overlapped
another would silently satisfy a requirement the host never declared, and that is exactly the
failure nobody would notice by reading the diff.

Nothing in this unit is reachable from a model's tool call, so no scenario here concerns a runtime
refusal; the obligations the contract places on an implementation are enforced and verified in
_ToolPackBuilder Unit Verification Design_.

Unit tests reside in `ToolPackTests.cs`, with the shared test double in `StubToolPack.cs`, within
the `DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, file system access or network access required
- **Mocking**: A hand-written test double, `StubToolPack`, implements the contract under
  verification; nothing else is substituted
- **Isolation**: Each test constructs its own pack; no state is shared

### Acceptance Criteria

A unit test run passes when all nine scenarios below pass without error or exception beyond those
explicitly asserted. A declaration that does not read back, a policy that does not reach the pack,
a capability member that is neither zero nor a single bit, and a missing flags attribute each
constitute a failure.

### Test Scenarios

#### AgentKitCore-ToolPack-Contract: A Pack Reports Its Declared Family Prefix

**Test**: `ToolPack_FamilyPrefix_Implementation_ReportsItsDeclaredPrefix`

Normal operation: the prefix a pack declares is the prefix a composer reads through the contract.

#### AgentKitCore-ToolPack-Contract: A Pack Reports Its Declared Capabilities

**Test**: `ToolPack_RequiredCapabilities_Implementation_ReportsItsDeclaredCapabilities`

Asserts that a pack needing vision says so rather than deciding for itself whether it can operate.

#### AgentKitCore-ToolPack-Contract: The Supplied Policy Is Handed to the Pack

**Test**: `ToolPack_CreateTools_SuppliedPolicy_IsHandedToThePack`

Asserts the identity of the policy instance, not merely its equivalence, confirming a pack builds
its tools against the policy it was given rather than one of its own.

#### AgentKitCore-ToolPack-NoCapabilityRequired: The No-Capability Member Is Zero

**Test**: `ToolPack_HostCapabilities_None_IsZero`

Boundary condition: asserts both that `None` is zero and that it is the default of the
enumeration, which is what makes an undeclared host the closed case rather than the open one.

#### AgentKitCore-ToolPack-NoCapabilityRequired: Requiring Nothing Is Always Satisfied

**Test**: `ToolPack_HostCapabilities_NoneRequirement_IsSatisfiedByEveryDeclaration`

Asserts the gating expression against a host declaring nothing and a host declaring vision,
confirming "always available" needs no special case in the rule.

#### AgentKitCore-ToolPack-CapabilitiesAreFlags: The Enumeration Is Declared as Flags

**Test**: `ToolPack_HostCapabilities_Enum_IsDeclaredAsFlags`

Asserts the attribute is present. Without it the members would not combine and a combined value
would render as a bare number, so the attribute is part of the contract rather than decoration.

#### AgentKitCore-ToolPack-CapabilitiesAreFlags: Every Member Is Zero or a Single Bit

**Test**: `ToolPack_HostCapabilities_EveryMember_IsZeroOrASingleBit`

The guard on future members. Iterates every declared member reflectively, so a member added later
that overlapped another would fail this scenario rather than silently satisfy a requirement the
host never declared.

#### AgentKitCore-ToolPack-CapabilitiesAreFlags: Vision Is Defined and Distinct From None

**Test**: `ToolPack_HostCapabilities_Vision_IsDefinedAndDistinctFromNone`

Asserts the capability the image tool family requires exists and that declaring it is a real
declaration rather than the absence of one.

#### AgentKitCore-ToolPack-DeclaredCapabilities: Delegation Is Defined and Independent of Vision

**Test**: `ToolPack_HostCapabilities_Delegation_IsDefinedAndIndependentOfVision`

Asserts the capability the agent tool family requires exists, is distinct from requiring nothing,
occupies a bit of its own, and composes with vision rather than replacing it: a host declaring
both satisfies each requirement on its own. This is what keeps an application that can show a
model an image from being taken to have said it will also start a second agent.
