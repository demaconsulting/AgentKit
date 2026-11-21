namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="ToolOutputOptions"/> class.
/// </summary>
public class ToolOutputOptionsTests
{
    /// <summary>
    ///     Proves the options a host receives when it configures nothing are the published defaults.
    /// </summary>
    [Fact]
    public void ToolOutputOptions_Default_CarriesThePublishedDefaults()
    {
        // Arrange / Act: the shared default instance
        var options = ToolOutputOptions.Default;

        // Assert: every value is the published constant, and the constants are the documented values
        Assert.Equal(ToolOutputOptions.DefaultPreviewCharacters, options.PreviewCharacters);
        Assert.Equal(ToolOutputOptions.DefaultMaxOutputCharacters, options.MaxOutputCharacters);
        Assert.Equal(ToolOutputOptions.DefaultMaxRetainedCharacters, options.MaxRetainedCharacters);
        Assert.Equal(ToolOutputOptions.DefaultMaxRetainedOutputs, options.MaxRetainedOutputs);
        Assert.Equal(4_000, ToolOutputOptions.DefaultPreviewCharacters);
        Assert.Equal(4_194_304, ToolOutputOptions.DefaultMaxOutputCharacters);
        Assert.Equal(16_777_216, ToolOutputOptions.DefaultMaxRetainedCharacters);
        Assert.Equal(256, ToolOutputOptions.DefaultMaxRetainedOutputs);
    }

    /// <summary>
    ///     Proves a host can replace each value without restating the others.
    /// </summary>
    [Fact]
    public void ToolOutputOptions_Constructor_OverriddenValues_AreCarried()
    {
        // Arrange / Act: override every value
        var options = new ToolOutputOptions(
            previewCharacters: 100,
            maxOutputCharacters: 200,
            maxRetainedCharacters: 300,
            maxRetainedOutputs: 4);

        // Assert: each value is the one supplied
        Assert.Equal(100, options.PreviewCharacters);
        Assert.Equal(200, options.MaxOutputCharacters);
        Assert.Equal(300, options.MaxRetainedCharacters);
        Assert.Equal(4, options.MaxRetainedOutputs);
    }

    /// <summary>
    ///     Proves a zero preview is a legitimate configuration.
    /// </summary>
    [Fact]
    public void ToolOutputOptions_Constructor_ZeroPreview_IsPermitted()
    {
        // Act: ask for references with no preview lines
        var options = new ToolOutputOptions(previewCharacters: 0);

        // Assert: accepted as configured
        Assert.Equal(0, options.PreviewCharacters);
    }

    /// <summary>
    ///     Proves each value outside its domain is rejected at construction.
    /// </summary>
    [Fact]
    public void ToolOutputOptions_Constructor_InvalidValues_ThrowArgumentOutOfRangeException()
    {
        // Act & Assert: a negative preview, a non-positive ceiling, and a store too small for one
        // maximum-size output are each a configuration mistake
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolOutputOptions(previewCharacters: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolOutputOptions(maxOutputCharacters: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolOutputOptions(maxRetainedOutputs: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ToolOutputOptions(maxOutputCharacters: 1_000, maxRetainedCharacters: 999));
    }
}
