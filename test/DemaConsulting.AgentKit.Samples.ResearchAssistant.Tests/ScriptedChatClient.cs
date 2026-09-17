using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests;

/// <summary>
///     One scripted answer a <see cref="ScriptedChatClient"/> gives, in order.
/// </summary>
/// <param name="Message">The message the provider produces for this request.</param>
/// <param name="InputTokens">
///     The input tokens the provider reports for this request, or <see langword="null"/> to report
///     no usage at all.
/// </param>
internal sealed record ScriptedAnswer(ChatMessage Message, long? InputTokens);

/// <summary>
///     A chat client that answers from a script, so a test can stand in for a provider without a
///     server.
/// </summary>
/// <remarks>
///     Answers are consumed in order, one per request. That ordering is what makes it possible to
///     script a tool-calling turn — a first request answered with a tool call, and a second
///     answered with the final text — which is the shape the sample's chat-client decorators exist
///     to cope with.
/// </remarks>
internal sealed class ScriptedChatClient : IChatClient
{
    /// <summary>
    ///     The answers to give, in request order.
    /// </summary>
    private readonly IReadOnlyList<ScriptedAnswer> _answers;

    /// <summary>
    ///     How many requests have been answered so far.
    /// </summary>
    private int _requests;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ScriptedChatClient"/> class.
    /// </summary>
    /// <param name="answers">The answers to give, in request order.</param>
    public ScriptedChatClient(params ScriptedAnswer[] answers)
    {
        _answers = answers;
    }

    /// <summary>
    ///     Gets the number of requests this client has answered.
    /// </summary>
    public int Requests => _requests;

    /// <inheritdoc/>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var answer = _answers[Math.Min(_requests, _answers.Count - 1)];
        _requests++;

        var response = new ChatResponse(answer.Message);
        if (answer.InputTokens is { } tokens)
        {
            response.Usage = new UsageDetails { InputTokenCount = tokens, OutputTokenCount = 1 };
        }

        return Task.FromResult(response);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The sample's compacting path never streams.");

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <inheritdoc/>
    public void Dispose()
    {
        // Nothing is held.
    }
}
