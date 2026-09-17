## AgentSessionOptions

![AgentKit Core Structure](AgentKitCoreView.svg)

### Purpose

`AgentSessionOptions` captures everything an application configures for one compacting session:
instructions, tools, the summarizer, and how many of the most-recent turns are kept verbatim.

### Data Model

Immutable properties:

- **`Summarizer`** (`ISummarizer`) — Required out-of-session consolidation implementation.
- **`Instructions`** (`string?`) — System instructions, or null for none.
- **`Tools`** (`IReadOnlyList<AIFunction>`) — Owned read-only copy of the tool declarations.
- **`VerbatimTurns`** (`int`) — Maximum most-recent turns kept word for word, defaulting to
  `DefaultVerbatimTurns`.

Published constant:

- **`DefaultVerbatimTurns`** (`int`) — Twenty turns, the tail length an application receives when it
  configures none.

The provider's context window is deliberately not configured here. It is a fact about the provider,
so it is stated where the provider is constructed and answered by the adapter thereafter; a second
copy here would give one fact two sources and the session a rule for deciding which to believe.

Nothing is measured here. The options carry what the application stated and no token figure derived
from it, because the only token figures this system uses come back from a provider session that
counted them.

### Key Methods

#### The AgentSessionOptions Constructor

**Purpose:** Validate application configuration and own the tool declarations every rotation seeds.

**Algorithm:**

1. Require a summarizer.
2. Require a positive verbatim tail length.
3. Reject a null tool entry.
4. Copy the tool list into owned read-only storage.

**Preconditions:** The summarizer is not null; the tool list contains no null entry; the verbatim
tail length is positive.

**Postconditions:** The instance is immutable, and a caller that adds a tool afterwards cannot change
what a rotation seeds.

#### RotationThresholdFor(int effectiveWindowTokens)

**Purpose:** Compute the conversation occupancy at which a session rotates for one effective window.

**Algorithm:** Multiply the effective window by the internal 0.70 threshold, truncate to an integer,
and return at least one token.

**Preconditions:** The effective window supplied by the caller is positive.

**Postconditions:** The returned threshold is never below one token, so a fraction too small to
survive truncation still means "rotate on every turn" rather than "rotate a conversation holding
nothing". `CompactingAgentSession` supplies the window the provider reported, less the overhead that
same reading credited, so the arithmetic is published once and applied in one currency.

### Error Handling

- **Null summarizer** — `ArgumentNullException` propagates.
- **Null tool entry** — `ArgumentException` propagates.
- **Non-positive verbatim tail** — `ArgumentOutOfRangeException` propagates.

The unit intentionally does not predict whether a future conversation will settle, and it refuses no
window because it is given none. Pressure is answered at runtime, in counts of turns and slots, once
real material exists.

### Dependencies

- **ISummarizer** — Required collaborator; see _Summarizer Unit Design_.
- **ContextLayout** — Defines the internal 0.70 rotation threshold.
- **Microsoft.Extensions.AI.Abstractions** — Supplies `AIFunction`.

### Callers

Applications construct `AgentSessionOptions` before creating a `CompactingAgentSession`.
`CompactingAgentSession` reads the stored values during creation and rotation.
