namespace DemaConsulting.AgentKit.Tools.Visual.Tests;

/// <summary>
///     Unit tests for the <see cref="VisualOptions"/> record.
/// </summary>
/// <remarks>
///     The options are a value a host configures once, so these scenarios verify what a host can
///     observe: the defaults it gets without configuring anything, that every value outside its
///     documented range is refused at the line that set it, and that a value inside the range is
///     kept as given.
/// </remarks>
public class VisualOptionsTests
{
    /// <summary>
    ///     Proves the defaults are the documented ones.
    /// </summary>
    [Fact]
    public void VisualOptions_Default_Values_AreTheDocumentedDefaults()
    {
        // Arrange / Act: the defaults a host gets without configuring anything
        var options = VisualOptions.Default;

        // Assert: each documented default
        Assert.Equal(TimeSpan.FromSeconds(30), options.RenderTimeout);
        Assert.Equal(2048, options.MaxRenderSide);
        Assert.Equal(768, options.MinPreviewSide);
        Assert.Equal(500_000, options.ScadMaxTriangles);
        Assert.Equal(1, options.MaxConcurrentRenders);
        Assert.Equal(new VisualOptions(), options);
    }

    /// <summary>
    ///     Proves a value inside each range is kept as given.
    /// </summary>
    [Fact]
    public void VisualOptions_Init_ValuesInRange_AreKept()
    {
        // Arrange / Act: every value at a boundary of its range
        var options = new VisualOptions
        {
            RenderTimeout = TimeSpan.FromMinutes(10),
            MaxRenderSide = 16,
            MinPreviewSide = 1,
            ScadMaxTriangles = 1,
            MaxConcurrentRenders = 4
        };

        // Assert: nothing was clamped or replaced
        Assert.Equal(TimeSpan.FromMinutes(10), options.RenderTimeout);
        Assert.Equal(16, options.MaxRenderSide);
        Assert.Equal(1, options.MinPreviewSide);
        Assert.Equal(1, options.ScadMaxTriangles);
        Assert.Equal(4, options.MaxConcurrentRenders);
    }

    /// <summary>
    ///     Proves a render timeout of zero, below zero or above ten minutes is refused.
    /// </summary>
    /// <param name="milliseconds">The timeout to set, in milliseconds.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(600_001)]
    public void VisualOptions_RenderTimeout_OutOfRange_Throws(int milliseconds)
    {
        // Act / Assert: the value is refused at the line that set it
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new VisualOptions { RenderTimeout = TimeSpan.FromMilliseconds(milliseconds) });
        Assert.Equal(nameof(VisualOptions.RenderTimeout), exception.ParamName);
    }

    /// <summary>
    ///     Proves a largest render side outside 16 to 8192 is refused.
    /// </summary>
    /// <param name="side">The side to set.</param>
    [Theory]
    [InlineData(15)]
    [InlineData(8193)]
    public void VisualOptions_MaxRenderSide_OutOfRange_Throws(int side)
    {
        // Act / Assert: the value is refused at the line that set it
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new VisualOptions { MaxRenderSide = side });
        Assert.Equal(nameof(VisualOptions.MaxRenderSide), exception.ParamName);
    }

    /// <summary>
    ///     Proves a smallest preview side outside 1 to 8192 is refused.
    /// </summary>
    /// <param name="side">The side to set.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(8193)]
    public void VisualOptions_MinPreviewSide_OutOfRange_Throws(int side)
    {
        // Act / Assert: the value is refused at the line that set it
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new VisualOptions { MinPreviewSide = side });
        Assert.Equal(nameof(VisualOptions.MinPreviewSide), exception.ParamName);
    }

    /// <summary>
    ///     Proves a triangle limit below 1 is refused.
    /// </summary>
    [Fact]
    public void VisualOptions_ScadMaxTriangles_BelowOne_Throws()
    {
        // Act / Assert: the value is refused at the line that set it
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new VisualOptions { ScadMaxTriangles = 0 });
        Assert.Equal(nameof(VisualOptions.ScadMaxTriangles), exception.ParamName);
    }

    /// <summary>
    ///     Proves a concurrency bound below 1 is refused.
    /// </summary>
    [Fact]
    public void VisualOptions_MaxConcurrentRenders_BelowOne_Throws()
    {
        // Act / Assert: the value is refused at the line that set it
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new VisualOptions { MaxConcurrentRenders = 0 });
        Assert.Equal(nameof(VisualOptions.MaxConcurrentRenders), exception.ParamName);
    }
}
