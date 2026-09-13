### AgentPack

![AgentKit Tools Agent Structure](AgentView.svg)

The `AgentPack` class publishes the agent family as one pack under the `agent` prefix.

#### Purpose

To be the single public attachment point for policy-governed delegation. The pack holds the
profiles and the runner the application registered, records the packs a delegated agent may draw
its tools from, and — at composition — validates every profile's grants against the parent's
policy and constructs the child-composition seam `agent_run` invokes for each delegated run.

The pack is also what makes the family's construction discipline enforceable. Because
`AgentRunTool.Create` is `internal` and this is the pack all attachment goes through, there is no
way for an application to obtain a run tool that was not created here, from the policy the
composition supplied. Because the pack declares the capability the family requires, the
composition can withhold the family from a host that cannot delegate.

#### Data Model

The class is sealed and immutable after construction; it is safe for concurrent use.

| Member                    | Type                      | Invariant                                            |
|---------------------------|---------------------------|------------------------------------------------------|
| `FamilyPrefix` (constant) | `string`                  | `agent`; leads every tool name it creates            |
| `IToolPack.FamilyPrefix`  | `string`                  | Reports the constant above                           |
| `RequiredCapabilities`    | `HostCapabilities`        | `Delegation`; the family is gated on it              |
| `_profiles`               | `List<AgentProfile>`      | Immutable snapshot; no duplicate name                |
| `_childPacks`             | `List<IToolPack>`         | Packs, not tools; no `AgentPack`; unique prefixes    |
| `_runner`                 | Host runner delegate      | The host's means of starting an agent                |
| `_hostCapabilities`       | `HostCapabilities`        | Applied when composing a child                       |
| `_depth`                  | `int`                     | Depth of the agent this pack is composed for         |
| `CreateTools(PathPolicy)` | `IEnumerable<AIFunction>` | Non-null; no null element; exactly the run tool      |

The prefix is published both as a constant and through the contract. The constant lets a test or
a composing application name the family without repeating a string literal, and the contract
member is what `ToolPackBuilder` reads. They cannot disagree, because the contract member returns
the constant. The contract member is implemented explicitly so the constant can keep the name the
contract uses; the class is sealed, so the member is not hidden from a derived type.

##### The child-packs collection is packs, not tools

`_childPacks` takes `IToolPack` instances rather than `AIFunction` instances, and that is the
isolation boundary. A pack can be asked to create a fresh set of tools against a different
policy, which is what gives every delegated agent its own per-composition state. There is no
overload taking `AIFunction` instances, because the only tool list a caller could pass is the
parent's, and the only thing this family could do with it is filter it — which is how a
sub-agent's twelve-item checklist ends up replacing its parent's sixty-eight-item list.

The pack must not appear in its own `_childPacks`. A delegated agent is given the family
automatically, one level deeper, whenever its profile admits `agent_run`; listing it as well
would publish the `agent` prefix twice.

#### Key Methods

##### AgentPack(profiles, runner, childPacks, hostCapabilities)

The public constructor. Registers the profiles and packs the application supplies, at the root
depth of zero.

**Preconditions:** `profiles`, `runner` and `childPacks` are non-null; `profiles` contains no
null entry and no duplicate name; `childPacks` contains no null entry, no `AgentPack`, and no two
packs claiming one family prefix. `hostCapabilities` defaults to `HostCapabilities.Delegation` so
a child whose profile admits `agent_run` can delegate in turn; a host whose children may also
see, for example, passes `HostCapabilities.Delegation | HostCapabilities.Vision`.

**Algorithm:** delegates to the private depth-aware constructor with `depth: 0`.

**Postconditions:** the pack is immutable, ready to be added to a `ToolPackBuilder`, and can be
added any number of times.

##### private AgentPack(..., int depth)

The private depth-aware constructor. Called only by `ComposeChild`, one level deeper than the
composing pack. Depth is private because a public depth parameter would let a composing
application reset the ceiling by starting a nested agent at zero, which is exactly the budget the
family exists to keep.

**Algorithm:** validates the arguments, materializes `profiles` and `childPacks` into internal
lists — a lazily evaluated sequence could yield a different set each time it was enumerated,
which would make a child's composition unrepeatable — validates the materialized collections, and
captures `runner`, `hostCapabilities` and `depth`.

##### RequiredCapabilities

Reports `HostCapabilities.Delegation`. The family starts a second agent, which only the
application can do, so the composition withholds the family from a host that has not declared it
can delegate. Because the builder gates before it asks a pack for its tools, a non-delegating
host never even causes `CreateTools` to run — the family is withheld rather than offered and
refused.

##### CreateTools(PathPolicy policy)

Creates the family's tools, governed by the supplied access policy.

**Preconditions:** `policy` is non-null; it is the policy the composition was built with.

**Algorithm:** validates `policy`, iterates the registered profiles and runs `ValidateNarrowing`
against each so a widening profile fails at composition rather than at run time, then returns
`AgentRunTool.Create` of `policy`, the profile list, the runner, a `ChildToolComposer` closure
over `this` and the policy, and `_depth`.

**Postconditions:** exactly the run tool, non-null, named `agent_`-prefixed and governed by the
supplied policy. Called once per `ToolPackBuilder.Build`, and only for a host that provides the
Delegation capability.

##### private ComposeChild(PathPolicy policy, AgentProfile profile)

The `ChildToolComposer` implementation; the whole isolation mechanism.

**Algorithm:** builds the child's policy from `BuildChildPolicy`, constructs a fresh
`ToolPackBuilder` on that policy under `_hostCapabilities`, adds every pack in `_childPacks` to
it, adds a new `AgentPack` one depth deeper — carrying the same profiles, runner, child packs
and host capabilities, so a child whose profile admits `agent_run` can delegate in turn — calls
`Build`, and returns the composed tools filtered by the profile's declared names.

**Every tool here is new.** The packs are asked for tools again rather than reusing the parent's,
so each child gets its own per-composition state. The parent's tools are not consulted, filtered,
or in scope.

##### private BuildChildPolicy(PathPolicy policy, AgentProfile profile)

Returns the parent's policy unchanged when the profile states no grants, otherwise returns a new
`PathPolicy` carrying the parent's working directory and limits and the profile's grants. The
working directory and limits are inherited because a child that resolved relative paths
differently from its parent would misread every path the parent passed it in a task.

##### private ValidateNarrowing(PathPolicy policy, AgentProfile profile)

Enforces "grants may only narrow." Every grant the profile states must be covered by some grant
the parent holds. Being covered means three things at once: the location is one the parent can
reach, the access level is no higher than the parent's, and every deny pattern the covering
parent grant imposes is carried by the child's grant too. The third is easy to overlook and is
the one that matters most — a child grant over the same root with the parent's exclusions
dropped is a strictly wider grant wearing a narrower shape. The first widening grant is named,
because reporting them all would not help a developer who has one profile to fix.

##### private Covers(PathRule held, PathRule wanted)

Determines whether one grant is wholly contained by another. A write where the parent holds only
a read is a widening whatever the location; an unrestricted child grant is only covered by an
unrestricted parent grant, so a rooted parent grant can never contain "everywhere"; every deny
pattern the parent imposes must survive in the child grant.

#### Error Handling

`ArgumentNullException` is raised for a null `profiles`, `runner` or `childPacks`.
`ArgumentException` is raised for a null profile, a duplicate profile name, a null child pack,
an `AgentPack` listed as a child pack, or two child packs sharing a family prefix. Each is a
programming error in the composing application, reported at the line that made it.

`ArgumentNullException` is also raised in `CreateTools` for a null policy — the same failure the
run tool's factory would raise, checked here as well so the failure names the composing
application's mistake at the line that made it.

`InvalidOperationException` is raised in `CreateTools` when a registered profile's grants would
widen the parent's policy. Its message names the profile so the developer knows which one to
fix.

The pack is not reachable from a model's tool call, so no runtime refusal arises here.

#### Dependencies

`IToolPack`, `HostCapabilities`, `PathPolicy`, `PathRule`, `AccessLevel` and `ToolPackBuilder`
from AgentKitCore. `AgentProfile`, `ChildAgentRequest`, `AgentRunTool` and `ChildToolComposer`
from this subsystem. `AIFunction` from `Microsoft.Extensions.AI.Abstractions`, as the type the
created tools take.

#### Callers

An application constructs an `AgentPack` and adds it to a `ToolPackBuilder`; the builder then
calls `CreateTools`, but only for a host that declared the Delegation capability. `ComposeChild`
calls the private depth-aware constructor when a child is composed. Nothing else in this package
calls the unit.
