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
3. The path the target is reached *through* is classified: `LinkGuard.FindLinkedAncestor` walks
   from the resolved target up to the grant root, and a link on that path refuses the request as
   `PathNotPermitted`, naming the offending component as the model spelled it. The grant root and
   everything above it are not classified.
4. A `path` that is **itself a link** takes the link branch, *before* anything is asked about what
   the path leads to: the link entry alone is removed, unfollowed, and the confirmation says what
   it pointed at was not touched. A link the host still reports as a directory is removed by the
   non-recursive directory delete; a link that leads nowhere at all is removed by the file delete,
   which is what the POSIX hosts require for one. A link that still resolves to a *file* is not
   this branch's business and falls through to the next step.
5. A `path` naming a file is refused as `InvalidRequest`.
6. A missing directory is refused as `TargetNotFound`.
7. **Phase one** walks the tree iteratively, over an explicit stack, and builds the removal plan,
   mutating nothing. The walk never descends into a link. The first link it meets — file or
   directory — ends the walk and the whole request is refused as `InvalidRequest`, naming the
   entry. A file-system failure during the walk is classified and refused as `InvalidRequest`,
   never thrown.
8. The plan's entry count, which **includes the named directory itself**, is compared against
   `policy.Limits.MaxDeleteEntries`. A plan exceeding it is refused as `ResourceTooLarge`, naming
   the exact count and the ceiling. The count is exact even for a tree far past the ceiling: the
   walk keeps counting past it and stops only retaining the paths.
9. **Phase two** removes exactly the approved plan: every file, then every directory in reverse
   collection order so each is empty when it goes. The confirmation reports the entry count; a
   failure part way through reports how many entries had already been removed.

#### Design Decisions

**The two phases are a requirement, not an optimization.** A single-pass walk that discovered a
link, or crossed the ceiling, mid-flight would leave a half-destroyed tree — the worst outcome
available and the one hardest to reason about afterwards. Deciding before acting is also what
makes both guards enforceable with one shape rather than two. What the two phases guarantee is
exact: **no removal begins until the whole plan is approved.** They cannot guarantee that a
removal, once begun, completes — the file system can refuse an entry at any point, and a file
another process holds open is the ordinary Windows case. The tool's answer there is to report the
figure rather than to claim the tree is intact; see *Error Handling*.

**The walk is iterative, over an explicit stack.** Recursion costs one stack frame per directory
level, and a tree deep enough to exhaust the thread's stack raises `StackOverflowException` —
which cannot be caught and terminates the host process, before the entry ceiling is ever
consulted. That would defeat the ceiling on exactly the input the ceiling exists for. An explicit
stack moves depth onto the heap, so depth costs the same bounded resource breadth already does.
Children are pushed in reverse so they pop in the order the directory reported them, which keeps
the visiting order — and therefore which entry a tree holding several links is refused for —
identical to the order the recursive walk produced.

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

**The named link is classified before the path is judged by what it leads to.** A link whose
target has since been removed is still an entry, and the two platform families disagree about what
kind of entry it is. Measured: a Windows junction whose target has gone still answers
`Directory.Exists`, while a POSIX symbolic link answers `File.Exists` instead, because
`Directory.Exists` follows it. An order that asked the existence questions first therefore made
the escape hatch above a *Windows* escape hatch — on Linux and macOS the same request was refused
as though the link were a file, leaving an entry the whole family would decline, since the walk
refuses any parent that contains it. Classification reads only the entry's own metadata, which
says nothing about the target, so placing it first costs nothing and makes the rule platform
independent. The *removal call* still depends on what the host reports, because it must:
`Directory.Delete` of a dangling POSIX symbolic link raises `DirectoryNotFoundException` and
`File.Delete` of a Windows junction raises `UnauthorizedAccessException`, both measured. A link
that still resolves to a *file* is deliberately left to `file_delete`, which keeps this tool from
being a second route to removing one named file.

**The refusal names the entry, never the target.** The offending entry is reported as the path the
model supplied plus the relative sub-path the walk reached, in the separator dialect the request
used. The link's target is a host location outside the grant, and naming it would disclose exactly
what the guard exists to protect.

**A path the model *names* that traverses a link is refused too.** `RealPathResolver` resolves
lexically, so `workspace/junction/sub` satisfies the write decision whenever `workspace/junction` is
spelled inside the grant — however far outside the grant the junction actually leads — and the
discovered-entry guard above never fires, because the walk starts *past* the link and every entry it
then meets is an ordinary file. This unit therefore applies its own rule to the components a path is
reached *through* as well as to the entries the walk finds: before planning, it walks from the
resolved target up to the grant root and refuses if any component is a link, using the one
`LinkTarget is not null` predicate in `LinkGuard` rather than a second notion of what a link is.
**This is a deliberate narrowing of what the caller may spell**, and it is made here and in
`FileMoveDirectoryTool` alone, because the blast radius of getting it wrong in those two is a whole
tree rather than a single entry. The rest of the library still resolves lexically, which is what
`RealPathResolver` and the README document; that wider question is untouched here.

**The grant root itself is not classified, and neither is anything above it.** An application author
who roots a grant at a link, or beneath one, has made that choice deliberately, and refusing it would
make the whole grant unusable. A link *inside* the grant has been vetted by nobody, which is exactly
the difference the walk draws. Where a policy carries nested read-write grants the boundary is the
*deepest* one containing the target, so a grant an author deliberately rooted beyond a link stays
usable. An unrestricted read-write grant has no boundary at all — there is no outside to escape to —
so nothing is classified under one.

**The ceiling bounds a mistake, not an intention.** An agent that means to destroy a tree can
remove it a file at a time with `file_delete` and this ceiling will not stop it. What the ceiling
buys is that a *mistake* — a wrong path, a model confusion, an off-by-one in a constructed path —
is survivable and observable rather than total in one call. The documentation must not be read as
claiming more.

**The count is exact, and the enumeration cost is accepted — but not the retention cost.** "More
than the limit" tells a model nothing about whether subdividing would help; a real figure does. A
very large tree is therefore enumerated in full only to be refused. The enumeration is
metadata-only, happens once per refused call, and covers a tree that was about to be enumerated
anyway — a trade recorded here so a later reader meets it rather than rediscovering it. Holding
that tree is a separate cost and is *not* accepted: once the running count passes
`MaxDeleteEntries` the walk stops appending paths and only keeps counting, so the exact figure is
still reported while the request too large to approve accumulates nothing. Under
`PathRule.Unrestricted(AccessLevel.ReadWrite)` — a supported configuration — that is the
difference between a refusal and a refusal that first materializes every path on the volume.

#### Error Handling

Everything a model controls produces a returned refusal, and so does every file-system failure the
unit can meet. The only exception the unit raises is `ArgumentNullException` for a null policy at
construction.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal. **Both phases are classified, not just the removal.** The planning walk
enumerates an arbitrary tree, so it meets a sub-directory the process may not enumerate, and a
child that vanishes between one step and the next — the plan-and-execute race this design reasons
about. Leaving those to escape would break the family's rule that a refusal is a result on the one
tool that walks a tree it did not choose. The refusal carries no exception text, which is
developer-facing and can name a host path the policy never disclosed.

The two phases refuse differently, because they leave the file system in different states:

- **Phase one** mutates nothing, so its refusal says the directory could not be deleted and that
  is the whole truth.
- **Phase two** may already have removed part of the plan, so its refusal names **how many entries
  were removed** out of how many the plan covered. Every other refusal this tool composes states
  that nothing was deleted; a bare "could not be deleted" would therefore be read as "the tree is
  intact", and a model would act on a tree that is no longer there. Nothing can prevent the
  partial removal itself — the file system is the thing that failed — but the outcome is reported
  rather than concealed. Phase one is what guarantees the tool never *chooses* to stop part way
  through.

#### Dependencies

`PathPolicy` for the write decision and for `ToolLimits.MaxDeleteEntries`, `ToolResult` for
results, and `GuardedToolFactory` for construction. `LinkGuard`, the subsystem's shared helper,
answers what counts as a link and whether a named path was reached through one. From the Base Class
Library it uses
`Directory`, `File`, `DirectoryInfo`, `FileInfo`, `FileSystemInfo.LinkTarget`,
`FileSystemInfo.ResolveLinkTarget`, `Path`, and
directory-deletion exceptions. `AIFunction`, from `Microsoft.Extensions.AI.Abstractions`, is the
constructed tool type.

#### Callers

`FilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.
