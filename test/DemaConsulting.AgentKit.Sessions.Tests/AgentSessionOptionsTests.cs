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
    ///     Proves the convergence invariant is asserted: a window in which a rotated context could
    ///     not land below the rotation threshold is refused, because such a session rotates on
    ///     nearly every turn without ever settling.
    /// </summary>
    [Fact]
    public void AgentSessionOptions_Construct_WindowSmallerThanTierBudgets_Throws()
    {
        // Arrange: the default policy needs 4,800 tokens of tier budget
        // Act / Assert: a 4,000-token window cannot accommodate it
        var exception = Assert.Throws<ArgumentException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 4_000));

        Assert.Contains("cannot converge with this policy", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the accepted boundary is the window at which the session <em>converges</em>, not
    ///     merely the one at which a rotated context <em>fits</em>.
    /// </summary>
    /// <remarks>
    ///     <b>These are different numbers, and asserting the wrong one was the defect.</b> A window
    ///     equal to the construction bound holds a rotated context exactly — and leaves it sitting
    ///     at 100 percent of a window whose rotation threshold is 70 percent, so the very next turn
    ///     rotates again, and so does every turn after it, silently, because each individual
    ///     consolidation reduces perfectly normally and raises no saturation signal. Convergence
    ///     requires the rotated context to land <em>below</em> the threshold, which takes a window
    ///     larger than the bound divided by the rotation fraction.
    /// </remarks>
    [Fact]
    public void AgentSessionOptions_Construct_WindowBelowTheConvergencePoint_IsRefused()
    {
        // Arrange: the bound - what a rotated context occupies - and the threshold it must clear
        var policy = CompactionPolicy.Default;
        var bound = policy.TotalTierBudgetTokens + ContextLayout.SeedFramingTokens(policy);

        // Act / Assert: a window that merely holds the bound is refused, because a rotation lands on
        // the threshold rather than below it
        var exception = Assert.Throws<ArgumentException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: bound));
        Assert.Contains("cannot converge with this policy", exception.Message, StringComparison.Ordinal);

        // Assert: the smallest accepted window is the one at which the threshold first exceeds the
        // bound, and one token less is refused - so the boundary is exactly where it is claimed
        var minimum = (int)Math.Ceiling((bound + 1) / policy.RotationThreshold);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: minimum);
        Assert.True(
            options.RotationThresholdTokens > bound,
            $"A rotated context of {bound} tokens must land below the threshold of "
            + $"{options.RotationThresholdTokens}.");

        Assert.Throws<ArgumentException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: minimum - 1));
    }

    /// <summary>
    ///     Proves a rotation fraction too small for any rotated context to land below is refused
    ///     outright rather than quietly turned into a session that rotates on every turn.
    /// </summary>
    /// <remarks>
    ///     A threshold that truncates to nothing is satisfied by a conversation of no tokens at all,
    ///     so the session would be willing to rotate a context holding nothing — spending summarizer
    ///     work and a fresh provider session on material that does not exist. Refusing the
    ///     configuration where the host wrote it is the repair; clamping the threshold to one token
    ///     only made the symptom less extreme.
    /// </remarks>
    [Fact]
    public void AgentSessionOptions_Construct_SubTokenThreshold_IsRefused()
    {
        // Arrange: a threshold whose product with the effective window is below one token
        var policy = new CompactionPolicy(rotationThreshold: 0.0001);

        // Act / Assert: refused, because no rotated context could ever land below it
        var exception = Assert.Throws<ArgumentException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 5_000, compaction: policy));

        Assert.Contains("cannot converge with this policy", exception.Message, StringComparison.Ordinal);
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

    /// <summary>
    ///     Proves a policy whose rotated context would occupy exactly the largest representable
    ///     token count reports the window it would need rather than a defect inside the helper that
    ///     computes it.
    /// </summary>
    /// <remarks>
    ///     <b><c>CompactionPolicy</c> permits this bound.</b> It refuses budgets and framing summing
    ///     <em>past</em> a token count and accepts a sum of exactly <c>int.MaxValue</c>, so the
    ///     minimum-window helper must answer for one. It did not: the lower bound it handed
    ///     <c>Math.Clamp</c> was the bound plus one, above the clamp's upper bound, and
    ///     <c>Math.Clamp</c> throws an argument error rather than saturating — so a valid policy
    ///     reported an internal clamp failure instead of the non-convergent window the caller had
    ///     actually asked about. The requirement saturates at <c>int.MaxValue</c>, which is the
    ///     honest answer: no representable window converges with such a policy.
    /// </remarks>
    [Fact]
    public void AgentSessionOptions_Construct_PolicyBoundAtTheLargestTokenCount_ReportsTheWindowItWouldNeed()
    {
        // Arrange: two budgets whose sum plus the framing of their seeded records is exactly
        // int.MaxValue - the largest bound a policy is allowed to carry
        var framing = (int)ContextLayout.SeedFramingTokens(2);
        var policy = new CompactionPolicy([int.MaxValue - framing - 1, 1]);
        Assert.Equal(
            int.MaxValue,
            (long)policy.TotalTierBudgetTokens + ContextLayout.SeedFramingTokens(policy));

        // Act / Assert: the minimum window saturates rather than throwing out of the clamp
        Assert.Equal(int.MaxValue, AgentSessionOptions.MinimumEffectiveWindowTokens(policy));

        // Act / Assert: and the configuration is refused for the reason the host can act on
        var exception = Assert.Throws<ArgumentException>(() =>
            new AgentSessionOptions(new FakeSummarizer(), compaction: policy));

        Assert.Equal("compaction", exception.ParamName);
        Assert.Contains("cannot converge with this policy", exception.Message, StringComparison.Ordinal);
    }
}
