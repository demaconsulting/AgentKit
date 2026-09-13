### TextFileCutLinesTool

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFileCutLinesTool` class publishes the `text_file_cut_lines` tool.

#### Purpose

To remove a 1-based inclusive range of lines from one permitted text file, while always capturing the
removed raw text into a named buffer first. It is the family's only way to remove a line range. A cut
is therefore a recoverable move to a buffer slot, not a destruction.

The unit addresses removal by line number, matching the line numbers `text_file_read` prints. A
model reads a numbered window, cuts the lines it saw, and can later paste the captured text back.

#### Data Model

The class is static and holds no state. A constructed tool captures two immutable references: the
`PathPolicy` supplied by the pack and the `TextFileLineBuffers` instance shared with paste.

| Member            | Type                  | Invariant                                                  |
| ----------------- | --------------------- | ---------------------------------------------------------- |
| `ToolName`        | `string`              | `text_file_cut_lines`; public constant; carries the prefix |
| `ToolDescription` | `string`              | Non-empty; explains the 1-based inclusive range            |
| `PathPolicy`      | `PathPolicy`          | Captured by the guarded delegate                           |
| Buffer store      | `TextFileLineBuffers` | Captured by the delegate; shared with paste                |
| Denial messages   | `string`              | Tool-composed constants; policy denials come from policy   |

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
its buffer with the paste tool from the same `CreateTools` call.

##### The tool delegate

**Algorithm**, in this order:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`.
2. `policy.TryResolveWrite(path, …)` is called. A refusal is returned as `PathNotPermitted`.
3. A directory is refused as `InvalidRequest`.
4. A missing file is refused as `TargetNotFound`.
5. The buffer slot name is selected, using `default` when the caller supplies none.
6. The file is read and split through `TextLines.Split`.
7. A range with `startLine` less than one, `endLine` before `startLine`, or `startLine` beyond the
   file's line count is refused. No text is captured and the file is unchanged.
8. `endLine` is clamped to the file's last line.
9. The exact raw slice from the first selected line through the terminator of the last selected line
   is captured into the slot.
10. The slice is removed and the updated file is written.
11. The confirmation reports the count and the first and last captured line content.

#### Error Handling

Everything a model controls produces a returned refusal. The unit raises `ArgumentNullException` only
for a null policy or buffer at construction, which is a composing-application error.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the file could not be edited. Cancellation is not classified.

**Capture precedes removal.** A successful cut has already stored the exact slice before writing the
updated file. A failed validation changes neither the buffer nor the file.

#### Dependencies

`PathPolicy` for the write decision, `ToolResult` for results, `GuardedToolFactory` for construction,
`TextFileLineBuffers` for named capture slots, and `TextLines` for splitting lines and finding raw
offsets. From the Base Class Library
it uses `Directory`, `File`, and `Path`-based text I/O. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the constructed tool type.

#### Callers

`TextFilePack.CreateTools` is the only caller of `Create`, and it passes the same
`TextFileLineBuffers` instance to `TextFilePasteLinesTool.Create`. The constructed tool is invoked by
the agent runtime an application composed it into.
