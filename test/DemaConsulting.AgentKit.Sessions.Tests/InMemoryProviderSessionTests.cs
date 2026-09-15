namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="InMemoryProviderSession"/> and
///     <see cref="InMemoryProviderSessionFactory"/>: the provider that contacts nothing, through
///     which the whole engine is exercised end to end.
/// </summary>
public class InMemoryProviderSessionTests
{
    /// <summary>
    ///     Proves a session starts holding what it was seeded with, so a rotation's preserved content
    ///     is genuinely carried into the replacement rather than merely handed to it.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSession_Construct_StartsHoldingTheSeededHistory()
    {
        // Arrange: a seed carrying a consolidated record and one verbatim turn
        var seed = new ProviderSessionSeed(
            "be helpful",
            [],
            [TranscriptEntry.ContextRecord("earlier"), TranscriptEntry.User("latest")]);

        // Act: create a session from it
        await using var session = new InMemoryProviderSession(seed, _ => new ProviderTurn("ok"), 1000);

        // Assert: the seeded history is present and the seed itself is inspectable
        Assert.Equal(2, session.History.Count);
        Assert.Same(seed, session.Seed);
    }

    /// <summary>
    ///     Proves a turn records the incoming message and everything the turn produced — the tool
    ///     work and the answer that followed it — so the session's history matches what a provider
    ///     holding the conversation server-side would have.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSession_SendAsync_RecordsTheMessageAndTheTurn()
    {
        // Arrange: a session whose responder calls a tool
        var seed = new ProviderSessionSeed(null, [], []);
        await using var session = new InMemoryProviderSession(
            seed,
            _ => new ProviderTurn("done", [TranscriptEntry.ToolCall("c1", "read"), TranscriptEntry.ToolResult("c1", "ok")]),
            1000);

        // Act: take one turn
        var turn = await session.SendAsync("please read", TestContext.Current.CancellationToken);

        // Assert: the message, both turn entries and the answer were recorded, in order
        Assert.Equal("done", turn.ResponseText);
        Assert.Equal(4, session.History.Count);
        Assert.Equal(TranscriptEntryKind.UserMessage, session.History[0].Kind);
        Assert.Equal(TranscriptEntryKind.ToolResult, session.History[2].Kind);
        Assert.Equal(TranscriptEntryKind.AssistantMessage, session.History[3].Kind);
        Assert.Equal("done", session.History[3].Text);
        Assert.Equal(1, session.TurnCount);
    }

    /// <summary>
    ///     Proves usage is reported as provider-supplied and grows with the conversation, which is
    ///     what lets a test exercise the branch where the engine prefers a provider's own figures.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSession_CurrentUsage_ReportsProviderOriginAndGrows()
    {
        // Arrange: a session with no seeded history
        await using var session = new InMemoryProviderSession(
            new ProviderSessionSeed(null, [], []),
            _ => new ProviderTurn("ok"),
            1000);
        var before = session.CurrentUsage!;

        // Act: take a turn
        await session.SendAsync("hello", TestContext.Current.CancellationToken);
        var after = session.CurrentUsage!;

        // Assert: reported as the provider's own figures, and larger than before
        Assert.Equal(ContextUsageOrigin.Provider, after.Origin);
        Assert.True(after.UsedTokens > before.UsedTokens);
        Assert.Equal(1000, after.WindowTokens);
    }

    /// <summary>
    ///     Proves a session can be configured to reveal nothing, standing in for the provider family
    ///     that reports no usage at all, so the engine's estimating branch is reachable.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSession_CurrentUsage_WhenNotReporting_IsNull()
    {
        // Arrange / Act: a session that does not report
        await using var session = new InMemoryProviderSession(
            new ProviderSessionSeed(null, [], []),
            _ => new ProviderTurn("ok"),
            1000,
            reportsUsage: false);

        // Assert: it says so rather than inventing a figure
        Assert.Null(session.CurrentUsage);
    }

    /// <summary>
    ///     Proves disposal is observable and final. Against a real provider a session left undisposed
    ///     holds server-side state that keeps being billed for, so a rotation that leaked one must be
    ///     detectable.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSession_DisposeAsync_MarksDisposedAndRefusesFurtherTurns()
    {
        // Arrange: a session that has taken a turn
        var session = new InMemoryProviderSession(
            new ProviderSessionSeed(null, [], []),
            _ => new ProviderTurn("ok"),
            1000);
        await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Act: dispose it, twice, which must be permitted
        await session.DisposeAsync();
        await session.DisposeAsync();

        // Assert: disposal is visible, history is released, and further turns are refused
        Assert.True(session.IsDisposed);
        Assert.Empty(session.History);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SendAsync("again", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves the factory remembers every session it made. Rotation creates a replacement and
    ///     disposes its predecessor, and both halves have to be observable for that behavior to be
    ///     verifiable at all.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSessionFactory_CreateAsync_RecordsEverySessionItMakes()
    {
        // Arrange: a factory with the default echoing responder
        var factory = new InMemoryProviderSessionFactory(windowTokens: 1000);

        // Act: create two sessions, as a conversation with one rotation would
        await factory.CreateAsync(new ProviderSessionSeed(null, [], []), TestContext.Current.CancellationToken);
        await factory.CreateAsync(
            new ProviderSessionSeed(null, [], [TranscriptEntry.ContextRecord("earlier")]),
            TestContext.Current.CancellationToken);

        // Assert: both are recorded, oldest first, carrying their own seeds
        Assert.Equal(2, factory.Sessions.Count);
        Assert.Empty(factory.Sessions[0].Seed.History);
        Assert.Single(factory.Sessions[1].Seed.History);
    }

    /// <summary>
    ///     Proves the factory records every session when rotations create them concurrently.
    ///     <see cref="IProviderSessionFactory"/> requires a concurrent-safe implementation because an
    ///     application may run several sessions against one factory, and an unsynchronized list can
    ///     lose a session or leave its record internally inconsistent.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSessionFactory_CreateAsync_ConcurrentCreations_RecordsEveryOne()
    {
        // Arrange: one factory, as several logical sessions rotating at once would share
        var factory = new InMemoryProviderSessionFactory(windowTokens: 1000);
        var seed = new ProviderSessionSeed(null, [], []);
        const int Creations = 2_000;

        // Act: create sessions from many threads at once
        await Parallel.ForEachAsync(
            Enumerable.Range(0, Creations),
            TestContext.Current.CancellationToken,
            async (_, token) => await factory.CreateAsync(seed, token));

        // Assert: every session was recorded
        var recorded = factory.Sessions;
        Assert.Equal(Creations, recorded.Count);
        Assert.All(recorded, Assert.NotNull);

        // Assert: the list handed out is a snapshot, so a later creation cannot disturb it
        await factory.CreateAsync(seed, TestContext.Current.CancellationToken);
        Assert.Equal(Creations, recorded.Count);
        Assert.Equal(Creations + 1, factory.Sessions.Count);
    }

    /// <summary>
    ///     Proves the default responder answers without a test having to supply one, so a test about
    ///     the session lifecycle is not obliged to also invent what a model says.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSessionFactory_DefaultResponder_Answers()
    {
        // Arrange: a factory configured with nothing but a window
        var factory = new InMemoryProviderSessionFactory(windowTokens: 1000);
        await using var session = await factory.CreateAsync(new ProviderSessionSeed(null, [], []), TestContext.Current.CancellationToken);

        // Act: take a turn
        var turn = await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Assert: an answer that names the message, so a test can tell turns apart
        Assert.Contains("hello", turn.ResponseText, StringComparison.Ordinal);
    }
}
