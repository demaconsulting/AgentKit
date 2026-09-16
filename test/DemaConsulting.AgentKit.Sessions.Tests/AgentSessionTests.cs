namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="AgentSessionResponse"/>: the per-turn report of the answer, the
///     usage, whether the session rotated, the compaction level, and whether material was dropped.
/// </summary>
public class AgentSessionTests
{
    /// <summary>
    ///     Proves the response carries everything a turn produced, including the compaction level
    ///     and the honest dropped-material signal that replace the old saturation family.
    /// </summary>
    [Fact]
    public void AgentSessionResponse_Construct_CarriesTheTurnResult()
    {
        var usage = ContextUsage.FromEstimate(100, 1000);

        var response = new AgentSessionResponse(
            "answer", usage, rotationOccurred: true, CompactionLevel.High, materialDropped: true);

        Assert.Equal("answer", response.Text);
        Assert.Same(usage, response.Usage);
        Assert.True(response.RotationOccurred);
        Assert.Equal(CompactionLevel.High, response.Level);
        Assert.True(response.MaterialDropped);
    }

    /// <summary>
    ///     Proves the level and dropped-material default to the relaxed, nothing-dropped state, which
    ///     is what an ordinary turn reports.
    /// </summary>
    [Fact]
    public void AgentSessionResponse_Construct_DefaultsToLowAndNotDropped()
    {
        var response = new AgentSessionResponse("answer", ContextUsage.FromEstimate(1, 1000), rotationOccurred: false);

        Assert.Equal(CompactionLevel.Low, response.Level);
        Assert.False(response.MaterialDropped);
    }

    /// <summary>
    ///     Proves a null answer or usage is refused rather than stored.
    /// </summary>
    [Fact]
    public void AgentSessionResponse_Construct_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentSessionResponse(null!, ContextUsage.FromEstimate(1, 1000), false));
        Assert.Throws<ArgumentNullException>(() =>
            new AgentSessionResponse("answer", null!, false));
    }

    /// <summary>
    ///     Proves an undefined compaction level is refused, following the enum-validation convention
    ///     the rest of the package uses.
    /// </summary>
    [Fact]
    public void AgentSessionResponse_Construct_UndefinedLevel_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentSessionResponse("answer", ContextUsage.FromEstimate(1, 1000), false, (CompactionLevel)99));
    }
}
