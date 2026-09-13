### TextFilePack Unit Verification Design

This document describes the unit-level verification strategy for the `TextFilePack` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses the public pack boundary; prefix, capabilities,
seven-tool order, no-null contract, shared buffers and supplied-policy behavior are checked. This
keeps verification at the same boundary the runtime or composing application uses, rather than
proving a substitute behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `TextFile/TextFilePackTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary state needed to verify composition behavior
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 7 requirement scenarios below, covering 10 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, wrong capability or tool order, ignored policy
decision, leaked path, unsafe file mutation, malformed request thrown as a framework error, or
returned content that violates a configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-TextFile-Pack-FamilyPrefix: Family Prefix

**Test**: `TextFilePack_FamilyPrefix_Constant_IsTextFile`

**Test**: `TextFilePack_FamilyPrefix_Contract_ReportsTheDeclaredConstant`

The listed tests prove the published family prefix constant is the family name; the contract reports
the same prefix the class publishes as a constant.

##### AgentKitTools-TextFile-Pack-NoCapabilityRequired: No Capability Required

**Test**: `TextFilePack_RequiredCapabilities_Pack_RequiresNoHostCapability`

The listed tests prove the family asks nothing of its host.

##### AgentKitTools-TextFile-Pack-RegistersSevenTools: Registers Seven Tools

**Test**: `TextFilePack_CreateTools_Policy_CreatesTheSevenToolsInOrder`

**Test**: `TextFilePack_CreateTools_Policy_ReturnsNoNullTool`

The listed tests prove the pack creates the seven tools in the fixed, documented order; the pack
honors the contract obligation to return no null tool.

##### AgentKitTools-TextFile-Pack-ToolsCarryFamilyPrefix: Tools Carry Family Prefix

**Test**: `TextFilePack_CreateTools_EveryTool_CarriesTheFamilyPrefix`

The listed tests prove every tool the pack creates carries the family prefix it declares.

##### AgentKitTools-TextFile-Pack-RequiresPolicy: Requires Policy

**Test**: `TextFilePack_CreateTools_NullPolicy_ThrowsArgumentNullException`

The listed tests prove a missing policy is a programming error rather than a denial.

##### AgentKitTools-TextFile-Pack-PolicyGovernsCreatedTools: Policy Governs Created Tools

**Test**: `TextFilePack_CreateTools_SuppliedPolicy_GovernsTheCreatedTools`

The listed tests prove the policy the composer supplies is the one governing the created tools.

##### AgentKitTools-TextFile-Pack-SharedBuffer: Shared Buffer

**Test**: `TextFilePack_CreateTools_CutAndPaste_ShareOneBufferPerComposition`

**Test**: `TextFilePack_CreateTools_TwoCompositions_DoNotShareBufferSlots`

The listed tests prove the cut and paste tools one composition produces share a buffer, so a range
cut through one is pasteable through the other; two separate compositions do not share buffer slots.
