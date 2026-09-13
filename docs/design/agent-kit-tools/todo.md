## Todo

![AgentKit Tools Todo Structure](TodoView.svg)

The Todo subsystem is the todo tool family: the pack an application attaches to give an agent a
task list it can write down, advance and close out.

### Overview

The subsystem's responsibility is to hold, for one agent, a flat list of the steps it is doing —
each with an identifier the agent chose, a one-line title, a status and an optional note — and to
publish exactly three tools over that list: one that reports it, one that adds a step or advances an
existing one, and one that drops a step. The list is state, not content: it lives in memory for the
lifetime of the tools that were built from it and belongs to that agent alone.

The boundary is deliberately narrow. The family carries no path, consults no policy decision and
touches no file. Its whole purpose is to keep a plan visible where a model can read it back and
where a user can watch it advance, because a five-phase job the model tries to remember by itself is
a job that quietly loses its fourth phase.

The subsystem contains five units:

| Unit             | Responsibility                                                            |
| ---------------- | ------------------------------------------------------------------------- |
| `TodoStore`      | Holds one agent's flat task list; internal, allocated per composition     |
| `TodoListTool`   | Publishes `todo_list`: reports the list in the order it was written down  |
| `TodoSetTool`    | Publishes `todo_set`: adds a step, or updates one already carrying its id |
| `TodoRemoveTool` | Publishes `todo_remove`: drops a step that is no longer part of the work  |
| `TodoPack`       | Publishes the three tools as one family under the `todo` prefix           |

### Interfaces

The subsystem exposes exactly one public type, `TodoPack`, plus the name constant each tool unit
publishes and the `TodoPack.SuggestedInstruction` string. Each tool's factory is `internal`, and
`TodoStore` is `internal`, so a tool cannot be obtained except through the pack that claims the
family prefix — and the store one composition holds is not reachable from another composition's
tools.

| Interface                       | Direction | Format                       | Constraints                            |
| ------------------------------- | --------- | ---------------------------- | -------------------------------------- |
| `TodoPack`                      | Outbound  | AgentKitCore `IToolPack`     | Prefix `todo`; no capability           |
| `Todo*Tool.ToolName`            | Outbound  | `string` constant            | The name each tool is published under  |
| `TodoPack.SuggestedInstruction` | Outbound  | `string` constant            | Instruction text for the agent         |
| `TodoStore`                     | Internal  | Flat task list store         | One instance per tool composition      |
| `PathPolicy`                    | Inbound   | AgentKitCore policy object   | Accepted at construction and ignored   |

The subsystem consumes `PathPolicy`, `ToolResult`, `GuardedToolFactory`, `IToolPack` and
`HostCapabilities` from AgentKitCore, and `AIFunction` from
`Microsoft.Extensions.AI.Abstractions` reached through Core. It exposes no content model another
package depends on and never reads or writes a file.

### Design

**Construction.** `TodoPack.CreateTools` receives the composition's policy, validates it, allocates
one fresh `TodoStore`, and calls each unit's internal factory over that store. Every factory
validates the store, then builds its tool through `GuardedToolFactory.Create`, capturing the store
in the tool's delegate. The policy is validated even though the family never consults it, so that a
composing application that forgot one is told at the point it made the mistake rather than by a
sibling family later. There is no other construction path and no setter, so a tool that could reach
into another agent's list is unrepresentable rather than merely discouraged.

**Fixed tool order.** The pack returns the three tools in the order a model should learn them:
`todo_list`, `todo_set`, then `todo_remove`. Reading the list is the first thing a model does with
the family, writing to it is the second, and removing from it is the last and least common. The
order is observable in tool selection, so it is a contract rather than an incidental collection
order.

**One list per agent, and belonging to that agent alone.** A fresh `TodoStore` is allocated inside
`CreateTools` — as a local, never a field on the pack and never a parameter accepted from outside —
and shared only among the three tools that method returns. A delegated agent, whose tools are
composed by a separate `CreateTools` call, therefore gets a separate store. Because `TodoStore` is
internal and no public type accepts one, there is no way to express handing one agent's list to
another agent's tools. This is the analogue of how the TextFile subsystem allocates one cut/paste
buffer per composition.

**The list is flat: a finding rather than a simplification.** There is no tree, no nesting, no
dependency edge and no replace-the-whole-list tool. Each was built and measured before being
removed. A nested list was redundant because delegation already expresses hierarchy: a parent's
single "delegate an inspection" step became the sub-agent's own four-phase list, and the tree the
nested model was meant to express was already being expressed by delegation. A `blockedBy` edge was
offered prominently in the tool description alongside a strong tracking instruction, and was used
zero times in three runs while costing fourteen todo calls against ten without it. A
replace-the-whole-list tool was never called under a weak prompt while incremental updates worked
perfectly under a strong one, so the apparent difference was instruction strength and not tool
shape. Sequence is carried by list order and by status; nothing else survived measurement.

**Set adds or updates in place, keyed by id.** The set tool adds a task when the identifier is new
and replaces the task's title, status and note when the identifier already exists, without moving
it. Order is the plan, so a status change must not reorder the plan under it. Making set do both
operations means a model that does not know whether it wrote a step down before never has to
guess: writing a step down and advancing it are the same call.

**The status vocabulary is exactly four values, compared ordinally.** `pending`, `in_progress` and
`done` are the life cycle of a step; `blocked` is separate from `pending` because a step nobody can
reach is not the same as a step nobody has started, and a model with no way to say so either leaves
the item `in_progress` forever or quietly deletes it. There is no `cancelled` or `deferred`: a step
that is no longer part of the work is removed. A model writing `In_Progress` is refused rather than
silently corrected, because silently accepting a variant teaches the model a vocabulary this
library does not publish.

**Results reduce the next question.** Set returns `Task 'phase2' is in_progress. The list now has 5
items.` and remove returns `Task 'phase2' was removed. The list now has 1 item.`, so the model does
not have to call `todo_list` to learn what its own write did. Singular and plural are distinguished
because a result that reads `1 items` is the kind of small wrongness that makes a model doubt the
rest of the sentence.

**Refusals are results, and state facts about the list.** A missing argument, an unknown status and
an unknown identifier are all returned refusals rather than exceptions. The unknown-id refusal from
`todo_remove` is `Denied (TargetNotFound): No task carries the id 'phase9'. The list holds 'phase1',
'phase2'.` — a plain statement of fact naming the list's contents, prescribing no other tool and
guessing at no identifier. It is the analogue of the text family's empty-buffer refusal, which
names the slots that do hold content without telling the model which tool to call next.

**Attaching the tools is not enough; the application must instruct the agent to use them.** This is
the single most important thing about the family, and it is a measured finding rather than a matter
of taste. Against a five-phase task, a soft instruction — "keep track of multi-step work using your
task list so progress is visible" — produced use in 1 of 5 runs, and the single run that used the
tools left an item stranded `in_progress`. An explicit instruction produced use in 3 of 3 runs with
all five phases recorded and closed and near-identical behavior each time. The wording that
produced the 3-of-3 result is published as `TodoPack.SuggestedInstruction` so an application can
append it rather than transcribe it, and so the text that was measured cannot drift from the text
that ships. The library deliberately does not inject the instruction automatically: composing tools
is what this library does, and silently editing an agent's system prompt is exactly the kind of
invisible behavior an application author cannot audit.

**No host capability is required.** An in-memory task list asks nothing of the host, so
`TodoPack.RequiredCapabilities` is `HostCapabilities.None` and every composition receives the
family rather than gating it behind a declaration an application would have to know to make.
