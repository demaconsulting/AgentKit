# System Verification Design

This document describes the system-level verification strategy for the AgentKit Tools.

## Verification Approach

The AgentKit Tools system is verified through system-level integration tests that exercise the
package as a whole from the perspective of a consumer. Tests compose the package through its
public integration surface — the AgentKitCore pack contract — and assert on observable outputs,
without relying on knowledge of internal implementation details. No mocking or stubbing is
required at the system level.

The system under verification at this stage is the package's integration surface: the promise that
its tool families are composed through the AgentKitCore `ToolPackBuilder` under one access policy,
that a composition to which no family has been attached is well defined and contributes no tools,
and that attaching a family contributes exactly that family's tools — or, where the family declares
a host capability, contributes them only when the host provides it. The first such family, the
TextFile family, has its own subsystem and unit verification; see _TextFile Subsystem Verification
Design_. The File family and the Markdown family each likewise have their own subsystem and unit
verification; see _File Subsystem Verification Design_ and _Markdown Subsystem Verification Design_.
The Image family, gated on the Vision capability, likewise has its own; see _Image
Subsystem Verification Design_. The Todo family and the Agent family — the latter gated on the
Delegation capability — likewise have their own; see _Todo Subsystem Verification Design_ and
_Agent Subsystem Verification Design_. The Memory family likewise has its own; see _Memory
Subsystem Verification Design_. The families introduced in subsequent increments are
verified the same way.

One scenario at this level is genuinely cross-family rather than compositional: a delegated agent
keeping a task list separate from its parent's spans the Agent and Todo subsystems, so it cannot be
verified from inside either one without making one subsystem's tests depend on the other's units.
It is therefore verified here.

System tests reside in `AgentKitToolsTests.cs` within the
`DemaConsulting.AgentKit.Tools.Tests` project.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **File system**: No file system access is required; the composition baseline is exercised
  entirely in memory
- **Isolation**: Each test method constructs its own policy and builder; no state is shared
  between tests

## External Interface Simulation

No external interface is simulated at the system level. The composition scenarios run against the
in-memory AgentKitCore pack contract, so there is nothing to substitute. The file-system interface
the TextFile family exercises is not simulated either: its subsystem and unit tests use a real
temporary directory tree containing a real reparse point, because path containment is a security
control and a simulated link would prove only that the simulation was written consistently.

## System-Level Test Scenarios

### Composition: An Empty Builder Contributes No Tools

**Test**: `AgentKitTools_SystemComposition_EmptyBuilder_ContributesNoTools`

Verifies that the package composes through the AgentKitCore pack contract and that a composition to
which no family has been attached contributes no tools. Constructs a real access policy from two
unrestricted rules, builds a `ToolPackBuilder` governed by that policy without adding any family,
and asserts the resulting tool list is empty. Confirms the integration surface is well defined
before any family is added and that the package is a peer composed like any other AgentKit pack.

### Composition: The TextFile Family Is Contributed to a Composition

**Test**: `AgentKitTools_SystemComposition_TextFilePack_ContributesTheTextFileFamily`

Verifies that attaching the TextFile pack contributes that family's tools to a composition, under
the one family prefix the pack claims. Constructs a real access policy, adds `TextFilePack` to a
`ToolPackBuilder` governed by it, and asserts the composed list is exactly `text_file_search`,
`text_file_read`, `text_file_create`, `text_file_replace`, `text_file_cut_lines`,
`text_file_copy_lines` and `text_file_paste_lines`. Confirms at the system level that a family is
attached as
one pack rather than tool by tool, and that the package now contributes a capability rather than
only a composition baseline.

### Composition: The File Family Is Contributed to a Composition

**Test**: `AgentKitTools_SystemComposition_FilePack_ContributesTheFileFamily`

Verifies that attaching the File pack contributes that family's tools to a composition, under
the one family prefix the pack claims. Constructs a real access policy, adds `FilePack` to a
`ToolPackBuilder` governed by it, and asserts the composed list is exactly `file_list`,
`file_copy`, `file_move` and `file_delete`.

### Composition: The Markdown Family Is Contributed to a Composition

**Test**: `AgentKitTools_SystemComposition_MarkdownPack_ContributesTheMarkdownFamily`

Verifies that attaching the Markdown pack contributes that family's tools to a composition,
under the one family prefix the pack claims. Constructs a real access policy, adds
`MarkdownPack` to a `ToolPackBuilder` governed by it, and asserts the composed list is exactly
`markdown_outline`.

### Composition: The Image Family Is Contributed to a Vision Host

**Test**: `AgentKitTools_SystemComposition_ImagePack_ContributesTheImageFamily`

Verifies that attaching the Image pack to a host that declares the Vision capability contributes
that family's tools, under the one family prefix the pack claims. Constructs a real access policy,
declares Vision on a `ToolPackBuilder`, adds `ImagePack`, and asserts the composed list is exactly
`image_read`. Confirms at the system level that a capability-gated family, on a host that meets its
requirement, is attached as one pack.

### Composition: The Image Family Is Withheld From a Non-Vision Host

**Test**: `AgentKitTools_SystemComposition_ImagePackWithoutVision_ContributesNoTools`

Verifies that attaching the Image pack to a host that declares no capability contributes no tools.
Constructs a real access policy, adds `ImagePack` to a `ToolPackBuilder` that declares nothing, and
asserts the composed list is empty. Confirms at the system level that a family whose required
capability the host has not declared is withheld, so a model that cannot see is never offered a tool
that returns content it could only fabricate around.

### Composition: The Todo Family Is Contributed to a Composition

**Test**: `AgentKitTools_SystemComposition_TodoPack_ContributesTheTodoFamily`

Verifies that attaching the Todo pack contributes that family's tools to a composition, under the
one family prefix the pack claims. Constructs a real access policy, adds `TodoPack` to a
`ToolPackBuilder` governed by it, and asserts the composed list is exactly `todo_list`, `todo_set`
and `todo_remove`.

### Composition: The Memory Family Is Contributed to a Composition

**Test**: `AgentKitTools_SystemComposition_MemoryPack_ContributesTheMemoryFamily`

Verifies that attaching the Memory pack contributes that family's tools to a composition, under the
one family prefix the pack claims. Constructs a real access policy and a deterministic offline
embedding generator, adds `MemoryPack` to a `ToolPackBuilder` governed by that policy, and asserts
the composed list is exactly `memory_file`, `memory_recall`, `memory_update`, `memory_revise` and
`memory_forget`. No real embedding backend is involved: the family is composable without one being
reachable, and which backend an application chose is invisible to this package.

### Composition: The Agent Family Is Contributed to a Delegating Host

**Test**: `AgentKitTools_SystemComposition_AgentPack_ContributesTheAgentFamily`

Verifies that attaching the Agent pack to a host that declares the Delegation capability
contributes that family's tools, under the one family prefix the pack claims. Constructs a real
access policy, declares Delegation on a `ToolPackBuilder`, adds an `AgentPack` carrying no profiles
and a runner that answers immediately, and asserts the composed list is exactly `agent_run`.

### Composition: The Agent Family Is Withheld From a Non-Delegating Host

**Test**: `AgentKitTools_SystemComposition_AgentPackWithoutDelegation_ContributesNoTools`

Verifies that attaching the Agent pack to a host that declares no capability contributes no tools.
Confirms at the system level that an application that cannot start a second agent is never offered
a tool whose every use would fail.

### Cross-Family: A Delegated Agent Keeps Its Own Task List

**Test**: `AgentKitTools_SystemComposition_DelegatedAgent_KeepsItsOwnTaskList`

Verifies the one property that spans two families and therefore belongs at the system level: a
delegated agent's task list is its own. Composes a parent carrying both the Todo family and the
Agent family, registers a child profile admitting `todo_list` and `todo_set`, has the parent record
one step of its own, then delegates to a runner that writes two steps into the list it was handed
and reads that list back. Asserts on **both** stores — the parent's list holds exactly its own one
step and the child's holds exactly its own two.

This is a regression test rather than a feature test. Two separate ways of composing a child were
tried while the families were being spiked, and both routed a sub-agent's task list straight into
its parent's: capturing the parent's store while building the child's tools, and filtering a
parent-bound tool list at the `agent_run` call site. Both stores are asserted because a test that
checked only the parent would pass against a composition that gave the child no list at all, and a
test that checked only the child would pass against one where both wrote into the parent's.

## Acceptance Criteria

A system-level test run passes when the scenarios above pass without error or exception beyond
those explicitly asserted. Any unexpected exception, a non-empty tool list from an empty
composition, a family that does not contribute its tools when attached, a family contributed to a
host that did not declare the capability it requires, a delegated agent's writes appearing in its
parent's task list, or a failure to compose through the AgentKitCore pack contract constitutes a
failure.
