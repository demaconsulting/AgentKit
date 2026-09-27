### TextFilePack Unit Verification Design

This document describes the unit-level verification strategy for the `TextFilePack` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses the public pack boundary; prefix, capabilities,
eight-tool order, no-null contract, shared buffers and supplied-policy behavior are checked. This
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

A unit test run passes when all 8 requirement scenarios below, covering 15 listed test method
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

##### AgentKitTools-TextFile-Pack-RegistersEightTools: Registers Eight Tools Under a Write-Granting Policy

**Test**: `TextFilePack_CreateTools_WriteGrantingPolicy_CreatesTheEightToolsInOrder`

**Test**: `TextFilePack_CreateTools_ReadOnlyWorkspaceWithWritableSession_PublishesEveryTool`

**Test**: `TextFilePack_CreateTools_Policy_ReturnsNoNullTool`

The three listed tests prove the pack creates the eight tools in the fixed, documented order
whenever the policy permits writing in any location; that a policy granting the workspace read-only
and a separate session location read-write still publishes all eight, because the question is asked
of the whole policy rather than of the anchor; and that the pack honors the contract obligation to
return no null tool.

##### AgentKitTools-TextFile-Pack-SuppressesWriteToolsWithoutAWriteGrant: Withholds the Write Tools Without a Write Grant

**Test**: `TextFilePack_CreateTools_ReadOnlyPolicy_PublishesOnlyTheNonWritingTools`

**Test**: `TextFilePack_CreateTools_NoGrants_PublishesOnlyTheNonWritingTools`

**Test**: `TextFilePack_CreateTools_ReadOnlyWorkspaceWithWritableSession_PublishesEveryTool`

The three listed tests prove that a policy whose every grant is read-only publishes exactly
`text_file_search`, `text_file_read` and `text_file_copy_lines`, in that relative order, so the five
tools that can act only by writing are withheld rather than offered and refused; that a policy
holding no grants at all withholds the same five, so the fully-confined case is not one the filter
overlooks; and that the suppression lifts as soon as any location is writable, so the test set can
distinguish a correct filter from one that suppresses unconditionally.

**The no-grants test asserts what is true of the survivors, not that they are usable.** Under an
empty grant set the three published tools each refuse every path, because no path resolves, so the
test invokes the read tool against a real file in the composition's own working directory and
asserts a `PathNotPermitted` denial. Only writing is gated, and no symmetric reading gate is built:
a grant is read-only or read-write, so write access always implies read access, and a reading tool
is unusable only under a policy granting nothing whatever — a policy whose agent can touch no file
however its tool list is trimmed.

The first two assert the exact ordered name list rather than set membership, because the surviving
tools must keep their relative order — `text_file_copy_lines` sits between two withheld groups, so
a filter that collapsed them into one block would move it.

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

**Test**: `TextFilePack_CreateTools_WriteAndPaste_ShareOneBufferPerComposition`

**Test**: `TextFilePack_CreateTools_TwoCompositions_DoNotShareBufferSlots`

The listed tests prove the cut and paste tools one composition produces share a buffer, so a range
cut through one is pasteable through the other; the write and paste tools of that same composition
share it too, so content a write displaced is recoverable through the paste tool's `overwritten`
slot; and two separate compositions do not share buffer slots.
