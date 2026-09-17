using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests;

/// <summary>
///     Unit tests for the two chat clients the sample installs beneath its compacting session:
///     <see cref="PromptSizeRecorder"/> and <see cref="TurnReportingChatClient"/>.
/// </summary>
/// <remarks>
///     <para>
///     These are tests of a workaround, and they are worth having for exactly that reason. A
///     tool-using turn is several requests, and the tool-calling loop reports their input tokens
///     <em>summed</em>. A compacting session reads that figure as the conversation's occupancy, so
///     an agent that uses tools appears to fill its window in a single turn and rotates on every
///     turn thereafter. The first test below pins the arithmetic; if AgentKit ever reports the last
///     prompt size itself, this whole pair of clients goes away and this test is what says so.
///     </para>
/// </remarks>
public class TurnReportingChatClientTests
{
    /// <summary>
    ///     Proves the occupancy a turn reports is the size of the last real prompt, not the sum of
    ///     the prompts the tool-calling loop sent.
    /// </summary>
    [Fact]
    public async Task TurnReportingChatClient_GetResponse_ToolCallingTurn_ReportsTheLastPromptSize()
    {
        // Arrange: a turn that calls one tool — a first request answered with the call, a second
        // answered with the text, each reporting its own prompt size
        var tool = AIFunctionFactory.Create(() => "ok", "probe_tool");
        using var provider = new ScriptedChatClient(
            new ScriptedAnswer(
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "probe_tool", null)]),
                InputTokens: 100),
            new ScriptedAnswer(new ChatMessage(ChatRole.Assistant, "done"), InputTokens: 150));

        using var client = Build(provider, out _, out _);

        // Act
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "go")],
            new ChatOptions { Tools = [tool] },
            TestContext.Current.CancellationToken);

        // Assert: 150, the conversation as the provider last counted it — not 250, which is what
        // the tool-calling loop reports and what the session would otherwise believe
        Assert.Equal(2, provider.Requests);
        Assert.Equal(150, response.Usage?.InputTokenCount);
    }

    /// <summary>
    ///     Proves every tool call and its result are handed to the observers, correlated, so a
    ///     compacting conversation can show what a session turn does not report.
    /// </summary>
    [Fact]
    public async Task TurnReportingChatClient_GetResponse_ToolCallingTurn_ReportsEveryCallWithItsResult()
    {
        // Arrange: the same one-tool turn, with the observers recording what they are given
        var tool = AIFunctionFactory.Create(() => "ok", "probe_tool");
        using var provider = new ScriptedChatClient(
            new ScriptedAnswer(
                new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "probe_tool", null)]),
                InputTokens: 100),
            new ScriptedAnswer(new ChatMessage(ChatRole.Assistant, "done"), InputTokens: 150));

        using var client = Build(provider, out var calls, out var results);

        // Act
        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "go")],
            new ChatOptions { Tools = [tool] },
            TestContext.Current.CancellationToken);

        // Assert: one call, named; one result, carrying the call it answers
        Assert.Equal(["probe_tool"], calls.Select(call => call.Name));
        Assert.Single(results);
        Assert.Equal("call-1", results[0].Result.CallId);
        Assert.Equal("probe_tool", results[0].Call?.Name);
    }

    /// <summary>
    ///     Proves a turn that called no tool reports the prompt it actually sent, so the ordinary
    ///     case is not distorted by the machinery the tool case needs.
    /// </summary>
    [Fact]
    public async Task TurnReportingChatClient_GetResponse_PlainTurn_ReportsThatTurnsPromptSize()
    {
        // Arrange: one request, one answer
        using var provider = new ScriptedChatClient(
            new ScriptedAnswer(new ChatMessage(ChatRole.Assistant, "hello"), InputTokens: 42));

        using var client = Build(provider, out var calls, out _);

        // Act
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(42, response.Usage?.InputTokenCount);
        Assert.Empty(calls);
    }

    /// <summary>
    ///     Proves a provider reporting no usage is left reporting none, so the session refuses the
    ///     turn where the adapter wrote that refusal rather than being handed an invented figure.
    /// </summary>
    [Fact]
    public async Task TurnReportingChatClient_GetResponse_ProviderReportsNoUsage_InventsNone()
    {
        // Arrange: a provider that answers without reporting usage
        using var provider = new ScriptedChatClient(
            new ScriptedAnswer(new ChatMessage(ChatRole.Assistant, "hello"), InputTokens: null));

        using var client = Build(provider, out _, out _);

        // Act
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            options: null,
            TestContext.Current.CancellationToken);

        // Assert: nothing was substituted
        Assert.Null(response.Usage?.InputTokenCount);
    }

    /// <summary>
    ///     Proves a missing recorder, observer, or inner client is refused where the application
    ///     composed the chain rather than at the first turn.
    /// </summary>
    [Fact]
    public void TurnReportingChatClient_Constructor_MissingCollaborator_Throws()
    {
        using var provider = new ScriptedChatClient(
            new ScriptedAnswer(new ChatMessage(ChatRole.Assistant, "hello"), InputTokens: 1));
        using var recorder = new PromptSizeRecorder(provider);

        Assert.Throws<ArgumentNullException>(
            () => new TurnReportingChatClient(recorder, null!, _ => { }, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(
            () => new TurnReportingChatClient(recorder, recorder, null!, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(
            () => new TurnReportingChatClient(recorder, recorder, _ => { }, null!));
    }

    /// <summary>
    ///     Builds the chain the sample installs beneath its compacting session, capturing what the
    ///     observers are handed.
    /// </summary>
    /// <param name="provider">The scripted provider at the bottom of the chain.</param>
    /// <param name="calls">Receives every reported tool call, in order.</param>
    /// <param name="results">Receives every reported tool result with the call it answers.</param>
    /// <returns>The outermost client, which is what the provider session would be given.</returns>
    private static IChatClient Build(
        IChatClient provider,
        out List<FunctionCallContent> calls,
        out List<(FunctionResultContent Result, FunctionCallContent? Call)> results)
    {
        var recordedCalls = new List<FunctionCallContent>();
        var recordedResults = new List<(FunctionResultContent, FunctionCallContent?)>();
        calls = recordedCalls;
        results = recordedResults;

        var promptSize = new PromptSizeRecorder(provider);
        var toolCalling = promptSize.AsBuilder().UseFunctionInvocation().Build();

        return new TurnReportingChatClient(
            toolCalling,
            promptSize,
            recordedCalls.Add,
            (result, call) => recordedResults.Add((result, call)));
    }
}
