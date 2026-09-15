## RotationEngine

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The `RotationEngine` class ages a session's context by one rotation: a deterministic function from
the current layout and an injected summarizer to the next layout. It is the heart of the system.

### Purpose

**Rotation, not in-place reduction.** When the context fills, older history is consolidated, the
provider session is disposed, and a fresh one is created seeded with the preserved content. This is
the only reduction mechanism both provider shapes support: one re-sends the whole history on every
turn and would accept an edit, the other keeps history server-side and would not. Rotating is what
makes the two behave identically.

**Aging happens only here, and in one batch.** Between rotations the context is strictly append-only
— nothing already sent is rewritten — which is what preserves a provider's prompt cache. At rotation
every overflowing tier consolidates at once, cascading into coarser tiers where it must. Batching
costs nothing extra, because a rotation invalidates the cache anyway.

**Deterministic and pure apart from the summarizer.** Every decision this class makes — where the
tier boundary falls, whether it snaps, which tiers overflow, whether a result saturated — is
arithmetic over the layout it was handed. Supply a deterministic fake summarizer and the whole engine
is a pure function, which is how it is tested without a model. Determinism is not a convenience
here; it is what makes the heart of the system verifiable at all.

The class is static, holds no state, and is safe for concurrent use.

### Data Model

The class holds no state. Its private `RotationState` carries the mutable working set of one
rotation — the tiers being aged, the summarizer, the accumulated saturation reports and the
consolidation count — so the cascading recursion needs neither a long parameter list nor a closure
per call. It is created inside `RotateAsync` and never escapes it, so a rotation remains a pure
function from the caller's point of view.

`SaturationReason` values:

- **`NoRedundancy`** — The consolidation returned output nearly as large as its input, so there is nothing left to
  remove
- **`TierOverBudget`** — The consolidated record still exceeds its tier's budget, and there was no coarser tier
  left to age the older record into

`SaturationSignal` properties, immutable after construction: `TierIndex` (one or greater),
`InputTokens` and `OutputTokens` (neither negative), and `Reason`.

`RotationOutcome` properties, immutable after construction:

- **`Layout`** (`ContextLayout`) — The layout after rotation; what a fresh provider session is seeded from
- **`Saturations`** (`IReadOnlyList<SaturationSignal>`) — Never contains null; empty when the rotation reduced
  normally; a read-only view over a copy of the rotation's own working list, which is mutable while the rotation runs
- **`ConsolidationCount`** (`int`) — How many consolidations the rotation performed
- **`IsSaturated`** (`bool`) — Derived: `Saturations.Count > 0`

`ConsolidationCount` is exposed because summarizer calls are the dominant cost of this arrangement,
and because a test asserting that only the overflowing tiers were consolidated needs to count them.

A null signal is refused before the list is copied, following the rule `AgentSessionResponse`
already applies: an outcome holding one reports `IsSaturated` true while the consumer that goes to
read the signal cannot, which is worse than reporting nothing at all.

### Key Methods

#### RotateAsync(ContextLayout layout, ISummarizer summarizer, CancellationToken cancellationToken)

**Algorithm:**

1. Validate the arguments, then honor cancellation. The check sits here, before any work is decided
   on, because step 2 can return without ever reaching a consolidation: a token checked only around
   the summarizer calls would let an already-canceled rotation return a successful result whenever
   the transcript happened to fit. Argument validation still comes first, because a malformed call
   is a defect in the caller and is worth reporting as such even on a canceled token.
2. Split the verbatim history at tier zero's budget, newest first, snapping the boundary so a tool
   call is never separated from its result. The retained suffix stays verbatim.
3. If nothing overflowed, return the layout unchanged with no summarizer call and no saturation.
   That is the correct outcome for a session whose recent history already fits, and it costs
   nothing: the rotation is a pure re-seed.
4. Otherwise render the overflow as labeled material and fold it into tier one through the private
   aging recursion below.
5. Return a layout built from the retained transcript and the aged tiers, together with the
   saturation reports and the consolidation count.

**Postconditions:** the returned layout's tier list matches the policy; every consolidation the
engine performed is counted; every failure to reduce is reported.

#### RotationState.AgeAsync(int tierIndex, string material, CancellationToken cancellationToken)

Folds material into one tier, aging that tier's existing record into the next coarser tier when the
two cannot fit together.

**Algorithm:**

1. Consolidate the tier's previous record together with the new material. The previous record is an
   **input**, not context: this is the ratchet that keeps detail an earlier consolidation decided to
   keep.
2. If the result is at least the policy's saturation ratio of the combined input, report
   `NoRedundancy`.
3. If the result fits the tier's budget, store it and stop. This is the common case.
4. If there is no previous record to age down, or no coarser tier to age it into, store the result
   anyway and report `TierOverBudget`.
5. Otherwise age the **previous** record one tier coarser — the deliberate degradation the design
   allows — then re-consolidate the new material at this tier alone and store that. Apply the same
   redundancy test to that re-recording, reporting `NoRedundancy` when it is at least the policy's
   saturation ratio of the material it was given, and report `TierOverBudget` as well if even that
   exceeds the budget.

**Why both recordings a cascade performs are tested for redundancy.** A re-recording that returns
nearly as much as it was given has saturated whether or not it happened to fit the tier. Testing
only the merge let a cascade whose re-recording had saturated, but which still fitted, report a plain
success — so the one signal the caller needed was the one it never saw.

**Why the overflow test is made after consolidating rather than before.** A consolidation
compresses. Summing the previous record and the new material first would cascade on material that
would in fact have fitted once combined, degrading detail that did not need to degrade.

**Why the record that ages down is the previous one.** That ordering is what keeps the hierarchy
monotonic in age: coarser always means older. Aging the new material down instead would interleave
recent and old material at the same level, and no later consolidation could untangle them.

**Recursion is bounded by the tier count**, so the worst case is one degradation per tier and one
extra consolidation at each tier that cascaded.

#### RotationState.ConsolidateAsync(...)

Performs one consolidation and counts it. Centralizing the null check on the summarizer's result
protects every call site, and centralizing the count means the reported `ConsolidationCount` cannot
drift from what actually happened.

### Error Handling

- **Null layout or summarizer** — `ArgumentNullException` propagates
- **Null entry in an outcome's `saturations`** — `ArgumentException` propagates
- **Summarizer returns null** — `InvalidOperationException` propagates, naming the tier
- **Cancellation** — `OperationCanceledException` propagates, from the check after argument
  validation or from the summarizer call
- **Consolidation cannot reduce** — Reported as a `SaturationSignal`; not an exception
- **Record exceeds its tier with nowhere coarser to go** — Reported as a `SaturationSignal`; not an exception

The last two rows are the design decision worth noting. Saturation is a condition of the
conversation, not a fault in the engine, so it is reported rather than thrown. Acting on it — warn,
stop, split the task, start fresh — depends on what the application is for and is deliberately left
to the application. A null record, by contrast, is a defect in a summarizer implementation: it would
be stored and would surface as a missing tier at a later rotation, far from its cause, so it is
refused immediately.

### Dependencies

- **ContextLayout** — the state rotated, and the tier objects aged; see _ContextLayout Unit Design_.
- **SessionTranscript** — supplies the boundary split and the material rendering; see
  _SessionTranscript Unit Design_.
- **CompactionPolicy** — supplies the tier budgets, the tier count and the saturation ratio; see
  _CompactionPolicy Unit Design_.
- **Summarizer** — supplies `ISummarizer` and `ConsolidationRequest`; see _Summarizer Unit Design_.
- **TokenEstimator** — measures each consolidation's input and output; see _TokenEstimator Unit
  Design_.

### Callers

`CompactingAgentSession` calls `RotateAsync` once per rotation, seeds a replacement provider session
from the returned layout, and surfaces the returned saturation reports on the turn's
`AgentSessionResponse`.
