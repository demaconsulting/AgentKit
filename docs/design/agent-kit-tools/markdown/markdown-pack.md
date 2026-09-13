### MarkdownPack

![AgentKit Tools Markdown Structure](MarkdownView.svg)

The `MarkdownPack` class publishes the markdown tool family under the `markdown` prefix.

#### Purpose

To be the single public attachment point for policy-governed Markdown outline tools. The pack claims
the `markdown` prefix and publishes the single `markdown_outline` tool.

The pack itself grants nothing. It receives the `PathPolicy` from the `ToolPackBuilder` composition
and passes that same policy to the outline tool factory.

#### Data Model

The class is sealed and stateless.

| Member                   | Type               | Invariant                                         |
| ------------------------ | ------------------ | ------------------------------------------------- |
| `FamilyPrefix`           | `string`           | `markdown`; every published tool name starts here |
| `IToolPack.FamilyPrefix` | `string`           | Explicit implementation returning `FamilyPrefix`  |
| `RequiredCapabilities`   | `HostCapabilities` | `None`; the family is available to every host     |

The returned tool collection contains a single tool, `MarkdownOutlineTool`.

#### Key Methods

##### MarkdownPack()

Constructs a stateless pack.

**Preconditions:** none.

**Algorithm:** no work; the policy is supplied later by `CreateTools`.

**Postconditions:** the pack can be added to a `ToolPackBuilder` any number of times.

##### CreateTools(PathPolicy policy)

Creates the family's tools.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy` and returns `MarkdownOutlineTool.Create(policy)` as the only tool.
The collection form is still used because the pack contract is a collection and because the family
can grow without changing callers.

**Postconditions:** the returned tool carries the `markdown` prefix and observes the supplied policy.

#### Error Handling

A null policy is a composing-application error and raises `ArgumentNullException`. Model-facing
errors are handled by `MarkdownOutlineTool` after composition.

The pack requires `HostCapabilities.None`, so it is not withheld by host capability checks and does
not issue capability-related refusals.

#### Dependencies

`IToolPack`, `HostCapabilities`, `PathPolicy`, and `ToolPackBuilder` conventions from AgentKitCore;
`AIFunction` from `Microsoft.Extensions.AI.Abstractions`; and `MarkdownOutlineTool`.

#### Callers

Applications do not normally call `CreateTools` directly; `ToolPackBuilder` calls it when a
composition adds `MarkdownPack`. The returned outline tool is invoked by the agent runtime.
