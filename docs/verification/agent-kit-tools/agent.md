## Agent Subsystem Verification Design

This document describes the subsystem-level verification strategy for the Agent tool family.

### Verification Approach

The subsystem is verified through integration tests that exercise the family the way an
application does: composed through the AgentKitCore `ToolPackBuilder` under one access policy
and a declared host capability, then invoked through the published tool list by name and argument
dictionary, exactly as an agent runtime invokes it. The runner is a test-owned lambda that
records the request it received, because the property under verification is what the *library*
hands the host, not how a real provider replies. The policy, the profiles and the
`ToolPackBuilder` are all real, because the properties under verification — that a non-delegating
host receives none of the family's tools, that a child's tool list is composed for the child
rather than filtered from the parent's, and that a chain of delegations is refused at the ceiling
— are properties of the real thing.

The scenarios here assert what belongs to the family as a whole: one family prefix, one tool
attached under it, the family withheld from a host without the Delegation capability, every
published name validated, the child's answer round-tripping through the runner, delegation chains
bounded at the depth ceiling, and the child's tools composed against the child's own state rather
than the parent's list. The algorithm of any single unit is verified in that unit's own document.

The agent family exists to compose *other* families' packs for a child, so its behavior is only
observable in the presence of another family's tools. Verifying that with a real sibling family
would make an agent test's failure indistinguishable from that sibling's, so the subsystem tests
use the local `StubToolPack` — the smallest thing that satisfies `IToolPack` and publishes named
do-nothing tools, alongside the shared `TemporaryDirectory` helper for policies that need a real
working directory. Both live in `test/DemaConsulting.AgentKit.Tools.Tests/Agent/` and are test
helpers, not tests. The one cross-family scenario — a delegated agent keeping its own task list
under a real todo family — belongs at system-composition scope, where a sibling family is
legitimately in the picture, and is verified in `SystemCompositionTests` as
`AgentKitTools_SystemComposition_DelegatedAgent_KeepsItsOwnTaskList`.

Subsystem tests reside in `Agent/AgentTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project, alongside the `StubToolPack` and
`TemporaryDirectory` helpers just described.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **File system**: Each scenario that needs a working directory creates its own temporary tree
  through `TemporaryDirectory` and deletes it afterwards
- **Runner**: A test-owned lambda that returns a stated string, records the request, or throws;
  no model provider is contacted
- **Isolation**: Each test constructs its own policy, composition and helpers; no state is
  shared between tests

### Acceptance Criteria

A subsystem test run passes when all six scenarios below pass without error or exception beyond
those explicitly asserted. A tool published outside the family prefix, a family registered for a
host without the Delegation capability, a tool offered without a validated name or a description,
a delegated child whose final text is not returned to the parent, a chain reaching past the
depth ceiling, and a child's tool list drawn from the parent's rather than composed for the
child each constitute a failure.

### Test Scenarios

#### AgentKitTools-Agent-FamilyComposition: The Family Publishes the Run Tool

**Test**: `Agent_Family_ComposedThroughBuilder_PublishesTheRunTool`

Normal operation: composes the pack through a `ToolPackBuilder` on a delegation-capable host and
asserts the run tool name, confirming the family is attached as one unit and publishes what it
promises.

#### AgentKitTools-Agent-CapabilityGated: A Host Without Delegation Receives No Tools

**Test**: `Agent_Family_HostWithoutDelegation_ContributesNoTools`

Boundary condition: a host that declares no capability receives none of the family's tools,
confirming the family is withheld from a host that cannot start a second agent.

#### AgentKitTools-Agent-GuardedConstruction: Every Tool Carries a Validated Name and a Description

**Test**: `Agent_Family_EveryTool_CarriesAValidatedNameAndDescription`

Runs each published name through the naming convention's own validation and asserts a non-empty
description, confirming no tool is offered to a model that the model cannot identify or choose.

#### AgentKitTools-Agent-FamilyComposition: A Delegated Agent's Final Text Is Returned to the Parent

**Test**: `Agent_Family_Delegation_ReturnsTheFinalTextOfTheChild`

Normal operation for the request an agent actually makes: the parent calls `agent_run` with a
profile name and a task, the runner returns a stated string, and the scenario asserts the tool
result carries that string unchanged. This is the load-bearing normal path: it proves the family
routes the model's selection to the profile the application registered, calls the runner with a
`ChildAgentRequest` shaped from that profile, and reports the runner's answer back to the parent.

#### AgentKitTools-Agent-DepthBounded: A Delegation Chain Is Refused at the Depth Ceiling

**Test**: `Agent_Family_DelegationChain_IsRefusedAtTheDepthCeiling`

Boundary condition and budget: composes the family with a small `MaxAgentDepth` and asserts that
a chain of delegations refuses at the ceiling with the ceiling and the current level named,
without the runner being asked to start anything at the refused level.

#### AgentKitTools-Agent-ChildIsolation: A Child's Tools Are Composed for the Child

**Test**: `Agent_Family_ChildTools_AreComposedForTheChild`

The load-bearing isolation scenario: composes the family with a `StubToolPack` as the child
pack, delegates through `agent_run`, and asserts on the request the runner received that
`Tools` was newly composed against the child's policy — the stub records each policy it was
built against, and every child's tools carry a fresh identity per composition. A parent-bound
list filtered here would carry the parent's identities instead.
