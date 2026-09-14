## TextFile

![AgentKit Tools TextFile Structure](TextFileView.svg)

The TextFile subsystem is the text file tool family: the pack an application attaches to give an
agent policy-governed searching, reading and editing of the contents of text files.

### Overview

The subsystem's responsibility is to turn content operations on text files into tools that an agent
can be handed safely. It owns no containment logic of its own — every decision about whether a path
may be read or written is made by the `PathPolicy` the composing application supplies — and its own
design is therefore about the things a tool must get right *around* that decision: constructing
tools so an unguarded one cannot exist, returning refusals instead of throwing, keeping host layout
out of model-facing output, and honoring the ceilings the policy carries instead of quietly
truncating.

The boundary is narrow and deliberate. The subsystem reads and edits the contents of text files. It
searches across files, reads a ranged and line-numbered window of one file, creates a new file,
replaces exact text that appears exactly once, and cuts, copies and pastes ranges of lines. It does
not list,
copy, move or delete files as file-system entities; those type-agnostic operations belong to the
sibling File subsystem. It also does not parse Markdown section structure; the Markdown subsystem
reports section ranges that compose into these line-based tools.

The subsystem contains nine units:

| Unit                     | Responsibility                                                           |
| ------------------------ | ------------------------------------------------------------------------ |
| `TextFileSearchTool`     | Publishes `text_file_search`: finds text in permitted files grep-style   |
| `TextFileReadTool`       | Publishes `text_file_read`: returns one permitted file's numbered window |
| `TextFileCreateTool`     | Publishes `text_file_create`: creates one new permitted text file        |
| `TextFileReplaceTool`    | Publishes `text_file_replace`: replaces one exact occurrence of text     |
| `TextFileCutLinesTool`   | Publishes `text_file_cut_lines`: removes and captures a line range       |
| `TextFileCopyLinesTool`  | Publishes `text_file_copy_lines`: captures a line range, source intact   |
| `TextFilePasteLinesTool` | Publishes `text_file_paste_lines`: inserts a captured line range         |
| `TextFileLineBuffers`    | Stores named cut/paste text slots shared by cut, copy and paste tools    |
| `TextFilePack`           | Publishes the seven tools as one family under the `text_file` prefix     |

`TextLines` and `TextFileBinaryGuard` are internal shared helpers rather than modeled units, in the
same way `MemoryEmbedding` and `MemoryDenials` are for the Memory subsystem; both are described
under *Design* below and are reviewed with this subsystem.

### Interfaces

The subsystem exposes exactly one public type, `TextFilePack`, plus the name constant each tool unit
publishes. Each tool's factory is `internal`, so a tool cannot be obtained except through the pack
that claims its family prefix — the pack is the unit of attachment, and an application that could
construct a single tool directly could also construct one outside the family whose prefix protects it
from collision.

| Interface                | Direction | Format                       | Constraints                           |
| ------------------------ | --------- | ---------------------------- | ------------------------------------- |
| `TextFilePack`           | Outbound  | AgentKitCore `IToolPack`     | Prefix `text_file`; no capability     |
| `TextFile*Tool.ToolName` | Outbound  | `string` constant            | The name each tool is published under |
| `TextFileLineBuffers`    | Internal  | Named text slot store        | One instance per tool composition     |
| `PathPolicy`             | Inbound   | AgentKitCore policy object   | Supplied at construction              |
| File system              | Inbound   | Base Class Library file APIs | Reached only where policy permits     |

The subsystem consumes `PathPolicy`, `ToolLimits`, `ToolResult`, `GuardedToolFactory`, `IToolPack`
and `HostCapabilities` from AgentKitCore, and `AIFunction` from
`Microsoft.Extensions.AI.Abstractions` reached through Core. It consumes `image_read` and
`ImageMediaTypes` from the Image subsystem for the one redirect a binary file's type earns — the only
redirect the subsystem still offers, because it states what the file is. It exposes no content model
another package depends on.

### Design

**Construction.** `TextFilePack.CreateTools` receives the composition's policy, allocates one
`TextFileLineBuffers` instance, and calls each unit's internal factory. Every factory validates the
policy, then builds its tool through `GuardedToolFactory.Create`, capturing the policy in the tool's
delegate. There is no other construction path, no setter and no default policy, so a tool that is
unguarded, or governed by a policy other than the composition's, is unrepresentable rather than
merely discouraged.

**Fixed tool order.** The pack returns the seven public tools in the order a model should learn them:
`text_file_search`, `text_file_read`, `text_file_create`, `text_file_replace`,
`text_file_cut_lines`, `text_file_copy_lines`, then `text_file_paste_lines`. The order is observable
in tool selection, so it is a contract rather than an incidental collection order. The helper unit is
modeled because it is shared state, but it is not a published tool.

**Shared helpers.** `TextLines` is the one place the family decides what a line is. It splits and
streams text into lines that each keep their own terminator, converts between a line number and a
character offset, and renders the span phrase a confirmation names — so the line the read tool
numbers is the same line the cut tool removes and the paste tool restores. `TextFileBinaryGuard` is
the one place the family decides whether a permitted file is text at all, sniffing a leading window
for a recognized byte-order mark or valid UTF-8 so that binary content is refused before anything
tries to decode it as text. Both are `internal static` and hold no state; neither is a modeled unit,
because each exists only so that several tools of this family reach one decision rather than seven.

**One decision per operation.** Search, read and copy consult the read decision. Create, replace, cut
and paste consult the write decision. A copy mutates nothing, so it does to the source exactly what
read does and consults the read decision — the deliberate inverse of cut's write decision, which
permits a copy from a read-only grant. Nothing in the subsystem combines read and write or
re-implements either decision. This is what makes an operator's read-wide, write-narrow configuration
real rather than decorative.

**Navigation is by line number; editing in place is by content.** Search reports `path:line:content`,
read returns a `path lines A-B of N` header plus numbered body lines, and cut removes a numbered line
range while copy captures one. Replace, by contrast, edits by exact raw content and requires the old
text to match exactly
once. That split lets a model navigate with stable line numbers while making local edits by copying
the actual file text it intends to change.

**The read output is display, not source.** A numbered read body renders a right-aligned 1-based line
number, a `|` delimiter, then the raw line content without its terminator. The prefix is not part of
the file. Replace arguments are raw file content and must not include those prefixes.

**Cut, copy and paste are buffer operations.** A cut always captures the exact raw line slice into a
named buffer slot before removing it; a copy captures the same slice while leaving its source
byte-identical, its non-destructive sibling. Paste inserts the stored text without consuming it, so a
copied block can be pasted more than once. The buffer is one instance per `CreateTools` call, shared
only by that composition's cut, copy and paste tools, so separate tool compositions do not leak slots
to each other.

**Refusals are results.** Every condition a model can provoke — an absent argument, a refused path,
a missing file, a directory where a file was expected, an ambiguous replacement, an empty paste slot,
an oversized result or binary content where text was required — produces a `ToolResult.Denied`
naming its reason. Nothing is thrown at a model, because an exception raised during a tool call ends
the agent's turn and strands it.

**Ceilings refuse, they do not truncate.** `MaxReadBytes` bounds what may be read and
`MaxResultCharacters` bounds what may be returned. Search and read refuse an overrun with the
ceiling named. A partial file or partial search result would be an omission the model cannot detect
and would reason past.
