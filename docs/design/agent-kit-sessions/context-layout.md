## ContextLayout

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The `ContextLayout` unit publishes `ContextTier`, one coarse tier of consolidated history, and
`ContextLayout`, the whole of a session's context as this system accounts for it.

### Purpose

`ContextLayout` is the state a rotation acts on. It holds the fixed overhead, the coarse tiers
holding consolidated records, and the verbatim recent history, and from those it derives the figures
every decision in the system rests on: the conversation tokens a threshold is compared against, and
the construction bound the arrangement can never exceed after a rotation.

`ContextTier` is deliberately a single string rather than a structured record. What a consolidation
produces is prose written for the agent to rely on later, and imposing a structure on it here would
either constrain the summarizer or require this library to parse a model's output — both of which
would make the arrangement brittle for no gain. Tier zero is not represented by this type at all: it
holds verbatim history and is a `SessionTranscript`.

### Data Model

`ContextTier` properties, immutable after construction:

- **`Index`** (`int`) — One or greater; tier zero is the transcript, not a tier object
- **`BudgetTokens`** (`int`) — Positive
- **`Content`** (`string`) — Never null; empty means the tier holds nothing yet
- **`EstimatedTokens`** (`int`) — Computed once at construction from `Content`
- **`IsEmpty`** (`bool`) — Derived: `Content.Length == 0`
- **`IsWithinBudget`** (`bool`) — Derived: `EstimatedTokens <= BudgetTokens`

`ContextLayout` properties:

- **`Policy`** (`CompactionPolicy`) — Never null
- **`SystemTokens`** (`int`) — Not negative; fixed overhead
- **`ToolDeclarationTokens`** (`int`) — Not negative; fixed overhead
- **`Transcript`** (`SessionTranscript`) — Tier zero; never null
- **`CoarseTiers`** (`IReadOnlyList<ContextTier>`) — Exactly `Policy.TierCount - 1` entries, tier one first; never
  contains null
- **`ConversationTokens`** (`int`) — Derived: transcript plus every coarse tier
- **`TotalEstimatedTokens`** (`int`) — Derived: `SystemTokens + ToolDeclarationTokens + ConversationTokens`
- **`MaximumBoundTokens`** (`int`) — Derived: `SystemTokens + ToolDeclarationTokens + Policy.TotalTierBudgetTokens`
- **`IsWithinBound`** (`bool`) — Derived: `TotalEstimatedTokens <= MaximumBoundTokens`

**`ConversationTokens` excludes the fixed overhead** because the rotation threshold is a fraction of
the effective window rather than of the whole one. Publishing both figures makes the comparison
unambiguous rather than something each caller re-derives.

**`IsWithinBound` is a post-rotation property.** It is true immediately after a rotation and false
in the ordinary course of a session between rotations, because tier zero is append-only and grows
past its budget until the next rotation batches everything back inside. That growth is exactly what
the rotation threshold's headroom is reserved for, and a caller checking this outside a
post-rotation assertion is asking the wrong question.

**Instances are immutable**: every change returns a new layout. That is what makes the rotation
engine a pure function and lets a test compare a before and an after.

### Key Methods

#### Create(CompactionPolicy policy, int systemTokens, int toolDeclarationTokens)

Allocates one empty coarse tier per non-verbatim budget, numbering them from one and giving each the
policy's budget for its index. Allocating up front means the hierarchy always matches the policy and
no code path has to grow it later — a tier appearing mid-session would make the bound unverifiable
at the moment it mattered most.

#### WithTranscript(SessionTranscript transcript)

Returns a layout carrying a different tier-zero history, leaving this one unchanged. Used on every
turn as the transcript grows.

#### WithTiers(SessionTranscript transcript, IReadOnlyList&lt;ContextTier&gt; coarseTiers)

Returns a layout carrying both a different history and different coarse tiers. Both are replaced
together because a rotation changes both at once, and applying them separately would produce an
intermediate layout that never actually exists. The tier list is validated to match the policy's
count and copied, so a layout whose hierarchy disagrees with its own budgets can never exist.

#### BuildSeed()

Builds the history a fresh provider session is seeded with, most stable first.

**Algorithm:**

1. Walk the coarse tiers from coarsest to finest. Skip any empty tier. Emit each non-empty tier as a
   `ContextRecord` entry, labeled with its detail level and a note that coarser levels cover older
   material.
2. Append the verbatim transcript entries in the order they happened.

**Why coarsest first.** Stability decreases from left to right, which is what allows a provider's
prompt cache to match the longest possible prefix between turns. The oldest, least detailed material
sits furthest from the live turn.

**Why each record is labeled.** A model handed several consolidated records with no ordering cue
cannot tell which supersedes which.

**Why empty tiers are skipped.** Seeding an empty record would spend framing tokens to say nothing.

**Why the system prompt and tool declarations are not emitted.** Providers accept them through their
own configuration rather than as history, which is exactly why they are accounted for here as fixed
overhead and not as entries.

### Error Handling

- **Null policy, transcript or tier list** — `ArgumentNullException` propagates
- **Negative system or tool declaration tokens** — `ArgumentOutOfRangeException` propagates
- **Tier index below one, or non-positive tier budget** — `ArgumentOutOfRangeException` propagates
- **Null tier content** — `ArgumentNullException` propagates
- **Tier list of the wrong length** — `ArgumentException` propagates, naming both counts
- **Null tier within the list** — `ArgumentException` propagates

### Dependencies

- **CompactionPolicy** — supplies the tier count, the per-tier budgets and the total bound; see
  _CompactionPolicy Unit Design_.
- **SessionTranscript** — supplies the verbatim tier zero and its entries; see _SessionTranscript
  Unit Design_.
- **TokenEstimator** — measures a tier's record; see _TokenEstimator Unit Design_.

### Callers

`RotationEngine` reads the layout's policy, transcript and tiers, and returns a new layout built
with `WithTiers`. `CompactingAgentSession` creates the layout, grows it with `WithTranscript` on
every turn, replaces it wholesale after a rotation, and calls `BuildSeed` to create the replacement
provider session. An application may read the layout from `CompactingAgentSession` to see what is
actually in its tiers.
