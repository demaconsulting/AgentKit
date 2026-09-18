using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Ollama.Tests;

/// <summary>
///     A chat client that contacts nothing and keeps a faithful record of every request it was
///     handed.
/// </summary>
/// <remarks>
///     <para>
///     The sizing decorator's entire job is what it puts on a request and what it leaves alone, so a
///     record of the options each request carried is the whole of what a test needs. A real provider
///     would add a server, a model load and a model's non-determinism without contributing to any
///     assertion.
///     </para>
///     <para>
///     Written by hand rather than substituted: the streaming member returns an asynchronous
///     sequence a substitute would have to be taught to produce, and the per-request record is the
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
    ///     The options each request carried, oldest first.
    /// </summary>
    private readonly List<ChatOptions?> _requests = [];

    /// <summary>
    ///     Gets the options each request carried, oldest first.
    /// </summary>
    /// <remarks>
    ///     Recorded as the instance that arrived rather than a copy of it, because whether the
    ///     decorator handed on the caller's own object or a clone of it is itself under test.
    /// </remarks>
    public IReadOnlyList<ChatOptions?> Requests => _requests;

    /// <summary>
    ///     Records the request and answers with a fixed reply.
    /// </summary>
    /// <param name="messages">The conversation handed to the client, unused.</param>
    /// <param name="options">The request options, recorded.</param>
    /// <param name="cancellationToken">A token that cancels the request, ignored.</param>
    /// <returns>A completed task carrying a fixed answer.</returns>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _requests.Add(options);

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
    }

    /// <summary>
    ///     Records the request and answers with a fixed reply, as a stream of updates.
    /// </summary>
    /// <param name="messages">The conversation handed to the client, unused.</param>
    /// <param name="options">The request options, recorded.</param>
    /// <param name="cancellationToken">A token that cancels the request, ignored.</param>
    /// <returns>A fixed answer, as updates.</returns>
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
    }

    /// <summary>
    ///     Returns no service, because this client offers none.
    /// </summary>
    /// <param name="serviceType">The service being asked for.</param>
    /// <param name="serviceKey">The key qualifying the service, if any.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <summary>
    ///     Releases nothing, because this client holds nothing.
    /// </summary>
    public void Dispose()
    {
        // Nothing is held: the client contacts no server and owns no handle.
    }
}
