## CompactingAgentSession Unit Verification Design

This document describes the unit-level verification strategy for `CompactingAgentSession`.

### Verification Approach

`CompactingAgentSession` is verified with deterministic provider factories and summarizers. Tests
exercise normal sending, threshold-triggered rotation, provider usage preference, compaction-level
adaptation, divergent tokenizer behavior, disposal, failed-disposal retry, and creation or rotation
failures that leave provider-session ownership clear.

Compaction-level adaptation is verified as hysteresis. A rotation soon after the previous one
escalates pressure; a rotation after enough quiet turns relaxes it. The response reports the settled
level and whether material was dropped.

The most important pressure tests use `DivergentTokenizerProviderSession`. It reports provider usage
at 1x, 2x and 3x this library's estimate, proving the session keeps answering and terminates when a
provider's tokenizer diverges from the estimator. A tight-window variant asserts the response stream
reaches `CompactionLevel.High` and reports `MaterialDropped`.

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
adopting and releasing sessions in order, mixes provider and estimated occupancy, fails to report
high pressure or dropped material, leaks a retained provider session, or fails to retry disposal
constitutes a failure.

### Test Scenarios

#### AgentKitSessions-CompactingAgentSession-AnswersTurns: A Session Starts Clean and Answers Cleanly

**Test**: `CompactingAgentSession_Send_AnswersTurns`

Creates a compacting session over the in-memory provider, sends a message and asserts the provider
answer is returned with low compaction and no rotation for the clean turn.

#### AgentKitSessions-CompactingAgentSession-RotatesAtThreshold: Crossing the Threshold Replaces the Session

**Test**: `CompactingAgentSession_Send_RotatesAtThreshold`

Drives enough turns to cross the rotation threshold, then asserts rotations occurred, the provider
factory created one live session plus replacements, and every superseded provider session was
disposed.

#### AgentKitSessions-RotationEngine-HonorsTheTriggerCurrency: A Reported Crossing Reaches the Provider

**Test**: `CompactingAgentSession_Usage_PrefersProviderReport`

Verifies the compacting session prefers provider-origin usage when the provider reports it. Rotation
threshold decisions therefore use provider figures on the provider path and estimated figures only on
the silent-provider path.

#### AgentKitSessions-CompactingAgentSession-RefusesUnusableReportedWindow: Pressure Adapts and Terminates

**Tests**:

- `CompactingAgentSession_DivergentTokenizer_KeepsAnsweringAndTerminates`
- `CompactingAgentSession_DivergentTokenizer_RotatesMoreOftenAndEscalatesHigher`
- `CompactingAgentSession_DivergentTokenizer_UnderTighterConfiguredBudget_DropsMaterialWhereConvergentDoesNot`
- `CompactingAgentSession_TightWindow_EscalatesToHighAndReportsDroppedMaterial`

The divergent-tokenizer theory runs at 1x, 2x and 3x divergence and asserts every turn receives a
response: a liveness property proving the session keeps answering and terminates rather than churning
silently or throwing. Divergence is then exercised as a difference: at a provider window equal to the
configured window, a 2x and 3x provider rotates strictly more often and escalates to a strictly higher
compaction level than a 1x provider — Rule 2 reading the provider's own count against its own window;
and when the application configures a budget tighter than the provider's real window, a 2x provider
reaches `CompactionLevel.High` and reports `MaterialDropped` where a 1x provider does neither, because
divergence-driven early rotation yields a seed the estimate-currency Rule 5 threshold rejects. Each
divergence test collapses and fails if the multiplier is reverted to 1x. The tight-window test is a
session-level Rule 5 test: with a window too small to hold a full structure it uses a non-divergent
provider and asserts at least one response reports `CompactionLevel.High` and at least one reports
`MaterialDropped`.

#### AgentKitSessions-CompactingAgentSession-ReleasesProviderSessionItCannotAdopt: Nothing Created Is Lost

**Tests**:

- `CompactingAgentSession_Create_UsageThrowsButReleased_RethrowsOriginal`
- `CompactingAgentSession_Create_UsageThrowsAndReleaseFails_CarriesRetainedSession`
- `CompactingAgentSession_Rotate_ReplacementUsageThrows_ReleasesReplacement`
- `CompactingAgentSession_Create_NullProviderSession_Throws`

Covers provider-session ownership during failure. Creation failure releases the provider and
rethrows the original error when release succeeds. If release fails, `AgentSessionCreationException`
carries `RetainedProviderSession`. A replacement that cannot be adopted during rotation is released,
and a factory returning null is refused.

#### AgentKitSessions-CompactingAgentSession-PrefersProviderUsage: The Better Measurement Wins

**Test**: `CompactingAgentSession_Usage_PrefersProviderReport`

Asserts session usage is provider-origin when the provider reports it. This is the unit-level proof
that provider accounting is not replaced by the fallback estimate.

#### AgentKitSessions-CompactingAgentSession-RejectsBlankMessage: Invalid Use Is Refused

**Tests**:

- `CompactingAgentSession_Send_BlankMessage_Throws`
- `CompactingAgentSession_Send_AfterDispose_Throws`

Rejects a blank message and a send after disposal. A blank turn would spend context to say nothing,
and a disposed session cannot safely forward messages to its released provider session.

#### AgentKitSessions-CompactingAgentSession-DisposesProviderSession: Disposal Releases the Live Session

**Tests**:

- `CompactingAgentSession_Dispose_ReleasesLiveProviderSession`
- `CompactingAgentSession_Dispose_FailedRelease_PropagatesAndRetries`

Asserts disposal releases the current provider session. A failed release propagates and a later
dispose call tries again, preserving retryable ownership rather than treating a failed release as
complete.
