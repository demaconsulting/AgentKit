namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="ConsolidationRequest"/> and <see cref="ConsolidationPrompt"/>: the
///     ratchet expressed as an input, and the documented prompt that asks for content rather than
///     for a size.
/// </summary>
public class SummarizerTests
{
    /// <summary>
    ///     Proves a first recording into a tier is reported as a degradation, which is what tells a
    ///     summarizer there is no previous record whose detail it must carry forward.
    /// </summary>
    [Fact]
    public void ConsolidationRequest_IsDegradation_TrueWhenNoPreviousRecord()
    {
        // Arrange / Act: a request with no previous record, and one with
        var first = new ConsolidationRequest(1, null, "material", 100);
        var extending = new ConsolidationRequest(1, "earlier record", "material", 100);

        // Assert: only the first is a degradation
        Assert.True(first.IsDegradation);
        Assert.False(extending.IsDegradation);
    }

    /// <summary>
    ///     Proves tier zero cannot be consolidated into: it is verbatim history by definition, and a
    ///     request to summarize into it would be a defect in the engine.
    /// </summary>
    [Fact]
    public void ConsolidationRequest_Construct_TierZero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConsolidationRequest(0, null, "material", 100));
    }

    /// <summary>
    ///     Proves blank material is refused: there would be nothing to consolidate, and the call
    ///     would spend a model round trip to say nothing.
    /// </summary>
    [Fact]
    public void ConsolidationRequest_Construct_BlankMaterial_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ConsolidationRequest(1, null, "   ", 100));
    }

    /// <summary>
    ///     Proves a non-positive budget is refused; a tier that could hold nothing is not a tier.
    /// </summary>
    [Fact]
    public void ConsolidationRequest_Construct_NonPositiveBudget_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConsolidationRequest(1, null, "material", 0));
    }

    /// <summary>
    ///     Proves the composed prompt carries the previous record ahead of the new material, which is
    ///     the order the ratchet reads in: carry this forward, then fold this in.
    /// </summary>
    [Fact]
    public void ConsolidationPrompt_Compose_PlacesThePreviousRecordBeforeTheNewMaterial()
    {
        // Arrange: a consolidation extending an existing record
        var request = new ConsolidationRequest(1, "the earlier record", "the new material", 100);

        // Act: compose the text a model would be sent
        var composed = ConsolidationPrompt.Compose(request);

        // Assert: both are present, in the order the ratchet reads
        Assert.Contains("the earlier record", composed, StringComparison.Ordinal);
        Assert.Contains("the new material", composed, StringComparison.Ordinal);
        Assert.True(
            composed.IndexOf("the earlier record", StringComparison.Ordinal)
            < composed.IndexOf("the new material", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves a degradation says so explicitly rather than presenting an empty section a model
    ///     might try to fill from nothing.
    /// </summary>
    [Fact]
    public void ConsolidationPrompt_Compose_Degradation_StatesThereIsNoPreviousRecord()
    {
        // Arrange: a first recording into a tier
        var request = new ConsolidationRequest(2, null, "the new material", 100);

        // Act: compose the prompt
        var composed = ConsolidationPrompt.Compose(request);

        // Assert: the absence is stated
        Assert.Contains("PREVIOUS RECORD: none", composed, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the recommended instruction is always present, so a summarizer built on it always
    ///     receives the rules the arrangement depends on.
    /// </summary>
    [Fact]
    public void ConsolidationPrompt_Compose_AlwaysCarriesTheInstruction()
    {
        // Arrange / Act: any request
        var composed = ConsolidationPrompt.Compose(new ConsolidationRequest(1, null, "material", 100));

        // Assert: the published instruction is part of it
        Assert.StartsWith(ConsolidationPrompt.Instruction, composed, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the instruction asks a model for no number at all. Requesting a token budget does
    ///     not work — a model cannot count its own output — so the absence of any digit is asserted
    ///     mechanically rather than left to review.
    /// </summary>
    [Fact]
    public void ConsolidationPrompt_Instruction_NamesNoTargetSize()
    {
        Assert.DoesNotContain(ConsolidationPrompt.Instruction, char.IsDigit);
    }

    /// <summary>
    ///     Proves the instruction names each category of detail that must survive, because those
    ///     categories are the whole of what makes a consolidation worth keeping.
    /// </summary>
    /// <param name="required">A phrase the instruction must contain.</param>
    [Theory]
    [InlineData("path")]
    [InlineData("decision")]
    [InlineData("constraint")]
    [InlineData("error")]
    [InlineData("outstanding")]
    public void ConsolidationPrompt_Instruction_AsksForSpecificContent(string required)
    {
        Assert.True(
            ConsolidationPrompt.Instruction.Contains(required, StringComparison.Ordinal),
            $"The recommended instruction does not ask the summarizer to preserve '{required}'.");
    }

    /// <summary>
    ///     Proves a missing request is refused rather than composing a prompt about nothing.
    /// </summary>
    [Fact]
    public void ConsolidationPrompt_Compose_NullRequest_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ConsolidationPrompt.Compose(null!));
    }
}
