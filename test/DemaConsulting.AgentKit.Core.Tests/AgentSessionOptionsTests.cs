using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for <see cref="AgentSessionOptions"/>: what an application configures about a
///     session, and the one derivation the options still perform.
/// </summary>
public class AgentSessionOptionsTests
{
    /// <summary>
    ///     Proves the options carry what the application configured, and hold the tools in storage
    ///     of their own so a later mutation of the caller's list cannot change what every future
    ///     rotation seeds.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_CarriesConfiguration()
    {
        // Arrange: a summarizer and a mutable tool list the caller keeps hold of
        var summarizer = new FakeSummarizer();
        var tools = new List<AIFunction> { AIFunctionFactory.Create(() => 0, "probe", "A probe tool.") };

        // Act: configure a session and then mutate the list that was passed in
        var options = new AgentSessionOptions(
            summarizer,
            instructions: "You are a helpful assistant.",
            tools: tools,
            verbatimTurns: 8);
        tools.Clear();

        // Assert: the configuration is carried, and the tools are the options' own copy
        Assert.Same(summarizer, options.Summarizer);
        Assert.Equal("You are a helpful assistant.", options.Instructions);
        Assert.Single(options.Tools);
        Assert.Equal(8, options.VerbatimTurns);
    }

    /// <summary>
    ///     Proves the defaults an application receives when it configures nothing but a summarizer,
    ///     and that the published default is the one it actually gets.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_Defaults()
    {
        // Arrange / Act: configure only the one required collaborator
        var options = new AgentSessionOptions(new FakeSummarizer());

        // Assert: no instructions, no tools, and the published verbatim tail
        Assert.Null(options.Instructions);
        Assert.Empty(options.Tools);
        Assert.Equal(20, AgentSessionOptions.DefaultVerbatimTurns);
        Assert.Equal(AgentSessionOptions.DefaultVerbatimTurns, options.VerbatimTurns);
    }

    /// <summary>
    ///     Proves a non-positive verbatim tail is refused: a tail of zero would keep no recent
    ///     history verbatim and rotate everything the moment it arrived.
    /// </summary>
    /// <param name="verbatimTurns">The rejected verbatim tail length.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AgentSessionOptions_Construct_NonPositiveVerbatimTurns_Throws(int verbatimTurns)
    {
        // Act / Assert: a tail that keeps nothing is refused where it was configured
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), verbatimTurns: verbatimTurns));
    }

    /// <summary>
    ///     Proves the rotation threshold is the published fraction of the effective window, and is
    ///     never below one token however small that window is.
    /// </summary>
    /// <remarks>
    ///     A fraction of a tiny window truncates to zero, and a threshold of zero means "the
    ///     conversation is already over budget while holding nothing" — a session that rotates
    ///     forever without ever aging anything out. The floor turns that into "rotate on every
    ///     turn", which at least makes progress.
    /// </remarks>
    [Fact]
    public void AgentSessionOptions_RotationThresholdFor_IsTheFractionAndNeverBelowOne()
    {
        // Act / Assert: the ordinary case is the published fraction, truncated
        Assert.Equal(700, AgentSessionOptions.RotationThresholdFor(1000));
        Assert.Equal(7, AgentSessionOptions.RotationThresholdFor(10));

        // Assert: a window too small for the fraction to survive truncation still yields one token
        Assert.Equal(1, AgentSessionOptions.RotationThresholdFor(1));
        Assert.Equal(1, AgentSessionOptions.RotationThresholdFor(0));
    }

    /// <summary>
    ///     Proves a null summarizer is refused: compaction cannot happen without one, and defaulting
    ///     it would give a session that silently never compacts.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_NullSummarizer_Throws()
    {
        // Act / Assert: the one required collaborator cannot be omitted
        Assert.Throws<ArgumentNullException>(() => new AgentSessionOptions(null!));
    }

    /// <summary>
    ///     Proves a null tool declaration is refused.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_NullTool_Throws()
    {
        // Act / Assert: a hole in the tool list is a composition defect, refused where it was written
        Assert.Throws<ArgumentException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), tools: [null!]));
    }
}
