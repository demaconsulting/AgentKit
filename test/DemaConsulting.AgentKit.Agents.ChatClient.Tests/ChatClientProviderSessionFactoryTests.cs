using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     Unit tests for <see cref="ChatClientProviderSessionFactory"/>: the sessions it creates, the
///     client and window they carry, the pipeline it builds around that client, and
///     construction-time validation.
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
    ///     Proves the occupancy a tool-using turn reports is the last request's prompt, not the sum
    ///     of every request the turn made.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is the reading the whole session engine turns on, and it is the one a
    ///     tool-calling turn is most easily wrong about. The tool-invoking layer the factory
    ///     installs answers a tool call and asks again, and the response it finally returns carries
    ///     usage summed across every request it made. A session reading that figure would have a
    ///     tool-using agent - which is every agent this library exists for - believe its window was
    ///     full on its first turn, rotate on every turn after it, and eventually discard history to
    ///     reclaim room it never occupied.
    ///     </para>
    ///     <para>
    ///     The scripted sizes are chosen so the right answer and the wrong one cannot be confused:
    ///     the last prompt is 150 and the sum is 250, and both are asserted - the second as the
    ///     figure this must not be.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ChatClientProviderSessionFactory_CreateAsync_ToolUsingTurn_ReportsTheLastRequestsPromptNotTheSum()
    {
        // Arrange: a provider that calls a tool and then answers, reporting a different prompt size
        // for each of the two requests that takes, and a session seeded with the tool
        var client = new RecordingChatClient()
            .Queue(
                RecordingChatClient.Answer(
                    [new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "probe", null)])],
                    inputTokens: 100))
            .Queue("done", inputTokens: 150);
        var factory = new ChatClientProviderSessionFactory(client, Window);
        var seed = new ProviderSessionSeed(null, [AIFunctionFactory.Create(() => "42", "probe")], []);

        // Act: take the turn and read the occupancy
        await using var session = await factory.CreateAsync(seed, TestContext.Current.CancellationToken);
        await session.SendAsync("look it up", TestContext.Current.CancellationToken);
        var usage = session.CurrentUsage;

        // Assert: the turn really did make two requests, and the occupancy is the last one's prompt
        // rather than the total the turn was billed for
        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(150, usage.UsedTokens);
        Assert.Equal(150, usage.ConversationTokens);
        Assert.NotEqual(100 + 150, usage.UsedTokens);
    }

    /// <summary>
    ///     Proves the factory installs tool invocation: a seeded tool is actually run, and its
    ///     result reaches the transcript the turn produces.
    /// </summary>
    /// <remarks>
    ///     A session seeds its tools into every request it sends, so a bare client would emit tool
    ///     calls that nothing answers: the model would wait for a result that never came, and the
    ///     transcript would record a call with no result beside it. The factory owns that placement
    ///     precisely so a correct-looking composition cannot be silently wrong, which is only
    ///     observable by watching the tool run.
    /// </remarks>
    [Fact]
    public async Task ChatClientProviderSessionFactory_CreateAsync_SeededTool_IsInvokedAndItsResultReachesTheTranscript()
    {
        // Arrange: a tool that records being run, and a provider that calls it and then answers
        var invocations = 0;
        var tool = AIFunctionFactory.Create(
            () =>
            {
                invocations++;
                return "42";
            },
            "probe");
        var client = new RecordingChatClient()
            .Queue(
                RecordingChatClient.Answer(
                    [new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "probe", null)])],
                    inputTokens: 100))
            .Queue("done", inputTokens: 150);
        var factory = new ChatClientProviderSessionFactory(client, Window);
        var seed = new ProviderSessionSeed(null, [tool], []);

        // Act: take the turn
        await using var session = await factory.CreateAsync(seed, TestContext.Current.CancellationToken);
        var turn = await session.SendAsync("look it up", TestContext.Current.CancellationToken);

        // Assert: the tool ran once, the result went back to the provider in a further request, and
        // the call and its result are both recorded in the transcript alongside the answer
        Assert.Equal(1, invocations);
        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(
            [TranscriptEntryKind.ToolCall, TranscriptEntryKind.ToolResult, TranscriptEntryKind.AssistantMessage],
            turn.Entries.Select(entry => entry.Kind));
        Assert.Equal("probe()", turn.Entries[0].Text);
        Assert.Equal("42", turn.Entries[1].Text);
        Assert.Equal("done", turn.ResponseText);
    }

    /// <summary>
    ///     Proves a session's provider pipeline promotes a tool-returned image onto a user message,
    ///     so a provider whose tool-result channel cannot carry one still sees it.
    /// </summary>
    /// <remarks>
    ///     A session is as exposed to the silent-drop asymmetry as an agent is: the provider answers
    ///     describing an image it never received, and nothing reports an error. This asserts the
    ///     decorator is installed beneath the function-invocation loop, where it can observe the
    ///     tool result.
    /// </remarks>
    [Fact]
    public async Task ChatClientProviderSessionFactory_CreateAsync_ToolReturningAnImage_PromotesItOntoAUserMessage()
    {
        // Arrange: a tool returning image content, and a provider that calls it and then answers
        var image = new DataContent("data:image/png;base64,iVBORw0KGgo="u8.ToArray(), "image/png");
        var tool = AIFunctionFactory.Create(() => image, "capture");
        var client = new RecordingChatClient()
            .Queue(
                RecordingChatClient.Answer(
                    [new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "capture", null)])],
                    inputTokens: 100))
            .Queue("a red square", inputTokens: 150);
        var factory = new ChatClientProviderSessionFactory(client, Window);
        var seed = new ProviderSessionSeed(null, [tool], []);

        // Act: take a turn that runs the tool
        await using var session = await factory.CreateAsync(seed, TestContext.Current.CancellationToken);
        await session.SendAsync("what do you see", TestContext.Current.CancellationToken);

        // Assert: the follow-up request carries the image on a user message, not only in the
        // tool result the provider would have dropped
        var followUp = client.Requests[1].Messages;
        var promoted = followUp
            .Where(message => message.Role == ChatRole.User)
            .SelectMany(message => message.Contents)
            .OfType<DataContent>()
            .ToList();
        Assert.Contains(promoted, part => part.HasTopLevelMediaType("image"));
    }

    /// <summary>
    ///     Proves a created session reads its own occupancy rather than one a previous session left
    ///     behind, which is what makes a replacement report occupying nothing until it has spoken.
    /// </summary>
    /// <remarks>
    ///     The factory builds the recording pipeline once per session for this reason. Shared, it
    ///     would hand a replacement the figure that provoked the rotation - so the engine would
    ///     rotate a session that had sent nothing, again and again - and two sessions run at once
    ///     would overwrite each other's reading, which the concurrency this contract promises does
    ///     not allow.
    /// </remarks>
    [Fact]
    public async Task ChatClientProviderSessionFactory_CreateAsync_AfterAnEarlierSessionSpoke_TheReplacementOccupiesNothing()
    {
        // Arrange: one factory, and a first session that has taken a turn filling the window
        var client = RecordingChatClient.Answering("answer", inputTokens: 3000);
        var factory = new ChatClientProviderSessionFactory(client, Window);
        var first = await factory.CreateAsync(EmptySeed(), TestContext.Current.CancellationToken);
        await first.SendAsync("hello", TestContext.Current.CancellationToken);
        var occupiedBefore = first.CurrentUsage.UsedTokens;
        await first.DisposeAsync();

        // Act: create the replacement a rotation would, and read its occupancy before it speaks
        await using var replacement = await factory.CreateAsync(
            new ProviderSessionSeed(null, [], [TranscriptEntry.ContextRecord("what happened earlier")]),
            TestContext.Current.CancellationToken);
        var usage = replacement.CurrentUsage;

        // Assert: the replacement occupies nothing, rather than what its predecessor occupied
        Assert.Equal(3000, occupiedBefore);
        Assert.Equal(0, usage.UsedTokens);
        Assert.Equal(Window, usage.WindowTokens);
    }

    /// <summary>
    ///     Proves a missing client is refused where the application configured its provider.
    /// </summary>
    [Fact]
    public void ChatClientProviderSessionFactory_Constructor_NullClient_Throws()
    {
        // Act / Assert: the omission is refused where the application configured its provider
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
        // Act / Assert: a window of zero or below would make every session report itself full
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
