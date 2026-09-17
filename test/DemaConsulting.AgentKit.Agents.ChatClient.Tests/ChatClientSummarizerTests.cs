using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     Unit tests for <see cref="ChatClientSummarizer"/>: the prompt it sends, what it declines to
///     send with it, and what it returns.
/// </summary>
public class ChatClientSummarizerTests
{
    /// <summary>
    ///     Proves the summarizer sends the prompt this package publishes, composed from the request,
    ///     as a single user message.
    /// </summary>
    /// <remarks>
    ///     The composition is asserted against <see cref="ConsolidationPrompt.Compose"/> itself
    ///     rather than against a copy of its text, so the assertion follows the published prompt
    ///     instead of freezing a transcription of it.
    /// </remarks>
    [Fact]
    public async Task ChatClientSummarizer_Consolidate_SendsTheComposedConsolidationPrompt()
    {
        // Arrange: a request naming material and an aggressiveness clause
        var client = RecordingChatClient.Answering("the record", inputTokens: 100);
        var summarizer = new ChatClientSummarizer(client);
        var request = new ConsolidationRequest(1, "USER: find the config\nASSISTANT: it is app.json", ConsolidationPrompt.LowInstruction);

        // Act: consolidate
        await summarizer.ConsolidateAsync(request, TestContext.Current.CancellationToken);

        // Assert: one user message carrying exactly the composed prompt
        var sent = Assert.Single(client.LastMessages);
        Assert.Equal(ChatRole.User, sent.Role);
        Assert.Equal(ConsolidationPrompt.Compose(request), sent.Text);
    }

    /// <summary>
    ///     Proves a consolidation offers no tools: it is a pure function from the material to a
    ///     record of it, and a summarizer that could act would be doing something else.
    /// </summary>
    [Fact]
    public async Task ChatClientSummarizer_Consolidate_OffersNoTools()
    {
        // Arrange: a summarizer over a client that records what it was offered
        var client = RecordingChatClient.Answering("the record", inputTokens: 100);
        var summarizer = new ChatClientSummarizer(client);

        // Act: consolidate
        await summarizer.ConsolidateAsync(Request("material"), TestContext.Current.CancellationToken);

        // Assert: the request carried no options at all, so no tool was published to the model
        Assert.Null(client.Requests[0].Options);
    }

    /// <summary>
    ///     Proves a consolidation carries no history: a second one sends its own prompt and nothing
    ///     of the first, which is what lets the same summarizer serve every rotation of every session.
    /// </summary>
    [Fact]
    public async Task ChatClientSummarizer_Consolidate_SecondCall_CarriesNoHistoryFromTheFirst()
    {
        // Arrange: one summarizer asked to consolidate twice
        var client = RecordingChatClient.Answering("the record", inputTokens: 100);
        var summarizer = new ChatClientSummarizer(client);
        var second = Request("the second material");

        // Act: consolidate two different bodies of material
        await summarizer.ConsolidateAsync(Request("the first material"), TestContext.Current.CancellationToken);
        await summarizer.ConsolidateAsync(second, TestContext.Current.CancellationToken);

        // Assert: the second request is one message, the second prompt, with no trace of the first
        var sent = Assert.Single(client.Requests[1].Messages);
        Assert.Equal(ConsolidationPrompt.Compose(second), sent.Text);
        Assert.DoesNotContain("the first material", sent.Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the record the model produced is what the summarizer returns.
    /// </summary>
    [Fact]
    public async Task ChatClientSummarizer_Consolidate_ReturnsTheRecordTheModelProduced()
    {
        var client = RecordingChatClient.Answering("decisions, facts and open threads", inputTokens: 100);
        var summarizer = new ChatClientSummarizer(client);

        var record = await summarizer.ConsolidateAsync(Request("material"), TestContext.Current.CancellationToken);

        Assert.Equal("decisions, facts and open threads", record);
    }

    /// <summary>
    ///     Proves an empty answer is returned as empty rather than raised as a failure.
    /// </summary>
    /// <remarks>
    ///     The engine treats a consolidation it could not obtain as material to keep rather than
    ///     material to lose, so a model declining to answer is a thing that happens rather than a
    ///     defect to escalate. Throwing here would turn a quiet non-answer into a failed turn.
    /// </remarks>
    [Fact]
    public async Task ChatClientSummarizer_Consolidate_EmptyAnswer_ReturnsEmpty()
    {
        // Arrange: a client answering with nothing at all
        var client = RecordingChatClient.Answering(string.Empty, inputTokens: 100);
        var summarizer = new ChatClientSummarizer(client);

        // Act: consolidate
        var record = await summarizer.ConsolidateAsync(Request("material"), TestContext.Current.CancellationToken);

        // Assert: the empty record comes back as one, with no exception
        Assert.Equal(string.Empty, record);
    }

    /// <summary>
    ///     Proves a missing client is refused where the application composed the summarizer.
    /// </summary>
    [Fact]
    public void ChatClientSummarizer_Constructor_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ChatClientSummarizer(null!));
    }

    /// <summary>
    ///     Proves a missing request is refused rather than composed into a prompt with nothing in it.
    /// </summary>
    [Fact]
    public async Task ChatClientSummarizer_Consolidate_NullRequest_Throws()
    {
        var summarizer = new ChatClientSummarizer(new RecordingChatClient());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => summarizer.ConsolidateAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a canceled consolidation sends nothing, so a canceled rotation costs no call.
    /// </summary>
    [Fact]
    public async Task ChatClientSummarizer_Consolidate_Canceled_ThrowsAndSendsNothing()
    {
        var client = RecordingChatClient.Answering("the record", inputTokens: 100);
        var summarizer = new ChatClientSummarizer(client);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => summarizer.ConsolidateAsync(Request("material"), cancellation.Token));

        Assert.Empty(client.Requests);
    }

    /// <summary>
    ///     Builds a consolidation request over the given material.
    /// </summary>
    /// <param name="material">The material to consolidate.</param>
    /// <returns>A tier-one request at the low aggressiveness clause.</returns>
    private static ConsolidationRequest Request(string material) =>
        new(1, material, ConsolidationPrompt.LowInstruction);
}
