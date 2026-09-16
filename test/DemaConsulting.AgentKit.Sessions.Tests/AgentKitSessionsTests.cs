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
            windowTokens: SessionTestData.ConvergentWindowTokens);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.1), providerWindowTokens: SessionTestData.ConvergentWindowTokens, compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        var message = new string('m', 40 * TokenEstimator.CharactersPerToken);

        // Act: run far more conversation than the window could ever hold at once
        for (var turn = 0; turn < 20; turn++)
        {
            var response = await session.SendAsync($"{turn}-{message}", TestContext.Current.CancellationToken);
            Assert.NotEmpty(response.Text);
        }

        // Assert: it compacted repeatedly, and it converged while doing so. A lower bound alone
        // cannot tell the two apart - a session rotating on every single turn satisfies "at least
        // five rotations in twenty turns" just as comfortably as a healthy one, which is exactly how
        // a non-converging configuration went unnoticed. The upper bound is what carries the claim.
        Assert.InRange(session.RotationCount, 3, 10);
        Assert.Equal(session.RotationCount + 1, factory.Sessions.Count);

        // Assert: every superseded provider session was released, and only the live one remains
        Assert.All(factory.Sessions.Take(factory.Sessions.Count - 1), s => Assert.True(s.IsDisposed));
        Assert.False(factory.Sessions[^1].IsDisposed);
    }

    /// <summary>
    ///     Proves the arrangement is bounded by construction over a whole conversation: after every
    ///     rotation the context sits within the fixed overhead plus the sum of the tier budgets, no
    ///     matter how long the session runs — and that the session settles between rotations rather
    ///     than rotating on every turn.
    /// </summary>
    /// <remarks>
    ///     <b>The bound and the settling are one claim, not two.</b> The requirement this test
    ///     carries justifies refusing an unworkable window on the grounds that it "would rotate into
    ///     a context already over budget and could never converge", so verifying only the bound
    ///     leaves the second half of that sentence unverified. A session that rotates on every turn
    ///     satisfies the bound perfectly — it is inside the bound at every single rotation — while
    ///     spending a summarizer call and a provider session per turn to achieve it.
    /// </remarks>
    [Fact]
    public async Task AgentKitSessions_LongConversation_StaysWithinItsConstructionBound()
    {
        // Arrange: a session whose bound is small enough to be violated if rotation misbehaved, and
        // a summarizer that fills every tier to its budget - the steady state a real session reaches
        // once there is no redundancy left to remove, and the only one that can show whether a
        // rotated context lands below the threshold or on top of it
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn(new string('r', 40 * TokenEstimator.CharactersPerToken)),
            windowTokens: SessionTestData.ConvergentWindowTokens);
        var options = new AgentSessionOptions(
            FakeSummarizer.Filling(),
            providerWindowTokens: SessionTestData.ConvergentWindowTokens,
            compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        var message = new string('m', 40 * TokenEstimator.CharactersPerToken);

        // Act / Assert: check the bound at the moment of every rotation, and record how many turns
        // the session managed between one rotation and the next
        var turnsSinceRotation = 0;
        var consecutiveRotations = 0;
        for (var turn = 0; turn < 40; turn++)
        {
            var response = await session.SendAsync($"{turn}-{message}", TestContext.Current.CancellationToken);
            if (!response.RotationOccurred)
            {
                turnsSinceRotation++;
                continue;
            }

            Assert.True(
                session.Layout.IsWithinBound,
                $"After rotation {session.RotationCount} the context held "
                + $"{session.Layout.TotalEstimatedTokens} tokens against a bound of "
                + $"{session.Layout.MaximumBoundTokens}.");

            if (turnsSinceRotation == 0)
            {
                consecutiveRotations++;
            }

            turnsSinceRotation = 0;
        }

        // Assert: it compacted, and it settled. A rotation immediately followed by another is the
        // signature of a configuration that rotates into a context still above its own threshold -
        // which the guards now refuse, and which this asserts is in fact what they deliver.
        Assert.True(session.RotationCount > 0);
        Assert.Equal(0, consecutiveRotations);
        Assert.InRange(session.RotationCount, 3, 20);
    }

    /// <summary>
    ///     Proves the session converges in the steady state a real conversation reaches: with every
    ///     tier filled to its budget, rotations stay occasional rather than becoming a per-turn tax.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>This is the test whose absence hid a convergence defect for five rounds.</b> Every
    ///     other rotation test in this repository used a compressing summarizer — 25 percent in the
    ///     unit tests, 10 percent here — so the tiers never approached their budgets and the context
    ///     a rotation landed on was a small fraction of the one the configuration actually permits.
    ///     The layout that thrashes is the full one, and no test could reach it.
    ///     </para>
    ///     <para>
    ///     Measured against the guard as it stood before this round, with the policy and window
    ///     these tests used — tier budgets <c>[100, 60, 40, 30]</c>, a rotated context of 311 tokens,
    ///     a 400-token window — this configuration rotated on nearly every turn while reporting no
    ///     saturation at all, because every individual consolidation reduced perfectly normally. The
    ///     window is now 600, which is what the convergence invariant actually requires.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AgentKitSessions_SummarizerFillsEveryTier_StillConvergesBetweenRotations()
    {
        // Arrange: a summarizer that fills each tier to its budget exactly - no redundancy left to
        // remove, but nothing over budget either, so this is a session the library claims settles
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn(new string('r', 40 * TokenEstimator.CharactersPerToken)),
            windowTokens: SessionTestData.ConvergentWindowTokens);
        var options = new AgentSessionOptions(
            FakeSummarizer.Filling(),
            providerWindowTokens: SessionTestData.ConvergentWindowTokens,
            compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        var message = new string('m', 40 * TokenEstimator.CharactersPerToken);

        // Act: a long conversation, counting any rotation that immediately follows another
        var turnsSinceRotation = 0;
        var consecutiveRotations = 0;
        var rotatedOnce = false;
        for (var turn = 0; turn < 40; turn++)
        {
            var response = await session.SendAsync($"{turn}-{message}", TestContext.Current.CancellationToken);
            if (!response.RotationOccurred)
            {
                turnsSinceRotation++;
                continue;
            }

            if (rotatedOnce && turnsSinceRotation == 0)
            {
                consecutiveRotations++;
            }

            rotatedOnce = true;
            turnsSinceRotation = 0;
        }

        // Assert: it compacted, and no rotation was immediately followed by another. A rotated
        // context landing on or above the threshold shows up here and nowhere else.
        Assert.True(session.RotationCount > 0);
        Assert.True(
            consecutiveRotations == 0,
            $"{consecutiveRotations} of {session.RotationCount} rotations in 40 turns immediately "
            + "followed another, so the rotated context did not land below the rotation threshold.");

        // Assert: and the rate is occasional rather than per-turn. A lower bound alone cannot tell
        // healthy hysteresis from thrash; the upper bound is what carries the claim.
        Assert.InRange(session.RotationCount, 3, 20);

        // Assert: nothing saturated, because every tier stayed within its budget. A session that
        // thrashes for want of headroom must not be able to hide behind a saturation signal - and
        // could not, which is exactly why the defect was invisible.
        Assert.True(session.Layout.CoarseTiers.All(tier => tier.IsWithinBudget));
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
        // Arrange: a summarizer that does what summarizing is for - drops the routine and collapses
        // repetition, keeping each distinct thing once. A summarizer that never reduces is a
        // different scenario entirely, and is exercised in the bound test below rather than here:
        // with budgets enforced, material that holds no redundancy is dropped by design, so
        // asserting unbounded retention against it would be asserting a promise no finite window
        // can keep.
        var summarizer = new FakeSummarizer(request =>
        {
            var kept = (request.PreviousRecord + "\n" + request.Material)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line))
                .Where(line => !line.Contains("Routine step", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal);
            return string.Join("\n", kept);
        });
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn("noted"),
            windowTokens: SessionTestData.ConvergentWindowTokens);
        var options = new AgentSessionOptions(
            summarizer, providerWindowTokens: SessionTestData.ConvergentWindowTokens, compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: state a distinctive fact first, then bury it under many later turns
        await session.SendAsync("The deployment key lives at /etc/secrets/deploy.key", TestContext.Current.CancellationToken);
        for (var turn = 0; turn < 45; turn++)
        {
            await session.SendAsync($"Routine step {turn} with some padding "
                + new string('p', 30 * TokenEstimator.CharactersPerToken),
                TestContext.Current.CancellationToken);
        }

        // Assert: the session rotated repeatedly, and the early detail is still in what it sends.
        // No upper bound on rotations here: the claim under test is retention across many
        // rotations, not how few of them there are. Convergence is asserted where it is a fair
        // claim - against a summarizer that stays within its budgets - in the construction-bound
        // test above.
        Assert.True(session.RotationCount >= 5, $"Expected repeated rotation, saw {session.RotationCount}.");
        var seeded = string.Join("\n", session.Layout.BuildSeed().Select(entry => entry.Text));
        Assert.True(
            seeded.Contains("/etc/secrets/deploy.key", StringComparison.Ordinal),
            $"Early detail was not carried after {session.RotationCount} rotations. Seed was:\n{seeded}");
    }

    /// <summary>
    ///     Proves a tier budget is a bound rather than a request: a summarizer that never reduces
    ///     anything still cannot grow the context past the budgets the policy configures.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is the guarantee the arrangement rests on, and it cannot be delegated to the
    ///     summarizer. Asking a model for a size does not produce that size — measured against live
    ///     models, requests in the tens of thousands of tokens came back as a small fraction of
    ///     them — so a budget that is only ever requested bounds nothing. When consolidation stops
    ///     deduplicating, the only move arithmetic leaves is to drop the oldest material, and that
    ///     is what the engine does.
    ///     </para>
    ///     <para>
    ///     Without this, a context that merely tends to stay small is free to stop staying small,
    ///     which is the failure the whole package exists to prevent.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AgentKitSessions_SummarizerThatNeverReduces_StillHoldsTheBound()
    {
        // Arrange: the adversarial summarizer - it concatenates, so every consolidation returns
        // more than it was given and nothing is ever deduplicated
        var summarizer = new FakeSummarizer(request =>
            string.IsNullOrEmpty(request.PreviousRecord)
                ? request.Material
                : request.PreviousRecord + "\n" + request.Material);
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn("noted"),
            windowTokens: SessionTestData.ConvergentWindowTokens);
        var options = new AgentSessionOptions(
            summarizer,
            providerWindowTokens: SessionTestData.ConvergentWindowTokens,
            compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Act: enough turns to rotate many times over, checking the bound at each rotation. The
        // bound describes a rotated context, so immediately after a rotation is the only moment it
        // is a fair question - tier zero is append-only and grows past its budget between them.
        var rotations = 0;
        for (var turn = 0; turn < 40; turn++)
        {
            var response = await session.SendAsync(
                $"Step {turn} " + new string('p', 30 * TokenEstimator.CharactersPerToken),
                TestContext.Current.CancellationToken);

            if (!response.RotationOccurred)
            {
                continue;
            }

            rotations++;

            // Assert: every tier is within the budget its policy configured, which is what makes
            // the total a property of the configuration rather than of the summarizer's cooperation
            foreach (var tier in session.Layout.CoarseTiers)
            {
                Assert.True(
                    tier.EstimatedTokens <= tier.BudgetTokens,
                    $"After rotation {rotations}, tier {tier.Index} holds {tier.EstimatedTokens} "
                        + $"tokens against a budget of {tier.BudgetTokens}.");
            }

            // Assert: and the layout agrees it is within the bound it states for itself
            Assert.True(
                session.Layout.IsWithinBound,
                $"After rotation {rotations}, layout holds {session.Layout.TotalEstimatedTokens} "
                    + $"tokens against a bound of {session.Layout.MaximumBoundTokens}.");
        }

        // Assert: it really did rotate repeatedly, so the bound was under continuous pressure
        Assert.True(rotations >= 5, $"Expected repeated rotation, saw {rotations}.");
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
            windowTokens: SessionTestData.ConvergentWindowTokens);
        var options = new AgentSessionOptions(
            new FakeSummarizer(request => request.Material),
            providerWindowTokens: SessionTestData.ConvergentWindowTokens,
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
            windowTokens: SessionTestData.ConvergentWindowTokens,
            reportsUsage: reportsUsage);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.1), providerWindowTokens: SessionTestData.ConvergentWindowTokens, compaction: SessionTestData.SmallPolicy);
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
