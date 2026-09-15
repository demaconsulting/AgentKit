namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="ContextUsage"/> and <see cref="IContextUsageReporter"/>: the one
///     shape both provider families are reduced to, and the record of which of them a figure came
///     from.
/// </summary>
public class ContextUsageTests
{
    /// <summary>
    ///     Proves a provider-reported figure is marked as such, so an application can tell a
    ///     measured number from a derived one.
    /// </summary>
    [Fact]
    public void ContextUsage_FromProvider_MarksTheOriginAsProvider()
    {
        // Arrange / Act: a figure a provider supplied
        var usage = ContextUsage.FromProvider(3000, 8000);

        // Assert: the figures and their origin survive
        Assert.Equal(3000, usage.UsedTokens);
        Assert.Equal(8000, usage.WindowTokens);
        Assert.Equal(ContextUsageOrigin.Provider, usage.Origin);
    }

    /// <summary>
    ///     Proves an estimated figure is marked as estimated, which is the honest answer for a
    ///     provider that reports nothing.
    /// </summary>
    [Fact]
    public void ContextUsage_FromEstimate_MarksTheOriginAsEstimated()
    {
        // Arrange / Act: a figure this library derived
        var usage = ContextUsage.FromEstimate(1200, 8000);

        // Assert: marked as estimated
        Assert.Equal(ContextUsageOrigin.Estimated, usage.Origin);
    }

    /// <summary>
    ///     Proves the derived figures are computed rather than stored, so they cannot disagree with
    ///     the counts they come from.
    /// </summary>
    [Fact]
    public void ContextUsage_Derived_ReportsFreeTokensAndFraction()
    {
        // Arrange: a quarter-full window
        var usage = ContextUsage.FromProvider(2000, 8000);

        // Act / Assert: the remainder and the fraction follow from the counts
        Assert.Equal(6000, usage.FreeTokens);
        Assert.Equal(0.25, usage.UsedFraction);
    }

    /// <summary>
    ///     Proves an over-full window reports no free space rather than a negative amount, while
    ///     leaving the over-full condition visible in the raw counts.
    /// </summary>
    [Fact]
    public void ContextUsage_OverFullWindow_ReportsNoFreeTokensButKeepsTheCounts()
    {
        // Arrange: a provider reporting usage beyond its own limit
        var usage = ContextUsage.FromProvider(9000, 8000);

        // Assert: free space floors at zero, the counts and fraction do not lie about it
        Assert.Equal(0, usage.FreeTokens);
        Assert.Equal(9000, usage.UsedTokens);
        Assert.True(usage.UsedFraction > 1.0);
    }

    /// <summary>
    ///     Proves a negative usage is refused; it could only come from a defect, and would make every
    ///     threshold comparison meaningless.
    /// </summary>
    [Fact]
    public void ContextUsage_Construct_NegativeUsage_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContextUsage.FromProvider(-1, 8000));
    }

    /// <summary>
    ///     Proves a window of zero is refused; there would be nothing to be a fraction of.
    /// </summary>
    [Fact]
    public void ContextUsage_Construct_ZeroWindow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContextUsage.FromEstimate(0, 0));
    }

    /// <summary>
    ///     Proves an undefined origin is refused rather than quietly treated as an estimate.
    ///     Everything that reads the origin asks only whether it is
    ///     <see cref="ContextUsageOrigin.Provider"/>, so a cast integer would take the
    ///     configured-window threshold path and skip the reported-window bound check entirely —
    ///     silently selecting a materially different behavior from a value that names nothing.
    /// </summary>
    [Fact]
    public void ContextUsage_Construct_UndefinedOrigin_Throws()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ContextUsage(100, 8000, (ContextUsageOrigin)99));

        Assert.Equal("origin", error.ParamName);
    }

    /// <summary>
    ///     Proves a reporter may answer "I do not know", which is how a provider that reveals
    ///     nothing is represented without inventing a number.
    /// </summary>
    [Fact]
    public void ContextUsage_Reporter_MayReportNothing()
    {
        // Arrange: a reporter that knows nothing
        IContextUsageReporter reporter = new SilentReporter();

        // Act / Assert: it says so, rather than returning a fabricated figure
        Assert.Null(reporter.CurrentUsage);
    }

    /// <summary>
    ///     A reporter standing in for a provider that reveals nothing about its window.
    /// </summary>
    private sealed class SilentReporter : IContextUsageReporter
    {
        /// <inheritdoc/>
        public ContextUsage? CurrentUsage => null;
    }
}
