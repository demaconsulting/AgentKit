namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="CompactionPolicy"/>: the validated defaults and the author
///     overrides that decide when a session rotates and how much of each tier survives.
/// </summary>
public class CompactionPolicyTests
{
    /// <summary>
    ///     Proves the published defaults are the ones an application actually receives, so the
    ///     documented four-tier arrangement and its bound are real rather than aspirational.
    /// </summary>
    [Fact]
    public void CompactionPolicy_Default_CarriesThePublishedTierBudgets()
    {
        // Arrange / Act: the policy an application receives when it configures nothing
        var policy = CompactionPolicy.Default;

        // Assert: four tiers, the published budgets, and the bound they sum to
        Assert.Equal(4, policy.TierCount);
        Assert.Equal([2000, 1200, 900, 700], policy.TierBudgetTokens);
        Assert.Equal(4800, policy.TotalTierBudgetTokens);
        Assert.Equal(CompactionPolicy.DefaultRotationThreshold, policy.RotationThreshold);
        Assert.Equal(CompactionPolicy.DefaultSaturationRatio, policy.SaturationRatio);
    }

    /// <summary>
    ///     Proves the default is a single shared instance, so a caller can establish by reference
    ///     that no host configuration was applied.
    /// </summary>
    [Fact]
    public void CompactionPolicy_Default_IsShared()
    {
        Assert.Same(CompactionPolicy.Default, CompactionPolicy.Default);
    }

    /// <summary>
    ///     Proves one control can be replaced without restating the others, which is the convention
    ///     the rest of AgentKit's option types follow.
    /// </summary>
    [Fact]
    public void CompactionPolicy_Construct_SingleOverride_KeepsOtherDefaults()
    {
        // Arrange / Act: replace only the rotation threshold
        var policy = new CompactionPolicy(rotationThreshold: 0.5);

        // Assert: the threshold changed and the tier budgets did not
        Assert.Equal(0.5, policy.RotationThreshold);
        Assert.Equal(CompactionPolicy.DefaultTierBudgetTokens, policy.TierBudgetTokens);
    }

    /// <summary>
    ///     Proves the budgets are copied at construction, so a caller mutating the list it supplied
    ///     cannot change a policy a session is already running under.
    /// </summary>
    [Fact]
    public void CompactionPolicy_Construct_CopiesTheSuppliedBudgets()
    {
        // Arrange: a mutable list handed to the policy
        var budgets = new List<int> { 100, 50 };
        var policy = new CompactionPolicy(budgets);

        // Act: mutate the caller's list afterwards
        budgets[0] = 9999;

        // Assert: the policy is unaffected
        Assert.Equal([100, 50], policy.TierBudgetTokens);
    }

    /// <summary>
    ///     Proves a single tier is refused: with nowhere for overflowing history to age into, the
    ///     arrangement degenerates to dropping the oldest turns outright.
    /// </summary>
    [Fact]
    public void CompactionPolicy_Construct_SingleTier_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CompactionPolicy([1000]));
    }

    /// <summary>
    ///     Proves a tier that could hold nothing is refused rather than silently disabling a level of
    ///     the hierarchy.
    /// </summary>
    [Fact]
    public void CompactionPolicy_Construct_NonPositiveBudget_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompactionPolicy([1000, 0]));
    }

    /// <summary>
    ///     Proves a coarser tier larger than the tier it ages from is refused: a coarser record
    ///     given more room than the finer one it replaces is the opposite of what consolidation is
    ///     for, and would never reduce.
    /// </summary>
    [Fact]
    public void CompactionPolicy_Construct_GrowingBudgets_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CompactionPolicy([500, 900]));
    }

    /// <summary>
    ///     Proves a threshold that could never fire, or would fire on every turn, is refused.
    /// </summary>
    /// <param name="threshold">The rejected rotation threshold.</param>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void CompactionPolicy_Construct_ThresholdOutOfRange_Throws(double threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompactionPolicy(rotationThreshold: threshold));
    }

    /// <summary>
    ///     Proves a saturation ratio outside the meaningful range is refused; a ratio above one could
    ///     never be reached and a ratio at or below zero would report every consolidation as
    ///     saturated.
    /// </summary>
    /// <param name="ratio">The rejected saturation ratio.</param>
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    public void CompactionPolicy_Construct_SaturationRatioOutOfRange_Throws(double ratio)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompactionPolicy(saturationRatio: ratio));
    }
}
