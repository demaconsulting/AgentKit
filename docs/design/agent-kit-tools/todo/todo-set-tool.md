### TodoSetTool

![AgentKit Tools Todo Structure](TodoView.svg)

The `TodoSetTool` class publishes the `todo_set` tool.

#### Purpose

To write a step into the agent's task list, or advance the step already carrying that identifier.
One call both records a step and advances it: the tool adds a task when the identifier is new and
replaces the existing task's title, status and note in place when the identifier is one the list
already holds. There is no separate add and update, because a model that must know whether an item
already exists before it may record a status will sometimes guess wrong, and the refusal costs a
turn and teaches it nothing.

Writing a step down is planning it, not starting it, so the status defaults to `pending` and the
common case — listing the phases of a job up front — needs no status argument at all.

Attaching this tool is not enough. The application must instruct the agent to use the family, and
`TodoPack.SuggestedInstruction` carries the wording that was measured; the subsystem design
document carries the measurements. An application that attaches the tool without such an
instruction will observe that the model mostly does not call it.

#### Data Model

The class is static and holds no state. A constructed tool captures only the store it was given.

| Member        | Type       | Invariant                                              |
| ------------- | ---------- | ------------------------------------------------------ |
| `ToolName`    | `string`   | `todo_set`; public constant; carries the prefix        |
| Text result   | `string`   | `Task '<id>' is <status>. The list now has N item(s).` |

The result's noun agrees with the count, because a result that reads `1 items` is the kind of
small wrongness that makes a model doubt the rest of the sentence.

#### Key Methods

##### Create(TodoStore store)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment and
the store parameter is how a task list is bound to exactly one agent.

**Preconditions:** `store` is non-null.

**Algorithm:** validates `store`, then builds a guarded synchronous delegate with defaulted `id`,
`title`, `status` and `note` parameters.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and
writes to the supplied store for the rest of its life.

##### The tool delegate: `(string? id = null, string? title = null, string? status = null, string? note = null)`

**Algorithm**, in this order:

1. An absent, empty or whitespace `id` is refused as `InvalidRequest`.
2. An absent, empty or whitespace `title` is refused as `InvalidRequest`.
3. An omitted or whitespace `status` is recorded as `pending`; any other value that is not one of
   the four published statuses is refused as `InvalidRequest` and the refusal names the permitted
   vocabulary.
4. A whitespace `note` is treated as no note.
5. `store.Set(id, title, status, note)` is called, adding the item at the end when the identifier
   is new and replacing the existing item at its position when the identifier is one the list
   already holds.
6. The result is a plain text sentence stating the identifier, the recorded status and the size of
   the list, so the model does not have to call `todo_list` to learn what its own write did.

##### Count(int count)

Renders a task count as the phrase a result sentence ends with — `1 item` or `N items` — under the
invariant culture, so a status message reads the same in every environment.

#### Error Handling

Every condition a model controls produces a returned refusal rather than an exception, because an
exception raised during a tool call ends the agent's turn and strands it. The only exception the
unit raises is `ArgumentNullException` for a null store at construction.

An unknown status arriving at the store is a defect in this library rather than something a model
did; the tool refuses an unrecognized status before the store is reached, and the store's own
`ArgumentException` on such an input is the belt-and-braces check that this ordering is honored.

#### Dependencies

`TodoStore` for the list, `TodoStatuses` for the vocabulary, `ToolResult` for text and denied
results, and `GuardedToolFactory` for construction. From `Microsoft.Extensions.AI.Abstractions` it
uses `AIFunction`.

#### Callers

`TodoPack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. `TodoRemoveTool` reuses `Count` so the size phrase
in a remove result matches the size phrase in a set result.
