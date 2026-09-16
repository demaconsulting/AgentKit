# AgentKitSessions System Design

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The AgentKitSessions system is an AgentKit-owned agent session with automatic context compaction,
built so that the same session behavior works across provider shapes. This increment is the
provider-neutral engine only: it contains no Copilot or `IChatClient` adapter wiring, and the core
is deterministic enough to exercise without a live model.

## Purpose

AgentKit ships guarded tools that behave identically on supported provider backends. A guarded tool
set is useful only when an agent can run long enough to use it, so this system adds a provider-neutral
session lifecycle for long-running conversations.

An application asks this system for a session, sends messages to it, and reads answers back. When
the conversation approaches the provider's context limit, the session rotates into a fresh provider
session seeded with preserved context. The application receives the answer for every accepted turn
and can observe whether rotation happened, which compaction level is active, and whether any material
had to be dropped.

## Architecture

The system is deliberately flat: twelve unit design documents sit directly under the system with no
intervening subsystem. The units divide into four responsibility groups:

- **The contract an application programs against** — `AgentSession` provides `IAgentSession` and
  `AgentSessionResponse`; `AgentSessionOptions` carries instructions, tools, window size and the
  summarizer; `CompactionPolicy` carries the single application setting, `VerbatimTurns`.
- **The accounting** — `ContextUsage` records usage in either provider-reported or estimated form;
  `TokenEstimator` supplies the fallback estimate; `SessionTranscript` holds whole turns; and
  `ContextLayout` holds the verbatim tail plus consolidated tiers.
- **The engine** — `RotationEngine` applies the round-robin aging rules, and `Summarizer` defines the
  injected out-of-session consolidation contract and prompt composition.
- **The provider seam** — `ProviderSession` defines seed, turn, session and factory contracts;
  `InMemoryProviderSession` is the deterministic in-memory provider used to exercise the lifecycle;
  and `CompactingAgentSession` sequences live turns, rotation and disposal.

A session runs as follows. `CompactingAgentSession` sends the message to the live provider session.
Only after the provider accepts the turn does the session record the user message, answer and all
intervening tool calls and results as one whole turn in `ContextLayout.Tail`. It then reads provider
usage when the provider reports it, or estimates usage from the layout otherwise. When conversation
occupancy reaches the 0.70 threshold of the effective window, the session asks `RotationEngine` to
age the layout, builds a `ProviderSessionSeed` from the result, creates and adopts a replacement
provider session, and finally disposes the superseded session.

### Rotation, Not In-Place Reduction

When the context fills, this system consolidates older history, creates a fresh provider session
seeded with the preserved content, adopts that replacement, and only then disposes the session it
replaced.

This is the reduction mechanism both provider shapes support. One provider shape re-sends history on
every turn; another holds history server-side and has no general way to retract a prior turn. A
replacement seeded from preserved context works in both cases. Disposal is part of the mechanism
rather than housekeeping: for a provider that holds conversation state, disposal is what releases the
superseded context.

### Round-Robin Retention

Context is laid out most-stable-first:

```text
[system][tool declarations][tier 3 coarse] [tier 2] [tier 1] [verbatim tail]
```

The verbatim tail holds whole recent turns. Behind it are three tiers, each a ring of at most four
consolidated slots. Resolution decays with age: recent turns remain word for word, older material is
held in a tier-one slot, older still in progressively coarser slots. Nothing is weighed against a
per-tier token allowance; the shape is defined by counts.

`CompactionPolicy.VerbatimTurns` is the one setting an application controls. The default is 20 turns,
and it is a maximum rather than a quota. Internal constants define four slots per tier, three tiers,
the 0.70 rotation threshold, and the hysteresis windows `k = VerbatimTurns` and
`m = 2 * VerbatimTurns`.

The alternative — a single rolling summary — was measured and rejected. Over 50 rotations with a
compressed window (n = 50 rotations, one run per arrangement, recorded in the compaction spike that
preceded this package), recall by rotations-ago was:

- **Flat rolling summary** (100% at 0-4, 50% at 5-9, 67% at 10-14, 0% beyond 15) — 5 of 23
- **Tiered** (50-100% held out to 50 rotations) — 13 of 17

The tiered arrangement also spent 17 percent fewer summarizer tokens (381,440 against 461,173)
because it consolidated only material that aged into a coarser tier. The cost is that remembered tier
content occupies context window space; the same measurement completed roughly 27 percent fewer turns
of work per rotation. That is the deliberate trade for retaining older detail.

### Aging Only at Rotation, in a Batch

Between rotations the context is append-only. Nothing already sent to a provider is rewritten while
a session is live, preserving provider prompt-cache prefixes. All reshaping happens during rotation,
where the provider session is being replaced anyway.

The rotation rules are:

1. Append each accepted exchange as one whole turn.
2. At 0.70 occupancy, consolidate everything older than the level-adjusted tail into one tier-one
   slot.
3. When a coarse tier from tier one through the next-to-last tier is full and another slot arrives,
   consolidate that tier's slots as peers into one slot of the next tier, then clear the full tier.
4. The last tier is a ring: when it is full, appending a slot drops its oldest slot.
5. After building the seed, escalate the compaction level until the seed fits. At the highest level,
   drop the oldest slot of the coarsest non-empty tier, then the oldest verbatim turn, until it fits;
   report when material was dropped.

Rule 5 terminates because there are finitely many levels, slots and turns, and the drop loop stops
when the newest turn stands alone.

### Rotating at Seventy Percent of the Effective Window

The effective window is the provider window minus fixed overhead: system instructions and tool
declarations. Rotation fires when the conversation reaches 0.70 of what is left. Tool declarations
are not a small correction — the compaction spike estimated a declaration block of 2,589 tokens for a
set of 11 tools (n = 11 tools, one measurement, recorded in that spike) — so removing overhead before
applying the threshold keeps the trigger meaningful.

Occupancy is measured after each provider turn. A provider that reports its own usage supplies the
window, total, and conversation split used for the comparison. A provider that reports nothing uses
this library's estimate against the configured window. The two currencies are never mixed: both sides
of a comparison come from the provider report or both come from the library estimate.

The rotation engine also sizes the seed it is about to send, using this library's estimate because
the seed has not yet been reported by any provider. If the seed is too large, rule 5 escalates and,
if necessary, drops material before the replacement provider session is created.

### The Summarizer Runs Out of Session

Consolidation is a separate stateless call that receives material as input. Asking the live provider
session to summarize itself would spend the session's own context and could invoke provider behavior
that the library is trying to avoid. The system therefore keeps its own transcript and hands rendered
material to an injected `ISummarizer`.

Oversized summarizer input is chunked internally. The engine splits large material at piece
boundaries, consolidates each chunk, and consolidates the chunk records until one slot remains. This
mechanism is internal and required; applications do not configure it.

### Consolidation as Peer Reduction

A `ConsolidationRequest` carries three values: `Instruction`, `Material` and `TierIndex`. There is no
previous record, target size, or degradation flag. The pieces handed to a consolidation are peers:
either a span of older transcript entries or the slots of a full tier.

The prompt preserves specific named facts, decisions, paths, values, errors, resolutions,
constraints and outstanding work. It also permits collapsing repetition across the material, because
removing redundancy is how a consolidation buys room. The prompt does not ask a model to hit a token
count. In the compaction spike, consolidations asked for between 9,870 and 19,741 tokens returned
1,665 and 4,259 tokens (n = 2 requests, recorded in that spike), demonstrating that output size must
be measured by the engine rather than dictated to the model.

### Compaction Level and Dropped Material

`CompactionLevel` is session state with `Low`, `Medium` and `High` values. It is reported on every
`AgentSessionResponse` and exposed on `IAgentSession`. Low keeps up to `VerbatimTurns` recent turns,
Medium keeps half, and High keeps a quarter, always leaving at least one turn. The level also selects
the plain-language terseness clause passed to the summarizer.

The level adapts by hysteresis. If a window fills again within `k` turns of a prior rotation, the next
rotation starts one level terser. If the session runs for `m` turns without filling, with `m` greater
than `k`, the level relaxes one step when a later rotation settles without further escalation. During
one rotation the engine may escalate further while rebuilding the seed.

`AgentSessionResponse.MaterialDropped` reports the last resort: even the highest level did not make
the seed fit, so the engine discarded the oldest preserved material. The library reports that fact and
leaves any policy decision to the application.

## External Interfaces

- **`IAgentSession`** — Direction: inbound, application to system; format: .NET interface;
  constraints: one conversation per session, sequential turns, caller disposes the session.
- **`AgentSessionResponse`** — Direction: outbound, system to application; format: immutable .NET
  class; constraints: includes answer text, usage, rotation flag, compaction level and dropped-material
  flag.
- **`AgentSessionOptions` / `CompactionPolicy`** — Direction: inbound configuration; format:
  immutable .NET classes; constraints: summarizer required, positive window, `VerbatimTurns` positive.
- **`ISummarizer`** — Direction: outbound, system to application implementation; format: .NET
  interface; constraints: stateless, out of session, safe for concurrent use, must not return null.
- **`IProviderSessionFactory` / `IProviderSession`** — Direction: outbound, system to adapter;
  format: .NET interfaces; constraints: factory is safe for concurrent use, session serves one
  conversation and is disposable.
- **`IContextUsageReporter`** — Direction: optional outbound query; format: .NET interface;
  constraints: implemented only by sessions that can report usage without contacting a provider.

Tools are carried as `Microsoft.Extensions.AI` `AIFunction` instances, which is the same tool
currency the rest of AgentKit uses, so a session accepts exactly what a tool pack produces with no
conversion layer.

## Dependencies

- **Microsoft.Extensions.AI.Abstractions** — supplies `AIFunction`, the tool currency the session
  options carry and the provider-session seed hands to an adapter; see
  *Microsoft.Extensions.AI.Abstractions Design*.

This system takes no reference on AgentKitCore. It composes a session around tools an application
already holds and needs none of Core's guarded-construction contract to do so. The relationship is
conceptual rather than compiled: the tools an application puts into `AgentSessionOptions` are exactly
the ones `ToolPackBuilder` produces.

This system takes no provider dependency. Provider-specific adapters live outside this engine and
implement `IProviderSessionFactory` and `IProviderSession`.

## Risk Control Measures

The primary segregation is between the engine and any provider. The engine never touches a provider
API. It produces `ProviderSessionSeed`, consumes `ProviderTurn`, and asks an adapter for current
usage only through `IContextUsageReporter`. This boundary makes the compaction behavior testable
against `InMemoryProviderSession` with no network access, credentials or model.

The second control is one-currency accounting. A provider-reported figure and this library's estimate
are never combined in one subtraction or threshold comparison. `ContextUsage` carries conversation
usage beside total usage, so whoever produced the total also produced the split. The estimator is used
only when no provider report exists and for seed sizing before a replacement is sent.

The third control is adaptive reporting. Repeated pressure raises `CompactionLevel`; last-resort
discarding sets `MaterialDropped`. The library keeps answering when it can, but it does not hide the
fact that fidelity has been reduced or material was discarded.

The fourth control is provider-session ownership. `CompactingAgentSession` pairs the live provider
session with its release state so disposal remains retryable. Creation and rotation guard the windows
where a provider session has been returned but not yet adopted: if adoption fails, the unowned session
is released; if creation cannot release it, `AgentSessionCreationException.RetainedProviderSession`
carries the handle so the caller can retry disposal.

## Data Flow

```text
application message
  -> IProviderSession.SendAsync
  -> on success, record the user message and ProviderTurn entries as one whole turn
  -> read ContextUsage from IContextUsageReporter, or estimate it from ContextLayout
  -> if conversation occupancy is below the threshold: return the answer
  -> otherwise:
       choose the starting CompactionLevel through hysteresis
       RotationEngine.RotateAsync(layout, summarizer, level, verbatim turns, threshold)
         -> split the tail by whole turns at the level-adjusted tail length
         -> consolidate older material into tier one
         -> cascade full tiers as peer slot batches
         -> size the built seed, escalate until it fits, then drop oldest material if needed
       IProviderSessionFactory.CreateAsync(ContextLayout.BuildSeed())
       adopt the replacement and dispose the replaced IProviderSession
  -> return the answer, usage, rotation flag, level and dropped-material flag
```

## Design Constraints

- **Provider-neutral.** No type in this system names a provider, and no engine path performs network
  access.
- **Deterministic core.** Every decision in the engine is arithmetic over the layout and the current
  level. Injecting the summarizer isolates the single collaborator that may vary.
- **Turn-granular boundaries.** A turn is one exchange: the user message, answer and all intervening
  tool calls and results. Boundaries are placed between turns only.
- **Append-only between rotations.** Existing history is reshaped only when the provider session is
  being replaced.
- **Fixed internal shape.** The layout has three tiers, four slots per tier and a 0.70 rotation
  threshold. Applications configure only the maximum verbatim tail.
- **Seed sizing before send.** The replacement seed is estimated before creation; the engine escalates
  or drops material before giving the seed to an adapter.
- **Chunked consolidation input.** Oversized consolidation material is broken into groups and reduced
  back to one slot.
- **Owned copies behind read-only views.** Public list properties expose immutable snapshots or
  read-only views over storage the object owns.
- **Multi-platform.** The library follows the repository's supported operating systems and target
  frameworks.

## Structure

- **AgentSession (Unit)** — the `IAgentSession` contract and per-turn response.
- **AgentSessionOptions (Unit)** — configuration, fixed overhead, effective window and threshold.
- **CompactionPolicy (Unit)** — the positive maximum verbatim tail length.
- **ContextUsage (Unit)** — usage accounting and optional provider usage reporting.
- **TokenEstimator (Unit)** — deterministic fallback estimates and seed sizing support.
- **SessionTranscript (Unit)** — append-only whole-turn history and rendering.
- **ContextLayout (Unit)** — the verbatim tail, internal tiers, slots and seed ordering.
- **RotationEngine (Unit)** — round-robin aging, chunking, escalation and dropping.
- **Summarizer (Unit)** — injected consolidation contract and prompt composition.
- **ProviderSession (Unit)** — the adapter seam: seed, turn, session and factory.
- **InMemoryProviderSession (Unit)** — the in-memory provider session and factory.
- **CompactingAgentSession (Unit)** — the implementation that sequences turns and rotations.

The public surface was reduced from 28 to 18 types. `CompactionLevel` is public; layout, transcript,
estimator,
rotation, prompt and in-memory provider implementation details are internal, along with slot and tier
storage used by the round-robin layout.

## Folder Layout

```text
src/DemaConsulting.AgentKit.Sessions/
├── AgentSession.cs             — the session contract and per-turn response
├── AgentSessionOptions.cs      — configuration, fixed overhead and threshold
├── CompactingAgentSession.cs   — the implementation that sequences turns and rotations
├── CompactionLevel.cs          — the reported compaction level
├── CompactionPolicy.cs         — the maximum verbatim tail length
├── ContextLayout.cs            — the verbatim tail, coarse tiers, slots and seed
├── ContextUsage.cs             — the usage shape and optional reporting contract
├── InMemoryProviderSession.cs  — the in-memory provider session and factory
├── ProviderSession.cs          — the seed, turn, session and factory contracts
├── RotationEngine.cs           — round-robin aging, chunking, escalation and dropping
├── SessionTranscript.cs        — append-only whole-turn history and rendering
├── Summarizer.cs               — the consolidation contract and prompt composition
└── TokenEstimator.cs           — deterministic fallback estimates
```

The folder is flat because the system is flat: each unit is one file directly under the project root,
mirroring the software structure above.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Text tables are used in preference to diagrams that may not render in all PDF viewers.
