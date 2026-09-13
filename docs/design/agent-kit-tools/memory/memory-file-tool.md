### MemoryFileTool

![AgentKit Tools Memory Structure](MemoryView.svg)

The `MemoryFileTool` class is a public static modeled unit publishing the `memory_file` tool.

#### Purpose

To store one memory, unless an existing memory already says nearly the same thing. It is the only
unit that writes a new memory, and therefore the only place near-duplicate detection can be
guaranteed to happen.

#### Data Model

The class is stateless. A constructed tool captures the store, the embedding generator and the
author's controls it was given. Three constants govern its behavior:

| Member                 | Type     | Value         | Rationale                                  |
| ---------------------- | -------- | ------------- | ------------------------------------------ |
| `ToolName`             | `string` | `memory_file` | The name the model is offered              |
| `NearestNeighborCount` | `int`    | 1             | The store returns nearest first            |
| `IdentifierLength`     | `int`    | 8             | Hex characters of a new identifier         |

`NearestNeighborCount` is one because if the closest memory is below the threshold no other memory
can be above it. `IdentifierLength` is eight because a full GUID demonstrably produces transcription
errors when a model quotes an identifier back, while eight hexadecimal characters make a collision
within one store impractical.

#### Key Methods

##### Create(IMemoryStore store, IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt; generator, MemoryOptions options)

Creates the tool over one composition's store.

**Preconditions:** all three arguments are non-null.

**Algorithm:** validates each collaborator, then builds the tool through `GuardedToolFactory.Create`
from a delegate declared to return `Task<object>`, capturing the three collaborators. The delegate's
parameters are `descriptor`, `details`, `sourceDocument`, `sourceLocator` and a cancellation token;
each string parameter is optional so that a model omitting one produces a refusal rather than a
schema error.

**Postconditions:** an `AIFunction` carrying `ToolName` and a description. Internal, because a pack
is the unit of attachment.

##### FileAsync(...)

Stores one memory.

**Preconditions:** none; every malformed request is refused rather than thrown.

**Algorithm, in an order that is itself the guarantee:** refuse a missing descriptor; refuse missing
details; embed the descriptor through `MemoryEmbedding`; ask the store for the single nearest
memory; if its similarity reaches `options.NearDuplicateThreshold`, return the structured
non-storage result and stop; otherwise construct a `MemoryRecord` with a new identifier, treating a
blank source document or locator as absent, add it, read the new count, and return the structured
stored result. Storing first and checking afterwards would leave a near-duplicate in the store on
every detection.

**Postconditions:** either exactly one memory was added and the result reports `stored: true`, its
identifier and the size of the store; or nothing was added and the result reports `stored: false`,
`reason: near_duplicate`, the similarity, the threshold, and the conflicting memory's identifier,
descriptor and details. There is no third outcome and no force-anyway argument: an author who wants
near-duplicates stored changes the threshold.

##### NewIdentifier()

Produces the identifier a new memory is addressed by.

**Preconditions:** none.

**Algorithm:** `mem-` followed by the first eight hexadecimal characters of a new GUID.

**Postconditions:** an identifier generated here rather than chosen by the model — a model asked to
invent unique identifiers reuses them — and rather than assigned by the store, so that a substituted
store may be a thin adapter over somebody else's persistence.

#### Error Handling

A missing descriptor and missing details are returned refusals naming `InvalidRequest`; both state
what is missing and nothing about what to do instead. The near-duplicate outcome is deliberately not
a refusal: it is a structured result, because when the same outcome was returned as prose in the
originating spike the model went on to assert that it had stored the fact. An embedding backend that
produces no vector raises `InvalidOperationException` from `MemoryEmbedding` and is not converted
into a refusal, because the model can do nothing about a host fault and a refusal would invite it to
keep trying.

#### Dependencies

`IMemoryStore`, `MemoryRecord`, `MemoryMatch`, `MemoryOptions`, `MemoryEmbedding`, AgentKitCore's
`ToolResult` and `GuardedToolFactory`, and `IEmbeddingGenerator` from
`Microsoft.Extensions.AI.Abstractions`.

#### Callers

`MemoryPack.CreateTools` constructs the tool first in the family's published order.
