# Proposal: Round-Robin Compaction by Count

This is a proposal, not a design of record. It asks for a decision on replacing the compaction core
of `AgentKitSessions` before any code is written.

Revised after three independent reviews. Where a review refuted a claim in the first draft, the
correction is kept in place rather than quietly removed, because several of those claims are ones a
reader would otherwise make again.

## Summary

Compaction is currently driven by token budgets: each tier carries a budget in estimated tokens, and
a stack of arithmetic decides in advance whether a configuration can settle. This proposal replaces
that with a round-robin structure configured in **counts** — turns and slots — with tokens used for
two purposes only: noticing that the window is filling, and checking afterwards that what we built
actually fits.

The application configures **one** number.

## Why the current arrangement is wrong

### A budget is not a contract a model can honor

Measured against live models, a summarizer asked for a specific size did not produce it. Requests in
the range of 9,870 to 19,741 tokens returned 1,665 and 4,259 tokens.

Two data points, both under-production, on unstated models. That is enough to support a narrow claim
— **a model cannot be asked to hit an output size** — and it is not enough to support the broader one
the first draft made, that every budget in the system is therefore fiction. A budget applied to a
tier's *input* by truncation is a real bound; it is simply a blunt one, because it enforces the size
by discarding what the summarizer would not compress.

The narrow claim is sufficient for this proposal. Asking for a size is the wrong instrument; the
right correction is to stop asking.

### The predictive arithmetic was in the wrong currency, and it did not work

The convergence guard compared a provider's own token count against this library's estimate at four
characters per token. Driven through the built assembly across thirty turns, with a provider whose
tokenizer counted twice what the estimator did:

| Reported window | Tokenizer | Rotations in 30 turns | Back to back | Saturation signals |
| --- | --- | --- | --- | --- |
| 12,000 | 1.0x | 2 | 0 | 0 |
| 12,000 | 2.0x | 23 | 22 | 0 |
| 16,000 | 2.0x | 5 | 0 | 0 |
| 12,000 | 2.5x | 24 | 23 | 1 |

The guard accepted the second row. The session spent a summarizer call and a provider session on
every turn and reported nothing, because each individual consolidation reduced perfectly normally.
The failure the guard exists to prevent is the failure it permitted.

Two honest qualifications. These are four synthetic rows with a simulated tokenizer multiplier, not
production traffic. And the third row shows the failure is scale-dependent, which is equally
consistent with "the four-characters-per-token constant is wrong" as with "the arithmetic must go".

What settles it is not the magnitude but the **direction**: the guard compares a measured number
against an estimated one, so its error is unbounded in principle and undetectable in practice. A
better constant would move the cliff, not remove it. The same arithmetic was wrong in the other
direction too — a provider reporting *less* overhead had a working session abandoned mid-conversation,
because a difference between two tokenizers was credited as hidden overhead.

### The concepts are not product concepts

The fold, runtime convergence and integer-saturation guarding were introduced by review rounds rather
than by the design. None is a thing an application author would recognize. The saturation guards
protect against contexts two hundred times larger than the largest context window that exists.

## What the providers actually report

Verified against the shipped assemblies. The first draft of this section was too generous to Copilot,
and the corrections matter.

### GitHub Copilot SDK

Per session, `SessionUsageInfoData` carries `ConversationTokens`, `CurrentTokens`, `SystemTokens`,
`ToolDefinitionsTokens`, `TokenLimit`, `MessagesLength` and `IsInitial`. Per API call,
`AssistantUsageData` carries `InputTokens`, `OutputTokens`, `ReasoningTokens`, `ToolTokenCount`,
`Cost`, `Model`, and cache telemetry in `CacheReadTokens`, `CacheWriteTokens` and `CacheExpiresAt`.

**Three corrections to the first draft, all load-bearing:**

1. **"Nothing is hidden" was wrong.** `MetadataContextInfoResultContextInfo` also carries
   `BufferTokens` ("output reserve plus tokens after the buffer-exhaustion blocking threshold,
   default 95%") and `Limit` ("prompt token limit **plus** the model's full output token limit"), and
   both `ToolDefinitionsTokens` and `McpToolsTokens` are documented as **excluding deferred tools**.
   `TotalTokens` is the sum of system, conversation and tool-definition tokens, and so excludes the
   buffer. There are therefore three different denominators available and at least one category of
   unaccounted tokens. Picking the wrong denominator is precisely the defect class this proposal
   exists to retire.

2. **Copilot compacts the context itself.** `CompactionThreshold` is documented as the "token count
   at which background compaction starts (configurable percentage of `promptTokenLimit`)". A second
   compactor operates on the same context on a schedule we do not control. Our earlier spike measured
   it firing at 92.3%, but the field documentation says it is configurable, so that figure is an
   observation and not a constant to design against. This is the strongest argument for rotating
   early: not to be tidy, but to stay clear of another compactor's floor.

3. **"Exact numbers at any moment" was overstated.** `SessionUsageInfoData` arrives as an *event*.
   `MetadataApi.ContextInfoAsync` is on demand, but its own documentation says it returns null "if
   uninitialized" — which is exactly the moment after a rotation the first draft singled out as the
   best case — and it requires the caller to pass the limits in, returning `DEFAULT_TOKEN_LIMIT` when
   they are unspecified.

### Microsoft.Extensions.AI

`UsageDetails` carries `InputTokenCount`, `OutputTokenCount`, `TotalTokenCount`,
`CachedInputTokenCount` and `ReasoningTokenCount`, among others — but these are **nested, not
additive**: cached input is documented as part of input, and reasoning as part of output. Summing
them double-counts.

There is **no context-window discovery API**. `ChatClientMetadata` carries only `ProviderName`,
`ProviderUri` and `DefaultModelId`; `ChatOptions.MaxOutputTokens` is an output cap, not a window. The
application must configure the window. Usage arrives only *after* a response.

### What this means for the design

The two providers differ in **when** a figure is available, not in what the design does with it. The
design must therefore never assume it can ask on demand.

## The proposed structure

A round-robin database does not measure the size of its archives. It states that a fixed number of
fine samples consolidate into one coarse sample, and that each archive holds a fixed number of them.
Resolution decays with age by construction, and nothing is weighed.

### Configuration: one setting

**`VerbatimTurns`** — the **most** recent turns kept word for word. Default 20. This is the only
setting.

`SlotsPerTier` (4) and `TierCount` (3) are **internal constants**, not settings. They are the values
the recall measurement was taken at, and the property this design is sold on is a consequence of
them; an application that sets `TierCount` to six silently destroys it and receives no signal saying
so. An author has no basis for choosing either number and no symptom telling them they chose wrong.

`VerbatimTurns` is a **maximum**, not a quota: the tail holds at most that many turns, and may hold
fewer when the context is under pressure.

A **turn** is one exchange — the message sent, the answer, and every tool call and tool result
produced in between. Tool traffic is therefore never split from the answer it belongs to, which
retires the tool-pair snapping rule the current design needs.

### Mechanics

**Rule 1 — Append.** Turns append to the verbatim tail.

**Rule 2 — Rotate when the window is filling.** When occupancy reaches **70% of the effective
window**, everything older than the last `VerbatimTurns` is summarized into one slot and appended to
tier one. Occupancy is defined per provider shape in the next section.

**Rule 3 — Consolidate a full tier.** When a tier from one to `TierCount - 1` is full and another
slot arrives, its slots consolidate into a single slot of the next tier, and it is cleared.

**Rule 4 — The last tier is a ring.** When the last tier is full and a slot arrives, its oldest slot
is dropped.

**Rule 5 — Drop until it fits.** After building a seed, measure it. While it still exceeds the
rotation threshold, drop the oldest slot of the coarsest non-empty tier and rebuild. If every slot is
gone and it still does not fit, drop the oldest verbatim turn. Report that material was dropped.

Rule 5 is the one that makes the guarantee real, and it is the principal change from the first draft.
Rules 3 and 4 respond to a *schedule*; rule 5 responds to *pressure*. Without it the level ladder
terminates while the context still does not fit — which is exactly row two of the table above, the
pathology this proposal indicts the current design for permitting silently.

Rule 5 terminates unconditionally: slots are finite, the verbatim tail is finite, and the loop bottoms
out at the newest turn alone.

### Defining "the window is filling", on every provider shape

This predicate is load-bearing and the first draft never defined it.

| Provider reports | Occupancy figure | When it is known |
| --- | --- | --- |
| A window and its own usage | Provider's own used-versus-limit, one currency | After each turn |
| Usage but no window | Reported input tokens vs the **configured** window | After each response |
| Nothing | This library's estimate vs the configured window | Always, approximately |

Three rules keep this neutral and honest:

- **Never mix currencies.** Both sides of the comparison come from the same source. An estimate is
  never subtracted from, or compared against, a measurement.
- **Never require on-demand querying.** A figure that arrives after a turn is sufficient, because
  rotation happens between turns. This is why the trigger is at 70% rather than at 95%: the headroom
  absorbs one turn's overshoot on a provider that can only tell us afterwards.
- **Where the provider offers several denominators, take the most conservative** — for Copilot, the
  prompt token limit rather than the prompt-plus-output `Limit`, and treat `BufferTokens` as occupied.

Where no provider figure exists at all, this library's estimate is the only instrument available and
rule 5 is the thing that makes its error survivable: a bad estimate produces a seed that does not
fit, which is measured and corrected rather than predicted and asserted.

### Worked example

With `VerbatimTurns = 20`, `SlotsPerTier = 4`, `TierCount = 3`:

```text
rotation 1-4:   T1[A B C D]                           T2[]           T3[]
rotation 5:     T1[E]         A B C D --consolidate--> T2[w]
rotation 9:     T1[I]         E F G H --consolidate--> T2[w x]
rotation 17:    T1[Q]                                  T2[w x y z]
rotation 21:    T1[U]         w x y z --consolidate--> T3[a]
...
T3 full        rule 4 drops its oldest slot, roughly 320 turns of history, as one block
```

### Why this keeps the recall that was measured

A flat rolling summary re-summarizes its own summary on every rotation, so after fifty rotations a
turn has been through fifty lossy passes. Measured, recall collapsed to zero past fifteen rotations.
Tiered retention held between fifty and one hundred percent out to fifty rotations, for seventeen
percent less summarizer cost.

Under this structure a turn is summarized **once per tier — three times in its whole life**. Slot `w`
is written at rotation five and is not touched again until rotation twenty-one.

**An honest qualification the first draft got wrong.** That measurement compares tiered against flat,
and the current design is already tiered, so at first sight it is evidence for a choice that is not
being changed. But the current implementation consolidates each tier's *standing record together with
the new material* on every rotation — a flat ratchet hiding inside each tier. The proposed rule 3 is
batch-then-clear: `SlotsPerTier` slots are consolidated once and the tier is emptied. So this is a
real change, and the measured number should be a **lower bound** on what it achieves rather than a
figure that transfers intact.

It should also cost less: one summarizer call per rotation plus amortized fractions for cascades,
against the current cascade path's two calls per tier.

### Aggressiveness

The level is session state, not a setting. It adapts, so an initial value is a guess about something
that will be overwritten within a few turns.

| Level | Verbatim kept | Instruction to summarizer |
| --- | --- | --- |
| Low | `VerbatimTurns` | Summarize concisely. |
| Medium | half | Be terse: decisions, facts and open threads only. |
| High | a quarter | One or two lines. Essential facts only. |

The level is never a number handed to the model. `ConsolidationRequest` carries the **instruction**,
not a budget.

Escalate when the window fills again within *k* turns of a rotation, and relax after *m* turns
without filling, with `m > k` required so the two predicates cannot both hold. Both are **internal
constants derived from `VerbatimTurns`** — *k* is `VerbatimTurns` and *m* is twice that — because an
application cannot tune them better than we can and they are two more concepts to learn. The level is
also re-evaluated against each freshly built seed, so a seed that does not fit escalates immediately
rather than a rotation later.

### Prompt caching falls out of the ordering

The seed is built coarsest first: tier three, then two, then one, then the verbatim tail. Tier three
changes roughly every sixty-four rotations, tier two every sixteen, tier one every rotation, and the
tail every turn. The further back in the prefix, the more stable the content — the shape a prefix
cache rewards.

This is why a dropped slot is discarded whole rather than trimmed gradually: shaving the oldest
characters off the front on every rotation would move the cached prefix every time.

Both provider families report cache telemetry — Copilot's `CacheReadTokens` and `CacheWriteTokens`,
and `CachedInputTokenCount` on the usage a chat client reports — so this is measurable on either, and
is not a Copilot-shaped argument.

## What is kept, and what is removed

**Kept, because it is measured, tested and sound:**

- Rotation as replacement: build a new provider session from a seed, then dispose the old one
- The provider seam, and provider-session ownership and disposal across every failure path
- Seed construction and its coarsest-first ordering
- The append-only transcript
- Out-of-session summarization
- **Normalization of a blank summarizer answer to empty, at the single boundary where it crosses.**
  Without it a blank slot silently erases a tier's material and the window keeps filling, so
  escalation fires for the wrong reason.
- **Detection that compaction bought nothing** — now expressed as rule 5 reporting that material was
  dropped, which is a fact rather than a ratio, and is the only thing standing between "escalated to
  High and still rotating every turn" and total silence.

**Removed:**

- Per-tier token budgets and the construction-bound arithmetic
- The fold, in all its forms
- The runtime convergence guard and the mid-conversation exceptions it throws
- `MinimumEffectiveWindowTokens` and the saturation ratio
- The integer-saturation family
- `ConsolidationRequest.BudgetTokens` and `IsDegradation`
- `ContextLayout` and `ContextTier` from the public surface
- `RotationEngine` from the public surface
- The tool-pair snapping rule, retired by turn-granular boundaries

The public type count must go **down**. If this lands with the surface still in the twenties, the
failure mode has been re-expressed in counts instead of tokens.

## Two internal mechanisms that are not optional

Neither is user-visible; both are hard failures if omitted.

**Chunking the summarizer's input.** Rule 2 hands the summarizer everything older than the tail. On
the first rotation of a large-window provider that can be hundreds of thousands of tokens going into
a summarizer whose own window is smaller. Oversized material must be chunked and the chunks
consolidated into the one slot.

**Sizing the seed before sending it.** Rule 5 requires measuring what we built. On a provider that
reports only after a turn, that measurement is this library's estimate — which is acceptable here
precisely because it is checked again after the next turn against the provider's own figure.

## The trade this asks you to accept

An application can no longer be told, before a session starts, that its context will never exceed a
stated number of tokens.

That number was computed in estimated tokens and was measurably wrong: it accepted a configuration
that then rotated twenty-three times in thirty turns. What replaces it is **escalate until it fits,
and drop until it fits** — a guarantee that terminates by construction, because rule 5 has a floor.

**The reassurance the first draft offered here was itself unsound** and is worth naming, because it
is the same mistake in miniature. It claimed a slot's size does not grow with the conversation
because each slot summarizes a bounded input. Bounded in *count of slots*, not in tokens — and the
argument that demolishes budgets demolishes that too.

The sounder version rests on the same measurement: summarizer output was roughly constant regardless
of what was asked, which means slots land at the model's natural summary length at every tier. That
has a consequence worth stating plainly: the floor on context is roughly
`SlotsPerTier × TierCount × natural-summary-length` — with the constants above, twelve slots — plus
the tail. A window smaller than that floor cannot hold a full structure.

The old design refused such a configuration at construction, in the wrong currency, and got it wrong.
This design does not refuse it: rule 5 runs the session with fewer live slots and says so. That is
the right answer, and it costs one rule rather than a bound.

## Risks this proposal accepts

- **Cost and latency are unbudgeted.** Every rotation is a summarizer call plus a provider-session
  rebuild. Rule 5 bounds the *size* of the context, not the *frequency* of rotation. An application
  that wants to bound spend must watch `RotationCount` and the reported level.
- **Summarizer failure mid-rotation** must leave the session usable on its existing provider session.
- **A misconfigured window on a provider that reports none** is corrected by rule 5 only after the
  fact, and a badly wrong configured window will still surface as a provider-side overflow error.
- **Removal is a breaking change** to published API: `MinimumEffectiveWindowTokens`,
  `EffectiveWindowTokens`, tier budgets and the saturation types are all public today.

## The gap this proposal does not close

The diagnosed root cause of the current design's problems is that **nothing has ever used this
library**: no shipped provider adapter, no shipped summarizer, no sample. An application author must
implement `IProviderSession`, `IProviderSessionFactory` and `ISummarizer` before sending one message,
against an alternative — `IChatClient.GetResponseAsync(messages)` — that is a single call.

Changing the compaction algorithm does not address that, and this proposal should not be approved as
if it did. It should land with the seams **filled**:

- A summarizer over an `IChatClient`, which is a small amount of code and removes an entire interface
  from the author's path
- A Copilot provider adapter
- One existing sample switched to a session

Until then, "AgentKit guarantees the mechanism" is not true of this package: the mechanism has a hole
in it where the author is expected to stand.

## Open questions

1. Should the reported level be on each turn's response, on the session, or both?
2. When the level changes, does the shortened verbatim tail take effect immediately or at the next
   rotation? This is directly observable as how much history survives under pressure.
3. Should `VerbatimTurns` remain configurable at all, or become a constant like the other two? It is
   the only one an author can reason about, which argues for keeping it — but the same argument was
   once made for tier budgets.
