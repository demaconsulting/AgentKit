## SessionTranscript Unit Verification Design

This document describes the unit-level verification strategy for `TranscriptEntry` and
`SessionTranscript`.

### Verification Approach

The transcript is verified as append-only, turn-granular history kept outside the provider session.
Tests append whole turns, split by newest-turn count, drop the oldest turn and render entries with
stable labels. Because all compaction boundaries are whole turns, and the split hands older material
back as whole turns, the redesigned core needs no entry-level boundary rule for tool traffic.

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
turns or flattens the older ones, any unlabeled rendering, or any malformed entry accepted
constitutes a failure.

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

#### AgentKitSessions-SessionTranscript-SplitsAtTurnBoundary: The Newest Turns Are Retained, the Rest Handed Back Whole

**Tests**:

- `SessionTranscript_SplitAtTail_KeepsNewestTurns`
- `SessionTranscript_SplitAtTail_NothingOlder_RetainsAll`

The tests assert the newest turns remain verbatim, that the older material comes back as whole turns
rather than a flat run of entries — three turns of two entries each, not six entries — and that a
transcript with nothing older is retained whole. Returning turns is what keeps a consolidation that
must span several summarizer calls from splitting one turn across two of them.

#### AgentKitSessions-SessionTranscript-KeepsToolTrafficWithItsTurn: A Boundary Never Falls Inside a Tool Pair

**Test**: `SessionTranscript_AppendTurn_GroupsEntriesAsOneTurn`

The redesigned transcript never places a boundary inside a turn, so there is no snapping rule to
verify. The test verifies the behavior that replaced it: a user message, tool call, tool result and
assistant answer are grouped as one indivisible turn.

#### AgentKitSessions-SessionTranscript-RendersLabeledMaterial: Material Says Who Said What

**Test**: `SessionTranscript_Render_IsDeterministicAndLabeled`

Asserts rendered material uses stable labels for user and assistant entries and refuses a null entry.
The rotation engine can therefore hand deterministic, labeled material to the summarizer.
