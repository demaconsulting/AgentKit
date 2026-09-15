## Summarizer

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The `Summarizer` unit publishes `ISummarizer`, the injected out-of-session consolidation contract,
`ConsolidationRequest`, one unit of work handed to it, and `ConsolidationPrompt`, the documented
default prompt.

### Purpose

**Injected rather than fixed, for one decisive reason: the rotation engine must be testable without
a model.** A test supplies a deterministic fake and gets a rotation engine that is a pure function of
its inputs; production supplies an implementation backed by a model. Nothing in the engine knows or
cares which it has.

**Implementations must run out of session.** The material arrives as an argument precisely so that
consolidation does not happen inside the live session being compacted — asking a session to
summarize itself spends that session's own context on the summary and provokes the provider's
built-in compactor, which defeats the purpose.

**Consolidation is a ratchet, and `ConsolidationRequest` is how the ratchet is expressed.**
`PreviousRecord` is the record a previous consolidation produced for the same stretch of history. It
is an input, not context: the result must incorporate it and must not drop detail it kept, unless
the request is a deliberate degradation to a coarser tier. A summarizer that ignored it would
re-summarize a summary, which is the downward ratchet that makes a flat rolling summary forget
everything beyond a handful of rotations.

### Data Model

`ConsolidationRequest` properties, immutable after construction:

- **`TierIndex`** (`int`) — One or greater; tier zero is verbatim history and is never consolidated into
- **`PreviousRecord`** (`string?`) — Null, empty or blank when this is the first recording into the tier
- **`Material`** (`string`) — Never null or blank; the new material, rendered as labeled transcript text
- **`BudgetTokens`** (`int`) — Positive; supplied **for information only**
- **`IsDegradation`** (`bool`) — Derived: true when there is no previous record, a blank one counting as none

**`BudgetTokens` is not an instruction to the model, and must never be turned into one.** Asking a
model to hit a token count does not work: in the compaction spike that preceded this package,
consolidations asked for between 9,870 and 19,741 tokens returned 1,665 and 4,259 tokens (n = 2
requests, recorded in that spike). A model cannot count its own output. The engine therefore prompts
for specificity and content, measures the result itself, and treats output size as a signal about
how much information the material carried rather than as something to be dictated. The budget is
exposed so an implementation can log or reason about it, not so it can be pasted into a prompt.

**`IsDegradation` distinguishes a legitimate coarsening from an accidental loss.** The ratchet rule
— never drop detail an earlier consolidation kept — applies only when a previous record exists.

`ISummarizer` exposes a single method, `ConsolidateAsync`. Implementations must be stateless, must
not return null, and must be safe for concurrent use, because an application may run more than one
session against the same summarizer.

`ConsolidationPrompt` is static and holds one published constant, `Instruction`, plus the
`Compose` method.

### Key Methods

#### ISummarizer.ConsolidateAsync(ConsolidationRequest request, CancellationToken cancellationToken)

Produces the tier record incorporating the previous record and the new material.

**Contract on the implementation:** incorporate `PreviousRecord` when there is one; do not drop
detail that record kept; do not interpret, speculate about, or comment on material that is not
present; return an empty string rather than null when there is nothing to say. The engine measures
the result's size itself; an implementation is not asked, and must not be asked, to hit a length.

**Throws:** `ArgumentNullException` for a null request; `OperationCanceledException` on
cancellation.

#### ConsolidationPrompt.Instruction

The recommended instruction given to a model performing a consolidation. It asks, by name, that the
result preserve: every file, path, identifier and location touched and what happened to it; every
concrete value, setting, number and name established; every decision and the reason for it; every
constraint, requirement or rule discovered or imposed; every error and how it was resolved or that
it is unresolved; and everything still outstanding with the next step on it. It states the ratchet
rule and forbids interpretation, speculation and editorializing.

It contains **no length, word count or token count** — see `BudgetTokens` above for the measurement
that settled that. Length is allowed to follow from how much the material contains, and that
variation is signal rather than noise.

It is published rather than buried inside an implementation so that an application can read exactly
what its summarizer is being asked to do, and can replace it with wording suited to its own domain.
Nothing in the engine calls it: a summarizer implementation does, if it wants to.

#### ConsolidationPrompt.Compose(ConsolidationRequest request)

Composes a request into the full text a model is sent: the instruction, then the previous record (or
an explicit statement that there is none), then the new material.

**Why the previous record comes first.** That is the order the ratchet reads in — carry this
forward, then fold this in.

**Why a degradation says so explicitly** rather than presenting an empty section: an empty section
is something a model may try to fill from nothing.

Composition is deterministic: the same request always composes to the same string, so a summarizer
implementation can be tested without a model and a cache can key on the result.

### Error Handling

- **Tier index below one** — `ArgumentOutOfRangeException` propagates
- **Blank consolidation material** — `ArgumentException` propagates
- **Non-positive tier budget** — `ArgumentOutOfRangeException` propagates
- **Null request to `Compose`** — `ArgumentNullException` propagates
- **Summarizer returns null** — Detected and refused by `RotationEngine`, not here

A request to consolidate into tier zero, or to consolidate nothing, could only be a defect in the
engine; each is refused where it was constructed rather than discovered from a record that makes no
sense.

### Dependencies

None at compile time beyond the .NET base library. `ConsolidationRequest` is constructed by
`RotationEngine` from material `SessionTranscript` rendered.

### Callers

`RotationEngine` constructs a `ConsolidationRequest` for every consolidation and calls
`ISummarizer.ConsolidateAsync`. `AgentSessionOptions` carries the summarizer an application
supplied. `ConsolidationPrompt` is called by an application's own summarizer implementation, not by
this system.
