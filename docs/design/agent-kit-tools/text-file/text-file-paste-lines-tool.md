### TextFilePasteLinesTool

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFilePasteLinesTool` class publishes the `text_file_paste_lines` tool.

#### Purpose

To insert text previously captured by `text_file_cut_lines` into one permitted text file, either
before a chosen 1-based line or at the end of the file when `atLine` is omitted. Paste is the other
half of cut: together they make a line-range move reversible and exact.

The unit does not consume the buffer slot. A captured fragment can be pasted more than once, and an
empty slot is a refusal naming the slot rather than a silent no-op.

#### Data Model

The class is static and holds no state. A constructed tool captures two immutable references: the
`PathPolicy` supplied by the pack and the `TextFileLineBuffers` instance shared with cut.

| Member            | Type                  | Invariant                                                    |
| ----------------- | --------------------- | ------------------------------------------------------------ |
| `ToolName`        | `string`              | `text_file_paste_lines`; public constant; carries the prefix |
| `ToolDescription` | `string`              | Non-empty; explains append behavior and default slot         |
| `PathPolicy`      | `PathPolicy`          | Captured by the guarded delegate                             |
| Buffer store      | `TextFileLineBuffers` | Captured by the delegate; shared with cut                    |
| Denial messages   | `string`              | Tool-composed constants; policy denials come from policy     |

The model supplies `path`, optional `atLine`, and optional `name`. A missing or whitespace name uses
`TextFileLineBuffers.DefaultSlot`.

#### Key Methods

##### Create(PathPolicy policy, TextFileLineBuffers buffers)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment and it is
the only unit that allocates the shared buffer.

**Preconditions:** `policy` and `buffers` are non-null.

**Algorithm:** validates both inputs, then builds a guarded delegate with defaulted parameters for
`path`, `atLine` and `name`.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and shares
its buffer with the cut tool from the same `CreateTools` call.

##### The tool delegate

**Algorithm**, in this order:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`.
2. An `atLine` less than one is refused as `InvalidRequest`; omitting `atLine` is valid.
3. `policy.TryResolveWrite(path, …)` is called. A refusal is returned as `PathNotPermitted`.
4. A directory is refused as `InvalidRequest`.
5. A missing file is refused as `TargetNotFound` with a plain statement of the fact, naming no other
   tool.
6. The buffer slot name is selected, using `default` when the caller supplies none.
7. `buffers.TryPaste` reads the slot without consuming it. An empty slot is refused as
   `TargetNotFound` with a plain statement that the named slot is empty. When other slots hold
   captured text the refusal names them from `buffers.PopulatedSlots()` as a statement of fact,
   because the usual cause is a capture into a named slot followed by a paste that omitted the same
   name; the refusal names no tool to run.
8. The target file is read and split through `TextLines.Split`.
9. An omitted `atLine`, or a value past the end of the file, appends. Otherwise the captured text is
   inserted before the requested line.
10. The updated file is written and the confirmation reports how many captured lines were pasted,
    where they were inserted, the explicit line span the pasted text now occupies, and the file's
    new total line count. The span and total are stated in the updated file's own numbering, because
    an insertion renumbers every line below it and a confirmation without them leaves a whole-file
    re-read as the only way to learn the new numbering. The span is computed with
    `TextLines.LineOfOffset` and `TextLines.LastLineOfInsertedText`, so captured text that does not
    end in a terminator is reported as running on into the line that followed the insertion point.

#### Error Handling

Everything a model controls produces a returned refusal. The unit raises `ArgumentNullException` only
for a null policy or buffer at construction, which is a composing-application error.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the file could not be edited. Cancellation is not classified.

**Paste is an exact round-trip.** The captured text includes its original line terminators, and the
insertion offset is a raw character boundary from the shared line model. Cutting a range and pasting
it back at the same place reproduces the original content.

#### Dependencies

`PathPolicy` for the write decision, `ToolResult` for results, `GuardedToolFactory` for construction,
`TextFileLineBuffers` for named paste slots and, through `PopulatedSlots`, for the names an empty-slot
refusal reports as a fact, and `TextLines` for line
counts, insertion offsets and the reported line span. From
the Base Class Library it uses `Directory` and text file I/O. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the constructed tool type.

#### Callers

`TextFilePack.CreateTools` is the only caller of `Create`, and it passes the same
`TextFileLineBuffers` instance to `TextFileCutLinesTool.Create`. The constructed tool is invoked by
the agent runtime an application composed it into.
