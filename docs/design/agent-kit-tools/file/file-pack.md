### FilePack

![AgentKit Tools File Structure](FileView.svg)

The `FilePack` class publishes the file tool family under the `file` prefix.

#### Purpose

To be the single public attachment point for policy-governed file-entity tools. The pack claims the
`file` prefix and publishes, in fixed order, those of its seven tools — list, copy, move and delete
a file, and create, move and delete a directory — that the policy could permit to succeed.

The pack itself grants nothing. It receives the `PathPolicy` from the `ToolPackBuilder` composition
and passes that same policy to every tool factory.

#### Data Model

The class is sealed and stateless.

| Member                   | Type               | Invariant                                        |
| ------------------------ | ------------------ | ------------------------------------------------ |
| `FamilyPrefix`           | `string`           | `file`; every published tool name starts here    |
| `IToolPack.FamilyPrefix` | `string`           | Explicit implementation returning `FamilyPrefix` |
| `RequiredCapabilities`   | `HostCapabilities` | `None`; the family is available to every host    |

The returned tool collection contains, in order, `FileListTool`, `FileCopyTool`, `FileMoveTool`,
`FileDeleteTool`, `FileCreateDirectoryTool`, `FileMoveDirectoryTool` and `FileDeleteDirectoryTool`
— the whole family of seven — when `policy.AnyLocationIsWritable` is true. When it
is false the collection contains only `FileListTool`: one tool rather than seven.

#### Key Methods

##### FilePack()

Constructs a stateless pack.

**Preconditions:** none.

**Algorithm:** no work; the policy is supplied later by `CreateTools`.

**Postconditions:** the pack can be added to a `ToolPackBuilder` any number of times.

##### CreateTools(PathPolicy policy)

Creates the family's tools.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy`, returns `FileListTool.Create(policy)`, then asks the policy one
question — `AnyLocationIsWritable` — and appends `FileCopyTool.Create(policy)`,
`FileMoveTool.Create(policy)`, `FileDeleteTool.Create(policy)`,
`FileCreateDirectoryTool.Create(policy)`, `FileMoveDirectoryTool.Create(policy)` and
`FileDeleteDirectoryTool.Create(policy)` in that fixed order only when the
answer is true.

**Postconditions:** all returned tools carry the `file` prefix and observe the same policy. When
the policy permits writing somewhere, all seven are returned; otherwise only the list tool is.

**Which tools are withheld, and why.** Copy, move, delete and all three directory tools each change
the file system, so under
a policy holding no read-write grant anywhere every one of them could only answer a refusal, and
none of them is published. List is never withheld: it consults the read decision to choose what to
report, and the write decision only to annotate a reported root as writable, so it stays fully
useful and leaves the agent able to discover what it may read. See *Policy-derived publication* in
the system design for the rule and the whole-family table.

#### Error Handling

A null policy is a composing-application error and raises `ArgumentNullException`. Model-facing
errors are handled by the individual tools after composition.

The pack requires `HostCapabilities.None`, so it is not withheld by host capability checks and does
not issue capability-related refusals.

#### Dependencies

`IToolPack`, `HostCapabilities`, `PathPolicy`, and `ToolPackBuilder` conventions from AgentKitCore;
`AIFunction` from `Microsoft.Extensions.AI.Abstractions`; and the seven file tool units.

#### Callers

Applications do not normally call `CreateTools` directly; `ToolPackBuilder` calls it when a
composition adds `FilePack`. The returned tools are invoked by the agent runtime.
