### TodoRemoveTool

![AgentKit Tools Todo Structure](TodoView.svg)

The `TodoRemoveTool` class publishes the `todo_remove` tool.

#### Purpose

To drop a step from the agent's task list when that step is no longer part of the work. A step the
agent has finished is not removed; it is marked `done` with `todo_set` and stays in the list,
because the list is how a person following along sees what was accomplished. This tool exists for
the case where a plan turns out to contain a step that should never have been in it.

Attaching this tool is not enough. The application must instruct the agent to use the family, and
`TodoPack.SuggestedInstruction` carries the wording that was measured; the subsystem design
document carries the measurements.

#### Data Model

The class is static and holds no state. A constructed tool captures only the store it was given.

| Member        | Type       | Invariant                                                |
| ------------- | ---------- | -------------------------------------------------------- |
| `ToolName`    | `string`   | `todo_remove`; public constant; carries the prefix       |
| Text result   | `string`   | `Task '<id>' was removed. The list now has N item(s).`   |

The result's noun agrees with the count, using the same helper as `TodoSetTool`.

#### Key Methods

##### Create(TodoStore store)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment and
the store parameter is how a task list is bound to exactly one agent.

**Preconditions:** `store` is non-null.

**Algorithm:** validates `store`, then builds a guarded synchronous delegate with a defaulted `id`
parameter.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and
removes from the supplied store for the rest of its life.

##### The tool delegate: `(string? id = null)`

**Algorithm**, in this order:

1. An absent, empty or whitespace `id` is refused as `InvalidRequest`.
2. `store.Remove(id, out remaining)` is called.
3. On a hit, the result is a plain text sentence stating the identifier that was removed and the
   size of the list, so the model does not have to call `todo_list` to learn what its own write
   did.
4. On a miss, the tool takes `store.Identifiers()` and returns a `TargetNotFound` refusal. When
   the list is empty, the refusal reads `No task carries the id '<id>'. The list is empty.`; when
   the list holds other identifiers, the refusal reads `No task carries the id '<id>'. The list
   holds '<first>', '<second>'.`. It prescribes no other tool and guesses at no identifier.

The empty-list message names the fact rather than an empty collection, because a message that ends
with `The list holds ''` is the kind of small wrongness that makes a model doubt the rest of the
sentence.

#### Error Handling

Every condition a model controls produces a returned refusal rather than an exception. A miss is a
returned `TargetNotFound` refusal that states the fact and names the list's contents; it is the
analogue of the text family's paste refusal, which names the buffer slots that do hold content
when the requested slot is empty. Neither refusal prescribes another tool — it is not this
library's job to author an agent's next step, only to state what happened.

The only exception the unit raises is `ArgumentNullException` for a null store at construction.

#### Dependencies

`TodoStore` for the list, `ToolResult` for text and denied results, `TodoSetTool.Count` for the
size phrase, and `GuardedToolFactory` for construction. From
`Microsoft.Extensions.AI.Abstractions` it uses `AIFunction`.

#### Callers

`TodoPack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into.
