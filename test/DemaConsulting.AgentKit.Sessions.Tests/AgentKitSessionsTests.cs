namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     System-level tests for AgentKitSessions: a long-running conversation that compacts itself,
///     exercised end to end through the in-memory provider and a deterministic summarizer.
/// </summary>
/// <remarks>
///     These tests assert the system's promise rather than any one unit's behavior: that a session
///     can run far past its provider's window, that it stays within the bound its configuration
///     declared, that detail from early in the conversation is still carried in the context it
///     sends, and that a context which can no longer be reduced says so instead of rotating
///     forever. No provider is contacted and no model is used.
/// </remarks>
public class AgentKitSessionsTests
{
    /// <summary>
    ///     Proves a session outlives its provider's window: a conversation far larger than the window
    ///     keeps answering, rotating repeatedly, with each rotation replacing the provider session.
    /// </summary>
    [Fact]
    public async Task AgentKitSessions_LongConversation_RotatesRepeatedlyAndKeepsAnswering()
    {
        // Arrange: a small window, a small policy, and turns large enough to fill it quickly
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn(new string('r', 40 * TokenEstimator.CharactersPerToken)),
            windowTokens: 300);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.1), providerWindowTokens: 300, compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        var message = new string('m', 40 * TokenEstimator.CharactersPerToken);

        // Act: run far more conversation than the window could ever hold at once
        for (var turn = 0; turn < 20; turn++)
        {
            var response = await session.SendAsync($"{turn}-{message}", TestContext.Current.CancellationToken);
            Assert.NotEmpty(response.Text);
        }

        // Assert: it rotated many times, and created one provider session per rotation plus the first
        Assert.True(session.RotationCount >= 5, $"Expected repeated rotation, saw {session.RotationCount}.");
        Assert.Equal(session.RotationCount + 1, factory.Sessions.Count);

        // Assert: every superseded provider session was released, and only the live one remains
        Assert.All(factory.Sessions.Take(factory.Sessions.Count - 1), s => Assert.True(s.IsDisposed));
        Assert.False(factory.Sessions[^1].IsDisposed);
    }

    /// <summary>
    ///     Proves the arrangement is bounded by construction over a whole conversation: after every
    ///     rotation the context sits within the fixed overhead plus the sum of the tier budgets, no
    ///     matter how long the session runs.
    /// </summary>
    [Fact]
    public async Task AgentKitSessions_LongConversation_StaysWithinItsConstructionBound()
    {
        // Arrange: a session whose bound is small enough to be violated if rotation misbehaved
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn(new string('r', 40 * TokenEstimator.CharactersPerToken)),
            windowTokens: 300);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.1), providerWindowTokens: 300, compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        var message = new string('m', 40 * TokenEstimator.CharactersPerToken);

        // Act / Assert: check the bound at the moment of every rotation
        for (var turn = 0; turn < 20; turn++)
        {
            var response = await session.SendAsync($"{turn}-{message}", TestContext.Current.CancellationToken);
            if (response.RotationOccurred)
            {
                Assert.True(
                    session.Layout.IsWithinBound,
                    $"After rotation {session.RotationCount} the context held "
                    + $"{session.Layout.TotalEstimatedTokens} tokens against a bound of "
                    + $"{session.Layout.MaximumBoundTokens}.");
            }
        }

        Assert.True(session.RotationCount > 0);
    }

    /// <summary>
    ///     Proves detail from early in a conversation is still present in what the session sends
    ///     after many rotations. This is the property the tiered arrangement exists for: a flat
    ///     rolling summary re-summarizes its own summary and loses old material entirely, whereas
    ///     each consolidation here carries the previous record forward as an input.
    /// </summary>
    [Fact]
    public async Task AgentKitSessions_AfterManyRotations_EarlyDetailIsStillCarriedInContext()
    {
        // Arrange: a summarizer that keeps everything it is given, so what survives is decided by
        // the tier arrangement rather than by a model's discretion
        var summarizer = new FakeSummarizer(request =>
            string.IsNullOrEmpty(request.PreviousRecord)
                ? request.Material
                : request.PreviousRecord + "\n" + request.Material);
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn("noted"),
            windowTokens: 300);
        var options = new AgentSessionOptions(
            summarizer, providerWindowTokens: 300, compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: state a distinctive fact first, then bury it under many later turns
        await session.SendAsync("The deployment key lives at /etc/secrets/deploy.key", TestContext.Current.CancellationToken);
        for (var turn = 0; turn < 30; turn++)
        {
            await session.SendAsync($"Routine step {turn} with some padding "
                + new string('p', 30 * TokenEstimator.CharactersPerToken),
                TestContext.Current.CancellationToken);
        }

        // Assert: the session rotated repeatedly, and the early detail is still in what it sends
        Assert.True(session.RotationCount >= 5, $"Expected repeated rotation, saw {session.RotationCount}.");
        var seeded = string.Join("\n", session.Layout.BuildSeed().Select(entry => entry.Text));
        Assert.Contains("/etc/secrets/deploy.key", seeded, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a context that can no longer be reduced reports saturation rather than rotating
    ///     forever for no gain. Without detection the failure is invisible: every rotation appears to
    ///     succeed while buying no room.
    /// </summary>
    [Fact]
    public async Task AgentKitSessions_ContextWithNoRedundancyLeft_ReportsSaturation()
    {
        // Arrange: a summarizer that cannot reduce what it is given
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn(new string('r', 40 * TokenEstimator.CharactersPerToken)),
            windowTokens: 300);
        var options = new AgentSessionOptions(
            new FakeSummarizer(request => request.Material),
            providerWindowTokens: 300,
            compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        var message = new string('m', 40 * TokenEstimator.CharactersPerToken);

        // Act: run until the first rotation occurs
        AgentSessionResponse? rotated = null;
        for (var turn = 0; turn < 10 && rotated is null; turn++)
        {
            var response = await session.SendAsync($"{turn}-{message}", TestContext.Current.CancellationToken);
            if (response.RotationOccurred)
            {
                rotated = response;
            }
        }

        // Assert: the rotation reported that it could not reduce, naming the tier and the figures
        Assert.NotNull(rotated);
        Assert.True(rotated.IsSaturated);
        Assert.Contains(rotated.Saturations, signal => signal.TierIndex == 1);
    }

    /// <summary>
    ///     Proves the engine behaves the same way on both provider families — one reporting its own
    ///     usage, one reporting nothing. Both compact, both stay within their construction bound, and
    ///     both keep the same shape of context.
    /// </summary>
    /// <remarks>
    ///     <b>Rotation counts are deliberately not asserted equal.</b> A provider's own figures count
    ///     framing this library never sees — the envelope around each seeded record, for one — so a
    ///     reporting provider legitimately crosses the threshold sooner than the engine's own
    ///     estimate does. That difference is a true account of the two providers rather than a defect,
    ///     and asserting it away would mean preferring an estimate over a measurement. What must hold
    ///     on both is that compaction happens, that the bound is respected, and that the surviving
    ///     context still carries consolidated records ahead of verbatim turns.
    /// </remarks>
    [Fact]
    public async Task AgentKitSessions_SameConversation_CompactsAndStaysBoundedOnBothProviderShapes()
    {
        // Arrange / Act: run the identical conversation against a reporting and a silent provider
        var reporting = await RunConversationAsync(reportsUsage: true);
        var silent = await RunConversationAsync(reportsUsage: false);

        // Assert: each used the usage source its provider offered
        Assert.Equal(ContextUsageOrigin.Provider, reporting.Origin);
        Assert.Equal(ContextUsageOrigin.Estimated, silent.Origin);

        // Assert: both compacted, both stayed within their bound, and both kept the same shape
        foreach (var run in new[] { reporting, silent })
        {
            Assert.True(run.Rotations > 0);
            Assert.True(run.WithinBound);
            Assert.Contains("Consolidated record of earlier work", run.Context, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Runs a fixed conversation against the in-memory provider and reports what survived.
    /// </summary>
    /// <remarks>
    ///     Shared by the cross-provider comparison so that the only difference between the two runs
    ///     is whether the provider reports its own usage.
    /// </remarks>
    /// <param name="reportsUsage">Whether the provider reports its own context usage.</param>
    /// <returns>The rotation count, the usage origin, whether the bound held, and the surviving context.</returns>
    private static async Task<(int Rotations, ContextUsageOrigin Origin, bool WithinBound, string Context)>
        RunConversationAsync(bool reportsUsage)
    {
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn(new string('r', 40 * TokenEstimator.CharactersPerToken)),
            windowTokens: 300,
            reportsUsage: reportsUsage);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.1), providerWindowTokens: 300, compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        for (var turn = 0; turn < 12; turn++)
        {
            await session.SendAsync(
                $"{turn}-" + new string('m', 40 * TokenEstimator.CharactersPerToken),
                TestContext.Current.CancellationToken);
        }

        return (
            session.RotationCount,
            session.Usage.Origin,
            session.Layout.IsWithinBound,
            string.Join("\n", session.Layout.BuildSeed().Select(entry => entry.Text)));
    }
}
