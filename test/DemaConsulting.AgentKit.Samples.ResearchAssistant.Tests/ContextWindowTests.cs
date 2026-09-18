using DemaConsulting.AgentKit.Agents.Ollama;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests;

/// <summary>
///     Unit tests for <see cref="ContextWindow"/>: how the startup banner describes a window, and
///     how a window the shipped Ollama package discovered arrives in the banner's own vocabulary.
/// </summary>
/// <remarks>
///     The precedence itself is the package's and is tested there. What is left here is what the
///     sample still owns: the wording a reader sees before a run starts, and the mapping that
///     decides which of those words a discovered window gets. A mapping that sent an assumed
///     default to the wrong member would print a guess as though it were a measurement, which is
///     exactly what naming the source exists to prevent.
/// </remarks>
public class ContextWindowTests
{
    /// <summary>
    ///     Proves every source describes itself, so the startup banner can never present an assumed
    ///     default as though it were a measured limit.
    /// </summary>
    /// <param name="source">The source being described.</param>
    [Theory]
    [InlineData(ContextWindowSource.Stated)]
    [InlineData(ContextWindowSource.LoadedModel)]
    [InlineData(ContextWindowSource.Assumed)]
    [InlineData(ContextWindowSource.Ceiling)]
    public void ContextWindow_Describe_EverySource_NamesTheNumberAndItsProvenance(
        ContextWindowSource source)
    {
        // Arrange
        var window = new ContextWindow(4096, source);

        // Act
        var described = window.Describe();

        // Assert: the number is there, and so is a statement about where it came from
        Assert.Contains("4096", described, StringComparison.Ordinal);
        Assert.Contains("(", described, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a ceiling is described as an upper bound rather than as the window.
    /// </summary>
    /// <remarks>
    ///     A ceiling only lowers: on a provider that reports its own window, one above the runtime's
    ///     limit is ignored entirely. A banner that stated it as the window could announce a figure
    ///     the session never accounts against - reporting 272,000 tokens while rotation happened at
    ///     8,000. Saying "at most" is what keeps the banner true whichever of the two governs.
    /// </remarks>
    [Fact]
    public void ContextWindow_Describe_Ceiling_ReadsAsAnUpperBoundNotTheWindow()
    {
        // Arrange / Act
        var described = new ContextWindow(11000, ContextWindowSource.Ceiling).Describe();

        // Assert
        Assert.StartsWith("at most 11000 tokens", described, StringComparison.Ordinal);
        Assert.Contains("the lower of the two governs", described, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves each source the Ollama package can report arrives in the banner as the same claim,
    ///     carrying the same figure.
    /// </summary>
    /// <param name="discovered">The source the package reported.</param>
    /// <param name="expected">The banner source it must become.</param>
    [Theory]
    [InlineData(OllamaContextWindowSource.Stated, ContextWindowSource.Stated)]
    [InlineData(OllamaContextWindowSource.LoadedModel, ContextWindowSource.LoadedModel)]
    [InlineData(OllamaContextWindowSource.Assumed, ContextWindowSource.Assumed)]
    public void ContextWindow_From_EveryDiscoverySource_KeepsTheFigureAndItsClaim(
        OllamaContextWindowSource discovered,
        ContextWindowSource expected)
    {
        // Arrange: a window the package reports, on a figure no other source would produce
        var window = new OllamaContextWindow(32768, discovered);

        // Act
        var banner = ContextWindow.From(window);

        // Assert
        Assert.Equal(32768, banner.Tokens);
        Assert.Equal(expected, banner.Source);
    }

    /// <summary>
    ///     Proves a missing discovery result is refused rather than turned into a plausible banner.
    /// </summary>
    [Fact]
    public void ContextWindow_From_NullDiscovery_Throws()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => ContextWindow.From(null!));
    }
}
