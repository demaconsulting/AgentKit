## TokenEstimator

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The `TokenEstimator` class estimates the token cost of session material, so the engine can decide
without asking a provider.

### Purpose

Two provider shapes must be served by the same engine, and they disagree about what they will
reveal. One reports current and maximum token counts after every turn; the other reports nothing and
leaves the window size to be configured. The engine therefore needs one arithmetic it can always
perform, and uses a provider's own numbers in preference when they are offered.

**Why a character ratio rather than a real tokenizer.** A tokenizer is provider- and model-specific,
changes with model releases, and would make this package's behavior vary across upgrades. Every
number this engine computes feeds a *threshold* decision — rotate, or do not rotate yet — taken at
roughly 70 percent of the effective window, which leaves around 30 percent of headroom for the
estimate to be wrong in. A cheap, stable, deterministic ratio is therefore the right instrument, and
being deterministic is what lets the rotation engine be unit-tested without a model at all.

The class is static, holds no state, and is safe for concurrent use.

### Data Model

The class holds no instance state. Its published constants are:

- **`CharactersPerToken`** (4) — A rule of thumb for English prose and source code under common tokenizers, not a
  measurement of any particular model. The same ratio is recorded in AgentKit Core's tool ceilings. Deliberately a
  whole number so every estimate is reproducible by hand when reviewing a test
- **`PerEntryOverheadTokens`** (4) — Every entry a provider receives is framed — a role marker, delimiters, and for
  a tool call or result an identifier tying the pair together — and that framing is charged even when the text is
  short. Large enough that the under-count does not accumulate over a long run of small entries, small enough that
  it never dominates
- **`PerToolOverheadTokens`** (8) — A declaration is wrapped in provider-specific structure heavier than a message
  envelope. Larger than the entry allowance because declarations are fixed overhead subtracted before any
  percentage, so under-counting them would inflate the effective window and delay rotation

### Key Methods

#### EstimateTokens(string? text)

Returns zero for null or empty text — the honest answer for absent content — and otherwise
`ceil(text.Length / CharactersPerToken)`.

**Rounding up is load-bearing.** A budget comparison that treated short content as free would let an
unbounded number of short entries accumulate inside it.

#### EstimateEntryTokens(TranscriptEntry entry)

Returns `EstimateTokens(entry.Text) + PerEntryOverheadTokens`. The framing allowance is included
here rather than left to each caller to remember, so no call site can forget it.

**Throws:** `ArgumentNullException` for a null entry — a null estimated as zero would silently
understate a transcript.

#### EstimateToolDeclarationTokens(IReadOnlyList&lt;AIFunction&gt;? tools)

Returns zero for a null or empty list. Otherwise sums, per tool, the estimated tokens of the name,
the description and the JSON schema, plus `PerToolOverheadTokens`. Those three pieces of text are
what a provider is given: the name a model selects by, the description it selects on, and the schema
it fills in.

**Throws:** `ArgumentException` for a null tool — skipping it would understate the fixed overhead
and delay rotation past the point it was meant to fire, which is a quiet miscalculation rather than
a visible failure — and for a declaration block whose estimate exceeds a token count.

**Why the total is accumulated wide.** Each declaration fits a token count on its own, because a
string cannot be longer than the largest representable length and the ratio only divides. The sum
need not, and an `int` accumulator would wrap it to a small or negative figure that every site
downstream would consume as a real measurement of the fixed overhead: the effective window, the
rotation threshold and the published overhead would all then describe a window nobody configured.
The declarations are where that figure first becomes computable, so it is accumulated in a wider type
and rejected here rather than re-checked at each later site. It is not reachable by any test this
build will run; see *TokenEstimator Unit Verification Design*.

**Order of magnitude.** The compaction spike that preceded this package measured a declaration block
of 2,589 tokens for a set of 11 tools (n = 11 tools, one measurement, recorded in that spike). It is
quoted to show the scale involved — thousands of tokens, not tens — and not as a value this method
reproduces.

### Error Handling

- **Null or empty text** — Returns zero; not an error
- **Null transcript entry** — `ArgumentNullException` propagates
- **Null or empty tool list** — Returns zero; not an error
- **Null tool within the list** — `ArgumentException` propagates
- **Declaration block larger than a token count** — `ArgumentException` propagates, rejected before
  the wide total is narrowed

### Dependencies

- **SessionTranscript** — supplies `TranscriptEntry`; see *SessionTranscript Unit Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies `AIFunction`.

### Callers

`TranscriptEntry` and `ContextTier` call `EstimateTokens` at construction, so the estimate is
computed once rather than re-derived on every rotation. `AgentSessionOptions` calls
`EstimateTokens` and `EstimateToolDeclarationTokens` to measure the fixed overhead.
`RotationEngine` calls `EstimateTokens` to decide whether a consolidated record fits its tier and
whether it saturated. `InMemoryProviderSession` uses both to account for its own simulated window.
