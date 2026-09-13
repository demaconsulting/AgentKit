### FileDeleteTool

![AgentKit Tools File Structure](FileView.svg)

The `FileDeleteTool` class publishes the `file_delete` tool.

#### Purpose

To delete one file at a path the access policy permits the agent to write. The tool deletes a single
file only: it never deletes a directory, never recurses, and does not keep a quarantine copy.
Recovery from a mistaken deletion is source control.

Deletion is a write operation in the strongest sense, so a file that is readable but not writable
cannot be deleted.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member            | Type     | Invariant                                                |
| ----------------- | -------- | -------------------------------------------------------- |
| `ToolName`        | `string` | `file_delete`; public constant; carries the prefix       |
| `ToolDescription` | `string` | Non-empty; states single-file, non-recursive deletion    |
| Denial messages   | `string` | Tool-composed constants; policy denials come from policy |

The model supplies only `path`. The file family prefix is part of the public name because a bare
`delete` collides with reserved framework naming.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy`, then builds a guarded synchronous delegate with a defaulted `path`
parameter so an omitted argument reaches the tool body as a refusal.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(string? path = null)`

**Algorithm**, in this order:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`.
2. `policy.TryResolveWrite(path, …)` is called. A refusal is returned as `PathNotPermitted`.
3. A directory is refused as `InvalidRequest`; the tool never recurses.
4. A missing file is refused as `TargetNotFound` with a plain statement of the fact, naming no other
   tool.
5. `File.Delete` removes the file and the tool returns a confirmation.

The write decision happens before file-system observations, so a refused path discloses nothing about
whether anything exists outside permitted write locations.

#### Error Handling

Everything a model controls produces a returned refusal. The only exception the unit raises is
`ArgumentNullException` for a null policy at construction.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the requested file could not be deleted.

**Missing files are refusals, not silent success.** A model that asked to delete a mistyped path must
learn the delete did not happen; the refusal states that plainly and names no other tool.

#### Dependencies

`PathPolicy` for the write decision, `ToolResult` for results, and `GuardedToolFactory` for
construction. From the Base Class Library it uses
`Directory`, `File`, and file-deletion exceptions. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the constructed tool type.

#### Callers

`FilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.
