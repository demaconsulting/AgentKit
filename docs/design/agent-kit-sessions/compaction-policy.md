## CompactionPolicy

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The `CompactionPolicy` class carries the validated controls governing when a session rotates and how
much of each tier survives.

### Purpose

Context is laid out most-stable-first — `[system][tool declarations][tier N coarse] ... [tier 1]
[tier 0 verbatim]` — and this policy carries one token budget per tier. Those budgets are what make
the whole arrangement bounded rather than merely well behaved: because each is fixed, the total is a
property of the configuration alone and can be stated before a session starts.

The class follows the convention `ToolLimits` and `MemoryOptions` already establish in this
repository: optional constructor parameters over published constants, validation before assignment,
and a shared `Default` instance. An author who has configured tool limits needs to learn nothing new.

### Data Model

- **`TierBudgetTokens`** (`IReadOnlyList<int>`) — At least two entries; every entry positive; no entry larger than
  the one before it; a defensive copy of what was supplied
- **`TierCount`** (`int`) — Derived: `TierBudgetTokens.Count`, counting the verbatim tier zero
- **`RotationThreshold`** (`double`) — Greater than zero and at most one
- **`SaturationRatio`** (`double`) — Greater than zero and at most one
- **`TotalTierBudgetTokens`** (`int`) — The sum of every tier budget; the most conversation context the arrangement
  can hold

Published constants and defaults:

- **`DefaultRotationThreshold`** (0.70) — Leaves 30 percent of the effective window as headroom, covering both the
  error in a character-ratio estimate and the turn in flight when the threshold is crossed. What guarantees rotation
  does not immediately re-trigger is the convergence invariant `AgentSessionOptions` asserts — a rotated context
  lands below the threshold — not this fraction on its own. How far below, and so how generous the hysteresis is,
  depends on how the host sized its window against its tier budgets: sized several times the bound, a rotation lands
  near 40 percent; sized near the invariant's minimum, it lands just under the threshold and buys one turn
- **`DefaultSaturationRatio`** (0.90) — A genuine consolidation reduces its input substantially, so a result this
  close to its input is not a near miss but a signal that the material is as compressed as it will get
- **`DefaultTierBudgetTokens`** (2000, 1200, 900, 700) — The shape matters more than the exact numbers: tier zero
  is largest because verbatim recent history is the most useful thing in the window, and each older tier is smaller
  because a coarser record should cost less. These are the starting values the compaction spike ran with (n = 50
  rotations, one run, recorded in that spike), published as constants precisely so a different window or task shape
  can replace them

`TotalTierBudgetTokens` for the defaults is 4,800 tokens.

**Instances are immutable after construction and safe for concurrent use.** `Default` is a single
shared instance rather than a factory method, so a caller may compare against it by reference to
establish that no host configuration was applied.

### Key Methods

#### CompactionPolicy(IReadOnlyList&lt;int&gt;? tierBudgetTokens, double rotationThreshold, double saturationRatio)

**Algorithm:**

1. Select the supplied budgets, or `DefaultTierBudgetTokens`.
2. Reject fewer than two budgets.
3. For each budget: reject a non-positive value, and reject a value larger than the budget before it,
   accumulating the running total in a type wider than a token count.
4. Reject a policy whose full bound — that total plus the framing each seeded tier record carries —
   cannot be represented as a token count.
5. Reject a rotation threshold or saturation ratio that is `NaN`, or that lies outside the range
   greater than zero and at most one.
6. Copy the budgets into storage the policy owns, assign, and publish the accumulated total as
   `TotalTierBudgetTokens`.

**Why at least two tiers.** One tier is not a hierarchy. With a single verbatim tier there is
nowhere for overflowing history to age into, and the arrangement degenerates to dropping the oldest
turns outright — which is the behavior this system exists to avoid.

**Why budgets must not grow.** A coarser tier allowed more room than the finer tier it ages from is
the opposite of what consolidation is for, and would mean the hierarchy never reduces.

**Why the budgets are copied and published as a read-only view.** A caller that mutated the list it
supplied could otherwise change the policy a session is already running under, silently altering the
bound mid-conversation; and an `IReadOnlyList` over a bare array can be cast back to `int[]` and
written through, which would do the same while `TotalTierBudgetTokens`, summed once at construction,
went stale.

**Why `NaN` is rejected explicitly.** Every ordered comparison against `NaN` is false, so a range
check built from them accepts a `NaN` that carries no sign bit. The policy it configures then
compares false against every conversation size and every consolidation result, silently disabling
rotation or saturation reporting rather than announcing that it had been misconfigured. The same
defect, with the same reasoning, is recorded on the `MemoryOptions` near-duplicate threshold in
AgentKit Tools.

**Why an unrepresentable bound is rejected here, and only here.** The bound is computed at three
places: this constructor, `AgentSessionOptions`' window check, and
`ContextLayout.MaximumBoundTokens`. Summing budgets in a token-sized type raised an undocumented
`OverflowException` at the first, and wrapped silently at the other two — a wrapped bound is
negative, so a window comparison passes a configuration no positive window could satisfy and an
empty layout reports itself outside its own bound. This constructor is the earliest point at which
the whole policy-derived bound is known, so it is where the rejection belongs; the two later sites
then rely on it and may add the same two figures in plain token arithmetic. `ContextLayout.Create`
adds the only remaining term — the fixed overhead — and refuses that on the same principle.

### Error Handling

- **Fewer than two tier budgets** — `ArgumentException` propagates
- **Non-positive tier budget** — `ArgumentOutOfRangeException` propagates, naming the tier and the value
- **`NaN` rotation threshold or saturation ratio** — `ArgumentOutOfRangeException` propagates, naming the control
- **Tier budget larger than the tier before it** — `ArgumentException` propagates, naming both tiers and both
  values
- **Budgets and framing exceeding a representable token count** — `ArgumentException` propagates, naming the total
- **Rotation threshold at or below zero, or above one** — `ArgumentOutOfRangeException` propagates
- **Saturation ratio at or below zero, or above one** — `ArgumentOutOfRangeException` propagates

Each is a defect in the host's configuration code rather than a runtime condition a model can
provoke, so each is surfaced rather than clamped.

### Dependencies

None. The class performs arithmetic on integers and doubles and holds no references.

### Callers

`AgentSessionOptions` holds a policy and reads `RotationThreshold` and `TotalTierBudgetTokens` from
it. `ContextLayout` reads `TierCount` and `TierBudgetTokens` when allocating its tiers and when
publishing its bound. `RotationEngine` reads `TierBudgetTokens`, `TierCount` and `SaturationRatio`
on every rotation.
