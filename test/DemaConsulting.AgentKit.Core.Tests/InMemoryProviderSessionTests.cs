using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for <see cref="InMemoryProviderSession"/> and
///     <see cref="InMemoryProviderSessionFactory"/>: the in-memory provider that exercises a
///     session's full lifecycle without contacting a provider.
/// </summary>
public class InMemoryProviderSessionTests
{
    /// <summary>
    ///     Proves the session answers from its responder and records the turn, contacting nothing.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSession_Send_AnswersAndRecords()
    {
        // Arrange: a session over a responder that echoes what it is given
        var seed = new ProviderSessionSeed(null, [], []);
        await using var session = new InMemoryProviderSession(seed, message => new ProviderTurn($"echo: {message}"), windowTokens: 1000);

        // Act: take one turn
        var turn = await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Assert: the responder's answer came back, and both halves of the exchange were recorded
        Assert.Equal("echo: hello", turn.ResponseText);
        Assert.Equal(1, session.TurnCount);
        Assert.Equal(2, session.History.Count);
    }

    /// <summary>
    ///     Proves disposal is observable, which is what lets a rotation test assert a superseded
    ///     session was released rather than leaked.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSession_Dispose_IsObservable()
    {
        // Arrange: a live session
        var session = new InMemoryProviderSession(new ProviderSessionSeed(null, [], []), _ => new ProviderTurn("x"), 1000);

        // Act: release it
        await session.DisposeAsync();

        // Assert: the release is visible to a rotation test
        Assert.True(session.IsDisposed);
    }

    /// <summary>
    ///     Proves the session answers for its own window, as every adapter does: it reports the
    ///     window it was given and splits what it holds into the fixed overhead and the
    ///     conversation, so the engine reads one figure in one currency.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSession_Usage_ReportsItsOwnWindowAndSplit()
    {
        // Arrange: a session carrying instructions and a tool, so there is a real fixed overhead
        var tool = AIFunctionFactory.Create(() => 0, "probe", "A probe tool.");
        var seed = new ProviderSessionSeed("system prompt here", [tool], []);
        await using var session = new InMemoryProviderSession(seed, _ => new ProviderTurn("answer"), 1000);
        var before = session.CurrentUsage;

        // Act: take one turn, which is the only thing that adds conversation
        await session.SendAsync("hello", TestContext.Current.CancellationToken);
        var after = session.CurrentUsage;

        // Assert: the window is the one it was given; the overhead is charged before any
        // conversation exists and does not move when the conversation grows
        Assert.Equal(1000, before.WindowTokens);
        Assert.True(session.FixedOverheadTokens > 0);
        Assert.Equal(session.FixedOverheadTokens, before.OverheadTokens);
        Assert.Equal(0, before.ConversationTokens);
        Assert.Equal(session.FixedOverheadTokens, after.OverheadTokens);
        Assert.True(
            after.ConversationTokens > 0,
            "A turn the session recorded must be charged to the conversation.");
    }

    /// <summary>
    ///     Proves a canceled turn leaves the session exactly as it was, with no ghost message.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSession_CanceledTurn_RecordsNothing()
    {
        // Arrange: a session and a token the caller has already given up on
        var session = new InMemoryProviderSession(new ProviderSessionSeed(null, [], []), _ => new ProviderTurn("x"), 1000);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act / Assert: the turn is refused, and no ghost message is left behind
        await Assert.ThrowsAsync<OperationCanceledException>(() => session.SendAsync("hi", cancellation.Token));
        Assert.Equal(0, session.TurnCount);
    }

    /// <summary>
    ///     Proves the factory records every session it made, oldest first, which is the evidence a
    ///     rotation test reads.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSessionFactory_Create_RecordsSessions()
    {
        // Arrange: a factory with nothing created yet
        var factory = new InMemoryProviderSessionFactory(windowTokens: 1000);

        // Act: create two sessions
        await factory.CreateAsync(new ProviderSessionSeed(null, [], []), TestContext.Current.CancellationToken);
        await factory.CreateAsync(new ProviderSessionSeed(null, [], []), TestContext.Current.CancellationToken);

        // Assert: both are remembered, which is the evidence a rotation test reads
        Assert.Equal(2, factory.Sessions.Count);
    }

    /// <summary>
    ///     Proves the factory's published default window is the one the sessions it makes actually
    ///     report, so a test that is not about the window can rely on the default without stating it.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSessionFactory_Create_DefaultWindow_IsThePublishedDefault()
    {
        // Arrange: a factory configured with nothing at all
        var factory = new InMemoryProviderSessionFactory();

        // Act: create a session from it
        await factory.CreateAsync(new ProviderSessionSeed(null, [], []), TestContext.Current.CancellationToken);

        // Assert: the session reports the published default rather than a window of its own choosing
        Assert.Equal(InMemoryProviderSessionFactory.DefaultWindowTokens, factory.WindowTokens);
        Assert.Equal(InMemoryProviderSessionFactory.DefaultWindowTokens, factory.Sessions[0].WindowTokens);
        Assert.Equal(
            InMemoryProviderSessionFactory.DefaultWindowTokens,
            factory.Sessions[0].CurrentUsage.WindowTokens);
    }

    /// <summary>
    ///     Proves a session cannot be built without a seed or a responder, because it would have
    ///     neither a history to start from nor a way to answer.
    /// </summary>
    [Fact]
    public void InMemoryProviderSession_Construct_NullArgument_Throws()
    {
        // Arrange: one valid half of each pair
        var seed = new ProviderSessionSeed(null, [], []);
        Func<string, ProviderTurn> responder = _ => new ProviderTurn("x");

        // Act / Assert: neither omission is accepted
        Assert.Throws<ArgumentNullException>(() => new InMemoryProviderSession(null!, responder, 1000));
        Assert.Throws<ArgumentNullException>(() => new InMemoryProviderSession(seed, null!, 1000));
    }

    /// <summary>
    ///     Proves a window that is not positive is refused by both the session and the factory: a
    ///     session reporting a window of zero would have the engine believe it was full before it
    ///     had said anything.
    /// </summary>
    /// <param name="windowTokens">The non-positive window to attempt.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InMemoryProviderSession_Construct_NonPositiveWindow_Throws(int windowTokens)
    {
        // Arrange: an otherwise valid seed and responder
        var seed = new ProviderSessionSeed(null, [], []);

        // Act / Assert: refused where it is written, by the session and by the factory alike
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InMemoryProviderSession(seed, _ => new ProviderTurn("x"), windowTokens));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InMemoryProviderSessionFactory(windowTokens: windowTokens));
    }

    /// <summary>
    ///     Proves a responder that returns nothing is refused rather than recorded, so a session
    ///     never holds a message no turn ever answered.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSession_Send_ResponderReturnsNull_Throws()
    {
        // Arrange: a responder that answers with nothing at all
        var session = new InMemoryProviderSession(new ProviderSessionSeed(null, [], []), _ => null!, 1000);

        // Act / Assert: the turn is refused
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.SendAsync("hi", TestContext.Current.CancellationToken));

        // Assert: nothing was recorded, so the history holds no ghost message
        Assert.Equal(0, session.TurnCount);
        Assert.Empty(session.History);
    }

    /// <summary>
    ///     Proves a disposed session refuses further turns rather than quietly reconnecting.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSession_Send_AfterDispose_Throws()
    {
        // Arrange: a session that has been released
        var session = new InMemoryProviderSession(new ProviderSessionSeed(null, [], []), _ => new ProviderTurn("x"), 1000);
        await session.DisposeAsync();

        // Act / Assert: the turn is refused
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            session.SendAsync("hi", TestContext.Current.CancellationToken));
    }
}
