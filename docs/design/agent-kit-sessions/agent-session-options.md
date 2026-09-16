## AgentSessionOptions

### Purpose

`AgentSessionOptions` captures everything an application configures for one compacting session:
instructions, tools, provider window size, compaction policy and summarizer. It also computes fixed
overhead and the estimated rotation threshold used when a provider reports no usage.

### Data Model

Immutable properties:

- **`Summarizer`** (`ISummarizer`) — Required out-of-session consolidation implementation.
- **`Instructions`** (`string?`) — System instructions, or null for none.
- **`Tools`** (`IReadOnlyList<AIFunction>`) — Owned read-only copy of the tool declarations.
- **`ProviderWindowTokens`** (`int`) — Configured window used by providers that report no window.
- **`Compaction`** (`CompactionPolicy`) — Policy, defaulting to `CompactionPolicy.Default`.
- **`VerbatimTurns`** (`int`) — Convenience access to `Compaction.VerbatimTurns`.
- **`SystemTokens`** (`int`) — Estimated instruction size.
- **`ToolDeclarationTokens`** (`int`) — Estimated tool declaration size.
- **`FixedOverheadTokens`** (`int`) — Estimated system plus tool overhead.
- **`EffectiveWindowTokens`** (`int`) — Configured window after estimated fixed overhead.
- **`RotationThresholdTokens`** (`int`) — 0.70 of the effective window, floored at one token.

The default configured provider window is an application fallback, not a claim about any specific
provider. A provider that reports its own window overrides it for rotation-trigger comparisons.

### Key Methods

#### The AgentSessionOptions Constructor

**Purpose:** Validate application configuration and derive the estimated fixed-overhead accounting.

**Algorithm:**

1. Require a summarizer.
2. Require a positive configured provider window.
3. Select the supplied `CompactionPolicy` or the shared default.
4. Estimate instruction and tool declaration tokens with `TokenEstimator`.
5. Reject null tool entries or declaration totals no token count can represent.
6. Subtract estimated fixed overhead from the configured window and reject a non-positive remainder.
7. Copy the tool list into owned read-only storage and store all derived figures.

**Preconditions:** The summarizer is not null; the provider window is positive; the tool list contains
no null entry.

**Postconditions:** The instance is immutable. `EffectiveWindowTokens` is positive, and
`RotationThresholdTokens` is the matching estimated trigger for providers that do not report usage.

#### RotationThresholdFor(int effectiveWindowTokens)

**Purpose:** Compute the conversation occupancy at which a session rotates for one effective window.

**Algorithm:** Multiply the effective window by the internal 0.70 threshold, truncate to an integer,
and return at least one token.

**Preconditions:** The effective window supplied by the caller is positive.

**Postconditions:** The returned threshold uses the same arithmetic for configured-window and
provider-reported paths.

### Error Handling

- **Null summarizer** — `ArgumentNullException` propagates.
- **Non-positive provider window** — `ArgumentOutOfRangeException` propagates.
- **Null tool entry** — `ArgumentException` propagates from tool declaration estimation.
- **Tool declarations too large to count** — `ArgumentException` propagates.
- **Fixed overhead consumes the configured window** — `ArgumentException` propagates, naming the
  configured window.

The unit intentionally does not predict whether a future conversation will settle. Runtime seed
sizing, level escalation and dropping handle that after real material exists.

### Dependencies

- **ISummarizer** — Required collaborator; see _Summarizer Unit Design_.
- **CompactionPolicy** — Supplies `VerbatimTurns`; see _CompactionPolicy Unit Design_.
- **TokenEstimator** — Estimates instructions and tool declarations; see _TokenEstimator Unit Design_.
- **ContextLayout** — Defines the internal 0.70 rotation threshold.
- **Microsoft.Extensions.AI.Abstractions** — Supplies `AIFunction`.

### Callers

Applications construct `AgentSessionOptions` before creating a `CompactingAgentSession`.
`CompactingAgentSession` reads the stored values during creation, usage estimation and rotation.
