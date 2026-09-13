### TextFileCopyLinesTool

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFileCopyLinesTool` class publishes the `text_file_copy_lines` tool.

#### Purpose

To capture a 1-based inclusive range of lines from one permitted text file into a named buffer slot
while leaving the source file byte-identical. It is the non-destructive sibling of
`text_file_cut_lines`: where cut removes the range and captures it, copy captures the same range and
changes nothing. A copy is therefore a duplication to a buffer slot, not a move.

The unit addresses the range by line number, matching the line numbers `text_file_read` prints. A
model reads a numbered window, copies the lines it saw, and can later paste the captured text — as
many times as it likes — with `text_file_paste_lines`.

#### Data Model

The class is static and holds no state. A constructed tool captures two immutable references: the
`PathPolicy` supplied by the pack and the `TextFileLineBuffers` instance shared with cut and paste.

| Member            | Type                  | Invariant                                                   |
| ----------------- | --------------------- | ----------------------------------------------------------- |
| `ToolName`        | `string`              | `text_file_copy_lines`; public constant; carries the prefix |
| `ToolDescription` | `string`              | Non-empty; explains the 1-based inclusive range             |
| `PathPolicy`      | `PathPolicy`          | Captured by the guarded delegate                            |
| Buffer store      | `TextFileLineBuffers` | Captured by the delegate; shared with cut and paste         |
| Denial messages   | `string`              | Tool-composed constants; policy denials come from policy    |

The model supplies `path`, `startLine`, `endLine`, and optional `name`. A missing or whitespace name
uses `TextFileLineBuffers.DefaultSlot`.

#### Key Methods

##### Create(PathPolicy policy, TextFileLineBuffers buffers)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment and it is
the only unit that allocates the shared buffer.

**Preconditions:** `policy` and `buffers` are non-null.

**Algorithm:** validates both inputs, then builds a guarded delegate with defaulted parameters for
`path` and `name`. The line numbers default to zero so an omitted line number becomes the tool's
out-of-bounds refusal.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and shares
its buffer with the cut and paste tools from the same `CreateTools` call.

##### The tool delegate

**Algorithm**, in this order:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`.
2. `policy.TryResolveRead(path, …)` is called — the read decision alone, the deliberate inverse of
   cut's write decision. A refusal is returned as `PathNotPermitted`.
3. A directory is refused as `InvalidRequest`.
4. A missing file is refused as `TargetNotFound`.
5. The buffer slot name is selected, using `default` when the caller supplies none.
6. The file is read and split through `TextLines.Split`.
7. A range with `startLine` less than one, `endLine` before `startLine`, or `startLine` beyond the
   file's line count is refused. No text is captured and the file is unchanged.
8. `endLine` is clamped to the file's last line.
9. The exact raw slice from the first selected line through the terminator of the last selected line
   is captured into the slot. The source file is never written, so it stays byte-identical.
10. The confirmation reports the count, states the source is unchanged, and names the first and last
    captured line content, echoing the model-supplied path normalized to forward slashes.

#### Error Handling

Everything a model controls produces a returned refusal. The unit raises `ArgumentNullException` only
for a null policy or buffer at construction, which is a composing-application error.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the file could not be read. Cancellation is not classified.

**The source is never mutated.** Every success and every refusal leaves the source file byte-for-byte
identical; the only state a successful copy changes is the buffer slot it captures into. A failed
validation changes neither the buffer nor the file.

#### Dependencies

`PathPolicy` for the read decision, `ToolResult` for results, `GuardedToolFactory` for construction,
`TextFileLineBuffers` for named capture slots, and `TextLines` for splitting lines, finding raw
offsets and normalizing the reported path. From the Base Class Library it uses `Directory`, `File`,
and `Path`-based text I/O.
`AIFunction`, from `Microsoft.Extensions.AI.Abstractions`, is the constructed tool type.

#### Callers

`TextFilePack.CreateTools` is the only caller of `Create`, and it passes the same
`TextFileLineBuffers` instance to `TextFileCutLinesTool.Create` and `TextFilePasteLinesTool.Create`.
The constructed tool is invoked by the agent runtime an application composed it into.
