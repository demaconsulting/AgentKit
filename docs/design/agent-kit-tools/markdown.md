## Markdown

![AgentKit Tools Markdown Structure](MarkdownView.svg)

The Markdown subsystem is the markdown tool family: the pack an application attaches to give an
agent a policy-governed outline of a Markdown file's heading structure.

### Overview

The subsystem's responsibility is to report where Markdown sections begin and end. It does not read a
section's text and it does not edit Markdown content, because the TextFile subsystem already reads
and cuts by line range and replaces by exact raw content. The Markdown subsystem supplies the one
piece the text tools cannot infer on their own: the line ranges of logical Markdown sections.

The boundary is deliberate. `markdown_outline` reports each heading section's level, title, and
1-based start and end line. A section spans from its heading through the line before the next heading
of the same or a higher level, or through the end of the file. `maxDepth` filters which heading
levels are reported without changing how section ends are computed.

The subsystem contains two units:

| Unit                  | Responsibility                                                       |
| --------------------- | -------------------------------------------------------------------- |
| `MarkdownOutlineTool` | Publishes `markdown_outline`: reports heading sections and ranges    |
| `MarkdownPack`        | Publishes the outline tool as one family under the `markdown` prefix |

### Interfaces

The subsystem exposes exactly one public type, `MarkdownPack`, plus the name constant the outline
unit publishes. The tool's factory is `internal`, so a tool cannot be obtained except through the
pack that claims its family prefix.

| Interface                      | Direction | Format                       | Constraints                       |
| ------------------------------ | --------- | ---------------------------- | --------------------------------- |
| `MarkdownPack`                 | Outbound  | AgentKitCore `IToolPack`     | Prefix `markdown`; no capability  |
| `MarkdownOutlineTool.ToolName` | Outbound  | `string` constant            | `markdown_outline`                |
| Structured outline result      | Outbound  | ToolResult structured object | Path, section count and sections  |
| `PathPolicy`                   | Inbound   | AgentKitCore policy object   | Supplied at construction          |
| File system                    | Inbound   | Base Class Library file APIs | Reached only where policy permits |

The subsystem consumes `PathPolicy`, `ToolLimits`, `ToolResult`, `GuardedToolFactory`, `IToolPack`
and `HostCapabilities` from AgentKitCore, and `AIFunction` from
`Microsoft.Extensions.AI.Abstractions` reached through Core. It exposes line ranges intended for the
TextFile subsystem to consume.

### Design

**Construction.** `MarkdownPack.CreateTools` receives the composition's policy and calls
`MarkdownOutlineTool.Create(policy)`. The tool factory validates the policy, builds through
`GuardedToolFactory.Create`, and captures the policy in the delegate. There is no construction path
for an unguarded outline tool.

**One tool is enough.** The family publishes only `markdown_outline`. A markdown read tool would
duplicate `text_file_read`, and a markdown replace tool would duplicate `text_file_replace`. The
outline composes with those tools by returning line ranges.

**Section ranges are structural.** The parser records all headings, computes each section's end from
the next heading of the same or a higher level, and only then applies `maxDepth` to the reported
sections. A shallow outline therefore still reports whole sections, not sections shortened by hidden
subheadings.

**ATX parsing is fence-aware and indent-aware.** Only one to six leading hash characters outside a
fenced code block and not indented as code count as headings. Hashes inside triple-backtick or
triple-tilde fences, and hashes on lines indented four or more spaces, are content.

**Policy and ceilings match text reading.** The outline tool uses the read decision, checks
`MaxReadBytes` before reading, and checks `MaxResultCharacters` against the serialized structured
result before returning it. Oversized inputs or results are refused rather than truncated.

**No host capability is required.** Reading Markdown structure needs no model vision or other special
host support, so `MarkdownPack.RequiredCapabilities` is `HostCapabilities.None`.
