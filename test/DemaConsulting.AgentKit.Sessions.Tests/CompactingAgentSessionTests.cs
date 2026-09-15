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
    ///     Proves a rotated session is coherent even when the superseded provider session fails to
    ///     dispose. The rotation has already succeeded by that point — the context was consolidated
    ///     and the replacement created — so a disposal failure is not allowed to surface as a
    ///     rotation failure, and must not leave this session pointing at the replacement while its
    ///     layout and counters still describe the session it replaced.
    /// </summary>
    [Fact]
    public async Task CompactingAgentSession_SendAsync_ReplacedProviderFailsToDispose_StaysCoherent()
    {
        // Arrange: the same arithmetic as the rotation scenario - a 400-token window, the small
        // policy, and turns of roughly 140 tokens - against a provider that throws when disposed
        var factory = new ThrowingDisposeProviderSessionFactory(
            _ => new ProviderTurn(new string('r', 70 * TokenEstimator.CharactersPerToken)));
        var options = new AgentSessionOptions(
            new FakeSummarizer(), providerWindowTokens: 400, compaction: SessionTestData.SmallPolicy);
        var session = await CompactingAgentSession.CreateAsync(options, factory, TestContext.Current.CancellationToken);
        var message = new string('m', 70 * TokenEstimator.CharactersPerToken);

        // Act: two turns, the second of which rotates and so disposes the first session
        await session.SendAsync(message, TestContext.Current.CancellationToken);
        var response = await session.SendAsync(message, TestContext.Current.CancellationToken);

        // Assert: the disposal was attempted and failed, and the rotation was still reported as the
        // success it was
        Assert.True(factory.Sessions[0].DisposeAttempted);
        Assert.True(response.RotationOccurred);
        Assert.Equal(1, session.RotationCount);
        Assert.True(session.ConsolidationCount > 0);

        // Assert: the session describes the replacement, not the session it replaced - the layout
        // was consolidated into tier one and the transcript was reduced to the retained suffix
        Assert.Equal(2, factory.Sessions.Count);
        Assert.False(session.Layout.CoarseTiers[0].IsEmpty);
        Assert.True(session.Layout.ConversationTokens <= session.Layout.MaximumBoundTokens);

        // Assert: and it still works, appending the next turn to the replacement's transcript
        var entriesBefore = session.Layout.Transcript.Entries.Count;
        await session.SendAsync("after the failed disposal", TestContext.Current.CancellationToken);
        Assert.True(session.Layout.Transcript.Entries.Count > entriesBefore);

        // Assert: explicit disposal is a different matter and does report the failure - a caller
        // that asked for the session to be released is entitled to learn that it was not
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());
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

/// <summary>
///     A provider session that answers normally but fails when it is disposed.
/// </summary>
/// <remarks>
///     Models the adapter this engine cannot control: one whose provider-side release fails, on a
///     network error or against a session the service has already reclaimed. The shipped in-memory
///     session cannot express that, because its disposal cannot fail.
/// </remarks>
/// <param name="seed">What the session was started from.</param>
/// <param name="responder">Produces the turn for a given message.</param>
internal sealed class ThrowingDisposeProviderSession(
    ProviderSessionSeed seed,
    Func<string, ProviderTurn> responder) : IProviderSession
{
    /// <summary>
    ///     Gets what the session was started from.
    /// </summary>
    public ProviderSessionSeed Seed { get; } = seed;

    /// <summary>
    ///     Gets a value indicating whether disposal was attempted on this session.
    /// </summary>
    /// <remarks>
    ///     Recorded rather than inferred: a rotation that swallows the disposal failure must still
    ///     be shown to have tried to release the session it replaced.
    /// </remarks>
    public bool DisposeAttempted { get; private set; }

    /// <inheritdoc/>
    public Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(responder(message));
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Records the attempt and then fails, which is the whole point of this fake.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        DisposeAttempted = true;
        throw new InvalidOperationException("The provider session could not be released.");
    }
}

/// <summary>
///     Creates <see cref="ThrowingDisposeProviderSession"/> instances and remembers every one it
///     made.
/// </summary>
/// <remarks>
///     Remembering them is what lets a test assert both halves of a rotation whose disposal failed:
///     that a replacement was created, and that the session it replaced was asked to release itself.
/// </remarks>
/// <param name="responder">Produces the turn for a given message.</param>
internal sealed class ThrowingDisposeProviderSessionFactory(Func<string, ProviderTurn> responder)
    : IProviderSessionFactory
{
    /// <summary>
    ///     Gets every session created so far, oldest first.
    /// </summary>
    public List<ThrowingDisposeProviderSession> Sessions { get; } = [];

    /// <inheritdoc/>
    public Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        var session = new ThrowingDisposeProviderSession(seed, responder);
        Sessions.Add(session);
        return Task.FromResult<IProviderSession>(session);
    }
}
