### FileMoveTool

![AgentKit Tools File Structure](FileView.svg)

The `FileMoveTool` class publishes the `file_move` tool.

#### Purpose

To move one source file from a path the access policy permits the agent to write to one destination
path the policy also permits the agent to write. A move changes both endpoints: it removes the
source and creates or replaces the destination.

The tool refuses an existing destination unless `overwrite` is `true`, refuses directories, and
creates no missing parent directory.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member            | Type     | Invariant                                                   |
| ----------------- | -------- | ----------------------------------------------------------- |
| `ToolName`        | `string` | `file_move`; public constant; carries the family prefix     |
| `ToolDescription` | `string` | Non-empty; names writable source, destination and overwrite |
| Denial messages   | `string` | Tool-composed constants; policy denials come from policy    |

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
2. The source is resolved with `policy.TryResolveWrite`.
3. The destination is resolved with `policy.TryResolveWrite`.
4. A directory source is refused; the tool moves a single file only.
5. A missing source file is refused as `TargetNotFound` with a plain statement of the fact, naming no
   other tool.
6. A destination directory is refused.
7. An existing destination file is refused unless `overwrite` is `true`.
8. A missing destination parent is refused; the tool creates no directory.
9. `File.Move` performs the move and the tool returns a confirmation.

Both write decisions happen before file-system observations, so a refused endpoint discloses nothing
about existence outside permitted write locations.

#### Error Handling

Everything a model controls produces a returned refusal. The only exception the unit raises is
`ArgumentNullException` for a null policy at construction.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the file could not be moved.

**The source requires write permission.** Moving a file takes it away from its original location, so a
read-only grant permits copying but not moving. That keeps read-only locations genuinely read-only.

#### Dependencies

`PathPolicy` for both endpoint write decisions, `ToolResult` for results, and `GuardedToolFactory`
for construction. From the Base Class Library it uses
`Directory`, `File`, `Path`, and file-move exceptions. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the constructed tool type.

#### Callers

`FilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.
