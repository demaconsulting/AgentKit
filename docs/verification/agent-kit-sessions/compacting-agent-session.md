## CompactingAgentSession Unit Verification Design

This document describes the unit-level verification strategy for the `CompactingAgentSession` class.

### Verification Approach

`CompactingAgentSession` owns sequencing and nothing else, so it is verified by driving whole turns
and asserting on the observable consequences of that sequencing: what was recorded, which usage
source was consulted, when a rotation fired, what the replacement session was seeded with, and what
was disposed.

The collaborators are the shipped `InMemoryProviderSessionFactory` and a `FakeSummarizer`, both
deterministic. That is a deliberate choice over a mocking framework: the factory already records
every session it made and each session already reports whether it was disposed, so the assertions
that matter — one session per rotation plus the original, every superseded session released — are
made against real recorded state rather than against verified call expectations.

One scenario needs a provider the shipped one cannot imitate: an adapter whose disposal fails. The
in-memory session's disposal cannot fail, so a small hand-written factory and session, which count
the disposal attempts made on them and throw for a configurable number of them, live alongside the
tests in `CompactingAgentSessionTests.cs`. An endless failure count gives the rotation scenario its
adapter that can never release; a count of one gives the disposal scenario the transient failure a
retry recovers from. A second hand-written pair reports nothing until it has answered a turn, which
the shipped session also cannot express — it either reports from the outset or never at all — and
which is what makes the moment a provider's window first becomes knowable reachable. A third pair
takes a **script of windows**, giving each successive session the next window in it, and can be told
to fail its own release: that is what makes a replacement adopted by a rotation reporting a
different window from the session it replaced reachable at all, and what lets the refusal's message
be examined on the path where the release it attempts has failed. Its conversation figure is
scripted rather than measured, so a scenario can cross a rotation threshold in a single turn instead
of building a transcript of thousands of tokens. They exist only to reach those paths; everything
else is still driven through the shipped fake.

The rotation scenario is sized arithmetically rather than by trial. A 400-token window with a
four-tier policy of 100, 60, 40 and 30 tokens gives a rotation threshold of 280 conversation tokens;
turns of 148 tokens — a 74-token message and a 74-token answer — therefore leave the first turn at
148 tokens, below the threshold, and put the second at 296, above it, so the scenario exercises both
the no-rotation path and the rotation path in one conversation, at a boundary that can be checked by
hand.

Unit tests reside in `CompactingAgentSessionTests.cs`, with the fake summarizer in
`FakeSummarizer.cs` and the shared small policy in `SessionTestData.cs`, all within the
`DemaConsulting.AgentKit.Sessions.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. **No provider, no model and no network access is used**
- **Mocking**: The shipped in-memory provider factory and a hand-written fake summarizer; no mocking
  framework. Six scenarios add hand-written providers: one whose disposal fails, one that
  reports its window only after answering a turn, one that reports scripted figures including
  its own conversation split — that one because the shipped in-memory session measures its reported
  overhead with the same estimator the engine uses, so a test written against it cannot distinguish
  a measurement from an estimate — one whose window is taken from a script, so successive
  sessions of one conversation report different windows, one that reports totals and a window
  with no conversation split at all, over a fixed overhead it really charges for, which is the only
  shape that can carry overhead the engine is not told about, and one whose usage reading is
  arithmetically impossible and therefore throws, which is the only way to reach the moment a
  provider session exists and nothing owns it
- **Isolation**: Each test constructs its own factory, summarizer, options and session; no state is
  shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any turn not recorded in the transcript, any answer missing from it, any
rotation that fails to dispose the session it replaced, any replacement seeded without the preserved
content, any replacement adopted without being held to the same window rule the live session is held
to, any provider session created and then lost on an error path before anything owned it, any failed
release that cannot be retried, any usage figure taken from the wrong source, any
provider-reported window in which a rotated context could not land below the rotation threshold
accepted rather than refused, any rotation reported for a turn that consolidated nothing, any
diagnostic claiming a release that did not happen, or any
invalid argument accepted rather than refused constitutes a failure.

### Test Scenarios

#### AgentKitSessions-CompactingAgentSession-AnswersTurns: A Session Starts Clean and Answers Cleanly

**Tests**: `CompactingAgentSession_CreateAsync_SeedsOneEmptyProviderSession`,
`CompactingAgentSession_SendAsync_BelowThreshold_AnswersWithoutRotating`,
`CompactingAgentSession_SendAsync_ProviderRejectsTheTurn_RecordsNoGhostEntry`,
`CompactingAgentSession_SendAsync_ToolUsingTurnRotates_SeedsTheAnswerIntoTheReplacement`

The first asserts creation produces exactly one provider session, seeded with no history and
carrying the configured instructions, with no rotations yet. The second takes one turn with plenty
of room and asserts the answer came back, nothing rotated, still only one provider session exists,
and **both halves of the turn** — the outgoing message and the answer — are in the engine's own
transcript. That transcript is where a later consolidation reads from, so a turn recorded only half
would lose material silently. The third sends against a token canceled before the call and asserts
neither the engine's transcript nor the provider's history holds the message: a provider may refuse
a turn it never took, and a message recorded ahead of the call would be a turn no provider ever saw,
which would still be consolidated at the next rotation and seeded into the replacement session.

The fourth is the tool-using scenario the other three do not reach, and the regression whose absence
let a real defect through: every earlier scenario used turns that called no tools, where the answer
is the only entry and cannot go missing. This one drives a provider that calls a tool on every turn
and states its conclusion **only** in the answer, runs the conversation past a rotation against a
summarizer that keeps everything it is given, and asserts the first turn's conclusion is present in
what the replacement provider session was seeded with. A turn whose answer never reached the
transcript passes every other scenario here and fails this one.

#### AgentKitSessions-CompactingAgentSession-RotatesAtThreshold: Crossing the Threshold Replaces the Session

**Tests**: `CompactingAgentSession_SendAsync_AboveThreshold_RotatesIntoAFreshSeededSession`,
`CompactingAgentSession_SendAsync_ProviderReportsASmallerWindow_RotatesAgainstTheReportedOne`,
`CompactingAgentSession_SendAsync_ProviderReportsConversation_RotatesRegardlessOfTheEstimate`,
`CompactingAgentSession_SendAsync_ReplacedProviderFailsToDispose_StaysCoherent`

The central scenario, and the one that pins the mechanism. Drives two turns across a deliberately
small window and asserts: the second turn reported a rotation; the rotation count is exactly one;
consolidations were performed; two provider sessions now exist; the first is disposed and the second
is not; the replacement's seeded history begins with a consolidated record and also contains
verbatim material; and the replacement carries the same tools, because rotation replaces history
rather than capability.

The second test pins **which window the threshold is taken from**. It configures a 4,000-token
window against a provider reporting 600 — a mismatch a host gets wrong easily and a provider can
introduce by itself — and asserts the session rotates on the third turn, at 444 conversation tokens
against the reported window's threshold of 420, while the configured window's threshold of 2,800 is
still many turns away. It asserts both thresholds explicitly, so the scenario states the
disagreement rather than relying on one of the two numbers being invisible. Taking the threshold
from the configured window let a provider reporting a smaller one run far past its own compactor's
firing point, which is the single failure this package exists to prevent.

The third test pins **that no estimate enters a reported comparison**, and it is the regression test
for mixing currencies. A hand-written provider reports a 10,000-token window carrying 500 tokens of
its own overhead and adds 1,000 conversation tokens per turn, so the threshold is 70 percent of
9,500 and the seventh turn crosses it. The same provider is driven twice: once against options
configuring no instructions, and once against options whose instructions estimate to 2,589 tokens —
the figure the compaction spike recorded for eleven tool declarations, and a figure the provider's
own reported numbers already account for. The test asserts the rotation falls on turn seven both
times.

Run against the arithmetic as it previously stood, this fails: the estimated fixed overhead was
subtracted from the provider's measured usage and from the provider's reported window, so the same
provider rotated on turn seven in the first run and turn eight in the second. **No existing test
could have caught that.** Every other rotation scenario either configures no instructions and no
tools, making the estimated overhead zero and the subtraction a no-op, or runs against
`InMemoryProviderSession`, whose reported overhead is measured with the same `TokenEstimator` the
engine would have used — so the two currencies are identical by construction and the subtraction
cancels exactly. Those tests pass because the estimator is deterministic, not because the property
they appear to assert holds. This scenario therefore scripts the reported figures, owing nothing to
any estimator.

The fourth test drives the same arithmetic against a hand-written provider whose `DisposeAsync`
throws — a shape the shipped in-memory session cannot express, because its disposal cannot fail — and
asserts the rotation is still reported as the success it was, that the disposal was attempted, and
that the session describes its replacement rather than the session it replaced: the layout was
consolidated into tier one, it sits within its bound, and a further turn is appended to the
replacement's transcript. It also asserts that explicit disposal of the session does propagate the
failure, which is the deliberate asymmetry: a caller that asked for a session to be released is
entitled to learn that it was not, while a caller taking a turn is not served by being told a
successful rotation failed.

#### AgentKitSessions-RotationEngine-HonorsTheTriggerCurrency: A Reported Crossing Reaches the Provider

**Test**: `CompactingAgentSession_SendAsync_ProviderReportsACrossingTheEstimateCannotSee_ConsolidatesAnyway`

This is the regression test for the second half of the currency defect, and it is the end-to-end
form of the engine scenarios in *RotationEngine Unit Verification Design*. The trigger was already
measured in the provider's tokens; the split the engine performs is measured in this library's
estimated tokens. Where the two disagree the trigger has fired on evidence the split cannot see, and
the engine consolidates the whole verbatim history rather than letting the split abandon the
rotation.

The scenario is the disagreement stated exactly: a scripted provider reports 500 conversation tokens
in a 600-token window, crossing a threshold of 420 on the very first turn, for a 20-token message
whose whole turn estimates to well under tier zero's 100-token budget. It asserts the usage came
from the provider, that a rotation occurred with one consolidation, that the material handed to the
summarizer estimates below tier zero's budget — which is what proves the estimated split saw no
overflow and the scenario is exercising the disagreement rather than an ordinary one — that nothing
survived verbatim, and that a replacement provider session exists with the superseded one released.

Run against the engine as it previously stood, the rotation is abandoned: `RotationOccurred` is
false, no summarizer call is made and one provider session exists, which is a session left running
into the provider's own compactor. No other scenario could have caught it, for the same reason the
currency scenario above could not: every rotation test either overflows tier zero in estimated
tokens, where the two currencies agree, or runs against `InMemoryProviderSession`, whose reported
figures are computed with the very estimator the split uses.

#### AgentKitSessions-CompactingAgentSession-RefusesUnusableReportedWindow: A Window Too Small to Settle In

**Tests**: `CompactingAgentSession_CreateAsync_ProviderWindowBelowTheBound_ReleasesAndThrows`,
`CompactingAgentSession_CreateAsync_ProviderWindowHoldsTheBoundButCannotConverge_ReleasesAndThrows`,
`CompactingAgentSession_CreateAsync_ProviderReportsTotalsOverUnreportedOverhead_ReleasesAndThrows`,
`CompactingAgentSession_SendAsync_ProviderReportsTotalsInAConvergentWindow_RotatesAndSettles`,
`CompactingAgentSession_SendAsync_ProviderReportsAWindowBelowTheBound_ReleasesAndThrows`,
`CompactingAgentSession_SendAsync_ReplacementReportsAWindowBelowTheBound_ReleasesAndThrows`,
`CompactingAgentSession_SendAsync_TotalsOnlyReplacedBySplitReporting_TakesTheReplacementsFold`,
`CompactingAgentSession_SendAsync_SplitReportingReplacedByTotalsOnly_RefusesTheReplacement`,
`CompactingAgentSession_SendAsync_ProviderBeginsReportingAfterItsFirstTurn_MeasuresTheFoldThen`,
`CompactingAgentSession_SendAsync_LateReportingProviderInAConvergentWindow_RotatesAndSettles`,
`CompactingAgentSession_SendAsync_LateReportingProviderThatReportsASplit_IsCreditedNoFold`,
`CompactingAgentSession_CreateAsync_ReleaseFailsWhileRefusingTheWindow_DoesNotClaimRelease`

The scenario the reported-window override made reachable. The tests configure a 4,000-token window
against a provider reporting far less, under a policy whose construction bound is 311 tokens — 230
of tier budgets plus 81 of seeded record framing, with no fixed overhead — and which requires 446
effective tokens to converge. Allowed through, the reported window would set a rotation threshold a
rotated context sits above, so every turn would cross the threshold, rotate, and cross it again: a
summarizer call and a provider session spent per turn, forever, with the context never converging
and no saturation signal raised, because each individual consolidation reduces perfectly normally.
Every refusal test therefore asserts an `InvalidOperationException` — an
`AgentSessionCreationException` on the creation path, which derives from it — and that the provider
session was released, because the session is being abandoned mid-life and the caller is left with no
handle to dispose. The first asserts the message names both figures, so a host can see which to
change.

The tests differ in **when the window first becomes knowable, and which session reports it**. The
first two use the shipped in-memory session, which reports from the moment it exists, so the refusal
happens at creation and no turn is ever spent; the second of those reports a window that comfortably
holds the 311-token rotated context but leaves it above the rotation threshold, which is the band
the guard used to admit.

The third and fourth are the pair about **overhead a provider charges for and never breaks out**.
Their provider reports totals and a window with no conversation split at all — a shape
`ContextUsage.FromProvider` documents as supported and which no other fake here produces, since the
split-reporting fake reports the split, the scripted-window fake reports a conversation equal to its
total and so hides nothing, and the shipped in-memory session reports a split measured with the
engine's own estimator. The third states the defect exactly: a 500-token window, a rotated bound of
311 and a threshold of 350, over 100 tokens the provider folds into its conversation figure. Run
against the guard as it previously stood the session is **accepted**, because the unsplit figure
reports zero overhead and the guard read the whole window as effective; the context the first
rotation then produces is reported at 411 against that same 350, and the session rotates on every
turn from then on. The test asserts the refusal names the 100 tokens that were folded in and quotes
589 rather than 446 as the requirement, so the figure being credited is visible rather than inferred
from a bare refusal. The fourth is its complement, and without it the third would be satisfied by a
guard that simply rejected every unsplit provider: the same provider in a 900-token window is
accepted, reads the provider's own figures, rotates, and does not rotate on every turn.

The fifth uses a hand-written provider that reports nothing until it has
answered something — a shape the shipped session cannot express, and precisely the shape that
motivated making usage reporting optional — so the refusal happens on the first turn instead. It
further asserts the abandoned session refuses later turns with `ObjectDisposedException` and that
disposing it again releases nothing a second time. A check placed only at creation would pass the
first tests and fail that one.

The sixth covers the session a **rotation** adopts. Its factory is scripted to hand out a first
session reporting a window that converges comfortably and a replacement reporting one that cannot,
which is a shape no single-window fake can produce and which nothing in the provider contract
forbids: a routed deployment, a changed model or a downgraded tier all report a smaller window than
the session before them. The turn crosses the first session's threshold and really consolidates, so
a replacement is genuinely created and adopted; the test asserts both provider sessions end
released — the superseded one by the rotation, the replacement by the refusal — and that the session
accepts no further turn. Without the check the turn is answered normally and the unusable
replacement surfaces only on the turn after, by which point the caller has been told the rotation
succeeded and has already sent a message into a session that cannot settle.

The seventh and eighth are the pair about **whose fold is being credited**, and they run in opposite
directions because the defect did. Their factory is scripted by *reporting shape* rather than by
window, so one conversation can change shape between rotations — no other fake here can do that, and
that is exactly why a fold measured against the first provider session could go on validating every
replacement without a test noticing. The seventh replaces a totals-only session folding 100 tokens
with a split-reporting one whose 600-token window leaves 500 once its own reported overhead is paid
for: the replacement folds nothing, needs only the 446 tokens the policy requires, and the rotation
must carry through. Against a fold measured once and reused it needed 589 and the rotation threw,
abandoning a session whose replacement was perfectly usable. The eighth is the reverse: a
split-reporting first session, whose fold is zero, replaced by a totals-only one folding 400 tokens
into a 500-token window. The replacement must be refused during the rotation that adopted it, and
the test asserts the refusal names the 400 tokens *that replacement* charges. Against the reused
fold of zero the replacement was accepted, and the session rotated on every turn thereafter while
raising no saturation signal. Both assert only what the fold decides; both provider sessions and the
rotation machinery are otherwise identical.

The ninth and tenth are the third direction the same defect ran in: **a fold that was never measured
at all**. Their provider reports nothing until it has answered a turn, which `IContextUsageReporter`
explicitly permits, and then reports totals alone over 100 tokens it never breaks out. Measured only
at adoption, that provider was credited a fold of zero for the whole life of the provider session,
because the one instant the measurement was taken is the one instant it says nothing. The ninth puts
it in a 500-token window: creation cannot refuse it, the estimated figure it falls back to carries
the configured window, and the first turn is where the provider first speaks and so where the fold
is measured. The test asserts the refusal names a fold other than zero, that the requirement quoted
is above the 446 tokens the same policy needs with no fold, that the provider session was released,
and that the session refuses further turns. Run against the previous behavior the turn returns an
ordinary answer, nothing is refused, and the session rotates on every turn thereafter without
raising a saturation signal — so this scenario fails there and passes here. The tenth is its
complement, for the same reason the fourth is the third's: the identical shape in a 1,200-token
window is accepted, rotates at least once across four turns and fewer than four times, and reads the
provider's own figures on a further turn. Refusing the reporting transition outright was the
alternative resolution, and it is this scenario that rules it out.

The eleventh is the fourth direction, and the one deferring the measurement created: **a fold
measured where there was never one to find**. Its provider is silent until it has answered, and then
reports a conversation split — 100 tokens of overhead broken out, over a conversation it counts at
600 while this library's character ratio makes the same short turn a handful. At adoption that
difference could not appear, because an empty conversation drives the subtraction to zero on its own;
deferring the measurement to the first reported turn is what let a non-empty conversation reach it.
Run against the measurement as it previously stood, 587 tokens of pure tokenizer disagreement are
credited as overhead the provider is hiding, the requirement rises to 1,285 tokens against the 1,100
the window actually leaves, and the session is abandoned on the caller's first turn over a
configuration that would have run indefinitely. The test asserts the turn is answered, that the
session reads the provider's own figures with the 100 tokens of overhead it genuinely reported, that
no rotation was provoked, and that the original provider session is still live. The guard it fixes is
a condition rather than a moment: a provider that breaks its overhead out has no fold by definition,
whenever it is asked. That also closes the same hole at adoption, where a replacement seeded with
tier records is no more an empty conversation than a first report is.

The twelfth is about **what the failure says, and what it hands back**. Its provider reports an
unusable window and then fails the release the refusal attempts, which is the one case where the
claim and the outcome came apart: the catch deliberately leaves the release flag false so a later
call can retry, and the message nonetheless said the session had been released. Worse, on the
creation path no session handle is returned at all, so the retryable state that flag records was
unreachable — the diagnostic said a retry was needed and left nothing to retry with. The test
asserts the release was attempted once and did not succeed, that the configuration defect is still
the failure reported rather than the adapter's disposal failure, that the message says the release
was *attempted* rather than claiming it happened, and that the failure is an
`AgentSessionCreationException` carrying the very provider session the provider still holds — which
the test then disposes, observing a second attempt actually reach the adapter. The other creation
tests assert the complement: when the release succeeds the failure carries nothing.

#### AgentKitSessions-CompactingAgentSession-ReleasesProviderSessionItCannotAdopt: Nothing Created Is Lost

**Tests**: `CompactingAgentSession_CreateAsync_ProviderUsageThrows_ReleasesTheProviderSession`,
`CompactingAgentSession_CreateAsync_ProviderUsageThrowsAndReleaseFails_CarriesTheProviderSession`,
`CompactingAgentSession_SendAsync_ReplacementUsageThrows_ReleasesTheReplacement`,
`AgentSessionCreationException_Construct_WithoutARetainedSession_IsUnambiguous`

All three behavioral tests drive the hand-written provider whose usage reading is arithmetically
impossible — it reports one more conversation token than it reports as occupied in total, the one
split `ContextUsage` refuses rather than clamps. That refusal is deliberate, so this is a condition
the library designs for rather than an implausible one, and it is the only way to reach the window in
which a provider session has been created and nothing yet owns it.

The first makes the very first provider session defective. It asserts the adapter's own
`ArgumentOutOfRangeException` reaches the caller unchanged — a wrapper would move the failure away
from the code that wrote it — and that the provider session created a moment earlier was disposed
exactly once rather than left held with no reference to it anywhere. Before the fix the instance was
discarded, the exception carried no handle at all, and for a provider holding history server-side the
remote session was never discarded.

The second makes the same session's release fail as well, which is the only state in which something
is still held and the creation path has no session to hand back. It asserts the failure is an
`AgentSessionCreationException` whose inner exception is still the adapter's refusal, whose message
says the provider still holds the session and names `RetainedProviderSession`, and whose
`RetainedProviderSession` is the very session the provider holds — which the test then disposes,
observing a second attempt actually reach the adapter.

The third is the rotation half of the same window: a sound first session whose scripted conversation
crosses the threshold in one turn, and a replacement whose usage cannot be read. It asserts the
replacement was created and then released, that the rotation was **not** carried out — no rotation
counted, the superseded session still live and undisposed — and that disposing the session afterwards
releases exactly that session and attempts nothing further against the replacement. Leaving the
conversation on the provider that still holds it was always the correct outcome; what it used to cost
was the replacement.

The fourth is a compile-time assertion. The creation failure declared both `(string, Exception)` and
`(string, IAsyncDisposable?)`, and neither parameter type converts to the other, so
`new AgentSessionCreationException(message, null)` — how an application says that nothing is
retained — was ambiguous and did not build. The test writes exactly that call and then writes the
retained session as an initializer alongside an inner exception, so the two axes are shown to compose;
neither line compiles if the resolution is undone.

#### AgentKitSessions-CompactingAgentSession-PrefersProviderUsage: The Better Measurement Wins

**Tests**: `CompactingAgentSession_SendAsync_ProviderReportsUsage_PrefersTheProviderFigures`,
`CompactingAgentSession_SendAsync_ProviderReportsNothing_UsesItsOwnEstimate`

The first asserts that against a reporting provider the turn's usage is marked as the provider's
own. The second asserts that against a silent provider it is marked estimated **and equals the
layout's own total against the configured window**, so the fallback is shown to be the engine's real
arithmetic rather than an arbitrary number. A provider's figures count framing this library never
sees, which is why they are preferred whenever offered.

#### AgentKitSessions-CompactingAgentSession-RejectsBlankMessage: Invalid Use Is Refused

**Tests**: `CompactingAgentSession_SendAsync_BlankMessage_Throws`,
`CompactingAgentSession_CreateAsync_NullArguments_Throw`,
`CompactingAgentSession_SendAsync_ProviderReturnsNullTurn_Throws`

A data-driven scenario covering a null, empty and whitespace message, each refused with
`ArgumentException` — a blank turn spends context to say nothing and is a defect in the calling
application rather than something to forward to a provider. A missing configuration or provider
factory is refused where the host wrote it rather than at the first conversation.

The third covers the malformed answer rather than the malformed question: an adapter that returns no
turn at all despite the nullable annotation. It asserts an `InvalidOperationException` naming the
null rather than the `NullReferenceException` a dereference produced, and that the transcript is
still empty — an adapter's defect reported as the adapter's, with the session left exactly as it
was.

#### AgentKitSessions-CompactingAgentSession-DisposesProviderSession: Disposal Releases the Live Session

**Tests**: `CompactingAgentSession_DisposeAsync_ReleasesTheLiveProviderSession`,
`CompactingAgentSession_DisposeAsync_FirstReleaseFails_RetriesAndReleases`

The first disposes a session that has taken a turn, disposes it a second time to confirm that is
permitted, and asserts the provider session was released and that a further turn is refused with
`ObjectDisposedException`. For some providers the live session is server-side state that keeps being
billed for until released.

The second drives the same lifecycle against a provider whose first disposal throws and whose second
succeeds — the hand-written failing provider, given a finite failure count rather than its usual
endless one. It asserts the first disposal propagates the failure and leaves the provider unreleased,
that a second disposal actually attempts the release again and succeeds, that a third attempts
nothing further, and that the session refused turns from the first disposal onwards regardless. A
session that marked itself released before awaiting the release would pass none of the middle
assertions: every later call would return at the flag while the provider still held the conversation,
turning a transient failure into a permanent leak.
