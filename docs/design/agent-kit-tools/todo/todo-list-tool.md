### TodoListTool

![AgentKit Tools Todo Structure](TodoView.svg)

The `TodoListTool` class publishes the `todo_list` tool.

#### Purpose

To report the agent's task list as a structured object, so the model can read individual fields
back — an identifier it will pass to `todo_set`, a status it will advance — without re-parsing its
own plan. The list is flat and its order is its sequence, so a reader learns what to do next from
the order of the items and from their statuses, and from nothing else.

The tool takes no arguments. A filter by status was considered and rejected: the lists an agent
keeps are short, so filtering would save no context worth the extra argument the model has to
reason about, and a filtered view is exactly how an item gets forgotten.

#### Data Model

The class is static and holds no state. A constructed tool captures only the store it was given.

| Member            | Type     | Invariant                                             |
| ----------------- | -------- | ----------------------------------------------------- |
| `ToolName`        | `string` | `todo_list`; public constant; carries the prefix      |
| Structured result | object   | `itemCount` and an ordered `items` array              |

The structured result has `itemCount` and `items`. Each item has `id`, `title`, `status` and
`note`. An empty list is reported as an empty result, not refused.

#### Key Methods

##### Create(TodoStore store)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment and the
store parameter is how a task list is bound to exactly one agent.

**Preconditions:** `store` is non-null.

**Algorithm:** validates `store`, then builds a guarded synchronous delegate that takes no
parameters.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and
reads the supplied store for the rest of its life.

##### The tool delegate: `()`

**Algorithm:** takes a snapshot of the store's items and returns a structured `ToolResult` naming
`itemCount` and an ordered `items` array whose entries mirror the store's field names. Structured
rather than rendered text, because the model has to read individual fields back.

**Postconditions:** the returned result is a live view of the list at the moment of the call;
because the store returns a snapshot, a concurrent write cannot mutate the reported list under the
reader.

#### Error Handling

The tool consults no policy and touches no file, so there is no file-system failure to classify.
An empty list is a legitimate answer and is returned as an empty result rather than a refusal,
because reporting "no items" as a denial would tell the model something untrue and invite it to
conclude the tool is broken. The only exception the unit raises is `ArgumentNullException` for a
null store at construction.

#### Dependencies

`TodoStore` for the list, `ToolResult` for the structured result, and `GuardedToolFactory` for
construction. From `Microsoft.Extensions.AI.Abstractions` it uses `AIFunction`.

#### Callers

`TodoPack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into.
