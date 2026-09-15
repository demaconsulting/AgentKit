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
            new ContextUsage(100, 8000, 100, (ContextUsageOrigin)99));

        Assert.Equal("origin", error.ParamName);
    }

    /// <summary>
    ///     Proves a provider's own split survives into the figure, and that the overhead is derived
    ///     from it rather than supplied from elsewhere. This is the seam a reporting adapter uses:
    ///     the conversation count it passes is the one every rotation decision is made against, so
    ///     no estimate of this library's is ever subtracted from a number the provider measured.
    /// </summary>
    [Fact]
    public void ContextUsage_FromProvider_WithConversationSplit_CarriesItAndDerivesTheOverhead()
    {
        // Arrange / Act: the shape a Copilot session reports - a current total, a conversation
        // count, and the limit
        var usage = ContextUsage.FromProvider(184_561, 200_000, 181_834);

        // Assert: the split is carried, and the overhead is what the two counts leave between them
        Assert.Equal(181_834, usage.ConversationTokens);
        Assert.Equal(2727, usage.OverheadTokens);
        Assert.Equal(ContextUsageOrigin.Provider, usage.Origin);
    }

    /// <summary>
    ///     Proves a figure whose producer reports no split treats the whole of its usage as
    ///     conversation. That credits the session with no overhead allowance, which rotates earlier
    ///     than a correct split would — the safe direction, because the guarantee this package
    ///     exists to deliver is that the provider's own compactor never fires.
    /// </summary>
    [Fact]
    public void ContextUsage_WithoutConversationSplit_TreatsTheWholeUsageAsConversation()
    {
        // Arrange / Act: both origins, neither offering a split
        var reported = ContextUsage.FromProvider(3000, 8000);
        var estimated = ContextUsage.FromEstimate(1200, 8000);

        // Assert: nothing is attributed to overhead, so nothing is credited that was not reported
        Assert.Equal(3000, reported.ConversationTokens);
        Assert.Equal(0, reported.OverheadTokens);
        Assert.Equal(1200, estimated.ConversationTokens);
        Assert.Equal(0, estimated.OverheadTokens);
    }

    /// <summary>
    ///     Proves a conversation larger than the whole usage is refused. No accounting produces it,
    ///     and it would make the derived overhead negative — which would <em>enlarge</em> the window
    ///     the rotation threshold is taken from and let the session run past the provider's own
    ///     compactor, the one failure this package exists to prevent.
    /// </summary>
    [Fact]
    public void ContextUsage_Construct_ConversationExceedingUsage_Throws()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ContextUsage.FromProvider(1000, 8000, 1001));

        Assert.Equal("conversationTokens", error.ParamName);
    }

    /// <summary>
    ///     Proves a negative conversation count is refused, for the same reason a negative usage is.
    /// </summary>
    [Fact]
    public void ContextUsage_Construct_NegativeConversation_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContextUsage.FromEstimate(1000, 8000, -1));
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
