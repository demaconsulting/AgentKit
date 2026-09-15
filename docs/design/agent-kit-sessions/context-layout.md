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
  contains null; a read-only view over the layout's own array
- **`ConversationTokens`** (`int`) — Derived: transcript, every coarse tier, and the seed framing of each non-empty
  tier
- **`TotalEstimatedTokens`** (`int`) — Derived: `SystemTokens + ToolDeclarationTokens + ConversationTokens`
- **`MaximumBoundTokens`** (`int`) — Derived:
  `SystemTokens + ToolDeclarationTokens + Policy.TotalTierBudgetTokens + SeedFramingTokens(Policy)`
- **`IsWithinBound`** (`bool`) — Derived: `TotalEstimatedTokens <= MaximumBoundTokens`

**The bound counts the seed framing, not raw tier content.** `BuildSeed` does not hand a provider a
tier's content: it wraps each non-empty tier in a transcript entry carrying a label that names the
detail level, and every entry is charged the estimator's per-entry allowance on top. Both are tokens
the provider actually receives. A bound counting raw content alone is an under-count that a seed
with every tier exactly within its budget would exceed — and for a provider reporting no usage, that
under-count is what drives the rotation decision. `ConversationTokens` charges the framing for
non-empty tiers only, because an empty tier is not seeded; `MaximumBoundTokens` charges it for every
coarse tier, because a bound must assume the worst case.

**`ConversationTokens` excludes the fixed overhead** because the rotation threshold is a fraction of
the effective window rather than of the whole one. Publishing both figures makes the comparison
unambiguous rather than something each caller re-derives.

**`IsWithinBound` is a post-rotation property.** It is true immediately after a rotation and false
in the ordinary course of a session between rotations, because tier zero is append-only and grows
past its budget until the next rotation batches everything back inside. That growth is exactly what
the rotation threshold's headroom is reserved for, and a caller checking this outside a
post-rotation assertion is asking the wrong question. The bound it compares against is always
positive: an unrepresentable one is refused where it is first computable, in `CompactionPolicy` for
the budgets and framing and in `Create` for the fixed overhead, so this comparison is never made
against a wrapped figure.

**Instances are immutable**: every change returns a new layout, and the tier list it publishes is a
read-only view rather than its backing array, so a caller cannot replace an element and change both
`ConversationTokens` and the next seed. That is what makes the rotation engine a pure function and
lets a test compare a before and an after.

### Key Methods

#### Create(CompactionPolicy policy, int systemTokens, int toolDeclarationTokens)

Allocates one empty coarse tier per non-verbatim budget, numbering them from one and giving each the
policy's budget for its index. Allocating up front means the hierarchy always matches the policy and
no code path has to grow it later — a tier appearing mid-session would make the bound unverifiable
at the moment it mattered most.

It also refuses a fixed overhead that would carry `MaximumBoundTokens` past what a token count can
represent. A policy already guarantees its own half of that bound, so the fixed overhead is the only
remaining term that can exceed one; left unchecked the addition wraps, the bound is negative, and a
layout holding nothing at all reports itself outside the bound it was constructed to respect.

#### WithTranscript(SessionTranscript transcript)

Returns a layout carrying a different tier-zero history, leaving this one unchanged. Used on every
turn as the transcript grows.

#### WithTiers(SessionTranscript transcript, IReadOnlyList&lt;ContextTier&gt; coarseTiers)

Returns a layout carrying both a different history and different coarse tiers. Both are replaced
together because a rotation changes both at once, and applying them separately would produce an
intermediate layout that never actually exists. Every tier is validated against the policy before
being copied — the list must hold one fewer tier than the policy's tier count, and the tier at
position `i` must carry index `i + 1` and the policy's budget for that index — so a layout whose
hierarchy disagrees with its own budgets can never exist. Validating the count alone would not
achieve that: the position in the list is what rotation reads the policy's budget by, what the seed
labels the record by, and what the bound is computed from, so a tier-one slot carrying tier three's
budget would leave those accounts describing different hierarchies while every one of them looked
individually correct.

#### SeedFramingTokens(CompactionPolicy policy)

Returns the tokens the framing of the seeded tier records adds for a policy: for every coarse tier,
the estimate of its record label plus the per-entry allowance. Published as a static function of the
policy because `AgentSessionOptions` must refuse a window that cannot hold the bound and has to
compute that bound before any layout exists. The label is built by the same private helper
`BuildSeed` uses, so what is emitted and what is charged for cannot drift apart — which is exactly
how the bound came to under-count the seed.

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

**The seed is published as a genuine read-only view**, not as the list it was built in, following the
same idiom as `CoarseTiers` and `SessionTranscript.Entries`. The seed is handed straight to a
provider-session factory, so a caller able to cast it back to `List<TranscriptEntry>` could alter the
history a fresh session is created from between building it and using it.

### Error Handling

- **Null policy, transcript or tier list** — `ArgumentNullException` propagates
- **Negative system or tool declaration tokens** — `ArgumentOutOfRangeException` propagates
- **Fixed overhead and policy bound exceeding a representable token count** — `ArgumentException` propagates,
  naming the total
- **Tier index below one, or non-positive tier budget** — `ArgumentOutOfRangeException` propagates
- **Null tier content** — `ArgumentNullException` propagates
- **Tier list of the wrong length** — `ArgumentException` propagates, naming both counts
- **Tier whose index disagrees with its position** — `ArgumentException` propagates, naming both indexes
- **Tier whose budget disagrees with the policy** — `ArgumentException` propagates, naming both budgets
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
