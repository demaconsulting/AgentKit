### MemoryRecallTool

![AgentKit Tools Memory Structure](MemoryView.svg)

The `MemoryRecallTool` class is a public static modeled unit publishing the `memory_recall` tool.

#### Purpose

To find the memories whose descriptors are closest to a question and return each of them whole. It
is the only unit that reads memories back to the model, and therefore the only place the
descriptor/payload split pays off.

#### Data Model

The class is stateless. A constructed tool captures the store, the embedding generator and the
author's controls it was given. One constant is published: `ToolName`, `memory_recall`.

There is deliberately no count parameter on the tool. How many memories come back is
`MemoryOptions.RecallCount`, which the author configures once against their own context budget; a
model that could raise it would be spending a budget it cannot see.

#### Key Methods

##### Create(IMemoryStore store, IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt; generator, MemoryOptions options)

Creates the tool over one composition's store.

**Preconditions:** all three arguments are non-null.

**Algorithm:** validates each collaborator, then builds the tool through `GuardedToolFactory.Create`
from a delegate declared to return `Task<object>`. The delegate takes one optional `query` parameter
and a cancellation token.

**Postconditions:** an `AIFunction` carrying `ToolName` and a description. Internal, because a pack
is the unit of attachment.

##### RecallAsync(...)

Searches the store.

**Preconditions:** none; a malformed request is refused rather than thrown.

**Algorithm:** refuse a missing query; ask the store how many memories it holds and answer with an
empty match list if none; otherwise embed the query through `MemoryEmbedding`, search for
`options.RecallCount` matches, and report them. The empty-store shortcut is not an optimization for
its own sake: the embedding call is the expensive part of a recall, there is nothing for its result
to be compared against, and skipping it is unobservable from outside.

**Postconditions:** a structured result carrying the query, the match count, and each match's
identifier, descriptor, details, source document, source locator and similarity.

##### Report(string query, IReadOnlyList&lt;MemoryMatch&gt; matches)

Renders the matches as the structured result the model reads.

**Preconditions:** none.

**Algorithm:** projects each match into a fielded object and wraps the whole in
`ToolResult.Structured`.

**Postconditions:** the result is structured rather than rendered prose, because whatever shape a
tool emits is the shape the model mirrors back — a list of fielded matches teaches attribution,
where a paragraph teaches blurring. Each match carries its own identifier and provenance so the
model can cite a source, and can name the memory it means in a later update, revision or removal,
without a second call. A memory with no provenance yields no provenance fields, because JSON
serialization drops nulls; the absence of a citation is therefore visible as the absence of a field.

#### Error Handling

A missing query is a returned refusal naming `InvalidRequest`. Finding nothing is not an error and
is not refused: an empty store and a query nothing matches both return an empty match list, because
a refusal would invite the model to conclude the tool is broken at exactly the moment the store is
legitimately empty. An embedding backend that produces no vector raises from `MemoryEmbedding` and
is not converted into a refusal.

#### Dependencies

`IMemoryStore`, `MemoryMatch`, `MemoryOptions`, `MemoryEmbedding`, AgentKitCore's `ToolResult` and
`GuardedToolFactory`, and `IEmbeddingGenerator` from `Microsoft.Extensions.AI.Abstractions`.

#### Callers

`MemoryPack.CreateTools` constructs the tool second in the family's published order.
