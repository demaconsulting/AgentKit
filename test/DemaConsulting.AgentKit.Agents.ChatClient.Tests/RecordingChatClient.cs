using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     A chat client that contacts nothing, records every request it was handed, and answers from a
///     script the test wrote.
/// </summary>
/// <remarks>
///     <para>
///     The session adapter's whole job is the exchange between this library and a chat client: what
///     it sends, what it does with what comes back, and what it reports about the window afterwards.
///     A faithful record of each request and a scripted answer are therefore everything a test
///     needs, and a real provider would add network access, credentials and a model's
///     non-determinism without contributing to any assertion.
///     </para>
///     <para>
///     <b>Usage is scripted because it is the fact under test.</b> A real provider reports the
///     input tokens it counted for the request it just answered, and the adapter's account of how
///     full the window is comes from nowhere else. Answers here therefore carry a usage figure the
///     test chose - including, deliberately, no figure at all, which is the condition the adapter
///     refuses rather than estimates around.
///     </para>
///     <para>
///     Written by hand rather than substituted: the streaming member returns an asynchronous
///     sequence a substitute would have to be taught to produce, and the request record is the
///     point of the double rather than an incidental capability.
///     </para>
///     <para>
///     Each test constructs its own instance, so no state is shared. Instances are not thread-safe
///     and are not intended to be used from more than one test at a time.
///     </para>
/// </remarks>
internal sealed class RecordingChatClient : IChatClient
{
    /// <summary>
    ///     The answers queued for the next requests, oldest first.
    /// </summary>
    private readonly Queue<ChatResponse> _scripted = new();

    /// <summary>
    ///     The answer given once the queue is empty, or <see langword="null"/> when there is none.
    /// </summary>
    private ChatResponse? _standing;

    /// <summary>
    ///     The requests this client received, oldest first.
    /// </summary>
    private readonly List<RecordedRequest> _requests = [];

    /// <summary>
    ///     Gets the requests this client received, oldest first.
    /// </summary>
    /// <remarks>
    ///     Each request's conversation is materialized on receipt rather than held as the sequence
    ///     itself, so a test asserts on what the client actually observed rather than on a sequence
    ///     that might enumerate differently the second time.
    /// </remarks>
    public IReadOnlyList<RecordedRequest> Requests => _requests;

    /// <summary>
    ///     Gets the number of times this client was disposed.
    /// </summary>
    /// <remarks>
    ///     A session must not dispose the client it was given: the client is the application's, is
    ///     shared by every session a rotation creates, and outlives all of them. That is only
    ///     observable if disposal is counted.
    /// </remarks>
    public int DisposeCount { get; private set; }

    /// <summary>
    ///     Gets the conversation of the most recent request.
    /// </summary>
    public IReadOnlyList<ChatMessage> LastMessages => _requests[^1].Messages;

    /// <summary>
    ///     Builds a client answering every request with the same text and usage figure.
    /// </summary>
    /// <param name="text">The answer text.</param>
    /// <param name="inputTokens">The input tokens the answer reports.</param>
    /// <returns>The client.</returns>
    public static RecordingChatClient Answering(string text, long inputTokens)
    {
        var client = new RecordingChatClient();
        client._standing = Answer(text, inputTokens);
        return client;
    }

    /// <summary>
    ///     Queues one answer, consumed by the next request that finds the queue non-empty.
    /// </summary>
    /// <param name="response">The answer to queue.</param>
    /// <returns>This client, so a script reads as one statement.</returns>
    public RecordingChatClient Queue(ChatResponse response)
    {
        _scripted.Enqueue(response);
        return this;
    }

    /// <summary>
    ///     Queues one textual answer reporting the given input tokens.
    /// </summary>
    /// <param name="text">The answer text.</param>
    /// <param name="inputTokens">The input tokens the answer reports.</param>
    /// <returns>This client, so a script reads as one statement.</returns>
    public RecordingChatClient Queue(string text, long inputTokens) => Queue(Answer(text, inputTokens));

    /// <summary>
    ///     Builds an answer carrying the given text and reporting the given input tokens.
    /// </summary>
    /// <param name="text">The answer text.</param>
    /// <param name="inputTokens">The input tokens the answer reports.</param>
    /// <returns>The answer.</returns>
    public static ChatResponse Answer(string text, long inputTokens) =>
        WithUsage(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)), inputTokens);

    /// <summary>
    ///     Builds an answer carrying the given messages and reporting the given input tokens.
    /// </summary>
    /// <param name="messages">The messages the provider produced.</param>
    /// <param name="inputTokens">The input tokens the answer reports.</param>
    /// <returns>The answer.</returns>
    public static ChatResponse Answer(IList<ChatMessage> messages, long inputTokens) =>
        WithUsage(new ChatResponse(messages), inputTokens);

    /// <summary>
    ///     Builds an answer that reports no usage at all, as a misconfigured or wrapped client might.
    /// </summary>
    /// <param name="text">The answer text.</param>
    /// <returns>The answer, carrying no usage.</returns>
    public static ChatResponse AnswerWithoutUsage(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text));

    /// <summary>
    ///     Records the request and answers from the script.
    /// </summary>
    /// <param name="messages">The conversation handed to the client.</param>
    /// <param name="options">The request options, recorded so a test can assert on the tools offered.</param>
    /// <param name="cancellationToken">A token that cancels the request, ignored.</param>
    /// <returns>A completed task carrying the scripted answer.</returns>
    /// <exception cref="InvalidOperationException">The test scripted no answer for this request.</exception>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _requests.Add(new RecordedRequest([.. messages], options));

        if (_scripted.Count > 0)
        {
            return Task.FromResult(_scripted.Dequeue());
        }

        return Task.FromResult(
            _standing
            ?? throw new InvalidOperationException(
                "The test scripted no answer for this request; queue one or give the client a standing answer."));
    }

    /// <summary>
    ///     Records the request and answers from the script as a stream of updates.
    /// </summary>
    /// <remarks>
    ///     The session adapter never streams, but the prompt-size recorder installed beneath it
    ///     implements the streaming member of <see cref="IChatClient"/> and reads usage from the
    ///     updates rather than from a response, so streaming is scripted here rather than refused.
    ///     The same scripted answer is rendered as the stream a provider would have produced: one
    ///     update per message, and a final update carrying the usage where the answer reported any.
    /// </remarks>
    /// <param name="messages">The conversation handed to the client.</param>
    /// <param name="options">The request options, recorded so a test can assert on the tools offered.</param>
    /// <param name="cancellationToken">A token that cancels the request, ignored.</param>
    /// <returns>The scripted answer, as updates.</returns>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);

        foreach (var message in response.Messages)
        {
            yield return new ChatResponseUpdate(message.Role, message.Contents);
        }

        if (response.Usage is { } usage)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, (IList<AIContent>)[new UsageContent(usage)]);
        }
    }

    /// <summary>
    ///     Returns no service, because this client offers none.
    /// </summary>
    /// <param name="serviceType">The service being asked for.</param>
    /// <param name="serviceKey">The key qualifying the service, if any.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <summary>
    ///     Counts the disposal, so a test can assert one never happened.
    /// </summary>
    public void Dispose() => DisposeCount++;

    /// <summary>
    ///     Attaches a usage figure to an answer.
    /// </summary>
    /// <param name="response">The answer to attach it to.</param>
    /// <param name="inputTokens">The input tokens the answer reports.</param>
    /// <returns>The same answer, carrying the usage.</returns>
    private static ChatResponse WithUsage(ChatResponse response, long inputTokens)
    {
        response.Usage = new UsageDetails { InputTokenCount = inputTokens };
        return response;
    }

    /// <summary>
    ///     One request this client received.
    /// </summary>
    /// <param name="Messages">The conversation, in the order it arrived.</param>
    /// <param name="Options">The options the request carried, or <see langword="null"/> when it carried none.</param>
    internal sealed record RecordedRequest(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options);
}
