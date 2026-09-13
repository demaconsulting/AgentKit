### TextFilePack

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFilePack` class publishes the text-file tool family under the `text_file` prefix.

#### Purpose

To be the single public attachment point for policy-governed text content tools. The pack claims the
`text_file` prefix, publishes the seven tools in fixed order, and creates the one cut/paste buffer
shared by the line-range tools in that composition.

The pack itself grants nothing. It receives the `PathPolicy` from the `ToolPackBuilder` composition
and passes that same policy to every tool factory.

#### Data Model

The class is sealed and stateless. The buffer it allocates is local to each `CreateTools` call, not a
field on the pack.

| Member                   | Type               | Invariant                                          |
| ------------------------ | ------------------ | -------------------------------------------------- |
| `FamilyPrefix`           | `string`           | `text_file`; every published tool name starts here |
| `IToolPack.FamilyPrefix` | `string`           | Explicit implementation returning `FamilyPrefix`   |
| `RequiredCapabilities`   | `HostCapabilities` | `None`; the family is available to every host      |

The returned tool collection contains, in order, `TextFileSearchTool`, `TextFileReadTool`,
`TextFileCreateTool`, `TextFileReplaceTool`, `TextFileCutLinesTool`, `TextFileCopyLinesTool`, and
`TextFilePasteLinesTool`.

#### Key Methods

##### TextFilePack()

Constructs a stateless pack.

**Preconditions:** none.

**Algorithm:** no work; the policy and buffer are supplied later by `CreateTools`.

**Postconditions:** the pack can be added to a `ToolPackBuilder` any number of times.

##### CreateTools(PathPolicy policy)

Creates the family's tools.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy`, allocates a new `TextFileLineBuffers`, and returns the seven tools
in fixed order. Search, read, create and replace receive only the policy. Cut, copy and paste receive
the same policy and the same buffer instance.

**Postconditions:** all returned tools carry the `text_file` prefix, observe the same policy, and the
cut, copy and paste tools share one buffer with the same lifetime as the returned tools.

#### Error Handling

A null policy is a composing-application error and raises `ArgumentNullException`. Model-facing
errors are handled by the individual tools after composition.

The pack requires `HostCapabilities.None`, so it is not withheld by host capability checks and does
not issue capability-related refusals.

#### Dependencies

`IToolPack`, `HostCapabilities`, `PathPolicy`, and `ToolPackBuilder` conventions from AgentKitCore;
`AIFunction` from `Microsoft.Extensions.AI.Abstractions`; and all seven text-file tool units. It also
allocates `TextFileLineBuffers` as internal shared state for cut, copy and paste.

#### Callers

Applications do not normally call `CreateTools` directly; `ToolPackBuilder` calls it when a
composition adds `TextFilePack`. The returned tools are invoked by the agent runtime.
