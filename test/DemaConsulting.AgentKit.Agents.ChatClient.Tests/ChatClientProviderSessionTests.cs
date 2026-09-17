using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     Unit tests for <see cref="ChatClientProviderSession"/>: the seeded message list, the
///     conversation resent on every turn, the provider's own occupancy figure, the transcript
///     entries a turn produces, and release.
/// </summary>
/// <remarks>
///     Every session here is obtained from <see cref="ChatClientProviderSessionFactory"/>, because
///     that is the only way an application obtains one: the factory owns the pipeline the session
///     runs on - the prompt-size recorder directly around the caller's client, the image promoter
///     above the recorder, and function invocation above that - and a session constructed around a
///     bare client would be a session no application can have. The recording client therefore sits
///     at the bottom of that pipeline, so what it observes is what the provider would.
/// </remarks>
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
        await using var session = await CreateSessionAsync(client, seed);

        // Act: take a turn, which is what sends the seeded list to the provider
        await session.SendAsync("next", TestContext.Current.CancellationToken);

        // Assert: the system message leads, the history follows in order, the new message is last,
        // and the tool the seed carried is offered as a tool rather than as conversation
        Assert.Equal(
            [
                "system:be brief", "user:u1", "assistant:a1", "assistant:probe()",
                "assistant:Tool result: 42", "user:next",
            ],
            Rendered(client.LastMessages));
        Assert.Equal(
            [tool],
            client.Requests[0].Options?.Tools ?? []);
    }

    /// <summary>
    ///     Proves a seeded history carries no tool-role message, so no provider can drop a tool
    ///     result from it.
    /// </summary>
    /// <remarks>
    ///     A tool-role message is addressed by call identifier on the wire. One carrying only text
    ///     cannot be represented, and the OpenAI family drops it without an error rather than
    ///     failing - so the model would be seeded a conversation in which it called a tool and was
    ///     never told the answer, once per rotation, silently. Asserting the absence of the role is
    ///     what makes that unrepresentable, rather than asserting the text of the replacement.
    /// </remarks>
    [Fact]
    public async Task ChatClientProviderSession_Send_SeededToolResult_UsesNoToolRoleMessage()
    {
        // Arrange: a seed whose history holds a tool call and its result
        var client = RecordingChatClient.Answering("answer", inputTokens: 100);
        var seed = new ProviderSessionSeed(
            null,
            [],
            [
                TranscriptEntry.ToolCall("call-1", "probe()"),
                TranscriptEntry.ToolResult("call-1", "42"),
            ]);
        await using var session = await CreateSessionAsync(client, seed);

        // Act: take a turn, which sends the seeded list to the provider
        await session.SendAsync("next", TestContext.Current.CancellationToken);

        // Assert: nothing went out under the tool role, and the result is still present
        Assert.DoesNotContain(client.LastMessages, message => message.Role == ChatRole.Tool);
        Assert.Contains(client.LastMessages, message => message.Text.Contains("42", StringComparison.Ordinal));
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
        await using var session = await CreateSessionAsync(client, EmptySeed());

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
        await using var session = await CreateSessionAsync(client, EmptySeed());

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
        await using var session = await CreateSessionAsync(client, EmptySeed());

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
    public async Task ChatClientProviderSession_CurrentUsage_BeforeFirstTurn_IsZero()
    {
        // Arrange: a session seeded as a rotation seeds a replacement, with nothing sent yet
        var client = RecordingChatClient.Answering("answer", inputTokens: 500);
        var seed = new ProviderSessionSeed(
            "be brief",
            [],
            [TranscriptEntry.ContextRecord("what happened earlier"), TranscriptEntry.User("recent")]);
        await using var session = await CreateSessionAsync(client, seed);

        // Act: read the usage before any turn has been taken
        var usage = session.CurrentUsage;

        // Assert: nothing is occupied, out of the window the session was given
        Assert.Equal(0, usage.UsedTokens);
        Assert.Equal(0, usage.ConversationTokens);
        Assert.Equal(Window, usage.WindowTokens);
        Assert.Empty(client.Requests);
    }

    /// <summary>
    ///     Proves a provider that has overrun its own window is reported as it answered rather than
    ///     clamped to the window, so the overrun stays visible to an application.
    /// </summary>
    /// <remarks>
    ///     The usage shape accepts a figure past the window deliberately, and says why: clamping
    ///     would hide exactly the condition an application most needs to see. A session reporting
    ///     full whether it overran by forty tokens or by forty thousand tells its caller nothing.
    ///     The rotation decision is the same either way, so nothing is bought by the clamp.
    /// </remarks>
    [Fact]
    public async Task ChatClientProviderSession_CurrentUsage_ProviderOverrunsTheWindow_ReportsTheOverrun()
    {
        // Arrange: a client reporting five times more input than the window holds
        var client = RecordingChatClient.Answering("answer", inputTokens: Window * 5);
        await using var session = await CreateSessionAsync(client, EmptySeed());

        // Act: take a turn and read the usage
        await session.SendAsync("hello", TestContext.Current.CancellationToken);
        var usage = session.CurrentUsage;

        // Assert: the overrun is carried, not flattened to the window
        Assert.Equal(Window * 5, usage.UsedTokens);
        Assert.Equal(Window * 5, usage.ConversationTokens);
        Assert.Equal(Window, usage.WindowTokens);
    }

    /// <summary>
    ///     Proves a provider that reports usage once and then stops is refused on the turn that
    ///     stopped, rather than carrying the earlier figure forward.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The refusal has to mean "no request of <em>this</em> turn reported usage", not "no request
    ///     this session ever made reported any". Carried forward, a figure from turn one would hold
    ///     occupancy frozen below the rotation threshold while the conversation grew behind it - the
    ///     session would never rotate, and the provider would eventually truncate the history itself,
    ///     which is the failure this library exists to prevent.
    ///     </para>
    ///     <para>
    ///     The first turn is asserted to succeed so the test cannot pass by refusing everything.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ChatClientProviderSession_Send_ProviderStopsReportingUsage_RefusesThatTurn()
    {
        // Arrange: a client that reports usage on its first answer and none on its second
        var client = new RecordingChatClient()
            .Queue("first", inputTokens: 100)
            .Queue(RecordingChatClient.AnswerWithoutUsage("second"));
        await using var session = await CreateSessionAsync(client, EmptySeed());

        // Act: the first turn reports, so it stands
        await session.SendAsync("hello", TestContext.Current.CancellationToken);
        Assert.Equal(100, session.CurrentUsage.ConversationTokens);

        // Act: the second reports nothing, so it is refused rather than inheriting the first figure
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("again", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a turn the provider fails leaves the session exactly as it was, with no message
    ///     recorded for an answer that never came.
    /// </summary>
    /// <remarks>
    ///     The session contract promises a caller that a provider which fails or cancels before
    ///     taking the turn leaves the session untouched, and the in-memory provider models it
    ///     deliberately. Recording the outgoing message before the call leaves it behind with no
    ///     answer beside it, so the next turn sends it again - a message the engine has no record of,
    ///     which the model may answer and the application is billed for.
    /// </remarks>
    [Fact]
    public async Task ChatClientProviderSession_Send_ProviderFails_LeavesNoGhostMessage()
    {
        // Arrange: a client that fails the first turn and answers the second
        var client = new RecordingChatClient()
            .QueueFailure(new TimeoutException("the provider did not answer"))
            .Queue("answer", inputTokens: 100);
        await using var session = await CreateSessionAsync(client, EmptySeed());

        // Act: the first turn fails
        await Assert.ThrowsAsync<TimeoutException>(
            () => session.SendAsync("lost", TestContext.Current.CancellationToken));

        // Act: the next turn succeeds
        await session.SendAsync("kept", TestContext.Current.CancellationToken);

        // Assert: the failed turn's message was never sent again - the provider saw only the second
        var sent = client.LastMessages;
        Assert.DoesNotContain(sent, message => message.Text.Contains("lost", StringComparison.Ordinal));
        Assert.Contains(sent, message => message.Text.Contains("kept", StringComparison.Ordinal));
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
        await using var session = await CreateSessionAsync(client, EmptySeed());

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
        await using var session = await CreateSessionAsync(client, EmptySeed());

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
        await using var session = await CreateSessionAsync(client, EmptySeed());

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
    /// <remarks>
    ///     The call is scripted beside the result that answered it, because that is the only shape a
    ///     turn can have once the factory has installed function invocation: a call left unanswered
    ///     is one the tool-invoking layer would go back to the provider about, and no turn this
    ///     session sees ever ends holding one.
    /// </remarks>
    [Fact]
    public async Task ChatClientProviderSession_Send_ToolCallWithoutArguments_RecordsTheBareCall()
    {
        // Arrange: a provider calling a tool that takes nothing, and the result answering it
        var client = new RecordingChatClient().Queue(
            RecordingChatClient.Answer(
                [
                    new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-9", "probe", null)]),
                    new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-9", "42")]),
                    new ChatMessage(ChatRole.Assistant, "done"),
                ],
                inputTokens: 100));
        await using var session = await CreateSessionAsync(client, EmptySeed());

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
        var session = await CreateSessionAsync(client, EmptySeed());
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
        // Arrange: one factory over one client, as a rotation shares both across sessions
        var client = RecordingChatClient.Answering("answer", inputTokens: 100);
        var factory = new ChatClientProviderSessionFactory(client, Window);
        var first = await CreateSessionAsync(factory, EmptySeed());
        await first.SendAsync("hello", TestContext.Current.CancellationToken);

        // Act: release that session and take a turn on a replacement over the same client
        await first.DisposeAsync();
        await using var replacement = await CreateSessionAsync(factory, EmptySeed());
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
        var session = await CreateSessionAsync(
            RecordingChatClient.Answering("answer", inputTokens: 100), EmptySeed());

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
        await using var session = await CreateSessionAsync(client, EmptySeed());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => session.SendAsync("hello", cancellation.Token));

        Assert.Empty(client.Requests);
    }

    /// <summary>
    ///     Proves a missing message is refused rather than sent to the provider as nothing.
    /// </summary>
    [Fact]
    public async Task ChatClientProviderSession_Send_NullMessage_Throws()
    {
        var client = RecordingChatClient.Answering("answer", inputTokens: 100);
        await using var session = await CreateSessionAsync(client, EmptySeed());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => session.SendAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Builds the session an application gets: one obtained from
    ///     <see cref="ChatClientProviderSessionFactory"/>, which owns the pipeline the session runs
    ///     on. Construction is deliberately not exercised directly, because an application cannot
    ///     reach it.
    /// </summary>
    /// <param name="client">The client the factory talks to the provider with.</param>
    /// <param name="seed">What the session starts from.</param>
    /// <returns>The created session.</returns>
    private static Task<ChatClientProviderSession> CreateSessionAsync(
        IChatClient client,
        ProviderSessionSeed seed) =>
        CreateSessionAsync(new ChatClientProviderSessionFactory(client, Window), seed);

    /// <summary>
    ///     Creates a session from an existing factory, for a test that needs two sessions over the
    ///     one factory a rotation shares.
    /// </summary>
    /// <param name="factory">The factory to create from.</param>
    /// <param name="seed">What the session starts from.</param>
    /// <returns>The created session.</returns>
    private static async Task<ChatClientProviderSession> CreateSessionAsync(
        ChatClientProviderSessionFactory factory,
        ProviderSessionSeed seed) =>
        Assert.IsType<ChatClientProviderSession>(
            await factory.CreateAsync(seed, TestContext.Current.CancellationToken));

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
