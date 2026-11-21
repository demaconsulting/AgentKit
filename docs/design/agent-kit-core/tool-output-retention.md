## ToolOutputRetention

![AgentKit Core Structure](AgentKitCoreView.svg)

The public `ToolOutputRetention` class is one conversation's retention of large tool outputs: it wraps
tools so an oversized output is retained and previewed, publishes the reader tools, and exports and
restores what it holds. Its snapshot types, `ToolOutputRetentionSnapshot` and `RetainedToolOutput`,
are documented here as supporting value types.

### Purpose

`ToolOutputSpill.Apply` retains outputs for one tool list and allocates one store per call. That
serves a host that composes a tool list once, but an application that owns its agent loop, or
injects tools into an agent runtime per conversation, needs more:

- **Tools added over time.** Covering a tool added after the first turn by re-applying retention to
  the whole list allocated a new store and lost every reference the model already held.
- **Persistence.** An agent runtime that serializes and reloads a conversation discarded the store
  with the tool list, so every reference in the reloaded history was answered "never retained".

`ToolOutputRetention` is the store as an object a host keeps with its conversation. `Wrap` covers a
tool at any time, `ReaderTools` publishes the two readers once, and `Export`/`Restore` carry the
retained outputs through persistence. `ToolOutputSpill.Apply` — and through it
`ToolPackBuilder.WithToolOutputSpill` — is built on this type, so both paths retain identically.

### Data Model

| Member | Type | Description |
| --- | --- | --- |
| `_store` | `ToolOutputStore` | The store the outputs are retained in; one per instance. |
| `_attachment` | `SpillAttachment` | The attachment every wrapped tool hands results to. |
| `Limits` | `ToolLimits` | The ceilings the instance was created with. |
| `Options` | `ToolOutputOptions` | The preview size and retention ceilings. |
| `ReaderTools` | `IReadOnlyList<AIFunction>` | `tool_output_read` then `tool_output_search`, built once; a read-only wrapper over an owned array. |

`ToolOutputRetentionSnapshot` holds `LastIssued` (`long`, the number of the last reference issued) and
`Outputs` (`IReadOnlyList<RetainedToolOutput>`, oldest first). `RetainedToolOutput` holds `Id`
(`string`, for example `output-3`), `Lines` (`IReadOnlyList<string>`, the normalized lines a reader
pages) and `Wrapped` (`bool`). Both are immutable and carry `[JsonConstructor]` constructors; each
constructor copies its collection into an owned read-only array (`Array.AsReadOnly`), so a later change
to the caller's list never reaches a snapshot, and `RetainedToolOutput` rejects null lines with
`ArgumentNullException`, including when deserializing JSON that omits them. The
internal source-generated `ToolOutputRetentionJsonContext` (camel-case property names) backs the
public `ToolOutputRetentionSnapshot.JsonTypeInfo`, so a snapshot serializes without reflection and is
safe under trimming and ahead-of-time compilation.

Invariants:

- Every tool an instance wraps, and both its readers, share its one store; two instances share none.
- `ReaderTools` returns the same two instances for the life of the retention.
- A restored retention never issues a reference at or below the snapshot's `LastIssued`, and every
  reference it restores is at or below it.

### Key Methods

#### ToolOutputRetention(ToolLimits limits, ToolOutputOptions? options)

Validates `limits` — null is an `ArgumentNullException`, a result ceiling below
`ToolOutputSpill.MinimumResultCharacters` an `ArgumentException` naming `limits`, worded by
`ToolOutputSpill.DescribeCeilingTooSmall` — then allocates the store, the attachment and the two
readers.

#### Wrap(AIFunction tool) and Wrap(IEnumerable&lt;AIFunction&gt; tools)

Validate, then wrap. A name longer than `ToolName.MaxLength` or carrying the reserved `tool_output`
prefix is an `ArgumentException`; the list overload additionally materializes the sequence once and
refuses a null entry or two tools of one name in the call before wrapping anything. A
`GuardedFunction` is re-wrapped over its inner function through `WithSpill`, keeping any ceilings
`AsGuarded` attached; any other function is wrapped in a new `GuardedFunction`. Duplicate names are
checked only within one call: wrapping a tool of an existing name later is the host's decision.

#### Export()

Takes `ToolOutputStore.Snapshot` under the store's lock and hands each entry's lines to a new
`RetainedToolOutput`, whose constructor copies them, so the snapshot is independent of the store.

#### Restore(ToolOutputRetentionSnapshot snapshot, ToolLimits limits, ToolOutputOptions? options)

1. Reject a null snapshot.
2. Construct a new retention with `limits` and `options`, so ceilings are validated as the constructor
   validates them.
3. Validate the snapshot: a negative `LastIssued`; a null output; an identifier `ToolOutputStore`
   could not have issued, one repeated, or one whose number is above `LastIssued` (a reference never
   issued); or lines that are empty, hold a null line, a line break, or a line longer than
   `ToolOutputStore.MaxLineCharacters` — each an `ArgumentException` naming `snapshot`. Nothing is
   restored from a rejected snapshot. Null lines never reach this step: `RetainedToolOutput` refuses
   them.
4. Order the outputs by number and hand them to `ToolOutputStore.Restore`, which re-measures each,
   drops one above the per-output ceiling, evicts oldest first to the count and character ceilings,
   and sets the counter to `LastIssued`, which validation has made at least the highest number.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `limits`, `tool`, `tools` or `snapshot` | `ArgumentNullException` propagates |
| Null `outputs` or `lines` building a snapshot (directly or by deserializing) | `ArgumentNullException` |
| Result ceiling below the minimum | `ArgumentException` naming `limits` |
| Null entry, reserved prefix, over-long name, duplicate in one call | `ArgumentException` |
| Malformed snapshot | `ArgumentException` naming `snapshot`; nothing restored |
| Oversized output, evicted or unknown reference | Returned to the model as a result |

Every exception is a composition or persistence mistake surfaced to the host; nothing a model
supplies is ever thrown.

### Design Constraints

**One retention, one conversation.** The store is the isolation boundary. A host that shares one
instance between conversations shares their references, exactly as it would share a tool list.

**Persisted outputs are conversation data.** A snapshot holds the full text of every retained output,
up to `ToolOutputOptions.MaxRetainedCharacters`. It belongs to the same privacy class as the chat
history, which already holds tool results, and is potentially large; a host persisting it should
size and protect it accordingly.

**No aliasing after restore.** Numbering continues past every reference ever issued, including
evicted ones and ones whose content was not persisted, so an old reference in a reloaded history is
answered "no longer retained" rather than with unrelated content.

Instances are safe for concurrent use: the store is lock-guarded and every other member is immutable.

### Dependencies

- **ToolOutputStore** — the store, its `Snapshot` and `Restore`, `TryParse` and `MaxLineCharacters`;
  see *ToolOutputStore Unit Design*.
- **ToolOutputSpill** — `SpillAttachment`, the ceiling and name checks and their wording; see
  *ToolOutputSpill Unit Design*.
- **GuardedToolFactory** — the `GuardedFunction` wrapper; see *GuardedToolFactory Unit Design*.
- **ToolOutputReadTool**, **ToolOutputSearchTool** — the readers; see their unit designs.
- **ToolLimits**, **ToolOutputOptions**, **ToolName** — ceilings and the name limit.
- **Microsoft.Extensions.AI.Abstractions** — `AIFunction`; **System.Text.Json** — the source-generated
  serialization contract.

### Callers

A public API entry point: an application creates one per conversation. Within this system
`ToolOutputSpill.Apply` creates one per call. In Agents.ChatClient, `ToolPackContextProvider` creates
one per agent session and persists its snapshot in the session's state.
