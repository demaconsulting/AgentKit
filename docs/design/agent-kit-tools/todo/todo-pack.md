### TodoPack

![AgentKit Tools Todo Structure](TodoView.svg)

The `TodoPack` class publishes the todo tool family under the `todo` prefix.

#### Purpose

To be the single public attachment point for a task list an agent can write down, advance and
close out. The pack claims the `todo` prefix, publishes the three tools in fixed order, and
allocates the one `TodoStore` shared by that composition's tools.

The pack itself grants nothing. It receives the `PathPolicy` from the `ToolPackBuilder`
composition and accepts it even though the family never consults it, so that a composing
application that forgot a policy is told at the point it made the mistake rather than by a sibling
family later.

The pack also publishes `SuggestedInstruction`, the wording an application should append to its
agent's instructions when it attaches the family. Attaching the tools is not enough — the single
most important thing about this family is that reliable use requires the application to instruct
the agent. The measurements are recorded in the type-level remarks of `TodoPack` and in the
subsystem design document; the constant is published so the wording that ships cannot drift from
the wording that was measured.

#### Data Model

The class is sealed and stateless. The store it allocates is local to each `CreateTools` call, not
a field on the pack, because a store held on the pack would be shared between every composition
that used the same pack instance.

| Member                   | Type               | Invariant                                        |
| ------------------------ | ------------------ | ------------------------------------------------ |
| `FamilyPrefix`           | `string`           | `todo`; every published tool name starts here    |
| `IToolPack.FamilyPrefix` | `string`           | Explicit implementation returning `FamilyPrefix` |
| `RequiredCapabilities`   | `HostCapabilities` | `None`; the family is available to every host    |
| `SuggestedInstruction`   | `string`           | The instruction wording measured at 3-of-3 runs  |

The returned tool collection contains, in order, `TodoListTool`, `TodoSetTool` and
`TodoRemoveTool`.

#### Key Methods

##### TodoPack()

Constructs a stateless pack.

**Preconditions:** none.

**Algorithm:** no work; the policy and the store are supplied later by `CreateTools`.

**Postconditions:** the pack can be added to a `ToolPackBuilder` any number of times, and each
composition it appears in will receive a task list of its own.

##### CreateTools(PathPolicy policy)

Creates the family's tools.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy`, allocates a fresh `TodoStore` as a local, and returns
`TodoListTool.Create(store)`, `TodoSetTool.Create(store)` and `TodoRemoveTool.Create(store)` in
that order. The store is a local rather than a field or a parameter, so there is no way to express
handing one agent's list to another agent's tools. The policy is validated so that a null one is
named at the point it was made, and otherwise ignored: this family touches no files, so it has
nothing to judge against a policy.

**Postconditions:** the returned tools carry the `todo` prefix, share one `TodoStore` whose
lifetime equals theirs, and no other composition can reach that store.

#### Error Handling

A null policy is a composing-application error and raises `ArgumentNullException`. Model-facing
errors are handled by the individual tools after composition.

The pack requires `HostCapabilities.None`, so it is not withheld by host capability checks and
does not issue capability-related refusals.

#### Dependencies

`IToolPack`, `HostCapabilities`, `PathPolicy`, and `ToolPackBuilder` conventions from
AgentKitCore; `AIFunction` from `Microsoft.Extensions.AI.Abstractions`; and the four other units in
the family. It allocates `TodoStore` as internal shared state for its three tools.

#### Callers

Applications do not normally call `CreateTools` directly; `ToolPackBuilder` calls it when a
composition adds `TodoPack`. The returned tools are invoked by the agent runtime an application
composed them into.
