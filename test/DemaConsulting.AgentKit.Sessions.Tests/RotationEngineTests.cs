namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="RotationEngine"/>: the deterministic aging of a context by one
///     rotation, exercised entirely against a fake summarizer so that every assertion is about the
///     engine rather than about a model.
/// </summary>
/// <remarks>
///     Every test here supplies a <see cref="FakeSummarizer"/>, which makes the engine a pure
///     function of the layout it was handed. That is what allows the cascade ordering, the ratchet,
///     the boundary snap and the saturation reports to be asserted exactly rather than
///     approximately.
/// </remarks>
public class RotationEngineTests
{
    /// <summary>
    ///     Proves a rotation whose recent history already fits is a pure re-seed: nothing ages, no
    ///     summarizer call is made, and the layout is handed back as it was.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_NothingOverflows_ConsolidatesNothing()
    {
        // Arrange: sixty tokens of history against a hundred-token verbatim tier
        var summarizer = new FakeSummarizer();
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(3, 20));

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(layout, summarizer, TestContext.Current.CancellationToken);

        // Assert: the same layout, and the summarizer was never asked anything
        Assert.Same(layout, outcome.Layout);
        Assert.Equal(0, outcome.ConsolidationCount);
        Assert.Equal(0, summarizer.CallCount);
        Assert.False(outcome.IsSaturated);
    }

    /// <summary>
    ///     Proves overflowing history is folded into tier one and the newest turns stay verbatim —
    ///     and that only the tier that actually overflowed was consolidated, which is where the
    ///     tiered scheme's cost advantage over a flat rolling summary comes from.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_Overflow_FoldsIntoTierOneOnly()
    {
        // Arrange: two hundred tokens of history against a hundred-token verbatim tier
        var summarizer = new FakeSummarizer();
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20));

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(layout, summarizer, TestContext.Current.CancellationToken);

        // Assert: one consolidation, into tier one, as a first recording
        Assert.Equal(1, outcome.ConsolidationCount);
        Assert.Equal([(1, true)], summarizer.Shape);

        // Assert: tier one now holds a record, the coarser tiers were left alone
        Assert.False(outcome.Layout.CoarseTiers[0].IsEmpty);
        Assert.True(outcome.Layout.CoarseTiers[1].IsEmpty);
        Assert.True(outcome.Layout.CoarseTiers[2].IsEmpty);

        // Assert: the newest turns survived verbatim, within tier zero's budget
        Assert.Equal(5, outcome.Layout.Transcript.Entries.Count);
        Assert.True(outcome.Layout.Transcript.EstimatedTokens <= 100);
    }

    /// <summary>
    ///     Proves the arrangement is bounded by construction: after a rotation the whole context sits
    ///     within the fixed overhead plus the sum of the tier budgets.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_AfterRotation_ContextIsWithinItsConstructionBound()
    {
        // Arrange: far more history than the bound allows, and a summarizer that reduces sharply
        var summarizer = new FakeSummarizer(0.05);
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(40, 20));

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(layout, summarizer, TestContext.Current.CancellationToken);

        // Assert: back inside the bound the configuration promised
        Assert.False(layout.IsWithinBound);
        Assert.True(outcome.Layout.IsWithinBound);
    }

    /// <summary>
    ///     Proves consolidation is a ratchet: the second rotation is handed tier one's existing
    ///     record as an input rather than starting from the new material alone. Without this the
    ///     arrangement degenerates into re-summarizing a summary, which is what makes a flat rolling
    ///     summary forget everything beyond a handful of rotations.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_SecondRotation_CarriesThePreviousRecordForward()
    {
        // Arrange: rotate once so tier one holds a record
        var summarizer = new FakeSummarizer();
        var first = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20)),
            summarizer,
            TestContext.Current.CancellationToken);
        var tierOneRecord = first.Layout.CoarseTiers[0].Content;

        // Act: add more history and rotate again
        var grown = first.Layout.WithTranscript(first.Layout.Transcript.Append(
            SessionTestData.TranscriptOf(10, 20).Entries));
        await RotationEngine.RotateAsync(grown, summarizer, TestContext.Current.CancellationToken);

        // Assert: the second consolidation received the first one's record as an input
        var second = summarizer.Requests[1];
        Assert.Equal(1, second.TierIndex);
        Assert.False(second.IsDegradation);
        Assert.Equal(tierOneRecord, second.PreviousRecord);
    }

    /// <summary>
    ///     Proves a tier that cannot hold both its old record and the new material cascades: the
    ///     <em>older</em> record degrades into the next coarser tier and the tier is then re-recorded
    ///     holding the new material alone. That ordering is what keeps the hierarchy monotonic in
    ///     age — coarser always means older.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_TierOneOverflows_DegradesTheOlderRecordIntoTierTwo()
    {
        // Arrange: a summarizer whose combined record overflows tier one but whose first recording
        // into any tier comfortably fits
        var summarizer = new FakeSummarizer(request =>
            new string('y', request.IsDegradation ? 80 : 260));
        var first = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20)),
            summarizer,
            TestContext.Current.CancellationToken);

        // Act: grow the history and rotate again, so tier one must hold old and new together
        var grown = first.Layout.WithTranscript(first.Layout.Transcript.Append(
            SessionTestData.TranscriptOf(10, 20).Entries));
        var second = await RotationEngine.RotateAsync(grown, summarizer, TestContext.Current.CancellationToken);

        // Assert: merge at tier one, degrade the older record into tier two, re-record tier one
        Assert.Equal(3, second.ConsolidationCount);
        Assert.Equal([(1, true), (1, false), (2, true), (1, true)], summarizer.Shape);

        // Assert: tier two now holds the degraded older record, and both tiers fit their budgets
        Assert.False(second.Layout.CoarseTiers[1].IsEmpty);
        Assert.True(second.Layout.CoarseTiers[0].IsWithinBudget);
        Assert.True(second.Layout.CoarseTiers[1].IsWithinBudget);
    }

    /// <summary>
    ///     Proves a tool call and its result are never separated by a rotation. A boundary falling
    ///     between them would leave the seeded history beginning with a result whose call is gone,
    ///     which some providers reject outright and no model can interpret.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_BoundaryInsideToolPair_NeverSeedsAnOrphanedResult()
    {
        // Arrange: a history of call-and-result pairs long enough to overflow tier zero
        var summarizer = new FakeSummarizer();
        var transcript = SessionTranscript.Empty;
        for (var index = 0; index < 8; index++)
        {
            transcript = transcript
                .Append(TranscriptEntry.ToolCall($"c{index}", new string('c', 16 * TokenEstimator.CharactersPerToken)))
                .Append(TranscriptEntry.ToolResult($"c{index}", new string('r', 16 * TokenEstimator.CharactersPerToken)));
        }

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.SmallPolicy, transcript),
            summarizer,
            TestContext.Current.CancellationToken);

        // Assert: what survives verbatim never begins with a result, so no call was orphaned
        Assert.NotEmpty(outcome.Layout.Transcript.Entries);
        Assert.NotEqual(TranscriptEntryKind.ToolResult, outcome.Layout.Transcript.Entries[0].Kind);
    }

    /// <summary>
    ///     Proves saturation is detected rather than silently repeated: a consolidation that returns
    ///     output nearly as large as its input means the material holds no redundancy left, and
    ///     rotating again would spend summarizer tokens for nothing.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_ConsolidationDoesNotReduce_ReportsNoRedundancy()
    {
        // Arrange: a summarizer that returns its material unchanged
        var summarizer = new FakeSummarizer(request => request.Material);
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20));

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(layout, summarizer, TestContext.Current.CancellationToken);

        // Assert: the failure to reduce is surfaced, with the figures that show it
        Assert.True(outcome.IsSaturated);
        var signal = outcome.Saturations.First(s => s.Reason == SaturationReason.NoRedundancy);
        Assert.Equal(1, signal.TierIndex);
        Assert.True(signal.OutputTokens >= signal.InputTokens * CompactionPolicy.DefaultSaturationRatio);
    }

    /// <summary>
    ///     Proves a record that will not fit and has nowhere coarser to go is reported rather than
    ///     quietly left over budget, so an application can see that the bound is no longer being met.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_CoarsestTierCannotFit_ReportsTierOverBudget()
    {
        // Arrange: a two-tier policy with a small coarse tier, and a summarizer that cannot reduce
        var summarizer = new FakeSummarizer(request => request.Material);
        var layout = SessionTestData.LayoutOf(
            new CompactionPolicy([100, 30]),
            SessionTestData.TranscriptOf(10, 20));

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(layout, summarizer, TestContext.Current.CancellationToken);

        // Assert: the over-budget condition is reported for the tier that could not hold it
        Assert.Contains(outcome.Saturations, s => s.Reason == SaturationReason.TierOverBudget);
    }

    /// <summary>
    ///     Proves the engine is deterministic: the same layout and the same summarizer behavior
    ///     produce byte-identical tiers and verbatim history. This is the property that makes every
    ///     other assertion in this file meaningful.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_SameInputs_ProduceIdenticalOutput()
    {
        // Arrange: one layout, two independent summarizers behaving identically
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20));

        // Act: rotate the same layout twice
        var first = await RotationEngine.RotateAsync(layout, new FakeSummarizer(), TestContext.Current.CancellationToken);
        var second = await RotationEngine.RotateAsync(layout, new FakeSummarizer(), TestContext.Current.CancellationToken);

        // Assert: identical tiers, identical surviving history, identical cost
        Assert.Equal(
            first.Layout.CoarseTiers.Select(tier => tier.Content),
            second.Layout.CoarseTiers.Select(tier => tier.Content));
        Assert.Equal(
            first.Layout.Transcript.Entries.Select(entry => entry.Text),
            second.Layout.Transcript.Entries.Select(entry => entry.Text));
        Assert.Equal(first.ConsolidationCount, second.ConsolidationCount);
    }

    /// <summary>
    ///     Proves a missing layout is refused as a programming error.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_NullLayout_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            RotationEngine.RotateAsync(null!, new FakeSummarizer(), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a missing summarizer is refused: compaction cannot happen without one, and a
    ///     session that silently never compacted would fail much later and much less clearly.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_NullSummarizer_Throws()
    {
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(3, 20));

        await Assert.ThrowsAsync<ArgumentNullException>(() => RotationEngine.RotateAsync(layout, null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a summarizer returning null is refused rather than stored: a null record would
    ///     surface as a missing tier at the next rotation, far from the implementation that caused it.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_SummarizerReturnsNull_Throws()
    {
        var summarizer = new FakeSummarizer(_ => null);
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RotationEngine.RotateAsync(layout, summarizer, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a rotation can be canceled, so a host shutting down is not held open by a
    ///     summarizer round trip.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_Canceled_Throws()
    {
        var summarizer = new FakeSummarizer();
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(10, 20));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RotationEngine.RotateAsync(layout, summarizer, source.Token));
    }

    /// <summary>
    ///     Proves a rotation that has nothing to do still honors an already-canceled token. The
    ///     no-work path returns before any consolidation, so a check placed only between
    ///     consolidations would hand a caller a successful rotation result for a rotation it had
    ///     already canceled, whenever the transcript happened to fit.
    /// </summary>
    [Fact]
    public async Task RotationEngine_RotateAsync_CanceledWithNothingToRotate_Throws()
    {
        // Arrange: a transcript of 60 tokens against a tier-zero budget of 100, so nothing
        // overflows and the rotation is a pure re-seed, and a token canceled before the call
        var summarizer = new FakeSummarizer();
        var layout = SessionTestData.LayoutOf(SessionTestData.SmallPolicy, SessionTestData.TranscriptOf(3, 20));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        // Act / Assert: refused rather than reported as a successful rotation
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RotationEngine.RotateAsync(layout, summarizer, source.Token));
    }
}
