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
retry recovers from. They exist only to reach those paths; everything else is still driven through
the shipped fake.

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
  framework. One scenario adds a hand-written provider whose disposal fails
- **Isolation**: Each test constructs its own factory, summarizer, options and session; no state is
  shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any turn not recorded in the transcript, any answer missing from it, any
rotation that fails to dispose the session it replaced, any replacement seeded without the preserved
content, any failed release that cannot be retried, any usage figure taken from the wrong source, or
any invalid argument accepted rather than refused constitutes a failure.

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
`CompactingAgentSession_SendAsync_ReplacedProviderFailsToDispose_StaysCoherent`

The central scenario, and the one that pins the mechanism. Drives two turns across a deliberately
small window and asserts: the second turn reported a rotation; the rotation count is exactly one;
consolidations were performed; two provider sessions now exist; the first is disposed and the second
is not; the replacement's seeded history begins with a consolidated record and also contains
verbatim material; and the replacement carries the same tools, because rotation replaces history
rather than capability.

The second test pins **which window the threshold is taken from**. It configures a 4,000-token
window against a provider reporting 400 — a mismatch a host gets wrong easily and a provider can
introduce by itself — and asserts the session rotates on the second turn, at 296 conversation tokens
against the reported window's threshold of 280, while the configured window's threshold of 2,800 is
still four turns away. It asserts both thresholds explicitly, so the scenario states the
disagreement rather than relying on one of the two numbers being invisible. Taking the threshold
from the configured window let a provider reporting a smaller one run far past its own compactor's
firing point, which is the single failure this package exists to prevent.

The third test drives the same arithmetic against a hand-written provider whose `DisposeAsync`
throws — a shape the shipped in-memory session cannot express, because its disposal cannot fail — and
asserts the rotation is still reported as the success it was, that the disposal was attempted, and
that the session describes its replacement rather than the session it replaced: the layout was
consolidated into tier one, it sits within its bound, and a further turn is appended to the
replacement's transcript. It also asserts that explicit disposal of the session does propagate the
failure, which is the deliberate asymmetry: a caller that asked for a session to be released is
entitled to learn that it was not, while a caller taking a turn is not served by being told a
successful rotation failed.

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
