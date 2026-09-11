## TextFile

![AgentKit Tools TextFile Structure](TextFileView.svg)

The TextFile subsystem is the text file tool family: the pack an application attaches to give an
agent policy-governed reading, writing and listing of text files.

### Overview

The subsystem's responsibility is to turn three ordinary file operations into three tools that an
agent can be handed safely. It owns no containment logic of its own — every decision about whether
a path may be read, written or listed is made by the `PathPolicy` the composing application
supplies — and its own design is therefore about the things a tool must get right *around* that
decision: constructing tools so an unguarded one cannot exist, returning refusals instead of
throwing, keeping host layout out of everything that reaches a model, and honoring the ceilings the
policy carries instead of quietly truncating.

The boundary is narrow and deliberate. The subsystem reads and writes text, and lists file names.
It does not create directories, delete anything, rename anything, or interpret a file's content.
Each of those would be a separate decision an operator should be able to grant or withhold
separately, and none of them is needed for the read-edit-write loop this family exists to support.

The subsystem contains four units:

| Unit                | Responsibility                                                            |
|---------------------|---------------------------------------------------------------------------|
| `TextFileReadTool`  | Publishes `text_file_read`: returns one permitted file's text             |
| `TextFileWriteTool` | Publishes `text_file_write`: replaces one permitted file's text           |
| `TextFileListTool`  | Publishes `text_file_list`: lists the permitted files beneath a directory |
| `TextFilePack`      | Publishes the three tools as one family under the `text_file` prefix      |

### Interfaces

The subsystem exposes exactly one public type, `TextFilePack`, plus the name constant each tool
unit publishes. Each tool's factory is `internal`, so a tool cannot be obtained except through the
pack that claims its family prefix — the pack is the unit of attachment, and an application that
could construct a single tool directly could also construct one outside the family whose prefix
protects it from collision.

| Interface                | Direction | Format                       | Constraints                           |
|--------------------------|-----------|------------------------------|---------------------------------------|
| `TextFilePack`           | Outbound  | AgentKitCore `IToolPack`     | Prefix `text_file`; no capability     |
| `TextFile*Tool.ToolName` | Outbound  | `string` constant            | The name each tool is published under |
| `PathPolicy`             | Inbound   | AgentKitCore policy object   | Supplied at construction              |
| File system              | Inbound   | Base Class Library file APIs | Reached only where policy permits     |

The subsystem consumes `PathPolicy`, `ToolLimits`, `ToolResult`, `GuardedToolFactory`, `IToolPack`
and `HostCapabilities` from AgentKitCore, and `AIFunction` from
`Microsoft.Extensions.AI.Abstractions` reached through Core. It exposes nothing of its own that
another package would depend on.

### Design

**Construction.** `TextFilePack.CreateTools` receives the composition's policy and calls each
unit's internal `Create(PathPolicy)`. Every one of those factories validates the policy, then
builds its tool through `GuardedToolFactory.Create`, capturing the policy in the tool's delegate.
There is no other construction path, no setter and no default policy, so a tool that is unguarded,
or governed by a policy other than the composition's, is unrepresentable rather than merely
discouraged. Each delegate is declared to return `object` (or `Task<object>`) because a tool
returns a union — a refusal, or text — and that declared shape is exactly the case the guard
exists to protect.

**One decision per operation.** The read tool and the list tool consult `TryResolveRead`; the write
tool consults `TryResolveWrite`. Nothing in the subsystem consults both, combines them, or
re-implements either. This is what makes an operator's read-wide, write-narrow configuration real
rather than decorative, and it is verified by a scenario that reads a path successfully and is then
refused the write of the same path.

**A path a model supplies is a workspace-relative path.** A model states the paths a person states
— `notes.txt` — and the list tool reports the names it finds relatively, so the name an agent holds
after a listing is exactly the name it hands back to the read tool. None of the three units
interprets a path itself: each passes the model's text to the policy, which holds the workspace a
relative name is measured against and which alone decides what an omitted path means. That is what
keeps the three in agreement; a family whose tools read names differently would let an agent list a
name it then could not read. An absolute path remains expressible throughout and remains subject to
the same containment decision.

**Enumeration is a policy call.** `TextFileListTool` enumerates through `PathPolicy.EnumerateFiles`
and never through the file system directly. Recursive enumeration by the operating system follows
directory junctions and symbolic links out of a permitted location, so a direct enumeration would
advertise files whose reading is refused — disclosing their existence and inviting the agent to ask
for them. The policy filters every candidate through the same read decision direct access uses,
which is the invariant that keeps a listing and a read from ever reaching different conclusions.

**Refusals are results.** Every condition a model can provoke — an absent path, a path outside the
permitted location, a missing file, a directory where a file was expected, an oversized file —
produces a `ToolResult.Denied` naming its reason. Nothing is thrown at a model, because an
exception raised during a tool call ends the agent's turn and strands it. Where a refusal has an
obvious better tool, it names that tool: a read of a directory and a read of a missing file both
redirect to `text_file_list`. Every refusal message is composed from compile-time constants, with
the only interpolated values being integers naming a ceiling, so no path, permitted location or
directory separator ever reaches the transcript.

**Ceilings refuse, they do not truncate.** `MaxReadBytes` bounds what may be read and
`MaxResultCharacters` bounds what may be returned; the read tool checks both and the list tool
checks the second. An overrun is refused with the ceiling named, never truncated, because a
truncated file or listing is an omission the model cannot detect and will reason past.

**Names leaving the subsystem are relative.** A listing reports each file relative to the requested
directory with forward-slash separators, sorted in ordinal order. That keeps host layout out of the
transcript, makes a listing identical on every platform, and gives the model names it can hand
straight back to `text_file_read`.
