using DemaConsulting.AgentKit.Core;
using GitHub.Copilot;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for <see cref="CopilotProviderSession"/>: what a turn records, what the session
///     reports about the runtime's window, which conditions it refuses rather than guesses around,
///     and what it does and does not release.
/// </summary>
/// <remarks>
///     Every session under test is obtained from <see cref="CopilotProviderSessionFactory"/> over a
///     fake channel, because that is the only way one exists: the constructor is internal and takes
///     a channel whose session was created with a matching observer already registered as its event
///     handler. No Copilot runtime is started and no credential is used.
/// </remarks>
public class CopilotProviderSessionTests
{
    /// <summary>
    ///     Proves the runtime's answer is returned to the caller and recorded exactly once, even
    ///     though it reaches this session twice — as an event the observer collects and as the value
    ///     the turn returns.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_Send_Answer_IsReturnedAndRecordedOnce()
    {
        // Arrange: a session whose one turn answers and reports its occupancy
        var runtime = Runtime(Turn(
            CopilotEvents.Usage(400, 8000),
            CopilotEvents.Assistant("the answer")));
        await using var session = await OpenAsync(runtime);

        // Act
        var turn = await session.SendAsync("a question", TestContext.Current.CancellationToken);

        // Assert: the answer came back, and the recorded history holds it once
        Assert.Equal("the answer", turn.ResponseText);
        var entry = Assert.Single(turn.Entries);
        Assert.Equal(TranscriptEntryKind.AssistantMessage, entry.Kind);
        Assert.Equal("the answer", entry.Text);
        Assert.Equal("a question", Assert.Single(runtime.Channels[0].Prompts));
    }

    /// <summary>
    ///     Proves tool traffic is recorded as the call-and-result pairs the transcript expects,
    ///     carrying the runtime's own call identifier so a rotation cannot separate them.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_Send_ToolCallAndResult_AreRecordedAsAPairCarryingTheRuntimesCallId()
    {
        // Arrange: a turn that calls a tool, gets a result, then answers
        var runtime = Runtime(Turn(
            CopilotEvents.ToolStart("call-7", "doc_read"),
            CopilotEvents.ToolComplete("call-7", "the contents"),
            CopilotEvents.Usage(900, 8000),
            CopilotEvents.Assistant("It says the contents.")));
        await using var session = await OpenAsync(runtime);

        // Act
        var turn = await session.SendAsync("what does it say?", TestContext.Current.CancellationToken);

        // Assert: call, result and answer, with the pair sharing the runtime's identifier
        Assert.Collection(
            turn.Entries,
            entry =>
            {
                Assert.Equal(TranscriptEntryKind.ToolCall, entry.Kind);
                Assert.Equal("call-7", entry.ToolCallId);
                Assert.Contains("doc_read", entry.Text, StringComparison.Ordinal);
            },
            entry =>
            {
                Assert.Equal(TranscriptEntryKind.ToolResult, entry.Kind);
                Assert.Equal("call-7", entry.ToolCallId);
                Assert.Equal("the contents", entry.Text);
            },
            entry => Assert.Equal("It says the contents.", entry.Text));
    }

    /// <summary>
    ///     Proves a nested agent's own tool traffic does not enter this conversation's history. The
    ///     model was never shown those messages as part of this conversation, so seeding them into a
    ///     replacement session would present it with a history it does not recognize.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_Send_NestedAgentEvents_AreNotRecorded()
    {
        // Arrange: a delegated run, whose child produces traffic of its own
        var runtime = Runtime(Turn(
            CopilotEvents.ToolStart("call-1", "agent_run"),
            CopilotEvents.ToolStart("nested-1", "doc_read", parentToolCallId: "call-1"),
            CopilotEvents.Assistant("nested answer", parentToolCallId: "call-1"),
            CopilotEvents.ToolComplete("call-1", "the child reported back"),
            CopilotEvents.Usage(900, 8000),
            CopilotEvents.Assistant("Delegated and done.")));
        await using var session = await OpenAsync(runtime);

        // Act
        var turn = await session.SendAsync("delegate this", TestContext.Current.CancellationToken);

        // Assert: this conversation's call, its result and the answer — and nothing the child did
        Assert.Equal(3, turn.Entries.Count);
        Assert.DoesNotContain(turn.Entries, entry => entry.Text.Contains("nested", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves the occupancy and the window both come from the runtime. This is the difference
    ///     that made a native Copilot adapter necessary: nothing is supplied by the application and
    ///     nothing is estimated.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_CurrentUsage_AfterTurn_ReportsTheRuntimesOccupancyAndLimit()
    {
        // Arrange: figures no estimate would arrive at
        var runtime = Runtime(Turn(
            CopilotEvents.Usage(currentTokens: 12_345, tokenLimit: 111_111),
            CopilotEvents.Assistant("ok")));
        await using var session = await OpenAsync(runtime);

        // Act
        await session.SendAsync("a question", TestContext.Current.CancellationToken);

        // Assert
        var usage = session.CurrentUsage;
        Assert.Equal(12_345, usage.UsedTokens);
        Assert.Equal(111_111, usage.WindowTokens);
    }

    /// <summary>
    ///     Proves the runtime's own split between the conversation and its overhead is passed
    ///     through rather than inferred. Every threshold comparison downstream is then made in
    ///     Copilot's own tokens, which is what <c>IProviderSession.CurrentUsage</c> asks of an
    ///     adapter that can distinguish the two.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_CurrentUsage_ConversationSplit_IsPassedThroughNotInferred()
    {
        // Arrange: a session whose overhead is most of what it occupies
        var runtime = Runtime(Turn(
            CopilotEvents.Usage(currentTokens: 5000, tokenLimit: 8000, conversationTokens: 1200),
            CopilotEvents.Assistant("ok")));
        await using var session = await OpenAsync(runtime);

        // Act
        await session.SendAsync("a question", TestContext.Current.CancellationToken);

        // Assert: the conversation is the runtime's figure, and the overhead is the difference
        var usage = session.CurrentUsage;
        Assert.Equal(1200, usage.ConversationTokens);
        Assert.Equal(3800, usage.OverheadTokens);
    }

    /// <summary>
    ///     Proves a runtime reporting no split is not given an invented one: the whole reading is
    ///     credited to the conversation, which allows no overhead at all and so rotates strictly
    ///     earlier than a correct split would.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_CurrentUsage_NoSplitReported_CreditsItAllToTheConversation()
    {
        // Arrange
        var runtime = Runtime(Turn(
            CopilotEvents.Usage(currentTokens: 5000, tokenLimit: 8000),
            CopilotEvents.Assistant("ok")));
        await using var session = await OpenAsync(runtime);

        // Act
        await session.SendAsync("a question", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5000, session.CurrentUsage.ConversationTokens);
        Assert.Equal(0, session.CurrentUsage.OverheadTokens);
    }

    /// <summary>
    ///     Proves a session that has taken no turn occupies nothing. The engine reads this at
    ///     construction, before Copilot has said anything, and a session that claimed to be full
    ///     there would rotate immediately and forever.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_CurrentUsage_BeforeFirstTurn_OccupiesNothing()
    {
        // Arrange: a replacement session, seeded as a rotation seeds one
        var runtime = Runtime(Turn(CopilotEvents.Usage(400, 8000), CopilotEvents.Assistant("ok")));
        await using var session = await OpenAsync(
            runtime,
            new ProviderSessionSeed(
                "be concise",
                [],
                [TranscriptEntry.ContextRecord("what happened earlier"), TranscriptEntry.User("and then")]));

        // Act / Assert: nothing occupied, and the rotation test - conversation below the threshold -
        // cannot be met by it
        var usage = session.CurrentUsage;
        Assert.Equal(0, usage.UsedTokens);
        Assert.Equal(0, usage.ConversationTokens);
        Assert.True(usage.WindowTokens > 0);
        Assert.Empty(runtime.Channels[0].Prompts);
    }

    /// <summary>
    ///     Proves figures beyond the range Core's usage shape accepts are clamped rather than thrown
    ///     on. Reading usage is documented as unable to fail, so a throw here would be a contract
    ///     violation — and this is the one place the runtime's wider counters are narrowed.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_CurrentUsage_LongFiguresBeyondIntRange_AreClamped()
    {
        // Arrange: a runtime reporting figures no real window would carry
        var runtime = Runtime(Turn(
            CopilotEvents.Usage(
                currentTokens: long.MaxValue,
                tokenLimit: long.MaxValue,
                conversationTokens: long.MaxValue),
            CopilotEvents.Assistant("ok")));
        await using var session = await OpenAsync(runtime);

        // Act
        await session.SendAsync("a question", TestContext.Current.CancellationToken);

        // Assert: held to the widest figure Core accepts, and still internally consistent
        var usage = session.CurrentUsage;
        Assert.Equal(int.MaxValue, usage.UsedTokens);
        Assert.Equal(int.MaxValue, usage.WindowTokens);
        Assert.Equal(int.MaxValue, usage.ConversationTokens);
    }

    /// <summary>
    ///     Proves a conversation figure larger than the total is held to the total rather than
    ///     rejected. Core refuses that combination — correctly, because it would enlarge the
    ///     effective window — and reading usage must not throw.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_CurrentUsage_ConversationBeyondTheTotal_IsHeldToIt()
    {
        // Arrange: a runtime reporting the two figures out of step
        var runtime = Runtime(Turn(
            CopilotEvents.Usage(currentTokens: 900, tokenLimit: 8000, conversationTokens: 5000),
            CopilotEvents.Assistant("ok")));
        await using var session = await OpenAsync(runtime);

        // Act
        await session.SendAsync("a question", TestContext.Current.CancellationToken);

        // Assert: no throw, and no negative overhead
        Assert.Equal(900, session.CurrentUsage.ConversationTokens);
        Assert.Equal(0, session.CurrentUsage.OverheadTokens);
    }

    /// <summary>
    ///     Proves a turn the runtime answered without ever reporting its occupancy is refused.
    ///     Knowing when the window is filling is the one thing the session engine needs a token count
    ///     for, and Copilot reports both figures itself — so an absent reading means something an
    ///     application can see and fix.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_Send_NoUsageReported_Throws()
    {
        // Arrange: an answer with no usage event anywhere in the turn
        var runtime = Runtime(Turn(CopilotEvents.Assistant("the answer")));
        await using var session = await OpenAsync(runtime);

        // Act / Assert: refused, naming the missing fact
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("a question", TestContext.Current.CancellationToken));
        Assert.Contains("token usage", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the next turn is refused once the runtime has compacted or truncated the session
    ///     itself. AgentKit asks for that to be switched off; if it happened anyway, the transcript
    ///     the engine believes it owns no longer describes what the provider holds, and every figure
    ///     afterwards is about a conversation that no longer exists.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_Send_ProviderCompactedOrTruncated_RefusesTheTurn()
    {
        // Arrange: a turn during which the runtime truncates the history itself
        var runtime = Runtime(Turn(
            CopilotEvents.Truncation(),
            CopilotEvents.Usage(400, 8000),
            CopilotEvents.Assistant("the answer")));
        await using var session = await OpenAsync(runtime);

        // Act / Assert: refused, naming the cause rather than answering from a diverged history
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("a question", TestContext.Current.CancellationToken));
        Assert.Contains("truncated", error.Message, StringComparison.Ordinal);
        Assert.Contains("compaction threshold raised well above", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a session that has observed a rewrite refuses the <em>next</em> turn without
    ///     sending it, rather than running it and reporting the divergence afterwards.
    /// </summary>
    /// <remarks>
    ///     Detecting the first rewrite is unavoidably after the fact. Every send after it is not: a
    ///     turn that reaches the runtime lets the model run this application's tools, with their
    ///     real side effects, against a conversation the engine no longer describes. A host that
    ///     responds to the first failure by retrying - the ordinary response to an
    ///     <see cref="InvalidOperationException"/> - would do it again on every attempt. Asserting
    ///     the prompt count is what distinguishes refusing from merely reporting.
    /// </remarks>
    [Fact]
    public async Task CopilotProviderSession_Send_AfterARewrite_RefusesWithoutSendingTheTurn()
    {
        // Arrange: a first turn the runtime truncates, and a second that would otherwise succeed
        var runtime = Runtime(
            Turn(
                CopilotEvents.Truncation(),
                CopilotEvents.Usage(400, 8000),
                CopilotEvents.Assistant("the answer")),
            Turn(
                CopilotEvents.Usage(500, 8000),
                CopilotEvents.Assistant("a later answer")));
        await using var session = await OpenAsync(runtime);

        // Act: the first turn is refused after the fact, then a second is attempted
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("a question", TestContext.Current.CancellationToken));
        var sentAfterFirstRefusal = runtime.Channels[0].Prompts.Count;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("another question", TestContext.Current.CancellationToken));

        // Assert: the second turn never reached the runtime
        Assert.Contains("truncated", error.Message, StringComparison.Ordinal);
        Assert.Equal(sentAfterFirstRefusal, runtime.Channels[0].Prompts.Count);
    }

    /// <summary>
    ///     Proves a session that went idle without answering is refused, and that the runtime's own
    ///     error is named. Recording an empty answer would present a runtime failure as a model with
    ///     nothing to say.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_Send_NoAssistantMessage_ThrowsNamingTheRuntimeError()
    {
        // Arrange: the runtime reports an error and produces no assistant message
        var runtime = Runtime(Turn(
            CopilotEvents.Usage(400, 8000),
            CopilotEvents.Error("model unavailable")));
        await using var session = await OpenAsync(runtime);

        // Act / Assert
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("a question", TestContext.Current.CancellationToken));
        Assert.Contains("model unavailable", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a turn the runtime processed but this session could not record ends the session,
    ///     rather than ending only that turn.
    /// </summary>
    /// <remarks>
    ///     Past the send, the runtime holds a turn the engine's transcript does not, and on a first
    ///     turn it has also consumed the seeded record that will not be sent again. Neither is
    ///     recoverable by retrying here, and retrying an <see cref="InvalidOperationException"/> is
    ///     the ordinary response - it would run this application's tools, with their side effects,
    ///     against a conversation the engine no longer describes. Asserting that the second attempt
    ///     never reaches the runtime is what distinguishes refusing from merely reporting.
    /// </remarks>
    [Fact]
    public async Task CopilotProviderSession_Send_AfterATurnItCouldNotRecord_RefusesWithoutSending()
    {
        // Arrange: a first turn the runtime answers without reporting usage, then one that would
        // otherwise succeed
        var runtime = Runtime(
            Turn(CopilotEvents.Assistant("an answer")),
            Turn(
                CopilotEvents.Usage(500, 8000),
                CopilotEvents.Assistant("a later answer")));
        await using var session = await OpenAsync(runtime);

        // Act: the first turn is refused after the fact, then a second is attempted
        var first = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("a question", TestContext.Current.CancellationToken));
        var sentAfterFirstRefusal = runtime.Channels[0].Prompts.Count;

        var second = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("another question", TestContext.Current.CancellationToken));

        // Assert: the second never reached the runtime, and the refusal names the original failure
        // rather than inventing a new one
        Assert.Contains("without reporting its token usage", first.Message, StringComparison.Ordinal);
        Assert.Equal(sentAfterFirstRefusal, runtime.Channels[0].Prompts.Count);
        Assert.Contains("could not be recorded", second.Message, StringComparison.Ordinal);
        Assert.Contains("without reporting its token usage", second.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a send that fails before the runtime accepts it still ends the session, so the
    ///     seeded record cannot be silently lost.
    /// </summary>
    /// <remarks>
    ///     The record is consumed as the message is handed to the runtime, and from that moment this
    ///     session cannot tell whether it arrived. Letting the caller retry would either repeat a
    ///     turn the runtime took or continue without the history the replacement was seeded with —
    ///     and the second is silent, because the conversation simply carries on having forgotten
    ///     everything before the rotation. Refusing is what turns that into something an application
    ///     can see.
    /// </remarks>
    [Fact]
    public async Task CopilotProviderSession_Send_FailedSendOfASeededTurn_EndsTheSession()
    {
        // Arrange: a session seeded with history, whose first send fails at the channel
        var runtime = new FakeCopilotRuntime(_ => [], beforeOpen: _ => { });
        var seed = new ProviderSessionSeed(null, [], [TranscriptEntry.User("earlier question")]);
        await using var session = await OpenAsync(runtime, seed);

        // Act: the first send fails because nothing was scripted for it
        await Assert.ThrowsAnyAsync<Exception>(
            () => session.SendAsync("a question", TestContext.Current.CancellationToken));

        // Assert: the session refuses further use rather than letting a retry proceed without the
        // seeded record it has already spent
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("a retry", TestContext.Current.CancellationToken));
        Assert.Contains("could not be recorded", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("will not be sent again", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a stated ceiling lowers the window the session accounts against.
    /// </summary>
    /// <remarks>
    ///     Copilot's smallest window is larger than any conversation a test would produce, so
    ///     without a ceiling a rotation is unreachable. Lowering the accounted window is what makes
    ///     one observable - and what lets a host prefer a consolidated record over a very long
    ///     context. It costs more rather than less, which the API documentation says plainly.
    /// </remarks>
    [Fact]
    public async Task CopilotProviderSession_CurrentUsage_StatedCeilingBelowTheRuntimes_LowersTheWindow()
    {
        // Arrange: a runtime reporting a large window, and a session told to account against less
        var runtime = Runtime(Turn(
            CopilotEvents.Usage(400, 272000),
            CopilotEvents.Assistant("the answer")));
        await using var session = await OpenAsync(runtime, maxWindowTokens: 8000);

        // Act
        await session.SendAsync("a question", TestContext.Current.CancellationToken);

        // Assert: the ceiling is what the session accounts against, not the runtime's figure
        Assert.Equal(8000, session.CurrentUsage.WindowTokens);
    }

    /// <summary>
    ///     Proves a ceiling above the runtime's own window is ignored rather than honored.
    /// </summary>
    /// <remarks>
    ///     This is the direction that matters. The runtime's figure is what the conversation may
    ///     actually reach, so accounting against a larger one would have the engine believe it has
    ///     room the provider will not give, and rotate too late - losing material rather than merely
    ///     costing money. Taking the smaller of the two makes the setting unable to do harm however
    ///     it is misused.
    /// </remarks>
    [Fact]
    public async Task CopilotProviderSession_CurrentUsage_StatedCeilingAboveTheRuntimes_IsIgnored()
    {
        // Arrange: a runtime reporting a small window, and a ceiling larger than it
        var runtime = Runtime(Turn(
            CopilotEvents.Usage(400, 8000),
            CopilotEvents.Assistant("the answer")));
        await using var session = await OpenAsync(runtime, maxWindowTokens: 272000);

        // Act
        await session.SendAsync("a question", TestContext.Current.CancellationToken);

        // Assert: the runtime's own window still governs
        Assert.Equal(8000, session.CurrentUsage.WindowTokens);
    }

    /// <summary>
    ///     Proves a session that went idle without answering and without reporting anything is still
    ///     refused, and says so rather than naming a cause it does not have.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_Send_NoAssistantMessageAndNoError_ThrowsSayingSo()
    {
        // Arrange
        var runtime = Runtime(Turn(CopilotEvents.Usage(400, 8000)));
        await using var session = await OpenAsync(runtime);

        // Act / Assert
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("a question", TestContext.Current.CancellationToken));
        Assert.Contains("reported no error", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing message is refused where the application wrote it.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_Send_NullMessage_Throws()
    {
        // Arrange
        var runtime = Runtime(Turn(CopilotEvents.Usage(400, 8000), CopilotEvents.Assistant("ok")));
        await using var session = await OpenAsync(runtime);

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => session.SendAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a canceled turn costs no call at all, so no message reaches the runtime that the
    ///     engine's transcript does not record.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_Send_Canceled_SendsNothing()
    {
        // Arrange
        var runtime = Runtime(Turn(CopilotEvents.Usage(400, 8000), CopilotEvents.Assistant("ok")));
        await using var session = await OpenAsync(runtime);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        // Act / Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.SendAsync("a question", canceled.Token));
        Assert.Empty(runtime.Channels[0].Prompts);
    }

    /// <summary>
    ///     Proves a turn the channel failed leaves nothing half-recorded, so the next turn records
    ///     its own work and no ghost of the one before it.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_Send_ChannelFails_EndsTheSessionAndHandsNothingBack()
    {
        // Arrange: a first turn that produced tool traffic and then failed, and a second that would
        // have worked
        var runtime = Runtime(
            new ScriptedTurn(
                [CopilotEvents.ToolStart("call-1", "doc_read"), CopilotEvents.Usage(400, 8000)],
                Failure: new InvalidOperationException("the runtime dropped the connection")),
            Turn(CopilotEvents.Usage(500, 8000), CopilotEvents.Assistant("the answer")));
        await using var session = await OpenAsync(runtime);

        // Act: the failure, then an attempt to carry on
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("first", TestContext.Current.CancellationToken));
        var sentAfterFailure = runtime.Channels[0].Prompts.Count;
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SendAsync("second", TestContext.Current.CancellationToken));

        // Assert: the original failure is reported, the second turn never reached the runtime, and
        // the refusal names the cause rather than inventing one
        Assert.Contains("dropped the connection", failure.Message, StringComparison.Ordinal);
        Assert.Equal(sentAfterFailure, runtime.Channels[0].Prompts.Count);
        Assert.Contains("could not be recorded", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves releasing the session releases the runtime session it owns — and nothing else. The
    ///     client is the host's, shared by every session a rotation creates, and outlives all of
    ///     them.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_Dispose_DisposesTheRuntimeSession_AndNotTheClient()
    {
        // Arrange
        var runtime = Runtime(Turn(CopilotEvents.Usage(400, 8000), CopilotEvents.Assistant("ok")));
        var session = await OpenAsync(runtime);

        // Act
        await session.DisposeAsync();

        // Assert: the runtime session was released, and a replacement can still be created on the
        // same client - which a session that had disposed the client would have made impossible
        Assert.Equal(1, runtime.Channels[0].DisposeCount);
        Assert.True(session.IsReleased);

        await using var replacement = await OpenAsync(runtime);
        Assert.Equal(2, runtime.Channels.Count);
    }

    /// <summary>
    ///     Proves a released session refuses further turns rather than reconnecting, and that
    ///     releasing twice is permitted — a rotation and an application's own release may both reach
    ///     the same session.
    /// </summary>
    [Fact]
    public async Task CopilotProviderSession_Dispose_Twice_IsPermittedAndRejectsLaterTurns()
    {
        // Arrange
        var runtime = Runtime(Turn(CopilotEvents.Usage(400, 8000), CopilotEvents.Assistant("ok")));
        var session = await OpenAsync(runtime);

        // Act
        await session.DisposeAsync();
        await session.DisposeAsync();

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => session.SendAsync("a question", TestContext.Current.CancellationToken));
        Assert.Empty(runtime.Channels[0].Prompts);
    }

    /// <summary>
    ///     Builds a scripted turn that answers with its last assistant message.
    /// </summary>
    /// <remarks>
    ///     The runtime both dispatches the final assistant message as an event and returns it from
    ///     the turn, so a script that omitted one of the two would exercise a shape the runtime never
    ///     produces — and would hide the duplicate-answer rule this adapter depends on.
    /// </remarks>
    /// <param name="events">The events the turn dispatches, in order.</param>
    /// <returns>The scripted turn.</returns>
    private static ScriptedTurn Turn(params SessionEvent[] events) =>
        new(events, events.OfType<AssistantMessageEvent>().LastOrDefault(
            message => message.Data?.ParentToolCallId is null));

    /// <summary>
    ///     Builds a fake runtime whose one session performs the given turns.
    /// </summary>
    /// <param name="turns">The turns the session performs, in order.</param>
    /// <returns>The fake runtime.</returns>
    private static FakeCopilotRuntime Runtime(params ScriptedTurn[] turns) => new(_ => turns);

    /// <summary>
    ///     Opens a provider session on a fake runtime, the only way one exists.
    /// </summary>
    /// <param name="runtime">The fake runtime to open on.</param>
    /// <param name="seed">What the session starts from, or <see langword="null"/> for a bare seed.</param>
    /// <param name="maxWindowTokens">
    ///     A ceiling on the accounted window, or <see langword="null"/> to account against whatever
    ///     the fake runtime reports.
    /// </param>
    /// <returns>The opened session.</returns>
    private static async Task<CopilotProviderSession> OpenAsync(
        FakeCopilotRuntime runtime,
        ProviderSessionSeed? seed = null,
        int? maxWindowTokens = null)
    {
        var factory = new CopilotProviderSessionFactory(runtime.Opener, model: null, maxWindowTokens);
        var session = await factory.CreateAsync(
            seed ?? new ProviderSessionSeed(null, [], []),
            TestContext.Current.CancellationToken);

        return Assert.IsType<CopilotProviderSession>(session);
    }
}
