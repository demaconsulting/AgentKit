### TextFilePack

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFilePack` class publishes the text file family as one pack.

#### Purpose

To be the one thing an application attaches in order to give an agent text file access. A package
exposes its tools as a pack rather than as individual tools so that attaching it costs one line
rather than one line per tool, and so that exactly one place claims the family prefix the tools'
names are protected by.

The pack is also what makes the family's construction discipline enforceable. Because each tool's
factory is `internal` and this is the only public type in the subsystem, there is no way for an
application to obtain a text file tool that was not created here, from the policy the composition
supplied.

#### Data Model

The class is sealed and holds no state; it is safe for concurrent use.

| Member                    | Type                      | Invariant                                     |
|---------------------------|---------------------------|-----------------------------------------------|
| `FamilyPrefix` (constant) | `string`                  | `text_file`; leads every tool name it creates |
| `IToolPack.FamilyPrefix`  | `string`                  | Reports the constant above                    |
| `RequiredCapabilities`    | `HostCapabilities`        | `None`; every host receives the family        |
| `CreateTools(PathPolicy)` | `IEnumerable<AIFunction>` | Non-null; no null element; three tools        |

The prefix is published both as a constant and through the contract. The constant lets a test or a
composing application name the family without repeating a string literal that could drift from the
names the tools actually carry; the contract member is what `ToolPackBuilder` reads. They cannot
disagree, because the contract member returns the constant. The contract member is implemented
explicitly so that the constant can keep the name the contract uses; the class is sealed, so the
member is not hidden from a derived type.

#### Key Methods

##### RequiredCapabilities

Reports `HostCapabilities.None`. Reading and writing text asks nothing of the model or the
application beyond what every host already provides, so the family is registered by every
composition. A speculative capability requirement would exclude hosts for no reason and force an
operator to discover a setting before ordinary file access works.

##### CreateTools(PathPolicy policy)

Creates the family's tools.

**Preconditions:** `policy` is non-null; it is the policy the composition was built with, not one
the pack invented.

**Algorithm:** validates `policy`, then returns `TextFileReadTool.Create(policy)`,
`TextFileWriteTool.Create(policy)` and `TextFileListTool.Create(policy)` in that order.

**Postconditions:** exactly three tools, non-null, in the stated order, each named
`text_file_`-prefixed and each governed by the supplied policy. The order is fixed rather than
incidental because the order a model sees its tools in is observable.

Called once per `ToolPackBuilder.Build`. Because `RequiredCapabilities` is `None`, it is called for
every host.

#### Error Handling

`ArgumentNullException` is raised for a null policy. This is validated here as well as in each
tool's factory so that the failure names the composing application's mistake at the line that made
it, rather than surfacing from inside a tool the application did not write. A family created without
a policy would hand an agent unrestricted file access while appearing correctly composed, which is
the failure this check exists to make impossible.

Nothing else in the unit can fail. The obligations the pack contract places on it — a non-empty
prefix, a non-null collection, no null element, names within the declared family — are verified by
`ToolPackBuilder.Build`, which throws when one is broken; see *ToolPackBuilder Unit Design*. The
pack is not reachable from a model's tool call, so no runtime refusal arises here.

#### Dependencies

`IToolPack` and `HostCapabilities` for the contract it implements, `PathPolicy` as the argument it
passes on, and the three tool units whose internal factories it calls. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the form each created tool takes.

#### Callers

An application constructs a `TextFilePack` and adds it to a `ToolPackBuilder`; the builder then
calls `CreateTools`. Nothing inside this package calls the unit.
