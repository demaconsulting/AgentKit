### AgentProfile

![AgentKit Tools Agent Structure](AgentView.svg)

The `AgentProfile` class describes one named agent an application is willing to have started on
its behalf.

#### Purpose

To be the one place the application's authorship of what a child agent *is* lives — the
instructions it is told, the names of the tools it may use, an optional narrower set of path
grants than its parent holds, and an optional description shown to the model beside the name. A
parent agent never writes any of these; the model selects a profile by name from a list the
application registered, and everything else is what the profile carries.

The type exists so delegation cannot become a route by which a model grants a second model
behavior the application never sanctioned. If a parent agent could author its child's
instructions, every control the application configured would be one prompt away from being reset.
The one thing the parent supplies is the task, which is data the child works on rather than
authority the child carries.

#### Data Model

The class is sealed and immutable after construction; a profile is safe for concurrent use.

| Member         | Type                     | Invariant                                                    |
|----------------|--------------------------|--------------------------------------------------------------|
| `Name`         | `string`                 | Non-empty; compared ordinally                                |
| `Instructions` | `string`                 | Non-empty; written by the application                        |
| `Description`  | `string?`                | Non-null when supplied; shown to the model beside the name   |
| `Tools`        | `IReadOnlyList<string>`  | Non-null; no null or empty entry; may be empty               |
| `Grants`       | `IReadOnlyList<PathRule>`| Non-null; no null entry; empty when the profile narrows none |

The tool and grant collections are materialized from their source sequences at construction, so a
caller mutating the source afterwards cannot change what the profile admits.

#### Key Methods

##### AgentProfile(name, instructions, tools, grants, description)

Constructs the profile. There is deliberately no setter and no builder: a profile that could be
adjusted after registration could be adjusted by whatever holds a reference to it, and the point
of the type is that the child's instructions have exactly one author.

**Preconditions:** `name` and `instructions` are non-null and non-empty; `tools` is non-null and
contains no null or empty entry; `grants`, if supplied, contains no null entry.

**Algorithm:** validates each argument, materializes `tools` into an internal list, materializes
`grants` — or an empty list when it is `null` — into a second internal list, and captures `name`,
`instructions` and `description`.

**Postconditions:** the profile carries exactly the values the application supplied and neither
list can be replaced or mutated through the type's public surface.

#### Error Handling

`ArgumentException` is raised for a name or instructions that are null or empty, for a tool entry
that is null or empty, and for a null grant entry. `ArgumentNullException` is raised for a null
`tools` collection. Each is a programming error in the composing application — a nameless profile
could not be selected, a silent profile has no job, an empty tool name could never match an
attached tool, and a null grant leaves the child's reach undetermined — and is reported at the
line that made the mistake rather than surfacing as a refusal a model has no way to correct.

The type raises nothing at run time. It is not reachable from a model's tool call.

#### Dependencies

`PathRule` from AgentKitCore, for the grants a profile may narrow to. Nothing else.

#### Callers

An application constructs `AgentProfile` instances and passes them to `AgentPack`. `AgentRunTool`
reads them through the pack's captured list; `AgentPack.CreateTools` validates their grants
against the composition's policy; `AgentPack.ComposeChild` reads their tool names as the filter
over what the packs published. Nothing else in this package constructs the type.
