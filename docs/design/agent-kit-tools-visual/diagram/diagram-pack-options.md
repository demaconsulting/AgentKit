### DiagramPackOptions

![AgentKit Tools Visual Diagram Structure](DiagramView.svg)

The `DiagramPackOptions` record carries the options a diagram pack is given: the rendering bounds
its drawings observe and the diagram languages it offers.

#### Purpose

A host decides once which languages its agent may draw in and how its drawings are bounded. Holding
both on one validated record means the pack, both of its tools and the help they serve apply the
same choice, and a value the pack could not apply is refused where the host set it.

#### Data Model

The record is sealed and immutable; it is safe to share between packs and threads.

| Member | Type | Default | Rule |
| --- | --- | --- | --- |
| `Default` (static) | `DiagramPackOptions` | every default below | — |
| `Visual` | `VisualOptions` | `VisualOptions.Default` | not null |
| `Formats` | `IReadOnlyList<string>` | all five known languages, in order | one or more known, each once |

The preview mode is deliberately not a member: `DiagramPack.For` chooses it from the host, and as an
option it would have to override a value the caller set.

#### Key Methods

##### Visual init accessor

Refuses `null` with `ArgumentNullException`.

##### Formats init accessor

Refuses `null` with `ArgumentNullException`, copies the list, and puts the copy in canonical form:
each name is matched case-insensitively, ignoring surrounding white space, against the known
languages and stored in lower case. An empty list, an unknown or null name, or a name listed twice
is refused with `ArgumentException` naming `Formats` and, for an unknown name, listing the known
ones. The copy is exposed through `Array.AsReadOnly`, so a change to the caller's list afterwards
changes nothing here and the exposed list cannot be changed through itself.

#### Error Handling

`ArgumentNullException` and `ArgumentException` from the `init` accessors. Nothing else can fail.

#### Dependencies

`VisualOptions`; `DiagramFormats` for the known languages.

#### Callers

A host constructs one and passes it to `DiagramPack` or `DiagramPack.For`.
