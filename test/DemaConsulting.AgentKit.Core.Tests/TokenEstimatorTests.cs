using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for <see cref="TokenEstimator"/>: the deterministic fallback estimate used when a
///     provider reports no usage and when the session sizes a seed it has not yet sent.
/// </summary>
public class TokenEstimatorTests
{
    /// <summary>
    ///     Proves absent content estimates as zero, which is the honest answer.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateTokens_AbsentContent_IsZero()
    {
        Assert.Equal(0, TokenEstimator.EstimateTokens(null));
        Assert.Equal(0, TokenEstimator.EstimateTokens(string.Empty));
    }

    /// <summary>
    ///     Proves the estimate rounds up, so short-but-present content is never charged zero.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateTokens_RoundsUp()
    {
        Assert.Equal(1, TokenEstimator.EstimateTokens("a"));
        Assert.Equal(1, TokenEstimator.EstimateTokens(new string('a', TokenEstimator.CharactersPerToken)));
        Assert.Equal(2, TokenEstimator.EstimateTokens(new string('a', TokenEstimator.CharactersPerToken + 1)));
    }

    /// <summary>
    ///     Proves an entry is charged the per-entry framing beyond the characters it carries.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateEntryTokens_IncludesFraming()
    {
        var entry = TranscriptEntry.User(new string('a', TokenEstimator.CharactersPerToken));

        Assert.Equal(1 + TokenEstimator.PerEntryOverheadTokens, TokenEstimator.EstimateEntryTokens(entry));
    }

    /// <summary>
    ///     Proves no tools estimate as no fixed overhead.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateToolDeclarations_None_IsZero()
    {
        Assert.Equal(0, TokenEstimator.EstimateToolDeclarationTokens(null));
        Assert.Equal(0, TokenEstimator.EstimateToolDeclarationTokens([]));
    }

    /// <summary>
    ///     Proves each declaration is charged its text plus a per-tool structural allowance.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateToolDeclarations_ChargesEachTool()
    {
        var tool = AIFunctionFactory.Create(() => 0, "probe", "A probe tool.");

        var estimate = TokenEstimator.EstimateToolDeclarationTokens([tool]);

        Assert.True(estimate >= TokenEstimator.PerToolOverheadTokens);
    }

    /// <summary>
    ///     Proves a null declaration is refused rather than dereferenced.
    /// </summary>
    [Fact]
    public void TokenEstimator_EstimateToolDeclarations_NullEntry_Throws()
    {
        Assert.Throws<ArgumentException>(() => TokenEstimator.EstimateToolDeclarationTokens([null!]));
    }
}
