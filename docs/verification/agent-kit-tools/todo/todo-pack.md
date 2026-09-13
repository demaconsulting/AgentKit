### TodoPack Unit Verification Design

This document describes the unit-level verification strategy for the `TodoPack` class.

#### Verification Approach

Nothing is mocked or stubbed. Each scenario uses the public pack boundary; the family prefix,
required capability, the three-tool registration in order, the supplied-policy requirement,
per-composition isolation of the task list and the suggested-instruction constant are all checked
directly. This keeps verification at the same boundary the composing application uses, rather
than proving a substitute behaves consistently with itself.

Unit tests reside in `Todo/TodoPackTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **State**: The scenarios that write to a list create fresh compositions of their own
- **Isolation**: Each test constructs its own pack, policy and tool list; no state is shared

#### Acceptance Criteria

A unit test run passes when all 6 requirement scenarios below, covering 6 listed test method
entries, pass without error or exception beyond those explicitly asserted. A wrong prefix, a
required capability, tools registered out of order, an accepted null policy, a task list shared
between two compositions of the same pack instance, or a suggested-instruction constant that no
longer names the tool or the transitions it asks the agent to make constitutes a failure.

#### Test Scenarios

##### AgentKitTools-Todo-Pack-FamilyPrefix: Family Prefix

**Test**: `TodoPack_FamilyPrefix_IsPublishedAsConstantAndContract`

The listed tests prove the pack publishes the family prefix `todo` as a constant and through the
`IToolPack` contract, so the constant and the contract cannot drift.

##### AgentKitTools-Todo-Pack-NoCapabilityRequired: No Capability Required

**Test**: `TodoPack_RequiredCapabilities_IsNone`

The listed tests prove the pack requires no host capability, so every host receives the family.

##### AgentKitTools-Todo-Pack-RegistersThreeTools: Registers Three Tools

**Test**: `TodoPack_CreateTools_RegistersTheFamilyInOrder`

The listed tests prove the pack creates its three tools — list, set and remove — in the fixed
order a model sees them in.

##### AgentKitTools-Todo-Pack-RequiresPolicy: Requires Policy

**Test**: `TodoPack_CreateTools_NullPolicy_ThrowsArgumentNullException`

The listed tests prove the pack requires a policy to create its tools, even though its tools
never consult one, because a composing application that forgot a policy is named at the point it
made the mistake rather than by a sibling family later.

##### AgentKitTools-Todo-Pack-IndependentLists: Independent Lists

**Test**: `TodoPack_CreateTools_CalledTwice_ProducesIndependentLists`

The listed tests prove each `CreateTools` call allocates a task list of its own, so two
compositions of the same pack instance never share a list. This is the property that keeps a
delegated agent's list out of its parent's: a sub-agent is composed by a separate `CreateTools`
call and therefore receives a separate store.

##### AgentKitTools-Todo-Pack-SuggestedInstruction: Suggested Instruction

**Test**: `TodoPack_SuggestedInstruction_NamesTheToolsAndTheStatusTransitions`

The listed tests prove the pack publishes the suggested instruction as a constant that names
`todo_set` and both of the status transitions it asks the agent to make — `in_progress` and
`done` — so the wording that produced the measured 3-of-3 result cannot drift from the wording
that ships. The measurements themselves are recorded in the subsystem design document.
