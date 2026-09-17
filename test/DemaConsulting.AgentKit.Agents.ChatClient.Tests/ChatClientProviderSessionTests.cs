using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     Unit tests for <see cref="ChatClientProviderSession"/>: the seeded message list, the
///     conversation resent on every turn, the provider's own occupancy figure, the transcript
///     entries a turn produces, and release.
/// </summary>
public class ChatClientProviderSessionTests
{
    /// <summary>
    ///     The window every session here is given, small enough that a reported figure is readable
    ///     and large enough that no test accidentally sits at the limit.
    /// </summary>
    private const int Window = 1000;

    /// <summary>
    ///     Proves a seed becomes the message list a stateless provider expects: the instructions
    ///     first as a system message, then the history in the order the engine produced it, then the
    ///     message being sent, with the seeded tools offered alongside.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSession_Send_Seed_ProducesInstructionsHistoryAndToolsInOrder()
    {
        // Arrange: a seed carrying instructions, one tool, and a history holding every entry kind a
        // rotation can seed - a user message, an answer, and a tool call with its result
        var client = RecordingChatClient.Answering("answer", inputTokens: 100);
        var tool = AIFunctionFactory.Create(() => "ok", "probe");
        var seed = new ProviderSessionSeed(
            "be brief",
            [tool],
            [
                TranscriptEntry.User("u1"),
                TranscriptEntry.Assistant("a1"),
                TranscriptEntry.ToolCall("call-1", "probe()"),
                TranscriptEntry.ToolResult("call-1", "42"),
            ]);
        await using var session = new ChatClientProviderSession(client, seed, Window);

        // Act: take a turn, which is what sends the seeded list to the provider
        await session.SendAsync("next", TestContext.Current.CancellationToken);

        // Assert: the system message leads, the history follows in order, the new message is last,
        // and the tool the seed carried is offered as a tool rather than as conversation
        Assert.Equal(
            ["system:be brief", "user:u1", "assistant:a1", "assistant:probe()", "tool:42", "user:next"],
            Rendered(client.LastMessages));
        Assert.Equal(
            [tool],
            client.Requests[0].Options?.Tools ?? []);
    }

    /// <summary>
    ///     Proves a seed carrying no instructions sends no system message, so the history is not
    ///     preceded by an empty instruction a provider would have to interpret.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSession_Send_SeedWithoutInstructions_SendsNoSystemMessage()
    {
        // Arrange: a seed with no instructions and no tools
        var client = RecordingChatClient.Answering("answer", inputTokens: 100);
        await using var session = new ChatClientProviderSession(client, EmptySeed(), Window);

        // Act: take a turn
        await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Assert: the conversation is the message alone, and no tools are offered
        Assert.Equal(["user:hello"], Rendered(client.LastMessages));
        Assert.Null(client.Requests[0].Options);
    }

    /// <summary>
    ///     Proves the adapter carries the whole conversation itself: a second turn sends the first
    ///     exchange again along with the new message, which is what a provider holding no
    ///     conversation of its own requires.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSession_Send_SecondTurn_SendsTheWholeConversationAgain()
    {
        // Arrange: a client answering two turns distinguishably
        var client = new RecordingChatClient()
            .Queue("first answer", inputTokens: 100)
            .Queue("second answer", inputTokens: 200);
        await using var session = new ChatClientProviderSession(client, EmptySeed(), Window);

        // Act: take two turns
        await session.SendAsync("first", TestContext.Current.CancellationToken);
        await session.SendAsync("second", TestContext.Current.CancellationToken);

        // Assert: the second request repeats the whole first exchange - the message and the answer
        // the provider gave - before the new message, so the provider is sent again a history it
        // has already seen rather than an isolated turn
        Assert.Equal(["user:first"], Rendered(client.Requests[0].Messages));
        Assert.Equal(
            ["user:first", "assistant:first answer", "user:second"],
            Rendered(client.Requests[1].Messages));
    }

    /// <summary>
    ///     Proves the occupancy figure is the provider's own: the input tokens it counted for the
    ///     request it just answered, reported against the window this session was given.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSession_CurrentUsage_AfterTurn_ReportsTheProvidersInputTokens()
    {
        // Arrange: a client reporting a figure no estimate would arrive at
        var client = new RecordingChatClient()
            .Queue("answer", inputTokens: 137)
            .Queue("answer", inputTokens: 461);
        await using var session = new ChatClientProviderSession(client, EmptySeed(), Window);

        // Act: take two turns, reading the usage after each
        await session.SendAsync("first", TestContext.Current.CancellationToken);
        var afterFirst = session.CurrentUsage;
        await session.SendAsync("second", TestContext.Current.CancellationToken);
        var afterSecond = session.CurrentUsage;

        // Assert: each reading is the figure the provider reported for that turn, taken against the
        // supplied window and attributed wholly to the conversation
        Assert.Equal(137, afterFirst.UsedTokens);
        Assert.Equal(137, afterFirst.ConversationTokens);
        Assert.Equal(Window, afterFirst.WindowTokens);
        Assert.Equal(461, afterSecond.UsedTokens);
        Assert.Equal(461, afterSecond.ConversationTokens);
    }

    /// <summary>
    ///     Proves a session that has sent nothing reports occupying nothing, including immediately
    ///     after a rotation, when the replacement holds a seed the provider has not yet seen.
    /// </summary>
    /// <remarks>
    ///     A stateless provider receives the conversation with the request, so until a request has
    ///     been made the provider holds none of it. The seed here is the one a rotation produces - a
    ///     consolidated record and a verbatim turn - precisely so the reading is taken in the state a
    ///     replacement session is created in.
    /// </remarks>
    [Fact]
    public void ChatClientProviderSession_CurrentUsage_BeforeFirstTurn_IsZero()
    {
        // Arrange: a session seeded as a rotation seeds a replacement, with nothing sent yet
        var client = RecordingChatClient.Answering("answer", inputTokens: 500);
        var seed = new ProviderSessionSeed(
            "be brief",
            [],
            [TranscriptEntry.ContextRecord("what happened earlier"), TranscriptEntry.User("recent")]);
        var session = new ChatClientProviderSession(client, seed, Window);

        // Act: read the usage before any turn has been taken
        var usage = session.CurrentUsage;

        // Assert: nothing is occupied, out of the window the session was given
        Assert.Equal(0, usage.UsedTokens);
        Assert.Equal(0, usage.ConversationTokens);
        Assert.Equal(Window, usage.WindowTokens);
        Assert.Empty(client.Requests);
    }

    /// <summary>
    ///     Proves a provider that has overrun its own window is reported as full rather than as a
    ///     figure the usage shape refuses, because being at the limit is the truthful reading and the
    ///     one that rotates.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSession_CurrentUsage_ProviderOverrunsTheWindow_ReportsFull()
    {
        // Arrange: a client reporting more input than the window holds
        var client = RecordingChatClient.Answering("answer", inputTokens: Window * 5);
        await using var session = new ChatClientProviderSession(client, EmptySeed(), Window);

        // Act: take a turn and read the usage
        await session.SendAsync("hello", TestContext.Current.CancellationToken);
        var usage = session.CurrentUsage;

        // Assert: the reading is the window itself, which the usage shape accepts and a rotation acts on
        Assert.Equal(Window, usage.UsedTokens);
        Assert.Equal(Window, usage.ConversationTokens);
    }

    /// <summary>
    ///     Proves a provider that answers without reporting usage is refused, with a message an
    ///     application can act on, rather than estimated around.
    /// </summary>
    /// <remarks>
    ///     Knowing when the window is filling is the one thing this library needs a token count for,
    ///     so an absent figure is something wrong the application can see and fix. The message is
    ///     asserted, not merely the exception type, because a refusal nobody can act on is no better
    ///     than a guess.
    /// </remarks>
    [Fact]
    public async Task ChatClientProviderSession_Send_ProviderReportsNoUsage_Throws()
    {
        // Arrange: a client answering normally but reporting no usage at all
        var client = new RecordingChatClient()
            .Queue(RecordingChatClient.AnswerWithoutUsage("answer"));
        await using var session = new ChatClientProviderSession(client, EmptySeed(), Window);

        // Act: take a turn
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("hello", TestContext.Current.CancellationToken));

        // Assert: the refusal names the missing fact and what the application can do about it
        Assert.Contains("token usage", exception.Message, StringComparison.Ordinal);
        Assert.Contains("chat client that reports usage", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a tool call and its result are recorded as the paired transcript entries this
    ///     library's transcript expects, each carrying the provider's own call identifier so a
    ///     rotation can keep them together.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSession_Send_ToolCallAndResult_AreRecordedAsAPairCarryingTheCallId()
    {
        // Arrange: a provider answering with a tool call, its result, and then the answer, as a
        // function-invocation loop produces
        var client = new RecordingChatClient().Queue(
            RecordingChatClient.Answer(
                [
                    new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-7", "probe", new Dictionary<string, object?> { ["path"] = "notes.md" })]),
                    new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-7", "42")]),
                    new ChatMessage(ChatRole.Assistant, "done"),
                ],
                inputTokens: 100));
        await using var session = new ChatClientProviderSession(client, EmptySeed(), Window);

        // Act: take the turn
        var turn = await session.SendAsync("look it up", TestContext.Current.CancellationToken);

        // Assert: the call and the result are a pair under one identifier, the call records what was
        // asked, and the answer closes the turn
        Assert.Collection(
            turn.Entries,
            entry =>
            {
                Assert.Equal(TranscriptEntryKind.ToolCall, entry.Kind);
                Assert.Equal("call-7", entry.ToolCallId);
                Assert.Equal("probe(path: notes.md)", entry.Text);
            },
            entry =>
            {
                Assert.Equal(TranscriptEntryKind.ToolResult, entry.Kind);
                Assert.Equal("call-7", entry.ToolCallId);
                Assert.Equal("42", entry.Text);
            },
            entry =>
            {
                Assert.Equal(TranscriptEntryKind.AssistantMessage, entry.Kind);
                Assert.Equal("done", entry.Text);
            });
    }

    /// <summary>
    ///     Proves an answer delivered alongside a tool call is recorded once rather than twice.
    /// </summary>
    /// <remarks>
    ///     A provider may put its answer on the same message as the call it is making, and that
    ///     message is not the end of the turn: a tool result follows it. Recording the message's text
    ///     as an assistant entry there, and then recording the turn's answer as the final entry,
    ///     would show the model saying the same thing twice and bill the conversation for it twice.
    /// </remarks>
    [Fact]
    public async Task ChatClientProviderSession_Send_AnswerAlongsideAToolCall_IsNotRecordedTwice()
    {
        // Arrange: a provider whose tool-calling message also carries the answer, with the tool
        // result arriving after it
        var client = new RecordingChatClient().Queue(
            RecordingChatClient.Answer(
                [
                    new ChatMessage(ChatRole.Assistant, [new TextContent("done"), new FunctionCallContent("call-3", "probe", null)]),
                    new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-3", "42")]),
                ],
                inputTokens: 100));
        await using var session = new ChatClientProviderSession(client, EmptySeed(), Window);

        // Act: take the turn
        var turn = await session.SendAsync("look it up", TestContext.Current.CancellationToken);

        // Assert: the answer appears exactly once, as the entry closing the turn, after the pair
        Assert.Equal("done", turn.ResponseText);
        Assert.Equal(
            [TranscriptEntryKind.ToolCall, TranscriptEntryKind.ToolResult, TranscriptEntryKind.AssistantMessage],
            turn.Entries.Select(entry => entry.Kind));
        Assert.Single(turn.Entries, entry => entry.Text == "done");
    }

    /// <summary>
    ///     Proves a tool call taking no arguments is recorded as the call it was, rather than
    ///     dropped or rendered as something a reader has to decode.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSession_Send_ToolCallWithoutArguments_RecordsTheBareCall()
    {
        // Arrange: a provider calling a tool that takes nothing
        var client = new RecordingChatClient().Queue(
            RecordingChatClient.Answer(
                [
                    new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-9", "probe", null)]),
                    new ChatMessage(ChatRole.Assistant, "done"),
                ],
                inputTokens: 100));
        await using var session = new ChatClientProviderSession(client, EmptySeed(), Window);

        // Act: take the turn
        var turn = await session.SendAsync("go", TestContext.Current.CancellationToken);

        // Assert: the call is recorded by name with empty arguments
        Assert.Equal("probe()", turn.Entries[0].Text);
    }

    /// <summary>
    ///     Proves the session marks itself spent on release and refuses later turns rather than
    ///     starting a fresh conversation against the provider.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSession_Dispose_MarksReleasedAndRejectsLaterTurns()
    {
        // Arrange: a session that has taken a turn, so there is a conversation to forget
        var client = RecordingChatClient.Answering("answer", inputTokens: 100);
        var session = new ChatClientProviderSession(client, EmptySeed(), Window);
        await session.SendAsync("hello", TestContext.Current.CancellationToken);

        // Act: release it
        await session.DisposeAsync();

        // Assert: it reports itself released, refuses another turn, and sent nothing further
        Assert.True(session.IsReleased);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => session.SendAsync("again", TestContext.Current.CancellationToken));
        Assert.Single(client.Requests);
    }

    /// <summary>
    ///     Proves releasing a session does not dispose the client it was given.
    /// </summary>
    /// <remarks>
    ///     The client is the application's, is shared by every session a rotation creates, and
    ///     outlives all of them. A session that disposed it would make the first rotation the last:
    ///     the replacement would be created over a client that had already been shut down.
    /// </remarks>
    [Fact]
    public async Task ChatClientProviderSession_Dispose_DoesNotDisposeTheSuppliedClient()
    {
        // Arrange: one client, as a rotation shares one across sessions
        var client = RecordingChatClient.Answering("answer", inputTokens: 100);
        var first = new ChatClientProviderSession(client, EmptySeed(), Window);
        await first.SendAsync("hello", TestContext.Current.CancellationToken);

        // Act: release that session and take a turn on a replacement over the same client
        await first.DisposeAsync();
        await using var replacement = new ChatClientProviderSession(client, EmptySeed(), Window);
        var turn = await replacement.SendAsync("hello again", TestContext.Current.CancellationToken);

        // Assert: the client was never disposed and still answers
        Assert.Equal(0, client.DisposeCount);
        Assert.Equal("answer", turn.ResponseText);
    }

    /// <summary>
    ///     Proves releasing twice is permitted, because a rotation and a disposal may both release
    ///     the same session.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSession_Dispose_Twice_IsPermitted()
    {
        var session = new ChatClientProviderSession(
            RecordingChatClient.Answering("answer", inputTokens: 100), EmptySeed(), Window);

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.True(session.IsReleased);
    }

    /// <summary>
    ///     Proves a canceled turn is refused before anything is sent, so a canceled send leaves no
    ///     request behind.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSession_Send_Canceled_ThrowsAndSendsNothing()
    {
        var client = RecordingChatClient.Answering("answer", inputTokens: 100);
        await using var session = new ChatClientProviderSession(client, EmptySeed(), Window);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => session.SendAsync("hello", cancellation.Token));

        Assert.Empty(client.Requests);
    }

    /// <summary>
    ///     Proves a missing client is refused where the application composed it.
    /// </summary>
    [Fact]
    public void ChatClientProviderSession_Constructor_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ChatClientProviderSession(null!, EmptySeed(), Window));
    }

    /// <summary>
    ///     Proves a missing seed is refused: a session with nothing to start from cannot be built.
    /// </summary>
    [Fact]
    public void ChatClientProviderSession_Constructor_NullSeed_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ChatClientProviderSession(new RecordingChatClient(), null!, Window));
    }

    /// <summary>
    ///     Proves a window that is not positive is refused, because every occupancy reading is taken
    ///     against it and a window of nothing describes no provider.
    /// </summary>
    /// <param name="windowTokens">The rejected window.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ChatClientProviderSession_Constructor_NonPositiveWindow_Throws(int windowTokens)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChatClientProviderSession(new RecordingChatClient(), EmptySeed(), windowTokens));
    }

    /// <summary>
    ///     Proves a missing message is refused rather than sent to the provider as nothing.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSession_Send_NullMessage_Throws()
    {
        var client = RecordingChatClient.Answering("answer", inputTokens: 100);
        await using var session = new ChatClientProviderSession(client, EmptySeed(), Window);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => session.SendAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Builds a seed carrying nothing, for a test about something other than seeding.
    /// </summary>
    /// <returns>A seed with no instructions, tools or history.</returns>
    private static ProviderSessionSeed EmptySeed() => new(null, [], []);

    /// <summary>
    ///     Renders a conversation as role-and-text strings, so an ordering assertion reads as the
    ///     conversation it describes.
    /// </summary>
    /// <param name="messages">The conversation to render.</param>
    /// <returns>One string per message.</returns>
    private static IEnumerable<string> Rendered(IEnumerable<ChatMessage> messages) =>
        messages.Select(message => $"{message.Role.Value}:{message.Text}");
}
