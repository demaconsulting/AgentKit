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
`TranscriptEntry_Construct_UndefinedKind_Throws`,
`SessionTranscript_Append_NullEntry_Throws`

Five error paths. A tool call without an identifier could not be paired with its result and could
therefore be orphaned at a tier boundary. An identifier on a plain message is meaningless and is
refused rather than ignored, so a caller that supplies one learns it misunderstood the model. An
undefined kind is refused because it would otherwise pass every pairing rule — it is neither a call
nor a result, so no identifier is required and none is rejected — and then render through the
default labeling branch as though it were a consolidated record, making malformed input part of the
context an agent is seeded from. A null
text could not be rendered for consolidation, and a null entry would fail later, at a rotation, far
from the code that put it there.

#### AgentKitSessions-SessionTranscript-SplitsAtTierBudget: The Newest Turns Within Budget Are Retained

**Tests**: `SessionTranscript_SplitAtBudget_RetainsNewestWithinBudget`,
`SessionTranscript_SplitAtBudget_OversizedEntry_RetainsNothing`,
`SessionTranscript_SplitAtBudget_EverythingFits_ReturnsSameInstance`,
`SessionTranscript_SplitAtBudget_NegativeBudget_Throws`,
`SessionTranscript_SplitAtBudget_Overflow_CannotBeCastAndMutated`

Normal operation and three boundaries. Five entries of twenty tokens split at forty retains exactly
the two newest and overflows the three oldest, oldest first. A single entry larger than the whole
budget retains nothing and overflows — reported honestly, because retaining it anyway would silently
break the bound the budget exists to enforce. A transcript that already fits is returned as the same
instance, so a rotation needing no aging allocates and consolidates nothing. A negative budget is
refused as a programming error rather than treated as zero.

The last asserts the overflow is not a bare array and that writing through it is refused. The
overflow is the material a consolidation is about to be handed, so a caller able to cast it back
could change what the summarizer sees after the split had already decided it — the same defect
`Entries` is protected against, applied to the other collection this type publishes.

**What no scenario here can reach, and how it is verified instead.** The fit test's overflow
condition requires a tier-zero budget near the largest representable token count *and* entries
accumulated near it. Because a single entry is capped by the runtime's string limit at about 2^28
tokens, nine entries carrying the longest string that can exist are needed to cross it — roughly
sixteen gigabytes of live strings — which is not a test this suite can run, here or on any
reasonable machine. The same is true of the cached total's rejection. Both were therefore confirmed
by executing the exact expressions at those magnitudes outside the suite: the ninth entry makes
`used + candidate` wrap to a negative figure that compares below the budget, so the old test retains
it, while the subtraction reports correctly; and the same nine entries sum to −1,879,048,228 in
`int` against 2,415,919,068 wide. The scenarios above continue to verify the behavior at ordinary
magnitudes, which is where the fix must change nothing.

#### AgentKitSessions-SessionTranscript-SnapsToolBoundary: A Boundary Inside a Tool Pair Snaps

**Tests**: `SessionTranscript_SplitAtBudget_BoundaryInsideToolPair_SnapsPastTheResult`,
`SessionTranscript_SplitAtBudget_InterleavedToolPairs_RetainsNoOrphanedResult`

The central scenario. Builds a history whose call and result sit either side of where an unsnapped
45-token boundary would cut, splits there, and asserts the retained history is the single newest
entry rather than the result plus that entry, and that the orphaned result was pushed into the
overflow instead. Some providers reject an orphaned result outright, and a model presented with one
cannot tell what was asked. The boundary moves later rather than earlier because moving later can
only shrink the retained set and so can never push it back over budget.

The second test covers the shape the first does not reach, and the one real agent traffic produces
constantly: an interleaved turn of `call c1, call c2, result c1, result c2`. A 60-token budget holds
three of the four entries, so the boundary lands on c2's call — not a result, so a check that looked
only at the first retained entry passed it — while c1's result stayed retained with its call in the
overflow. The assertion is the guarantee itself rather than a position: every retained result is
matched against the retained calls, and none may be unmatched. It also asserts that in this
arrangement nothing is retained at all, because no orphan-free suffix fits the budget and
consolidating the whole run together is the only split that keeps every pair intact.

#### AgentKitSessions-SessionTranscript-RendersLabeledMaterial: Material Says Who Said What

**Tests**: `SessionTranscript_Render_LabelsEveryKind`,
`SessionTranscript_Render_NullEntry_Throws`,
`SessionTranscript_Append_NullEntry_Throws`,
`TranscriptEntry_ToTranscriptLine_ContextRecord_LabelsAsRecord`

Asserts the exact rendered string for one entry of each conversational kind, with the pairing
identifier visible on both halves of a tool pair, and that a consolidated record renders under its
own label rather than as something the model said. The rendering is asserted exactly rather than
loosely because a fake summarizer in the engine's own tests asserts on what it was handed, and that
is only meaningful if the rendering is fixed.

The null scenarios assert that **both** public entry points refuse a null where the caller supplied
it. Appending already did so; rendering did not, and dereferenced every element instead, so a null
among otherwise valid entries produced a `NullReferenceException` from inside the projection —
undocumented, and naming neither the argument nor the position at fault. Rendering is public and a
caller may compose material from a history of its own, so it is asserted to throw an
`ArgumentException` naming `entries`, matching the rule its neighbors in this package already
apply.
