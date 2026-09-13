### MarkdownOutlineTool

![AgentKit Tools Markdown Structure](MarkdownView.svg)

The `MarkdownOutlineTool` class publishes the `markdown_outline` tool.

#### Purpose

To report the heading structure of one Markdown file the access policy permits the agent to read, as
a structured outline of sections. Each section carries a heading level, title, 1-based start line,
and 1-based end line.

The tool exists so a model can compose Markdown structure with text tools. It can outline a document,
then use `text_file_read` to read a section's range or `text_file_cut_lines` to remove that section.
That is why there is no markdown read or replace tool.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member             | Type     | Invariant                                               |
| ------------------ | -------- | ------------------------------------------------------- |
| `ToolName`         | `string` | `markdown_outline`; public constant; carries the prefix |
| `MaxHeadingLevel`  | `int`    | `6`; greatest ATX heading level Markdown defines        |
| `MaxHeadingIndent` | `int`    | `3`; four spaces begins an indented code block          |
| `HeadingMarker`    | `char`   | `#`; marker for ATX headings                            |
| Structured result  | object   | Path, section count, and section list                   |

The structured result has `path`, `sectionCount`, and `sections`. Each section has `level`, `title`,
`startLine`, and `endLine`. The result is bounded by `PathPolicy.Limits.MaxResultCharacters` after it
is serialized. `MaxReadBytes` does not participate in this unit: the document is streamed heading by
heading, so a document larger than the read ceiling is outlined rather than refused — only the outline
result is bounded.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment and
`MarkdownPack` is the only place the `markdown` family prefix is claimed.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy`, then builds a guarded synchronous delegate with defaulted `path`
and `maxDepth` parameters.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(string? path = null, int? maxDepth = null)`

**Algorithm**, in this order:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`.
2. A `maxDepth` outside 1 through 6 is refused as `InvalidRequest`.
3. `policy.TryResolveRead(path, …)` is called. A refusal is returned as `PathNotPermitted`.
4. A directory is refused as `InvalidRequest`.
5. A missing file is refused as `TargetNotFound`.
6. The file is streamed heading by heading, headings are parsed, and sections are computed. The
   document's size does not gate the outline; only the small heading list and a running line count
   are held.
7. The reported path mirrors the caller's dialect.
8. The structured result is serialized to measure it against `MaxResultCharacters`.
9. An oversized outline is refused with advice to limit depth; otherwise the structured result is
   returned.

The policy decision precedes file-system observations, so a refused path never discloses whether it
exists.

##### ParseHeadings(TextReader reader, out int totalLines)

Streams the Markdown text line by line through `TextLines.EnumerateLines`, so a document larger than
the read ceiling is still outlined while only the heading list and the running line count are held. A
fence line beginning with

``` or ~~~ toggles a fenced code block. Lines inside a fence are ignored for heading detection. A
line indented four or more spaces is also ignored as an indented code block. Outside those contexts,
`TryReadHeading` recognizes ATX headings.

##### BuildSections(...)

Computes each heading's section end from the next heading of the same or a higher level, or from the
file's total line count when there is no such heading. The method applies `maxDepth` only to which
sections are returned, not to the headings used to compute end lines.

##### TryReadHeading(...)

Recognizes one to six leading hash characters followed by a space or by the end of the line. The
title is trimmed, and optional closing hash characters are removed. Setext headings are not modeled
by this tool.

#### Error Handling

Everything a model controls produces a returned refusal. The only exception the unit raises is
`ArgumentNullException` for a null policy at construction.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the requested Markdown file could not be read.

**The outline streams the source and bounds only the result.** The document is streamed heading by
heading, so its size never gates the outline; the serialized structured result is checked against
`MaxResultCharacters` before return. A partial outline would hide sections and make line ranges unsafe
to act on, so an oversized outline is refused with advice to limit depth rather than truncated.

#### Dependencies

`PathPolicy` and `ToolLimits` for the read decision and result ceiling, `ToolResult` for structured
and denied results, and `GuardedToolFactory` for construction. It streams lines through `TextLines`
and uses `JsonSerializer` only to measure the result against the character ceiling. From the Base
Class Library it uses `Directory`, `File`, `FileStream`, `StreamReader`, `Encoding`, and string
operations. `AIFunction`, from `Microsoft.Extensions.AI.Abstractions`, is the constructed tool type.

#### Callers

`MarkdownPack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Text-file tools do not call this unit; a model composes
the returned line ranges into those tools.
