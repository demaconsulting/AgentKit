### MemoryForgetTool

![AgentKit Tools Memory Structure](MemoryView.svg)

The `MemoryForgetTool` class is a public static modeled unit publishing the `memory_forget` tool.

#### Purpose

To remove one named memory permanently. It is the only way a memory leaves the store.

#### Data Model

The class is stateless. A constructed tool captures only the store. One constant is published:
`ToolName`, `memory_forget`.

The tool takes exactly one identifier and nothing else. There is no clear-all, no forget-by-query
and no expiry: a bulk removal is an operation whose blast radius the model cannot see before it acts
— a query matching more than it meant to erases evidence that nothing will report as missing — and an
expiry policy would be this library deciding how long an author's facts stay true. What protects an
author beyond that is that the family is attached as a whole: an application unwilling to let an
agent forget anything composes without this pack.

#### Key Methods

##### Create(IMemoryStore store)

Creates the tool over one composition's store.

**Preconditions:** `store` is non-null.

**Algorithm:** validates the store, then builds the tool through `GuardedToolFactory.Create` from a
delegate declared to return `Task<object>`, taking one optional `memoryId` parameter and a
cancellation token.

**Postconditions:** an `AIFunction` carrying `ToolName` and a description. The description states
plainly that the removal is permanent rather than guarding against it.

##### ForgetAsync(...)

Removes one memory.

**Preconditions:** none; every malformed request is refused rather than thrown.

**Algorithm:** refuse a missing identifier through `MemoryDenials.MissingIdentifier`; ask the store
to remove the identifier and, on a miss, refuse through `MemoryDenials.NotFoundAsync`; otherwise
read the new count and report.

**Postconditions:** exactly one memory is gone and the result reports `forgotten: true`, the
identifier and the size of the store, so the model never has to recall to find out what its own
removal did.

#### Error Handling

A missing identifier and an identifier the store does not hold are returned refusals. A miss is
never reported as a quiet success: a model told that a removal succeeded when nothing was removed
goes on believing a fact is gone from a store that still returns it on the next recall. The
not-found refusal states the identifier and how many memories are held and prescribes nothing.

#### Dependencies

`IMemoryStore`, `MemoryDenials`, and AgentKitCore's `ToolResult` and `GuardedToolFactory`.

#### Callers

`MemoryPack.CreateTools` constructs the tool last in the family's published order.
