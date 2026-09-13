using DemaConsulting.AgentKit.Tools.Memory;

namespace DemaConsulting.AgentKit.Tools.Tests.Memory;

/// <summary>
///     Unit tests for the <see cref="MemoryOptions"/> class.
/// </summary>
public class MemoryOptionsTests
{
    /// <summary>
    ///     Proves an author who configures nothing gets the published defaults, so the numbers that
    ///     appear in the requirements are the numbers that ship.
    /// </summary>
    [Fact]
    public void MemoryOptions_Constructor_NoArguments_UsesThePublishedDefaults()
    {
        // Arrange / Act: the configuration an author receives when they configure nothing
        var options = new MemoryOptions();

        // Assert: both controls carry their published constants
        Assert.Equal(MemoryOptions.DefaultNearDuplicateThreshold, options.NearDuplicateThreshold);
        Assert.Equal(MemoryOptions.DefaultRecallCount, options.RecallCount);
        Assert.Equal(0.88, MemoryOptions.DefaultNearDuplicateThreshold);
        Assert.Equal(5, MemoryOptions.DefaultRecallCount);
    }

    /// <summary>
    ///     Proves the shared default instance carries the same values as a freshly constructed one.
    /// </summary>
    [Fact]
    public void MemoryOptions_Default_CarriesThePublishedDefaults()
    {
        // Assert: the shared instance is the default configuration, not a separate set of numbers
        Assert.Equal(MemoryOptions.DefaultNearDuplicateThreshold, MemoryOptions.Default.NearDuplicateThreshold);
        Assert.Equal(MemoryOptions.DefaultRecallCount, MemoryOptions.Default.RecallCount);
    }

    /// <summary>
    ///     Proves one control can be replaced without restating the other, which is why both
    ///     parameters are optional.
    /// </summary>
    [Fact]
    public void MemoryOptions_Constructor_OneArgument_LeavesTheOtherAtItsDefault()
    {
        // Act: replace only the recall count
        var options = new MemoryOptions(recallCount: 3);

        // Assert: the threshold is untouched
        Assert.Equal(3, options.RecallCount);
        Assert.Equal(MemoryOptions.DefaultNearDuplicateThreshold, options.NearDuplicateThreshold);
    }

    /// <summary>
    ///     Proves the extremes of both controls are accepted, because a threshold of 0 or 1 and a
    ///     recall count of zero are legitimate author configurations rather than mistakes.
    /// </summary>
    [Fact]
    public void MemoryOptions_Constructor_BoundaryValues_AreAccepted()
    {
        // Act: the four boundary configurations
        var strictest = new MemoryOptions(nearDuplicateThreshold: 1.0, recallCount: 0);
        var loosest = new MemoryOptions(nearDuplicateThreshold: 0.0, recallCount: int.MaxValue);

        // Assert: all four values are held as stated
        Assert.Equal(1.0, strictest.NearDuplicateThreshold);
        Assert.Equal(0, strictest.RecallCount);
        Assert.Equal(0.0, loosest.NearDuplicateThreshold);
        Assert.Equal(int.MaxValue, loosest.RecallCount);
    }

    /// <summary>
    ///     Proves a threshold outside the range cosine can produce is rejected rather than clamped,
    ///     because a clamped threshold is a control the author believes they set and did not.
    /// </summary>
    /// <param name="threshold">The out-of-range threshold to reject.</param>
    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MemoryOptions_Constructor_ThresholdOutOfRange_ThrowsArgumentOutOfRangeException(double threshold)
    {
        // Act / Assert: the configuration is refused at the point it was written
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryOptions(nearDuplicateThreshold: threshold));
    }

    /// <summary>
    ///     Proves a negative recall count is rejected, because a negative number of results has no
    ///     meaning at all.
    /// </summary>
    [Fact]
    public void MemoryOptions_Constructor_NegativeRecallCount_ThrowsArgumentOutOfRangeException()
    {
        // Act / Assert: the configuration is refused at the point it was written
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryOptions(recallCount: -1));
    }
}
