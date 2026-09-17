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
        // Act / Assert: the counts the design is sold on are fixed, not configurable
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
        // Act / Assert: a blank record is refused, and a real one is held as written
        Assert.Throws<ArgumentException>(() => new Slot("   "));
        Assert.Equal("record", new Slot("record").Content);
    }

    /// <summary>
    ///     Proves a tier holds its slots oldest first and hands back a new tier for every change,
    ///     so a rotation can compare a before and after.
    /// </summary>
    [Fact]
    public void Tier_AppendAndDropOldest_KeepOldestFirstAndLeaveTheOriginal()
    {
        // Arrange: a tier filled to its complement, oldest first
        var tier = Tier.Empty;
        for (var index = 0; index < ContextLayout.SlotsPerTier; index++)
        {
            Assert.Equal(index, tier.Count);
            tier = tier.Append(new Slot($"s{index}"));
        }

        // Act: drop the oldest slot
        var dropped = tier.DropOldest();

        // Assert: the order is oldest first, the drop removes that end, and the tier it was
        // taken from is unchanged
        Assert.Equal(ContextLayout.SlotsPerTier, tier.Count);
        Assert.Equal("s0", tier.Slots[0].Content);
        Assert.Equal($"s{ContextLayout.SlotsPerTier - 1}", tier.Slots[^1].Content);
        Assert.Equal(ContextLayout.SlotsPerTier - 1, dropped.Count);
        Assert.Equal("s1", dropped.Slots[0].Content);
        Assert.False(tier.IsEmpty);
    }

    /// <summary>
    ///     Proves dropping from an empty tier is refused rather than answered with another empty
    ///     tier, so a cascade that reached for a slot that was never there says so.
    /// </summary>
    [Fact]
    public void Tier_DropOldest_Empty_Throws()
    {
        // Act / Assert: there is no oldest slot to hand back
        Assert.Throws<InvalidOperationException>(() => Tier.Empty.DropOldest());
    }

    /// <summary>
    ///     Proves a null slot is refused rather than appended, since a tier of holes would seed a
    ///     fresh session from nothing.
    /// </summary>
    [Fact]
    public void Tier_Append_Null_Throws()
    {
        // Act / Assert: the hole is refused where it was written
        Assert.Throws<ArgumentNullException>(() => Tier.Empty.Append(null!));
    }

    /// <summary>
    ///     Proves a fresh layout is empty: no slots and no verbatim turns.
    /// </summary>
    [Fact]
    public void ContextLayout_Create_IsEmpty()
    {
        // Arrange / Act: the layout a session starts from
        var layout = ContextLayout.Create();

        // Assert: nothing is held, and there is nothing to seed a provider session with
        Assert.Equal(ContextLayout.TierCount, layout.Tiers.Count);
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
        // Arrange: one slot in each tier, and one verbatim turn
        var tail = SessionTranscript.Empty.AppendTurn([TranscriptEntry.User("newest")]);
        var layout = SessionTestData.LayoutOf(
            tail,
            SessionTestData.TierOf(new Slot("tier1-slot")),
            SessionTestData.TierOf(new Slot("tier2-slot")),
            SessionTestData.TierOf(new Slot("tier3-slot")));

        // Act: build the history a fresh provider session is seeded with
        var seed = layout.BuildSeed();

        // Assert: coarsest first, then each finer tier, then the tail in its own order
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
        // Arrange: a fine slot and a coarse one, with the middle tier empty
        var layout = SessionTestData.LayoutOf(
            SessionTranscript.Empty,
            SessionTestData.TierOf(new Slot("fine")),
            Tier.Empty,
            SessionTestData.TierOf(new Slot("coarse")));

        // Act: build the seed
        var seed = layout.BuildSeed();

        // Assert: each record names the detail level it belongs to
        Assert.Contains("detail level 3", seed[0].Text, StringComparison.Ordinal);
        Assert.Contains("detail level 1", seed[1].Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the verbatim tail can be replaced without disturbing the tiers, and that the
    ///     layout it was taken from is unchanged.
    /// </summary>
    /// <remarks>
    ///     This is what makes the rotation engine a pure function a test can compare a before and
    ///     after of: a mutator that edited in place would leave every caller holding the same
    ///     object.
    /// </remarks>
    [Fact]
    public void ContextLayout_WithTail_ReplacesTheTailAndLeavesTheOriginal()
    {
        // Arrange: a layout carrying one slot and one turn
        var original = SessionTestData.LayoutOf(
            SessionTranscript.Empty.AppendTurn([TranscriptEntry.User("first")]),
            SessionTestData.TierOf(new Slot("record")));

        // Act: replace the tail
        var replaced = original.WithTail(
            original.Tail.AppendTurn([TranscriptEntry.User("second")]));

        // Assert: the new layout carries the new tail and the same tiers; the old one is untouched
        Assert.Equal(2, replaced.Tail.TurnCount);
        Assert.Equal(1, original.Tail.TurnCount);
        Assert.Equal("record", replaced.Tiers[0].Slots[0].Content);
    }

    /// <summary>
    ///     Proves the wrong number of tiers, or a null tier, is refused when replacing the tiers.
    /// </summary>
    [Fact]
    public void ContextLayout_WithTiers_Malformed_Throws()
    {
        // Act / Assert: a layout of the wrong shape is refused rather than built
        Assert.Throws<ArgumentNullException>(() => ContextLayout.WithTiers(null!, [Tier.Empty, Tier.Empty, Tier.Empty]));
        Assert.Throws<ArgumentNullException>(() => ContextLayout.WithTiers(SessionTranscript.Empty, null!));
        Assert.Throws<ArgumentException>(() => ContextLayout.WithTiers(SessionTranscript.Empty, [Tier.Empty]));
        Assert.Throws<ArgumentException>(() =>
            ContextLayout.WithTiers(SessionTranscript.Empty, [Tier.Empty, null!, Tier.Empty]));
    }
}
