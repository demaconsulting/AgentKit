### TodoStore

![AgentKit Tools Todo Structure](TodoView.svg)

The `TodoStore` class is an internal modeled unit that holds one agent's flat task list, shared by
the `todo_list`, `todo_set` and `todo_remove` tools of one composition.

#### Purpose

To hold the steps a model wrote down, in the order it wrote them, so that each of the three tools
in the composition reads and writes the same list. The store is not a user-facing tool and does not
publish itself: it exists so the family has one place to keep its state and so a delegated agent's
list cannot reach into its parent's.

The store is deliberately flat and deliberately in memory. A tree, a `blockedBy` edge and a
replace-the-whole-list operation were all built and measured before being removed; the design
document for the subsystem carries the measurements. A task list is the working state of one agent
run, not a document; when the composition is discarded, so is the list.

#### Data Model

The class is sealed and internal. One instance is allocated by `TodoPack.CreateTools` for each
composition and shared by that composition's list, set and remove tools.

| Member   | Type             | Invariant                                                     |
| -------- | ---------------- | ------------------------------------------------------------- |
| `_gate`  | `object`         | Lock guarding all access to `_items`                          |
| `_items` | `List<TodoItem>` | Insertion order preserved; identifiers unique inside the list |

The `TodoItem` record carries the identifier, title, status and note. It is a record rather than a
class so the store can hand an item out without any caller being able to alter what the store
holds. The `TodoStatuses` type publishes the four permitted status strings — `pending`,
`in_progress`, `done` and `blocked` — in life-cycle order, and reports whether a candidate string
is recognized under an ordinal comparison. Identifiers, titles and statuses are compared ordinally,
so an identifier means exactly itself.

#### Key Methods

##### Set(string id, string title, string status, string? note)

Adds a task or updates the one already carrying the identifier.

**Preconditions:** `id` and `title` are non-null and non-empty; `status` is one of
`TodoStatuses.All`; the caller — the tool — is expected to have refused an unrecognized status
before reaching here.

**Algorithm:** validates the inputs, takes `_gate`, looks the identifier up under an ordinal
comparison, and either appends a new `TodoItem` or replaces the existing one at its position. A
status change never reorders the plan under it.

**Postconditions:** the list holds an item under `id` with the given title, status and note, at
either the position the item already had or at the end of the list; the reported count is the new
size of the list.

##### Remove(string id, out int remaining)

Removes the task carrying an identifier.

**Preconditions:** `id` is non-null and non-empty.

**Algorithm:** validates the identifier, takes `_gate`, looks the identifier up, and removes the
item at that position when one exists. `remaining` is set to the size of the list after the
attempted removal in either case.

**Postconditions:** on success, the list is one item shorter and no longer holds the identifier; on
a miss, the list is unchanged and the caller can turn a miss into a refusal.

##### Items()

Reports the tasks in the order the model wrote them down.

**Preconditions:** none.

**Algorithm:** takes `_gate` and returns a copy of the items list. The items themselves are records
and cannot be altered.

**Postconditions:** the returned collection is a stable snapshot; a later write cannot mutate a
list a caller is still reading.

##### Identifiers()

Lists the identifiers the list currently holds, in list order.

**Preconditions:** none.

**Algorithm:** takes `_gate` and returns the identifiers of the items in order.

**Postconditions:** the returned list names exactly the identifiers the list holds and nothing
else. Deliberately internal rather than a tool of its own, because `todo_list` already reports the
list; this exists so a removal refusal can name what the list does hold when the identifier the
model chose is not one of them.

#### Error Handling

Null or empty identifiers, null or empty titles and unrecognized statuses are programming errors
from the tool units and are reported by argument exceptions. Model-facing validation happens in the
set and remove tools before this unit is called, so a store that receives an unrecognized status is
witnessing a defect in this library rather than something a model did. The lock makes concurrent
calls safe.

#### Dependencies

The unit depends only on Base Class Library collections and locking. It is referenced by
`TodoPack`, `TodoListTool`, `TodoSetTool` and `TodoRemoveTool`.

#### Callers

`TodoPack.CreateTools` constructs one instance per composition. `TodoListTool` calls `Items`;
`TodoSetTool` calls `Set`; `TodoRemoveTool` calls `Remove` and, on a miss, `Identifiers` to name
the list's contents in its refusal.
