namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for <see cref="CompactionPolicy"/>: the one control an application configures, the
///     maximum verbatim tail length.
/// </summary>
public class CompactionPolicyTests
{
    /// <summary>
    ///     Proves the published default is the one an application actually receives.
    /// </summary>
    [Fact]
    public void CompactionPolicy_Default_CarriesThePublishedVerbatimTurns()
    {
        var policy = CompactionPolicy.Default;

        Assert.Equal(20, policy.VerbatimTurns);
        Assert.Equal(CompactionPolicy.DefaultVerbatimTurns, policy.VerbatimTurns);
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
    ///     Proves the one control can be replaced.
    /// </summary>
    [Fact]
    public void CompactionPolicy_Construct_SetsVerbatimTurns()
    {
        var policy = new CompactionPolicy(verbatimTurns: 8);

        Assert.Equal(8, policy.VerbatimTurns);
    }

    /// <summary>
    ///     Proves a non-positive verbatim tail is refused: a tail of zero would keep no recent
    ///     history verbatim and rotate everything the moment it arrived.
    /// </summary>
    /// <param name="verbatimTurns">The rejected verbatim tail length.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CompactionPolicy_Construct_NonPositive_Throws(int verbatimTurns)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompactionPolicy(verbatimTurns));
    }
}
