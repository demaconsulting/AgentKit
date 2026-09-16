## ContextUsage

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The `ContextUsage` unit publishes the one shape both provider families are reduced to, and
`IContextUsageReporter`, the optional contract a provider session implements when it can account for
its own window.

### Purpose

Providers disagree about what they will tell us. A GitHub Copilot session reports its current token
count and its token limit after every turn. A provider reached through an `IChatClient` reports
neither, and the window size has to be configured by the application instead. The compaction engine
cannot have two behaviors, so both shapes are reduced to this one figure.

`Origin` is not decoration. A provider-reported figure counts what the provider actually charged,
including framing this library never sees; an estimated figure is derived from a character ratio and
is only as good as that ratio. Surfacing the difference lets an application log it, and lets a test
assert that a provider's own numbers were preferred when they were available.

`ConversationTokens` is carried rather than derived by the consumer, and that is what this shape is
for. Every rotation decision is made against the conversation alone — the window less what the
system prompt and the tool declarations occupy — and the only honest way to obtain that figure is to
ask whoever produced the totals. A provider reporting one counted it with the same tokenizer it
counted everything else with. Working it out instead by subtracting `AgentSessionOptions`'
character-ratio estimate of the fixed overhead would produce a number in neither currency, and the
error would be inherited by every threshold comparison downstream; tool declarations are nested JSON
schemas, which is the material a four-characters-per-token ratio serves worst, so the error is not
small. `OverheadTokens` is therefore the derived value, and it is derived in whatever currency the
figures arrived in.

**Why the split is not carried as separate system and tool-declaration counts.** A provider that
reports them — Copilot reports `systemTokens` and `toolDefinitionsTokens` — reports them as a
breakdown of a total the engine already has. Nothing in the engine reads them apart: every decision
needs "the conversation" against "everything else", and that is one number. Carrying three counts
would introduce an invariant between them that a provider is free to break, forcing a choice between
refusing a real provider's figures and holding counts that silently disagree with their own total.
It would also publish fields with no consumer, and the one field that is published would be the only
one anything read. A provider that offers a finer breakdown can be logged by the adapter that
receives it; the engine asks for, and is given, the one distinction it acts on.

`IContextUsageReporter` is deliberately **separate and optional**. Requiring every provider session
to report usage would force an adapter for a provider that reports nothing to invent a number, and
an invented number is indistinguishable from a real one at the point it is consumed. Keeping the
capability in its own interface lets an adapter say "I do not know" by simply not implementing it.

### Data Model

`ContextUsage` properties, immutable after construction:

- **`UsedTokens`** (`int`) — Not negative. Counts everything the provider holds: system prompt, tool declarations
  and conversation
- **`WindowTokens`** (`int`) — Positive
- **`ConversationTokens`** (`int`) — Not negative, and not greater than `UsedTokens`. The figure a rotation
  threshold is compared against
- **`Origin`** (`ContextUsageOrigin`) — `Provider` or `Estimated`
- **`OverheadTokens`** (`int`) — Derived: `UsedTokens - ConversationTokens`; the system prompt and tool
  declarations as whoever produced the figures counted them
- **`FreeTokens`** (`int`) — Derived: `Max(0, WindowTokens - UsedTokens)`
- **`UsedFraction`** (`double`) — Derived: `UsedTokens / WindowTokens`; may exceed one

`ContextUsageOrigin` values: `Provider` (the provider reported the figure) and `Estimated` (derived
from this library's own transcript because the provider reports nothing).

`IContextUsageReporter` exposes `CurrentUsage`, a `ContextUsage?` read after every turn. An
implementation must not contact the provider to answer: it reports what the last exchange already
revealed, so reading it is free and cannot fail. An implementation whose provider distinguishes the
conversation from its framing passes that split rather than leaving it to be inferred.

**Reporting late costs accuracy, not the measurement itself.** The fold an unsplit figure hides is
measured once per provider session, at the first instant that provider session produces a figure of
its own. For an implementation reporting from creation that instant is creation, where the
conversation is exactly the history the session was seeded with and the subtraction is exact. For an
implementation that returns `null` at creation — which this contract explicitly invites — it is the
first turn the implementation does report on, and the subtrahend is this library's own estimate of
the conversation by then, so a turn's worth of estimating error sits inside the result. That error
is credited as fold, and the fold only ever *raises* the bound a rotation threshold must exceed, so
an over-credit refuses a marginal window rather than accepting one that cannot settle.

What reporting late no longer does is leave the fold unmeasured. An implementation silent at
creation used to be credited a fold of zero and to keep it until the next rotation created a new
provider session, so a provider charging real overhead it never breaks out was treated as charging
none: a window it cannot actually converge in was accepted and the session rotated on every turn
without raising a saturation signal. Reporting from creation, or reporting the split, remains what
an implementation *should* do, because it is the difference between an exact measurement and an
approximate one — but neither is required for the fold to be measured at all.

**Usage is permitted to exceed the window.** A provider may report that, and clamping it would hide
exactly the condition an application most needs to see. `FreeTokens` floors at zero because a
window that is over-full has no negative amount of room, but neither `UsedTokens` nor `UsedFraction`
is clamped.

**A conversation exceeding the usage is not permitted**, which is the deliberate opposite. No
accounting produces it, and it would make `OverheadTokens` negative — which would *enlarge* the
window a rotation threshold is taken from and let a session run past the provider's own compactor,
the one failure this package exists to prevent.

### Key Methods

#### ContextUsage(int usedTokens, int windowTokens, int conversationTokens, ContextUsageOrigin origin)

Validates before any assignment so an unusable figure never exists even briefly. A negative
occupied count could only come from a defect and would make every threshold comparison meaningless;
a window of zero would leave nothing for the occupied fraction to be a fraction of; a conversation
count outside the range zero to `usedTokens` describes no context that could exist.

`origin` must be a defined `ContextUsageOrigin` member, checked with `Enum.IsDefined` as
`TranscriptEntry` checks its kind. Everything that reads the origin asks only whether it is
`Provider`, so a cast integer would be accepted and then silently read as an estimate: it would take
the configured-window threshold path and skip the reported-window bound check entirely. A value that
names nothing must not select behavior, so it is refused where the caller wrote it.

#### FromProvider(int usedTokens, int windowTokens, int? conversationTokens = null)

Creates a figure marked `ContextUsageOrigin.Provider`. Used by an adapter reporting its provider's
own account. An adapter whose provider reports a conversation count passes it, and every threshold
comparison downstream is then made entirely in that provider's own tokens — this is the seam a
Copilot adapter uses, mapping `currentTokens`, `tokenLimit` and `conversationTokens` straight across
with no arithmetic. An adapter that receives only totals omits it, and the whole of the usage is
treated as conversation: that credits the session with no overhead allowance, which rotates strictly
earlier than a correct split would and so cannot let the session run past the provider's compactor.
That is a claim about the rotation trigger alone. The convergence check a session makes before
accepting a window cannot inherit it — crediting no overhead makes a window look *larger* than it is
— so `CompactingAgentSession` measures what an unsplit figure folds in, per provider session and at
the first instant that provider session reports anything, and credits that instead. An adapter
reporting totals alone should therefore prefer to report them from the moment a session exists
rather than only after its first turn, because that is the one moment the measurement is exact; one
that begins reporting later has it taken approximately, and in the safe direction. See
*CompactingAgentSession Unit Design*.

#### FromEstimate(int usedTokens, int windowTokens, int? conversationTokens = null)

Creates a figure marked `ContextUsageOrigin.Estimated`. Used by `CompactingAgentSession` when the
live provider session offers nothing, passing `ContextLayout.ConversationTokens` alongside
`ContextLayout.TotalEstimatedTokens` so that both sides of the figure are estimated and neither is
contaminated by the other currency.

### Error Handling

- **Negative occupied count** — `ArgumentOutOfRangeException` propagates
- **Non-positive window size** — `ArgumentOutOfRangeException` propagates
- **Conversation count negative or exceeding the occupied count** — `ArgumentOutOfRangeException` propagates
- **Undefined origin** — `ArgumentOutOfRangeException` propagates
- **Provider cannot account for its window** — Reported as `null` from `CurrentUsage`; not an error
- **Provider reports totals but no conversation split** — The whole usage is treated as conversation; not an error

The last two rows are the important ones: not knowing is a legitimate answer, expressed as an
absence rather than as a fabricated figure or an exception, and knowing only part of the answer is
resolved by claiming no overhead rather than by estimating one.

### Dependencies

None. The class performs arithmetic on integers and holds no references.

### Callers

`CompactingAgentSession` reads `CurrentUsage` from its live provider session after every turn,
preferring it over its own estimate, and publishes the result as `IAgentSession.Usage` and on every
`AgentSessionResponse`. It compares `ConversationTokens` against a threshold taken from
`WindowTokens` less `OverheadTokens`, so nothing it does mixes the two currencies.
`InMemoryProviderSession` implements `IContextUsageReporter` and can be configured either to report
or to report nothing, so both engine paths are reachable without a live model; when it reports, it
reports the conversation separately, as a real reporting adapter does.
