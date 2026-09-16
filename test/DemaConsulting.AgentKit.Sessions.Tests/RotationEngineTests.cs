namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="RotationEngine"/>: rules 1–5, chunking, escalate-until-it-fits and
///     drop-until-it-fits.
/// </summary>
public class RotationEngineTests
{
    /// <summary>
    ///     A threshold large enough that a small layout always fits without escalating or dropping.
    /// </summary>
    private const int Roomy = 1_000_000;

    /// <summary>
    ///     Proves rule 2: everything older than the level-adjusted tail is consolidated into one
    ///     slot appended to tier one, and the newest turns stay verbatim.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_ConsolidatesOlderIntoOneTierOneSlot()
    {
        var summarizer = FakeSummarizer.Fixed(2);
        var layout = SessionTestData.LayoutOf(SessionTestData.TranscriptOf(5, tokensEach: 40));

        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, CompactionLevel.Low, verbatimTurns: 2, Roomy, CancellationToken.None);

        Assert.Equal(1, outcome.Layout.Tiers[0].Count);
        Assert.Equal(2, outcome.Layout.Tail.TurnCount);
        Assert.Equal(1, outcome.ConsolidationCount);
        Assert.Equal([(1, ConsolidationPrompt.LowInstruction)], summarizer.Shape);
        Assert.False(outcome.MaterialDropped);
    }

    /// <summary>
    ///     Proves rule 3: a full coarse tier consolidates its slots as peers into one slot of the
    ///     next tier and is cleared, with the arriving slot placed in the now-empty tier.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_FullTier_ConsolidatesAsPeersIntoNextTier()
    {
        var summarizer = FakeSummarizer.Fixed(2);
        var tierOne = SessionTestData.TierOf(
            new Slot("a"), new Slot("b"), new Slot("c"), new Slot("d"));
        var layout = SessionTestData.LayoutOf(SessionTestData.TranscriptOf(3, tokensEach: 40), tierOne);

        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, CompactionLevel.Low, verbatimTurns: 2, Roomy, CancellationToken.None);

        // Rule 2 made a tier-1 slot; tier 1 was full, so rule 3 consolidated its four slots into
        // tier 2 and the arriving slot took the emptied tier 1.
        Assert.Equal(1, outcome.Layout.Tiers[0].Count);
        Assert.Equal(1, outcome.Layout.Tiers[1].Count);
        Assert.Equal([(1, ConsolidationPrompt.LowInstruction), (2, ConsolidationPrompt.LowInstruction)], summarizer.Shape);
    }

    /// <summary>
    ///     Proves rule 4: the coarsest tier is a ring — a cascade that would overflow it drops its
    ///     oldest slot rather than growing it.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_CoarsestTier_IsARing()
    {
        var full = SessionTestData.TierOf(new Slot("1"), new Slot("2"), new Slot("3"), new Slot("4"));
        var layout = SessionTestData.LayoutOf(
            SessionTestData.TranscriptOf(3, tokensEach: 40), full, full, full);

        var outcome = await RotationEngine.RotateAsync(
            layout, FakeSummarizer.Fixed(1), CompactionLevel.Low, verbatimTurns: 2, Roomy, CancellationToken.None);

        // The cascade reached the coarsest tier; the ring holds it at its complement.
        Assert.Equal(ContextLayout.SlotsPerTier, outcome.Layout.Tiers[ContextLayout.TierCount - 1].Count);
    }

    /// <summary>
    ///     Proves a turn is consolidated once per tier — three times in its whole life — by driving
    ///     enough rotations for a slot to cascade from tier one to tier three.
    /// </summary>
    [Fact]
    public async Task RotationEngine_ManyRotations_ConsolidatesOncePerTier()
    {
        var summarizer = FakeSummarizer.Fixed(1);
        var layout = SessionTestData.LayoutOf(SessionTestData.TranscriptOf(2, tokensEach: 20));

        for (var rotation = 0; rotation < 21; rotation++)
        {
            layout = layout.WithTail(layout.Tail.AppendTurn(SessionTestData.TurnEntries(20, $"r{rotation}")));
            var outcome = await RotationEngine.RotateAsync(
                layout, summarizer, CompactionLevel.Low, verbatimTurns: 2, Roomy, CancellationToken.None);
            layout = outcome.Layout;
        }

        // A tier-3 consolidation request proves a slot reached the coarsest tier, which happens only
        // after it was consolidated at tier one, then tier two, then tier three.
        Assert.Contains(summarizer.Requests, request => request.TierIndex == 1);
        Assert.Contains(summarizer.Requests, request => request.TierIndex == 2);
        Assert.Contains(summarizer.Requests, request => request.TierIndex == 3);
    }

    /// <summary>
    ///     Proves oversized material is chunked into several summarizer calls rather than handed over
    ///     whole.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_OversizedMaterial_IsChunked()
    {
        var summarizer = FakeSummarizer.Fixed(2);
        var perEntry = ContextLayout.MaxSummarizerInputTokens; // each entry alone approaches the bound
        var bigTurn = SessionTranscript.Empty
            .AppendTurn([
                SessionTestData.UserOfTokens(perEntry, "u"),
                SessionTestData.AssistantOfTokens(perEntry, "a"),
            ])
            .AppendTurn(SessionTestData.TurnEntries(20, "recent"));

        var outcome = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(bigTurn), summarizer, CompactionLevel.Low, verbatimTurns: 1, Roomy, CancellationToken.None);

        // Chunking produces more than the single call a consolidation without chunking would.
        Assert.True(summarizer.CallCount > 1);
        Assert.Equal(1, outcome.Layout.Tiers[0].Count);
    }

    /// <summary>
    ///     Proves a blank summarizer answer is normalized to empty: no slot is created rather than a
    ///     whitespace slot that costs framing to say nothing.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_BlankAnswer_ProducesNoSlot()
    {
        var outcome = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.TranscriptOf(4, tokensEach: 40)),
            FakeSummarizer.Blank(), CompactionLevel.Low, verbatimTurns: 2, Roomy, CancellationToken.None);

        Assert.True(outcome.Layout.Tiers[0].IsEmpty);
    }

    /// <summary>
    ///     Proves a seed that does not fit escalates the compaction level until it does, keeping more
    ///     history than dropping would.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_SeedThatDoesNotFit_Escalates()
    {
        var layout = SessionTestData.LayoutOf(SessionTestData.TranscriptOf(8, tokensEach: 40));

        var outcome = await RotationEngine.RotateAsync(
            layout, FakeSummarizer.Fixed(1), CompactionLevel.Low, verbatimTurns: 8, rotationThresholdTokens: 150, CancellationToken.None);

        Assert.NotEqual(CompactionLevel.Low, outcome.Level);
        Assert.False(outcome.MaterialDropped);
        Assert.True(outcome.Layout.EstimatedConversationTokens <= 150);
    }

    /// <summary>
    ///     Proves the drop-until-it-fits rule terminates and reports material dropped when even the
    ///     tersest structure does not fit, exercised with a summarizer that expands its input.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_ExpandingSummarizer_DropsAndTerminates()
    {
        var layout = SessionTestData.LayoutOf(SessionTestData.TranscriptOf(6, tokensEach: 40));

        var outcome = await RotationEngine.RotateAsync(
            layout, FakeSummarizer.Expanding(), CompactionLevel.Low, verbatimTurns: 4, rotationThresholdTokens: 60, CancellationToken.None);

        Assert.True(outcome.MaterialDropped);
        Assert.Equal(CompactionLevel.High, outcome.Level);
        // Bottomed out at a context it could not reduce further: the newest turn stands alone or it
        // fits.
        Assert.True(outcome.Layout.Tail.TurnCount >= 1);
    }

    /// <summary>
    ///     Proves the drop loop terminates when a single verbatim turn is larger than the whole
    ///     allowance: it drops the older turns and bottoms out at the newest turn alone.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_SingleOversizedTurn_BottomsOutAtNewestTurn()
    {
        var tail = SessionTranscript.Empty
            .AppendTurn(SessionTestData.TurnEntries(40, "old"))
            .AppendTurn(SessionTestData.TurnEntries(40, "mid"))
            .AppendTurn([SessionTestData.UserOfTokens(500, "huge")]);

        var outcome = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(tail), FakeSummarizer.Fixed(1),
            CompactionLevel.High, verbatimTurns: 20, rotationThresholdTokens: 60, CancellationToken.None);

        Assert.True(outcome.MaterialDropped);
        Assert.Equal(1, outcome.Layout.Tail.TurnCount);
    }

    /// <summary>
    ///     Proves a null summarizer answer is refused rather than stored.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_NullAnswer_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.TranscriptOf(4, tokensEach: 40)),
            FakeSummarizer.Null(), CompactionLevel.Low, verbatimTurns: 2, Roomy, CancellationToken.None));
    }

    /// <summary>
    ///     Proves cancellation is honored by the engine rather than delegated to the summarizer, so a
    ///     cascade does not run to completion after the caller has given up.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_Canceled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var summarizer = new InattentiveSummarizer(_ => "record");

        await Assert.ThrowsAsync<OperationCanceledException>(() => RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.TranscriptOf(4, tokensEach: 40)),
            summarizer, CompactionLevel.Low, verbatimTurns: 2, Roomy, cancellation.Token));
    }

    /// <summary>
    ///     Proves the level helpers escalate to and relax from the extremes without running past
    ///     them.
    /// </summary>
    [Fact]
    public void RotationEngine_LevelHelpers_SaturateAtTheExtremes()
    {
        Assert.Equal(CompactionLevel.Medium, RotationEngine.Escalate(CompactionLevel.Low));
        Assert.Equal(CompactionLevel.High, RotationEngine.Escalate(CompactionLevel.Medium));
        Assert.Equal(CompactionLevel.High, RotationEngine.Escalate(CompactionLevel.High));
        Assert.Equal(CompactionLevel.Medium, RotationEngine.Relax(CompactionLevel.High));
        Assert.Equal(CompactionLevel.Low, RotationEngine.Relax(CompactionLevel.Medium));
        Assert.Equal(CompactionLevel.Low, RotationEngine.Relax(CompactionLevel.Low));
    }

    /// <summary>
    ///     Proves the tail length shortens with the level: full, half, then a quarter.
    /// </summary>
    [Fact]
    public void RotationEngine_VerbatimTurnsFor_ShortensWithLevel()
    {
        Assert.Equal(20, RotationEngine.VerbatimTurnsFor(CompactionLevel.Low, 20));
        Assert.Equal(10, RotationEngine.VerbatimTurnsFor(CompactionLevel.Medium, 20));
        Assert.Equal(5, RotationEngine.VerbatimTurnsFor(CompactionLevel.High, 20));
        Assert.Equal(1, RotationEngine.VerbatimTurnsFor(CompactionLevel.High, 2));
    }
}
