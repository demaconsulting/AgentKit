### MemoryStore

![AgentKit Tools Memory Structure](MemoryView.svg)

The `MemoryStore` unit comprises the public `IMemoryStore` contract and the public sealed
`InMemoryMemoryStore` default implementation.

#### Purpose

To be the only thing the memory tools know about persistence. The interface exists so the
application author chooses where memories live; the default implementation exists so the family can
be attached without an author first choosing a database. Both are public because substituting
persistence is a supported, first-class use of the family rather than an extension point bolted on.

#### Data Model

`IMemoryStore` holds no state. `InMemoryMemoryStore` is sealed and holds:

| Member      | Type                 | Invariant                                   |
| ----------- | -------------------- | ------------------------------------------- |
| `_gate`     | `object`             | Lock guarding all access to `_memories`     |
| `_memories` | `List<MemoryRecord>` | Ordered; ids unique; vectors all one length |

A list rather than a dictionary, because every search is an exhaustive scan anyway and insertion
order gives a stable tie-break between two memories of equal similarity. An exhaustive scan is the
right algorithm at the scale this default is for — an agent session's worth of memories, tens to low
thousands — and an author whose corpus outgrows it substitutes a store that indexes, which is the
reason the interface is public.

#### Key Methods

##### AddAsync(MemoryRecord memory, CancellationToken cancellationToken)

Adds a memory the store does not already hold.

**Preconditions:** `memory` is non-null; the store does not already hold its identifier; its vector
is the same length as the vectors the store holds.

**Algorithm:** validates, takes `_gate`, checks the identifier ordinally, checks the vector length
against the first held memory, and appends.

**Postconditions:** the memory is held and counted. A repeated identifier raises rather than
overwriting: identifiers are assigned by the calling tool, so a collision cannot be something a model
did, and overwriting silently would destroy a memory nobody asked to remove.

##### FindAsync(string id, CancellationToken cancellationToken)

Reports the memory carrying an identifier.

**Preconditions:** `id` is non-null and non-empty.

**Algorithm:** takes `_gate` and scans ordinally.

**Postconditions:** the memory, or `null` on a miss. A miss is an ordinary outcome the tool turns
into a refusal, not an error.

##### ReplaceAsync(MemoryRecord memory, CancellationToken cancellationToken)

Replaces the memory carrying an identifier.

**Preconditions:** `memory` is non-null; its vector is the same length as the vectors the store
holds.

**Algorithm:** takes `_gate`, finds the identifier, checks the vector length, and assigns at that
position.

**Postconditions:** on a hit the memory is replaced where it stood and `true` is reported; on a miss
the store is unchanged and `false` is reported. Whole-record replacement means an update that keeps
the vector and a revision that recomputes it travel one path, so neither can half-apply.

##### RemoveAsync(string id, CancellationToken cancellationToken)

Removes the memory carrying an identifier.

**Preconditions:** `id` is non-null and non-empty.

**Algorithm:** takes `_gate`, finds the identifier, and removes at that position.

**Postconditions:** `true` on a hit, `false` on a miss with the store unchanged, so the tool can
turn a miss into a refusal rather than telling a model a fact is gone from a store that still
returns it.

##### SearchAsync(ReadOnlyMemory&lt;float&gt; vector, int count, CancellationToken cancellationToken)

Reports the memories whose descriptors are closest to a vector.

**Preconditions:** `count` is not negative; `vector` holds at least one value and is the same length
as the vectors the store holds.

**Algorithm:** validates, takes `_gate`, answers immediately with no matches for an empty store or a
zero count, checks the vector length, then scores every held memory by cosine similarity in one pass
per memory and orders by descending similarity, taking at most `count`. Cosine is computed inline —
one loop accumulating the dot product and both magnitudes — rather than taken from a library the
consumer would inherit for one loop. A vector of all zeros has no direction, so its similarity to
anything is reported as zero rather than as a division by zero.

**Postconditions:** matches ordered nearest first, at most `count` of them. Ordering is part of the
contract because the near-duplicate decision reads only the first match. Cosine — rather than a
distance — is the contract because it is the measure the author's threshold is expressed in.

##### CountAsync(CancellationToken cancellationToken)

Reports how many memories the store holds.

**Preconditions:** none.

**Postconditions:** the count, which tools state in their results so the model never has to recall
merely to check its own write.

#### Error Handling

A null memory, a null or empty identifier, a negative count and an empty search vector are all
programming errors from the tool units and are reported by argument exceptions; the tools refuse a
model's malformed request before the store is reached. A repeated identifier is likewise a
programming error. A vector of a length the store does not hold is a configuration error in the
composing application — it can only arise from changing embedding generator against a persisted
store — and is refused rather than scored, because mixing two models produces similarity numbers
that look ordinary and mean nothing. The lock makes each individual call safe against concurrent
callers; it does not combine two calls into one, so a file tool's search and its subsequent add
remain separable — see _MemoryFileTool_.

#### Dependencies

The unit depends on `MemoryRecord` and `MemoryMatch`, and otherwise only on Base Class Library
collections, locking and arithmetic. It depends on no embedding abstraction at all: a store never
embeds anything, because which backend is in use is the author's decision and invisible here.

#### Callers

`MemoryPack.CreateTools` constructs one `InMemoryMemoryStore` per composition when the application
supplied no store. All five tool units read and write through `IMemoryStore`: `MemoryFileTool` calls
`SearchAsync`, `AddAsync` and `CountAsync`; `MemoryRecallTool` calls `CountAsync` and `SearchAsync`;
`MemoryUpdateTool` and `MemoryReviseTool` call `FindAsync` and `ReplaceAsync`; `MemoryForgetTool`
calls `RemoveAsync` and `CountAsync`; and `MemoryDenials` calls `CountAsync`.
