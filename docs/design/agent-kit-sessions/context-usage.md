## ContextUsage

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The `ContextUsage` unit publishes the one shape both provider families are reduced to, and
`IContextUsageReporter`, the optional contract a provider session implements when it can account for
its own window.

### Purpose

Providers disagree about what they will tell us. A GitHub Copilot session reports its current token
count and its token limit after every turn. A provider reached through an `IChatClient` reports
neither, and the window size has to be configured by the application instead. The compaction engine
cannot have two behaviors, so both shapes are reduced to this one figure.

`Origin` is not decoration. A provider-reported figure counts what the provider actually charged,
including framing this library never sees; an estimated figure is derived from a character ratio and
is only as good as that ratio. Surfacing the difference lets an application log it, and lets a test
assert that a provider's own numbers were preferred when they were available.

`IContextUsageReporter` is deliberately **separate and optional**. Requiring every provider session
to report usage would force an adapter for a provider that reports nothing to invent a number, and
an invented number is indistinguishable from a real one at the point it is consumed. Keeping the
capability in its own interface lets an adapter say "I do not know" by simply not implementing it.

### Data Model

`ContextUsage` properties, immutable after construction:

- **`UsedTokens`** (`int`) — Not negative. Counts everything the provider holds: system prompt, tool declarations
  and conversation
- **`WindowTokens`** (`int`) — Positive
- **`Origin`** (`ContextUsageOrigin`) — `Provider` or `Estimated`
- **`FreeTokens`** (`int`) — Derived: `Max(0, WindowTokens - UsedTokens)`
- **`UsedFraction`** (`double`) — Derived: `UsedTokens / WindowTokens`; may exceed one

`ContextUsageOrigin` values: `Provider` (the provider reported the figure) and `Estimated` (derived
from this library's own transcript because the provider reports nothing).

`IContextUsageReporter` exposes `CurrentUsage`, a `ContextUsage?` read after every turn. An
implementation must not contact the provider to answer: it reports what the last exchange already
revealed, so reading it is free and cannot fail.

**Usage is permitted to exceed the window.** A provider may report that, and clamping it would hide
exactly the condition an application most needs to see. `FreeTokens` floors at zero because a
window that is over-full has no negative amount of room, but neither `UsedTokens` nor `UsedFraction`
is clamped.

### Key Methods

#### ContextUsage(int usedTokens, int windowTokens, ContextUsageOrigin origin)

Validates before any assignment so an unusable figure never exists even briefly. A negative
occupied count could only come from a defect and would make every threshold comparison meaningless;
a window of zero would leave nothing for the occupied fraction to be a fraction of.

#### FromProvider(int usedTokens, int windowTokens)

Creates a figure marked `ContextUsageOrigin.Provider`. Used by an adapter reporting its provider's
own account.

#### FromEstimate(int usedTokens, int windowTokens)

Creates a figure marked `ContextUsageOrigin.Estimated`. Used by `CompactingAgentSession` when the
live provider session offers nothing.

### Error Handling

- **Negative occupied count** — `ArgumentOutOfRangeException` propagates
- **Non-positive window size** — `ArgumentOutOfRangeException` propagates
- **Provider cannot account for its window** — Reported as `null` from `CurrentUsage`; not an error

The last row is the important one: not knowing is a legitimate answer, expressed as an absence
rather than as a fabricated figure or an exception.

### Dependencies

None. The class performs arithmetic on integers and holds no references.

### Callers

`CompactingAgentSession` reads `CurrentUsage` from its live provider session after every turn,
preferring it over its own estimate, and publishes the result as `IAgentSession.Usage` and on every
`AgentSessionResponse`. `InMemoryProviderSession` implements `IContextUsageReporter` and can be
configured either to report or to report nothing, so both engine paths are reachable without a live
model.
