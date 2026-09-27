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
| `ToolDescription` | `string` | Non-empty; states both outcomes and the parent boundary       |
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
3. Every directory **above** the target that would have to be created is judged by the same write
   decision. The walk climbs from the target's parent and stops at the first ancestor that already
   exists; each missing one must be permitted by a read-write grant, or the request is refused as
   `PathNotPermitted` and **nothing at any level is created**.
4. An existing **file** at the path is refused as `InvalidRequest`. A file is never replaced by a
   directory.
5. Whether the directory already exists is recorded **before** the creation, because afterwards
   the two outcomes are indistinguishable.
6. The directory is created, along with every missing directory above it, and the tool returns the
   confirmation matching what step 5 observed.

The write decision happens before file-system observations, so a refused path discloses nothing
about whether anything exists outside permitted write locations.

**Why an existing directory is a success.** The family's no-silent-overwrite instinct is about
*destruction*, and creating a directory that is already there destroys nothing — so a refusal
would borrow a rule from a situation that does not apply. Meanwhile the composing use, "ensure
this path exists and then write into it", is the dominant one, and a refusal would force every
caller into a list-then-create dance that buys no safety. What does carry over from the family's
instinct is that the model must never be left guessing: the two outcomes carry two distinct texts,
so a caller can always tell which happened.

**Why missing parents are created rather than refused.** Refusing because a middle component is
absent would only force a model into a create-one-level-at-a-time loop, spending turns to reach the
same state. The condition that makes this safe is that every directory created lies inside a
location the policy permits writing — and that is checked, not assumed; see below.

**Why every ancestor is judged, not just the named path.** `Directory.CreateDirectory` materializes
*every* missing component of the path it is given, so the writes one call performs are the named
directory plus each absent directory above it. Only the named one passed through
`TryResolveWrite`. A grant root need not exist — `RealPathResolver` is lexical and requires no
component to be present — so a grant rooted at `workspace/new-root` authorized `new-root/child`
while the same call created `workspace/new-root` and, if they were missing, its own parents too:
writes in a location no grant covers. Nothing is destroyed by that, which is why it was a
containment gap rather than a destructive one; but containment is the library's central promise,
and a tool that can create directories the operator never granted is expressing an operation the
policy was supposed to make inexpressible. Each missing ancestor is therefore tested against the
read-write grants directly — the ancestors are already resolved real locations, so re-resolving
them through `TryResolveWrite` would re-interpret paths the tool produced rather than paths a model
supplied.

*The alternative — creating the ancestors at or below the grant root and refusing the rest — was
rejected because it collapses into the same behavior.* A directory cannot be created inside a
parent that does not exist, so that rule still has to refuse the moment an ancestor above the grant
root is missing; the only difference is that it refuses after leaving a half-built tree behind.
Refusing up front says the same thing with one decision and keeps this unit's promise identical to
the rest of the family's: a refusal means nothing happened. The refusal names no path, because the
offending directory lies above every permitted location and its name is a host location the policy
never disclosed; the tool's description states the boundary instead, so a model is not invited into
a request whose only possible outcome is a refusal it could not have predicted. **A grant root that
does not yet exist is still created**, because the root is itself a permitted location — the rule
bites on ancestors the grants do not reach, not on missing ancestors as such.

**Why this unit carries no linked-ancestor guard, deliberately.** `file_delete_directory` and
`file_move_directory` refuse a path they are asked to reach *through* a link, because path
resolution is lexical and a tree reached that way lies outside every grant. This unit is not given
that rule, and the omission is a decision rather than an oversight: creating a directory through a
link writes outside the grant but **destroys nothing**. The narrowing those two tools apply is
justified by a blast radius of a whole tree; here the worst outcome is an empty directory in an
unexpected place, which the lexical resolution the rest of the library uses already accepts. If
the library later decides the question for every tool, this unit follows that decision rather than
carrying a private one. The ancestor check above is a different question and is *not* a link rule:
it asks whether the policy permits a location, not what the location leads to, and so it applies to
a lexical path exactly as the write decision does.

#### Error Handling

Everything a model controls produces a returned refusal. The only exception the unit raises is
`ArgumentNullException` for a null policy at construction.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the requested directory could not be created. The refusal carries no
exception text, which is developer-facing and can name a host path the policy never disclosed.

#### Dependencies

`PathPolicy` for the write decision and for the grants each would-be ancestor is judged against,
`PathRule.Allows` for judging an already-resolved ancestor, `ToolResult` for results, and
`GuardedToolFactory` for construction. From the Base Class Library it uses `Directory`, `File`,
`Path`, and directory-creation exceptions. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the constructed tool type.

#### Callers

`FilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.
