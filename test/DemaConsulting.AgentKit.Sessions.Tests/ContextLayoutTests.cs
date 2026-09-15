namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="ContextTier"/> and <see cref="ContextLayout"/>: the accounting that
///     makes the arrangement bounded by construction, and the most-stable-first order a fresh
///     provider session is seeded in.
/// </summary>
public class ContextLayoutTests
{
    /// <summary>
    ///     Proves a new layout allocates one empty coarse tier per non-verbatim budget, so no code
    ///     path has to grow the hierarchy later.
    /// </summary>
    [Fact]
    public void ContextLayout_Create_AllocatesOneCoarseTierPerBudgetAboveTierZero()
    {
        // Arrange / Act: a layout for the small four-tier policy
        var layout = ContextLayout.Create(SessionTestData.SmallPolicy, 100, 200);

        // Assert: three coarse tiers, numbered from one, carrying the policy's budgets
        Assert.Equal(3, layout.CoarseTiers.Count);
        Assert.Equal([1, 2, 3], layout.CoarseTiers.Select(tier => tier.Index));
        Assert.Equal([60, 40, 30], layout.CoarseTiers.Select(tier => tier.BudgetTokens));
        Assert.All(layout.CoarseTiers, tier => Assert.True(tier.IsEmpty));
    }

    /// <summary>
    ///     Proves the construction bound is the fixed overhead plus every tier budget — the single
    ///     claim the whole arrangement exists to make good on.
    /// </summary>
    [Fact]
    public void ContextLayout_MaximumBoundTokens_IsOverheadPlusEveryTierBudget()
    {
        // Arrange: a layout with a measured system prompt and tool declarations
        var layout = ContextLayout.Create(SessionTestData.SmallPolicy, 100, 200);

        // Act / Assert: the bound follows from the configuration alone
        Assert.Equal(100 + 200 + SessionTestData.SmallPolicy.TotalTierBudgetTokens, layout.MaximumBoundTokens);
        Assert.True(layout.IsWithinBound);
    }

    /// <summary>
    ///     Proves conversation tokens exclude the fixed overhead, because the rotation threshold is a
    ///     fraction of the effective window rather than of the whole one.
    /// </summary>
    [Fact]
    public void ContextLayout_ConversationTokens_ExcludeTheFixedOverhead()
    {
        // Arrange: a layout carrying three twenty-token entries and a measured overhead
        var transcript = SessionTestData.TranscriptOf(3, 20);
        var layout = ContextLayout.Create(SessionTestData.SmallPolicy, 100, 200).WithTranscript(transcript);

        // Act / Assert: the conversation is the transcript, the total adds the overhead back
        Assert.Equal(60, layout.ConversationTokens);
        Assert.Equal(360, layout.TotalEstimatedTokens);
    }

    /// <summary>
    ///     Proves replacing the transcript produces a new layout and leaves the original untouched,
    ///     which is what lets a test hold a before-and-after pair across a rotation.
    /// </summary>
    [Fact]
    public void ContextLayout_WithTranscript_LeavesTheOriginalUnchanged()
    {
        // Arrange: an empty layout
        var original = ContextLayout.Create(SessionTestData.SmallPolicy, 0, 0);

        // Act: give it a transcript
        var updated = original.WithTranscript(SessionTestData.TranscriptOf(2, 20));

        // Assert: the original is still empty
        Assert.Equal(0, original.ConversationTokens);
        Assert.Equal(40, updated.ConversationTokens);
    }

    /// <summary>
    ///     Proves a tier list that does not match the policy is refused, so a layout whose hierarchy
    ///     disagrees with its own budgets can never exist.
    /// </summary>
    [Fact]
    public void ContextLayout_WithTiers_WrongTierCount_Throws()
    {
        // Arrange: a four-tier layout and a single replacement tier
        var layout = ContextLayout.Create(SessionTestData.SmallPolicy, 0, 0);

        // Act / Assert: refused rather than silently truncating the hierarchy
        Assert.Throws<ArgumentException>(() =>
            layout.WithTiers(SessionTranscript.Empty, [ContextTier.Empty(1, 60)]));
    }

    /// <summary>
    ///     Proves the seed is emitted most stable first — coarsest records, then finer records, then
    ///     the verbatim recent turns — which is what lets a provider's prompt cache match the longest
    ///     possible prefix.
    /// </summary>
    [Fact]
    public void ContextLayout_BuildSeed_EmitsCoarsestRecordsFirstThenVerbatimHistory()
    {
        // Arrange: a layout with records in tiers one and two and one verbatim turn
        var layout = ContextLayout
            .Create(SessionTestData.SmallPolicy, 0, 0)
            .WithTiers(
                SessionTranscript.Empty.Append(TranscriptEntry.User("newest")),
                [
                    ContextTier.Empty(1, 60).WithContent("tier one record"),
                    ContextTier.Empty(2, 40).WithContent("tier two record"),
                    ContextTier.Empty(3, 30)
                ]);

        // Act: build the seed a fresh provider session would receive
        var seed = layout.BuildSeed();

        // Assert: tier two before tier one, the empty tier three skipped, verbatim history last
        Assert.Equal(3, seed.Count);
        Assert.Contains("tier two record", seed[0].Text, StringComparison.Ordinal);
        Assert.Contains("tier one record", seed[1].Text, StringComparison.Ordinal);
        Assert.Equal(TranscriptEntryKind.UserMessage, seed[2].Kind);
        Assert.Equal("newest", seed[2].Text);
    }

    /// <summary>
    ///     Proves an empty tier contributes nothing to the seed, so framing tokens are not spent
    ///     saying nothing.
    /// </summary>
    [Fact]
    public void ContextLayout_BuildSeed_EmptyLayout_EmitsNothing()
    {
        // Arrange / Act: a layout that has held no conversation
        var seed = ContextLayout.Create(SessionTestData.SmallPolicy, 0, 0).BuildSeed();

        // Assert: nothing to seed
        Assert.Empty(seed);
    }

    /// <summary>
    ///     Proves a tier knows whether its record still fits, which is the test the rotation engine
    ///     makes after every consolidation.
    /// </summary>
    [Fact]
    public void ContextTier_IsWithinBudget_ReflectsTheRecordSize()
    {
        // Arrange: a tier budgeted at ten tokens
        var tier = ContextTier.Empty(1, 10);

        // Act: give it a record of twenty tokens
        var overfull = tier.WithContent(new string('x', 20 * TokenEstimator.CharactersPerToken));

        // Assert: the empty tier fits, the overfull one does not
        Assert.True(tier.IsWithinBudget);
        Assert.False(overfull.IsWithinBudget);
        Assert.Equal(20, overfull.EstimatedTokens);
    }

    /// <summary>
    ///     Proves tier zero cannot be represented as a coarse tier: it holds verbatim history and is
    ///     a transcript, not a consolidated record.
    /// </summary>
    [Fact]
    public void ContextTier_Construct_TierZero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContextTier.Empty(0, 100));
    }
}
