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
    ///     Proves the budget list a policy hands out cannot be cast back to an array and mutated.
    ///     A mutated budget would change an allegedly immutable policy while
    ///     <c>TotalTierBudgetTokens</c>, computed once at construction, went stale.
    /// </summary>
    [Fact]
    public void CompactionPolicy_TierBudgetTokens_CannotBeCastAndMutated()
    {
        // Arrange: a policy and the budget list it publishes
        var policy = new CompactionPolicy([100, 50]);

        // Act / Assert: the list is a read-only view, and writing through it is refused
        Assert.IsNotType<int[]>(policy.TierBudgetTokens);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)policy.TierBudgetTokens)[0] = 9999);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)CompactionPolicy.DefaultTierBudgetTokens)[0] = 1);
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

    /// <summary>
    ///     Proves a <c>NaN</c> control is refused whichever sign bit it carries. Every ordered
    ///     comparison against <c>NaN</c> is false, so a range check built from them accepts a
    ///     positive <c>NaN</c>, and the policy it configures then compares false against everything —
    ///     silently disabling rotation or saturation reporting rather than announcing that it had
    ///     been misconfigured.
    /// </summary>
    [Fact]
    public void CompactionPolicy_Construct_NaNControl_Throws()
    {
        // Arrange: the literal NaN carries a set sign bit; this one does not
        var positiveNaN = Math.Abs(double.NaN);

        // Act / Assert: both forms are refused, for both controls
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CompactionPolicy(rotationThreshold: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CompactionPolicy(rotationThreshold: positiveNaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CompactionPolicy(saturationRatio: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CompactionPolicy(saturationRatio: positiveNaN));
    }

    /// <summary>
    ///     Proves a policy whose bound cannot be represented as a token count is refused as the
    ///     argument error it is. Summing the budgets in a token-sized type surfaced an undocumented
    ///     <see cref="OverflowException"/> instead, and every site that later adds the budgets to
    ///     the seed framing — the session options' window check and the layout's own bound — would
    ///     otherwise wrap into a negative figure that a window comparison silently passes.
    /// </summary>
    [Fact]
    public void CompactionPolicy_Construct_UnrepresentableBound_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new CompactionPolicy([int.MaxValue, int.MaxValue]));

        Assert.Equal("tierBudgetTokens", exception.ParamName);
    }
}
