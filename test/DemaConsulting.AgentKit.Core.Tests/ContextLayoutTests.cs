namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the round-robin structure: <see cref="Slot"/>, <see cref="Tier"/> and
///     <see cref="ContextLayout"/>.
/// </summary>
public class ContextLayoutTests
{
    /// <summary>
    ///     Proves the structural counts are the internal constants the design is sold on.
    /// </summary>
    [Fact]
    public void ContextLayout_Constants_AreTheRoundRobinShape()
    {
        Assert.Equal(4, ContextLayout.SlotsPerTier);
        Assert.Equal(3, ContextLayout.TierCount);
        Assert.Equal(0.70, ContextLayout.RotationThreshold);
    }

    /// <summary>
    ///     Proves a slot must hold a non-blank record; a blank one would spend framing to say
    ///     nothing.
    /// </summary>
    [Fact]
    public void Slot_Construct_Blank_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Slot("   "));
        Assert.Equal("record", new Slot("record").Content);
    }

    /// <summary>
    ///     Proves a tier is a ring: it reports full at its complement, hands back its oldest slot,
    ///     and drops its oldest.
    /// </summary>
    [Fact]
    public void Tier_RingOperations_Hold()
    {
        var tier = Tier.Empty;
        for (var index = 0; index < ContextLayout.SlotsPerTier; index++)
        {
            Assert.False(tier.IsFull);
            tier = tier.Append(new Slot($"s{index}"));
        }

        Assert.True(tier.IsFull);
        Assert.Equal("s0", tier.Oldest.Content);
        Assert.Equal("s1", tier.DropOldest().Oldest.Content);
    }

    /// <summary>
    ///     Proves a fresh layout is empty: no slots and no verbatim turns.
    /// </summary>
    [Fact]
    public void ContextLayout_Create_IsEmpty()
    {
        var layout = ContextLayout.Create(systemTokens: 10, toolDeclarationTokens: 20);

        Assert.Equal(10, layout.SystemTokens);
        Assert.Equal(20, layout.ToolDeclarationTokens);
        Assert.Equal(0, layout.Tail.TurnCount);
        Assert.All(layout.Tiers, tier => Assert.True(tier.IsEmpty));
        Assert.Empty(layout.BuildSeed());
    }

    /// <summary>
    ///     Proves the seed is built coarsest first: the coarsest tier's slots, then each finer tier,
    ///     then the verbatim tail in its own order.
    /// </summary>
    [Fact]
    public void ContextLayout_BuildSeed_IsCoarsestFirst()
    {
        var tail = SessionTranscript.Empty.AppendTurn([TranscriptEntry.User("newest")]);
        var layout = SessionTestData.LayoutOf(
            tail,
            SessionTestData.TierOf(new Slot("tier1-slot")),
            SessionTestData.TierOf(new Slot("tier2-slot")),
            SessionTestData.TierOf(new Slot("tier3-slot")));

        var seed = layout.BuildSeed();

        Assert.Equal(4, seed.Count);
        Assert.Contains("tier3-slot", seed[0].Text, StringComparison.Ordinal);
        Assert.Contains("tier2-slot", seed[1].Text, StringComparison.Ordinal);
        Assert.Contains("tier1-slot", seed[2].Text, StringComparison.Ordinal);
        Assert.Equal("newest", seed[3].Text);
    }

    /// <summary>
    ///     Proves each seeded slot is labeled with its detail level, so a model can tell which record
    ///     supersedes which.
    /// </summary>
    [Fact]
    public void ContextLayout_BuildSeed_LabelsSlotsByTier()
    {
        var layout = SessionTestData.LayoutOf(
            SessionTranscript.Empty,
            SessionTestData.TierOf(new Slot("fine")),
            Tier.Empty,
            SessionTestData.TierOf(new Slot("coarse")));

        var seed = layout.BuildSeed();

        Assert.Contains("detail level 3", seed[0].Text, StringComparison.Ordinal);
        Assert.Contains("detail level 1", seed[1].Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the estimated conversation size counts the seeded slots, their framing and the
    ///     tail, and the total adds the fixed overhead.
    /// </summary>
    [Fact]
    public void ContextLayout_EstimatedTokens_CountSeedAndOverhead()
    {
        var tail = SessionTestData.TranscriptOf(1, tokensEach: 40);
        var layout = SessionTestData.LayoutOf(tail, SessionTestData.TierOf(SessionTestData.SlotOfTokens(20, "s")));

        Assert.True(layout.EstimatedConversationTokens > 0);
        Assert.Equal(layout.EstimatedConversationTokens, layout.TotalEstimatedTokens);
    }

    /// <summary>
    ///     Proves the wrong number of tiers, or a null tier, is refused when replacing the tiers.
    /// </summary>
    [Fact]
    public void ContextLayout_WithTiers_Malformed_Throws()
    {
        var layout = ContextLayout.Create(0, 0);

        Assert.Throws<ArgumentException>(() => layout.WithTiers(SessionTranscript.Empty, [Tier.Empty]));
        Assert.Throws<ArgumentException>(() =>
            layout.WithTiers(SessionTranscript.Empty, [Tier.Empty, null!, Tier.Empty]));
    }
}
