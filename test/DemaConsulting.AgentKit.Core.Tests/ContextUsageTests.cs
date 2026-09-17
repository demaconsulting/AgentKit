namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for <see cref="ContextUsage"/>: the one usage shape both provider families are
///     reduced to, and the origin that records which road a figure came down.
/// </summary>
public class ContextUsageTests
{
    /// <summary>
    ///     Proves a provider figure carries its split and is marked provider-reported, so every
    ///     comparison downstream is made in the provider's own tokens.
    /// </summary>
    [Fact]
    public void ContextUsage_FromProvider_CarriesSplitAndOrigin()
    {
        var usage = ContextUsage.FromProvider(usedTokens: 900, windowTokens: 1000, conversationTokens: 700);

        Assert.Equal(900, usage.UsedTokens);
        Assert.Equal(1000, usage.WindowTokens);
        Assert.Equal(700, usage.ConversationTokens);
        Assert.Equal(200, usage.OverheadTokens);
        Assert.Equal(ContextUsageOrigin.Provider, usage.Origin);
    }

    /// <summary>
    ///     Proves a provider reporting only totals credits no overhead, treating the whole of the
    ///     usage as conversation — which rotates earlier rather than later, the safe direction.
    /// </summary>
    [Fact]
    public void ContextUsage_FromProvider_NoSplit_TreatsAllAsConversation()
    {
        var usage = ContextUsage.FromProvider(usedTokens: 900, windowTokens: 1000);

        Assert.Equal(900, usage.ConversationTokens);
        Assert.Equal(0, usage.OverheadTokens);
    }

    /// <summary>
    ///     Proves an estimated figure is marked estimated and reports the same figures on both sides.
    /// </summary>
    [Fact]
    public void ContextUsage_FromEstimate_MarksEstimated()
    {
        var usage = ContextUsage.FromEstimate(usedTokens: 500, windowTokens: 1000, conversationTokens: 400);

        Assert.Equal(ContextUsageOrigin.Estimated, usage.Origin);
        Assert.Equal(400, usage.ConversationTokens);
        Assert.Equal(100, usage.OverheadTokens);
    }

    /// <summary>
    ///     Proves the derived helpers: free tokens floored at zero, and the used fraction not clamped
    ///     so an over-full window is visible.
    /// </summary>
    [Fact]
    public void ContextUsage_DerivedFigures_AreHonest()
    {
        var usage = ContextUsage.FromProvider(usedTokens: 1200, windowTokens: 1000);

        Assert.Equal(0, usage.FreeTokens);
        Assert.Equal(1.2, usage.UsedFraction);
    }

    /// <summary>
    ///     Proves a conversation larger than the total, a non-positive window, or an undefined origin
    ///     is refused where the adapter's arithmetic defect was written.
    /// </summary>
    [Fact]
    public void ContextUsage_Construct_MeaninglessFigures_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContextUsage.FromProvider(100, 1000, conversationTokens: 200));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContextUsage.FromProvider(100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ContextUsage(100, 1000, 50, (ContextUsageOrigin)99));
    }
}
