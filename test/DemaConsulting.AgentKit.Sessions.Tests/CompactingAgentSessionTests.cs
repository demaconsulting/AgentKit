namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="CompactingAgentSession"/>: occupancy-triggered rotation, the
///     compaction level and dropped-material reporting, and the provider-session ownership and
///     disposal behavior that must not regress.
/// </summary>
public class CompactingAgentSessionTests
{
    /// <summary>
    ///     Builds a message occupying approximately the requested number of tokens.
    /// </summary>
    /// <param name="tokens">The tokens the message should occupy.</param>
    /// <returns>A message string.</returns>
    private static string Msg(int tokens) => new('m', tokens * TokenEstimator.CharactersPerToken);

    /// <summary>
    ///     Proves a session answers turns and returns the provider's answer.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Send_AnswersTurns()
    {
        var factory = new InMemoryProviderSessionFactory(message => new ProviderTurn($"echo: {message}"));
        var options = new AgentSessionOptions(new FakeSummarizer());
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        var response = await session.SendAsync("hello", TestContext.Current.CancellationToken);

        Assert.Equal("echo: hello", response.Text);
        Assert.Equal(CompactionLevel.Low, response.Level);
        Assert.False(response.RotationOccurred);
    }

    /// <summary>
    ///     Proves a blank message is refused: a blank turn spends context to say nothing.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Send_BlankMessage_Throws()
    {
        var factory = new InMemoryProviderSessionFactory();
        await using var session = await CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => session.SendAsync("   ", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a disposed session refuses further turns.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Send_AfterDispose_Throws()
    {
        var factory = new InMemoryProviderSessionFactory();
        var session = await CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken);
        await session.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SendAsync("hi", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves disposal releases the live provider session.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Dispose_ReleasesLiveProviderSession()
    {
        var factory = new InMemoryProviderSessionFactory();
        var session = await CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken);

        await session.DisposeAsync();

        Assert.True(factory.Sessions[0].IsDisposed);
    }

    /// <summary>
    ///     Proves a session prefers a provider's own usage figure when it reports one.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Usage_PrefersProviderReport()
    {
        var factory = new InMemoryProviderSessionFactory(windowTokens: 1000);
        await using var session = await CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken);

        await session.SendAsync("hello", TestContext.Current.CancellationToken);

        Assert.Equal(ContextUsageOrigin.Provider, session.Usage.Origin);
    }

    /// <summary>
    ///     Proves a session rotates when the conversation crosses the rotation threshold, creating a
    ///     replacement and disposing the session it replaced.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Send_RotatesAtThreshold()
    {
        var factory = new InMemoryProviderSessionFactory(
            SessionTestData.SizedResponder(15), windowTokens: 300);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.2), compaction: new CompactionPolicy(verbatimTurns: 2));
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        for (var turn = 0; turn < 20; turn++)
        {
            await session.SendAsync(Msg(15), TestContext.Current.CancellationToken);
        }

        Assert.True(session.RotationCount > 0);
        Assert.Equal(session.RotationCount + 1, factory.Sessions.Count);

        // Every superseded session was released; only the live one remains.
        for (var index = 0; index < factory.Sessions.Count - 1; index++)
        {
            Assert.True(factory.Sessions[index].IsDisposed);
        }
    }

    /// <summary>
    ///     A liveness property: a session survives a provider whose tokenizer diverges from this
    ///     library's estimate. At one, two and three times divergence it keeps answering and
    ///     terminates rather than churning silently or throwing — the exact condition the old design
    ///     failed on. The divergence-dependent behavior (rotating more often, escalating higher, and
    ///     dropping material under a tighter budget) is asserted by the two tests above.
    /// </summary>
    /// <param name="multiplier">The provider's tokenizer multiplier relative to the estimate.</param>
    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    public async Task CompactingAgentSession_DivergentTokenizer_KeepsAnsweringAndTerminates(double multiplier)
    {
        var factory = new DivergentTokenizerProviderSessionFactory(multiplier, windowTokens: 400, SessionTestData.SizedResponder(15));
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.2), compaction: new CompactionPolicy(verbatimTurns: 8));
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        var answered = 0;
        for (var turn = 0; turn < 30; turn++)
        {
            var response = await session.SendAsync(Msg(15), TestContext.Current.CancellationToken);
            Assert.NotNull(response.Text);
            answered++;
        }

        Assert.Equal(30, answered);
    }

    /// <summary>
    ///     Proves that when the window is too small to hold a full structure, the drop-until-it-fits
    ///     rule escalates to the highest level and reports material dropped — the honest signal that
    ///     compaction bought nothing. This is a session-level Rule-5 test: a genuinely undersized
    ///     window forces the drop, so it uses a non-divergent provider that counts with this
    ///     library's own estimator. Divergence is proven separately, by the two tests below.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_TightWindow_EscalatesToHighAndReportsDroppedMaterial()
    {
        var factory = new InMemoryProviderSessionFactory(SessionTestData.SizedResponder(15), windowTokens: 100);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.5), compaction: new CompactionPolicy(verbatimTurns: 8));
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        var responses = new List<AgentSessionResponse>();
        for (var turn = 0; turn < 40; turn++)
        {
            responses.Add(await session.SendAsync(Msg(15), TestContext.Current.CancellationToken));
        }

        Assert.Contains(responses, response => response.Level == CompactionLevel.High);
        Assert.Contains(responses, response => response.MaterialDropped);
    }

    /// <summary>
    ///     Proves divergence is handled by Rule 2 in the provider's own currency: a provider whose
    ///     tokenizer reports more tokens for the same history crosses the rotation threshold sooner,
    ///     so at two and three times divergence the session rotates strictly more often and escalates
    ///     strictly higher than at one times, where nothing forces a rotation at all. This is the
    ///     divergence-dependent observable; reverting every run to one times collapses all three rows
    ///     to zero rotations at the relaxed level and fails both strict chains.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_DivergentTokenizer_RotatesMoreOftenAndEscalatesHigher()
    {
        var one = await RunDivergentAsync(multiplier: 1.0, providerWindow: 2000, TestContext.Current.CancellationToken);
        var two = await RunDivergentAsync(multiplier: 2.0, providerWindow: 2000, TestContext.Current.CancellationToken);
        var three = await RunDivergentAsync(multiplier: 3.0, providerWindow: 2000, TestContext.Current.CancellationToken);

        // Rule 2 reads the provider's own count against its own window, so a larger multiplier crosses
        // the threshold sooner: strictly more rotations and a strictly higher escalation level.
        Assert.True(
            one.Rotations < two.Rotations && two.Rotations < three.Rotations,
            $"Rotations must strictly increase with divergence, but were 1x={one.Rotations}, 2x={two.Rotations}, 3x={three.Rotations}.");
        Assert.True(
            one.MaxLevel < two.MaxLevel && two.MaxLevel < three.MaxLevel,
            $"Escalation level must strictly increase with divergence, but was 1x={one.MaxLevel}, 2x={two.MaxLevel}, 3x={three.MaxLevel}.");
    }
    /// <summary>
    ///     Proves the context stops growing even when consolidation never succeeds, so a summarizer
    ///     that answers blank cannot make a session grow without limit.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A blank answer is a contracted return value, and blankness is content-dependent and so
    ///     sticky: material that produced one will produce it again on the next rotation, because
    ///     the same turns are grouped with the same neighbors. A rotation then deliberately leaves
    ///     the material where it is, which is right - but it means nothing shrinks, and the coarse
    ///     tiers never fill because nothing ever reaches them. Under pressure there is then no slot
    ///     to bin.
    ///     </para>
    ///     <para>
    ///     Without a fall-through to the oldest verbatim turn, the tail gains a turn per message and
    ///     sheds nothing forever, while every rotation reports success and spends a fresh provider
    ///     session. This is the test that catches that: the earlier design was bounded by a rule
    ///     that measured the seed, and when that rule was removed the last-resort bound went with
    ///     it.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_SummarizerAlwaysBlank_StopsGrowing()
    {
        var factory = new InMemoryProviderSessionFactory(
            SessionTestData.SizedResponder(15), windowTokens: 300);
        var options = new AgentSessionOptions(
            FakeSummarizer.Blank(), compaction: new CompactionPolicy(verbatimTurns: 3));
        await using var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        for (var turn = 0; turn < 12; turn++)
        {
            await session.SendAsync(Msg(15), TestContext.Current.CancellationToken);
        }

        var settled = session.Layout.Tail.TurnCount;

        for (var turn = 0; turn < 24; turn++)
        {
            await session.SendAsync(Msg(15), TestContext.Current.CancellationToken);
        }

        Assert.True(
            session.Layout.Tail.TurnCount <= settled,
            $"The verbatim tail grew from {settled} to {session.Layout.Tail.TurnCount} turns while "
                + "consolidation never succeeded, so nothing bounds the context.");
    }



    /// <remarks>
    ///     The relaxation branch is the only path that lowers a level, and it is guarded by three
    ///     conditions at once - a prior rotation, at least m quiet turns, and a rotation that
    ///     escalated nothing. Every other session test rotates far more often than m turns apart, so
    ///     none of them reaches it: the branch could be deleted and they would all still pass. The
    ///     answer size is switched between phases rather than the message size, because the answer
    ///     is what dominates a turn here.
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_AfterAQuietStretch_RelaxesTheCompactionLevel()
    {
        var answerTokens = 700;
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn(SessionTestData.AssistantOfTokens(answerTokens, "r").Text),
            windowTokens: 2000);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.05),
            compaction: new CompactionPolicy(verbatimTurns: 2));
        await using var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Phase one: turns heavy enough to refill the window within k turns of each rotation, which
        // is the condition that escalates.
        var peak = CompactionLevel.Low;
        for (var turn = 0; turn < 40; turn++)
        {
            var response = await session.SendAsync(Msg(700), TestContext.Current.CancellationToken);
            if (response.Level > peak)
            {
                peak = response.Level;
            }
        }

        Assert.True(peak > CompactionLevel.Low, "The session never escalated, so there is nothing to relax from.");

        // Phase two: a long quiet stretch of light turns. The summarizer compresses hard, so the
        // slots the session is carrying leave real slack, and the window then takes far more than m
        // turns to fill - which is the condition that relaxes.
        answerTokens = 1;
        var settled = peak;
        for (var turn = 0; turn < 600; turn++)
        {
            var response = await session.SendAsync("ok", TestContext.Current.CancellationToken);
            settled = response.Level;
            if (settled == CompactionLevel.Low)
            {
                break;
            }
        }

        Assert.True(settled < peak, $"The level must come down after a quiet stretch, but stayed at {settled}.");
    }

    /// <summary>
    ///     Drives a <see cref="CompactingAgentSession"/> over a fixed run of equally sized turns
    ///     against a divergent-tokenizer provider and reports the divergence-dependent observables:
    ///     how many times it rotated, the highest compaction level it reached, and whether any turn
    ///     reported material dropped.
    /// </summary>
    /// <param name="multiplier">The provider's tokenizer multiplier relative to this library's estimate.</param>
    /// <param name="providerWindow">The window the provider reports as its own.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The rotation count, the highest level seen, and whether any turn dropped material.</returns>
    private static async Task<(int Rotations, CompactionLevel MaxLevel, bool AnyDropped)> RunDivergentAsync(
        double multiplier,
        int providerWindow,
        CancellationToken cancellationToken)
    {
        var factory = new DivergentTokenizerProviderSessionFactory(multiplier, providerWindow, SessionTestData.SizedResponder(15));
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.2), compaction: new CompactionPolicy(verbatimTurns: 8));
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, cancellationToken);

        var maxLevel = CompactionLevel.Low;
        var anyDropped = false;
        for (var turn = 0; turn < 40; turn++)
        {
            var response = await session.SendAsync(Msg(15), cancellationToken);
            if (response.Level > maxLevel)
            {
                maxLevel = response.Level;
            }

            if (response.MaterialDropped)
            {
                anyDropped = true;
            }
        }

        return (session.RotationCount, maxLevel, anyDropped);
    }

    /// <summary>
    ///     Proves a failed release propagates from disposal and stays retryable, so a transient
    ///     provider failure does not become a permanent leak.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Dispose_FailedRelease_PropagatesAndRetries()
    {
        var failing = new DisposeThrowingProviderSession();
        var factory = new ScriptedProviderSessionFactory(_ => failing);
        var session = await CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<IOException>(async () => await session.DisposeAsync());
        await Assert.ThrowsAsync<IOException>(async () => await session.DisposeAsync());

        Assert.Equal(2, failing.DisposeAttempts);
    }

    /// <summary>
    ///     Proves a creation whose provider usage throws releases the provider and rethrows the
    ///     original failure unchanged when the release succeeds.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Create_UsageThrowsButReleased_RethrowsOriginal()
    {
        var factory = new ScriptedProviderSessionFactory(_ => new UsageThrowingProviderSession(throwOnDispose: false));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken));

        Assert.IsNotType<AgentSessionCreationException>(failure);
        Assert.True(((UsageThrowingProviderSession)factory.Created[0]).IsDisposed);
    }

    /// <summary>
    ///     Proves a creation whose provider usage throws and whose release then fails carries the
    ///     unreleased provider session on the failure, so the retryable state is reachable.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Create_UsageThrowsAndReleaseFails_CarriesRetainedSession()
    {
        var factory = new ScriptedProviderSessionFactory(_ => new UsageThrowingProviderSession(throwOnDispose: true));

        var failure = await Assert.ThrowsAsync<AgentSessionCreationException>(() =>
            CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken));

        Assert.NotNull(failure.RetainedProviderSession);
    }

    /// <summary>
    ///     Proves a rotation whose replacement cannot be adopted releases the replacement rather than
    ///     orphaning it, leaving the session coherent against the provider it already had.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Rotate_ReplacementUsageThrows_ReleasesReplacement()
    {
        var bad = new UsageThrowingProviderSession(throwOnDispose: false);
        var factory = new ScriptedProviderSessionFactory(
            seed => new InMemoryProviderSession(seed, SessionTestData.SizedResponder(15), 100),
            _ => bad);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.2), compaction: new CompactionPolicy(verbatimTurns: 2));
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Drive turns until a rotation is attempted; adopting the bad replacement throws.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            for (var turn = 0; turn < 8; turn++)
            {
                await session.SendAsync(Msg(15), TestContext.Current.CancellationToken);
            }
        });

        Assert.True(bad.IsDisposed);
    }

    /// <summary>
    ///     Proves a factory returning null at creation is refused.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Create_NullProviderSession_Throws()
    {
        var factory = new ScriptedProviderSessionFactory(_ => null!);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken));
    }
}

