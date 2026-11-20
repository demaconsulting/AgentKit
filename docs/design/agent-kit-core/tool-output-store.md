## ToolOutputStore

![AgentKit Core Structure](AgentKitCoreView.svg)

The internal `ToolOutputStore` class holds the large tool outputs one composition has retained,
names each by an opaque reference, and answers lookups by that reference.

### Purpose

A retained output must live somewhere the reader tools can reach and the file system cannot. The
store keeps outputs in memory, normalized into the lines a model pages through, and bounds what it
holds. It is the single piece of per-composition state retention introduces.

**There is no path anywhere in it.** Holding outputs on disk would introduce host paths, a cleanup
obligation and a new surface for confinement to defend; holding them in memory makes the readers
unable to reach the file system by construction. The store lives exactly as long as the tool list
built with it, which is why a reference survives a session's rotation: rotation carries the tool
list unchanged.

**One store belongs to one composition.** `ToolOutputSpill.Apply` allocates it and shares it only
with the tools that call produces. It is internal and no public type accepts one, so a delegated
agent composed separately has a separate store — the same isolation `TodoStore` gives a task list.

Access is guarded by one lock, so concurrent tool calls against one composition are safe.

### Data Model

| Member | Type | Description |
| --- | --- | --- |
| `_entries` | `Dictionary<long, ToolOutputEntry>` | Retained entries by numeric identifier. |
| `_order` | `Queue<long>` | Numeric identifiers of retained entries, oldest first. |
| `_retainedCharacters` | `long` | Characters currently retained across every entry. |
| `_issued` | `long` | The numeric identifier most recently issued; zero when none. |
| `_options` | `ToolOutputOptions` | The ceilings observed. |
| `_gate` | `object` | The lock guarding the four mutable fields. |

`ToolOutputEntry` is an immutable record of `Id` (for example `output-3`), `Lines` (the normalized
lines, never empty), `Characters` (the normalized size, counting one per line break) and
`Wrapped` (whether any line was wrapped). Being immutable, an entry is read without the lock even
if the store evicts it meanwhile.

`ToolOutputLookup` names the four lookup outcomes: `Found`, `Malformed`, `Unknown` and `Evicted`.

**Invariants:** identifiers are issued in strictly increasing order and never reused;
`_retainedCharacters` equals the sum of the retained entries' sizes; after every addition both
`MaxRetainedOutputs` and `MaxRetainedCharacters` hold.

### Key Methods

#### Add(string text)

Normalizes the output outside the lock, then retains it.

**Normalization:** `\r\n`, `\r` and `\n` each end a line, and a break at the very end ends the last
line rather than opening an empty one (matching how `text_file_read` counts a file's lines). A line
longer than `MaxLineCharacters` (2,000) is split into chunks no longer than that, never between the
two halves of a surrogate pair. An empty output is one empty line.

**Retention:** when the normalized size exceeds `MaxOutputCharacters` the method returns `null` and
evicts nothing — refusing one output must not cost the model references it already holds. Otherwise
it evicts the oldest entries until one more entry fits both ceilings (which always terminates,
because the per-output ceiling is no larger than the total), issues the next identifier, and
returns the entry.

#### Find(string? id, out ToolOutputEntry? entry)

Parses the identifier — surrounding whitespace ignored, then exactly `output-` followed by a
positive decimal number with no leading zero — and looks it up. Because identifiers are issued in
order, an absent identifier at or below the last issued was retained once and has been evicted; one
above it was never issued. No record of evicted entries is therefore needed.

#### Refusal(ToolOutputLookup outcome, string? id)

Composes the refusal both readers return for a failed lookup, so they state the same facts in the
same words: `InvalidRequest` naming the identifier form for a malformed or missing identifier;
`TargetNotFound` stating the output was never retained; `TargetNotFound` stating the output is no
longer retained because it was evicted.

#### Indent(JsonElement element)

Writes a JSON document indented through a `Utf8JsonWriter` with a relaxed encoder, so compact JSON
— one line a model could not page — spans one value per line and stays readable. No reflection is involved, so
no trimming warning reaches a consumer.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null options or text | `ArgumentNullException` propagates |
| Output over the per-output ceiling | `Add` returns `null`; the caller refuses |
| Malformed, unknown or evicted identifier | `Find` reports the outcome; `Refusal` composes it |
| `Refusal` asked about `Found` | `ArgumentOutOfRangeException` (a defect in the caller) |

### Dependencies

- **ToolOutputOptions** — the ceilings; see *ToolOutputOptions Unit Design*.
- **ToolResult** — composes the lookup refusals; see *ToolResult Unit Design*.
- **System.Text.Json** — `Utf8JsonWriter` for indenting structured data.

### Callers

- **ToolOutputSpill** — allocates the store, adds outputs, and indents structured results.
- **ToolOutputReadTool** and **ToolOutputSearchTool** — look outputs up and compose refusals.
