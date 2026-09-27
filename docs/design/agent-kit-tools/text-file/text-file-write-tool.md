### TextFileWriteTool

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFileWriteTool` class publishes the `text_file_write` tool.

#### Purpose

To make a permitted text file's content be exactly the text the model supplied — creating the file
when it is absent, replacing everything in it when it is present — and to capture whatever it
displaces into the family's recovery buffer first, so a wholesale overwrite is no less recoverable
than a cut.

It is the middle of three editing tools, and the boundaries between them are the point of having
three. `text_file_create` brings a file into existence and refuses to replace one. `text_file_write`
sets all of a file's content. `text_file_replace` changes part of one by exact match. There is
deliberately **no `overwrite` flag**: a mode switch would make one name mean two things, and a model
would have to work out which mode it was in before it could predict what a call would do. Three
names, each saying one thing, is what lets each description be unambiguous.

The unit does no containment reasoning of its own. It asks the policy for the write decision and then
performs only the single write the model requested.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly two captured values — the
`PathPolicy` and the `TextFileLineBuffers` instance supplied at construction — and neither can be
replaced afterwards.

| Member            | Type                  | Invariant                                                  |
| ----------------- | --------------------- | ---------------------------------------------------------- |
| `ToolName`        | `string`              | `text_file_write`; public constant; carries family prefix  |
| `ToolDescription` | `string`              | Non-empty; names the sibling tools and the recovery slot   |
| Denial messages   | `string`              | Tool-composed constants; policy denials come from policy   |
| Captured buffer   | `TextFileLineBuffers` | The composition's single instance, shared with paste       |

The model supplies `path` and `content`. A null `content` is malformed, but empty content is a valid
request and empties the file.

#### Key Methods

##### Create(PathPolicy policy, TextFileLineBuffers buffers)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment.

**Preconditions:** `policy` and `buffers` are both non-null.

**Algorithm:** validates both arguments, then builds a guarded delegate with optional-default `path`
and `content` parameters so omitted arguments reach the tool body as refusals rather than framework
errors.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description naming
`text_file_create` and `text_file_replace`, and is governed by the supplied policy and buffer for the
rest of its life. Requiring the buffer at construction is what makes a write tool that could destroy
content without capturing it unrepresentable rather than merely discouraged.

##### The tool delegate: `(string? path = null, string? content = null, ...)`

**Algorithm**, in this order:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`.
2. A null `content` is refused as `InvalidRequest`. An empty string is accepted.
3. `policy.TryResolveWrite(path, …)` is called. A refusal is returned as `PathNotPermitted`.
4. A directory at the target path is refused as `InvalidRequest`.
5. A missing parent directory is refused as `TargetNotFound`; the tool creates no directory.
6. When a file already exists, `TextFileBinaryGuard.IsBinaryAsync` classifies it. Binary content is
   refused as `UnsupportedMediaType` and the file is left untouched.
7. When a file already exists and is text, its entire content is read with `File.ReadAllTextAsync`.
   If that content is non-empty it is captured into `TextFileLineBuffers.OverwrittenSlot`.
8. The file is written and the confirmation reports the character count, the resulting line count
   from `TextLines.Split`, whether the file was created or replaced, and — when replaced — whether
   anything was captured.

#### Design Decisions

**The capture is the file's previous text, verbatim.** `TextFileLineBuffers` stores a raw string per
slot, so no line mapping is involved: the whole previous content is captured unmodified. That is what
makes the restoration byte-exact — a trailing newline survives, `\r\n` terminators survive, and a
file with no final newline restores without one. It is the same property that makes the family's
cut/paste round trip exact.

**The capture goes to a distinct, fixed slot.** `OverwrittenSlot` is `overwritten`, not
`DefaultSlot`, and `text_file_write` takes no slot-name argument.

- *Not the default slot*, because the default slot is the model's working clipboard. A model that has
  cut a fragment and is about to paste it is holding that fragment in `default`; a capture it never
  requested landing on top would destroy an edit in flight, converting a safety net into a second act
  of destruction. The two captures have different owners — one the model asked for, one it did not —
  so they must not share a name.
- *Not a model-supplied name*, because cut and copy take a name to stage a fragment the model intends
  to paste and therefore knows where it put. A write's capture is a safety net the model did not ask
  for and, by construction, did not anticipate needing. A name it chose could be `default`, reopening
  the hole a distinct name closes. More decisively, the description can only state *unconditionally*
  where displaced content went if the name is fixed.
- *Not a per-file slot name*, because that invents unbounded slot names in a store the family
  documents as "a staging area for a move, not a durable document store", and would make the paste
  refusal's slot guidance grow without bound.

**Only the most recent overwrite is recoverable, and the tool says so.** One fixed slot means a
second overwrite replaces the first capture — ordinary clipboard semantics this family already
documents. The guarantee is therefore *the most recent overwrite is recoverable*, and both the tool
description and the replacement confirmation state that rather than implying an unbounded undo
history.

**The capture is a sequential guarantee, and the tool says that too.** Reading the previous
content, capturing it and writing the new content are three steps, and nothing serializes them
against another writer of the same file. Two concurrent writes can both read content `X`; the first
writes `A`, the second writes `B`, and `overwritten` still holds `X` — so `A` is destroyed without
ever having been captured. *Cross-tool locking was rejected* as the answer. The library coordinates
concurrent access nowhere else, an agent's tool calls are sequential, and a delegated sub-agent
composes its own buffers, so introducing a locking regime for this one tool would be a far larger
change than the exposure justifies. The honest answer is to scope the promise instead, exactly as
the one-slot limit above scopes it: **every sequential overwrite is recoverable; if two callers
write the same file concurrently, the buffer holds the content that was there before whichever
write read it, and an interleaved write can be lost uncaptured.** A promise with an unstated
exception is the defect; a stated limit is not.

**Nothing is captured when there is nothing to lose, and the slot is released.** An absent file has
no previous content. An existing but empty file has none either, and capturing `string.Empty` would
leave a slot that `TryPaste` reports as a hit but that pastes nothing while reporting success. In
both cases the tool captures nothing and the confirmation says so — and it releases the slot, rather
than leaving an earlier capture standing. A stale slot would be the failure hardest to notice: the
confirmation would report that nothing was captured while a later paste handed back content
displaced by some older write, contradicting the promise that only the most recent overwrite is
recoverable.

**No ceiling is enforced, on the written content or on the capture.** This is a decision, not an
omission. Every ceiling `ToolLimits` carries reasons from the model's context budget and is applied
to content flowing *to* the model. The `content` argument arrives *from* the model and is already in
the transcript, so refusing it would spend a turn rejecting text the provider has already accepted
and would save no context. The capture goes into the buffer and never into a result, so it spends
none either. This matches `text_file_create`, `text_file_replace`, `text_file_cut_lines`,
`text_file_copy_lines` and `text_file_paste_lines`, none of which consults a ceiling.
`MaxResultCharacters` is likewise not checked: the confirmation is a fixed-shape sentence whose only
variable parts are decimal counts and cannot approach the ceiling.

**A non-text target is refused rather than captured and destroyed.** This unit is the only one in the
family that refuses a binary target on a write path, and it follows from the family's invariant
rather than from sibling parity. The invariant is that nothing the family destroys is unrecoverable
through the line buffers. Binary content read through `ReadAllTextAsync` comes back with replacement
characters, so a captured binary file would not restore: the recoverability promise would be false
in exactly the case where the destruction is total. Cut removes a named range a model deliberately
chose; write destroys everything, so the same latent exposure is maximal here. The corresponding
pre-existing exposure in `text_file_cut_lines` and `text_file_replace` is recorded as a known
limitation and is not changed by this unit.

**Refusals state facts; the description names the siblings.** No refusal this unit returns names
another tool or opens a redirect sentence, because a denial that prescribed a remedy was measured
pushing a model into a destructive workaround a user had forbidden. The teaching happens in the
description, where it is load-bearing: an agent that reaches for `text_file_write` to make a small
edit clobbers the file, so the description names `text_file_create` and `text_file_replace` to steer
it to the narrower operation.

**Known limitation, shared with the family.** `File.ReadAllTextAsync` and `File.WriteAllTextAsync`
strip and omit a UTF-8 byte-order mark, so a BOM-marked file does not round-trip through the buffer
byte for byte. This is pre-existing behavior shared with `text_file_cut_lines` and
`text_file_replace`, recorded here rather than changed by this unit.

#### Error Handling

Everything a model controls produces a returned refusal, never an exception. The only exceptions the
unit raises are `ArgumentNullException` for a null policy or buffer at construction.

File system failures are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal that the file could not be written. Cancellation is not classified, so a
canceled write propagates as the runtime expects.

The capture precedes the write, so a write that fails after a successful capture leaves the file
unchanged and the previous content already in the buffer. A refusal at any earlier step — a refused
path, a directory, a missing parent, a binary target — captures nothing and writes nothing.

#### Dependencies

`PathPolicy` for the write decision, `ToolResult` for results, and `GuardedToolFactory` for
construction, all from AgentKitCore. Within the subsystem: `TextFileLineBuffers` for the capture,
`TextFileBinaryGuard` for the text-versus-binary decision, and `TextLines` for the reported line
count. From the Base Class Library it uses `Directory`, `File`, `FileInfo` and `Path`. `AIFunction`,
from `Microsoft.Extensions.AI.Abstractions`, is the constructed tool type.

#### Callers

`TextFilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.
