using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Rendering;

/// <summary>
///     A local listener that counts every connection it receives and answers each with an HTTP
///     404.
/// </summary>
/// <remarks>
///     <para>
///     A render must never fetch anything from the network. Pointing a document at a listener
///     the test owns, and counting what reaches it, turns "never fetched" into an observable
///     zero. Every fetch connects before it sends a request, so counting connections is at least
///     as strict as counting requests.
///     </para>
///     <para>
///     Each test starts its own listener on a port the operating system chooses for the very
///     socket that listens, so no other process can take the port between choosing and listening.
///     The address is the IPv4 loopback literal, so a client cannot reach for another loopback
///     address the listener does not hold.
///     </para>
/// </remarks>
internal sealed class CountingListener : IDisposable
{
    /// <summary>
    ///     The answer to every connection: not found, and the connection closes.
    /// </summary>
    private static readonly byte[] NotFound =
        Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    /// <summary>
    ///     The listener.
    /// </summary>
    private readonly TcpListener _listener;

    /// <summary>
    ///     Stops the accept loop when the listener is disposed.
    /// </summary>
    private readonly CancellationTokenSource _stop = new();

    /// <summary>
    ///     The number of connections received.
    /// </summary>
    private int _hits;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CountingListener"/> class.
    /// </summary>
    /// <param name="listener">The started listener.</param>
    private CountingListener(TcpListener listener)
    {
        _listener = listener;
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Url = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/";
        _ = CountAsync();
    }

    /// <summary>
    ///     Gets the listener's base address, ending in a slash.
    /// </summary>
    public string Url { get; }

    /// <summary>
    ///     Gets the number of connections received.
    /// </summary>
    public int Hits => Volatile.Read(ref _hits);

    /// <summary>
    ///     Starts a listener on a free loopback port.
    /// </summary>
    /// <returns>The started listener.</returns>
    public static CountingListener Start()
    {
        // Port 0: the operating system chooses a free port for this socket, which keeps it.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new CountingListener(listener);
    }

    /// <summary>
    ///     Stops the listener.
    /// </summary>
    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }

    /// <summary>
    ///     Counts and answers connections until the listener is stopped.
    /// </summary>
    /// <returns>A task that completes when the listener is stopped.</returns>
    private async Task CountAsync()
    {
        try
        {
            var token = _stop.Token;
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync(token);
                Interlocked.Increment(ref _hits);
                try
                {
                    // The request head is read before answering, so the client sees a clean 404
                    // rather than a reset connection.
                    var stream = client.GetStream();
                    await ReadRequestHeadAsync(stream, token);
                    await stream.WriteAsync(NotFound, token);
                    client.Client.Shutdown(SocketShutdown.Send);
                }
                catch (Exception exception) when (exception is IOException or SocketException)
                {
                    // The client went away first; it has been counted already.
                }
            }
        }
        catch (Exception exception) when (exception is SocketException
            or ObjectDisposedException
            or InvalidOperationException
            or OperationCanceledException)
        {
            // The listener was stopped.
        }
    }

    /// <summary>
    ///     Reads a request head up to its blank line, the end of the stream, or a size bound.
    /// </summary>
    /// <param name="stream">The connection's stream.</param>
    /// <param name="token">A token that stops the read.</param>
    /// <returns>A task that completes when the head has been read.</returns>
    private static async Task ReadRequestHeadAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[1024];
        var total = 0;
        var tail = 0u;
        while (total < 64 * 1024)
        {
            var read = await stream.ReadAsync(buffer, token);
            if (read == 0)
            {
                return;
            }

            total += read;
            for (var i = 0; i < read; i++)
            {
                // The last four bytes; CR LF CR LF ends the head.
                tail = (tail << 8) | buffer[i];
                if (tail == 0x0D0A0D0A)
                {
                    return;
                }
            }
        }
    }
}
