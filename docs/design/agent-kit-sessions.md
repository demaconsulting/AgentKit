# AgentKitSessions System Design

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The AgentKitSessions system is an AgentKit-owned agent session with automatic context compaction,
built so that the same session behaves identically on every provider. This increment is the
provider-agnostic engine only: it contains no Copilot and no `IChatClient` wiring, and everything in
it is deterministic and exercisable without a live model.

## Purpose

AgentKit ships guarded tools that behave identically on two provider backends. A guarded tool set is
of no use to an agent that cannot run long enough to use it, and until now the repository had no
session lifecycle at all — so a long-running agent was impossible and the kit could not fulfil its
stated purpose. This system adds the missing half.

An application asks this system for a session, sends messages to it, and reads answers back. When
the conversation grows past what the provider's context window can hold, the session compacts itself
and carries on. The application is told that this happened, so it can log it or act on it, but it is
never required to manage it.

## Architecture

The system is deliberately flat: twelve units sit directly under the system with no intervening
subsystems, mirroring how `AgentKitCore` is organized. The units divide into four groups, which are
boundaries of responsibility rather than subsystems:

- **The contract an application programs against** — `AgentSession` (the `IAgentSession` interface
  and the per-turn response), `AgentSessionOptions` (what an application configures), and
  `CompactionPolicy` (the controls governing compaction).
- **The accounting** — `TokenEstimator` (the deterministic arithmetic), `ContextUsage` (the one
  usage shape both provider families are reduced to), `SessionTranscript` (the append-only verbatim
  record), and `ContextLayout` (the whole context as this system accounts for it).
- **The engine** — `RotationEngine` (the deterministic aging function) and `Summarizer` (the
  injected out-of-session consolidation contract and its documented default prompt).
- **The provider seam** — `ProviderSession` (the minimal adapter interface),
  `InMemoryProviderSession` (a provider that contacts nothing), and `CompactingAgentSession` (the
  implementation that sequences all of the above).

A session runs as follows. `CompactingAgentSession` records each outgoing message and everything a
turn produced in its own `SessionTranscript`, held inside a `ContextLayout`. After each turn it asks
the live provider session for its own account of the window, falling back to `TokenEstimator` when
the provider offers none. When the conversation — the part of the usage outside the system prompt and
the tool declarations, carried on the usage figure by whoever produced it — reaches
the rotation threshold, it calls `RotationEngine`, which consolidates older history through the
injected `ISummarizer` into the coarse tiers of the layout. The session then creates a replacement
provider session from `ContextLayout.BuildSeed`, and only then disposes the one it replaced.

**The threshold is derived from whichever window the usage figure was measured against.** A provider
that reports its own window governs; the threshold `AgentSessionOptions` computed from the
configured window governs only the family of providers that report nothing. Both apply identical
arithmetic — remove the overhead, apply the policy's rotation fraction, floor at one token —
so they differ only in which window they measure and whose overhead they remove. A reported window
has the provider's own reported overhead removed; a configured window has this library's estimate of
it removed. The two are never crossed, because subtracting an estimate from a measurement gives a
figure in neither currency. Rotating at a fraction of the window exists to
keep the provider's own compactor from ever firing, and that guarantee is about the window the
provider actually has, so a configured window that disagrees with a reported one does not decide.

### Rotation, Not In-Place Reduction

When the context fills, this system summarizes older history, creates a fresh provider session
seeded with the preserved content, and only then disposes the one it replaced.

**This is the only reduction mechanism both provider shapes support.** One provider shape re-sends
the whole history on every turn and would accept an edited history; the other keeps history
server-side and offers no supported way to retract a turn from it. Editing in place therefore works
on one and not the other, and an engine that behaved differently on the two would defeat the
repository's central promise. Rotating — replace the session, seed the replacement — works on both.
Disposal is part of the mechanism rather than housekeeping: for a provider holding the conversation
server-side, disposal is what actually discards it.

### Tiered Retention With Fixed Per-Tier Budgets

Context is laid out most-stable-first:

```text
[system][tool declarations][tier N coarse] ... [tier 1][tier 0 verbatim]
```

Tier zero holds the most recent turns verbatim; each higher tier holds a progressively coarser
record of older history. Every tier carries a fixed token budget, and the budgets are what make the
arrangement bounded.

The alternative — a single rolling summary — was measured and rejected. Over 50 rotations with a
deliberately compressed window (n = 50 rotations, one run per arrangement, recorded in the
compaction spike that preceded this package), recall by rotations-ago was:

- **Flat rolling summary** (100% at 0–4, 50% at 5–9, 67% at 10–14, 0% beyond 15) — 5 of 23
- **Tiered** (50–100% held out to 50 rotations) — 13 of 17

The tiered arrangement also spent 17 percent fewer summarizer tokens (381,440 against 461,173),
because it consolidates only the tiers that actually overflowed. The mechanism behind the flat
arrangement's collapse is that repeated re-summarization is a downward ratchet: its final context
had shrunk to 363 tokens against the tiered arrangement's 3,158.

**The cost is real and is recorded here rather than left for someone to discover.** Tier content
occupies window space, so the tiered arrangement completed roughly 27 percent fewer turns of work
per rotation. That is the price of remembering, and it is the trade this system deliberately makes.

### Aging Only at Rotation, in a Batch

Between rotations the context is strictly append-only: nothing already sent is ever rewritten. That
is what preserves a provider's prompt caching, since an in-place edit anywhere in the history
invalidates the cached prefix for every following turn. At rotation, every overflowing tier
consolidates at once, cascading into coarser tiers where required. Batching costs nothing extra,
because a rotation invalidates the cache anyway.

### Rotating at Seventy Percent of the Effective Window

The effective window is the provider window minus the system prompt and the tool declarations — both
fixed overhead present on every turn and never consolidated. Rotation fires when the conversation
reaches 70 percent of what is left.

Applying the percentage to the raw window instead would make the rotation point drift with how many
tools an application attached: attach more tools and the agent silently gets less conversation
before rotating. Tool declarations are not a small correction — the compaction spike *estimated* a
declaration block of 2,589 tokens for a set of 11 tools (n = 11 tools, one measurement, recorded in
that spike) — so removing them before the percentage is applied is what makes the threshold mean
what it says.

That figure is also the reason the removal must be done in the right currency. 2,589 is a
character-ratio estimate of JSON schemas, which is the material the ratio serves worst, so a
provider's real count for the same declarations may differ substantially. When the provider reports
its own overhead, that is what is removed from its own window; the estimate is removed only from the
window a host configured, for the provider family that reports nothing.

Seventy percent leaves 30 percent of headroom, which covers both the error in a character-ratio
token estimate and the turn in flight when the threshold is crossed.

**A rotated context must land below the rotation threshold. That is the convergence invariant, and
it is a constraint on sizing rather than a property of the mechanism.** A rotation leaves the
conversation holding at most the sum of the tier budgets plus the framing their seeded records
carry. If that figure is not strictly below the threshold, the layout a rotation produces is already
over the threshold, so the next turn rotates again — and every turn after it, indefinitely, while
raising no saturation signal, because each individual consolidation reduces perfectly normally. The
tier budgets merely make a rotated context *fit* the window; landing below the threshold is what
makes the session *settle*, and the two conditions are separated by a factor of the rotation
fraction. `AgentSessionOptions` refuses any configuration that fails the invariant, and
`CompactingAgentSession` refuses any provider-reported window that fails it — crediting, in the
reported case, any fixed overhead the provider charges for and does not break out of its conversation
figure, because a rotated context will still be counted as carrying it. That overhead is measured
rather than estimated, per provider session and at the moment each one is created — it is what the
provider reports as conversation over and above this library's count of the content that session was
seeded with, which for the first session of a conversation is nothing at all. It therefore travels
with the live provider session and is replaced when a rotation replaces it; a figure measured once
and reused refused convergent replacements in one direction and accepted thrashing ones in the
other. The rotation trigger's
zero-overhead default for an unsplit figure is safe only for the trigger, where it fires early,
and errs the opposite way here.

How much headroom there is beyond the one guaranteed turn depends on how generously the window was
sized against the tier budgets. With the tiers sized in the low thousands of tokens against a window
several times larger, a rotation lands the session near 40 percent rather than just under the
threshold and rotation is comfortably infrequent; sized close to the invariant's minimum, the same
mechanism guarantees only that a rotation is followed by at least one turn that does not rotate.
The earlier claim that "a rotation lands the session near 40 percent" was stated as though it were a
property of the mechanism; it is a property of that sizing.

### The Summarizer Runs Out of Session

Consolidation is a separate stateless call that receives the material as input. Asking the live
session to summarize itself was measured and rejected: it consumes the session's own context to
produce the summary and triggers the provider's built-in compactor, which is self-defeating. The
system therefore maintains its own transcript, which also means the material is still available when
a provider session has been disposed — exactly when a fresh one must be seeded.

### Never Asking a Model to Hit a Token Budget

The consolidation prompt asks for specificity and content: every file path, value, decision and
reason, constraint, error and resolution, and outstanding item. It names no target size.

Asking for one does not work. In the compaction spike, consolidations asked for between 9,870 and
19,741 tokens returned 1,665 and 4,259 tokens (n = 2 requests, recorded in that spike). A model
cannot count its own output. The engine therefore measures the result itself and treats output size
as a signal about how much information the material carried rather than as something to be dictated.

### Consolidation as a Ratchet

Each consolidation receives the previous record for that material as an **input**, not as context,
and must not drop detail that record kept — unless the consolidation is deliberately degrading the
material to a coarser tier. That is the distinction between a legitimate coarsening and an
accidental loss, and it is what stops the tiered arrangement from decaying into the flat one.

## External Interfaces

- **`IAgentSession`** — Direction: Inbound (application to system); Format: .NET interface; Constraints: One
  session is one conversation; turns are sequential; the session must be disposed
- **`ISummarizer`** — Direction: Outbound (system to application); Format: .NET interface; Constraints: Stateless;
  must run out of session; must not return null; must be safe for concurrent use
- **`IProviderSessionFactory` / `IProviderSession`** — Direction: Outbound (system to adapter); Format: .NET
  interface; Constraints: The factory must be safe for concurrent use; a session serves one conversation
- **`IContextUsageReporter`** — Direction: Outbound, optional; Format: .NET interface; Constraints: Implemented
  only by a provider session that can account for its own window; must not contact the provider to answer

Tools are carried as `Microsoft.Extensions.AI` `AIFunction` instances, which is the same tool
currency the rest of AgentKit uses, so a session accepts exactly what a tool pack produces with no
conversion layer between them.

## Dependencies

- **Microsoft.Extensions.AI.Abstractions** — supplies `AIFunction`, the tool currency the session
  options carry and the provider-session seed hands to an adapter; see
  *Microsoft.Extensions.AI.Abstractions Design*.

**This system takes no reference on AgentKitCore.** It composes a session around tools an
application already holds and needs none of Core's guarded-construction contract to do so. Adding an
unused reference purely for family symmetry would misrepresent the dependency graph and the SBOM.
The relationship is conceptual rather than compiled: the tools an application puts into
`AgentSessionOptions` are exactly the ones `ToolPackBuilder` produces.

**This system takes no provider dependency at all.** `Microsoft.Agents.AI` and
`Microsoft.Agents.AI.GitHub.Copilot` are carried by the two provider-adapter systems, and this
increment deliberately adds no third. The provider seam is `IProviderSession`, which an adapter
implements; the adapters that will do so are a later increment.

## Risk Control Measures

The segregation that matters here is between the **engine** and any **provider**. The engine never
touches a provider API: it produces a `ProviderSessionSeed` and consumes a `ProviderTurn`, and an
adapter does everything else. That boundary is what makes the compaction behavior identical across
providers rather than merely intended to be, and it is what allows the whole engine to be verified
against `InMemoryProviderSession` with no network access, no credentials and no model.

The second control is that the context is **bounded by construction, in estimated tokens**, rather
than by convention. The total is the system prompt, plus the tool declarations, plus the sum of the
tier budgets, plus the framing each tier record carries when it is seeded into a replacement
session. The framing is counted because it is part of what the provider receives: a bound counting
raw tier content alone would be exceeded by a seed in which every tier sat exactly within its budget,
and for a provider that reports no usage that under-count is what would drive rotation. Every term is
measured by `TokenEstimator`'s four-characters-per-token ratio, so the bound is a rule of thumb held
within the headroom the rotation fraction reserves, not a claim about what a provider's tokenizer
will charge.

`AgentSessionOptions` refuses at construction any configuration in which a rotated context of that
size would not land **below the rotation threshold**, and `CompactingAgentSession` applies the same
refusal to a provider-reported window. Asserting only that the window *holds* the bound is the
necessary condition, not the sufficient one: a window between the bound and the bound divided by the
rotation fraction holds a rotated context and still rotates on every turn. A session that constructs
is one whose arrangement fits **and** settles, as this library measures the context.

The currency discipline is what keeps that honest. A figure a provider reported and a figure this
library estimated are never mixed in one subtraction, and a decision made in one currency is never
allowed to be overruled by a computation in the other. `ContextUsage` carries the conversation count
alongside the totals, so whoever produced the figures also produced the split, and every rotation
comparison is made wholly in reported tokens or wholly in estimated ones. `FixedOverheadTokens` is an
estimate and is applied only to the configured window. Where the two roles genuinely differ the
currency is passed along rather than assumed: a provider knows *whether* the context is too large,
the estimator is all there is for deciding *what* to consolidate, so `RotateAsync` is told which
currency crossed the threshold and a provider-reported crossing forces a real consolidation even
where the estimated split sees room left in tier zero. The one place the two currencies necessarily
meet — comparing an estimated tier bound against a reported window, when a reported window is
refused — is documented as approximate rather than presented as exact.

The third is **saturation detection**. An agent whose context holds no redundancy left will keep
crossing the rotation threshold, spending summarizer tokens and buying nothing, while every rotation
appears to succeed. The system detects that and surfaces it; it deliberately does not act on it,
because what to do about a saturated agent depends on what the application is for.

## Data Flow

```text
application message
  -> IProviderSession.SendAsync
  -> on success, the message and the ProviderTurn entries (which end with the answer) are recorded
     together in SessionTranscript (inside ContextLayout); a turn the provider never accepted
     records nothing
  -> usage read from IContextUsageReporter, or estimated by TokenEstimator
  -> if conversation tokens < threshold: return the answer
  -> otherwise:
       RotationEngine.RotateAsync(layout, summarizer, usage origin)
         -> SessionTranscript.SplitAtBudget (snapping tool pairs); a provider-reported crossing
            that the estimated split sees no overflow for splits at zero instead, consolidating
            the whole verbatim history
         -> ISummarizer.ConsolidateAsync per overflowing tier, cascading
         -> new ContextLayout + saturation reports
       IProviderSessionFactory.CreateAsync(ContextLayout.BuildSeed())
       dispose the replaced IProviderSession
  -> return the answer, the usage, the rotation flag and any saturation
```

## Design Constraints

- **Provider-agnostic.** No type in this system names a provider, and no code path in it performs
  network access.
- **Deterministic.** Every decision the engine makes is arithmetic over the layout it was handed.
  Injecting the summarizer isolates the single non-deterministic collaborator, which is what makes
  the heart of the system unit-testable without a model.
- **Tool call and result pairs are indivisible.** A tier boundary falling between them is snapped,
  because some providers reject an orphaned pair outright and no model can interpret one.
- **Published collections are owned copies behind read-only views.** Every type in this system that
  publishes an `IReadOnlyList` copies the caller's collection at construction and hands out a
  read-only view of that copy, never the array or list itself. An `IReadOnlyList` over a bare array
  can be cast back to the array and written through, which for these types would let a cached token
  total, a saturation verdict or a validated seed disagree with its own contents.
- **Bounded by construction, asserted.** See *Risk Control Measures* above. The bound is in estimated
  tokens, and it is a
  post-rotation property: between rotations tier zero is append-only and grows past its budget,
  which is precisely what the rotation threshold's headroom is reserved for. The assertion is the
  convergence invariant — a rotated context lands below the rotation threshold — not merely that it
  fits the window.
- **One currency per comparison.** A provider-reported figure and an estimated one are never combined
  in a single subtraction, and no decision made in one currency is silently overruled by a
  computation in the other. `ContextUsage` carries the conversation count so the split is made by
  whoever made the totals, and `AgentSessionOptions.FixedOverheadTokens` is applied only to the
  configured window. Where a decision in one currency must feed a computation in the other, the
  currency travels with it: `RotationEngine.RotateAsync` takes the `ContextUsageOrigin` of the
  crossing that triggered it, and a provider-reported crossing consolidates the whole verbatim
  history rather than letting an estimated split conclude there was nothing to do.
- **A rotation that consolidates nothing is not a rotation.** When the transcript already fits tier
  zero the engine returns the layout unchanged and reports no consolidations, and
  `CompactingAgentSession` treats that as a turn that did not rotate. Replacing a provider session
  to arrive at the context the session already had costs a session per turn and is invisible,
  because nothing consolidated and so nothing could saturate.
- **One definition of empty, established at the boundary.** `ISummarizer` forbids only null, so a
  whitespace answer is contract-conformant. `RotationEngine` normalizes a blank answer to an empty
  string where it receives it, before the value is sized, cascaded on or stored, so a tier record, a
  cascade's older record and a consolidation's material are one thing rather than three readings of
  the same string. The blank tests those consumers carry remain for the one route a summarizer does
  not take: a layout a host composed through `ContextTier`'s public constructor.
- **A diagnostic states facts.** A message reports what was attempted and what is known, never what
  was hoped for. The refusal of an unusable reported window says the provider's release was
  attempted, because the release it makes can fail and the flag it leaves behind says so.
- **Multi-platform and multi-runtime.** Windows, Linux and macOS; .NET 8, 9 and 10, matching the
  rest of the repository.

## Structure

- **AgentSession (Unit)** — the `IAgentSession` contract and the per-turn response.
- **AgentSessionOptions (Unit)** — the configured instructions, tools, window, policy and
  summarizer, and the fixed overhead, effective window and rotation threshold derived from them.
- **CompactionPolicy (Unit)** — the validated tier budgets, rotation threshold and saturation ratio.
- **ContextUsage (Unit)** — the one usage shape, and the optional provider reporting contract.
- **TokenEstimator (Unit)** — the deterministic character-ratio arithmetic.
- **SessionTranscript (Unit)** — the append-only verbatim history and the tier-zero boundary split.
- **ContextLayout (Unit)** — the coarse tiers, the construction bound, and the seed.
- **RotationEngine (Unit)** — the deterministic aging function and its saturation reports.
- **Summarizer (Unit)** — the injected consolidation contract and the documented default prompt.
- **ProviderSession (Unit)** — the adapter seam: seed, turn, session and factory.
- **InMemoryProviderSession (Unit)** — a provider session that contacts nothing, and its factory.
- **CompactingAgentSession (Unit)** — the implementation that sequences all of the above.

## Folder Layout

```text
src/DemaConsulting.AgentKit.Sessions/
├── AgentSession.cs             — the session contract and the per-turn response
├── AgentSessionOptions.cs      — the configuration, the fixed overhead and the threshold
├── CompactingAgentSession.cs   — the implementation that sequences turns and rotations
├── CompactionPolicy.cs         — tier budgets, rotation threshold, saturation ratio
├── ContextLayout.cs            — the coarse tiers, the construction bound and the seed
├── ContextUsage.cs             — the usage shape and the optional reporting contract
├── InMemoryProviderSession.cs  — a provider session that contacts nothing, and its factory
├── ProviderSession.cs          — the seed, the turn, the session and the factory contracts
├── RotationEngine.cs           — the deterministic aging function and its saturation reports
├── SessionTranscript.cs        — the append-only history and the tier-zero boundary split
├── Summarizer.cs               — the consolidation contract and the documented default prompt
└── TokenEstimator.cs           — the deterministic character-ratio arithmetic
```

The folder is flat because the system is flat: each unit is one file directly under the project
root, mirroring the software structure above.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.
