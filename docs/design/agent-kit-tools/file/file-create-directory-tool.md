### FileCreateDirectoryTool

![AgentKit Tools File Structure](FileView.svg)

The `FileCreateDirectoryTool` class publishes the `file_create_directory` tool.

#### Purpose

To create a directory at a path the access policy permits the agent to write, including every
directory above it that does not yet exist. Creating a place to write into is the operation that
makes the rest of the family usable for anything more than editing files that already exist.

Creating a directory changes the file system, so a location that is readable but not writable
cannot receive one. The write decision alone governs the tool; read access never implies it.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member            | Type     | Invariant                                                     |
| ----------------- | -------- | ------------------------------------------------------------- |
| `ToolName`        | `string` | `file_create_directory`; public constant; carries the prefix  |
| `ToolDescription` | `string` | Non-empty; states both outcomes and the parent creation       |
| Denial messages   | `string` | Tool-composed constants; policy denials come from policy      |
| Success messages  | `string` | Two constants; created and already-present are never the same |

The model supplies only `path`.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy`, then builds a guarded synchronous delegate with a defaulted
`path` parameter so an omitted argument reaches the tool body as a refusal.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(string? path = null)`

**Algorithm**, in this order:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`.
2. `policy.TryResolveWrite(path, …)` is called. A refusal is returned as `PathNotPermitted`.
3. An existing **file** at the path is refused as `InvalidRequest`. A file is never replaced by a
   directory.
4. Whether the directory already exists is recorded **before** the creation, because afterwards
   the two outcomes are indistinguishable.
5. The directory is created, along with every missing directory above it, and the tool returns the
   confirmation matching what step 4 observed.

The write decision happens before file-system observations, so a refused path discloses nothing
about whether anything exists outside permitted write locations.

**Why an existing directory is a success.** The family's no-silent-overwrite instinct is about
*destruction*, and creating a directory that is already there destroys nothing — so a refusal
would borrow a rule from a situation that does not apply. Meanwhile the composing use, "ensure
this path exists and then write into it", is the dominant one, and a refusal would force every
caller into a list-then-create dance that buys no safety. What does carry over from the family's
instinct is that the model must never be left guessing: the two outcomes carry two distinct texts,
so a caller can always tell which happened.

**Why missing parents are created rather than refused.** Every directory created lies inside the
same permitted location the policy already approved, so there is no containment question to
answer. Refusing because a middle component is absent would only force a model into a
create-one-level-at-a-time loop, spending turns to reach the same state.

**Why this unit carries no linked-ancestor guard, deliberately.** `file_delete_directory` and
`file_move_directory` refuse a path they are asked to reach *through* a link, because path
resolution is lexical and a tree reached that way lies outside every grant. This unit is not given
that rule, and the omission is a decision rather than an oversight: creating a directory through a
link writes outside the grant but **destroys nothing**. The narrowing those two tools apply is
justified by a blast radius of a whole tree; here the worst outcome is an empty directory in an
unexpected place, which the lexical resolution the rest of the library uses already accepts. If
the library later decides the question for every tool, this unit follows that decision rather than
carrying a private one.

#### Error Handling

Everything a model controls produces a returned refusal. The only exception the unit raises is
`ArgumentNullException` for a null policy at construction.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the requested directory could not be created. The refusal carries no
exception text, which is developer-facing and can name a host path the policy never disclosed.

#### Dependencies

`PathPolicy` for the write decision, `ToolResult` for results, and `GuardedToolFactory` for
construction. From the Base Class Library it uses `Directory`, `File`, and directory-creation
exceptions. `AIFunction`, from `Microsoft.Extensions.AI.Abstractions`, is the constructed tool
type.

#### Callers

`FilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.
