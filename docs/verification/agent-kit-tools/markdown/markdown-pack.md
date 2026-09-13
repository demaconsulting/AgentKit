### MarkdownPack Unit Verification Design

This document describes the unit-level verification strategy for the `MarkdownPack` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses the public pack boundary; prefix, capabilities,
outline-tool registration and supplied-policy requirement are checked. This keeps verification at
the same boundary the runtime or composing application uses, rather than proving a substitute
behaves consistently with itself.

Returned results and file system state are both checked where the unit mutates or reads files. A
successful response must correspond to the real state change or read, and a refusal must leave the
protected state unchanged.

Unit tests reside in `Markdown/MarkdownPackTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: Each scenario creates the temporary state needed to verify composition behavior
- **Isolation**: Each test constructs its own policy, tool, pack or helper state; no state is shared

#### Acceptance Criteria

A unit test run passes when all 4 requirement scenarios below, covering 4 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing name or
description, accepted null construction input, wrong capability or tool order, ignored policy
decision, leaked path, unsafe file mutation, malformed request thrown as a framework error, or
returned content that violates a configured ceiling constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Markdown-Pack-FamilyPrefix: Family Prefix

**Test**: `MarkdownPack_FamilyPrefix_IsPublishedAsConstantAndContract`

The listed tests prove the pack publishes its family prefix as a constant and through the contract.

##### AgentKitTools-Markdown-Pack-NoCapabilityRequired: No Capability Required

**Test**: `MarkdownPack_RequiredCapabilities_IsNone`

The listed tests prove the pack requires no host capability, so every host receives the family.

##### AgentKitTools-Markdown-Pack-RegistersOutlineTool: Registers Outline Tool

**Test**: `MarkdownPack_CreateTools_RegistersTheOutlineTool`

The listed tests prove the pack creates its single outline tool.

##### AgentKitTools-Markdown-Pack-RequiresPolicy: Requires Policy

**Test**: `MarkdownPack_CreateTools_NullPolicy_ThrowsArgumentNullException`

The listed tests prove the pack requires a policy to create its tools.
