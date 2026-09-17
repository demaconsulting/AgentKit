using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     Unit tests for <see cref="PromptSizeRecordingChatClient"/>: the prompt size it keeps for each
///     individual request, what it does beneath a tool-calling loop, and what it does with an answer
///     that reports nothing.
/// </summary>
/// <remarks>
///     The class is internal, and is reached here because the package makes its internals visible to
///     this test project. It is nonetheless exercised as its callers use it — as a chat client,
///     through <see cref="IChatClient"/> — rather than by reaching past that interface.
/// </remarks>
public class PromptSizeRecordingChatClientTests
{
    /// <summary>
    ///     Proves each request is recorded separately and the most recent one stands, which is what
    ///     makes the figure the size of one prompt rather than a running total.
    /// </summary>
    [Fact]
    public async Task PromptSizeRecordingChatClient_GetResponse_SeveralRequests_KeepsTheMostRecentPrompt()
    {
        // Arrange: a provider reporting a different prompt size for each of two requests
        var provider = new RecordingChatClient()
            .Queue("first", inputTokens: 100)
            .Queue("second", inputTokens: 150);
        using var recorder = new PromptSizeRecordingChatClient(provider);

        // Act: make both requests, reading the recorded figure after each
        await recorder.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "one")], cancellationToken: TestContext.Current.CancellationToken);
        var afterFirst = recorder.LastPromptTokens;
        await recorder.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "two")], cancellationToken: TestContext.Current.CancellationToken);
        var afterSecond = recorder.LastPromptTokens;

        // Assert: each reading is that request's own prompt, and the second replaced the first
        // rather than being added to it
        Assert.Equal(100, afterFirst);
        Assert.Equal(150, afterSecond);
    }

    /// <summary>
    ///     Proves the fact this class exists for: beneath a tool-calling loop it keeps the last
    ///     request's prompt, while the response the loop hands back reports the sum of every request
    ///     it made.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Both figures are asserted in the one test, because the point is the difference between
    ///     them. The summed figure is not an incidental detail: it is what a session reading
    ///     <see cref="ChatResponse.Usage"/> would take as occupancy, and it is larger than the
    ///     conversation ever was — which is why a tool-using agent reading it concluded its window
    ///     was full on its first turn.
    ///     </para>
    ///     <para>
    ///     The scripted sizes are deliberately unequal and neither is a factor of their sum, so no
    ///     reading can be mistaken for another by coincidence.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task PromptSizeRecordingChatClient_GetResponse_BeneathToolInvocation_KeepsTheLastPromptWhileTheTurnReportsTheSum()
    {
        // Arrange: a provider that calls a tool and then answers, the recorder beneath the
        // tool-invoking layer, and a tool for that layer to run
        var provider = new RecordingChatClient()
            .Queue(
                RecordingChatClient.Answer(
                    [new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "probe", null)])],
                    inputTokens: 100))
            .Queue("done", inputTokens: 150);
        using var recorder = new PromptSizeRecordingChatClient(provider);
        using var pipeline = new FunctionInvokingChatClient(recorder);
        var options = new ChatOptions { Tools = [AIFunctionFactory.Create(() => "42", "probe")] };

        // Act: take one turn, which the tool call makes into two requests
        var response = await pipeline.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "look it up")],
            options,
            TestContext.Current.CancellationToken);

        // Assert: two requests really were made; the response reports their sum, which is more than
        // the conversation ever occupied; and the recorded figure is the last request's own prompt
        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal(250, response.Usage?.InputTokenCount);
        Assert.Equal(150, recorder.LastPromptTokens);
    }

    /// <summary>
    ///     Proves nothing is reported before a request has been made, which is what lets a session
    ///     tell "has not spoken yet" from "occupies nothing".
    /// </summary>
    [Fact]
    public void PromptSizeRecordingChatClient_LastPromptTokens_BeforeAnyRequest_IsNull()
    {
        // Arrange: a recorder that has answered nothing
        using var recorder = new PromptSizeRecordingChatClient(new RecordingChatClient());

        // Act / Assert: there is no figure to report, rather than a figure of zero
        Assert.Null(recorder.LastPromptTokens);
    }

    /// <summary>
    ///     Proves a request whose answer reports no usage leaves the previous reading standing.
    /// </summary>
    /// <remarks>
    ///     A tool-calling turn makes several requests and a provider need not report usage on every
    ///     one of them. Clearing the figure there would make a session that had been told the truth
    ///     refuse the turn that followed it; the last figure that was true is the better answer. A
    ///     provider that reports usage on no request at all still leaves nothing recorded, which is
    ///     the condition the session refuses.
    /// </remarks>
    [Fact]
    public async Task PromptSizeRecordingChatClient_GetResponse_RequestReportingNoUsage_KeepsThePreviousReading()
    {
        // Arrange: a provider reporting a prompt size once and then reporting none
        var provider = new RecordingChatClient()
            .Queue("first", inputTokens: 100)
            .Queue(RecordingChatClient.AnswerWithoutUsage("second"));
        using var recorder = new PromptSizeRecordingChatClient(provider);

        // Act: make both requests
        await recorder.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "one")], cancellationToken: TestContext.Current.CancellationToken);
        await recorder.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "two")], cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the figure that was reported still stands
        Assert.Equal(100, recorder.LastPromptTokens);
    }

    /// <summary>
    ///     Proves a streamed answer is recorded from the usage it carries, and that every update
    ///     still reaches the caller.
    /// </summary>
    /// <remarks>
    ///     A streaming provider reports usage as one more update in the sequence rather than on a
    ///     response, so the figure has to be taken from the stream. Passing the updates through
    ///     unchanged is asserted alongside it, because a decorator that consumed what it read would
    ///     leave the caller with an answer missing its content.
    /// </remarks>
    [Fact]
    public async Task PromptSizeRecordingChatClient_GetStreamingResponse_RecordsTheStreamedUsageAndPassesUpdatesThrough()
    {
        // Arrange: a provider answering as a stream, reporting its prompt size within it
        var provider = new RecordingChatClient().Queue("streamed", inputTokens: 175);
        using var recorder = new PromptSizeRecordingChatClient(provider);

        // Act: consume the whole stream
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in recorder.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        // Assert: the streamed usage was recorded, and the answer reached the caller intact
        Assert.Equal(175, recorder.LastPromptTokens);
        Assert.Contains(updates, update => update.Text == "streamed");
    }
}
