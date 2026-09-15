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
    ///     Proves the construction bound is the fixed overhead, every tier budget, and the framing
    ///     every seeded tier record carries — the single claim the whole arrangement exists to make
    ///     good on.
    /// </summary>
    [Fact]
    public void ContextLayout_MaximumBoundTokens_IsOverheadPlusEveryTierBudget()
    {
        // Arrange: a layout with a measured system prompt and tool declarations
        var layout = ContextLayout.Create(SessionTestData.SmallPolicy, 100, 200);

        // Act / Assert: the bound follows from the configuration alone
        Assert.Equal(
            100 + 200 + SessionTestData.SmallPolicy.TotalTierBudgetTokens
            + ContextLayout.SeedFramingTokens(SessionTestData.SmallPolicy),
            layout.MaximumBoundTokens);
        Assert.True(layout.IsWithinBound);
    }

    /// <summary>
    ///     Proves a fixed overhead that would carry the bound past what a token count can represent
    ///     is refused, rather than wrapping. A wrapped bound is negative, and an empty layout —
    ///     which holds nothing at all — would then report itself outside it.
    /// </summary>
    [Fact]
    public void ContextLayout_Create_UnrepresentableBound_Throws()
    {
        // Arrange / Act / Assert: the policy's own half of the bound is guaranteed representable,
        // so only a fixed overhead this large can take the total past it
        var exception = Assert.Throws<ArgumentException>(() =>
            ContextLayout.Create(SessionTestData.SmallPolicy, int.MaxValue, int.MaxValue));

        Assert.Equal("systemTokens", exception.ParamName);
    }

    /// <summary>
    ///     Proves the bound covers what a provider is actually sent. Every tier record is seeded
    ///     wrapped in a label and an entry envelope, so a bound counting only raw tier content would
    ///     be exceeded by a layout in which every tier sat exactly within its budget — and for a
    ///     provider that reports no usage, that under-count is what drives rotation.
    /// </summary>
    [Fact]
    public void ContextLayout_MaximumBoundTokens_CoversTheFramingOfEverySeededRecord()
    {
        // Arrange: every coarse tier filled to exactly its budget, and tier zero exactly at its own
        var policy = SessionTestData.SmallPolicy;
        var layout = ContextLayout
            .Create(policy, 100, 200)
            .WithTiers(
                SessionTestData.TranscriptOf(5, 20),
                [
                    ContextTier.Empty(1, 60).WithContent(new string('a', 60 * TokenEstimator.CharactersPerToken)),
                    ContextTier.Empty(2, 40).WithContent(new string('b', 40 * TokenEstimator.CharactersPerToken)),
                    ContextTier.Empty(3, 30).WithContent(new string('c', 30 * TokenEstimator.CharactersPerToken))
                ]);

        // Act: measure what a fresh provider session would actually receive
        var seedTokens = layout.BuildSeed().Sum(entry => entry.EstimatedTokens);

        // Assert: the seed exceeds the raw sum of the tier budgets, and the bound still covers it
        Assert.True(
            seedTokens > policy.TotalTierBudgetTokens,
            $"The seed held {seedTokens} tokens against {policy.TotalTierBudgetTokens} tokens of budget.");
        Assert.True(
            100 + 200 + seedTokens <= layout.MaximumBoundTokens,
            $"The seed plus overhead held {100 + 200 + seedTokens} tokens against a bound of "
            + $"{layout.MaximumBoundTokens}.");
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
    ///     Proves a tier carrying the wrong index is refused. The position in the list is what the
    ///     policy's budget is read by and what the seed labels the record by, so a tier whose own
    ///     index disagrees with its slot would leave the policy, the labels and the bound describing
    ///     different hierarchies.
    /// </summary>
    [Fact]
    public void ContextLayout_WithTiers_TierIndexDisagreesWithThePolicy_Throws()
    {
        // Arrange: a four-tier layout, and a tier list whose first slot claims to be tier three
        var layout = ContextLayout.Create(SessionTestData.SmallPolicy, 0, 0);
        ContextTier[] tiers =
        [
            new(3, 60, string.Empty),
            ContextTier.Empty(2, 40),
            ContextTier.Empty(3, 30)
        ];

        // Act / Assert: refused rather than producing a layout that indexes one tier and labels another
        Assert.Throws<ArgumentException>(() => layout.WithTiers(SessionTranscript.Empty, tiers));
    }

    /// <summary>
    ///     Proves a tier carrying a budget the policy did not give it is refused. Rotation indexes
    ///     the policy's budget for the slot while the tier reports its own, so a tier-one slot
    ///     carrying tier three's budget would be consolidated against one figure and charged against
    ///     another.
    /// </summary>
    [Fact]
    public void ContextLayout_WithTiers_TierBudgetDisagreesWithThePolicy_Throws()
    {
        // Arrange: a four-tier layout, and a tier-one slot carrying tier three's budget
        var layout = ContextLayout.Create(SessionTestData.SmallPolicy, 0, 0);
        ContextTier[] tiers =
        [
            ContextTier.Empty(1, 30),
            ContextTier.Empty(2, 40),
            ContextTier.Empty(3, 30)
        ];

        // Act / Assert: refused rather than letting the budgets disagree with the policy
        Assert.Throws<ArgumentException>(() => layout.WithTiers(SessionTranscript.Empty, tiers));
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
    ///     Proves the tier list a layout hands out cannot be cast back to its backing array and
    ///     mutated. A replaced element would change both <c>ConversationTokens</c> and the next seed
    ///     of a layout documented as immutable.
    /// </summary>
    [Fact]
    public void ContextLayout_CoarseTiers_CannotBeCastAndMutated()
    {
        // Arrange: a layout and the tier list it publishes
        var layout = ContextLayout.Create(SessionTestData.SmallPolicy, 0, 0);

        // Act / Assert: the list is a read-only view, and writing through it is refused
        Assert.IsNotType<ContextTier[]>(layout.CoarseTiers);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ContextTier>)layout.CoarseTiers)[0] = ContextTier.Empty(1, 60).WithContent("forged"));
    }

    /// <summary>
    ///     Proves the seed a rotation hands to a provider-session factory cannot be cast back and
    ///     altered. The seed is the whole history a fresh session is created from, and a caller able
    ///     to edit it between building it and using it could seed a session with material the
    ///     layout never held.
    /// </summary>
    [Fact]
    public void ContextLayout_BuildSeed_CannotBeCastAndMutated()
    {
        // Arrange: a layout carrying a consolidated record and some verbatim history
        var layout = ContextLayout
            .Create(SessionTestData.SmallPolicy, 0, 0)
            .WithTiers(
                SessionTestData.TranscriptOf(2, 10),
                [
                    ContextTier.Empty(1, 60).WithContent("tier one"),
                    ContextTier.Empty(2, 40),
                    ContextTier.Empty(3, 30),
                ]);

        // Act: build the seed
        var seed = layout.BuildSeed();

        // Assert: it is a read-only view, and writing through it is refused
        Assert.IsNotType<List<TranscriptEntry>>(seed);
        Assert.IsNotType<TranscriptEntry[]>(seed);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<TranscriptEntry>)seed)[0] = TranscriptEntry.User("forged"));
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
