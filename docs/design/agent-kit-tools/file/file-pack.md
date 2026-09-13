### FilePack

![AgentKit Tools File Structure](FileView.svg)

The `FilePack` class publishes the file tool family under the `file` prefix.

#### Purpose

To be the single public attachment point for policy-governed file-entity tools. The pack claims the
`file` prefix and publishes four tools in fixed order: list, copy, move and delete.

The pack itself grants nothing. It receives the `PathPolicy` from the `ToolPackBuilder` composition
and passes that same policy to every tool factory.

#### Data Model

The class is sealed and stateless.

| Member                   | Type               | Invariant                                        |
| ------------------------ | ------------------ | ------------------------------------------------ |
| `FamilyPrefix`           | `string`           | `file`; every published tool name starts here    |
| `IToolPack.FamilyPrefix` | `string`           | Explicit implementation returning `FamilyPrefix` |
| `RequiredCapabilities`   | `HostCapabilities` | `None`; the family is available to every host    |

The returned tool collection contains, in order, `FileListTool`, `FileCopyTool`, `FileMoveTool`, and
`FileDeleteTool`.

#### Key Methods

##### FilePack()

Constructs a stateless pack.

**Preconditions:** none.

**Algorithm:** no work; the policy is supplied later by `CreateTools`.

**Postconditions:** the pack can be added to a `ToolPackBuilder` any number of times.

##### CreateTools(PathPolicy policy)

Creates the family's tools.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy` and returns `FileListTool.Create(policy)`,
`FileCopyTool.Create(policy)`, `FileMoveTool.Create(policy)`, and `FileDeleteTool.Create(policy)` in
that fixed order.

**Postconditions:** all returned tools carry the `file` prefix and observe the same policy.

#### Error Handling

A null policy is a composing-application error and raises `ArgumentNullException`. Model-facing
errors are handled by the individual tools after composition.

The pack requires `HostCapabilities.None`, so it is not withheld by host capability checks and does
not issue capability-related refusals.

#### Dependencies

`IToolPack`, `HostCapabilities`, `PathPolicy`, and `ToolPackBuilder` conventions from AgentKitCore;
`AIFunction` from `Microsoft.Extensions.AI.Abstractions`; and the four file tool units.

#### Callers

Applications do not normally call `CreateTools` directly; `ToolPackBuilder` calls it when a
composition adds `FilePack`. The returned tools are invoked by the agent runtime.
