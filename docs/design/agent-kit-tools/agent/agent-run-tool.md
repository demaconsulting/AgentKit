### AgentRunTool

![AgentKit Tools Agent Structure](AgentView.svg)

The `AgentRunTool` class publishes the `agent_run` tool and defines the internal
`ChildToolComposer` seam every child's tools are built through.

#### Purpose

To start one of the application's registered agents on a task and return what it said, choosing
among profiles the model can see by name and description in the tool description, refusing every
malformed or unsatisfiable call as a returned result, and never composing a parent's tool list
into a child's request.

The unit exists so the model's authority is exactly one word: the *name* of an agent. It does not
supply the child's instructions, its tools or its grants. Refusing a call the tool cannot honor
returns a result naming the reason rather than throwing, because an exception in a tool call ends
the agent's turn and strands it.

#### Data Model

The class is static and holds no state. A constructed tool captures five values, all supplied at
construction and none of them mutable afterwards: the parent composition's `PathPolicy`, the
registered `AgentProfile` list, the host's runner, the `ChildToolComposer` seam, and the depth of
the agent this tool is being built for.

| Member                    | Type      | Invariant                                              |
|---------------------------|-----------|--------------------------------------------------------|
| `ToolName`                | `string`  | `agent_run`; public constant; carries the family prefix|
| Tool description prefix   | `string`  | Constant; explains the shape of a call                 |
| Constructed description   | `string`  | Names each registered profile and any description      |
| `ChildToolComposer`       | delegate  | `internal`; the only route to a child's tools          |
| Refusal messages          | `string`  | Constants or interpolations of a name, ceiling or level|

##### `internal delegate IReadOnlyList<AIFunction> ChildToolComposer(AgentProfile profile)`

The child-composition seam. It is declared `internal` because it is the whole isolation
mechanism: no public API accepts a tool list for a child, so there is no expression in which a
caller hands the parent's tools down to be filtered. Its only implementation, in
`AgentPack.CreateTools`, composes the registered packs afresh against the child's own policy.
Invoking it once per `agent_run` call — rather than once at construction — is what gives two
sibling children different per-composition state.

#### Key Methods

##### Create(policy, profiles, runner, composer, depth)

Creates the tool. `internal` because the pack is the unit of attachment and because two of these
parameters — the composer and the depth — are the family's isolation and budget controls rather
than things a caller should be choosing. An application obtains this tool by attaching
`AgentPack`.

**Preconditions:** `policy`, `profiles`, `runner` and `composer` are non-null; `depth` is
non-negative.

**Algorithm:** validates each argument, builds a delegate that awaits `RunAsync` with those
values captured, and returns `GuardedToolFactory.Create` of that delegate under `ToolName` with a
description composed from the profile list.

**Postconditions:** the returned tool carries `ToolName`, a non-empty description that names each
registered profile, and the five captured values for the rest of its life.

##### The tool delegate: `(string? profile, string? task, CancellationToken cancellationToken)`

**Algorithm**, in this order, because the order is itself the contract:

1. An absent, empty or whitespace `profile` is refused as `InvalidRequest`, listing the profiles
   that exist. **The parameter carries a default**, so an omitted argument is refused by the tool
   rather than by the function factory, and the model sees a refusal it can act on rather than an
   opaque framework error.
2. An absent, empty or whitespace `task` is refused as `InvalidRequest`, stating that a task is
   required. Same rationale.
3. The profile named is looked up by ordinal comparison. A miss is refused as `TargetNotFound`,
   naming the available profiles as a statement of fact about the tool's state.
4. The child's depth is computed as `depth + 1` and compared against `policy.Limits.MaxAgentDepth`.
   An overrun is refused as `InvalidRequest`, stating the ceiling and the current level. **This
   check happens before composition**, so a refused run costs nothing to reach and nothing is
   built for it.
5. The child's tools are composed by calling the `ChildToolComposer` seam with the selected
   profile. The seam is invoked with the profile alone; no tool list is passed in, and no
   filtering of the tool's own captured state occurs.
6. A `ChildAgentRequest` is constructed from the profile's name and instructions, the composed
   tools, the task, and the child's depth, and handed to the host's runner alongside the
   cancellation token.
7. The runner's result is turned into a returned result: a `null` or empty answer is reported as
   an agent that finished without reporting anything, an answer longer than
   `policy.Limits.MaxResultCharacters` is refused as `ResourceTooLarge` naming the ceiling, and
   any other answer is returned as text.

##### DescribeTool(IReadOnlyList<AgentProfile> profiles)

Composes the tool description a model reads while choosing. Every registered profile's name is
included; a profile's description is included where it has one, in parentheses beside the name.
An empty registration produces a description that states none are registered, so a model that
sees the tool but has nothing to delegate to reads the fact rather than inventing a profile name.

#### Error Handling

Everything a model controls produces a returned refusal, never an exception: `InvalidRequest` for
a malformed request, `TargetNotFound` for an unknown profile, `InvalidRequest` for a depth
overrun, and `ResourceTooLarge` for an oversized answer. Each refusal states a fact and
prescribes no other tool, on the same basis as the text family's empty-buffer refusal.

The only exceptions this unit raises are at construction: `ArgumentNullException` for a missing
policy, profiles collection, runner or composer, and `ArgumentOutOfRangeException` for a negative
depth. Each is a defect in this library rather than something a composing application did, so it
is reported at the line that made the mistake.

A runner exception is deliberately allowed to propagate. The library cannot tell a transient
outage from a misconfiguration, and reporting either as a refusal would tell the model something
this library does not know.

#### Dependencies

`PathPolicy`, `ToolLimits`, `ToolResult`, `DenialReason` and `GuardedToolFactory` from
AgentKitCore, for the policy, ceilings and returned results. `AgentProfile` and
`ChildAgentRequest` from this subsystem. `AIFunction` from
`Microsoft.Extensions.AI.Abstractions`, as the type the constructed tool takes.

#### Callers

`AgentPack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by
the agent runtime an application composed it into. The `ChildToolComposer` delegate is
implemented in the closure `AgentPack.CreateTools` constructs; nothing else in the package
implements it.
