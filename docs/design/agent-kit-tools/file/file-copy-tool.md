### FileCopyTool

![AgentKit Tools File Structure](FileView.svg)

The `FileCopyTool` class publishes the `file_copy` tool.

#### Purpose

To copy one source file the access policy permits the agent to read to one destination path the
policy permits the agent to write. It copies the file as an entity, regardless of content type.

The source is not changed. The destination is refused when it already exists unless the caller sets
`overwrite` to `true`, making replacement an explicit act rather than an accidental side effect.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member            | Type     | Invariant                                                |
| ----------------- | -------- | -------------------------------------------------------- |
| `ToolName`        | `string` | `file_copy`; public constant; carries the prefix         |
| `ToolDescription` | `string` | Non-empty; names source, destination and overwrite       |
| Denial messages   | `string` | Tool-composed constants; policy denials come from policy |

The model supplies `source`, `destination`, and optional `overwrite`. `overwrite` defaults to
`false`.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy`, then builds a guarded synchronous delegate with defaulted
`source`, `destination`, and `overwrite` parameters.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(string? source = null, string? destination = null, bool overwrite = false)`

**Algorithm**, in this order:

1. A missing source or destination is refused as `InvalidRequest`.
2. The source is resolved with `policy.TryResolveRead`.
3. The destination is resolved with `policy.TryResolveWrite`.
4. A directory source is refused; the tool copies a single file only.
5. A missing source file is refused as `TargetNotFound` with a plain statement of the fact, naming no
   other tool.
6. A destination directory is refused.
7. An existing destination file is refused unless `overwrite` is `true`.
8. A missing destination parent is refused; the tool creates no directory.
9. `File.Copy` performs the copy and the tool returns a confirmation.

Both policy decisions happen before file-system observations, so a refused source or destination
discloses nothing about existence outside permitted locations.

#### Error Handling

Everything a model controls produces a returned refusal. The only exception the unit raises is
`ArgumentNullException` for a null policy at construction.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the file could not be copied.

**Overwrite is opt-in.** The default path is refusal, and the refusal names the `overwrite` flag so a
model can retry only when replacement was intended.

#### Dependencies

`PathPolicy` for read and write endpoint decisions, `ToolResult` for results, and
`GuardedToolFactory` for construction. From the
Base Class Library it uses `Directory`, `File`, `Path`, and file-copy exceptions. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the constructed tool type.

#### Callers

`FilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.
