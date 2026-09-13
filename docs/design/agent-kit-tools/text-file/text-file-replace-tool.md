### TextFileReplaceTool

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFileReplaceTool` class publishes the `text_file_replace` tool.

#### Purpose

To edit one permitted text file by replacing one exact occurrence of existing raw content with new
raw content. This is the family's only general editor. It can change text, delete text by using an
empty replacement, or insert text by including the surrounding context in both `oldText` and
`newText`.

The unit edits by content rather than by line number. A model may navigate with search and read, but
when it changes a file it states the exact text that is present and the exact text that should be
there instead.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member            | Type     | Invariant                                                       |
| ----------------- | -------- | --------------------------------------------------------------- |
| `ToolName`        | `string` | `text_file_replace`; public constant; carries the family prefix |
| `ToolDescription` | `string` | Non-empty; warns that line-number prefixes are not raw content  |
| Denial messages   | `string` | Tool-composed constants; policy denials come from policy        |

The model supplies `path`, `oldText` and `newText`. `oldText` must be non-empty and must appear
exactly once. `newText` may be empty, because an empty replacement is deletion.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy`, then builds a guarded delegate with defaulted `path`, `oldText`
and `newText` parameters. Each omitted argument becomes a refusal this unit controls rather than a
framework error.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate

**Algorithm**, in this order:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`.
2. A null or empty `oldText` is refused as `InvalidRequest`.
3. A null `newText` is refused as `InvalidRequest`; an empty string is accepted.
4. `policy.TryResolveWrite(path, …)` is called. A refusal is returned as `PathNotPermitted`.
5. A directory is refused as `InvalidRequest`.
6. A missing file is refused as `TargetNotFound` with a plain statement of the fact, naming no other
   tool.
7. The file is read and occurrences of `oldText` are counted ordinally.
8. Zero occurrences are refused as not found; the refusal states the fact and names no other tool.
9. More than one occurrence is refused as ambiguous; the refusal names the count and tells the model
   to include more surrounding lines.
10. Exactly one occurrence is replaced and the updated file is written.
11. The confirmation reports the line-count delta.

##### CountOccurrences(string text, string value)

Counts non-overlapping ordinal occurrences. The match is exact raw text, not culture-sensitive text
and not a regular expression. The count is taken before any write, so a not-found or ambiguous edit
leaves the file unchanged.

##### ReportDelta(int before, int after)

Composes the success message from the before and after line counts, including a signed delta. The
line count uses the same `TextLines` model as read, search, cut and paste.

#### Error Handling

Everything a model controls produces a returned refusal. The only exception the unit raises is
`ArgumentNullException` for a null policy at construction.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the requested file could not be edited. Cancellation is not classified.

**The exactly-once rule is the safety check.** A not-found match states that the text was not found.
An appears-N-times match names `N` and instructs the model to include more surrounding lines,
which is the action that makes the edit unique.

#### Dependencies

`PathPolicy` for the write decision, `ToolResult` for results, `GuardedToolFactory` for construction,
and `TextLines` for line-count deltas. From the Base Class Library it uses
`Directory`, `File`, and ordinal string operations. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the constructed tool type.

#### Callers

`TextFilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.
