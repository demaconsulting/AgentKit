using System.Net;
using System.Text;

namespace DemaConsulting.AgentKit.Agents.ChatClient.Tests;

/// <summary>
///     An HTTP message handler that records every request body it is sent and answers from a script
///     of canned response bodies, so a real HTTP client library can be exercised without a server.
/// </summary>
/// <remarks>
///     <para>
///     Used to observe exactly what a provider library puts on the wire — the request body it
///     serializes — which is the only place a defect in how it maps chat content can be seen. The
///     real client library runs unchanged above it; only the network is replaced.
///     </para>
///     <para>
///     Each test constructs its own instance, so no state is shared. Instances are not thread-safe.
///     </para>
/// </remarks>
internal sealed class CapturingHttpHandler : HttpMessageHandler
{
    /// <summary>
    ///     The response bodies queued for the next requests, oldest first.
    /// </summary>
    private readonly Queue<string> _responses = new();

    /// <summary>
    ///     Gets the body of every request received, oldest first.
    /// </summary>
    public List<string> RequestBodies { get; } = [];

    /// <summary>
    ///     Queues one response body, returned with status 200 to the next request.
    /// </summary>
    /// <param name="body">The response body.</param>
    /// <returns>This handler, so a script reads as one statement.</returns>
    public CapturingHttpHandler Respond(string body)
    {
        _responses.Enqueue(body);
        return this;
    }

    /// <summary>
    ///     Records the request body and answers with the next scripted body.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The scripted response.</returns>
    /// <exception cref="InvalidOperationException">The test scripted no response for this request.</exception>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("The test scripted no response for request " + RequestBodies.Count + ".");
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_responses.Dequeue(), Encoding.UTF8, "application/x-ndjson")
        };
    }
}
