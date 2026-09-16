## SessionTranscript Unit Verification Design

This document describes the unit-level verification strategy for `TranscriptEntry` and
`SessionTranscript`.

### Verification Approach

The transcript is verified as append-only, turn-granular history kept outside the provider session.
Tests append whole turns, split by newest-turn count, drop the oldest turn and render entries with
stable labels. Because all compaction boundaries are whole turns, the redesigned core no longer
needs an entry-level boundary rule for tool traffic.

`TranscriptEntry` validation is covered with the provider-session tests because entries are part of
the provider seam. Exact-size transcript builders in `SessionTestData.cs` support the rotation tests
that consume this unit.

Unit tests reside in `SessionTranscriptTests.cs`; entry validation evidence also resides in
`ProviderSessionTests.cs`.

### Test Environment

- **Framework**: xUnit running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no network access is used
- **Mocking**: None required
- **Isolation**: Each test builds its own transcript and entries

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any turn split into partial entries, any newest-tail split that keeps the wrong
turns, any inability to drop the oldest turn for rule five, any unlabeled rendering, or any malformed
entry accepted constitutes a failure.

### Test Scenarios

#### AgentKitSessions-SessionTranscript-KeptOutOfSession: Appending Never Mutates

**Tests**:

- `SessionTranscript_AppendTurn_GroupsEntriesAsOneTurn`
- `SessionTranscript_AppendTurn_Malformed_Throws`

Asserts appending records a complete exchange as one turn and rejects a null sequence, empty turn or
null entry. The transcript remains the session-owned record that rotation can reason about without
editing the live provider session.

#### AgentKitSessions-SessionTranscript-PairedEntries: The Pairing Identifier Is Required Exactly Where It Means Something

**Test**: `TranscriptEntry_Construct_ValidatesPairingIdentifier`

Verifies tool calls carry identifiers and plain messages do not. The boundary guarantee now comes
from whole-turn operations, while entry validation still ensures tool traffic is well formed inside a
turn.

#### AgentKitSessions-SessionTranscript-SplitsAtTierBudget: The Newest Turns Within Budget Are Retained

**Tests**:

- `SessionTranscript_SplitAtTail_KeepsNewestTurns`
- `SessionTranscript_SplitAtTail_NothingOlder_RetainsAll`
- `SessionTranscript_DropOldestTurn_RemovesTheOldest`

The requirement identifier is retained for traceability, but the operation now splits by turn count.
The tests assert the newest turns remain verbatim, all older entries are returned for consolidation,
a short transcript is retained whole, and the oldest turn can be dropped as the final fitting step.

#### AgentKitSessions-SessionTranscript-SnapsToolBoundary: A Boundary Inside a Tool Pair Snaps

**Test**: `SessionTranscript_AppendTurn_GroupsEntriesAsOneTurn`

N/A for entry-level snapping - the redesigned transcript never places a boundary inside a turn. The
test verifies the replacement behavior: a user message, tool call, tool result and assistant answer
are grouped as one indivisible turn.

#### AgentKitSessions-SessionTranscript-RendersLabeledMaterial: Material Says Who Said What

**Test**: `SessionTranscript_Render_IsDeterministicAndLabeled`

Asserts rendered material uses stable labels for user and assistant entries and refuses a null entry.
The rotation engine can therefore hand deterministic, labeled material to the summarizer.
