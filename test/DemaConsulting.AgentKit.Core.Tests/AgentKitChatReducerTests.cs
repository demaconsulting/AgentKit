using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="AgentKitChatReducer"/> class.
/// </summary>
/// <remarks>
///     The summarizer is the deterministic <see cref="FakeSummarizer"/>, which records every request
///     it was given. The reducer's behavior is the history it returns and the request it made, so a
///     recording fake observes all of it; a real model would add only non-determinism.
/// </remarks>
public class AgentKitChatReducerTests
{
    /// <summary>
    ///     Proves a history within the threshold is returned unchanged, with no consolidation.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitChatReducer_ReduceAsync_UnderThreshold_ReturnsMessagesUnchanged()
    {
        // Arrange: a short history and a generous threshold
        var summarizer = new FakeSummarizer(_ => "record");
        var reducer = new AgentKitChatReducer(summarizer, thresholdCharacters: 10_000);
        var history = Conversation(turns: 3);

        // Act: reduce
        var reduced = (await reducer.ReduceAsync(history, TestContext.Current.CancellationToken)).ToList();

        // Assert: the same messages, in order, and no request made
        Assert.Equal(history, reduced);
        Assert.Equal(0, summarizer.CallCount);
    }

    /// <summary>
    ///     Proves an over-threshold history keeps its system messages and the last turns verbatim,
    ///     replacing everything older with one record.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitChatReducer_ReduceAsync_OverThreshold_KeepsSystemMessagesAndVerbatimTail()
    {
        // Arrange: a system message and six turns, a small threshold, two verbatim turns
        var summarizer = new FakeSummarizer(_ => "the record");
        var reducer = new AgentKitChatReducer(summarizer, thresholdCharacters: 50, verbatimTurns: 2);
        var system = new ChatMessage(ChatRole.System, "Be helpful.");
        List<ChatMessage> history = [system, .. Conversation(turns: 6)];

        // Act: reduce
        var reduced = (await reducer.ReduceAsync(history, TestContext.Current.CancellationToken)).ToList();

        // Assert: system, record, then the last two turns as the same instances
        Assert.Same(system, reduced[0]);
        Assert.Equal(ChatRole.Assistant, reduced[1].Role);
        Assert.Equal(AgentKitChatReducer.RecordHeader + "\nthe record", reduced[1].Text);
        Assert.Equal(history.Skip(history.Count - 4), reduced.Skip(2));
        Assert.Equal(1, summarizer.CallCount);
    }

    /// <summary>
    ///     Proves the tail begins at a user message, so a tool call is never separated from its
    ///     result.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitChatReducer_ReduceAsync_TailBoundary_StartsAtUserMessage_NeverSplitsToolPair()
    {
        // Arrange: an old turn, then a last turn holding a tool call and its result
        var reducer = new AgentKitChatReducer(new FakeSummarizer(_ => "record"), thresholdCharacters: 10, verbatimTurns: 1);
        var question = new ChatMessage(ChatRole.User, "Read notes.txt.");
        var call = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "text_file_read")]);
        var result = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "notes")]);
        var answer = new ChatMessage(ChatRole.Assistant, "It says notes.");
        List<ChatMessage> history = [.. Conversation(turns: 2), question, call, result, answer];

        // Act: reduce, keeping one turn
        var reduced = (await reducer.ReduceAsync(history, TestContext.Current.CancellationToken)).ToList();

        // Assert: the record, then the whole last turn with its call and result together
        Assert.Equal([question, call, result, answer], reduced.Skip(1));
    }

    /// <summary>
    ///     Proves the consolidation is asked of the summarizer at tier one with the prompt clause for
    ///     the configured level, over labeled transcript lines.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitChatReducer_ReduceAsync_ConsolidationRequest_UsesConsolidationPromptForLevel()
    {
        // Arrange: a reducer at the high level
        var summarizer = new FakeSummarizer(_ => "record");
        var reducer = new AgentKitChatReducer(summarizer, thresholdCharacters: 10, verbatimTurns: 1, CompactionLevel.High);

        // Act: reduce a three-turn history
        await reducer.ReduceAsync(Conversation(turns: 3), TestContext.Current.CancellationToken);

        // Assert: one tier-one request, the high clause, and the two older turns as labeled lines
        var request = Assert.Single(summarizer.Requests);
        Assert.Equal(1, request.TierIndex);
        Assert.Equal(ConsolidationPrompt.HighInstruction, request.Instruction);
        Assert.Equal(
            "USER: question 1\nASSISTANT: answer 1\nUSER: question 2\nASSISTANT: answer 2",
            request.Material);
    }

    /// <summary>
    ///     Proves a previous record is folded into the new material as a record, so a second
    ///     reduction consolidates the first rather than keeping both.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitChatReducer_ReduceAsync_PreviousRecord_IsFoldedIntoMaterial()
    {
        // Arrange: a history that was reduced once, then grew by two turns
        var summarizer = new FakeSummarizer(request => "second record");
        var reducer = new AgentKitChatReducer(summarizer, thresholdCharacters: 10, verbatimTurns: 1);
        var previous = new ChatMessage(ChatRole.Assistant, AgentKitChatReducer.RecordHeader + "\nfirst record");
        List<ChatMessage> history = [previous, .. Conversation(turns: 2)];

        // Act: reduce again
        var reduced = (await reducer.ReduceAsync(history, TestContext.Current.CancellationToken)).ToList();

        // Assert: the first record arrived as a record line, and only one record remains
        Assert.StartsWith("RECORD: first record\nUSER: question 1", Assert.Single(summarizer.Requests).Material, StringComparison.Ordinal);
        Assert.Single(reduced, message => message.Text.StartsWith(AgentKitChatReducer.RecordHeader, StringComparison.Ordinal));
        Assert.Contains("second record", reduced[0].Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a blank record leaves the history unchanged rather than losing the material.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitChatReducer_ReduceAsync_BlankRecord_ReturnsMessagesUnchanged()
    {
        // Arrange: a summarizer that refuses with a blank answer
        var reducer = new AgentKitChatReducer(FakeSummarizer.Blank(), thresholdCharacters: 10, verbatimTurns: 1);
        var history = Conversation(turns: 3);

        // Act: reduce
        var reduced = await reducer.ReduceAsync(history, TestContext.Current.CancellationToken);

        // Assert: nothing was dropped
        Assert.Equal(history, reduced);
    }

    /// <summary>
    ///     Proves a null record, which the summarizer contract forbids, is reported.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitChatReducer_ReduceAsync_NullRecord_ThrowsInvalidOperationException()
    {
        // Arrange: a summarizer that breaks its contract
        var reducer = new AgentKitChatReducer(FakeSummarizer.Null(), thresholdCharacters: 10, verbatimTurns: 1);

        // Act / Assert: the defect is surfaced
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reducer.ReduceAsync(Conversation(turns: 3), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves image content in older history reaches the summarizer as its media type and size,
    ///     never as bytes.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitChatReducer_ReduceAsync_ImageContent_RenderedAsPlaceholderInMaterial()
    {
        // Arrange: an older turn whose tool returned a caption and an image
        var summarizer = new FakeSummarizer(_ => "record");
        var reducer = new AgentKitChatReducer(summarizer, thresholdCharacters: 10, verbatimTurns: 1);
        var image = new DataContent(new byte[5_000], "image/png");
        List<ChatMessage> history =
        [
            new(ChatRole.User, "Show the chart."),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "image_read")]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", new List<AIContent> { new TextContent("A chart."), image })]),
            new(ChatRole.User, "Thanks."),
            new(ChatRole.Assistant, "You are welcome.")
        ];

        // Act: reduce, keeping only the last turn
        await reducer.ReduceAsync(history, TestContext.Current.CancellationToken);

        // Assert: the result is described, not carried
        var material = Assert.Single(summarizer.Requests).Material;
        Assert.Contains("TOOL RESULT [c1]: A chart.\n[image/png, 5000 bytes]", material, StringComparison.Ordinal);
        Assert.DoesNotContain("base64", material, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a history with nothing older than the kept turns is returned unchanged even when it
    ///     exceeds the threshold.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitChatReducer_ReduceAsync_NotEnoughTurns_ReturnsMessagesUnchanged()
    {
        // Arrange: two turns, both kept
        var summarizer = new FakeSummarizer(_ => "record");
        var reducer = new AgentKitChatReducer(summarizer, thresholdCharacters: 10, verbatimTurns: 2);
        var history = Conversation(turns: 2);

        // Act: reduce
        var reduced = await reducer.ReduceAsync(history, TestContext.Current.CancellationToken);

        // Assert: unchanged and no request made
        Assert.Equal(history, reduced);
        Assert.Equal(0, summarizer.CallCount);
    }

    /// <summary>
    ///     Proves a canceled reduction throws, whether canceled before it starts or by the time the
    ///     summarizer answers.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitChatReducer_ReduceAsync_Cancelled_Throws()
    {
        // Arrange: a canceled token, and a summarizer that ignores tokens and cancels mid-reduction
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        using var during = new CancellationTokenSource();
        var inattentive = new InattentiveSummarizer(_ =>
        {
            during.Cancel();
            return "record";
        });
        var before = new AgentKitChatReducer(new FakeSummarizer(), thresholdCharacters: 10, verbatimTurns: 1);
        var after = new AgentKitChatReducer(inattentive, thresholdCharacters: 10, verbatimTurns: 1);

        // Act / Assert: both are canceled
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => before.ReduceAsync(Conversation(3), canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => after.ReduceAsync(Conversation(3), during.Token));
    }

    /// <summary>
    ///     Proves invalid construction arguments are programming errors.
    /// </summary>
    [Fact]
    public void AgentKitChatReducer_Constructor_InvalidArguments_Throw()
    {
        // Arrange: a valid summarizer
        var summarizer = new FakeSummarizer();

        // Act / Assert: each invalid argument is refused
        Assert.Throws<ArgumentNullException>(() => new AgentKitChatReducer(null!, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentKitChatReducer(summarizer, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentKitChatReducer(summarizer, 100, verbatimTurns: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentKitChatReducer(summarizer, 100, 1, (CompactionLevel)99));
    }

    /// <summary>
    ///     Builds a conversation of numbered question-and-answer turns.
    /// </summary>
    /// <param name="turns">The number of turns.</param>
    /// <returns>The messages, user then assistant per turn.</returns>
    private static List<ChatMessage> Conversation(int turns) =>
    [
        .. Enumerable.Range(1, turns).SelectMany(n => new[]
        {
            new ChatMessage(ChatRole.User, "question " + n),
            new ChatMessage(ChatRole.Assistant, "answer " + n)
        })
    ];
}
