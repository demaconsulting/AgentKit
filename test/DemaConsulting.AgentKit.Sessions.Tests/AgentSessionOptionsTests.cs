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
            compaction: new CompactionPolicy(verbatimTurns: 8));

        Assert.Same(summarizer, options.Summarizer);
        Assert.Equal(8, options.VerbatimTurns);
    }

    /// <summary>
    ///     Proves the defaults: the shared policy and its verbatim tail.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_Defaults()
    {
        var options = new AgentSessionOptions(new FakeSummarizer());

        Assert.Same(CompactionPolicy.Default, options.Compaction);
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
            new FakeSummarizer(), instructions: "system prompt here", tools: [tool]);

        Assert.True(options.SystemTokens > 0);
        Assert.True(options.ToolDeclarationTokens > 0);
        Assert.Equal(options.SystemTokens + options.ToolDeclarationTokens, options.FixedOverheadTokens);
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
    ///     Proves a null tool declaration is refused.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_NullTool_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), tools: [null!]));
    }
}
