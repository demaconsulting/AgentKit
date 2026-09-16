## AgentSessionOptions

### Purpose

`AgentSessionOptions` captures everything an application configures for one compacting session:
instructions, tools, compaction policy and summarizer. It also measures the fixed overhead the
context carries on every turn.

### Data Model

Immutable properties:

- **`Summarizer`** (`ISummarizer`) — Required out-of-session consolidation implementation.
- **`Instructions`** (`string?`) — System instructions, or null for none.
- **`Tools`** (`IReadOnlyList<AIFunction>`) — Owned read-only copy of the tool declarations.
- **`Compaction`** (`CompactionPolicy`) — Policy, defaulting to `CompactionPolicy.Default`.
- **`VerbatimTurns`** (`int`) — Convenience access to `Compaction.VerbatimTurns`.
- **`SystemTokens`** (`int`) — Estimated instruction size.
- **`ToolDeclarationTokens`** (`int`) — Estimated tool declaration size.
- **`FixedOverheadTokens`** (`int`) — Estimated system plus tool overhead.

The provider's context window is deliberately not configured here. It is a fact about the provider,
so it is stated where the provider is constructed and answered by the adapter thereafter; a second
copy here would give one fact two sources and the session a rule for deciding which to believe.

### Key Methods

#### The AgentSessionOptions Constructor

**Purpose:** Validate application configuration and measure the fixed overhead the seed carries.

**Algorithm:**

1. Require a summarizer.
2. Select the supplied `CompactionPolicy` or the shared default.
3. Estimate instruction and tool declaration tokens with `TokenEstimator`.
4. Reject null tool entries or declaration totals no token count can represent.
5. Copy the tool list into owned read-only storage and store the measured figures.

**Preconditions:** The summarizer is not null; the tool list contains no null entry.

**Postconditions:** The instance is immutable, and the overhead figures describe the declarations the
options now own rather than a list the caller can still change.

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
- **Null tool entry** — `ArgumentException` propagates from tool declaration estimation.
- **Tool declarations too large to count** — `ArgumentException` propagates.

The unit intentionally does not predict whether a future conversation will settle, and it refuses no
window because it is given none. Pressure is answered at runtime, in counts of turns and slots, once
real material exists.

### Dependencies

- **ISummarizer** — Required collaborator; see _Summarizer Unit Design_.
- **CompactionPolicy** — Supplies `VerbatimTurns`; see _CompactionPolicy Unit Design_.
- **TokenEstimator** — Estimates instructions and tool declarations; see _TokenEstimator Unit Design_.
- **ContextLayout** — Defines the internal 0.70 rotation threshold.
- **Microsoft.Extensions.AI.Abstractions** — Supplies `AIFunction`.

### Callers

Applications construct `AgentSessionOptions` before creating a `CompactingAgentSession`.
`CompactingAgentSession` reads the stored values during creation and rotation.
