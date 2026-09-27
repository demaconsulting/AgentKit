## File

![AgentKit Tools File Structure](FileView.svg)

The File subsystem is the type-agnostic file tool family: the pack an application attaches to give an
agent policy-governed listing, copying, moving and deletion of files of any type, and creation,
moving and recursive deletion of the directories that hold them.

### Overview

The subsystem's responsibility is to turn file-entity operations into tools that an agent can be
handed safely. It owns no containment logic of its own — every decision about whether an endpoint may
be read or written is made by the `PathPolicy` the composing application supplies — and its design is
therefore about making each operation use the right decision, refuse unsafe side effects, and return
model-actionable denials instead of throwing.

The boundary is type-agnostic on purpose. Listing, copying, moving and deleting a file are the same
operations whether the file holds text, an image, a PDF, or any other content. The directory
structure those files live in belongs here too, for the same reason: it is the container rather
than the content. Reading or editing the
contents belongs to content families such as TextFile and Markdown. The File subsystem treats the
file as the thing being managed.

The subsystem contains eight units:

| Unit                      | Responsibility                                                          |
| ------------------------- | ----------------------------------------------------------------------- |
| `FileListTool`            | Publishes `file_list`: lists permitted files of any type                |
| `FileCopyTool`            | Publishes `file_copy`: copies one readable file to one writable path    |
| `FileMoveTool`            | Publishes `file_move`: moves one writable source to one writable path   |
| `FileDeleteTool`          | Publishes `file_delete`: deletes one writable file                      |
| `FileCreateDirectoryTool` | Publishes `file_create_directory`: creates a directory and its parents  |
| `FileMoveDirectoryTool`   | Publishes `file_move_directory`: moves or renames a whole directory     |
| `FileDeleteDirectoryTool` | Publishes `file_delete_directory`: removes a directory tree, bounded    |
| `FilePack`                | Publishes the file tools as one family under the `file` prefix          |

`LinkGuard` is a shared helper rather than a unit: it publishes no tool, holds no state, and exists
only so that the two destructive directory tools ask one question — *is this entry a link, and was
this path reached through one* — in exactly one place. Duplicating that predicate would be two
rules that could drift apart on a decision whose failure mode is content outside every grant
destroyed by a request that never named it.

`SubtreeGuard` is a shared helper for the same reason and on the same pair of tools. It holds the
one walk over everything beneath a directory and the one question asked of each entry it finds —
*does the policy permit this agent to write here* — so that a removal and a move judge what they
actually touch by the same rule. Two walks would be two answers to a question whose failure mode
is content the operator's policy excludes, destroyed or relocated by a request that named only the
directory above it.

### Interfaces

The subsystem exposes exactly one public type, `FilePack`, plus the name constant each tool unit
publishes. Each tool's factory is `internal`, so a tool cannot be obtained except through the pack
that claims its family prefix.

| Interface            | Direction | Format                       | Constraints                           |
| -------------------- | --------- | ---------------------------- | ------------------------------------- |
| `FilePack`           | Outbound  | AgentKitCore `IToolPack`     | Prefix `file`; no capability          |
| `File*Tool.ToolName` | Outbound  | `string` constant            | The name each tool is published under |
| `PathPolicy`         | Inbound   | AgentKitCore policy object   | Supplied at construction              |
| File system          | Inbound   | Base Class Library file APIs | Reached only where policy permits     |

The subsystem consumes `PathPolicy`, `ToolLimits`, `ToolResult`, `GuardedToolFactory`, `IToolPack`
and `HostCapabilities` from AgentKitCore, and `AIFunction` from
`Microsoft.Extensions.AI.Abstractions` reached through Core. It exposes no content model another
package depends on.

### Design

**Construction.** `FilePack.CreateTools` receives the composition's policy and calls each unit's
internal `Create(PathPolicy)` factory. Every factory validates the policy, then builds its tool
through `GuardedToolFactory.Create`, capturing the policy in the tool's delegate. There is no other
construction path, no setter and no default policy, so a file operation outside the policy is not
representable.

**Fixed tool order.** The pack returns the seven public tools in the order `file_list`,
`file_copy`, `file_move`, `file_delete`, `file_create_directory`, `file_move_directory`, then
`file_delete_directory`. The file tools come first and the directory tools follow as a block, so
the family reads as "files, then directories". The order a model sees is observable, so the pack
makes it fixed rather than incidental.

**Write-performing tools need a write grant to be published.** Six of the seven — copy, move,
delete, and all three directory tools — each change the file system, so under a policy holding no
read-write grant anywhere they
could only ever answer a refusal, and the pack does not publish them at all. `file_list` remains,
because it needs only the read decision to report what exists and consults the write decision only
to annotate a listed root as writable. The surviving tool keeps its place, so a model sees the
family shortened, never rearranged. See *Policy-derived publication* in the system design for the
rule and the whole-family table.

**Endpoint decisions match the side effect.** Listing and a copy source use the read decision. A copy
destination uses the write decision. Move source, move destination, delete path, directory creation,
both directory-move endpoints and the directory-removal path all use the write decision because
those operations change or remove the named endpoint. Nothing in the subsystem
combines decisions or treats a readable path as writable.

**List is discovery.** `file_list` replaces the old text-file listing operation and lists files of
any type. It enumerates only through `PathPolicy.EnumerateFiles`, groups names under absolute
location headers, includes empty discovery roots with an access-level marker, and mirrors the
caller's dialect so names can be handed back to sibling tools.

**Copy and move do not clobber by accident.** Both operations refuse an existing destination unless
`overwrite` is explicitly `true`. They also refuse a missing destination parent rather than creating
a directory tree the operator did not request.

**Moving a file or a directory is also how it is renamed.** A destination in the same parent
renames, so no rename tool exists; both `file_move` and `file_move_directory` say so in the
descriptions a model reads, because a capability a model cannot discover is one the family does not
really offer.

**Directory operations are separate from the file operations, deliberately.** Creating a directory
is idempotent and reports which of its two outcomes occurred. Moving a directory admits **no
overwrite at all**, because replacing a directory is a recursive destruction wearing a moving verb.
Removing a directory is the only operation in the subsystem whose reach is not stated by the
request itself, so it is the only one carrying controls of its own: a pre-flight walk that refuses
to follow a link out of the tree, and a ceiling — `ToolLimits.MaxDeleteEntries` — on how many
entries one call may take. The ceiling bounds the damage of a *mistake*; it is not protection
against an agent that intends the destruction, which could remove a tree one file at a time. See
*FileDeleteDirectoryTool Design* for the two-phase algorithm and the rejected alternatives.

**The two destructive directory tools also refuse a path they are asked to reach *through* a
link.** Path resolution is lexical — `RealPathResolver` says so — so a path naming a link inside a
grant satisfies the write decision however far outside the grant the link actually leads, and
`file_delete_directory`'s walk would never see the link because it starts past it. Both
`file_delete_directory` and `file_move_directory` therefore classify the components a path is
reached through, from the resolved target up to the root of the deepest read-write grant that both
contains and permits it, using `LinkGuard`;
`file_move_directory` does so for its source and its destination alike. The grant root itself, and
everything above it, is not classified: an author who roots a grant at a link has chosen that,
while a link inside the grant has been vetted by nobody. A grant that merely encloses the target
while its denied patterns reject it is not the boundary either — it authorized nothing, and taking
it as the boundary would stop the walk below a link the request actually traversed. This is a
narrowing of what a caller may
spell, and it is applied **only** where the blast radius is a whole tree. `file_create_directory`
deliberately does not carry it, because creating through a link writes outside the grant but
destroys nothing, and the rest of the library — `text_file_read` and every other path-taking tool
— still resolves lexically, as the README describes.

**The same two tools judge every entry the operation touches, not only the path they were
named.** A grant permits a location and may exclude names within it: `PathRule.Allows` refuses a
path any of whose segments matches a denied pattern, so `ReadWrite(root, ["*.key"])` permits
`root` and refuses `root/secret.key`. A recursive removal reaches every entry beneath the path it
was given and a directory move relocates the whole subtree, so judging the named path alone
answers a different question from the one the operation asks — and the answer it gives is that
content the operator excluded is destroyed, or relocated, by a request that never named it. Both
tools therefore consult the write decision for every entry, through the one walk and the one
predicate in `SubtreeGuard`; the move asks it twice per entry, once where the entry stands and
once where it would land, because a destination governed by a narrower grant is a location the
policy was never asked about. **One refused entry refuses the whole request.** The permitted
subset is deliberately not removed: a partial deletion nobody asked for is the outcome the
recursive delete's two phases exist to avoid, and it would leave a workspace no one chose.

**Both link rules are pre-flight checks over paths, and the family claims no more for them.** The
path a caller *names* is classified before anything is touched, and the recursive delete's walk
refuses the links it *discovers* before a single entry goes. Neither defends against a process
racing the tool from inside a location the operator already granted: it can replace a checked
component before the removal or the move runs, and a second path check would only move that window,
because portable .NET exposes no handle-relative, no-follow directory removal or move. What bounds
the race is that no removal these tools issue follows a link — measured, `File.Delete` unlinks a
symbolic link rather than its target and refuses a Windows junction outright, and
`Directory.Delete(recursive: false)` removes a junction while leaving what it points at whole — so
the cost of a swapped component is bounded by a single entry rather than a tree.

**`file_create_directory` judges every directory it would create, not just the one named.** A
single creation call materializes each missing component of the path, and a grant root need not
exist, so a grant rooted beneath missing directories would have had them created too, in a location
no grant covers. Each missing ancestor is judged by the same write decision, and a request that
would reach above every grant is refused with nothing created at any level. This is a containment
rule, not a link rule: it asks what the policy permits, never what a path leads to.

**Single-file deletion stays single-file.** `file_delete` removes one file, never a directory, and
never recurses. It does not quarantine the deleted content; recovery is source control, the same
mechanism used for any other unwanted workspace change. Keeping it apart from
`file_delete_directory` is the safety property rather than an accident of increments: it is what
stops "delete this" ever meaning "delete this tree", which is why no recursive option was added to
it.

**No host capability is required.** Managing files needs nothing special from the model or host, so
`FilePack.RequiredCapabilities` is `HostCapabilities.None`.
