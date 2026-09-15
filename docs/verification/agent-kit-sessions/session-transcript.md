## SessionTranscript Unit Verification Design

This document describes the unit-level verification strategy for `TranscriptEntry` and
`SessionTranscript`.

### Verification Approach

Both types are immutable and deterministic, so they are verified by direct construction and direct
call. Nothing is mocked.

The scenarios that carry the weight are the `SplitAtBudget` ones, because that method is where tier
zero's boundary is decided and is the one place a tool call can be orphaned. They are built with the
exact-size helpers in `SessionTestData.cs`, which invert the token estimate so a test can ask for an
entry of precisely twenty tokens and state exactly which side of the boundary it falls on. A test
that could only say "roughly" could not distinguish the snapped boundary from the unsnapped one,
which is the whole point of the scenario.

The immutability scenarios assert the *original* is unchanged rather than only that the result is
correct, because immutability is what makes the rotation engine a pure function of its inputs; a
mutating append would still produce correct-looking transcripts while breaking every
before-and-after comparison the engine's own tests rely on.

Unit tests reside in `SessionTranscriptTests.cs`, with the exact-size builders in
`SessionTestData.cs`, both within the `DemaConsulting.AgentKit.Sessions.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; **no network access is used**
- **Mocking**: None required
- **Isolation**: Each test builds its own transcript; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any append that mutates its source, any split that retains more than its
budget, any split that leaves an orphaned tool result at the head of the retained history, or any
malformed entry accepted constitutes a failure.

### Test Scenarios

#### AgentKitSessions-SessionTranscript-KeptOutOfSession: Appending Never Mutates

**Tests**: `SessionTranscript_Empty_HoldsNothing`,
`SessionTranscript_Append_LeavesTheOriginalUnchanged`,
`SessionTranscript_AppendMany_PreservesOrder`,
`SessionTranscript_AppendMany_Empty_ReturnsSameInstance`,
`SessionTranscript_Entries_CannotBeCastAndMutated`

Asserts the shared empty transcript holds nothing and costs nothing; that appending returns a new
transcript and leaves the original at its previous length; that a run of entries — the shape one
tool-using turn produces — appends in the order it happened; and that appending nothing returns the
very same instance rather than an equal copy; and that the published entry list is not the backing
array and refuses a write through an `IList` cast. An entry replaced that way — including with a
null — would corrupt the token total cached at construction and every rotation decision taken from
it.

#### AgentKitSessions-SessionTranscript-PairedEntries: The Pairing Identifier Is Required Exactly Where It Means Something

**Tests**: `TranscriptEntry_Construct_ToolCallWithoutIdentifier_Throws`,
`TranscriptEntry_Construct_MessageWithIdentifier_Throws`,
`TranscriptEntry_Construct_NullText_Throws`,
`SessionTranscript_Append_NullEntry_Throws`

Four error paths. A tool call without an identifier could not be paired with its result and could
therefore be orphaned at a tier boundary. An identifier on a plain message is meaningless and is
refused rather than ignored, so a caller that supplies one learns it misunderstood the model. A null
text could not be rendered for consolidation, and a null entry would fail later, at a rotation, far
from the code that put it there.

#### AgentKitSessions-SessionTranscript-SplitsAtTierBudget: The Newest Turns Within Budget Are Retained

**Tests**: `SessionTranscript_SplitAtBudget_RetainsNewestWithinBudget`,
`SessionTranscript_SplitAtBudget_OversizedEntry_RetainsNothing`,
`SessionTranscript_SplitAtBudget_EverythingFits_ReturnsSameInstance`,
`SessionTranscript_SplitAtBudget_NegativeBudget_Throws`

Normal operation and three boundaries. Five entries of twenty tokens split at forty retains exactly
the two newest and overflows the three oldest, oldest first. A single entry larger than the whole
budget retains nothing and overflows — reported honestly, because retaining it anyway would silently
break the bound the budget exists to enforce. A transcript that already fits is returned as the same
instance, so a rotation needing no aging allocates and consolidates nothing. A negative budget is
refused as a programming error rather than treated as zero.

#### AgentKitSessions-SessionTranscript-SnapsToolBoundary: A Boundary Inside a Tool Pair Snaps

**Test**: `SessionTranscript_SplitAtBudget_BoundaryInsideToolPair_SnapsPastTheResult`

The central scenario. Builds a history whose call and result sit either side of where an unsnapped
45-token boundary would cut, splits there, and asserts the retained history is the single newest
entry rather than the result plus that entry, and that the orphaned result was pushed into the
overflow instead. Some providers reject an orphaned result outright, and a model presented with one
cannot tell what was asked. The boundary moves later rather than earlier because moving later can
only shrink the retained set and so can never push it back over budget.

#### AgentKitSessions-SessionTranscript-RendersLabeledMaterial: Material Says Who Said What

**Tests**: `SessionTranscript_Render_LabelsEveryKind`,
`TranscriptEntry_ToTranscriptLine_ContextRecord_LabelsAsRecord`

Asserts the exact rendered string for one entry of each conversational kind, with the pairing
identifier visible on both halves of a tool pair, and that a consolidated record renders under its
own label rather than as something the model said. The rendering is asserted exactly rather than
loosely because a fake summarizer in the engine's own tests asserts on what it was handed, and that
is only meaningful if the rendering is fixed.
