using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="AgentSessionOptions"/>: the application's own configuration and the
///     validation of its own numbers.
/// </summary>
public class AgentSessionOptionsTests
{
    /// <summary>
    ///     Proves the options carry what the application configured, and derive the effective window
    ///     and rotation threshold from it.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_CarriesConfiguration()
    {
        var summarizer = new FakeSummarizer();

        var options = new AgentSessionOptions(
            summarizer,
            instructions: null,
            tools: null,
            providerWindowTokens: 1000,
            compaction: new CompactionPolicy(verbatimTurns: 8));

        Assert.Same(summarizer, options.Summarizer);
        Assert.Equal(1000, options.ProviderWindowTokens);
        Assert.Equal(1000, options.EffectiveWindowTokens);
        Assert.Equal(700, options.RotationThresholdTokens);
        Assert.Equal(8, options.VerbatimTurns);
    }

    /// <summary>
    ///     Proves the defaults: the shared policy, the default window, and its verbatim tail.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_Defaults()
    {
        var options = new AgentSessionOptions(new FakeSummarizer());

        Assert.Same(CompactionPolicy.Default, options.Compaction);
        Assert.Equal(AgentSessionOptions.DefaultProviderWindowTokens, options.ProviderWindowTokens);
        Assert.Equal(CompactionPolicy.DefaultVerbatimTurns, options.VerbatimTurns);
    }

    /// <summary>
    ///     Proves the fixed overhead is measured from the instructions and tool declarations and
    ///     subtracted before the rotation percentage is applied.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_MeasuresFixedOverhead()
    {
        var tool = AIFunctionFactory.Create(() => 0, "probe", "A probe tool.");

        var options = new AgentSessionOptions(
            new FakeSummarizer(), instructions: "system prompt here", tools: [tool], providerWindowTokens: 100_000);

        Assert.True(options.SystemTokens > 0);
        Assert.True(options.ToolDeclarationTokens > 0);
        Assert.Equal(options.SystemTokens + options.ToolDeclarationTokens, options.FixedOverheadTokens);
        Assert.Equal(options.ProviderWindowTokens - options.FixedOverheadTokens, options.EffectiveWindowTokens);
    }

    /// <summary>
    ///     Proves a null summarizer is refused: compaction cannot happen without one, and defaulting
    ///     it would give a session that silently never compacts.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_NullSummarizer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentSessionOptions(null!));
    }

    /// <summary>
    ///     Proves a non-positive window, and a window the overhead leaves no room in, are refused
    ///     where the application wrote them.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_UnworkableWindow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 0));

        Assert.Throws<ArgumentException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), instructions: new string('a', 4000), providerWindowTokens: 10));
    }

    /// <summary>
    ///     Proves a null tool declaration is refused.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_NullTool_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), tools: [null!]));
    }
}
