namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="ToolLimits"/> class.
/// </summary>
/// <remarks>
///     The default-value scenario asserts against literal numbers rather than against the
///     published constants. Asserting a constant against itself is vacuous, and these four
///     values appear in the design document, in requirement text and in the public API surface;
///     this test is what stops them drifting silently.
/// </remarks>
public class ToolLimitsTests
{
    /// <summary>
    ///     Proves that the shared default instance exposes the published ceilings.
    /// </summary>
    [Fact]
    public void ToolLimits_Default_AllCeilings_MatchPublishedDefaults()
    {
        // Arrange: the shared instance a host receives when it configures nothing
        var limits = ToolLimits.Default;

        // Act: read every published ceiling
        var readBytes = limits.MaxReadBytes;
        var resultCharacters = limits.MaxResultCharacters;
        var binaryBytes = limits.MaxBinaryBytes;
        var attachments = limits.MaxAttachmentsPerTurn;

        // Assert: the literal published values, so a silent change to a constant fails here
        Assert.Equal(65536, readBytes);
        Assert.Equal(32000, resultCharacters);
        Assert.Equal(8388608, binaryBytes);
        Assert.Equal(4, attachments);
    }

    /// <summary>
    ///     Proves that constructing with no arguments produces the documented defaults.
    /// </summary>
    [Fact]
    public void ToolLimits_Constructor_NoArguments_MatchesDefault()
    {
        // Arrange: the shared default to compare against
        var expected = ToolLimits.Default;

        // Act: construct without configuring anything
        var limits = new ToolLimits();

        // Assert: an instance built with no arguments is equivalent to the published default
        Assert.Equal(expected.MaxReadBytes, limits.MaxReadBytes);
        Assert.Equal(expected.MaxResultCharacters, limits.MaxResultCharacters);
        Assert.Equal(expected.MaxBinaryBytes, limits.MaxBinaryBytes);
        Assert.Equal(expected.MaxAttachmentsPerTurn, limits.MaxAttachmentsPerTurn);
    }

    /// <summary>
    ///     Proves that replacing one ceiling leaves the other three at their defaults.
    /// </summary>
    /// <remarks>
    ///     This is what the optional-parameter constructor is for: a host states the one
    ///     ceiling it cares about without restating — or accidentally resetting — the rest.
    /// </remarks>
    [Fact]
    public void ToolLimits_Constructor_SingleCeilingOverridden_RetainsOtherDefaults()
    {
        // Arrange: a host that cares only about the size of binary content
        const int customBinaryBytes = 1024;

        // Act: replace exactly one ceiling
        var limits = new ToolLimits(maxBinaryBytes: customBinaryBytes);

        // Assert: the replaced ceiling takes effect and the others are untouched
        Assert.Equal(customBinaryBytes, limits.MaxBinaryBytes);
        Assert.Equal(ToolLimits.DefaultMaxReadBytes, limits.MaxReadBytes);
        Assert.Equal(ToolLimits.DefaultMaxResultCharacters, limits.MaxResultCharacters);
        Assert.Equal(ToolLimits.DefaultMaxAttachmentsPerTurn, limits.MaxAttachmentsPerTurn);
    }

    /// <summary>
    ///     Proves that every ceiling can be replaced at once and is exposed as supplied.
    /// </summary>
    [Fact]
    public void ToolLimits_Constructor_AllCeilingsSupplied_ExposesSuppliedValues()
    {
        // Arrange: four values that are distinguishable from each other and from the defaults
        const int readBytes = 11;
        const int resultCharacters = 22;
        const int binaryBytes = 33;
        const int attachments = 44;

        // Act: supply every ceiling positionally, in the documented order
        var limits = new ToolLimits(readBytes, resultCharacters, binaryBytes, attachments);

        // Assert: each ceiling lands on its own property, so the order cannot have transposed
        Assert.Equal(readBytes, limits.MaxReadBytes);
        Assert.Equal(resultCharacters, limits.MaxResultCharacters);
        Assert.Equal(binaryBytes, limits.MaxBinaryBytes);
        Assert.Equal(attachments, limits.MaxAttachmentsPerTurn);
    }

    /// <summary>
    ///     Proves that a negative read ceiling is rejected.
    /// </summary>
    [Fact]
    public void ToolLimits_Constructor_NegativeMaxReadBytes_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert: a negative ceiling has no meaning and is a configuration error
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolLimits(maxReadBytes: -1));
    }

    /// <summary>
    ///     Proves that a negative result-character ceiling is rejected.
    /// </summary>
    [Fact]
    public void ToolLimits_Constructor_NegativeMaxResultCharacters_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert: every ceiling is validated, not only the first
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolLimits(maxResultCharacters: -1));
    }

    /// <summary>
    ///     Proves that a negative binary-content ceiling is rejected.
    /// </summary>
    [Fact]
    public void ToolLimits_Constructor_NegativeMaxBinaryBytes_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert: every ceiling is validated
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolLimits(maxBinaryBytes: -1));
    }

    /// <summary>
    ///     Proves that a negative attachment ceiling is rejected.
    /// </summary>
    [Fact]
    public void ToolLimits_Constructor_NegativeMaxAttachmentsPerTurn_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert: every ceiling is validated, including the last
        Assert.Throws<ArgumentOutOfRangeException>(() => new ToolLimits(maxAttachmentsPerTurn: -1));
    }

    /// <summary>
    ///     Proves that a ceiling of zero is accepted.
    /// </summary>
    /// <remarks>
    ///     Zero is the expressible way for a host to disable an operation entirely — a tool
    ///     permitted to attach nothing, for instance. Treating it as invalid alongside a
    ///     negative value would remove a meaningful configuration.
    /// </remarks>
    [Fact]
    public void ToolLimits_Constructor_ZeroCeiling_IsAccepted()
    {
        // Act: disable every operation by configuring a ceiling of zero
        var limits = new ToolLimits(0, 0, 0, 0);

        // Assert: the zero ceilings are accepted and reported back unchanged
        Assert.Equal(0, limits.MaxReadBytes);
        Assert.Equal(0, limits.MaxResultCharacters);
        Assert.Equal(0, limits.MaxBinaryBytes);
        Assert.Equal(0, limits.MaxAttachmentsPerTurn);
    }
}
