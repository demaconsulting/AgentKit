## RotationEngine Unit Verification Design

This document describes the unit-level verification strategy for the `RotationEngine` class and its
result types.

### Verification Approach

This is the heart of the system, and it is verified **entirely without a model**.

Every scenario supplies a `FakeSummarizer`, which computes its answer arithmetically from the
request it was given. That replaces the engine's one non-deterministic collaborator with a
deterministic one, which makes the whole rotation a pure function of its inputs: the same layout
always produces the same tiers, the same consolidation count and the same saturation reports. That
is what allows the cascade ordering, the ratchet, the boundary snap and the saturation reports to be
asserted *exactly* rather than approximately.

Three things about the fake are deliberate and are what make the scenarios meaningful:

- **It records every request.** The design rules that cannot be observed from the resulting layout
  alone — that a consolidation receives the previous record as an input, that a cascade degrades the
  older record rather than the newer material, that only overflowing tiers are consolidated — are
  visible in the request sequence and nowhere else. The cascade scenario asserts the exact sequence
  of tier index and degradation flag, which pins the ordering the design specifies.
- **Its output size is a stated function of its input.** The compressing fake lets a scenario choose
  whether a consolidation will fit its tier, so the fitting path and the cascading path are both
  reachable by construction rather than by luck.
- **It can be replaced wholesale.** The pathological scenarios supply a summarizer that expands, one
  that echoes its input unchanged, and one that returns null, each of which is a shape a real
  implementation could produce and none of which could be provoked reliably from a model.

Layouts are built with the exact-size helpers in `SessionTestData.cs`, which invert the token
estimate so a scenario can state precisely how many entries will overflow a tier.

Unit tests reside in `RotationEngineTests.cs`, with the fake summarizer in `FakeSummarizer.cs` and
the builders in `SessionTestData.cs`, all within the `DemaConsulting.AgentKit.Sessions.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. **No model and no network access is used**
- **Mocking**: A hand-written deterministic fake summarizer; no mocking framework
- **Isolation**: Each test builds its own layout and summarizer; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any rotation that consolidates a tier that did not overflow, that abandons a
provider-reported crossing because its own estimate saw no overflow, that fails to
carry the previous record forward, that degrades the newer material instead of the older record,
that seeds an orphaned tool result, that leaves the context outside its bound, that stores or sizes
a blank summarizer answer as content, or that fails to
report a consolidation which could not reduce constitutes a failure.

### Test Scenarios

#### AgentKitSessions-RotationEngine-AgesOnlyOverflowingTiers: A Rotation With Nothing to Age Costs Nothing

**Tests**: `RotationEngine_RotateAsync_NothingOverflows_ConsolidatesNothing`,
`RotationEngine_RotateAsync_Overflow_FoldsIntoTierOneOnly`

The first asserts that a layout whose history already fits, rotated on a crossing this library
measured itself, is handed back as the *same instance*,
with zero consolidations and the summarizer never called — a rotation that is a pure re-seed. The
second asserts that when history does overflow, exactly one consolidation happens, into tier one, as
a first recording, and that tiers two and three are left untouched. Consolidating only the tiers
that overflowed is where the tiered scheme's measured cost advantage over a flat rolling summary
comes from.

#### AgentKitSessions-RotationEngine-HonorsTheTriggerCurrency: A Reported Crossing Is Not Vetoed by an Estimate

**Tests**: `RotationEngine_RotateAsync_ProviderReportedTrigger_ConsolidatesEvenWithoutEstimatedOverflow`,
`RotationEngine_RotateAsync_ProviderReportedTriggerWithEmptyTranscript_ConsolidatesNothing`,
`RotationEngine_RotateAsync_ProviderReportedTriggerWithToolPairs_ConsolidatesTheWholeHistoryInOrder`,
`RotationEngine_RotateAsync_UndefinedTriggerOrigin_Throws`,
`CompactingAgentSession_SendAsync_ProviderReportsACrossingTheEstimateCannotSee_ConsolidatesAnyway`

The first rotates the very layout the scenario above is entitled to leave alone — 60 tokens of
history against a tier-zero budget of 100 — and states that the crossing was the provider's own. It
asserts a real consolidation happened, that the layout returned is not the one handed in, that tier
one holds a record, that every entry of the verbatim history appears in the material the summarizer
was given, and that nothing survived verbatim. Asserting the *material* rather than only the count is
what pins the forced split to the whole history: a forced split that aged out an entry or two would
satisfy a count assertion while removing less than the next turn adds, which is not a reduction of
anything the provider is counting.

The second is the termination case. A layout holding no verbatim history at all consolidates
nothing even on a provider-reported crossing, is handed back as the same instance, and calls no
summarizer — so the forced path cannot manufacture an empty consolidation or a provider session per
turn out of a session with nothing recorded.

The third confirms the forced split hands over the **whole** verbatim history, in the order it was
recorded: three interleaved runs of parallel calls, 60 tokens in all and so comfortably inside tier
zero's budget. It asserts against the **material the summarizer was handed**, reading back the
labeled call and result lines and requiring the full interleaved sequence in recorded order — which
is what a split taking a portion, dropping an entry, or emitting the overflow newest-first would
break.

**What it deliberately does not claim.** It does not exercise the boundary snap that keeps a tool
call with its result, and it no longer says it does. The forced path splits at a budget of zero, so
the retained window begins past the last entry; the orphan scan over an empty window never enters
its loop and reports none, and the overflow is the whole transcript in recorded order however the
pairing logic behaves. A deliberately broken snap cannot change the outcome here, so an assertion
about orphans — over the material, the retained window or the seed alike — would be true by
construction. Pair integrity is exercised where the snap actually decides something, in
`RotationEngine_RotateAsync_BoundaryInsideToolPair_NeverSeedsAnOrphanedResult` under
*AgentKitSessions-SessionTranscript-SnapsToolBoundary*.

The fourth refuses an undefined origin. The origin decides whether an estimated split may abandon the
rotation, so a cast integer is a defect in the caller and is refused as the other enum-taking members
of this package refuse one.

The fifth is the end-to-end form, and it is the regression test for the defect itself; see
*CompactingAgentSession Unit Verification Design*.

**Why none of the scenarios above could have caught it.** Every other rotation scenario in this file
builds a layout that overflows tier zero in estimated tokens, which is the case where the two
currencies agree about what to do. The disagreement is only reachable from a provider whose reported
figures owe nothing to this library's estimate, which is why the end-to-end scenario uses the
scripted split-reporting fake rather than the shipped in-memory session — that session measures its
own reported figures with the same `TokenEstimator`, so the two currencies coincide by construction.

#### AgentKitSessions-RotationEngine-FoldsOverflowIntoTiers: The Ratchet Carries the Previous Record Forward

**Test**: `RotationEngine_RotateAsync_SecondRotation_CarriesThePreviousRecordForward`

Rotates once so tier one holds a record, grows the history, rotates again, and asserts the second
consolidation received the first one's record as its `PreviousRecord` and was not flagged as a
degradation. This is the assertion that separates the tiered arrangement from the flat one: without
it each consolidation re-summarizes the previous summary, which is the downward ratchet that makes
recall collapse beyond roughly fifteen rotations.

#### AgentKitSessions-RotationEngine-CascadesDegradation: The Older Record Degrades, Not the Newer Material

**Test**: `RotationEngine_RotateAsync_TierOneOverflows_DegradesTheOlderRecordIntoTierTwo`

Uses a summarizer whose combined record overflows tier one but whose first recording into any tier
comfortably fits, rotates twice, and asserts the exact request sequence — a merge at tier one, a
degradation into tier two, then a fresh recording at tier one — together with three consolidations,
a non-empty tier two and both tiers within budget. The request sequence is the only place the
ordering is observable, and the ordering is what keeps the hierarchy monotonic in age.

#### AgentKitSessions-RotationEngine-FoldsOverflowIntoTiers: The Context Returns Inside Its Bound

**Test**: `RotationEngine_RotateAsync_AfterRotation_ContextIsWithinItsConstructionBound`

Builds a layout far outside its bound, rotates, and asserts the layout was outside before and inside
after. Asserting the "before" as well as the "after" is what stops the scenario passing vacuously on
a layout that was never over budget to begin with.

#### AgentKitSessions-SessionTranscript-SnapsToolBoundary: A Rotation Never Seeds an Orphaned Result

**Test**: `RotationEngine_RotateAsync_BoundaryInsideToolPair_NeverSeedsAnOrphanedResult`

Builds a history entirely of call-and-result pairs, long enough that the tier-zero boundary must
fall inside one, rotates, and asserts what survives verbatim is non-empty and does not begin with a
tool result. This is the end-to-end form of the boundary snap: the unit-level snap is verified in
*SessionTranscript Unit Verification Design*, and this confirms a rotation actually benefits from it.

#### AgentKitSessions-RotationEngine-ReportsSaturation: A Failure to Reduce Is Reported

**Tests**: `RotationEngine_RotateAsync_ConsolidationDoesNotReduce_ReportsNoRedundancy`,
`RotationEngine_RotateAsync_CascadeReRecordingDoesNotReduce_ReportsNoRedundancy`,
`RotationEngine_RotateAsync_CoarsestTierCannotFit_ReportsTierOverBudget`,
`RotationOutcome_Construct_NullSaturationEntry_Throws`

The first supplies a summarizer that returns its material unchanged and asserts a `NoRedundancy`
signal naming tier one, with the reported output at least the policy's saturation ratio of the
reported input — so the figures in the signal are asserted, not just its presence.

The second covers the cascade path, which the first does not reach. Its summarizer is scripted call
by call, so the second rotation is driven into a genuine cascade — a merge that overflows tier one's
60-token budget, the older record degrading into tier two, then tier one re-recorded holding the new
material alone — and the re-recording returns 17 tokens for the 18 it was given: within budget, but
with nothing left to remove. The test asserts the request shape first, so the scenario proves it
cascaded rather than assuming it, and then asserts exactly one signal, a `NoRedundancy` at tier one.
Checking only the merge left that rotation reporting an unqualified success.

The third uses a
two-tier policy with a small coarse tier and asserts a `TierOverBudget` signal, which is the case
where there is nothing coarser left to degrade into. The signal reports what the consolidation
returned, and the record it produced is then cut to the budget rather than stored over it — the
signal says the material could not be reduced, and the cut is what keeps that from also making the
context unbounded. Without detection both failures are invisible:
every rotation appears to succeed while buying no room. The fourth refuses a null signal in an
outcome, following the rule the session response already applies: an outcome holding one reports
itself saturated while the consumer that goes to read the signal cannot.

#### AgentKitSessions-RotationEngine-Deterministic: The Same Inputs Produce the Same Output

**Tests**: `RotationEngine_RotateAsync_SameInputs_ProduceIdenticalOutput`,
`RotationEngine_RotateAsync_NullLayout_Throws`,
`RotationEngine_RotateAsync_NullSummarizer_Throws`

Rotates one layout twice with two independent but identically behaving summarizers and asserts
byte-identical tier contents, identical surviving history and an identical consolidation count.
Determinism is the property every other assertion in this file rests on, so it is asserted directly
rather than assumed. The two null scenarios refuse a missing layout and a missing summarizer, the
latter because a session that silently never compacted would fail much later and much less clearly.

#### AgentKitSessions-RotationEngine-RejectsMalformedConsolidation: A Null Record and Cancellation

**Tests**: `RotationEngine_RotateAsync_SummarizerReturnsNull_Throws`,
`RotationEngine_RotateAsync_Canceled_Throws`,
`RotationEngine_RotateAsync_CanceledWithNothingToRotate_Throws`,
`RotationEngine_RotateAsync_CanceledDuringTheOnlyConsolidation_DoesNotAcceptTheResult`

A summarizer returning null is refused with `InvalidOperationException` rather than stored, because
a null record would surface as a missing tier at a later rotation, far from the implementation that
caused it. A canceled token aborts the rotation, so a host shutting down is not held open by a
summarizer round trip.

The third scenario is the one that pins the contract rather than the common case: it cancels the
token before the call and rotates a transcript of 60 tokens against a tier-zero budget of 100, so
nothing overflows and the rotation would otherwise return a successful result without ever reaching
a consolidation. Cancellation is asserted there too, because a contract honored only where work
happens to be required is not a contract a caller can rely on.

The fourth covers the cancellation that arrives **while a consolidation is running**, in the case
where nothing later can catch it. Its summarizer cancels the token as it answers and then ignores the
token entirely, which `ISummarizer` permits, and its answer fits tier one — so the rotation performs
exactly one consolidation and reaches no further check. Run against the engine as it previously
stood the rotation **succeeds**: the answer is sized, stored in tier one and returned inside a
`RotationOutcome` that `CompactingAgentSession` seeds a replacement provider session from, on a token
the caller had canceled. The mid-cascade scenario cannot reach this, because there a second
consolidation follows and the check made before it catches the cancellation. The test asserts the
rotation throws, that the one consolidation really did run and answer — so the refusal is the
engine's own and not the summarizer's — and that the layout handed in still holds no tier records.

#### AgentKitSessions-RotationEngine-NormalizesBlankRecords: A Blank Answer Becomes an Empty Record

**Tests**: `RotationEngine_RotateAsync_SummarizerReturnsWhitespace_TreatsTheRecordAsEmpty`,
`RotationEngine_RotateAsync_SummarizerReturnsWhitespace_StoresAnEmptyRecordAndReportsNoSaturation`,
`RotationEngine_RotateAsync_BlankPreviousRecord_ChargesItNothingIntoTheSaturationRatio`,
`ContextLayout_ConversationTokens_BlankTierRecord_ChargesNothingItWouldNotSeed`,
`ContextTier_BlankRecord_IsChargedNothingAndFitsItsBudget`

A summarizer returning whitespace is behaving within its contract — `ISummarizer` forbids only
null — so the engine has to have an answer for it, and for several rounds that answer was
"whichever consumer is asked". The first scenario is the one that closed the *reading*: it records a
whitespace answer, asserts the tier reports itself empty and is not seeded, then grows the history
and rotates again, which is where the disagreement used to surface as an undocumented
`ArgumentException` thrown out of every rotation from then on, because the record was permanent
state by the time it was rejected.

The second is the one that closes the *value*, and it fails against the reading alone. Its
summarizer returns whitespace three times the size of the tier's budget. Read as content, that is a
record measured over budget, stored as such, and reported as `TierOverBudget` saturation — telling
the session its context has saturated on material the seed omits entirely — and counted in the
estimated conversation the rotation threshold is compared against, so the estimating path and the
provider-reported path describe the same session differently. The test asserts the stored content is
exactly the empty string, that it estimates at zero tokens and sits within budget, that no
saturation was reported, and that the layout's conversation figure equals its transcript alone and
agrees with a seed carrying no record at all.

The third is where this class reopened, at a consumer that charged the record anyway. It composes a
layout through the public tier constructor — the route no summarizer takes and no normalization can
reach — whose tier one holds 50 tokens of whitespace, and rotates with a summarizer that echoes its
material back, a consolidation that removed precisely nothing. The test asserts the redundancy signal
exists and that its input is the material alone, exactly the estimate of the rendered overflow.
Charging the whitespace as well puts the input half as far again above the output, which is below
the saturation ratio, so the previous behavior reported no redundancy signal at all and the scenario
fails there: a consolidation that had genuinely saturated was reported as an ordinary success. The
same figure is asserted across every signal the rotation raised, so a cascade path cannot charge it
either.

The fourth exercises the same rule by the one route no summarizer takes. `ContextTier`'s constructor
is public, so a host composing its own layout can still supply a blank record; the scenario builds
one carrying 40 tokens of whitespace beside 60 tokens of verbatim history and asserts the
conversation figure is 60 — neither the content nor its framing charged — matching a seed that emits
no record. Charging the content before asking whether the tier was empty is what made the two
accounts disagree.

The fifth closes the reading at the tier itself, which is where every other consumer measures one. A
one-token tier holding whitespace asserts an estimate of zero and a budget it therefore fits;
against an estimate taken from the content unconditionally it is 25 tokens and over budget, which is
a tier declared unfit for material no provider would ever be sent. Applying the definition of empty
in the cached estimate rather than at each consumer is what keeps the three scenarios above from
being three separate agreements that a fourth consumer can break.
