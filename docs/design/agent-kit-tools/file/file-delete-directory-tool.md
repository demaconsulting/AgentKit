### FileDeleteDirectoryTool

![AgentKit Tools File Structure](FileView.svg)

The `FileDeleteDirectoryTool` class publishes the `file_delete_directory` tool.

#### Purpose

To remove a directory, with everything beneath it, in a location the access policy permits the
agent to write. It is the only operation in the family whose reach is not stated by the request
itself — a path names one directory but may stand for tens of thousands of entries — which is why
it is the only one that carries two controls of its own: a link guard and an entry ceiling.

Removing a directory is a write in the strongest sense, so a tree that is readable but not
writable cannot be removed. The write decision alone governs the tool.

`file_delete` is unchanged by this unit's existence and still refuses a directory. **Keeping the
two apart is the safety property**, not an accident of increments: it is what stops a request to
delete something ever meaning a request to delete a tree.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable. The removal ceiling is read
from `policy.Limits.MaxDeleteEntries` on every call rather than captured, so a host's
configuration reaches the tool through the same object every other budget does.

| Member            | Type     | Invariant                                                     |
| ----------------- | -------- | ------------------------------------------------------------- |
| `ToolName`        | `string` | `file_delete_directory`; public constant; carries the prefix  |
| `ToolDescription` | `string` | Non-empty; states the link rule and the entry ceiling         |
| Denial messages   | `string` | Tool-composed constants; policy denials come from policy      |
| Success messages  | `string` | Two constants; a removed tree and a removed link differ       |

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
3. A `path` naming a file is refused as `InvalidRequest`.
4. A missing directory is refused as `TargetNotFound`.
5. A `path` that is **itself a link** takes the link branch: the link entry alone is removed,
   unfollowed, and the confirmation says what it pointed at was not touched.
6. **Phase one** walks the tree and builds the complete removal plan, mutating nothing. The walk
   never descends into a link. The first link it meets — file or directory — ends the walk and the
   whole request is refused as `InvalidRequest`, naming the entry.
7. The plan's entry count, which **includes the named directory itself**, is compared against
   `policy.Limits.MaxDeleteEntries`. A plan exceeding it is refused as `ResourceTooLarge`, naming
   the exact count and the ceiling.
8. **Phase two** removes exactly the approved plan: every file, then every directory in reverse
   collection order so each is empty when it goes. The confirmation reports the entry count.

#### Design Decisions

**The two phases are a requirement, not an optimization.** A single-pass walk that discovered a
link, or crossed the ceiling, mid-flight would leave a half-destroyed tree — the worst outcome
available and the one hardest to reason about afterwards. Deciding before acting is also what
makes both guards enforceable with one shape rather than two.

**No recursive framework delete is ever issued.** Deleting a tree recursively was measured on
Windows to throw when the tree contains a directory junction, and to leave the tree *partly
deleted* when it does. The walk is therefore hand-rolled and every removal is a single entry.

**A link inside the tree refuses the whole request; a link that is the named path is removed.**
Following a link would remove content the request never named; removing a discovered link entry
would destroy a connection the request never mentioned. Neither was asked for, so the tool refuses
— which is the family's established voice, the same one `file_copy`, `file_move` and
`text_file_create` use where an irreversible act is ambiguous. *The alternative — silently removing
the link and continuing — was rejected*, because it turns a success into a success needing an
asterisk, and the family's own missing-file rule exists precisely because a bland success leaves a
model unable to tell one outcome from another.

Allowing the *named* path to be a link is what keeps that refusal from being a dead end. Were a
link never removable, a workspace containing one would be permanently undeletable by the agent:
`file_delete` refuses a directory, and a link is a directory. Naming the link directly removes it
alone, and a non-recursive delete of a link was measured to leave the target's contents intact.

**The refusal names the entry, never the target.** The offending entry is reported as the path the
model supplied plus the relative sub-path the walk reached, in the separator dialect the request
used. The link's target is a host location outside the grant, and naming it would disclose exactly
what the guard exists to protect.

**Stated boundary.** A path the model *names* that traverses a link — `workspace/junction/sub` —
is resolved lexically and permitted, exactly as `file_delete` and every other tool in the library
already behave. That is the position `RealPathResolver` documents and the README states. This unit
guards what the walk *discovers*; it does not reopen the question of what the caller may spell.

**The ceiling bounds a mistake, not an intention.** An agent that means to destroy a tree can
remove it a file at a time with `file_delete` and this ceiling will not stop it. What the ceiling
buys is that a *mistake* — a wrong path, a model confusion, an off-by-one in a constructed path —
is survivable and observable rather than total in one call. The documentation must not be read as
claiming more.

**The count is exact, and the enumeration cost is accepted.** "More than the limit" tells a model
nothing about whether subdividing would help; a real figure does. A very large tree is therefore
enumerated in full only to be refused. The enumeration is metadata-only, happens once per refused
call, and covers a tree that was about to be enumerated anyway — a trade recorded here so a later
reader meets it rather than rediscovering it.

#### Error Handling

Everything a model controls produces a returned refusal. The only exception the unit raises is
`ArgumentNullException` for a null policy at construction.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the directory could not be deleted. The refusal carries no exception
text, which is developer-facing and can name a host path the policy never disclosed.

A failure during phase two can leave a partly removed tree; nothing can prevent that, since the
file system is the thing that failed. Phase one is what guarantees the tool never *chooses* to
stop part way through.

#### Dependencies

`PathPolicy` for the write decision and for `ToolLimits.MaxDeleteEntries`, `ToolResult` for
results, and `GuardedToolFactory` for construction. From the Base Class Library it uses
`Directory`, `File`, `DirectoryInfo`, `FileInfo`, `FileSystemInfo.LinkTarget`, `Path`, and
directory-deletion exceptions. `AIFunction`, from `Microsoft.Extensions.AI.Abstractions`, is the
constructed tool type.

#### Callers

`FilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.
