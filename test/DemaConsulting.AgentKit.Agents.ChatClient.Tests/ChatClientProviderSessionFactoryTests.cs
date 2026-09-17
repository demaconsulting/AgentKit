using DemaConsulting.AgentKit.Core;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     Unit tests for <see cref="ChatClientProviderSessionFactory"/>: the sessions it creates, the
///     client and window they carry, and construction-time validation.
/// </summary>
public class ChatClientProviderSessionFactoryTests
{
    /// <summary>
    ///     The window the factory is configured with, distinct from any default so a session
    ///     reporting it can only have got it from here.
    /// </summary>
    private const int Window = 4096;

    /// <summary>
    ///     Proves a created session carries the factory's client and the factory's window: it
    ///     reports that window, and its turns reach that client.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSessionFactory_CreateAsync_CreatesSessionCarryingItsClientAndWindow()
    {
        // Arrange: a factory over one client and one stated window
        var client = RecordingChatClient.Answering("answer", inputTokens: 300);
        var factory = new ChatClientProviderSessionFactory(client, Window);

        // Act: create a session and take a turn on it
        await using var session = await factory.CreateAsync(EmptySeed(), TestContext.Current.CancellationToken);
        await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Assert: the turn reached the factory's client, and the occupancy is reported against the
        // factory's window rather than any window of the session's own choosing
        Assert.Equal(Window, factory.WindowTokens);
        Assert.Equal(Window, session.CurrentUsage.WindowTokens);
        Assert.Single(client.Requests);
        Assert.Equal(["user:hello"], client.LastMessages.Select(message => $"{message.Role.Value}:{message.Text}"));
    }

    /// <summary>
    ///     Proves the seed reaches the created session, which is what makes a replacement continue
    ///     the conversation rather than start one.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSessionFactory_CreateAsync_PassesTheSeedToTheSession()
    {
        // Arrange: a factory and the seed a rotation would hand it
        var client = RecordingChatClient.Answering("answer", inputTokens: 300);
        var factory = new ChatClientProviderSessionFactory(client, Window);
        var seed = new ProviderSessionSeed(
            "be brief",
            [],
            [TranscriptEntry.ContextRecord("what happened earlier")]);

        // Act: create a session from it and take a turn
        await using var session = await factory.CreateAsync(seed, TestContext.Current.CancellationToken);
        await session.SendAsync("next", TestContext.Current.CancellationToken);

        // Assert: the provider received the seeded instructions and record before the new message
        Assert.Equal(
            ["system:be brief", "assistant:what happened earlier", "user:next"],
            client.LastMessages.Select(message => $"{message.Role.Value}:{message.Text}"));
    }

    /// <summary>
    ///     Proves each call yields a fresh session over the same client, which is exactly what a
    ///     rotation asks for: a new conversation, and the client the application configured once.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSessionFactory_CreateAsync_EachCall_ReturnsADistinctSessionOverTheSameClient()
    {
        // Arrange: one factory over one client
        var client = RecordingChatClient.Answering("answer", inputTokens: 300);
        var factory = new ChatClientProviderSessionFactory(client, Window);

        // Act: create two sessions and take a turn on each
        await using var first = await factory.CreateAsync(EmptySeed(), TestContext.Current.CancellationToken);
        await using var second = await factory.CreateAsync(EmptySeed(), TestContext.Current.CancellationToken);
        await first.SendAsync("first", TestContext.Current.CancellationToken);
        await second.SendAsync("second", TestContext.Current.CancellationToken);

        // Assert: they are separate sessions holding separate conversations, both carried on the one
        // client the factory holds
        Assert.NotSame(first, second);
        Assert.Equal(["user:first"], Rendered(client.Requests[0]));
        Assert.Equal(["user:second"], Rendered(client.Requests[1]));
    }

    /// <summary>
    ///     Proves a missing client is refused where the application configured its provider.
    /// </summary>
    [Fact]
    public void ChatClientProviderSessionFactory_Constructor_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ChatClientProviderSessionFactory(null!, Window));
    }

    /// <summary>
    ///     Proves a window that is not positive is refused, because every session this factory makes
    ///     would report an occupancy against it.
    /// </summary>
    /// <param name="windowTokens">The rejected window.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ChatClientProviderSessionFactory_Constructor_NonPositiveWindow_Throws(int windowTokens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChatClientProviderSessionFactory(new RecordingChatClient(), windowTokens));
    }

    /// <summary>
    ///     Proves a missing seed is refused rather than turned into a session starting from nothing.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSessionFactory_CreateAsync_NullSeed_Throws()
    {
        var factory = new ChatClientProviderSessionFactory(new RecordingChatClient(), Window);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => factory.CreateAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a canceled creation yields no session, so a canceled rotation leaves nothing
    ///     unowned.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSessionFactory_CreateAsync_Canceled_Throws()
    {
        var factory = new ChatClientProviderSessionFactory(new RecordingChatClient(), Window);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => factory.CreateAsync(EmptySeed(), cancellation.Token));
    }

    /// <summary>
    ///     Builds a seed carrying nothing, for a test about something other than seeding.
    /// </summary>
    /// <returns>A seed with no instructions, tools or history.</returns>
    private static ProviderSessionSeed EmptySeed() => new(null, [], []);

    /// <summary>
    ///     Renders a recorded request's conversation as role-and-text strings.
    /// </summary>
    /// <param name="request">The request to render.</param>
    /// <returns>One string per message.</returns>
    private static IEnumerable<string> Rendered(RecordingChatClient.RecordedRequest request) =>
        request.Messages.Select(message => $"{message.Role.Value}:{message.Text}");
}
