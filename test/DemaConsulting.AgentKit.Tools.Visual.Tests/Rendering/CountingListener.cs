using System.Net;
using System.Net.Sockets;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Rendering;

/// <summary>
///     A local HTTP listener that counts every request it receives.
/// </summary>
/// <remarks>
///     A render must never fetch anything from the network. Pointing a document at a listener
///     the test owns, and counting what reaches it, turns "never fetched" into an observable
///     zero. Each test starts its own listener on a port the operating system chose.
/// </remarks>
internal sealed class CountingListener : IDisposable
{
    /// <summary>
    ///     The listener.
    /// </summary>
    private readonly HttpListener _listener;

    /// <summary>
    ///     The number of requests received.
    /// </summary>
    private int _hits;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CountingListener"/> class.
    /// </summary>
    /// <param name="listener">The started listener.</param>
    /// <param name="url">The listener's base address.</param>
    private CountingListener(HttpListener listener, string url)
    {
        _listener = listener;
        Url = url;
        _ = CountAsync();
    }

    /// <summary>
    ///     Gets the listener's base address, ending in a slash.
    /// </summary>
    public string Url { get; }

    /// <summary>
    ///     Gets the number of requests received.
    /// </summary>
    public int Hits => Volatile.Read(ref _hits);

    /// <summary>
    ///     Starts a listener on a free local port.
    /// </summary>
    /// <returns>The started listener.</returns>
    public static CountingListener Start()
    {
        // A free port, found by letting the operating system choose one.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var url = "http://localhost:" + port + "/";
        var listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Start();
        return new CountingListener(listener, url);
    }

    /// <summary>
    ///     Stops the listener.
    /// </summary>
    public void Dispose()
    {
        _listener.Close();
    }

    /// <summary>
    ///     Counts and answers requests until the listener is closed.
    /// </summary>
    /// <returns>A task that completes when the listener is closed.</returns>
    private async Task CountAsync()
    {
        try
        {
            while (_listener.IsListening)
            {
                var context = await _listener.GetContextAsync();
                Interlocked.Increment(ref _hits);
                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
        {
            // The listener was closed.
        }
    }
}
