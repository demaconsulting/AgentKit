## RotationEngine

### Purpose

`RotationEngine` ages a context layout by one rotation. It keeps the newest whole turns verbatim at
the compaction level it is given, consolidates everything older into round-robin slots, cascades the
tiers that fill as a result, and reports what it did. It does not decide how hard to compact and does
not measure whether the result will fit.

### Data Model

Internal data types:

- **`RotationOutcome`** — Immutable result carrying `Layout`, `ConsolidationCount`, the `Level` the
  rotation ran at and `MaterialDropped`.
- **`RotationState`** — Private mutable working set for one rotation at one compaction level.
- **`Slot` / `Tier`** — Internal layout storage transformed by the engine.

Public `CompactionLevel` values used by the engine:

- **Low** — Keep the full maximum verbatim tail and use the low terseness instruction.
- **Medium** — Keep half the maximum verbatim tail and use the medium instruction.
- **High** — Keep a quarter of the maximum verbatim tail and use the high instruction.

### Key Methods

#### RotateAsync(...)

**Purpose:** Age the layout by one rotation at the level supplied.

**Algorithm:**

1. Validate inputs and the supplied level.
2. Take the tail length the level asks for, capped at one turn fewer than the tail holds, so the
   rotation always moves something; if the tail holds no turns at all, return the layout unchanged.
3. Split the tail at that boundary into whole older turns and the retained newest turns.
4. Consolidate the older turns into one tier-one slot, chunking by whole turns when the material is
   too large for one summarizer call.
5. Append the slot, cascading any tier that fills as a result, and return the aged layout with the
   consolidation count, the level and whether material was lost.

**Preconditions:** `layout` and `summarizer` are not null; `level` is a defined `CompactionLevel`.

**Postconditions:** The outcome carries the layout to seed, the level it ran at, the number of
summarizer calls, and whether a failed consolidation left material unrecorded. Nothing in the outcome
depends on a token measurement of the result.

#### VerbatimTurnsFor(CompactionLevel level, int verbatimTurns)

**Purpose:** Compute the tail length for a compaction level.

**Algorithm:** Low returns the full maximum, Medium returns half, and High returns a quarter. Medium
and High return at least one turn.

**Preconditions:** `level` is a defined value and `verbatimTurns` is positive.

**Postconditions:** The returned count is the number of newest turns retained verbatim at that level,
before the one-turn-fewer cap that guarantees progress is applied.

#### Escalate(CompactionLevel level) and Relax(CompactionLevel level)

**Purpose:** Move one step toward terser or gentler compaction.

**Algorithm:** Escalation maps Low to Medium and Medium to High, while High remains High. Relaxation
maps High to Medium and Medium to Low, while Low remains Low.

**Preconditions:** The caller supplies a defined level.

**Postconditions:** The returned level stays within the three defined levels. `CompactingAgentSession`
is the caller: the engine never changes the level it was handed.

#### RotationState.AppendSlotAsync(Slot slot, int tier, CancellationToken cancellationToken)

**Purpose:** Append a slot and cascade full tiers according to round-robin rules.

**Algorithm:** Append when the tier has room. If the coarsest tier is full, remove its oldest slot and
append the new one. If another coarse tier is full, consolidate its existing slots as peers into one
slot of the next tier, clear it, append the arriving slot to the emptied tier, and cascade the
coarser result as needed.

**Preconditions:** `slot` belongs to the target tier and `tier` is in range.

**Postconditions:** No tier exceeds four slots.

#### RotationState.ConsolidateTurnsAsync(IReadOnlyList&lt;SessionTurn&gt; turns, int tierIndex, CancellationToken cancellationToken)

**Purpose:** Reduce whole turns into one optional slot, keeping each turn indivisible.

**Algorithm:** Render each turn as one piece — its message, tool calls, tool results and answer joined
together — and hand the pieces to the chunking consolidation, which can place a turn in one group or
another but never in both.

**Preconditions:** `turns` holds the older turns, oldest first, and `tierIndex` is one or greater.

**Postconditions:** Returns one slot, or null when there is nothing to consolidate or the
consolidation failed.

#### RotationState.ConsolidateItemsAsync(IReadOnlyList&lt;string&gt; items, int tierIndex, CancellationToken cancellationToken)

**Purpose:** Reduce indivisible peer pieces into one optional slot.

**Algorithm:** If the joined material fits the summarizer input limit, consolidate it directly. If it
is too large, group the pieces by estimated size without splitting one, consolidate each group, and
consolidate the group records until one record remains. A blank record from any group fails the whole
consolidation and returns null, because dropping that group and combining the rest would discard its
span of history while the result looked like an ordinary success.

**Preconditions:** `items` is non-null, holds pieces the caller considers indivisible — a whole turn
for rule 2, a whole slot record for a rule 3 cascade — and `tierIndex` is one or greater.

**Postconditions:** Returns one slot, or null when there is no material or any part of the
consolidation came back blank.

### Error Handling

- **Null layout or summarizer** — `ArgumentNullException` propagates.
- **Undefined compaction level** — `ArgumentOutOfRangeException` propagates.
- **Null summarizer answer** — `InvalidOperationException` propagates.
- **Cancellation before or after a summarizer call** — `OperationCanceledException` propagates.
- **Blank summarizer answer** — Normalized to empty, produces no slot, and the material it was given
  stays verbatim.

A failed consolidation is not an exception. It is reported through `RotationOutcome.MaterialDropped`
so the owning session can surface it on `AgentSessionResponse`.

### Dependencies

- **ContextLayout** — Supplies tiers, slot limits and the summarizer input bound.
- **SessionTranscript** — Splits the tail into whole turns and renders material.
- **ISummarizer** — Performs out-of-session consolidation.
- **ConsolidationPrompt** — Supplies the terseness instruction for the current level.
- **CompactionLevel** — Selects tail length and consolidation instruction.

### Callers

`CompactingAgentSession` calls `RotateAsync` after a usage reading crosses the rotation threshold,
having already chosen the level and, under sustained pressure, discarded the oldest slot itself. No
provider adapter calls the engine directly.
