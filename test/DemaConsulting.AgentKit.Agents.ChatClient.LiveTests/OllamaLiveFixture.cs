using System.Globalization;
using System.Text;
using OllamaSharp;

namespace DemaConsulting.AgentKit.Agents.ChatClient.LiveTests;

/// <summary>
///     The shared state of one live run: the opt-in gate, the Ollama server and model to use, a
///     sandbox the tools are confined to, and a log of what was sent and answered.
/// </summary>
/// <remarks>
///     <para>
///     <b>Nothing happens unless the run opted in.</b> When <see cref="ModelVariable"/> is unset the
///     fixture creates nothing and every test skips itself, naming the variable, so an accidental run
///     contacts no server.
///     </para>
///     <para>
///     The sandbox holds a note carrying a codeword, a 400-line log whose search results exceed the
///     run's result ceiling, a large speckled PNG of a number, and — outside every permitted location
///     — a secret no confined tool may return.
///     </para>
/// </remarks>
public sealed class OllamaLiveFixture : IAsyncLifetime
{
    /// <summary>
    ///     The environment variable that opts a run in and names its model.
    /// </summary>
    internal const string ModelVariable = "AGENTKIT_OLLAMA_LIVE_MODEL";

    /// <summary>
    ///     The environment variable naming the Ollama server.
    /// </summary>
    internal const string HostVariable = "AGENTKIT_OLLAMA_LIVE_HOST";

    /// <summary>
    ///     The environment variable naming the context length every request asks for.
    /// </summary>
    internal const string NumContextVariable = "AGENTKIT_OLLAMA_LIVE_NUM_CTX";

    /// <summary>
    ///     The environment variable naming the folder the run's log is written to.
    /// </summary>
    internal const string LogDirectoryVariable = "AGENTKIT_OLLAMA_LIVE_LOG_DIRECTORY";

    /// <summary>
    ///     The codeword the readable note carries.
    /// </summary>
    internal const string Codeword = "HERON-42";

    /// <summary>
    ///     The secret outside the sandbox, which no confined tool may return.
    /// </summary>
    internal const string Secret = "SECRET-ZZ9-PLURAL-Z-ALPHA";

    /// <summary>
    ///     The number the test image shows.
    /// </summary>
    internal const string ImageNumber = "7319";

    /// <summary>
    ///     Guards the log, which concurrent handlers may write.
    /// </summary>
    private readonly object _logGate = new();

    /// <summary>
    ///     The run's log, or <see langword="null"/> before initialization or when the run did not opt in.
    /// </summary>
    private StreamWriter? _log;

    /// <summary>
    ///     Gets the model the run uses, or <see langword="null"/> when the run did not opt in.
    /// </summary>
    internal string? Model { get; } = NullIfBlank(Environment.GetEnvironmentVariable(ModelVariable));

    /// <summary>
    ///     Gets the server the run talks to.
    /// </summary>
    internal Uri Host { get; } = new(NullIfBlank(Environment.GetEnvironmentVariable(HostVariable)) ?? "http://localhost:11434");

    /// <summary>
    ///     Gets the context length every request asks for.
    /// </summary>
    internal int NumContext { get; } = int.TryParse(
        Environment.GetEnvironmentVariable(NumContextVariable),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var value) ? value : 16384;

    /// <summary>
    ///     Gets the run's root folder, under the system temporary folder.
    /// </summary>
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "agentkit-ollama-live", Guid.NewGuid().ToString("N"));

    /// <summary>
    ///     Gets the folder the tools may read.
    /// </summary>
    internal string Sandbox => Path.Combine(Root, "ws");

    /// <summary>
    ///     Gets the folder the tools may not read.
    /// </summary>
    internal string Outside => Path.Combine(Root, "outside");

    /// <summary>
    ///     Gets the size in bytes of the test image.
    /// </summary>
    internal int ImageBytes { get; private set; }

    /// <summary>
    ///     Gets the path of the run's log, or <see langword="null"/> when none is written.
    /// </summary>
    internal string? LogPath { get; private set; }

    /// <summary>
    ///     Skips the calling test unless the run opted in.
    /// </summary>
    internal void RequireOptIn() =>
        Assert.SkipUnless(
            Model is not null,
            $"Live Ollama tests run only when {ModelVariable} names the model to use.");

    /// <summary>
    ///     Creates an OllamaSharp client over a wire-recording handler.
    /// </summary>
    /// <returns>The client and the handler recording its requests.</returns>
    internal (OllamaApiClient Client, WireRecordingHandler Wire) CreateClient()
    {
        var wire = new WireRecordingHandler(NumContext, Log);
        var http = new HttpClient(wire) { BaseAddress = Host, Timeout = TimeSpan.FromMinutes(10) };
        return (new OllamaApiClient(http, Model!), wire);
    }

    /// <summary>
    ///     Writes one line to the run's log and to the current test's output.
    /// </summary>
    /// <param name="line">The line.</param>
    internal void Log(string line)
    {
        lock (_logGate)
        {
            _log?.WriteLine($"{DateTime.UtcNow:HH:mm:ss.fff} {line}");
        }

        TestContext.Current.TestOutputHelper?.WriteLine(line);
    }

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        if (Model is null)
        {
            return;
        }

        // The sandbox: a note, a log whose matches exceed the ceiling, a large image, and a secret
        Directory.CreateDirectory(Sandbox);
        Directory.CreateDirectory(Outside);
        await File.WriteAllTextAsync(Path.Combine(Sandbox, "notes.txt"), $"Project notes.\nProject codename: {Codeword}\nOwner: live suite\n");
        await File.WriteAllTextAsync(Path.Combine(Outside, "secret.txt"), $"TOP SECRET: {Secret}\n");
        var big = new StringBuilder();
        for (var line = 1; line <= 400; line++)
        {
            big.Append(line % 10 == 0
                ? $"{line:0000} ERROR code=E{line / 10:00} order=ORD-{line * 7:00000} component=billing"
                : $"{line:0000} INFO heartbeat ok component=web").Append('\n');
        }

        await File.WriteAllTextAsync(Path.Combine(Sandbox, "big.log"), big.ToString());
        var png = TestPng.Number(ImageNumber, scale: 24);
        ImageBytes = png.Length;
        await File.WriteAllBytesAsync(Path.Combine(Sandbox, "dashboard.png"), png);

        // The log, beside the test results when the run names a folder for it
        var logDirectory = NullIfBlank(Environment.GetEnvironmentVariable(LogDirectoryVariable)) ?? Root;
        Directory.CreateDirectory(logDirectory);
        LogPath = Path.Combine(logDirectory, $"ollama-live-{Model.Replace(':', '_')}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
        _log = new StreamWriter(LogPath) { AutoFlush = true };
        Log($"Live Ollama run: model={Model} host={Host} num_ctx={NumContext} image={ImageBytes} bytes os={Environment.OSVersion}");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_log is not null)
        {
            await _log.DisposeAsync();
        }

        if (Model is not null && Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    /// <summary>
    ///     Normalizes a blank value to <see langword="null"/>.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The value, or <see langword="null"/> when it is blank.</returns>
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
///     Groups the live tests into one sequential collection sharing one <see cref="OllamaLiveFixture"/>.
/// </summary>
/// <remarks>
///     Sequential on purpose: the scenarios share one model on one server, and parallel requests would
///     only queue there while making the log unreadable.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OllamaLiveCollection : ICollectionFixture<OllamaLiveFixture>
{
    /// <summary>
    ///     The collection's name.
    /// </summary>
    internal const string Name = "Ollama live";
}
