## CompactingAgentSession Unit Verification Design

This document describes the unit-level verification strategy for `CompactingAgentSession`.

### Verification Approach

`CompactingAgentSession` is verified with deterministic provider factories and summarizers. Tests
exercise normal sending, threshold-triggered rotation, reading the window from the provider,
compaction-level adaptation, divergent tokenizer behavior, disposal, failed-disposal retry, and
creation or rotation failures that leave provider-session ownership clear.

Compaction-level adaptation is verified as hysteresis in turns. A rotation soon after the previous one
compacts one level terser, or discards the oldest slot when the level is already High; a rotation
after enough quiet turns relaxes it. The response reports the level and whether material was dropped.

The termination guard is verified separately, because it is the only bound that survives a summarizer
that never produces a record: with every tier empty, the drop under pressure falls through to the
oldest verbatim turn, and the tail therefore stops growing.

The most important pressure tests use `DivergentTokenizerProviderSession`. It reports provider usage
at 1x, 2x and 3x the rate `InMemoryProviderSession` charges for the same history, proving the session
keeps answering and terminates when one provider counts a conversation far more heavily than another.
A narrow-window variant asserts the response stream reaches `CompactionLevel.High` and reports
`MaterialDropped`.

Unit tests reside in `CompactingAgentSessionTests.cs`, with provider doubles in
`DivergentTokenizerProviderSession.cs` and `ProviderTestDoubles.cs`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; provider behavior is supplied by local doubles
- **Mocking**: Hand-written provider factories and `FakeSummarizer`; no mocking framework
- **Isolation**: Each test constructs its own options, factory and session

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any session that fails to answer, accepts a blank message, rotates without
adopting and releasing sessions in order, takes an occupancy figure from anywhere but the live
provider session, fails to report high pressure or dropped material, lets its context grow without
bound while consolidation never succeeds, orphans a provider session it could not adopt, or fails to
retry disposal constitutes a failure.

### Test Scenarios

#### AgentKitCore-CompactingAgentSession-AnswersTurns: A Session Starts Clean and Answers Cleanly

**Test**: `CompactingAgentSession_Send_AnswersTurns`

Creates a compacting session over the in-memory provider, sends a message and asserts the provider
answer is returned with low compaction and no rotation for the clean turn.

#### AgentKitCore-CompactingAgentSession-RotatesAtThreshold: Crossing the Threshold Replaces the Session

**Test**: `CompactingAgentSession_Send_RotatesAtThreshold`

Drives enough turns to cross the rotation threshold, then asserts rotations occurred, the provider
factory created one live session plus replacements, and every superseded provider session was
disposed.

#### AgentKitCore-CompactingAgentSession-TakesTheWindowFromTheProvider: A Reported Crossing Reaches the Provider

**Test**: `CompactingAgentSession_Usage_IsTheProviderSessionsOwnFigure`

Verifies the session's usage is the reading the live provider session gave it, which is the
unit-level proof that the window and the occupancy come from the adapter and nowhere else. Rotation
threshold decisions therefore use that reading's window, overhead and conversation, so the comparison
is in one currency by construction rather than by a rule for choosing between two.

#### AgentKitCore-CompactingAgentSession-AdaptsLevelUnderPressure: Pressure Adapts and Terminates

**Tests**:

- `CompactingAgentSession_DivergentTokenizer_KeepsAnsweringAndTerminates`
- `CompactingAgentSession_DivergentTokenizer_RotatesMoreOftenAndEscalatesHigher`
- `CompactingAgentSession_AfterAQuietStretch_RelaxesTheCompactionLevel`
- `CompactingAgentSession_TightWindow_EscalatesToHighAndReportsDroppedMaterial`

The divergent-tokenizer theory runs at 1x, 2x and 3x divergence and asserts every turn receives a
response: a liveness property proving the session keeps answering and terminates rather than churning
silently or throwing. Divergence is then exercised as a difference: against the same reported provider
window, a 2x and 3x provider rotates strictly more often and escalates to a strictly higher compaction
level than a 1x provider — rule 2 reading the provider's own count against its own window. Each
divergence test collapses and fails if the multiplier is reverted to 1x.

Escalation is only half of adapting, so the quiet-stretch test covers the other half: it drives the
level up under heavy turns, then sends a long run of light ones and asserts the level comes back down.
That branch is the only path that lowers a level and is guarded by two conditions at once — a prior
rotation and at least `m` quiet turns — so no other test reaches it; inverting the guard fails this
test alone. Without it a session that met one busy stretch would pay for it in fidelity for the rest
of its life.

An earlier test drove a drop by configuring a window tighter than the one the provider reported and
asserted material was discarded. That encoded a defect rather than a behavior — the provider had ample
room, and the history was thrown away only because two windows disagreed. There is now only one
window, so the condition cannot arise.

The narrow-window test is the session-level pressure test: with a provider window too small to hold a
full structure, the session escalates until it is at its tersest and then bins the oldest slot, and at
least one response reports `CompactionLevel.High` while at least one reports `MaterialDropped`.

#### AgentKitCore-CompactingAgentSession-BoundsTheContextWhenConsolidationNeverSucceeds: The Context Stops Growing

**Test**: `CompactingAgentSession_SummarizerAlwaysBlank_StopsGrowing`

Drives a session whose summarizer always answers blank, so no slot is ever written and every tier
stays empty. The tail is allowed to settle over twelve turns, then twenty-four more are sent and the
tail is asserted not to have grown. This is the only test that reaches the fall-through from the
tiers to the oldest verbatim turn: every other pressure test has slots to bin, so the fall-through
could be deleted and they would all still pass, while the tail here would grow from twelve turns to
thirty-six.

#### AgentKitCore-CompactingAgentSession-ReleasesProviderSessionItCannotAdopt: Nothing Created Is Lost

**Tests**:

- `CompactingAgentSession_Create_UsageThrows_ReleasesAndRethrowsOriginal`
- `CompactingAgentSession_Create_UsageThrowsAndReleaseFails_ReportsTheCreationFailure`
- `CompactingAgentSession_Rotate_ReplacementUsageThrows_ReleasesReplacement`
- `CompactingAgentSession_Create_NullProviderSession_Throws`

Covers provider-session ownership during failure. Creation failure releases the provider and
rethrows the original error when release succeeds. When the release fails as well, the original
creation failure is still what the caller learns, because the reason creation failed is of more use
than the reason the cleanup after it failed. A replacement that cannot be adopted during rotation is
released, and a factory returning null is refused.

#### AgentKitCore-CompactingAgentSession-RejectsBlankMessage: Invalid Use Is Refused

**Tests**:

- `CompactingAgentSession_Send_BlankMessage_Throws`
- `CompactingAgentSession_Send_AfterDispose_Throws`

Rejects a blank message and a send after disposal. A blank turn would spend context to say nothing,
and a disposed session cannot safely forward messages to its released provider session.

#### AgentKitCore-CompactingAgentSession-DisposesProviderSession: Disposal Releases the Live Session

**Tests**:

- `CompactingAgentSession_Dispose_ReleasesLiveProviderSession`
- `CompactingAgentSession_Dispose_FailedRelease_PropagatesAndRetries`

Asserts disposal releases the current provider session. A failed release propagates and a later
dispose call tries again, preserving retryable ownership rather than treating a failed release as
complete.
