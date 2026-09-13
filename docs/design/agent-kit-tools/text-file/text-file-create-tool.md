### TextFileCreateTool

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFileCreateTool` class publishes the `text_file_create` tool.

#### Purpose

To create one new text file at a path the access policy permits the agent to write, without ever
replacing an existing file or creating a missing directory tree. It is the family's tool for bringing
new content into existence; changes to existing content go through `text_file_replace`.

The unit does no containment reasoning of its own. It asks the policy for the write decision and then
performs only the single creation the model requested.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member            | Type     | Invariant                                                      |
| ----------------- | -------- | -------------------------------------------------------------- |
| `ToolName`        | `string` | `text_file_create`; public constant; carries the family prefix |
| `ToolDescription` | `string` | Non-empty; the basis on which a model chooses this tool        |
| Denial messages   | `string` | Tool-composed constants; policy denials come from policy       |

The model supplies `path` and `content`. A null `content` is malformed, but empty content is a valid
request and creates an empty file.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy`, then builds a guarded delegate with optional-default `path` and
`content` parameters so omitted arguments reach the tool body as refusals rather than framework
errors.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(string? path = null, string? content = null, ...)`

**Algorithm**, in this order:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`.
2. A null `content` is refused as `InvalidRequest`. An empty string is accepted.
3. `policy.TryResolveWrite(path, …)` is called. A refusal is returned as `PathNotPermitted`.
4. A directory at the target path is refused as `InvalidRequest`.
5. An existing file is refused as `InvalidRequest` with a plain statement of the fact, naming no
   other tool.
6. A missing parent directory is refused as `TargetNotFound`; the tool creates no directory.
7. The file is written and the confirmation reports the character count and the created file's total
   line count, so a model that goes on to address the file by line number needs no exploratory read
   first. The line count uses `TextLines.Split`, so an empty file is reported as zero lines rather
   than one.

#### Error Handling

Everything a model controls produces a returned refusal, never an exception. The only exception the
unit raises is `ArgumentNullException` for a null policy at construction.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the file could not be created. Cancellation is not classified, so a
canceled write propagates as the runtime expects.

**Create refuses to replace.** The existing-file check is the tool's defining guarantee. It protects
against a model destroying content when it meant to start a new file; the refusal states the fact and
prescribes no remedy.

#### Dependencies

`PathPolicy` for the write decision, `ToolResult` for results, and `GuardedToolFactory` for
construction. From the Base Class Library it
uses `Directory`, `File`, `Path`, and file write APIs. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the constructed tool type.

#### Callers

`TextFilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.
