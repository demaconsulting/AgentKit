using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Image;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Copilot;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot.LiveTests;

/// <summary>
///     The shared state of one live run: the opt-in gate, a started Copilot client, a sandbox the
///     tools are confined to, the AgentKit tools under test, and a log of what the runtime did.
/// </summary>
/// <remarks>
///     <para>
///     <b>Nothing happens unless the run opted in.</b> When <see cref="ModelVariable"/> is unset the
///     fixture starts no client and every test skips itself, naming the variable, so an accidental
///     run costs nothing. When it is set it names the model every scenario uses — a cheap one is
///     expected.
///     </para>
///     <para>
///     One client serves the whole run, in Empty mode on the in-process runtime, authenticated as
///     the logged-in user (or from <c>GH_TOKEN</c>/<c>GITHUB_TOKEN</c> in CI). It is started through
///     <see cref="CopilotClientStarter"/>, as a host should, and disposed when the run ends.
///     </para>
/// </remarks>
public sealed class CopilotLiveFixture : IAsyncLifetime
{
    /// <summary>
    ///     The environment variable that opts a run in and names its model.
    /// </summary>
    internal const string ModelVariable = "AGENTKIT_COPILOT_LIVE_MODEL";

    /// <summary>
    ///     The environment variable naming the folder the run's log is written to.
    /// </summary>
    internal const string LogDirectoryVariable = "AGENTKIT_COPILOT_LIVE_LOG_DIRECTORY";

    /// <summary>
    ///     The codeword the readable sandbox file carries.
    /// </summary>
    internal const string Codeword = "PERIWINKLE-42";

    /// <summary>
    ///     The codeword the file outside the sandbox carries, which no confined tool may return.
    /// </summary>
    internal const string OutsideCodeword = "MUSTARD-99";

    /// <summary>
    ///     Guards the log, which tests and runtime event handlers both write.
    /// </summary>
    private readonly object _logGate = new();

    /// <summary>
    ///     The run's log, or <see langword="null"/> before initialization or when the run did not opt in.
    /// </summary>
    private StreamWriter? _log;

    /// <summary>
    ///     The started client, or <see langword="null"/> when the run did not opt in.
    /// </summary>
    private CopilotClient? _client;

    /// <summary>
    ///     Gets the model the run uses, or <see langword="null"/> when the run did not opt in.
    /// </summary>
    internal string? Model { get; } = NullIfBlank(Environment.GetEnvironmentVariable(ModelVariable));

    /// <summary>
    ///     Gets the run's root folder, under the system temporary folder.
    /// </summary>
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "agentkit-copilot-live", Guid.NewGuid().ToString("N"));

    /// <summary>
    ///     Gets the folder the tools may read.
    /// </summary>
    internal string Sandbox => Path.Combine(Root, "sandbox");

    /// <summary>
    ///     Gets the folder the tools may not read.
    /// </summary>
    internal string Outside => Path.Combine(Root, "outside");

    /// <summary>
    ///     Gets the started client.
    /// </summary>
    /// <exception cref="InvalidOperationException">The run did not opt in.</exception>
    internal CopilotClient Client => _client ?? throw new InvalidOperationException("The live run did not opt in.");

    /// <summary>
    ///     Gets the AgentKit tools under test: a confined text-file reader and image reader.
    /// </summary>
    internal IList<AIFunction> ReadTools { get; private set; } = [];

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
            $"Live Copilot tests run only when {ModelVariable} names the model to use.");

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

        // The sandbox: a readable note and image, and a secret outside every permitted location
        Directory.CreateDirectory(Sandbox);
        Directory.CreateDirectory(Outside);
        await File.WriteAllTextAsync(Path.Combine(Sandbox, "notes.txt"), $"Project notes.\nCODEWORD: {Codeword}\nEnd of notes.\n");
        await File.WriteAllTextAsync(Path.Combine(Outside, "secret.txt"), $"OUTSIDE-SECRET: {OutsideCodeword}\n");
        await File.WriteAllBytesAsync(Path.Combine(Sandbox, "split.png"), TestPng.SplitBlueYellow(64, 64));

        // The log, beside the test results when the run names a folder for it
        var logDirectory = NullIfBlank(Environment.GetEnvironmentVariable(LogDirectoryVariable)) ?? Root;
        Directory.CreateDirectory(logDirectory);
        LogPath = Path.Combine(logDirectory, $"live-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
        _log = new StreamWriter(LogPath) { AutoFlush = true };
        Log($"Live Copilot run: model={Model} sdk={typeof(CopilotClient).Assembly.GetName().Version} os={Environment.OSVersion}");

        // The tools under test, confined by a path policy that grants only the sandbox
        var policy = new PathPolicy(Sandbox, [PathRule.ReadOnly(Sandbox)]);
        ReadTools =
        [
            .. new ToolPackBuilder(policy)
                .WithHostCapabilities(HostCapabilities.Vision)
                .Add(new TextFilePack())
                .Add(new ImagePack())
                .Build()
                .Where(tool => tool.Name is "text_file_read" or "image_read"),
        ];

        // One client for the run, in Empty mode, started as a host should start it
        _client = new CopilotClient(CreateClientOptions(Path.Combine(Root, "copilot-home")));
        await CopilotClientStarter.StartAsync(_client);
        Log("Client started.");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_log is not null)
        {
            await _log.DisposeAsync();
        }
    }

    /// <summary>
    ///     Builds the options for a client in Empty mode on the in-process runtime, authenticated as
    ///     the logged-in user.
    /// </summary>
    /// <param name="dataDirectory">The runtime's data directory.</param>
    /// <returns>The client options.</returns>
    internal static CopilotClientOptions CreateClientOptions(string dataDirectory) =>
        new()
        {
            Mode = CopilotClientMode.Empty,
            Runtime = RuntimeConnection.ForInProcess(baseDirectory: dataDirectory),
            GitHub = new ClientGitHubOptions { UseLoggedInUser = true },
        };

    /// <summary>
    ///     Normalizes a blank value to <see langword="null"/>.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The value, or <see langword="null"/> when it is blank.</returns>
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>
///     Groups the live tests into one sequential collection sharing one <see cref="CopilotLiveFixture"/>.
/// </summary>
/// <remarks>
///     Sequential on purpose: the run spends model quota, and one scenario changes the process
///     environment while it checks the client starter.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CopilotLiveCollection : ICollectionFixture<CopilotLiveFixture>
{
    /// <summary>
    ///     The collection's name.
    /// </summary>
    internal const string Name = "Copilot live";
}
