## Summarizer

![AgentKit Core Structure](AgentKitCoreView.svg)

### Purpose

`Summarizer` defines the out-of-session consolidation contract and the internal prompt composer. The
rotation engine injects this collaborator so compaction can be tested deterministically and production
applications can choose their own model-backed implementation.

### Data Model

`ConsolidationRequest` public properties:

- **`TierIndex`** (`int`) — One-based tier the result belongs to; must be one or greater.
- **`Material`** (`string`) — Labeled peer material to consolidate; non-blank.
- **`Instruction`** (`string`) — Plain-language terseness clause selected by compaction level;
  non-blank.

`ConsolidationPrompt` internal members:

- **`Instruction`** — Base prompt preserving named facts, decisions, paths, values, constraints,
  errors, resolutions and outstanding work.
- **`LowInstruction`** — Low-level terseness clause.
- **`MediumInstruction`** — Medium-level terseness clause.
- **`HighInstruction`** — High-level terseness clause.

The request carries no target size, because a model cannot be asked to hit an output length. Nothing
measures the record that comes back either: the structure is answered in counts of slots, and the
only token figure the session reads is the one the provider reports about itself.

### Key Methods

#### ISummarizer.ConsolidateAsync(ConsolidationRequest request, CancellationToken cancellationToken)

**Purpose:** Return one consolidated record for the material.

**Algorithm:** The implementation reads the request material and instruction, preserves specific
facts and outstanding work, collapses repetition, and returns a string. It may return an empty string
when there is nothing to keep, but must not return null.

**Preconditions:** `request` is not null.

**Postconditions:** The returned record states only information present in the material and is ready
for the engine to place in a slot.

#### ConsolidationPrompt.Instruction

**Purpose:** Define the base consolidation prompt used by built-in prompt composition.

**Algorithm:** The prompt frames the material as the only surviving account of that span of history,
lists the categories that must survive by name, allows repetition to be collapsed, and forbids
speculation or commentary.

**Preconditions:** N/A - constant text.

**Postconditions:** The prompt contains no length, word-count or token-count target.

#### ConsolidationPrompt.Compose(ConsolidationRequest request)

**Purpose:** Build the full prompt text for a model-backed summarizer.

**Algorithm:** Require the request, then concatenate the base instruction, the request's terseness
instruction, and the `MATERIAL` section.

**Preconditions:** `request` is not null.

**Postconditions:** The same request always composes to the same prompt text.

#### ConsolidationPrompt.InstructionFor(CompactionLevel level)

**Purpose:** Select the terseness clause for a compaction level.

**Algorithm:** Return the low, medium or high clause for the supplied level; unrecognized values use
the low clause only inside this internal helper, while public validation occurs before rotation.

**Preconditions:** The rotation path supplies a defined compaction level.

**Postconditions:** The returned text becomes `ConsolidationRequest.Instruction`.

### Error Handling

- **Tier index less than one** — `ArgumentOutOfRangeException` propagates.
- **Blank material or instruction** — `ArgumentException` propagates.
- **Null request passed to a summarizer or prompt composer** — `ArgumentNullException` propagates.
- **Null summarizer result** — Refused by `RotationEngine` as `InvalidOperationException`.
- **Cancellation** — The summarizer may throw `OperationCanceledException`; the engine also checks
  cancellation before and after calls.

### Dependencies

- **RotationEngine** — Creates requests and normalizes summarizer output.
- **CompactionLevel** — Selects terseness instructions.
- **SessionTranscript** — Provides rendered material.

### Callers

Applications implement `ISummarizer`. `RotationEngine` calls it during rotation, once for the
material ageing out of the verbatim tail and again for each cascade that consolidates a full tier.
