namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for <see cref="RotationEngine"/>: rules 2 to 4, the material one consolidation is
///     handed, and the level helpers the session steers with.
/// </summary>
public class RotationEngineTests
{
    /// <summary>
    ///     Proves rule 2: everything older than the level-adjusted tail is consolidated into one
    ///     slot appended to tier one, and the newest turns stay verbatim.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_ConsolidatesOlderIntoOneTierOneSlot()
    {
        // Arrange: five turns, of which two are to stay verbatim
        var summarizer = FakeSummarizer.Fixed(2);
        var layout = SessionTestData.LayoutOf(SessionTestData.TranscriptOf(5, tokensEach: 40));

        // Act: rotate at the relaxed level
        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, CompactionLevel.Low, verbatimTurns: 2, CancellationToken.None);

        // Assert: one slot in tier one, the newest two turns still verbatim, one consolidation asked
        // for at tier one with the relaxed clause, and nothing discarded
        Assert.Equal(1, outcome.Layout.Tiers[0].Count);
        Assert.Equal(2, outcome.Layout.Tail.TurnCount);
        Assert.Equal(1, outcome.ConsolidationCount);
        Assert.Equal([(1, ConsolidationPrompt.LowInstruction)], summarizer.Shape);
        Assert.False(outcome.MaterialDropped);
    }

    /// <summary>
    ///     Proves the material one consolidation receives is exactly the turns older than the tail,
    ///     oldest first, rendered as labeled lines — and that the retained turns are not in it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A consolidation is one stateless call carrying text, so this material is the whole of
    ///     what the summarizer learns. Two failures it would hide are worth separating: material
    ///     that included the retained tail would consolidate history the session is still holding
    ///     verbatim and pay for it twice, and material in the wrong order would present the record
    ///     of a conversation that never happened that way.
    ///     </para>
    ///     <para>
    ///     A turn is also the indivisible piece: each turn is rendered whole, which is what keeps a
    ///     tool result from reaching a summarizer without the call it answers.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task RotationEngine_Rotate_HandsTheOlderTurnsOverAsRenderedMaterial()
    {
        // Arrange: four tagged turns, of which one is to stay verbatim
        var summarizer = FakeSummarizer.Fixed(2);
        var layout = SessionTestData.LayoutOf(SessionTestData.TranscriptOf(4, tokensEach: 40));

        // Act: rotate keeping a single turn
        await RotationEngine.RotateAsync(
            layout, summarizer, CompactionLevel.Low, verbatimTurns: 1, CancellationToken.None);

        // Assert: one call, carrying the three older turns as labeled lines
        var material = Assert.Single(summarizer.Requests).Material;
        Assert.Contains("USER: u0", material, StringComparison.Ordinal);
        Assert.Contains("ASSISTANT: a0", material, StringComparison.Ordinal);
        Assert.Contains("USER: u2", material, StringComparison.Ordinal);

        // Assert: oldest first, and the retained newest turn was not consolidated
        Assert.True(
            material.IndexOf("u0", StringComparison.Ordinal) < material.IndexOf("u1", StringComparison.Ordinal),
            "The material must carry the older turns oldest first.");
        Assert.True(
            material.IndexOf("u1", StringComparison.Ordinal) < material.IndexOf("u2", StringComparison.Ordinal),
            "The material must carry the older turns oldest first.");
        Assert.DoesNotContain("u3", material, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves rule 3: a full coarse tier consolidates its slots as peers into one slot of the
    ///     next tier and is cleared, with the arriving slot placed in the now-empty tier.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_FullTier_ConsolidatesAsPeersIntoNextTier()
    {
        // Arrange: a full tier one, so the slot rule 2 produces has nowhere to go
        var summarizer = FakeSummarizer.Fixed(2);
        var tierOne = SessionTestData.TierOf(
            new Slot("a"), new Slot("b"), new Slot("c"), new Slot("d"));
        var layout = SessionTestData.LayoutOf(SessionTestData.TranscriptOf(3, tokensEach: 40), tierOne);

        // Act: rotate, which cascades
        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, CompactionLevel.Low, verbatimTurns: 2, CancellationToken.None);

        // Assert: rule 2 made a tier-one slot; tier one was full, so rule 3 consolidated its four
        // slots into tier two and the arriving slot took the emptied tier one
        Assert.Equal(1, outcome.Layout.Tiers[0].Count);
        Assert.Equal(1, outcome.Layout.Tiers[1].Count);
        Assert.Equal([(1, ConsolidationPrompt.LowInstruction), (2, ConsolidationPrompt.LowInstruction)], summarizer.Shape);

        // Assert: the tier-two request carried the four standing slots as peers, and nothing was lost
        Assert.Contains("a", summarizer.Requests[1].Material, StringComparison.Ordinal);
        Assert.Contains("d", summarizer.Requests[1].Material, StringComparison.Ordinal);
        Assert.False(outcome.MaterialDropped);
    }

    /// <summary>
    ///     Proves rule 4: the coarsest tier is a ring — a cascade that would overflow it drops its
    ///     oldest slot rather than growing it, and says so.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_CoarsestTier_IsARing()
    {
        // Arrange: every tier full, so a rotation cascades all the way to the coarsest
        var full = SessionTestData.TierOf(new Slot("1"), new Slot("2"), new Slot("3"), new Slot("4"));
        var layout = SessionTestData.LayoutOf(
            SessionTestData.TranscriptOf(3, tokensEach: 40), full, full, full);

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(
            layout, FakeSummarizer.Fixed(1), CompactionLevel.Low, verbatimTurns: 2, CancellationToken.None);

        // Assert: the ring holds the coarsest tier at its complement, having displaced its oldest
        // slot for the arriving one
        var coarsest = outcome.Layout.Tiers[ContextLayout.TierCount - 1];
        Assert.Equal(ContextLayout.SlotsPerTier, coarsest.Count);
        Assert.Equal("2", coarsest.Slots[0].Content);
        Assert.Equal(new string('s', SessionTestData.CharactersPerToken), coarsest.Slots[^1].Content);

        // Assert: the displacement is reported rather than silent — it is history discarded outright
        Assert.True(outcome.MaterialDropped, "A slot binned by the ring is material dropped.");
    }

    /// <summary>
    ///     Proves a turn is consolidated once per tier — three times in its whole life — by driving
    ///     enough rotations for a slot to cascade from tier one to tier three.
    /// </summary>
    /// <remarks>
    ///     The discriminating observable is how many consolidations each tier receives, not whether
    ///     it receives any. The design this replaced re-consolidated each tier's standing record on
    ///     every rotation — a flat ratchet, and the reason a flat scheme's recall collapses — and
    ///     that implementation would produce requests at all three tiers just as this one does.
    ///     What it could not produce is four of them at tier two and one at tier three: batch-then-
    ///     clear consolidates a tier only when it is full, so with four slots to a tier, twenty-one
    ///     rotations give tier one twenty-one, tier two four, and tier three one.
    /// </remarks>
    [Fact]
    public async Task RotationEngine_ManyRotations_ConsolidatesOncePerTier()
    {
        // Arrange: a small layout and a summarizer that always answers
        var summarizer = FakeSummarizer.Fixed(1);
        var layout = SessionTestData.LayoutOf(SessionTestData.TranscriptOf(2, tokensEach: 20));

        // Act: twenty-one rotations, each preceded by one new turn
        for (var rotation = 0; rotation < 21; rotation++)
        {
            layout = layout.WithTail(layout.Tail.AppendTurn(SessionTestData.TurnEntries(20, $"r{rotation}")));
            var outcome = await RotationEngine.RotateAsync(
                layout, summarizer, CompactionLevel.Low, verbatimTurns: 2, CancellationToken.None);
            layout = outcome.Layout;
        }

        var tierOne = summarizer.Requests.Count(request => request.TierIndex == 1);
        var tierTwo = summarizer.Requests.Count(request => request.TierIndex == 2);
        var tierThree = summarizer.Requests.Count(request => request.TierIndex == 3);

        // Assert: one consolidation into tier one per rotation - that is rule 2, and it is the only
        // tier that sees every rotation
        Assert.Equal(21, tierOne);

        // Assert: tier two is consolidated only when tier one fills, which is once every SlotsPerTier
        // rotations, and tier three only when tier two fills. A ratchet would put both near 21.
        Assert.Equal(21 / ContextLayout.SlotsPerTier, tierTwo);
        Assert.Equal(21 / (ContextLayout.SlotsPerTier * ContextLayout.SlotsPerTier), tierThree);
    }

    /// <summary>
    ///     Proves a rotation that consolidates nothing is not reported as a success, because a
    ///     rotation which changes nothing leaves the session using a provider it has been told is
    ///     full.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Rule 2 triggers on the provider's occupancy, measured in tokens, but the verbatim tail is
    ///     held by a count of turns. A provider counting well above the rate a rotation was sized
    ///     for reaches its threshold while the tail is still shorter than the configured maximum —
    ///     so there is nothing older to consolidate and the layout produced is identical to the one
    ///     handed in. Measured end to end at five times divergence before this was fixed, a session
    ///     rode to one hundred and forty percent of the provider's window across nineteen turns
    ///     without rotating once, which is where the provider's own compactor fires and truncates
    ///     history.
    ///     </para>
    ///     <para>
    ///     The tail here holds four turns against a maximum of twelve, so keeping the level's figure
    ///     blindly would age nothing out. Capping the tail at one turn fewer than it holds moves the
    ///     oldest turn instead, which is the least the rotation can do and still have done something.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task RotationEngine_Rotate_TailShorterThanItsMaximum_StillMakesProgress()
    {
        // Arrange: four turns against a configured maximum of twelve
        var summarizer = FakeSummarizer.Fixed(1);

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.TranscriptOf(4, tokensEach: 20)),
            summarizer, CompactionLevel.Low, verbatimTurns: 12, CancellationToken.None);

        // Assert: the oldest turn aged out, so the rotation actually moved something
        Assert.True(
            outcome.ConsolidationCount > 0,
            "A rotation that consolidated nothing leaves the session on a provider it was told is full.");
        Assert.False(outcome.Layout.Tiers[0].IsEmpty);
        Assert.Equal(3, outcome.Layout.Tail.TurnCount);
    }

    /// <summary>
    ///     Proves a rotation of a layout holding nothing asks for no consolidation at all.
    /// </summary>
    /// <remarks>
    ///     There is nothing older than the newest turn when there is no turn, and a summarizer call
    ///     for empty material would be paid for to record nothing.
    /// </remarks>
    [Fact]
    public async Task RotationEngine_Rotate_EmptyTail_ConsolidatesNothing()
    {
        // Arrange: the layout a session starts from
        var summarizer = FakeSummarizer.Fixed(1);

        // Act: rotate it
        var outcome = await RotationEngine.RotateAsync(
            ContextLayout.Create(), summarizer, CompactionLevel.Low, verbatimTurns: 4, CancellationToken.None);

        // Assert: no call was made, and nothing was invented to seed a session with
        Assert.Equal(0, summarizer.CallCount);
        Assert.Equal(0, outcome.ConsolidationCount);
        Assert.False(outcome.MaterialDropped);
        Assert.Empty(outcome.Layout.BuildSeed());
    }

    /// <summary>
    ///     Proves a blank summarizer answer is normalized to empty: no slot is created rather than a
    ///     whitespace slot that costs framing to say nothing, and the material that slot would have
    ///     held stays verbatim rather than being discarded.
    /// </summary>
    /// <remarks>
    ///     Producing no slot is only half the behavior. Retaining just the tail alongside it would
    ///     discard every older turn while recording nothing in their place - a silent loss reported
    ///     as an ordinary success, and committed to the provider as soon as the replacement session
    ///     is seeded from the shortened layout. Asserting the tier is empty does not catch that; the
    ///     turn count does. Nor is it a loss: a consolidation that could not be made leaves the
    ///     material where it is, which the outcome must distinguish from history being binned.
    /// </remarks>
    [Fact]
    public async Task RotationEngine_Rotate_BlankAnswer_ProducesNoSlotAndKeepsTheMaterial()
    {
        // Arrange: four turns and a summarizer with nothing to say
        var transcript = SessionTestData.TranscriptOf(4, tokensEach: 40);

        // Act: rotate
        var outcome = await RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(transcript),
            FakeSummarizer.Blank(), CompactionLevel.Low, verbatimTurns: 2, CancellationToken.None);

        // Assert: no slot was written, every turn is still held verbatim, and nothing is reported
        // as dropped
        Assert.True(outcome.Layout.Tiers[0].IsEmpty);
        Assert.Equal(transcript.TurnCount, outcome.Layout.Tail.TurnCount);
        Assert.False(outcome.MaterialDropped);
    }

    /// <summary>
    ///     Proves a full tier whose consolidation comes back blank is not cleared: the arriving slot
    ///     displaces its oldest instead, and the loss of that one slot is reported.
    /// </summary>
    /// <remarks>
    ///     Clearing the tier on the strength of a record that was never written would discard its
    ///     whole complement — up to a tier's worth of history — and append nothing in its place. The
    ///     bounded move is the same one the coarsest tier's ring makes: one slot is lost rather than
    ///     all of them, and it is reported rather than silent.
    /// </remarks>
    [Fact]
    public async Task RotationEngine_Rotate_FullTierBlankAnswer_DisplacesOneSlotAndReportsIt()
    {
        // Arrange: a summarizer that answers rule 2 but has nothing to say when a tier is folded up
        var summarizer = new FakeSummarizer(request => request.TierIndex == 1 ? "record" : "   ");
        var tierOne = SessionTestData.TierOf(
            new Slot("a"), new Slot("b"), new Slot("c"), new Slot("d"));
        var layout = SessionTestData.LayoutOf(SessionTestData.TranscriptOf(3, tokensEach: 40), tierOne);

        // Act: rotate, which fills tier one and finds it cannot be folded into tier two
        var outcome = await RotationEngine.RotateAsync(
            layout, summarizer, CompactionLevel.Low, verbatimTurns: 2, CancellationToken.None);

        // Assert: the tier holds its complement still - its oldest displaced, the arriving slot last
        var survivors = outcome.Layout.Tiers[0];
        Assert.Equal(ContextLayout.SlotsPerTier, survivors.Count);
        Assert.Equal("b", survivors.Slots[0].Content);
        Assert.Equal("record", survivors.Slots[^1].Content);

        // Assert: the tier was not cleared into a record that was never written, and the one slot
        // that was binned is reported
        Assert.True(outcome.Layout.Tiers[1].IsEmpty);
        Assert.True(outcome.MaterialDropped, "A slot displaced because a tier could not be folded up is a loss.");
    }

    /// <summary>
    ///     Proves a null summarizer answer is refused rather than stored.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_NullAnswer_Throws()
    {
        // Act / Assert: an implementation with nothing to say must return an empty string, not null
        await Assert.ThrowsAsync<InvalidOperationException>(() => RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.TranscriptOf(4, tokensEach: 40)),
            FakeSummarizer.Null(), CompactionLevel.Low, verbatimTurns: 2, CancellationToken.None));
    }

    /// <summary>
    ///     Proves a missing collaborator or an undefined level is refused before any consolidation is
    ///     paid for.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_InvalidArguments_Throw()
    {
        // Arrange: an otherwise valid rotation
        var summarizer = FakeSummarizer.Fixed(1);
        var layout = SessionTestData.LayoutOf(SessionTestData.TranscriptOf(4, tokensEach: 40));

        // Act / Assert: each malformed call is refused, and nothing was asked of the summarizer
        await Assert.ThrowsAsync<ArgumentNullException>(() => RotationEngine.RotateAsync(
            null!, summarizer, CompactionLevel.Low, verbatimTurns: 2, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => RotationEngine.RotateAsync(
            layout, null!, CompactionLevel.Low, verbatimTurns: 2, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => RotationEngine.RotateAsync(
            layout, summarizer, (CompactionLevel)99, verbatimTurns: 2, CancellationToken.None));
        Assert.Equal(0, summarizer.CallCount);
    }

    /// <summary>
    ///     Proves cancellation is honored by the engine rather than delegated to the summarizer, so a
    ///     cascade does not run to completion after the caller has given up.
    /// </summary>
    [Fact]
    public async Task RotationEngine_Rotate_Canceled_Throws()
    {
        // Arrange: a canceled token and a summarizer that never looks at one
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var summarizer = new InattentiveSummarizer(_ => "record");

        // Act / Assert: the engine refuses the rotation itself
        await Assert.ThrowsAsync<OperationCanceledException>(() => RotationEngine.RotateAsync(
            SessionTestData.LayoutOf(SessionTestData.TranscriptOf(4, tokensEach: 40)),
            summarizer, CompactionLevel.Low, verbatimTurns: 2, cancellation.Token));
    }

    /// <summary>
    ///     Proves the level helpers escalate to and relax from the extremes without running past
    ///     them.
    /// </summary>
    [Fact]
    public void RotationEngine_LevelHelpers_SaturateAtTheExtremes()
    {
        // Act / Assert: each step moves one level, and the extremes hold
        Assert.Equal(CompactionLevel.Medium, RotationEngine.Escalate(CompactionLevel.Low));
        Assert.Equal(CompactionLevel.High, RotationEngine.Escalate(CompactionLevel.Medium));
        Assert.Equal(CompactionLevel.High, RotationEngine.Escalate(CompactionLevel.High));
        Assert.Equal(CompactionLevel.Medium, RotationEngine.Relax(CompactionLevel.High));
        Assert.Equal(CompactionLevel.Low, RotationEngine.Relax(CompactionLevel.Medium));
        Assert.Equal(CompactionLevel.Low, RotationEngine.Relax(CompactionLevel.Low));
    }

    /// <summary>
    ///     Proves the tail length shortens with the level: full, half, then a quarter, and never
    ///     below one turn.
    /// </summary>
    [Fact]
    public void RotationEngine_VerbatimTurnsFor_ShortensWithLevel()
    {
        // Act / Assert: the level buys room by keeping less of the tail verbatim
        Assert.Equal(20, RotationEngine.VerbatimTurnsFor(CompactionLevel.Low, 20));
        Assert.Equal(10, RotationEngine.VerbatimTurnsFor(CompactionLevel.Medium, 20));
        Assert.Equal(5, RotationEngine.VerbatimTurnsFor(CompactionLevel.High, 20));
        Assert.Equal(1, RotationEngine.VerbatimTurnsFor(CompactionLevel.High, 2));
    }
}
