## File

![AgentKit Tools File Structure](FileView.svg)

The File subsystem is the type-agnostic file tool family: the pack an application attaches to give an
agent policy-governed listing, copying, moving and deletion of files of any type.

### Overview

The subsystem's responsibility is to turn file-entity operations into tools that an agent can be
handed safely. It owns no containment logic of its own — every decision about whether an endpoint may
be read or written is made by the `PathPolicy` the composing application supplies — and its design is
therefore about making each operation use the right decision, refuse unsafe side effects, and return
model-actionable denials instead of throwing.

The boundary is type-agnostic on purpose. Listing, copying, moving and deleting a file are the same
operations whether the file holds text, an image, a PDF, or any other content. Reading or editing the
contents belongs to content families such as TextFile and Markdown. The File subsystem treats the
file as the thing being managed.

The subsystem contains five units:

| Unit             | Responsibility                                                        |
| ---------------- | --------------------------------------------------------------------- |
| `FileListTool`   | Publishes `file_list`: lists permitted files of any type              |
| `FileCopyTool`   | Publishes `file_copy`: copies one readable file to one writable path  |
| `FileMoveTool`   | Publishes `file_move`: moves one writable source to one writable path |
| `FileDeleteTool` | Publishes `file_delete`: deletes one writable file                    |
| `FilePack`       | Publishes the four tools as one family under the `file` prefix        |

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

**Fixed tool order.** The pack returns the four public tools in the order `file_list`, `file_copy`,
`file_move`, then `file_delete`. The order a model sees is observable, so the pack makes it fixed
rather than incidental.

**Endpoint decisions match the side effect.** Listing and a copy source use the read decision. A copy
destination uses the write decision. Move source, move destination and delete path all use the write
decision because those operations change or remove the named endpoint. Nothing in the subsystem
combines decisions or treats a readable path as writable.

**List is discovery.** `file_list` replaces the old text-file listing operation and lists files of
any type. It enumerates only through `PathPolicy.EnumerateFiles`, groups names under absolute
location headers, includes empty discovery roots with an access-level marker, and mirrors the
caller's dialect so names can be handed back to sibling tools.

**Copy and move do not clobber by accident.** Both operations refuse an existing destination unless
`overwrite` is explicitly `true`. They also refuse a missing destination parent rather than creating
a directory tree the operator did not request.

**Delete is single-file only.** `file_delete` removes one file and never a directory. It never
recurses and does not quarantine the deleted content. Recovery is source control, the same mechanism
used for any other unwanted workspace change.

**No host capability is required.** Managing files needs nothing special from the model or host, so
`FilePack.RequiredCapabilities` is `HostCapabilities.None`.
