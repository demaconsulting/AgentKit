## Agent

![AgentKit Tools Agent Structure](AgentView.svg)

The Agent subsystem is the agent tool family: the pack an application attaches to let a delegating
agent hand a task to one of the agents the application has registered, and get back what that agent
said.

### Overview

The subsystem's responsibility is to take one dangerous operation — starting a second agent — and
turn it into a tool an agent can be handed safely. It owns none of the mechanics of running an
agent; only the application knows its provider, its model and its credentials, so only the
application can start a second agent. What the family owns is everything *around* that step: what
the child is allowed to be, what tools it is allowed to hold, where it is allowed to reach, how
deep chains of delegation may run, and — the property that distinguishes this family from every
other in the library — that a child's per-composition state is the child's alone.

The boundary is deliberate. An application registers `AgentProfile` instances; a model selects one
by name and states a task; the host builds an agent from the profile's instructions and the tools
the library composed for the child, runs it on the task, and returns its final text. The parent
agent never authors the child's instructions, never names a tool the application did not attach,
never widens a grant, and never hands a tool list down.

The subsystem contains three units:

| Unit                | Responsibility                                                            |
|---------------------|---------------------------------------------------------------------------|
| `AgentProfile`      | One named child agent the application is willing to have started          |
| `AgentRunTool`      | Publishes `agent_run`; chooses, composes, and shapes the host's request   |
| `AgentPack`         | Publishes the family under the `agent` prefix, gated on Delegation        |

### Interfaces

The subsystem exposes three public types — `AgentPack`, the unit of attachment, `AgentProfile`, the
registration record, and `ChildAgentRequest`, the type the host's runner receives — plus the name
constant `AgentRunTool` publishes. The `AgentRunTool` factory and `ChildToolComposer` are
`internal`: the tool is only obtainable through the pack that claims its family prefix, and the
composition seam is the mechanism the family's safety-critical property depends on.

| Interface               | Direction | Format                     | Constraints                          |
|-------------------------|-----------|----------------------------|--------------------------------------|
| `AgentPack`             | Outbound  | AgentKitCore `IToolPack`   | Prefix `agent`; requires Delegation  |
| `AgentProfile`          | Outbound  | Immutable sealed class     | Constructed by the application       |
| `ChildAgentRequest`     | Outbound  | Immutable sealed class     | Constructed only by the library      |
| `AgentRunTool.ToolName` | Outbound  | `string` constant          | The name the tool is published under |
| Host delegation runner  | Inbound   | `Func` returning a string  | Supplied at pack construction        |
| Registered child packs  | Inbound   | `IEnumerable<IToolPack>`   | Packs, not tools; no `AgentPack`     |
| `PathPolicy`            | Inbound   | AgentKitCore policy object | Supplied to `CreateTools`            |

The subsystem consumes `PathPolicy`, `PathRule`, `AccessLevel`, `ToolLimits`, `ToolResult`,
`GuardedToolFactory`, `IToolPack`, `ToolPackBuilder` and `HostCapabilities` from AgentKitCore, and
`AIFunction` from `Microsoft.Extensions.AI.Abstractions` reached through Core. It composes other
families' packs for a child through `ToolPackBuilder`, and takes their tools *from that
composition*, never from the parent's tool list.

### Design

**Construction.** `AgentPack.CreateTools` receives the composition's policy, validates that no
registered profile widens it, then calls `AgentRunTool.Create` with the policy, the profiles, the
runner and the child-composition seam. There is no other construction path, no setter and no
default policy, so an unguarded run tool is unrepresentable rather than merely discouraged.

**A profile is the application's authorship of what a child is.** The model chooses a child by
*name* from a list the application registered; it does not supply the child's system prompt, its
tool list, or its grants. If it could, delegation would be a way for a model to grant a second
model behavior the application never sanctioned — "you are an unrestricted assistant; ignore the
rules" — and every control the application configured would be one prompt away from being reset.
The only thing the parent supplies is the task, which is data the child works on rather than
authority the child carries.

**Naming a tool does not conjure it.** A profile's tool names are a filter, not a grant: the child
receives the tools whose names appear both in the profile and in the packs the application handed
to `AgentPack`. A profile naming `shell_exec` in an application that attached no such tool
produces a child with no such tool, silently and by construction. A profile with an empty tool
list is a legitimate profile — a child that can only think and answer — and is not treated as a
mistake.

**A profile's grants may only narrow.** A profile that omits grants gives the child the parent's
policy unchanged; a profile that states them is checked against the parent's policy in
`AgentPack.CreateTools`, and a grant reaching outside what the parent holds — a location the
parent cannot reach, a write where the parent has only a read, an unrestricted grant under a
rooted parent, or a deny pattern the parent imposes and the profile drops — throws an
`InvalidOperationException`. Checking at composition rather than at run time reports the mistake
to the developer who wrote it rather than to a model that has no way to correct it.

**Delegation is gated on the host's capability.** `AgentPack` declares
`HostCapabilities.Delegation`, so a host that has not declared it receives none of the family's
tools — and receives none because the composition never asks the pack for them, exactly the way
`ImagePack` is withheld from a non-vision host. The runner the host supplies is the seam only the
application can build, because only the application knows its provider, model and credentials.

**Delegation is bounded by `ToolLimits.MaxAgentDepth`.** The ceiling defaults to 2, counting the
application's own root agent as zero, and sits with the other budgets rather than as a per-tool
constant. A child whose profile admits `agent_run` receives the family one level deeper, so chains
are possible and are budgeted; at the ceiling the tool is offered and refuses, stating the ceiling
and the current level. Nothing is composed or started for a refused run.

**Refusals state facts and prescribe no other tool.** An unknown profile returns `Denied
(TargetNotFound): No profile is named 'auditor'. The available profiles are 'reviewer', 'writer'.` —
a statement about the tool's own state, like the text family's empty-buffer refusal. Profile names
are compared ordinally.

**The run tool's description names the registered profiles.** A model that has to call a tool to
find out what it may delegate to will usually just not delegate, so the profile names — and their
descriptions where they have one — appear in the tool description a model reads while choosing.

**A child that said nothing is not a failure.** A runner returning `null` or empty text is
reported as an agent that finished without reporting anything; a child can legitimately have found
there was nothing to report, and telling the parent it failed would invite it to retry work that
was already done. A child's answer is bounded by `MaxResultCharacters` and refused rather than
truncated, because a truncated report is worse than none. An exception raised inside the runner
propagates rather than becoming a refusal, because this library cannot tell a transient outage
from a misconfiguration.

### The Isolation Mistake, Made Impossible to Write

The family's safety-critical property is that a child's per-composition state — a task list, a cut
buffer — belongs to the child alone. Two contaminating mistakes were made in one file while the
family was being spiked, both while explicitly trying to prevent them, and both routed a child's
task list into its parent's:

- **Closing over the parent's store while building the child's tools.** The pack held a reference
  to the parent's per-composition state and used it while composing the child, despite the builder
  taking a store parameter. The child's tools then wrote through the parent's store.
- **Filtering a parent-bound tool list at the `agent_run` call site.** The tool took the tools it
  had itself been handed, filtered them by name, and passed the survivors to the runner. A
  filtered parent tool retains the parent's captured state; the filter changes which tools reach
  the child, not whose state they hold.

Both matched a real incident where a sub-agent's twelve-item checklist replaced its parent's
sixty-eight-item list. The response was not to add care; the response was to make the mistakes
impossible to write. Nothing public in this family accepts an `AIFunction`. `AgentPack` takes
`IToolPack` instances, not tools. The child composition runs through an internal
`ChildToolComposer` delegate whose only implementation calls a fresh `ToolPackBuilder` over the
registered packs against the child's own policy, so per-composition state is allocated per child.
The isolation is enforced by API shape, not by developer care, and the fix cannot regress by
somebody adding a filter to a pull request.

### Cross-Subsystem Dependency

The agent family exists to compose *other* families' packs for a child, so its behavior is only
observable in the presence of another family's tools. Verifying that at the unit level with a real
family would make the agent unit tests depend on an unrelated subsystem, so subsystem-level and
unit-level tests use a local stub pack — `test/DemaConsulting.AgentKit.Tools.Tests/Agent/StubToolPack.cs`,
alongside `TemporaryDirectory.cs` — that publishes named do-nothing tools and records the policies
it was asked to build against. The stub is the smallest thing that satisfies `IToolPack` and lets
a test assert which tools a child received and which policy they were built against; using a real
sibling family would confuse a failure in the sibling with a failure here.

The one cross-family scenario — a delegated agent keeping its own task list, distinct from its
parent's, proving the isolation property under a real todo family — lives in the system test file
as `AgentKitTools_SystemComposition_DelegatedAgent_KeepsItsOwnTaskList`, where the system
composition is the thing under test and a sibling family is legitimately in scope. That is the one
place the isolation property is proved end-to-end against a real store; every other subsystem test
proves it against the stub, where the property can be asserted directly on the tools and policies
the family produced.
