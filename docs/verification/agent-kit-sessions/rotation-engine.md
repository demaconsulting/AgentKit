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
explicitly asserted. Any rotation that consolidates a tier that did not overflow, that fails to
carry the previous record forward, that degrades the newer material instead of the older record,
that seeds an orphaned tool result, that leaves the context outside its bound, that stores or sizes
a blank summarizer answer as content, or that fails to
report a consolidation which could not reduce constitutes a failure.

### Test Scenarios

#### AgentKitSessions-RotationEngine-AgesOnlyOverflowingTiers: A Rotation With Nothing to Age Costs Nothing

**Tests**: `RotationEngine_RotateAsync_NothingOverflows_ConsolidatesNothing`,
`RotationEngine_RotateAsync_Overflow_FoldsIntoTierOneOnly`

The first asserts that a layout whose history already fits is handed back as the *same instance*,
with zero consolidations and the summarizer never called — a rotation that is a pure re-seed. The
second asserts that when history does overflow, exactly one consolidation happens, into tier one, as
a first recording, and that tiers two and three are left untouched. Consolidating only the tiers
that overflowed is where the tiered scheme's measured cost advantage over a flat rolling summary
comes from.

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
where there is nothing coarser left to degrade into. Without detection both failures are invisible:
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
`RotationEngine_RotateAsync_CanceledWithNothingToRotate_Throws`

A summarizer returning null is refused with `InvalidOperationException` rather than stored, because
a null record would surface as a missing tier at a later rotation, far from the implementation that
caused it. A canceled token aborts the rotation, so a host shutting down is not held open by a
summarizer round trip.

The third scenario is the one that pins the contract rather than the common case: it cancels the
token before the call and rotates a transcript of 60 tokens against a tier-zero budget of 100, so
nothing overflows and the rotation would otherwise return a successful result without ever reaching
a consolidation. Cancellation is asserted there too, because a contract honored only where work
happens to be required is not a contract a caller can rely on.

#### AgentKitSessions-RotationEngine-NormalizesBlankRecords: A Blank Answer Becomes an Empty Record

**Tests**: `RotationEngine_RotateAsync_SummarizerReturnsWhitespace_TreatsTheRecordAsEmpty`,
`RotationEngine_RotateAsync_SummarizerReturnsWhitespace_StoresAnEmptyRecordAndReportsNoSaturation`,
`ContextLayout_ConversationTokens_BlankTierRecord_ChargesNothingItWouldNotSeed`

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

The third exercises the same rule by the one route no summarizer takes. `ContextTier`'s constructor
is public, so a host composing its own layout can still supply a blank record; the scenario builds
one carrying 40 tokens of whitespace beside 60 tokens of verbatim history and asserts the
conversation figure is 60 — neither the content nor its framing charged — matching a seed that emits
no record. Charging the content before asking whether the tier was empty is what made the two
accounts disagree.
