### TextFileReadTool

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFileReadTool` class publishes the `text_file_read` tool.

#### Purpose

To return a ranged, line-numbered window of one text file the access policy permits the agent to
read, and to refuse — in a way the agent can act on — every request it cannot honor. The window
opens with a `path lines A-B of N` header, where `N` is the file's true line count, so a model can
page through a large file without guessing how far it extends.

The unit does no containment reasoning of its own. It asks the policy, checks the file is text, and
shapes the answer into a display form that composes with the search, replace and cut tools.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member                | Type     | Invariant                                                    |
| --------------------- | -------- | ------------------------------------------------------------ |
| `ToolName`            | `string` | `text_file_read`; public constant; carries the family prefix |
| `ToolDescription`     | `string` | Non-empty; the basis on which a model chooses this tool      |
| `LineNumberDelimiter` | `string` | `\|`; display delimiter, never part of file content          |
| `StreamBufferSize`    | `int`    | Byte buffer used while streaming a file's lines              |
| Denial messages       | `string` | Tool-composed constants; policy denials come from policy     |

Two ceilings from `PathPolicy.Limits` bound the operation. `MaxReadBytes` bounds the bytes
materialized to satisfy the requested read window — not the size of the file, which is streamed so
only the window is held in memory — and `MaxResultCharacters` is the budget for the rendered numbered
result. Both are inclusive — a window or result exactly at a ceiling is returned. Binary detection is
delegated to `TextFileBinaryGuard`, which sniffs only a leading window, so a file far larger than
`MaxReadBytes` is still classified and paged.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment and
`TextFilePack` is the only place the `text_file` family prefix is claimed.

**Preconditions:** `policy` is non-null; it is the policy the composition was built with.

**Algorithm:** validates `policy`, then returns
`GuardedToolFactory.Create(delegate, ToolName, ToolDescription)`. The delegate accepts `path`,
optional `startLine`, optional `lineCount`, and a cancellation token. The delegate is declared
`Task<object>` deliberately, because the tool returns a union of refusal or text.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(string? path = null, int? startLine = null, int? lineCount = null, ...)`

**Algorithm**, in this order, because the order is itself the contract:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`, naming the form a request
   should take.
2. A `startLine` less than one, or a `lineCount` less than zero, is refused as `InvalidRequest`.
3. `policy.TryResolveRead(path, …)` is called. A refusal is returned as `PathNotPermitted` carrying
   the policy's own message unchanged.
4. An existing directory is refused as `InvalidRequest` with a plain statement of what was wrong,
   naming no other tool.
5. A non-existent file is refused as `TargetNotFound` with the same plain statement, naming no other
   tool.
6. A file whose leading bytes indicate binary content is refused as `UnsupportedMediaType` before
   decode. The sniff reads only a leading window, so it never materializes a large file. When the
   extension is one the image family recognizes, the refusal redirects to `image_read`.
7. The file is streamed line by line. Every line is counted so the header's `N` is the file's true
   total, but only the lines of the requested window are materialized, and their accumulated UTF-8
   bytes are bounded by `MaxReadBytes`.
8. If the window's bytes exceed `MaxReadBytes` — which an unranged read of a large file always does,
   because its window is the whole file — the request is refused as `ResourceTooLarge`. The refusal
   names the byte ceiling **and** the file's true total line count **and** directs the model to page
   with `startLine` and `lineCount`, so a large file is never a dead end.
9. Otherwise the window is rendered as a numbered listing. A rendered result longer than
   `MaxResultCharacters` is refused as `ResourceTooLarge`, naming the character ceiling.
10. Otherwise the rendered text is returned.

The policy decision precedes every observation of the file system, so a refused path never discloses
whether it exists.

##### RenderWindow(...)

Renders the materialized window into the model-facing form. The header is `path lines A-B of N`,
where `N` is the true total streamed to the end of the file. Each body line is the right-aligned
1-based number, the `|` delimiter, and the line's content without the line terminator. The reported
path mirrors the caller's dialect: relative when the caller addressed a working-directory file
relatively and the policy permits, otherwise absolute.

A range beyond the end of the file is an honest empty window, not a refusal. For a file with ten
lines, `startLine` eleven renders `lines 11-10 of 10`. A zero `lineCount` also renders an empty
window. An empty file renders `lines 0-0 of 0`.

##### Streaming and binary detection

Lines are streamed through `TextLines.EnumerateLines`, which splits only on `\n` so a line the reader
numbers is the same line `TextLines.Split` numbers and the cut and paste tools address. Binary
detection is delegated to `TextFileBinaryGuard`, which sniffs a leading byte window before decode: a
recognized UTF byte-order mark means text; without a mark, a NUL byte means binary, and otherwise the
window must pass strict UTF-8 validation. Because both the sniff and the window stream read only what
they need, a file far larger than `MaxReadBytes` is still classified and paged.

#### Error Handling

Everything a model controls produces a returned refusal, never an exception. The only exception the
unit raises is `ArgumentNullException` for a null policy at construction, which is a programming
error in the composing application rather than anything a model can provoke.

File system failures are caught by an explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal. Cancellation is not classified, so a canceled read propagates as the
runtime expects.

**A large file is paged, and only an oversized result or an unranged large read is refused — always
with recourse.** `MaxReadBytes` bounds the bytes materialized for a window rather than gating the
file, so a model can page to any line of a file far larger than the read ceiling. A truncated window
would be an omission the model cannot detect, so an unranged read of a large file is refused instead,
naming the total line count and the paging arguments; a rendered window past `MaxResultCharacters` is
likewise refused, naming the character ceiling.

**A binary file is refused, never decoded into garbled text.** The binary sniff is subject to the
same access-failure classification as the decode, so a read failure during sniff becomes the ordinary
unreadable-file refusal rather than a thrown exception.

#### Dependencies

`PathPolicy` and `ToolLimits` for the decision and the ceilings, `ToolResult` for every result it
returns, and `GuardedToolFactory` for construction. It streams lines through `TextLines` and detects
binary content through `TextFileBinaryGuard`. It names `ImageMediaTypes` plus `ImageReadTool.ToolName`
for the binary image redirect — the one redirect it still offers, because it states what the file is.
From the Base Class Library: `File`, `FileInfo`, `Directory`, `FileStream`, `StreamReader`,
`StringBuilder`, and `Encoding` from `System.Text`. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the form the constructed tool takes.

#### Callers

`TextFilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.
