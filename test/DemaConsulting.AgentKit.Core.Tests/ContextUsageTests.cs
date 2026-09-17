namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for <see cref="ContextUsage"/>: the one usage shape every provider session is
///     reduced to, and the arithmetic it refuses.
/// </summary>
public class ContextUsageTests
{
    /// <summary>
    ///     Proves a provider figure carries its split, and derives the overhead from it rather than
    ///     from anything a consumer works out afterwards.
    /// </summary>
    [Fact]
    public void ContextUsage_FromProvider_CarriesSplit()
    {
        // Arrange / Act: a provider that distinguishes its conversation from its fixed overhead
        var usage = ContextUsage.FromProvider(usedTokens: 900, windowTokens: 1000, conversationTokens: 700);

        // Assert: every figure is the provider's own, and the overhead is the difference
        Assert.Equal(900, usage.UsedTokens);
        Assert.Equal(1000, usage.WindowTokens);
        Assert.Equal(700, usage.ConversationTokens);
        Assert.Equal(200, usage.OverheadTokens);
    }

    /// <summary>
    ///     Proves a provider reporting only totals credits no overhead, treating the whole of the
    ///     usage as conversation — which rotates earlier rather than later, the safe direction.
    /// </summary>
    [Fact]
    public void ContextUsage_FromProvider_NoSplit_TreatsAllAsConversation()
    {
        // Arrange / Act: a provider that reports what it holds but not what it holds it for
        var usage = ContextUsage.FromProvider(usedTokens: 900, windowTokens: 1000);

        // Assert: no overhead allowance is invented, so the whole of the usage is compared against
        // the threshold
        Assert.Equal(900, usage.ConversationTokens);
        Assert.Equal(0, usage.OverheadTokens);
    }

    /// <summary>
    ///     Proves usage beyond the window is reported as it arrived rather than clamped to it.
    /// </summary>
    /// <remarks>
    ///     A provider genuinely reports this, and it is the one condition an application most needs
    ///     to see. Clamping would make an over-full window indistinguishable from a full one, and
    ///     would do it inside the type every rotation decision is read from.
    /// </remarks>
    [Fact]
    public void ContextUsage_FromProvider_BeyondTheWindow_IsNotClamped()
    {
        // Arrange / Act: a provider reporting more occupied than its window holds
        var usage = ContextUsage.FromProvider(usedTokens: 1200, windowTokens: 1000);

        // Assert: both figures survive intact, so the overshoot is visible
        Assert.Equal(1200, usage.UsedTokens);
        Assert.Equal(1000, usage.WindowTokens);
        Assert.Equal(1200, usage.ConversationTokens);
    }

    /// <summary>
    ///     Proves a figure no accounting can produce is refused where the adapter's arithmetic defect
    ///     was written, rather than clamped into something the engine would act on.
    /// </summary>
    /// <remarks>
    ///     A conversation larger than the total would make <see cref="ContextUsage.OverheadTokens"/>
    ///     negative, which enlarges the effective window the rotation threshold is taken from — the
    ///     session would then rotate later than the provider's own window allows, which is the one
    ///     failure this package exists to prevent.
    /// </remarks>
    [Fact]
    public void ContextUsage_Construct_MeaninglessFigures_Throws()
    {
        // Act / Assert: each impossible figure is refused at construction
        Assert.Throws<ArgumentOutOfRangeException>(() => ContextUsage.FromProvider(100, 1000, conversationTokens: 200));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContextUsage.FromProvider(100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContextUsage.FromProvider(-1, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContextUsage(100, 1000, -1));
    }
}
