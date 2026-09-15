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

The rotation scenario is sized arithmetically rather than by trial. A 300-token window with a
four-tier policy of 100, 60, 40 and 30 tokens gives a rotation threshold of 210 conversation tokens;
turns of roughly 108 tokens therefore leave the first turn below the threshold and put the second
above it, so the scenario exercises both the no-rotation path and the rotation path in one
conversation, at a boundary that can be checked by hand.

Unit tests reside in `CompactingAgentSessionTests.cs`, with the fake summarizer in
`FakeSummarizer.cs` and the shared small policy in `SessionTestData.cs`, all within the
`DemaConsulting.AgentKit.Sessions.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. **No provider, no model and no network access is used**
- **Mocking**: The shipped in-memory provider factory and a hand-written fake summarizer; no mocking
  framework
- **Isolation**: Each test constructs its own factory, summarizer, options and session; no state is
  shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any turn not recorded in the transcript, any rotation that fails to dispose the
session it replaced, any replacement seeded without the preserved content, any usage figure taken
from the wrong source, or any invalid argument accepted rather than refused constitutes a failure.

### Test Scenarios

#### AgentKitSessions-CompactingAgentSession-AnswersTurns: A Session Starts Clean and Answers Cleanly

**Tests**: `CompactingAgentSession_CreateAsync_SeedsOneEmptyProviderSession`,
`CompactingAgentSession_SendAsync_BelowThreshold_AnswersWithoutRotating`,
`CompactingAgentSession_SendAsync_ProviderRejectsTheTurn_RecordsNoGhostEntry`

The first asserts creation produces exactly one provider session, seeded with no history and
carrying the configured instructions, with no rotations yet. The second takes one turn with plenty
of room and asserts the answer came back, nothing rotated, still only one provider session exists,
and **both halves of the turn** — the outgoing message and the answer — are in the engine's own
transcript. That transcript is where a later consolidation reads from, so a turn recorded only half
would lose material silently. The third sends against a token canceled before the call and asserts
neither the engine's transcript nor the provider's history holds the message: a provider may refuse
a turn it never took, and a message recorded ahead of the call would be a turn no provider ever saw,
which would still be consolidated at the next rotation and seeded into the replacement session.

#### AgentKitSessions-CompactingAgentSession-RotatesAtThreshold: Crossing the Threshold Replaces the Session

**Test**: `CompactingAgentSession_SendAsync_AboveThreshold_RotatesIntoAFreshSeededSession`

The central scenario, and the one that pins the mechanism. Drives two turns across a deliberately
small window and asserts: the second turn reported a rotation; the rotation count is exactly one;
consolidations were performed; two provider sessions now exist; the first is disposed and the second
is not; the replacement's seeded history begins with a consolidated record and also contains
verbatim material; and the replacement carries the same tools, because rotation replaces history
rather than capability.

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

**Test**: `CompactingAgentSession_DisposeAsync_ReleasesTheLiveProviderSession`

Disposes a session that has taken a turn, disposes it a second time to confirm that is permitted,
and asserts the provider session was released and that a further turn is refused with
`ObjectDisposedException`. For some providers the live session is server-side state that keeps being
billed for until released.
