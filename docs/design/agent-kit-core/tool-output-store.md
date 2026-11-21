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

**Invariants:** identifiers are issued in strictly increasing order, never reused, and never wrap;
`_retainedCharacters` equals the sum of the retained entries' sizes; after every addition both
`MaxRetainedOutputs` and `MaxRetainedCharacters` hold.

### Key Methods

#### Add(string text)

Refuses an output whose raw length alone proves it cannot fit, then normalizes the output outside
the lock, then retains it.

**Early refusal:** before any copy is made, `Add` returns `null` when `MustExceed(text.Length,
MaxOutputCharacters)` holds, that is when the raw length `L` exceeds `2(M + 1)` for a ceiling `M`.
Normalization keeps every character outside a line break, counts each break sequence as one
character whether it was `\r\n`, `\r` or `\n`, drops at most one break at the very end, and wrapping
only adds breaks. With `C` characters outside breaks and `B` break sequences, `L` is at most
`C + 2B` and the normalized size `N` is at least `C + B - 1`, so `N` is at least `L / 2 - 1`, and a
raw length above `2(M + 1)` forces `N` above `M`. The bound is tight: `M + 1` CRLF pairs are
`2(M + 1)` characters long and normalize to exactly `M`, which fits. The comparison is made in 64
bits because `M` may be as large as `int.MaxValue`. A very large output is therefore never copied
into lines only to be discarded, and no output that would fit is refused early.

The check is made in `Add`, on exactly the text about to be normalized, so every retention path
goes through it. It is not made on compact JSON before `Indent`: re-indenting with the relaxed
encoder can shorten escaped JSON (`\u00e9` becomes `é`), so the compact length does not bound the
indented one.

**Normalization:** `\r\n`, `\r` and `\n` each end a line, and a break at the very end ends the last
line rather than opening an empty one (matching how `text_file_read` counts a file's lines). A line
longer than `MaxLineCharacters` (2,000) is split into chunks no longer than that, never between the
two halves of a surrogate pair. An empty output is one empty line. `Normalize` and `Measure` run
one shared scan, which assembles the lines only when asked to, so the size measured and the size
retained can never drift apart.

**Retention:** when the normalized size exceeds `MaxOutputCharacters` the method returns `null` and
evicts nothing — refusing one output must not cost the model references it already holds. Before
evicting, it throws `InvalidOperationException` when the counter is already `long.MaxValue`, so the
counter never wraps to a negative identifier no lookup resolves and a refusal evicts nothing;
`ToolOutputRetention.Restore` bounds a restored counter by `MaxRestorableCounter` (`long.MaxValue / 2`),
so no public path reaches it. Otherwise
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

A malformed identifier is echoed back only when it is no longer than `MaxEchoedIdCharacters` (64),
so a near miss shows the model its mistake while a long value — text the model chose — cannot grow
the refusal without bound. A well-formed identifier is at most 26 characters (`output-` and 19
digits), so the unknown and evicted refusals, which echo only an identifier that parsed, are
bounded already. Every refusal this method composes is therefore far shorter than
`ToolOutputSpill.MinimumResultCharacters`.

#### Indent(JsonElement element)

Writes a JSON document indented through a `Utf8JsonWriter` with a relaxed encoder, so compact JSON
— one line a model could not page — spans one value per line and stays readable. No reflection is involved, so
no trimming warning reaches a consumer.

#### MustExceed(int rawLength, int maxCharacters)

Reports whether `rawLength > 2(maxCharacters + 1)`, computed in 64 bits: the early-refusal test
`Add` makes before normalizing, derived under `Add` above. Internal so the unit tests can prove it
never refuses an output that fits.

#### Measure(string text)

Returns exactly the normalized size `Normalize` reports and `Add` compares to
`MaxOutputCharacters` — line breaks normalized and counted one each, long lines wrapped — by
running the same scan without assembling any line, so nothing is copied. `ToolOutputSpill` uses it
to state, in the retention-limit refusal, the very size the store judged, even for an output far
too large to have been normalized.

#### Snapshot(out long lastIssued)

Returns the retained entries, oldest first (the eviction order), and the last identifier issued, both
taken under the lock so they describe one moment. Used by `ToolOutputRetention.Export`.

#### Restore(IEnumerable&lt;(long Number, IReadOnlyList&lt;string&gt; Lines, bool Wrapped)&gt; entries, long lastIssued)

Fills an empty store with previously exported entries, under the lock:

1. Throw `InvalidOperationException` when the store already holds an entry or has issued an
   identifier: restoring over live outputs would interleave two histories under one counter.
2. For each entry, in ascending number, re-measure it from its lines (characters plus one per break)
   so the charge is the store's own figure; skip it when it exceeds `MaxOutputCharacters`; otherwise
   evict oldest first until `MaxRetainedOutputs` and `MaxRetainedCharacters` hold, then add it.
3. Set the counter to the larger of `lastIssued` and the highest entry number, so no identifier at or
   below one ever issued is issued again, and a skipped or evicted entry is answered as evicted.

The caller (`ToolOutputRetention.Restore`) validates identifiers, lines and the counter bound
(`MaxRestorableCounter`) before calling.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null options or text | `ArgumentNullException` propagates |
| Output over the per-output ceiling | `Add` returns `null`; the caller refuses |
| Raw length over `2(M + 1)` | `Add` returns `null` before normalizing; the caller refuses |
| Malformed, unknown or evicted identifier | `Find` reports the outcome; `Refusal` composes it |
| `Refusal` asked about `Found` | `ArgumentOutOfRangeException` (a defect in the caller) |
| `Restore` on a store that has retained output | `InvalidOperationException` (a defect in the caller) |
| `Add` after the largest identifier was issued | `InvalidOperationException` before anything is evicted (a defect in the caller) |

### Dependencies

- **ToolOutputOptions** — the ceilings; see *ToolOutputOptions Unit Design*.
- **ToolResult** — composes the lookup refusals; see *ToolResult Unit Design*.
- **System.Text.Json** — `Utf8JsonWriter` for indenting structured data.

### Callers

- **ToolOutputRetention** — allocates the store, snapshots it for export and restores it.
- **ToolOutputSpill** — through its attachment, adds outputs, indents structured results, and
  measures an output it must refuse.
- **ToolOutputReadTool** and **ToolOutputSearchTool** — look outputs up and compose refusals.
