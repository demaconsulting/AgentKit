### FileMoveDirectoryTool

![AgentKit Tools File Structure](FileView.svg)

The `FileMoveDirectoryTool` class publishes the `file_move_directory` tool.

#### Purpose

To move a directory, with everything beneath it, from a source the access policy permits the agent
to write to a destination it permits the agent to write. A destination in the same parent renames
the directory; there is no separate rename tool, because a rename is this same operation with a
destination that happens to share the source's parent.

Both endpoints are judged by the write decision, because a move takes the directory away from its
source and creates it at its destination. The source is judged first, so a tree an agent may read
but not write can never be taken away from where its operator put it.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member            | Type     | Invariant                                                     |
| ----------------- | -------- | ------------------------------------------------------------- |
| `ToolName`        | `string` | `file_move_directory`; public constant; carries the prefix    |
| `ToolDescription` | `string` | Non-empty; states the rename and the unconditional no-clobber |
| Denial messages   | `string` | Tool-composed constants; policy denials come from policy      |

The model supplies `source` and `destination`. **There is no `overwrite` parameter**; see below.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy`, then builds a guarded synchronous delegate with both parameters
defaulted so an omitted argument reaches the tool body as a refusal.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(string? source = null, string? destination = null)`

**Algorithm**, in this order:

1. An absent, empty or whitespace `source` or `destination` is refused as `InvalidRequest`.
2. `policy.TryResolveWrite(source, …)` is called, then `policy.TryResolveWrite(destination, …)`.
   Either refusal is returned as `PathNotPermitted`.
3. Each endpoint is then classified by the path it is reached *through*:
   `LinkGuard.FindLinkedAncestor` walks from the resolved endpoint up to the root of the deepest
   read-write grant that both contains and permits it, and a link
   on that path refuses the request as `PathNotPermitted`, naming the offending component as the
   model spelled it. The source is classified first, in the same order the write decisions were
   taken, and the two refusals are reported separately so the model knows which path to re-address.
4. A `source` naming a file is refused as `InvalidRequest`; this tool is not a second route to
   moving one file.
5. A missing `source` is refused as `TargetNotFound`.
6. A `destination` that is an existing file is refused as `InvalidRequest`; a `destination` that is
   an existing directory is refused as `InvalidRequest`. The two are reported separately because
   they are different mistakes.
7. A `destination` lying inside the `source` is refused as `InvalidRequest`.
8. A missing destination parent is refused as `TargetNotFound`, never created.
9. The directory is moved, and the tool returns a confirmation.

#### Design Decisions

**There is no overwrite flag, by decision.** The analogy to `file_move` does not hold.
`file_move`'s `overwrite` replaces one named file with one named file. A directory overwrite is a
**recursive destruction of an unbounded tree**, performed under a verb the model reads as "move".
That would smuggle the single most dangerous operation in the family past both of the controls
built for it — the entry ceiling and the link guard — neither of which a move applies. The
capability is not lost, only made explicit: `file_delete_directory` removes the destination first,
bounded and reported, and the model then moves into the space. Two calls, each of which says what
it does.

**Neither endpoint may be reached through a link inside the grant.** `RealPathResolver` resolves
lexically, so `workspace/junction/sub` satisfies the write decision whenever `workspace/junction` is
spelled inside the grant, however far outside the grant the junction actually leads. Moving a tree
*out of* a location reached that way takes content from where nothing was granted; moving a tree
*into* one places a whole tree there. Both are the same destruction the `file_delete_directory` link
guard exists to prevent, so the same rule is applied here, through the same `LinkGuard` helper and
the same `LinkTarget is not null` predicate. **Both endpoints are classified, not just one**: a
guard applied to the source alone would leave the destination open, and the tests are written as a
pair for that reason. The grant root, and everything above it, is not classified — an author who
roots a grant at a link has made that choice, while a link inside the grant has been vetted by
nobody. The boundary is the deepest grant that both contains **and permits** the endpoint: a grant
whose denied patterns reject the path authorized nothing, and treating it as the boundary anyway
would stop the walk below a link the request actually traversed.
`file_create_directory` deliberately does **not** carry this rule: creating a directory
through a link writes outside the grant but destroys nothing, so it stays with the rest of the
library's lexical resolution rather than being narrowed here.

**The endpoint classification is pre-flight, and that is the whole claim.** It answers for the
paths as they stand when the request is judged, before anything is moved. It is not a defense
against a process racing the tool: one able to write inside a location the operator already granted
can replace a component between the check and the `Directory.Move`, a time-of-check-to-time-of-use
window that re-checking the path would move rather than close, because portable .NET exposes no
handle-relative, no-follow directory move. The window is stated rather than papered over. An
adversary already writing inside a granted location is outside what a path-based API can defend
against, and this design does not claim otherwise.

**The containment check compares path segments, not string prefixes.** A sibling whose name merely
begins with the
source's name — `drafts-archive` beside `drafts` — is not a child, and a prefix comparison on raw
strings would wrongly refuse it. The relative path between the two is computed instead, and the
comparison follows the host's own case rules, because two spellings differing only in case name
the same directory on Windows and different directories elsewhere. The "climbs out" half of that
test is segment-based for the same reason the prefix half is: `Path.GetRelativePath` answers
`..foo` for a genuine child of that name, and an unconditional test for a leading `..` would read
that real child as lying outside the source — so a move into it would escape the
destination-inside-source refusal and fail later with an opaque file-system error instead. Only the
exact segment `..`, alone or followed by a separator, means the destination sits above or beside
the source.

**A move between volumes is refused, not emulated.** `Directory.Move` cannot cross a volume
boundary, and a policy may well grant a workspace on one volume and a session location on another.
The failure is caught and reported as "the directory could not be moved". **No copy-then-delete
fallback is implemented**: it would be a recursive copy plus a recursive delete wearing a move's
name, doubling the blast radius and bypassing the entry ceiling entirely. The limitation is stated
here rather than hidden behind a fallback that behaves differently from what the verb says.

#### Error Handling

Everything a model controls produces a returned refusal. The only exception the unit raises is
`ArgumentNullException` for a null policy at construction.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the directory could not be moved. The refusal carries no exception
text, which is developer-facing and can name a host path the policy never disclosed. The
cross-volume case arrives here.

#### Dependencies

`PathPolicy` for both write decisions, `ToolResult` for results, and `GuardedToolFactory` for
construction. `LinkGuard`, the subsystem's shared helper, answers whether either endpoint was
reached through a link. From the Base Class Library it uses `Directory`, `File`, `Path`, and
directory-move
exceptions. `AIFunction`, from `Microsoft.Extensions.AI.Abstractions`, is the constructed tool
type.

#### Callers

`FilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.
