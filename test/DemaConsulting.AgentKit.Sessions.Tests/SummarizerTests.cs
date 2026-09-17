namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="ConsolidationRequest"/> and <see cref="ConsolidationPrompt"/>: the
///     material handed to a summarizer, the aggressiveness clause, and how a request composes into a
///     prompt.
/// </summary>
public class SummarizerTests
{
    /// <summary>
    ///     Proves a request carries the tier, the material and the aggressiveness clause, and no
    ///     length or previous record.
    /// </summary>
    [Fact]
    public void ConsolidationRequest_Construct_CarriesTierMaterialAndInstruction()
    {
        var request = new ConsolidationRequest(2, "USER: hello", "Be terse.");

        Assert.Equal(2, request.TierIndex);
        Assert.Equal("USER: hello", request.Material);
        Assert.Equal("Be terse.", request.Instruction);
    }

    /// <summary>
    ///     Proves a tier index below one is refused: the verbatim tail is tier zero and is never
    ///     consolidated into.
    /// </summary>
    [Fact]
    public void ConsolidationRequest_Construct_TierBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConsolidationRequest(0, "material", "instruction"));
    }

    /// <summary>
    ///     Proves blank material or a blank instruction is refused; there is nothing to consolidate
    ///     without material, and no aggressiveness without an instruction.
    /// </summary>
    /// <param name="material">The material to supply.</param>
    /// <param name="instruction">The instruction to supply.</param>
    [Theory]
    [InlineData("   ", "instruction")]
    [InlineData("material", "   ")]
    public void ConsolidationRequest_Construct_Blank_Throws(string material, string instruction)
    {
        Assert.Throws<ArgumentException>(() => new ConsolidationRequest(1, material, instruction));
    }

    /// <summary>
    ///     Proves the composed prompt carries the base instruction, the aggressiveness clause and the
    ///     material, and has no previous-record section — the material is a set of peers.
    /// </summary>
    [Fact]
    public void ConsolidationPrompt_Compose_HasNoPreviousRecordSection()
    {
        var request = new ConsolidationRequest(1, "USER: hi", ConsolidationPrompt.LowInstruction);

        var composed = ConsolidationPrompt.Compose(request);

        Assert.Contains(ConsolidationPrompt.Instruction, composed, StringComparison.Ordinal);
        Assert.Contains(ConsolidationPrompt.LowInstruction, composed, StringComparison.Ordinal);
        Assert.Contains("USER: hi", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("PREVIOUS RECORD", composed, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the base prompt licenses collapsing repetition, which is what makes a consolidation
    ///     buy room, and still names the specific categories that must survive.
    /// </summary>
    [Fact]
    public void ConsolidationPrompt_Instruction_LicensesCollapsingRepetition()
    {
        Assert.Contains("Collapse repetition", ConsolidationPrompt.Instruction, StringComparison.Ordinal);
        Assert.Contains("decision", ConsolidationPrompt.Instruction, StringComparison.Ordinal);
        Assert.Contains("error", ConsolidationPrompt.Instruction, StringComparison.Ordinal);
        Assert.Contains("outstanding", ConsolidationPrompt.Instruction, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves each compaction level selects its own aggressiveness clause, and none is a number.
    /// </summary>
    [Fact]
    public void ConsolidationPrompt_InstructionFor_SelectsPerLevelClause()
    {
        Assert.Equal(ConsolidationPrompt.LowInstruction, ConsolidationPrompt.InstructionFor(CompactionLevel.Low));
        Assert.Equal(ConsolidationPrompt.MediumInstruction, ConsolidationPrompt.InstructionFor(CompactionLevel.Medium));
        Assert.Equal(ConsolidationPrompt.HighInstruction, ConsolidationPrompt.InstructionFor(CompactionLevel.High));
    }

    /// <summary>
    ///     Proves composing a null request is refused.
    /// </summary>
    [Fact]
    public void ConsolidationPrompt_Compose_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ConsolidationPrompt.Compose(null!));
    }
}
