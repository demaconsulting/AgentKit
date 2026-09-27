### FilePack Unit Verification Design

This document describes the unit-level verification strategy for the `FilePack` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses the public pack boundary; prefix, capabilities,
tool order, tool metadata and supplied-policy requirement are checked. This keeps verification
at the same boundary the runtime or composing application uses, rather than proving a substitute
behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `File/FilePackTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary state needed to verify composition behavior
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 6 requirement scenarios below, covering 8 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, wrong capability or tool order, ignored policy
decision, leaked path, unsafe file mutation, malformed request thrown as a framework error, or
returned content that violates a configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-File-Pack-FamilyPrefix: Family Prefix

**Test**: `FilePack_FamilyPrefix_IsPublishedAsConstantAndContract`

The listed tests prove the pack publishes its family prefix as a constant and through the contract.

##### AgentKitTools-File-Pack-NoCapabilityRequired: No Capability Required

**Test**: `FilePack_RequiredCapabilities_IsNone`

The listed tests prove the pack requires no host capability, so every host receives the family.

##### AgentKitTools-File-Pack-RegistersEveryTool: Registers Every Tool Under a Write-Granting Policy

**Test**: `FilePack_CreateTools_WriteGrantingPolicy_RegistersEveryToolInOrder`

**Test**: `FilePack_CreateTools_ReadOnlyWorkspaceWithWritableSession_PublishesEveryTool`

The two listed tests prove the pack creates every tool it publishes in the documented order —
the file tools first, the directory tools following as a block — whenever the
policy permits writing in any location, and that a policy granting the workspace read-only and a
separate session location read-write still publishes all of them, because the question is asked of
the whole policy rather than of the anchor.

##### AgentKitTools-File-Pack-SuppressesWriteToolsWithoutAWriteGrant: Withholds Every Write Tool Without a Write Grant

**Test**: `FilePack_CreateTools_ReadOnlyPolicy_PublishesOnlyTheListTool`

**Test**: `FilePack_CreateTools_ReadOnlyWorkspaceWithWritableSession_PublishesEveryTool`

The two listed tests prove that a policy whose every grant is read-only publishes exactly
`file_list`, so the six tools that change the file system are withheld rather than offered and
refused; and that the suppression lifts as soon as any location is writable, so the test set can
distinguish a correct filter from one that suppresses unconditionally. Listing survives because it
needs only the read decision to report what exists.

##### AgentKitTools-File-Pack-ToolsCarryFamilyPrefix: Tools Carry Family Prefix

**Test**: `FilePack_CreateTools_EveryTool_CarriesAValidatedNameAndDescription`

The listed tests prove every tool the pack publishes carries a valid, family-prefixed name and a
description.

##### AgentKitTools-File-Pack-RequiresPolicy: Requires Policy

**Test**: `FilePack_CreateTools_NullPolicy_ThrowsArgumentNullException`

The listed tests prove the pack requires a policy to create its tools.
