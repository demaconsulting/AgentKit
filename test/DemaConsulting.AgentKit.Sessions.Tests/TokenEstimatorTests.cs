using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="TokenEstimator"/>: the deterministic arithmetic every budget
///     comparison in the engine rests on.
/// </summary>
public class TokenEstimatorTests
{
    /// <summary>
    ///     Proves absent content is charged nothing, so an empty tier or a missing system prompt
    ///     does not consume budget.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateTokens_AbsentText_ReturnsZero()
    {
        // Arrange / Act: estimate both forms of absent content
        var nullEstimate = TokenEstimator.EstimateTokens(null);
        var emptyEstimate = TokenEstimator.EstimateTokens(string.Empty);

        // Assert: neither costs anything
        Assert.Equal(0, nullEstimate);
        Assert.Equal(0, emptyEstimate);
    }

    /// <summary>
    ///     Proves the character ratio is applied exactly, so a test can state a transcript's size
    ///     rather than approximate it.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateTokens_WholeMultiple_AppliesCharacterRatio()
    {
        // Arrange: exactly four tokens' worth of characters
        var text = new string('x', 4 * TokenEstimator.CharactersPerToken);

        // Act: estimate it
        var estimate = TokenEstimator.EstimateTokens(text);

        // Assert: four tokens, with no rounding applied
        Assert.Equal(4, estimate);
    }

    /// <summary>
    ///     Proves short content is never free: rounding up is what stops an unbounded number of
    ///     one-character entries accumulating inside a budget that thinks they cost nothing.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateTokens_PartialToken_RoundsUp()
    {
        // Arrange / Act: one character, far short of a whole token
        var estimate = TokenEstimator.EstimateTokens("x");

        // Assert: charged a whole token
        Assert.Equal(1, estimate);
    }

    /// <summary>
    ///     Proves an entry is charged for its framing as well as its text, so a long run of small
    ///     entries is not systematically under-counted.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateEntryTokens_AddsFramingAllowance()
    {
        // Arrange: an entry whose text is exactly two tokens
        var entry = TranscriptEntry.User(new string('x', 2 * TokenEstimator.CharactersPerToken));

        // Act: estimate the entry rather than the text
        var estimate = TokenEstimator.EstimateEntryTokens(entry);

        // Assert: the text plus the flat framing allowance
        Assert.Equal(2 + TokenEstimator.PerEntryOverheadTokens, estimate);
    }

    /// <summary>
    ///     Proves a missing entry is refused rather than estimated as zero, which would silently
    ///     understate a transcript.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateEntryTokens_NullEntry_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TokenEstimator.EstimateEntryTokens(null!));
    }

    /// <summary>
    ///     Proves an agent with no tools carries no declaration overhead, so the effective window is
    ///     not reduced for capability it does not have.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateToolDeclarationTokens_NoTools_ReturnsZero()
    {
        // Arrange / Act: both ways of having no tools
        var nullEstimate = TokenEstimator.EstimateToolDeclarationTokens(null);
        var emptyEstimate = TokenEstimator.EstimateToolDeclarationTokens([]);

        // Assert: no fixed overhead in either case
        Assert.Equal(0, nullEstimate);
        Assert.Equal(0, emptyEstimate);
    }

    /// <summary>
    ///     Proves declarations are charged, and charged more as more tools are attached: this is the
    ///     fixed overhead subtracted before any rotation percentage is applied.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateToolDeclarationTokens_GrowsWithToolCount()
    {
        // Arrange: one tool, then the same tool twice
        var tool = MakeTool("example_tool");

        // Act: estimate both sets
        var one = TokenEstimator.EstimateToolDeclarationTokens([tool]);
        var two = TokenEstimator.EstimateToolDeclarationTokens([tool, tool]);

        // Assert: a declaration costs something, and two cost exactly twice one
        Assert.True(one > 0);
        Assert.Equal(one * 2, two);
    }

    /// <summary>
    ///     Proves a null tool is refused rather than skipped, because a skipped declaration would
    ///     understate the fixed overhead and delay rotation.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateToolDeclarationTokens_NullTool_Throws()
    {
        Assert.Throws<ArgumentException>(() => TokenEstimator.EstimateToolDeclarationTokens([null!]));
    }

    /// <summary>
    ///     Builds a tool carrying a name, a description and a schema, as a real declaration does.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>A callable tool suitable for declaration estimation.</returns>
    private static AIFunction MakeTool(string name) =>
        AIFunctionFactory.Create(
            (string input) => input,
            name,
            "A tool used only to give the estimator a realistic declaration to measure.");
}
