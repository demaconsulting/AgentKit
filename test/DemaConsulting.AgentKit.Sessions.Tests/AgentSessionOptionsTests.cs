using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="AgentSessionOptions"/>: the fixed-overhead measurement, the
///     effective window it leaves, and the construction-time assertion that the arrangement is
///     bounded.
/// </summary>
public class AgentSessionOptionsTests
{
    /// <summary>
    ///     Proves a session that configures nothing receives the published window and the default
    ///     compaction policy, with no fixed overhead when it has no prompt and no tools.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_Defaults_CarryThePublishedWindowAndPolicy()
    {
        // Arrange / Act: configure nothing but the summarizer
        var options = new AgentSessionOptions(new FakeSummarizer());

        // Assert: the published window, the shared default policy, and no overhead
        Assert.Equal(AgentSessionOptions.DefaultProviderWindowTokens, options.ProviderWindowTokens);
        Assert.Same(CompactionPolicy.Default, options.Compaction);
        Assert.Equal(0, options.FixedOverheadTokens);
        Assert.Empty(options.Tools);
    }

    /// <summary>
    ///     Proves the system prompt and the tool declarations are measured and subtracted before the
    ///     rotation percentage is applied. Applying the percentage to the raw window instead would
    ///     make the rotation point drift with how many tools an application attached.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_SubtractsFixedOverheadBeforeTheThreshold()
    {
        // Arrange: a measurable prompt and one tool, in a modest window
        var instructions = new string('i', 40 * TokenEstimator.CharactersPerToken);
        var tool = AIFunctionFactory.Create((string input) => input, "example_tool", "An example tool.");

        // Act: configure a session around them
        var options = new AgentSessionOptions(
            new FakeSummarizer(),
            instructions,
            [tool],
            providerWindowTokens: 20_000);

        // Assert: the prompt and declarations are charged, and the threshold is a fraction of what
        // is left rather than of the whole window
        Assert.Equal(40, options.SystemTokens);
        Assert.True(options.ToolDeclarationTokens > 0);
        Assert.Equal(options.SystemTokens + options.ToolDeclarationTokens, options.FixedOverheadTokens);
        Assert.Equal(20_000 - options.FixedOverheadTokens, options.EffectiveWindowTokens);
        Assert.Equal(
            (int)(options.EffectiveWindowTokens * CompactionPolicy.DefaultRotationThreshold),
            options.RotationThresholdTokens);
    }

    /// <summary>
    ///     Proves compaction cannot be configured away: without a summarizer a session would
    ///     silently never compact, which is the failure this package exists to prevent.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_NullSummarizer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentSessionOptions(null!));
    }

    /// <summary>
    ///     Proves a non-positive window is refused; there would be nothing to rotate within.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_NonPositiveWindow_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 0));
    }

    /// <summary>
    ///     Proves a window entirely consumed by the system prompt is refused, rather than producing a
    ///     session with no room for a conversation at all.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_OverheadConsumesTheWindow_Throws()
    {
        // Arrange: a prompt larger than the window it would be sent in
        var instructions = new string('i', 500 * TokenEstimator.CharactersPerToken);

        // Act / Assert: refused where the host configured it
        Assert.Throws<ArgumentException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), instructions, providerWindowTokens: 100));
    }

    /// <summary>
    ///     Proves the construction bound is asserted: a window that cannot hold the tier budgets is
    ///     refused, because such a session would rotate into a context already over budget and could
    ///     never converge.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_WindowSmallerThanTierBudgets_Throws()
    {
        // Arrange: the default policy needs 4,800 tokens of tier budget
        // Act / Assert: a 4,000-token window cannot accommodate it
        var exception = Assert.Throws<ArgumentException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 4_000));

        Assert.Contains("could never rotate back within its bound", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a window exactly large enough for the bound — the tier budgets plus the framing
    ///     their seeded records carry — is accepted, so the bound is a genuine boundary rather than
    ///     an approximation with hidden slack.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_WindowExactlyFitsTheBound_IsAccepted()
    {
        // Arrange: a window equal to the default policy's bound
        var bound = CompactionPolicy.Default.TotalTierBudgetTokens
            + ContextLayout.SeedFramingTokens(CompactionPolicy.Default);

        // Act: configure a session in exactly that window
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: bound);

        // Assert: accepted, with the whole window available for conversation
        Assert.Equal(bound, options.EffectiveWindowTokens);

        // Assert: one token less is refused, so the boundary is where it is claimed to be
        Assert.Throws<ArgumentException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: bound - 1));
    }

    /// <summary>
    ///     Proves a rotation threshold too small to survive truncation still leaves a positive token
    ///     threshold. A threshold of zero is satisfied by a conversation of no tokens at all, so the
    ///     session would be willing to rotate a context holding nothing — spending summarizer work
    ///     and a fresh provider session on material that does not exist.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_SubTokenThreshold_ClampsToOneToken()
    {
        // Arrange: a threshold whose product with the effective window is below one token
        var policy = new CompactionPolicy(rotationThreshold: 0.0001);

        // Act: configure a session around it
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 5_000, compaction: policy);

        // Assert: truncation cannot take the threshold below one token
        Assert.Equal(1, options.RotationThresholdTokens);
    }

    /// <summary>
    ///     Proves the tools are copied at construction and published as a read-only view. The
    ///     declaration tokens are measured once, so a caller able to add or remove a tool afterwards
    ///     would change what every future rotation seeds without changing the effective window or
    ///     the threshold derived from it.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_CopiesTheSuppliedTools()
    {
        // Arrange: a mutable tool list handed to the options
        var tool = AIFunctionFactory.Create((string input) => input, "example_tool", "An example tool.");
        var tools = new List<AIFunction> { tool };
        var options = new AgentSessionOptions(new FakeSummarizer(), tools: tools);

        // Act: mutate the caller's list afterwards
        tools.Clear();

        // Assert: the options are unaffected, and the list they publish cannot be written through
        Assert.Same(tool, Assert.Single(options.Tools));
        Assert.Throws<NotSupportedException>(() => ((IList<AIFunction>)options.Tools).Clear());
    }

    /// <summary>
    ///     Proves a null tool is refused rather than skipped, because a skipped declaration would
    ///     understate the fixed overhead and delay rotation past the point it was meant to fire.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_NullTool_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), tools: [null!]));
    }
}
