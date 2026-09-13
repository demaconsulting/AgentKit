### MemoryReviseTool

![AgentKit Tools Memory Structure](MemoryView.svg)

The `MemoryReviseTool` class is a public static modeled unit publishing the `memory_revise` tool.

#### Purpose

To replace a memory's descriptor, details and provenance together and re-embed it, so the memory is
found by what it now says and cites the document it now reflects. It is the unit that fixes the
stale-provenance defect observed in the spike this family comes from.

#### Data Model

The class is stateless. A constructed tool captures the store and the embedding generator. One
constant is published: `ToolName`, `memory_revise`.

#### Key Methods

##### Create(IMemoryStore store, IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt; generator)

Creates the tool over one composition's store.

**Preconditions:** both arguments are non-null.

**Algorithm:** validates each collaborator, then builds the tool through `GuardedToolFactory.Create`
from a delegate declared to return `Task<object>`, taking optional `memoryId`, `descriptor`,
`details`, `sourceDocument` and `sourceLocator` parameters and a cancellation token.

**Postconditions:** an `AIFunction` carrying `ToolName` and a description. The description states
plainly that an omitted source clears the source — a model that did not know this would omit the
argument meaning "unchanged" and get "none", a surprise the tool has no way to detect and the model
no way to discover.

##### ReviseAsync(...)

Replaces one memory wholesale.

**Preconditions:** none; every malformed request is refused rather than thrown.

**Algorithm:** refuse a missing identifier, a missing descriptor and missing details; find the
memory and, on a miss, refuse — looking it up before embedding means a revision naming a wrong
identifier costs nothing at the embedding backend; embed the new descriptor; construct a new
`MemoryRecord` keeping only the identifier and taking descriptor, details and both provenance fields
from the call, treating a blank source as absent; and replace it in the store.

**Postconditions:** the memory is found by its new wording, says what the call stated, and cites
exactly what the call stated — including nothing. The result reports `revised: true`, the
identifier, the new descriptor, whether a source was stated, and the source. Reporting the resulting
provenance is what makes the outcome visible rather than assumed, and the `sourceStated` flag
carries an absence that JSON serialization would otherwise express only by omitting a field.

Two design decisions are recorded here because both are the subject of requirements:

_Provenance is taken from the call, never from the memory._ The spike's revision silently preserved
the original source document and locator, so a fact revised from a different document went on citing
the superseded one — observed with details drawn from `02-revision.md` beside a structured field
still reading `01-initial.md`. Inheriting when the caller states nothing would be the same defect in
another form, so an omitted source clears. No policy about whether sources replace, accumulate or
require corroboration is imposed: that is the application author's instruction to give, and this
tool's only job is not to silently keep data it knows may be wrong.

_A revision is not near-duplicate checked._ The check exists to stop a second memory being filed
about a fact already held. A revision names one existing memory and changes it, so there is no second
memory to prevent — and because most corrections keep wording close to the original, checking would
refuse the very corrections the family exists to make possible, including refusing a memory against
itself.

#### Error Handling

A missing identifier, a missing descriptor, missing details and an identifier the store does not
hold are all returned refusals, composed for the shared cases by `MemoryDenials`. The not-found
refusal states the identifier and how many memories are held and prescribes nothing. An embedding
backend that produces no vector raises from `MemoryEmbedding` and is not converted into a refusal,
because the model can do nothing about a host fault.

#### Dependencies

`IMemoryStore`, `MemoryRecord`, `MemoryDenials`, `MemoryEmbedding`, AgentKitCore's `ToolResult` and
`GuardedToolFactory`, and `IEmbeddingGenerator` from `Microsoft.Extensions.AI.Abstractions`.

#### Callers

`MemoryPack.CreateTools` constructs the tool fourth in the family's published order.
