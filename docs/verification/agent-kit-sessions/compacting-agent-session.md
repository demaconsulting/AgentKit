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
  framework. Four scenarios add hand-written providers: one whose disposal fails, one that
  reports its window only after answering a turn, one that reports scripted figures including
  its own conversation split — that one because the shipped in-memory session measures its reported
  overhead with the same estimator the engine uses, so a test written against it cannot distinguish
  a measurement from an estimate — and one whose window is taken from a script, so successive
  sessions of one conversation report different windows
- **Isolation**: Each test constructs its own factory, summarizer, options and session; no state is
  shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any turn not recorded in the transcript, any answer missing from it, any
rotation that fails to dispose the session it replaced, any replacement seeded without the preserved
content, any replacement adopted without being held to the same window rule the live session is held
to, any failed release that cannot be retried, any usage figure taken from the wrong source, any
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

#### AgentKitSessions-CompactingAgentSession-RefusesUnusableReportedWindow: A Window Too Small to Settle In

**Tests**: `CompactingAgentSession_CreateAsync_ProviderWindowBelowTheBound_ReleasesAndThrows`,
`CompactingAgentSession_CreateAsync_ProviderWindowHoldsTheBoundButCannotConverge_ReleasesAndThrows`,
`CompactingAgentSession_SendAsync_ProviderReportsAWindowBelowTheBound_ReleasesAndThrows`,
`CompactingAgentSession_SendAsync_ReplacementReportsAWindowBelowTheBound_ReleasesAndThrows`,
`CompactingAgentSession_CreateAsync_ReleaseFailsWhileRefusingTheWindow_DoesNotClaimRelease`

The scenario the reported-window override made reachable. The tests configure a 4,000-token window
against a provider reporting far less, under a policy whose construction bound is 311 tokens — 230
of tier budgets plus 81 of seeded record framing, with no fixed overhead — and which requires 446
effective tokens to converge. Allowed through, the reported window would set a rotation threshold a
rotated context sits above, so every turn would cross the threshold, rotate, and cross it again: a
summarizer call and a provider session spent per turn, forever, with the context never converging
and no saturation signal raised, because each individual consolidation reduces perfectly normally.
Every test therefore asserts an `InvalidOperationException` and that the provider session was
released, because the session is being abandoned mid-life and the caller is left with no handle to
dispose. The first asserts the message names both figures, so a host can see which to change.

The tests differ in **when the window first becomes knowable, and which session reports it**. The
first two use the shipped in-memory session, which reports from the moment it exists, so the refusal
happens at creation and no turn is ever spent; the second of those reports a window that comfortably
holds the 311-token rotated context but leaves it above the rotation threshold, which is the band
the guard used to admit. The third uses a hand-written provider that reports nothing until it has
answered something — a shape the shipped session cannot express, and precisely the shape that
motivated making usage reporting optional — so the refusal happens on the first turn instead. It
further asserts the abandoned session refuses later turns with `ObjectDisposedException` and that
disposing it again releases nothing a second time. A check placed only at creation would pass the
first tests and fail that one.

The fourth covers the session a **rotation** adopts. Its factory is scripted to hand out a first
session reporting a window that converges comfortably and a replacement reporting one that cannot,
which is a shape no single-window fake can produce and which nothing in the provider contract
forbids: a routed deployment, a changed model or a downgraded tier all report a smaller window than
the session before them. The turn crosses the first session's threshold and really consolidates, so
a replacement is genuinely created and adopted; the test asserts both provider sessions end
released — the superseded one by the rotation, the replacement by the refusal — and that the session
accepts no further turn. Without the check the turn is answered normally and the unusable
replacement surfaces only on the turn after, by which point the caller has been told the rotation
succeeded and has already sent a message into a session that cannot settle.

The fifth is about **what the failure says rather than what it does**. Its provider reports an
unusable window and then fails the release the refusal attempts, which is the one case where the
claim and the outcome came apart: the catch deliberately leaves the release flag false so a later
call can retry, and the message nonetheless said the session had been released. On the creation path
no session handle is returned at all, so an operator reading that has nothing left to retry the
release with and no reason to suspect the provider still holds it. The test asserts the release was
attempted once and did not succeed, that the configuration defect is still the failure reported
rather than the adapter's disposal failure, and that the message says the release was *attempted*
rather than claiming it happened.

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
`CompactingAgentSession_CreateAsync_NullArguments_Throw`

A data-driven scenario covering a null, empty and whitespace message, each refused with
`ArgumentException` — a blank turn spends context to say nothing and is a defect in the calling
application rather than something to forward to a provider. A missing configuration or provider
factory is refused where the host wrote it rather than at the first conversation.

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
