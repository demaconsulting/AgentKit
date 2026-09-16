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
        var factory = new InMemoryProviderSessionFactory(windowTokens: 1000, reportsUsage: true);
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
        var factory = new InMemoryProviderSessionFactory(SessionTestData.SizedResponder(15), reportsUsage: false);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.2), providerWindowTokens: 300, compaction: new CompactionPolicy(verbatimTurns: 2));
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
    ///     Proves a session survives a provider whose tokenizer diverges from this library's
    ///     estimate: at one, two and three times divergence it keeps answering and terminates rather
    ///     than churning silently — the exact condition the old design failed on.
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
            new FakeSummarizer(0.2), providerWindowTokens: 400, compaction: new CompactionPolicy(verbatimTurns: 8));
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
    ///     Proves that under a window too small to hold a full structure, the drop-until-it-fits rule
    ///     escalates to the highest level and reports material dropped — the honest signal that
    ///     compaction bought nothing — exercised with a provider whose count diverges from ours.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_TightWindow_EscalatesToHighAndReportsDroppedMaterial()
    {
        var factory = new DivergentTokenizerProviderSessionFactory(multiplier: 2.0, windowTokens: 100, SessionTestData.SizedResponder(15));
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.5), providerWindowTokens: 100, compaction: new CompactionPolicy(verbatimTurns: 8));
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
            seed => new InMemoryProviderSession(seed, SessionTestData.SizedResponder(15), 100, reportsUsage: false),
            _ => bad);
        var options = new AgentSessionOptions(
            new FakeSummarizer(0.2), providerWindowTokens: 100, compaction: new CompactionPolicy(verbatimTurns: 2));
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
