## Todo Subsystem Verification Design

This document describes the subsystem-level verification strategy for the Todo tool family.

### Verification Approach

The subsystem is verified through integration tests that exercise the family the way an application
does: composed through the AgentKitCore `ToolPackBuilder` under one access policy, then invoked
through the published tool list by name and argument dictionary, exactly as an agent runtime invokes
it. Nothing is mocked. The task list, the tools and the returned refusals are the real ones a
composing application would receive.

The scenarios here assert what belongs to the family as a whole: three tools under one prefix, one
list shared by those three tools, a whole plan that can be written down, advanced and closed out
through the published tool list, and refusals that are returned values rather than exceptions. The
per-agent isolation of the list and the published instruction wording belong to the pack unit and
are exercised at that level; both are surfaced here because they are family-wide properties. The
algorithm of any single tool is verified in that unit's own document.

The boundary the subsystem is exercised at is deliberately the composed tool list rather than the
units' internal factories, because that list is what an application actually attaches. A tool that
could not be reached that way would not be reachable by an agent either.

Subsystem tests reside in `Todo/TodoTests.cs` within the `DemaConsulting.AgentKit.Tools.Tests`
project. The per-agent isolation and instruction-text scenarios reside in `Todo/TodoPackTests.cs`
and are cited from the corresponding subsystem requirements below.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **File system**: The family touches no files; the policy used by the scenarios is any valid one
- **Isolation**: Each test constructs its own policy, composition and task list; no state is shared

### Acceptance Criteria

A subsystem test run passes when all 6 requirement scenarios below, covering 6 listed test method
entries, pass without error or exception beyond those explicitly asserted. A missing tool, a wrong
family prefix, a shared list across compositions, a thrown refusal, a status the family does not
publish being silently accepted, or a suggested-instruction constant that drifts from the tools it
names constitutes a failure.

### Test Scenarios

#### AgentKitTools-Todo-FamilyComposition: Family Composition

**Test**: `Todo_Family_ComposedThroughBuilder_PublishesTheFamily`

The listed tests prove a composition attaching the family publishes the three tools — list, set and
remove — in order under the `todo` prefix.

#### AgentKitTools-Todo-GuardedConstruction: Guarded Construction

**Test**: `Todo_Family_EveryTool_CarriesAValidatedNameAndDescription`

The listed tests prove every tool in the family carries a valid name, the family prefix and a
description, so the guarded construction path was the one used.

#### AgentKitTools-Todo-PerAgentList: Per-Agent List

**Test**: `TodoPack_CreateTools_CalledTwice_ProducesIndependentLists`

The listed tests prove two compositions of the same pack instance receive independent task lists,
so a delegated agent — whose tools are composed by a separate `CreateTools` call — keeps its own
list rather than reaching into its parent's.

#### AgentKitTools-Todo-TaskLifecycle: Task Life Cycle

**Test**: `Todo_Family_MultiPhasePlan_IsRecordedAdvancedAndClosedOut`

The listed tests prove a whole plan can be written down, advanced, corrected and closed out
through the published tools alone, with each tool observing the same list: three phases are
planned, the first is advanced through `in_progress` to `done` in place, the third is removed, and
the list reported by `todo_list` matches the writes the set and remove tools recorded.

#### AgentKitTools-Todo-DenialsAreResults: Denials Are Results

**Test**: `Todo_Family_RefusedRequests_AreReturnedValues`

The listed tests prove a status the family does not publish and a removal of an identifier the
list does not hold are both returned as refusal text a model can read and act on, rather than as
exceptions that would end the agent's turn.

#### AgentKitTools-Todo-InstructionRequired: Instruction Required

**Test**: `TodoPack_SuggestedInstruction_NamesTheToolsAndTheStatusTransitions`

The listed tests prove the pack publishes the suggested instruction as a constant that names
`todo_set` and both of the status transitions it asks the agent to make, so the wording that ships
cannot drift from the wording that was measured. The measurements themselves — 1-of-5 runs under a
soft instruction against 3-of-3 runs under an explicit one — are recorded in the subsystem design
document.
