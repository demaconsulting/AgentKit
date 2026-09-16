## ContextLayout

### Purpose

`ContextLayout` represents the complete context as this library accounts for it: fixed overhead,
coarse consolidated tiers, and a verbatim tail of whole turns. It builds the most-stable-first seed
used when a provider session is replaced.

### Data Model

Internal constants:

- **`SlotsPerTier`** (`int`) — Four slots per tier.
- **`TierCount`** (`int`) — Three coarse tiers.
- **`RotationThreshold`** (`double`) — 0.70 of the window left once overhead is paid for.
- **`MaxSummarizerInputTokens`** (`int`) — Maximum estimated material sent to one summarizer call
  before chunking.

Internal structures:

- **`Slot`** — One non-blank consolidated record with cached estimated tokens.
- **`Tier`** — Immutable ordered ring of slots, oldest first, with `Append`, `DropOldest`, `IsFull`,
  `IsEmpty`, `Count`, `Oldest` and cached estimated tokens.
- **`ContextLayout`** — Immutable fixed overhead, verbatim `Tail`, and `Tiers` from tier one to the
  coarsest tier.

Derived values:

- **`EstimatedConversationTokens`** — Estimated size of the built seed history, excluding fixed
  overhead.
- **`TotalEstimatedTokens`** — Fixed overhead plus estimated conversation tokens.

These estimates are this library's own account of the context it holds, in its own currency. They are
never compared against a figure a provider reported, and rotation does not assign a per-tier token
allowance.

### Key Methods

#### Create(int systemTokens, int toolDeclarationTokens)

**Purpose:** Create an empty layout after fixed overhead has been measured.

**Algorithm:** Reject negative overhead counts, allocate three empty tiers, and store an empty tail.

**Preconditions:** Both overhead counts are non-negative.

**Postconditions:** The layout holds no history, has three empty tiers, and is immutable.

#### WithTail(SessionTranscript tail)

**Purpose:** Return a layout with the same fixed overhead and tiers but a different verbatim tail.

**Algorithm:** Require `tail`, then create a new layout reusing the same tier array.

**Preconditions:** `tail` is not null.

**Postconditions:** The original layout is unchanged.

#### WithTiers(SessionTranscript tail, IReadOnlyList&lt;Tier&gt; tiers)

**Purpose:** Return a layout after rotation changed the tail and tiers together.

**Algorithm:** Require tail and tier list, require exactly three non-null tiers, copy the tier
references into layout-owned storage, and create a new layout.

**Preconditions:** `tail` and `tiers` are not null; `tiers` has exactly `TierCount` entries; no entry
is null.

**Postconditions:** The layout exposes tiers through a read-only view.

#### BuildSeed()

**Purpose:** Produce the history for a fresh provider session.

**Algorithm:** Emit slots from coarsest tier to finest tier, each tier oldest first, then append the
verbatim tail entries in their original order. Each slot is emitted as a labeled `ContextRecord`; the
system prompt and tool declarations are omitted because providers receive them as configuration.

**Preconditions:** None beyond the layout invariants.

**Postconditions:** The seed is most-stable-first, so older coarser records appear before newer
verbatim turns.

### Error Handling

- **Negative overhead counts** — `ArgumentOutOfRangeException` propagates.
- **Blank slot content** — `ArgumentException` propagates.
- **Null tail or tier list** — `ArgumentNullException` propagates.
- **Wrong tier count or null tier entry** — `ArgumentException` propagates.
- **Reading oldest or dropping oldest from an empty tier** — `InvalidOperationException` propagates.

### Dependencies

- **SessionTranscript** — Supplies the verbatim tail and flattened seed entries.
- **TokenEstimator** — Estimates slot and seed sizes.
- **RotationEngine** — Builds new layouts and reads tier constants.
- **ProviderSession** — Receives the seed entries built by the layout.

### Callers

`CompactingAgentSession` owns the current layout. `RotationEngine` transforms it during rotation.
Provider adapters receive the resulting seed through `ProviderSessionSeed`.
