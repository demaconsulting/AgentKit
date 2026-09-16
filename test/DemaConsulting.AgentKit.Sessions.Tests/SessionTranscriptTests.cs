namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="TranscriptEntry"/> and <see cref="SessionTranscript"/>: the
///     append-only record the engine keeps out of session, and the tier-boundary split that never
///     separates a tool call from its result.
/// </summary>
public class SessionTranscriptTests
{
    /// <summary>
    ///     Proves the shared empty transcript holds nothing and costs nothing.
    /// </summary>
    [Fact]
    public void SessionTranscript_Empty_HoldsNothing()
    {
        // Arrange / Act: the shared empty instance
        var transcript = SessionTranscript.Empty;

        // Assert: no entries and no cost
        Assert.Empty(transcript.Entries);
        Assert.Equal(0, transcript.EstimatedTokens);
    }

    /// <summary>
    ///     Proves appending produces a new transcript and leaves the original untouched, which is
    ///     what makes the rotation engine a pure function of the layout it was handed.
    /// </summary>
    [Fact]
    public void SessionTranscript_Append_LeavesTheOriginalUnchanged()
    {
        // Arrange: a transcript holding one entry
        var original = SessionTranscript.Empty.Append(TranscriptEntry.User("first"));

        // Act: append to it
        var appended = original.Append(TranscriptEntry.Assistant("second"));

        // Assert: the original still holds one entry, the result holds two
        Assert.Single(original.Entries);
        Assert.Equal(2, appended.Entries.Count);
        Assert.Equal("second", appended.Entries[1].Text);
    }

    /// <summary>
    ///     Proves the entry list a transcript hands out cannot be cast back to its backing array and
    ///     mutated. A replaced entry — including a null one — would corrupt the token total cached
    ///     at construction and every rotation decision taken from it.
    /// </summary>
    [Fact]
    public void SessionTranscript_Entries_CannotBeCastAndMutated()
    {
        // Arrange: a transcript holding one entry
        var transcript = SessionTranscript.Empty.Append(TranscriptEntry.User("first"));

        // Act / Assert: the list is a read-only view, and writing through it is refused
        Assert.IsNotType<TranscriptEntry[]>(transcript.Entries);
        Assert.Throws<NotSupportedException>(() => ((IList<TranscriptEntry>)transcript.Entries)[0] = null!);
    }

    /// <summary>
    ///     Proves the overflow slice a split hands out cannot be cast back to an array and mutated.
    ///     The overflow is the material a consolidation is about to be given, so a caller able to
    ///     edit it could change what the summarizer sees after the split decided it.
    /// </summary>
    [Fact]
    public void SessionTranscript_SplitAtBudget_Overflow_CannotBeCastAndMutated()
    {
        // Arrange: three entries of ten tokens each and a budget that retains only the newest
        var transcript = SessionTestData.TranscriptOf(3, 10);

        // Act
        var (_, overflow) = transcript.SplitAtBudget(10);

        // Assert: the slice is a read-only view, and writing through it is refused
        Assert.Equal(2, overflow.Count);
        Assert.IsNotType<TranscriptEntry[]>(overflow);
        Assert.Throws<NotSupportedException>(() => ((IList<TranscriptEntry>)overflow)[0] = null!);
    }

    /// <summary>
    ///     Proves a run of entries appends in order, which is how one tool-using turn is recorded.
    /// </summary>
    [Fact]
    public void SessionTranscript_AppendMany_PreservesOrder()
    {
        // Arrange: the entries one tool-using turn produces
        TranscriptEntry[] turn =
        [
            TranscriptEntry.Assistant("thinking"),
            TranscriptEntry.ToolCall("c1", "read"),
            TranscriptEntry.ToolResult("c1", "contents")
        ];

        // Act: append them together
        var transcript = SessionTranscript.Empty.Append(turn);

        // Assert: in the order they happened
        Assert.Equal(["thinking", "read", "contents"], transcript.Entries.Select(entry => entry.Text));
    }

    /// <summary>
    ///     Proves an empty run is a no-op that allocates nothing, so a turn producing no entries
    ///     costs nothing to record.
    /// </summary>
    [Fact]
    public void SessionTranscript_AppendMany_Empty_ReturnsSameInstance()
    {
        // Arrange: any transcript
        var transcript = SessionTranscript.Empty.Append(TranscriptEntry.User("first"));

        // Act: append nothing
        var appended = transcript.Append([]);

        // Assert: the very same transcript
        Assert.Same(transcript, appended);
    }

    /// <summary>
    ///     Proves a null entry is refused: a null in the transcript would fail later, at a rotation,
    ///     far from the code that put it there.
    /// </summary>
    [Fact]
    public void SessionTranscript_Append_NullEntry_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SessionTranscript.Empty.Append((TranscriptEntry)null!));
    }

    /// <summary>
    ///     Proves a tool call without an identifier is refused, because an unidentified call cannot
    ///     be paired with its result and so could be orphaned at a tier boundary.
    /// </summary>
    [Fact]
    public void TranscriptEntry_Construct_ToolCallWithoutIdentifier_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new TranscriptEntry(TranscriptEntryKind.ToolCall, "read", null));
    }

    /// <summary>
    ///     Proves an identifier on a plain message is refused rather than ignored, so a caller that
    ///     supplies one learns it misunderstood the model.
    /// </summary>
    [Fact]
    public void TranscriptEntry_Construct_MessageWithIdentifier_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new TranscriptEntry(TranscriptEntryKind.UserMessage, "hello", "c1"));
    }

    /// <summary>
    ///     Proves a null text is refused; an entry with no text could not be rendered for
    ///     consolidation.
    /// </summary>
    [Fact]
    public void TranscriptEntry_Construct_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TranscriptEntry.User(null!));
    }

    /// <summary>
    ///     Proves an undefined entry kind is refused. It satisfies every pairing rule — it is
    ///     neither a call nor a result, so no identifier is required and none is rejected — and
    ///     would then render through the default branch as a consolidated record, making malformed
    ///     input part of the context an agent is seeded from.
    /// </summary>
    [Fact]
    public void TranscriptEntry_Construct_UndefinedKind_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TranscriptEntry((TranscriptEntryKind)999, "not history"));
    }

    /// <summary>
    ///     Proves the split retains the newest entries that fit and overflows the rest, which is the
    ///     whole of what tier zero is for.
    /// </summary>
    [Fact]
    public void SessionTranscript_SplitAtBudget_RetainsNewestWithinBudget()
    {
        // Arrange: five entries of twenty tokens each
        var transcript = SessionTestData.TranscriptOf(5, 20);

        // Act: split at a budget that holds exactly two of them
        var (retained, overflow) = transcript.SplitAtBudget(40);

        // Assert: the two newest stay verbatim, the three oldest overflow, oldest first
        Assert.Equal(2, retained.Entries.Count);
        Assert.Equal(3, overflow.Count);
        Assert.StartsWith("e3", retained.Entries[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("e0", overflow[0].Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the boundary snaps so the retained history never begins with a tool result whose
    ///     call has gone. Some providers reject an orphaned pair outright, and a model presented with
    ///     one cannot tell what was asked.
    /// </summary>
    [Fact]
    public void SessionTranscript_SplitAtBudget_BoundaryInsideToolPair_SnapsPastTheResult()
    {
        // Arrange: a call and its result sit either side of where a 45-token budget would cut
        var transcript = SessionTranscript.Empty
            .Append(SessionTestData.UserOfTokens(20, "ask"))
            .Append(TranscriptEntry.ToolCall("c1", new string('c', 16 * TokenEstimator.CharactersPerToken)))
            .Append(TranscriptEntry.ToolResult("c1", new string('r', 16 * TokenEstimator.CharactersPerToken)))
            .Append(SessionTestData.UserOfTokens(20, "next"));

        // Act: split where the unsnapped boundary would retain the result but not its call
        var (retained, overflow) = transcript.SplitAtBudget(45);

        // Assert: the orphaned result was pushed into the overflow instead
        Assert.Single(retained.Entries);
        Assert.StartsWith("next", retained.Entries[0].Text, StringComparison.Ordinal);
        Assert.Equal(TranscriptEntryKind.ToolResult, overflow[^1].Kind);
    }

    /// <summary>
    ///     Proves the pairing guarantee holds for an interleaved multi-tool turn, where the
    ///     orphaned result is not the first retained entry. Parallel tool calls are ordinary agent
    ///     traffic, and a boundary landing on the second call leaves the first call's result
    ///     retained with its call consolidated away — the same orphan, one entry further in.
    /// </summary>
    [Fact]
    public void SessionTranscript_SplitAtBudget_InterleavedToolPairs_RetainsNoOrphanedResult()
    {
        // Arrange: a valid interleaved turn - both calls issued, then both results
        var text = new string('x', 16 * TokenEstimator.CharactersPerToken);
        var transcript = SessionTranscript.Empty
            .Append(TranscriptEntry.ToolCall("c1", text))
            .Append(TranscriptEntry.ToolCall("c2", text))
            .Append(TranscriptEntry.ToolResult("c1", text))
            .Append(TranscriptEntry.ToolResult("c2", text));

        // Act: split at a budget that holds three of the four entries, so the unsnapped boundary
        // lands on c2's call and strands c1's result behind it
        var (retained, overflow) = transcript.SplitAtBudget(60);

        // Assert: every retained result still has its call
        var calls = retained.Entries
            .Where(entry => entry.Kind == TranscriptEntryKind.ToolCall)
            .Select(entry => entry.ToolCallId)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(
            retained.Entries,
            entry => entry.Kind == TranscriptEntryKind.ToolResult && !calls.Contains(entry.ToolCallId!));

        // Assert: no orphan-free suffix fits the budget, so the whole run is consolidated together
        Assert.Empty(retained.Entries);
        Assert.Equal(4, overflow.Count);
    }

    /// <summary>
    ///     Proves an entry larger than the whole budget is reported honestly rather than retained
    ///     anyway: it overflows like any other material and the retained set is empty.
    /// </summary>
    [Fact]
    public void SessionTranscript_SplitAtBudget_OversizedEntry_RetainsNothing()
    {
        // Arrange: one entry far larger than the budget it will be split against
        var transcript = SessionTranscript.Empty.Append(SessionTestData.UserOfTokens(80, "huge"));

        // Act: split at a budget it cannot fit
        var (retained, overflow) = transcript.SplitAtBudget(20);

        // Assert: nothing is retained and the entry is consolidated like any other overflow
        Assert.Empty(retained.Entries);
        Assert.Single(overflow);
    }

    /// <summary>
    ///     Proves a transcript that already fits is returned unchanged, so a rotation that needs no
    ///     aging allocates nothing and consolidates nothing.
    /// </summary>
    [Fact]
    public void SessionTranscript_SplitAtBudget_EverythingFits_ReturnsSameInstance()
    {
        // Arrange: three small entries
        var transcript = SessionTestData.TranscriptOf(3, 20);

        // Act: split at a budget larger than all of them
        var (retained, overflow) = transcript.SplitAtBudget(1000);

        // Assert: the very same transcript, and nothing aged out
        Assert.Same(transcript, retained);
        Assert.Empty(overflow);
    }

    /// <summary>
    ///     Proves a negative budget is refused as a programming error rather than treated as zero.
    /// </summary>
    [Fact]
    public void SessionTranscript_SplitAtBudget_NegativeBudget_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionTranscript.Empty.SplitAtBudget(-1));
    }

    /// <summary>
    ///     Proves rendering labels every kind of entry, so the stateless summarizer can tell who said
    ///     what and which result answered which call.
    /// </summary>
    [Fact]
    public void SessionTranscript_Render_LabelsEveryKind()
    {
        // Arrange: one entry of each conversational kind
        TranscriptEntry[] entries =
        [
            TranscriptEntry.User("ask"),
            TranscriptEntry.Assistant("answer"),
            TranscriptEntry.ToolCall("c1", "read"),
            TranscriptEntry.ToolResult("c1", "contents")
        ];

        // Act: render them as consolidation material
        var rendered = SessionTranscript.Render(entries);

        // Assert: one labeled line each, with the pairing identifier visible
        Assert.Equal(
            "USER: ask\nASSISTANT: answer\nTOOL CALL [c1]: read\nTOOL RESULT [c1]: contents",
            rendered);
    }

    /// <summary>
    ///     Proves a null element is refused at the boundary rather than dereferenced, matching the
    ///     rule the neighboring append, seed and turn APIs already apply.
    /// </summary>
    /// <remarks>
    ///     A public method that dereferenced every element produced an undocumented
    ///     <c>NullReferenceException</c> from inside a LINQ projection, which names neither the
    ///     argument nor the position at fault. Every other public member in this package that takes
    ///     a sequence of entries rejects a null element with an <c>ArgumentException</c> naming the
    ///     parameter, so this one does too.
    /// </remarks>
    [Fact]
    public void SessionTranscript_Render_NullEntry_Throws()
    {
        // Arrange: a sequence with a null among otherwise valid entries
        TranscriptEntry[] entries = [TranscriptEntry.User("ask"), null!];

        // Act / Assert: refused by argument, not by dereference
        var error = Assert.Throws<ArgumentException>(() => SessionTranscript.Render(entries));
        Assert.Equal("entries", error.ParamName);
    }

    /// <summary>
    ///     Proves a consolidated record renders under its own label, so material seeded from a tier
    ///     is not mistaken for something the model said.
    /// </summary>
    [Fact]
    public void TranscriptEntry_ToTranscriptLine_ContextRecord_LabelsAsRecord()
    {
        // Arrange: a consolidated record
        var entry = TranscriptEntry.ContextRecord("earlier work");

        // Act / Assert: labeled as a record rather than as a message
        Assert.Equal("RECORD: earlier work", entry.ToTranscriptLine());
    }
}
