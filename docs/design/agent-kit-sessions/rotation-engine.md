## RotationEngine

### Purpose

`RotationEngine` ages a context layout by one rotation. It keeps the newest whole turns verbatim at
the current compaction level, consolidates older material into round-robin slots, escalates when the
seed is still too large, and drops the oldest preserved material only as the final option.

### Data Model

Internal data types:

- **`RotationOutcome`** — Immutable result carrying `Layout`, `ConsolidationCount`, settled `Level`
  and `MaterialDropped`.
- **`RotationState`** — Private mutable working set for one build attempt at one compaction level.
- **`Slot` / `Tier`** — Internal layout storage transformed by the engine.

Public `CompactionLevel` values used by the engine:

- **Low** — Keep the full maximum verbatim tail and use the low terseness instruction.
- **Medium** — Keep half the maximum verbatim tail and use the medium instruction.
- **High** — Keep a quarter of the maximum verbatim tail and use the high instruction.

### Key Methods

#### RotateAsync(...)

**Purpose:** Produce the rotated layout that fits within the seed-size threshold.

**Algorithm:**

1. Validate inputs and start at `startLevel`.
2. Build a candidate layout at the current level.
3. Size the candidate seed using `EstimatedConversationTokens`.
4. If it fits, return it.
5. If it does not fit and the level is below High, escalate and rebuild from the original layout.
6. At High, drop the oldest slot of the coarsest non-empty tier, then oldest verbatim turns, until
   the seed fits or only the newest turn remains.

**Preconditions:** `layout` and `summarizer` are not null; `startLevel` is defined;
`rotationThresholdTokens` is positive.

**Postconditions:** The outcome carries the layout to seed, the level reached, the total number of
summarizer calls and whether anything was dropped.

#### VerbatimTurnsFor(CompactionLevel level, int verbatimTurns)

**Purpose:** Compute the tail length for a compaction level.

**Algorithm:** Low returns the full maximum, Medium returns half, and High returns a quarter. Medium
and High return at least one turn.

**Preconditions:** `level` is a defined value and `verbatimTurns` is positive.

**Postconditions:** The returned count is the number of newest turns retained verbatim at that level.

#### Escalate(CompactionLevel level) and Relax(CompactionLevel level)

**Purpose:** Move one step toward terser or gentler compaction.

**Algorithm:** Escalation maps Low to Medium and Medium to High, while High remains High. Relaxation
maps High to Medium and Medium to Low, while Low remains Low.

**Preconditions:** The caller supplies a defined level.

**Postconditions:** The returned level stays within the three defined levels.

#### RotationState.AppendSlotAsync(Slot slot, int tier, CancellationToken cancellationToken)

**Purpose:** Append a slot and cascade full tiers according to round-robin rules.

**Algorithm:** Append when the tier has room. If the coarsest tier is full, remove its oldest slot and
append the new one. If another coarse tier is full, consolidate its existing slots as peers into one
slot of the next tier, clear it, append the arriving slot to the emptied tier, and cascade the
coarser result as needed.

**Preconditions:** `slot` belongs to the target tier and `tier` is in range.

**Postconditions:** No tier exceeds four slots.

#### RotationState.ConsolidateItemsAsync(IReadOnlyList&lt;string&gt; items, int tierIndex, CancellationToken cancellationToken)

**Purpose:** Reduce rendered peer material into one optional slot.

**Algorithm:** If the joined material fits the summarizer input limit, consolidate it directly. If it
is too large, group pieces by estimated size, consolidate each group, and recursively consolidate the
non-blank chunk records until one record remains. A blank summarizer answer becomes no slot.

**Preconditions:** `items` is non-null, contains rendered material pieces, and `tierIndex` is one or
greater.

**Postconditions:** Returns one slot, or null when there is no material or every consolidation was
blank.

### Error Handling

- **Null layout or summarizer** — `ArgumentNullException` propagates.
- **Undefined compaction level** — `ArgumentOutOfRangeException` propagates.
- **Non-positive threshold** — `ArgumentOutOfRangeException` propagates.
- **Null summarizer answer** — `InvalidOperationException` propagates.
- **Cancellation before or after a summarizer call** — `OperationCanceledException` propagates.
- **Blank summarizer answer** — Normalized to empty and produces no slot.

Dropping material is not an exception. It is reported through `RotationOutcome.MaterialDropped` so the
owning session can surface it on `AgentSessionResponse`.

### Dependencies

- **ContextLayout** — Supplies tiers, slot limits, threshold fraction and seed estimates.
- **SessionTranscript** — Splits the tail by whole turns and renders material.
- **ISummarizer** — Performs out-of-session consolidation.
- **ConsolidationPrompt** — Supplies the terseness instruction for the current level.
- **CompactionLevel** — Selects tail length and consolidation instruction.

### Callers

`CompactingAgentSession` calls `RotateAsync` after a usage reading crosses the rotation threshold. No
provider adapter calls the engine directly.
