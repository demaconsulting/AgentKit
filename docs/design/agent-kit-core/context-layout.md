## ContextLayout

![AgentKit Core Structure](AgentKitCoreView.svg)

### Purpose

`ContextLayout` represents the complete context as this library accounts for it: coarse consolidated
tiers and a verbatim tail of whole turns. It builds the most-stable-first seed used when a provider
session is replaced.

### Data Model

Internal constants:

- **`SlotsPerTier`** (`int`) — Four slots per tier.
- **`TierCount`** (`int`) — Three coarse tiers.
- **`RotationThreshold`** (`double`) — 0.70 of the window left once overhead is paid for.

Internal structures:

- **`Slot`** — One non-blank consolidated record.
- **`Tier`** — Immutable ordered ring of slots, oldest first, with `Append`, `DropOldest`, `IsEmpty`
  and `Count`.
- **`ContextLayout`** — Immutable verbatim `Tail` and `Tiers` from tier one to the coarsest tier.

The layout holds no size of its own. It is a structure counted in turns and slots; the only token
figures the session uses come back from the provider session that counted them, so there is nothing
here for such a figure to be compared against and rotation assigns no per-tier token allowance.

### Key Methods

#### Create()

**Purpose:** Create an empty layout.

**Algorithm:** Allocate three empty tiers and store an empty tail.

**Preconditions:** None.

**Postconditions:** The layout holds no history, has three empty tiers, and is immutable.

#### WithTail(SessionTranscript tail)

**Purpose:** Return a layout with the same tiers but a different verbatim tail.

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

- **Blank slot content** — `ArgumentException` propagates.
- **Null slot appended to a tier** — `ArgumentNullException` propagates.
- **Null tail or tier list** — `ArgumentNullException` propagates.
- **Wrong tier count or null tier entry** — `ArgumentException` propagates.
- **Dropping the oldest slot of an empty tier** — `InvalidOperationException` propagates.

### Dependencies

- **SessionTranscript** — Supplies the verbatim tail and flattened seed entries.
- **RotationEngine** — Builds new layouts and reads tier constants.
- **ProviderSession** — Receives the seed entries built by the layout.

### Callers

`CompactingAgentSession` owns the current layout. `RotationEngine` transforms it during rotation.
Provider adapters receive the resulting seed through `ProviderSessionSeed`.
