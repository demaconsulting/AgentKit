namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="CompactingAgentSession"/>: the sequencing that turns options, a
///     layout, a rotation engine and a provider factory into a session that outlives its window.
/// </summary>
public class CompactingAgentSessionTests
{
    /// <summary>
    ///     Proves a new session starts one provider session seeded with no history, carrying the
    ///     instructions the application configured.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_CreateAsync_SeedsOneEmptyProviderSession()
    {
        // Arrange: a session over the in-memory provider
        var factory = new InMemoryProviderSessionFactory(windowTokens: 1000);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), "be helpful", providerWindowTokens: 1000, compaction: SessionTestData.SmallPolicy);

        // Act: create the session
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Assert: exactly one provider session, seeded empty, carrying the instructions
        var provider = Assert.Single(factory.Sessions);
        Assert.Empty(provider.Seed.History);
        Assert.Equal("be helpful", provider.Seed.Instructions);
        Assert.Equal(0, session.RotationCount);
    }

    /// <summary>
    ///     Proves an ordinary turn is answered without compaction and is recorded in the engine's own
    ///     transcript, which is where a later consolidation will read it from.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_BelowThreshold_AnswersWithoutRotating()
    {
        // Arrange: a session with plenty of room
        var factory = new InMemoryProviderSessionFactory(windowTokens: 100_000);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 100_000);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: take one turn
        var response = await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Assert: answered, nothing rotated, and both halves of the turn are in the transcript
        Assert.Contains("hello", response.Text, StringComparison.Ordinal);
        Assert.False(response.RotationOccurred);
        Assert.Equal(0, session.RotationCount);
        Assert.Equal(2, session.Layout.Transcript.Entries.Count);
        Assert.Single(factory.Sessions);
    }

    /// <summary>
    ///     Proves the central behavior: crossing the threshold consolidates older history, disposes
    ///     the live provider session, and creates a fresh one seeded from the preserved content.
    ///     Rotation rather than in-place editing is what makes the behavior identical on a provider
    ///     that re-sends history and one that holds it server-side.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_AboveThreshold_RotatesIntoAFreshSeededSession()
    {
        // Arrange: a small window and a small policy, with turns large enough to fill it in two
        var factory = new InMemoryProviderSessionFactory(
            _ => new ProviderTurn(new string('r', 70 * TokenEstimator.CharactersPerToken)),
            windowTokens: 400);
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 400, compaction: SessionTestData.SmallPolicy);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        var message = new string('m', 70 * TokenEstimator.CharactersPerToken);

        // Act: two turns, the second of which crosses the threshold
        await session.SendAsync(message, TestContext.Current.CancellationToken);
        var response = await session.SendAsync(message, TestContext.Current.CancellationToken);

        // Assert: the session rotated exactly once and reported it
        Assert.True(response.RotationOccurred);
        Assert.Equal(1, session.RotationCount);
        Assert.True(session.ConsolidationCount > 0);

        // Assert: the previous provider session was disposed and a replacement created
        Assert.Equal(2, factory.Sessions.Count);
        Assert.True(factory.Sessions[0].IsDisposed);
        Assert.False(factory.Sessions[1].IsDisposed);

        // Assert: the replacement was seeded with a consolidated record ahead of verbatim history
        var seeded = factory.Sessions[1].Seed.History;
        Assert.Equal(TranscriptEntryKind.ContextRecord, seeded[0].Kind);
        Assert.Contains(seeded, entry => entry.Kind != TranscriptEntryKind.ContextRecord);

        // Assert: the replacement carries the same capability, because rotation replaces history
        // rather than capability
        Assert.Equal(options.Tools, factory.Sessions[1].Seed.Tools);
    }

    /// <summary>
    ///     Proves a turn the provider never accepted leaves no trace. A provider may honor
    ///     cancellation or fail before taking the turn; a message recorded ahead of that would be a
    ///     turn no provider ever saw, which a later rotation would nonetheless consolidate and seed
    ///     into the replacement session.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ProviderRejectsTheTurn_RecordsNoGhostEntry()
    {
        // Arrange: a live session and a token that was canceled before the turn was taken
        var factory = new InMemoryProviderSessionFactory(windowTokens: 100_000);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 100_000);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        // Act: the provider refuses the turn
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.SendAsync("never seen by any provider", canceled.Token));

        // Assert: neither the engine's transcript nor the provider's history holds the message
        Assert.Empty(session.Layout.Transcript.Entries);
        Assert.Empty(factory.Sessions[0].History);
    }

    /// <summary>
    ///     Proves the engine prefers a provider's own account of the window, because those figures
    ///     count framing this library never sees.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ProviderReportsUsage_PrefersTheProviderFigures()
    {
        // Arrange: a provider that reports
        var factory = new InMemoryProviderSessionFactory(windowTokens: 100_000);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 100_000);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: take a turn
        var response = await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Assert: the figure is marked as the provider's own
        Assert.Equal(ContextUsageOrigin.Provider, response.Usage.Origin);
    }

    /// <summary>
    ///     Proves the engine falls back to its own estimate for the provider family that reports
    ///     nothing, so both provider shapes reach the same rotation decision by the same arithmetic.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ProviderReportsNothing_UsesItsOwnEstimate()
    {
        // Arrange: a provider that reveals nothing about its window
        var factory = new InMemoryProviderSessionFactory(windowTokens: 100_000, reportsUsage: false);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 100_000);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        // Act: take a turn
        var response = await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Assert: the figure is marked as estimated, and reflects the engine's own transcript
        Assert.Equal(ContextUsageOrigin.Estimated, response.Usage.Origin);
        Assert.Equal(session.Layout.TotalEstimatedTokens, response.Usage.UsedTokens);
        Assert.Equal(100_000, response.Usage.WindowTokens);
    }

    /// <summary>
    ///     Proves a blank message is refused: a blank turn spends context to say nothing, and is a
    ///     defect in the calling application rather than something to forward to a provider.
    /// </summary>
    /// <param name="message">The rejected message.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CompactingAgentSession_SendAsync_BlankMessage_Throws(string? message)
    {
        var factory = new InMemoryProviderSessionFactory(windowTokens: 100_000);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 100_000);
        await using var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(() => session.SendAsync(message!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves disposal releases the live provider session, which for some providers is
    ///     server-side state that keeps being billed for until it is released.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_DisposeAsync_ReleasesTheLiveProviderSession()
    {
        // Arrange: a session that has taken a turn
        var factory = new InMemoryProviderSessionFactory(windowTokens: 100_000);
        var options = new AgentSessionOptions(new FakeSummarizer(), providerWindowTokens: 100_000);
        var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Act: dispose it, twice, which must be permitted
        await session.DisposeAsync();
        await session.DisposeAsync();

        // Assert: the provider session was released, and the session refuses further turns
        Assert.True(factory.Sessions[0].IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SendAsync("again", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a missing configuration or provider factory is refused where the host wrote it,
    ///     rather than at the first conversation.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_CreateAsync_NullArguments_Throw()
    {
        var options = new AgentSessionOptions(new FakeSummarizer());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CompactingAgentSession.CreateAsync(null!, new InMemoryProviderSessionFactory(), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CompactingAgentSession.CreateAsync(options, null!, TestContext.Current.CancellationToken));
    }
}
