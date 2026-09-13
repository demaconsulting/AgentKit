### MemoryUpdateTool

![AgentKit Tools Memory Structure](MemoryView.svg)

The `MemoryUpdateTool` class is a public static modeled unit publishing the `memory_update` tool.

#### Purpose

To replace a memory's details while leaving its descriptor, its provenance and its vector exactly as
they were. It exists as a unit separate from `MemoryReviseTool` because re-embedding is the
expensive half of a correction and most corrections do not need it.

#### Data Model

The class is stateless. A constructed tool captures only the store. One constant is published:
`ToolName`, `memory_update`.

The unit takes no embedding generator. That is not an omission but its defining property: an update
embeds nothing, and a unit holding a generator it never used would invite a later change to start
using it.

#### Key Methods

##### Create(IMemoryStore store)

Creates the tool over one composition's store.

**Preconditions:** `store` is non-null.

**Algorithm:** validates the store, then builds the tool through `GuardedToolFactory.Create` from a
delegate declared to return `Task<object>`, taking optional `memoryId` and `details` parameters and
a cancellation token.

**Postconditions:** an `AIFunction` carrying `ToolName` and a description. Internal, because a pack
is the unit of attachment.

##### UpdateAsync(...)

Replaces one memory's details.

**Preconditions:** none; every malformed request is refused rather than thrown.

**Algorithm:** refuse a missing identifier through `MemoryDenials.MissingIdentifier`; refuse missing
details; find the memory and, on a miss, refuse through `MemoryDenials.NotFoundAsync`; otherwise
re-state the memory as `existing with { Details = details }` and replace it in the store. Carrying
everything else across in one expression is what makes a half-applied update unrepresentable.

**Postconditions:** the memory holds the new details and is otherwise byte-for-byte what it was,
including its vector. The result reports `updated: true`, the identifier, the descriptor the memory
is still filed under, whether it carries a source at all, and that source. Those are the facts most
likely to be wrong in the model's own head after an update. The `sourceStated` flag is not
redundant: JSON serialization drops a null field, and an absent field reads identically to one the
reader did not notice.

#### Error Handling

A missing identifier, missing details and an identifier the store does not hold are all returned
refusals. The not-found refusal states the identifier and how many memories are held and stops
there: it does not list the identifiers that do exist, because a store may hold thousands of
machine-assigned ones, and it prescribes no remedy, because a refusal that prescribes is a
demonstration the model imitates. Both refusals are composed by `MemoryDenials` so that this unit,
`MemoryReviseTool` and `MemoryForgetTool` cannot drift into three wordings for one mistake.

#### Dependencies

`IMemoryStore`, `MemoryRecord`, `MemoryDenials`, and AgentKitCore's `ToolResult` and
`GuardedToolFactory`.

#### Callers

`MemoryPack.CreateTools` constructs the tool third in the family's published order.
