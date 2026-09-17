namespace DemaConsulting.AgentKit.Core.Tests;

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
        // Arrange: a usage figure the provider reported
        var usage = ContextUsage.FromProvider(100, 1000);

        // Act: build the report of a turn that rotated, compacted hard and dropped material
        var response = new AgentSessionResponse(
            "answer", usage, rotationOccurred: true, CompactionLevel.High, materialDropped: true);

        // Assert: every part of the turn's result is carried through unaltered
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
        // Act: build the report of an ordinary turn, supplying neither optional argument
        var response = new AgentSessionResponse("answer", ContextUsage.FromProvider(1, 1000), rotationOccurred: false);

        // Assert: the relaxed, nothing-dropped state
        Assert.Equal(CompactionLevel.Low, response.Level);
        Assert.False(response.MaterialDropped);
    }

    /// <summary>
    ///     Proves a null answer or usage is refused rather than stored.
    /// </summary>
    [Fact]
    public void AgentSessionResponse_Construct_NullArgument_Throws()
    {
        // Act / Assert: neither a null answer nor a null usage figure is stored
        Assert.Throws<ArgumentNullException>(() =>
            new AgentSessionResponse(null!, ContextUsage.FromProvider(1, 1000), false));
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
        // Act / Assert: a level outside the enum is refused rather than reported to an application
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentSessionResponse("answer", ContextUsage.FromProvider(1, 1000), false, (CompactionLevel)99));
    }
}
