## CopilotTurnChannel Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotTurnChannel` unit — the
`ICopilotTurnChannel` interface, the `CopilotChannelOpener` delegate, and the production
`CopilotSessionChannel`.

### Verification Approach

This unit is the seam the rest of the package is tested through, and **the production implementation
behind it is the one piece of this package no automated test reaches.** That is stated first, plainly,
because implying otherwise would misrepresent what the evidence covers.

**Why it cannot be reached.** `CopilotSession` is sealed, its constructor is not public, and none of
its methods is virtual; `CopilotClient` is sealed too. A `CopilotSessionChannel` can therefore only
be constructed by opening a real session on a real client, which requires a live Copilot runtime and
an authenticated user — neither of which this repository's continuous integration has, and neither of
which it deliberately requires.

**What is verified, and what that is worth.** The *contract* this unit publishes — that a channel
takes a turn and releases exactly one runtime session, that an opener is injectable, and that the
caller's cancellation token is the turn's only deadline — is verified through the units built on it,
which are exercised against a scripted channel that replays the SDK's own event types. Those tests
prove that the adapter above the seam uses it correctly: that one session is opened per rotation and
per consolidation, that each is released on every path including failure, that the client is never
disposed, and that a canceled turn sends nothing.

**What is deliberately not claimed.** Three behaviors of the production implementation are reviewed
rather than tested, and each is recorded here rather than implied to be covered:

- **That the session is created, that a turn reaches the model, and that release and deletion take
  effect** — all of which require the live runtime.
- **That the SDK accepts an infinite timeout and treats it as "no deadline".** This was established
  by reading the SDK's own implementation, which carries the value into a linked cancellation
  source's delayed cancel, where `Timeout.InfiniteTimeSpan` means "never" — a documented framework
  behavior. It was checked rather than assumed, because the alternative is the SDK's sixty-second
  default, which would fail nearly every tool-using turn and would look like a model or tool fault.
  It is nonetheless a reading of someone else's code rather than an observation of a running system.
- **That the best-effort session deletion succeeds.** It is deliberately swallowed, so even a live
  run would not report its failure; what a live run can establish is whether per-rotation session
  data accumulates on disk.

**The mitigation is that the production implementation is kept small enough to read.** It forwards
two calls and owns one guarded disposal, and every decision worth testing is placed *above* the seam
where the tests reach it. A wider seam would move decisions below the line and out of evidence, which
is the reason the interface has exactly one method.

The scripted channel that stands in for it lives in `FakeCopilotTurnChannel.cs` within the
`DemaConsulting.AgentKit.Agents.Copilot.Tests` project, and is claimed by this unit's review-set for
the same reason the ChatClient adapter's recording client is claimed by its units: it is the double
the evidence rests on, and a reviewer cannot judge that evidence without it.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Copilot CLI is started, **no credential is used**, and no network
  access is made — which is the whole reason this seam exists
- **Mocking**: A hand-written scripted channel; no mocking framework
- **File system**: None
- **Isolation**: Each test constructs its own channel; no state is shared

### Acceptance Criteria

A test run passes when every scenario below passes without error or exception beyond those explicitly
asserted. Any session opened and not released, any release that also disposes the client, any session
shared between rotations or consolidations, or any turn taken after a caller's cancellation
constitutes a failure. **No acceptance criterion here covers the production implementation's own
calls into the SDK**; see the verification approach above.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotTurnChannel-CarriesOneRuntimeSession: The Seam Carries a Turn

**Tests**:

- `CopilotProviderSession_Send_Answer_IsReturnedAndRecordedOnce`
- `CopilotProviderSessionFactory_CreateAsync_RegistersTheObserverBeforeCreation`
- `CopilotSummarizer_Consolidate_RunsOutOfTheSessionBeingCompacted`

These are the scenarios that demonstrate the seam is sufficient: a turn sent through it produces an
answer and a transcript, a session opened through it carries the event handler that was on its
configuration, and a fresh one is opened per consolidation. They belong to the units above the seam
and are described in full in their own verification chapters; they are listed here because they are
the only evidence this unit has, and saying so is more honest than restating them as if they were
tests of the channel itself.

#### AgentKitAgentsCopilot-CopilotTurnChannel-OwnsTheSessionNotTheClient: One Session, Released, Client Untouched

**Tests**:

- `CopilotProviderSession_Dispose_DisposesTheRuntimeSession_AndNotTheClient`
- `CopilotSummarizer_Consolidate_Fails_DisposesTheSession`

The first asserts a released provider session released exactly one runtime session and that another
can still be opened on the same runtime afterwards — which a channel that had disposed the client
would have made impossible, and which is exactly what a rotation does. The second asserts the release
happens on the failing path too, which is where a leak hides.

The production implementation's additional step — asking the runtime to delete the session after
releasing it, and swallowing any failure — is not covered by either. It is a housekeeping call whose
failure is deliberately invisible, and it exists because releasing alone is documented to *preserve*
a session's state on disk, which would leave one preserved session per rotation behind. A live run is
what would establish whether it works.

#### AgentKitAgentsCopilot-CopilotTurnChannel-CallerGovernsTheDeadline: No Deadline of Its Own

**Tests**:

- `CopilotProviderSession_Send_Canceled_SendsNothing`
- `CopilotSummarizer_Consolidate_Canceled_OpensNothing`

These assert the caller's token is honored above the seam: a canceled turn sends nothing and a
canceled consolidation opens nothing. The scripted channel deliberately does **not** refuse a
canceled turn of its own accord, so the first assertion falsifies the adapter's own check rather than
the fake's.

That the channel places **no timeout of its own** on a live turn is, as stated above, established by
reading the SDK rather than by a test. The consequence of getting it wrong is worth restating: the
SDK's default is sixty seconds, a tool-using research turn routinely exceeds it, and the resulting
failure would read as a model or tool fault rather than as a deadline nobody chose.
