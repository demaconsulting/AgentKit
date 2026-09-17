using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests;

/// <summary>
///     Unit tests for <see cref="ToolCallReportingChatClient"/>, the one chat client the sample
///     still installs beneath its compacting session.
/// </summary>
/// <remarks>
///     <para>
///     There used to be two, and the other was a workaround. A tool-using turn is several requests
///     and the tool-calling loop reports their input tokens <em>summed</em>; a compacting session
///     reading that figure would have a tool-using agent fill its window in a single turn. The
///     sample recorded each real prompt beneath the loop and put the last one back above it.
///     AgentKit now does that itself, inside <c>ChatClientProviderSessionFactory</c>, so both of the
///     sample's classes for it are gone and only the reporting remains.
///     </para>
///     <para>
///     The reporting is exercised through the arrangement the factory builds — the tool-calling loop
///     above this client — because where it sits is what decides whether it can see a call and its
///     result at all.
///     </para>
/// </remarks>
public class ToolCallReportingChatClientTests
{
    /// <summary>
    ///     Proves every tool call and its result are handed to the observers, correlated, so a
    ///     compacting conversation can show what a session turn does not report.
    /// </summary>
    [Fact]
    public async Task ToolCallReportingChatClient_GetResponse_ToolCallingTurn_ReportsEveryCallWithItsResult()
    {
        // Arrange: a turn that calls one tool - a first request answered with the call, a second
        // answered with the text - beneath the tool-calling loop the session factory installs
        var tool = AIFunctionFactory.Create(() => "ok", "probe_tool");
        using var provider = new ScriptedChatClient(
            new ScriptedAnswer(
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "probe_tool", null)]),
                InputTokens: 100),
            new ScriptedAnswer(new ChatMessage(ChatRole.Assistant, "done"), InputTokens: 150));

        using var client = Build(provider, out var calls, out var results);

        // Act: take the turn
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "go")],
            new ChatOptions { Tools = [tool] },
            TestContext.Current.CancellationToken);

        // Assert: one call, named; one result, carrying the call it answers
        Assert.Equal(2, provider.Requests);
        Assert.Equal(["probe_tool"], calls.Select(call => call.Name));
        Assert.Single(results);
        Assert.Equal("call-1", results[0].Result.CallId);
        Assert.Equal("probe_tool", results[0].Call?.Name);
    }

    /// <summary>
    ///     Proves a call already reported is not reported again when the conversation carrying it is
    ///     resent.
    /// </summary>
    /// <remarks>
    ///     A provider reached through a chat client holds no conversation, so every turn sends the
    ///     whole of it again — including the tool calls and results of every turn before it. A
    ///     reporter that printed what it saw would print the same call once more on every turn for
    ///     the rest of the run.
    /// </remarks>
    [Fact]
    public async Task ToolCallReportingChatClient_GetResponse_ConversationResent_ReportsEachCallOnce()
    {
        // Arrange: the same one-tool turn
        var tool = AIFunctionFactory.Create(() => "ok", "probe_tool");
        using var provider = new ScriptedChatClient(
            new ScriptedAnswer(
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "probe_tool", null)]),
                InputTokens: 100),
            new ScriptedAnswer(new ChatMessage(ChatRole.Assistant, "done"), InputTokens: 150));

        using var client = Build(provider, out var calls, out var results);
        var conversation = new List<ChatMessage> { new(ChatRole.User, "go") };
        var options = new ChatOptions { Tools = [tool] };

        // Act: take the turn, then send the whole conversation it produced again, as the next turn
        // of a stateless provider does
        var first = await client.GetResponseAsync(
            conversation, options, TestContext.Current.CancellationToken);
        conversation.AddRange(first.Messages);
        conversation.Add(new ChatMessage(ChatRole.User, "and again"));
        await client.GetResponseAsync(conversation, options, TestContext.Current.CancellationToken);

        // Assert: the call and its result were each reported exactly once across both turns
        Assert.Single(calls);
        Assert.Single(results);
    }

    /// <summary>
    ///     Proves a turn that called no tool reports nothing, so an ordinary exchange is not
    ///     annotated with machinery the tool case needs.
    /// </summary>
    [Fact]
    public async Task ToolCallReportingChatClient_GetResponse_PlainTurn_ReportsNothing()
    {
        // Arrange: one request, one answer, no tools
        using var provider = new ScriptedChatClient(
            new ScriptedAnswer(new ChatMessage(ChatRole.Assistant, "hello"), InputTokens: 42));

        using var client = Build(provider, out var calls, out var results);

        // Act: take the turn
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            options: null,
            TestContext.Current.CancellationToken);

        // Assert: the answer came through untouched and nothing was reported
        Assert.Equal("hello", response.Text);
        Assert.Empty(calls);
        Assert.Empty(results);
    }

    /// <summary>
    ///     Proves a missing observer is refused where the application composed the chain rather than
    ///     at the first turn.
    /// </summary>
    [Fact]
    public void ToolCallReportingChatClient_Constructor_MissingCollaborator_Throws()
    {
        using var provider = new ScriptedChatClient(
            new ScriptedAnswer(new ChatMessage(ChatRole.Assistant, "hello"), InputTokens: 1));

        Assert.Throws<ArgumentNullException>(
            () => new ToolCallReportingChatClient(provider, null!, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(
            () => new ToolCallReportingChatClient(provider, _ => { }, null!));
    }

    /// <summary>
    ///     Builds the arrangement the session factory builds around the sample's client: the
    ///     reporting client directly over the provider, with the tool-calling loop above it.
    /// </summary>
    /// <param name="provider">The scripted provider at the bottom of the chain.</param>
    /// <param name="calls">Receives every reported tool call, in order.</param>
    /// <param name="results">Receives every reported tool result with the call it answers.</param>
    /// <returns>The outermost client, which is what a session sends its turns through.</returns>
    private static IChatClient Build(
        IChatClient provider,
        out List<FunctionCallContent> calls,
        out List<(FunctionResultContent Result, FunctionCallContent? Call)> results)
    {
        var recordedCalls = new List<FunctionCallContent>();
        var recordedResults = new List<(FunctionResultContent, FunctionCallContent?)>();
        calls = recordedCalls;
        results = recordedResults;

        var reporting = new ToolCallReportingChatClient(
            provider,
            recordedCalls.Add,
            (result, call) => recordedResults.Add((result, call)));

        return new FunctionInvokingChatClient(reporting);
    }
}
