### MemoryOptions

![AgentKit Tools Memory Structure](MemoryView.svg)

The `MemoryOptions` class is a public modeled unit holding the controls an application author sets
over the memory family.

#### Purpose

To carry the two numbers the family's behavior depends on — the near-duplicate threshold and the
recall count — as one immutable object the author constructs once and the pack hands to the tools
that need it. The type exists so that neither number is a constant buried in a tool: AgentKit
guarantees that every file call compares against what the store holds when it runs and that a recall
is bounded, and the author
decides at what values.

#### Data Model

The class is sealed and public. Instances are immutable after construction.

| Member                   | Type            | Invariant                                  |
| ------------------------ | --------------- | ------------------------------------------ |
| `NearDuplicateThreshold` | `double`        | A number in the inclusive range 0.0 to 1.0 |
| `RecallCount`            | `int`           | Not negative                               |
| `Default`                | `MemoryOptions` | A shared instance carrying both defaults   |

Two constants are published: `DefaultNearDuplicateThreshold` (0.88) and `DefaultRecallCount` (5).
The threshold is a measured starting point — over a seventeen-document technical corpus, two
contradicting statements of one fact scored 0.965 cosine while unrelated statements sat well below
0.88 — not a constant of nature; an author whose corpus is narrower should expect to raise it. The
recall count reflects that each match returns a whole detail payload and therefore costs real
context. Both values are provisional and are to be confirmed by the repository owner before the
first tagged release.

`Default` is a shared instance rather than a factory method, because the type is immutable and
sharing it is therefore free of risk.

#### Key Methods

##### MemoryOptions(double nearDuplicateThreshold, int recallCount)

Constructs the controls.

**Preconditions:** `nearDuplicateThreshold` is a number in the inclusive range 0.0 to 1.0;
`recallCount` is not negative. Both parameters are optional and default to the published constants,
so one control may be replaced without restating the other.

**Algorithm:** validates both values before any assignment, so a rejected instance never exists even
briefly, then assigns. A NaN threshold is rejected explicitly and first: it compares false against
everything, so accepting it would silently disable near-duplicate detection rather than announcing
that the configuration was wrong.

**Postconditions:** the instance carries exactly the values stated. The extremes are held as stated
and are not second-guessed: 1.0 admits everything that is not a numerically exact match, 0.0 treats
the nearest memory as a duplicate of anything, and a recall count of zero returns no matches. All
three are legitimate author configurations.

#### Error Handling

An out-of-range threshold, a NaN or infinite threshold, and a negative recall count are all reported
by `ArgumentOutOfRangeException`. They are configuration errors in the composing application's own
code rather than runtime conditions a model can provoke, so they are surfaced rather than clamped: a
clamped control is one the author believes they set and did not.

#### Dependencies

The unit depends only on the Base Class Library.

#### Callers

`MemoryPack` accepts an instance or substitutes `Default`, exposes it as `MemoryPack.Options`, and
passes it to `MemoryFileTool` and `MemoryRecallTool`. `MemoryUpdateTool`, `MemoryReviseTool` and
`MemoryForgetTool` do not take it, because none of their behavior depends on either control.
