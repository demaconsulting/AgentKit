namespace DemaConsulting.AgentKit.Core.Tests;

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
    private static string Msg(int tokens) => new('m', tokens * SessionTestData.CharactersPerToken);

    /// <summary>
    ///     Proves a session answers turns and returns the provider's answer.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Send_AnswersTurns()
    {
        // Arrange: a session over an echoing in-memory provider
        var factory = new InMemoryProviderSessionFactory(message => new ProviderTurn($"echo: {message}"));
        var options = new AgentSessionOptions(new FakeSummarizer());
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: take one turn
        var response = await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Assert: the provider's answer, at the relaxed level, with no rotation on a clean turn
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
        // Arrange: a live session
        var factory = new InMemoryProviderSessionFactory();
        await using var session = await CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken);

        // Act / Assert: a blank turn would spend context to say nothing, so it is refused
        await Assert.ThrowsAsync<ArgumentException>(() => session.SendAsync("   ", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a disposed session refuses further turns.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Send_AfterDispose_Throws()
    {
        // Arrange: a session that has already been disposed
        var factory = new InMemoryProviderSessionFactory();
        var session = await CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken);
        await session.DisposeAsync();

        // Act / Assert: the turn is refused rather than sent to a released provider session
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SendAsync("hi", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves disposal releases the live provider session.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Dispose_ReleasesLiveProviderSession()
    {
        // Arrange: a live session over the in-memory provider
        var factory = new InMemoryProviderSessionFactory();
        var session = await CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken);

        // Act: dispose it
        await session.DisposeAsync();

        // Assert: the provider session it held was released
        Assert.True(factory.Sessions[0].IsDisposed);
    }

    /// <summary>
    ///     Proves the usage a session reports is the provider session's own figure, unaltered.
    /// </summary>
    /// <remarks>
    ///     The engine performs no token arithmetic. A session that recomputed, adjusted or blended
    ///     the figure would be reintroducing a second source of truth for one fact — the thing this
    ///     design removed — and the mismatch would only show up as rotating at the wrong moment.
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_Usage_IsTheProviderSessionsOwnFigure()
    {
        // Arrange: a session against a provider that answers for a known window
        var factory = new InMemoryProviderSessionFactory(windowTokens: 1000);
        await using var session = await CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken);

        // Act: take a turn, which is when the session reads the provider
        var response = await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Assert: every figure is the live provider session's own
        var provider = factory.Sessions[0].CurrentUsage;
        Assert.Equal(provider.WindowTokens, session.Usage.WindowTokens);
        Assert.Equal(provider.UsedTokens, session.Usage.UsedTokens);
        Assert.Equal(provider.ConversationTokens, session.Usage.ConversationTokens);
        Assert.Same(session.Usage, response.Usage);
    }

    /// <summary>
    ///     Proves a session rotates when the conversation crosses the rotation threshold, creating a
    ///     replacement and disposing the session it replaced.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Send_RotatesAtThreshold()
    {
        // Arrange: a narrow provider window and a short verbatim tail, so turns reach the threshold
        var factory = new InMemoryProviderSessionFactory(
            SessionTestData.SizedResponder(15), windowTokens: 300);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.2), verbatimTurns: 2);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: drive enough turns to cross it
        for (var turn = 0; turn < 20; turn++)
        {
            await session.SendAsync(Msg(15), TestContext.Current.CancellationToken);
        }

        // Assert: it rotated, and created exactly one provider session per rotation plus the first
        Assert.True(session.RotationCount > 0);
        Assert.Equal(session.RotationCount + 1, factory.Sessions.Count);

        // Assert: every superseded session was released; only the live one remains.
        for (var index = 0; index < factory.Sessions.Count - 1; index++)
        {
            Assert.True(factory.Sessions[index].IsDisposed);
        }
    }

    /// <summary>
    ///     A liveness property: a session survives a provider that charges several times what the
    ///     turns it is given would suggest. At one, two and three times it keeps answering and
    ///     terminates rather than churning silently or throwing — the exact condition the old design
    ///     failed on. The rate-dependent behavior (rotating more often and escalating higher) is
    ///     asserted separately.
    /// </summary>
    /// <param name="multiplier">The provider's tokenizer multiplier relative to the baseline count.</param>
    [Theory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    public async Task CompactingAgentSession_DivergentTokenizer_KeepsAnsweringAndTerminates(double multiplier)
    {
        // Arrange: a provider charging a multiple of the baseline rate for the same history
        var factory = new DivergentTokenizerProviderSessionFactory(multiplier, windowTokens: 400, SessionTestData.SizedResponder(15));
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.2), verbatimTurns: 8);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: drive a long run of equally sized turns
        var answered = 0;
        for (var turn = 0; turn < 30; turn++)
        {
            var response = await session.SendAsync(Msg(15), TestContext.Current.CancellationToken);
            Assert.NotNull(response.Text);
            answered++;
        }

        // Assert: every turn was answered, so the session terminates rather than churning
        Assert.Equal(30, answered);
    }

    /// <summary>
    ///     Proves that when the window is too small to hold a structure the session can compact into,
    ///     it escalates to the highest level and then reports material dropped — the honest signal
    ///     that compaction bought nothing. This is the session-level rule 5: at the tersest level
    ///     there is nowhere further to go, so the oldest card goes in the bin.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_TightWindow_EscalatesToHighAndReportsDroppedMaterial()
    {
        // Arrange: a window too small to hold a structure the session can compact into
        var factory = new InMemoryProviderSessionFactory(SessionTestData.SizedResponder(15), windowTokens: 100);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.5), verbatimTurns: 8);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: drive a long run of turns under that pressure
        var responses = new List<AgentSessionResponse>();
        for (var turn = 0; turn < 40; turn++)
        {
            responses.Add(await session.SendAsync(Msg(15), TestContext.Current.CancellationToken));
        }

        // Assert: it escalated to its tersest level and then honestly reported binning the oldest
        Assert.Contains(responses, response => response.Level == CompactionLevel.High);
        Assert.Contains(responses, response => response.MaterialDropped);
    }

    /// <summary>
    ///     Proves rule 2 is answered in the provider's own currency: a provider whose tokenizer
    ///     reports more tokens for the same history crosses the rotation threshold sooner, so at two
    ///     and three times the baseline rate the session rotates strictly more often and escalates
    ///     strictly higher than at one times, where nothing forces a rotation at all. This is the
    ///     rate-dependent observable; reverting every run to one times collapses all three rows to
    ///     zero rotations at the relaxed level and fails both strict chains.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_DivergentTokenizer_RotatesMoreOftenAndEscalatesHigher()
    {
        // Arrange / Act: the same run against providers counting at one, two and three times
        var one = await RunDivergentAsync(multiplier: 1.0, providerWindow: 2000, TestContext.Current.CancellationToken);
        var two = await RunDivergentAsync(multiplier: 2.0, providerWindow: 2000, TestContext.Current.CancellationToken);
        var three = await RunDivergentAsync(multiplier: 3.0, providerWindow: 2000, TestContext.Current.CancellationToken);

        // Assert: rule 2 reads the provider's own count against its own window, so a larger
        // multiplier crosses the threshold sooner: strictly more rotations and a strictly higher
        // escalation level
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
        // Arrange: a session whose summarizer never produces a record
        var factory = new InMemoryProviderSessionFactory(
            SessionTestData.SizedResponder(15), windowTokens: 300);
        var options = new AgentSessionOptions(
            FakeSummarizer.Blank(), verbatimTurns: 3);
        await using var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Act: drive it until the tail settles, then drive twice as far again
        for (var turn = 0; turn < 12; turn++)
        {
            await session.SendAsync(Msg(15), TestContext.Current.CancellationToken);
        }

        var settled = session.Layout.Tail.TurnCount;

        for (var turn = 0; turn < 24; turn++)
        {
            await session.SendAsync(Msg(15), TestContext.Current.CancellationToken);
        }

        // Assert: the verbatim tail did not grow, so the context is bounded even though no
        // consolidation ever succeeded
        Assert.True(
            session.Layout.Tail.TurnCount <= settled,
            $"The verbatim tail grew from {settled} to {session.Layout.Tail.TurnCount} turns while "
                + "consolidation never succeeded, so nothing bounds the context.");
    }

    /// <summary>
    ///     Proves a session that escalated under a busy stretch comes back down after a quiet one,
    ///     so one busy stretch is not paid for in fidelity for the rest of the session's life.
    /// </summary>
    /// <remarks>
    ///     The relaxation branch is the only path that lowers a level, and it is guarded by two
    ///     conditions at once - a prior rotation, and at least m quiet turns since it. Every other
    ///     session test rotates far more often than m turns apart, so none of them reaches it: the
    ///     branch could be deleted and they would all still pass. The answer size is switched
    ///     between phases rather than the message size, because the answer is what dominates a turn
    ///     here.
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_AfterAQuietStretch_RelaxesTheCompactionLevel()
    {
        // Arrange: a session over a provider whose answer size the test controls
        var answerTokens = 700;
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn(SessionTestData.AssistantOfTokens(answerTokens, "r").Text),
            windowTokens: 2000);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.05),
            verbatimTurns: 2);
        await using var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Act: phase one - turns heavy enough to refill the window within k turns of each rotation,
        // which is the condition that escalates
        var peak = CompactionLevel.Low;
        for (var turn = 0; turn < 40; turn++)
        {
            var response = await session.SendAsync(Msg(700), TestContext.Current.CancellationToken);
            if (response.Level > peak)
            {
                peak = response.Level;
            }
        }

        // Assert: the session escalated, so there is something to relax from
        Assert.True(peak > CompactionLevel.Low, "The session never escalated, so there is nothing to relax from.");

        // Act: phase two - a long quiet stretch of light turns. The summarizer compresses hard, so
        // the slots the session is carrying leave real slack, and the window then takes far more
        // than m turns to fill, which is the condition that relaxes.
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

        // Assert: the level came back down
        Assert.True(settled < peak, $"The level must come down after a quiet stretch, but stayed at {settled}.");
    }

    /// <summary>
    ///     Drives a <see cref="CompactingAgentSession"/> over a fixed run of equally sized turns
    ///     against a provider counting at a multiple of the baseline rate, and reports the
    ///     rate-dependent observables: how many times it rotated, the highest compaction level it
    ///     reached, and whether any turn reported material dropped.
    /// </summary>
    /// <param name="multiplier">The provider's tokenizer multiplier relative to the baseline count.</param>
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
            new FakeSummarizer(0.2), verbatimTurns: 8);
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
        // Arrange: a provider session whose release always fails
        var failing = new DisposeThrowingProviderSession();
        var factory = new ScriptedProviderSessionFactory(_ => failing);
        var session = await CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken);

        // Act / Assert: the failure propagates, and a second disposal tries again
        await Assert.ThrowsAsync<IOException>(async () => await session.DisposeAsync());
        await Assert.ThrowsAsync<IOException>(async () => await session.DisposeAsync());

        // Assert: the release really was retried rather than reported as already done
        Assert.Equal(2, failing.DisposeAttempts);
    }

    /// <summary>
    ///     Proves a creation whose provider usage throws releases the provider and reports the
    ///     original failure unchanged.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Create_UsageThrows_ReleasesAndRethrowsOriginal()
    {
        // Arrange: an adapter whose usage report is arithmetically impossible
        var factory = new ScriptedProviderSessionFactory(_ => new UsageThrowingProviderSession(throwOnDispose: false));

        // Act: create the session
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken));

        // Assert: the adapter's own failure reaches the caller unwrapped, and nothing was orphaned
        Assert.Equal("The adapter reported an impossible split.", failure.Message);
        Assert.True(((UsageThrowingProviderSession)factory.Created[0]).IsDisposed);
    }

    /// <summary>
    ///     Proves a creation whose provider usage throws and whose release then <em>also</em> fails
    ///     still reports the creation failure, rather than the disposal failure that followed it.
    /// </summary>
    /// <remarks>
    ///     The release is attempted and its outcome deliberately not acted on. Letting the disposal
    ///     failure surface instead would replace the fault the caller can act on — the adapter's
    ///     impossible usage split — with the consequence of it, and the provider is left to reclaim
    ///     the session when it expires either way.
    /// </remarks>
    [Fact]
    public async Task CompactingAgentSession_Create_UsageThrowsAndReleaseFails_ReportsTheCreationFailure()
    {
        // Arrange: an adapter that fails twice over - on usage, and again on release
        var factory = new ScriptedProviderSessionFactory(_ => new UsageThrowingProviderSession(throwOnDispose: true));

        // Act: create the session
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken));

        // Assert: the creation failure is what the caller is told about, not the swallowed IOException
        Assert.Equal("The adapter reported an impossible split.", failure.Message);
    }

    /// <summary>
    ///     Proves a rotation whose replacement cannot be adopted releases the replacement rather than
    ///     orphaning it, leaving the session coherent against the provider it already had.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Rotate_ReplacementUsageThrows_ReleasesReplacement()
    {
        // Arrange: a factory whose replacement session cannot report its usage
        var bad = new UsageThrowingProviderSession(throwOnDispose: false);
        var factory = new ScriptedProviderSessionFactory(
            seed => new InMemoryProviderSession(seed, SessionTestData.SizedResponder(15), 100),
            _ => bad);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.2), verbatimTurns: 2);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: drive turns until a rotation is attempted; adopting the bad replacement throws
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            for (var turn = 0; turn < 8; turn++)
            {
                await session.SendAsync(Msg(15), TestContext.Current.CancellationToken);
            }
        });

        // Assert: the replacement was released rather than orphaned
        Assert.True(bad.IsDisposed);
    }

    /// <summary>
    ///     Proves a factory returning null at creation is refused.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_Create_NullProviderSession_Throws()
    {
        // Arrange: a factory that returns nothing
        var factory = new ScriptedProviderSessionFactory(_ => null!);

        // Act / Assert: creation is refused rather than proceeding with no provider session
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CompactingAgentSession.CreateAsync(new AgentSessionOptions(new FakeSummarizer()), factory, TestContext.Current.CancellationToken));
    }
}

