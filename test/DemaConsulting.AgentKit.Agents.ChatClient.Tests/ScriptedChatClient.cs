using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     A chat client that contacts nothing, records the conversation it was handed, and returns
///     a canned response.
/// </summary>
/// <remarks>
///     <para>
///     The behavior under verification is the installation of the image-promoting decorator and
///     the rewrite it performs, so the only thing a test needs from a client is a faithful record
///     of what it received. A real provider would add network access, credentials and
///     non-determinism without contributing anything to the assertion, and the asymmetry that
///     motivates the decorator cannot be observed from inside this process in any case — it
///     happens at a provider's wire format.
///     </para>
///     <para>
///     This is a hand-written copy of the helper the Core tests use, because that one is internal
///     to the Core test project. Written by hand rather than substituted, because the streaming
///     member returns an asynchronous sequence that a substitute would have to be taught to
///     produce anyway.
///     </para>
///     <para>
///     Each test constructs its own instance, so no state is shared. Instances are not
///     thread-safe and are not intended to be used from more than one test at a time.
///     </para>
/// </remarks>
internal sealed class ScriptedChatClient : IChatClient
{
    /// <summary>
    ///     Gets the conversation the client was last handed, in the order it arrived.
    /// </summary>
    /// <remarks>
    ///     Materialized on receipt rather than stored as the sequence itself, so that a test
    ///     asserts on what the client actually observed rather than re-enumerating a sequence
    ///     that might be produced differently the second time.
    /// </remarks>
    public IReadOnlyList<ChatMessage> ReceivedMessages { get; private set; } = [];

    /// <summary>
    ///     Records the conversation and returns a fixed, uninteresting response.
    /// </summary>
    /// <param name="messages">The conversation handed to the client.</param>
    /// <param name="options">The request options, ignored.</param>
    /// <param name="cancellationToken">A token that cancels the request, ignored.</param>
    /// <returns>A completed task carrying the canned response.</returns>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ReceivedMessages = messages.ToList();

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
    }

    /// <summary>
    ///     Records the conversation and returns a fixed, uninteresting stream of updates.
    /// </summary>
    /// <param name="messages">The conversation handed to the client.</param>
    /// <param name="options">The request options, ignored.</param>
    /// <param name="cancellationToken">A token that cancels the request, ignored.</param>
    /// <returns>A single-element asynchronous sequence carrying the canned update.</returns>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ReceivedMessages = messages.ToList();

        // Yielding inside an asynchronous iterator requires an await for the method to be
        // genuinely asynchronous; a completed task is the cheapest one that keeps the shape.
        await Task.CompletedTask.ConfigureAwait(false);
        yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
    }

    /// <summary>
    ///     Returns no service, because this client offers none.
    /// </summary>
    /// <param name="serviceType">The service being asked for.</param>
    /// <param name="serviceKey">The key qualifying the service, if any.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    /// <summary>
    ///     Releases nothing, because this client holds nothing.
    /// </summary>
    public void Dispose()
    {
        // No unmanaged resource, no connection and no handle is held; the member exists only to
        // satisfy the interface a real client needs it for.
    }
}
