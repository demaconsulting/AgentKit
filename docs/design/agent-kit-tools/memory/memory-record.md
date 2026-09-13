### MemoryRecord

![AgentKit Tools Memory Structure](MemoryView.svg)

The `MemoryRecord` unit comprises two public immutable records: `MemoryRecord`, which is one memory,
and `MemoryMatch`, which is one memory as a search found it.

#### Purpose

To be the currency of the family: what a file call produces, what a store holds, what a recall
returns, and what an update or a revision replaces. The types exist so that the descriptor/payload
split is expressed in the data model rather than left as a convention the tools have to remember,
and so that a store can hand a memory out without any caller being able to mutate what the store
holds.

#### Data Model

Both are sealed positional records and are public, because `IMemoryStore` is public and an author
substituting persistence has to be able to name what they are persisting.

`MemoryRecord`:

| Member           | Type                    | Invariant                                     |
| ---------------- | ----------------------- | --------------------------------------------- |
| `Id`             | `string`                | Unique within the store; never changed        |
| `Descriptor`     | `string`                | The only text embedded, and the only searched |
| `Details`        | `string`                | Never embedded; returned whole on recall      |
| `SourceDocument` | `string?`               | The document the fact came from, or absent    |
| `SourceLocator`  | `string?`               | Where in that document, or absent             |
| `Embedding`      | `ReadOnlyMemory<float>` | The vector `Descriptor` produced              |

`MemoryMatch`:

| Member       | Type           | Invariant                                         |
| ------------ | -------------- | ------------------------------------------------- |
| `Memory`     | `MemoryRecord` | The memory found                                  |
| `Similarity` | `double`       | Cosine of one comparison, inclusive range -1 to 1 |

Three properties of the model are deliberate. The vector is held beside the descriptor so that
near-duplicate detection and recall both read an already-computed value rather than re-embedding
stored text — which is what makes the duplicate check nearly free. Provenance is nullable because a
memory whose source is unknown is a real memory, and requiring one would either block filing what a
model legitimately read or invite it to invent a citation. And there is no link, edge or relation to
another memory: two spikes produced 530 such links, correctly wired, none of which ever contributed
to a correct answer.

The score is carried on `MemoryMatch` rather than on `MemoryRecord` because it is a property of one
comparison: the same memory scores differently against every query, and a score stored in the record
would be silently contradicted by the next recall.

#### Key Methods

##### MemoryRecord positional constructor

The compiler-generated positional constructor, taking `Id`, `Descriptor`, `Details`,
`SourceDocument`, `SourceLocator` and `Embedding`.

**Preconditions:** none enforced here. Validation of what a model supplied happens in the tools, and
validation of what reaches a store happens in the store; a record that also validated would triple
the places one rule is written.

**Postconditions:** every part is reported exactly as stated.

##### with-expression

The compiler-generated non-destructive mutation.

**Preconditions:** none.

**Algorithm:** produces a new record with the named parts replaced and every other part carried
across.

**Postconditions:** `MemoryUpdateTool` uses `existing with { Details = ... }` to replace a payload
while the descriptor, the provenance and the vector are carried over in one expression, which is
what makes a half-applied update unrepresentable.

#### Error Handling

N/A - the records enforce no invariants of their own and therefore raise nothing. Validation belongs
to the tools, which refuse a model's malformed request, and to the store, which rejects a
programming error from a tool.

#### Dependencies

The unit depends only on the Base Class Library.

#### Callers

`IMemoryStore` and `InMemoryMemoryStore` hold and return `MemoryRecord` and return `MemoryMatch`.
`MemoryFileTool` constructs a record; `MemoryUpdateTool` re-states one; `MemoryReviseTool`
constructs a replacement; `MemoryRecallTool` and `MemoryFileTool` read `MemoryMatch`.
