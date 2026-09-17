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
        var seed = new ProviderSessionSeed(null, [], []);
        await using var session = new InMemoryProviderSession(seed, message => new ProviderTurn($"echo: {message}"), windowTokens: 1000);

        var turn = await session.SendAsync("hello", TestContext.Current.CancellationToken);

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
        var session = new InMemoryProviderSession(new ProviderSessionSeed(null, [], []), _ => new ProviderTurn("x"), 1000);

        await session.DisposeAsync();

        Assert.True(session.IsDisposed);
    }

    /// <summary>
    ///     Proves the session answers for its own window, as every adapter does: it reports what it
    ///     holds and the window it was given, both as a provider's own figures.
    /// </summary>
    [Fact]
    public void InMemoryProviderSession_Usage_AnswersForItsOwnWindow()
    {
        var seed = new ProviderSessionSeed(null, [], []);

        var session = new InMemoryProviderSession(seed, _ => new ProviderTurn("x"), 1000);

        Assert.Equal(ContextUsageOrigin.Provider, session.CurrentUsage.Origin);
        Assert.Equal(1000, session.CurrentUsage.WindowTokens);
    }

    /// <summary>
    ///     Proves a canceled turn leaves the session exactly as it was, with no ghost message.
    /// </summary>
    [Fact]
    public async Task InMemoryProviderSession_CanceledTurn_RecordsNothing()
    {
        var session = new InMemoryProviderSession(new ProviderSessionSeed(null, [], []), _ => new ProviderTurn("x"), 1000);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

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
        var factory = new InMemoryProviderSessionFactory(windowTokens: 1000);

        await factory.CreateAsync(new ProviderSessionSeed(null, [], []), TestContext.Current.CancellationToken);
        await factory.CreateAsync(new ProviderSessionSeed(null, [], []), TestContext.Current.CancellationToken);

        Assert.Equal(2, factory.Sessions.Count);
    }
}
