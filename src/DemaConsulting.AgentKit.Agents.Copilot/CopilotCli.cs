using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     The installed GitHub Copilot CLI AgentKit runs by default: where it is found, which version it
///     must be, and how AgentKit reads that version before it uses the CLI.
/// </summary>
/// <remarks>
///     <para>
///     <b>Discovery.</b> <see cref="FindInstalled()"/> looks for the CLI first on <c>PATH</c> — on
///     Windows only <c>copilot.exe</c>, because the <c>.cmd</c> and <c>.ps1</c> shims npm creates
///     cannot be started without a shell; elsewhere a <c>copilot</c> file with an execute bit — and then
///     in the platform's well-known installation locations: WinGet and npm on Windows; Homebrew, the
///     install script's <c>~/.local/bin</c> and npm on macOS and Linux. The first candidate that exists,
///     can be run and is not an editor's launcher is chosen. The Copilot launcher Visual Studio Code
///     puts on <c>PATH</c> forwards to another CLI and may prompt to install one, so it is passed over
///     and reported. <c>COPILOT_CLI_PATH</c> is never consulted: the ambient environment does not choose
///     the executable a confined runtime runs; a host that wants a specific CLI passes its path.
///     </para>
///     <para>
///     <b>Version.</b> The CLI is started with <c>--no-auto-update</c>, which makes it run the version
///     built into the installed executable rather than a newer one that auto-update or
///     <c>copilot update</c> downloaded into the CLI's package cache.
///     AgentKit therefore reads the version with <c>&lt;cli&gt; --no-auto-update --version</c> — the
///     version the runtime will run — and refuses a CLI older than <see cref="MinimumVersion"/>, the
///     first that speaks the SDK protocol this release's <c>Microsoft.Copilot.SDK</c> requires. The
///     probe runs without a shell, under the same withheld environment and data directory as the
///     runtime, and within a bounded time.
///     </para>
///     <para>
///     Stateless and safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Showing a user which Copilot CLI AgentKit would run, or why it found none.
///     </para>
///     <code>
///     var found = CopilotCli.FindInstalled();
///     Console.WriteLine(found.Path is null
///         ? $"No Copilot CLI found; searched: {string.Join(", ", found.SearchedLocations)}"
///         : $"Copilot CLI: {found.Path} (version {CopilotCli.MinimumVersion} or later required)");
///     </code>
/// </example>
public static partial class CopilotCli
{
    /// <summary>
    ///     The SDK protocol version the referenced <c>Microsoft.Copilot.SDK</c> speaks, and the only
    ///     one it accepts from a runtime.
    /// </summary>
    /// <remarks>
    ///     Tied to the SDK reference together with <see cref="MinimumVersion"/>: revisit both whenever the
    ///     SDK is updated.
    /// </remarks>
    internal const int SupportedProtocolVersion = 4;

    /// <summary>
    ///     The largest file the launcher check reads, in bytes.
    /// </summary>
    /// <remarks>
    ///     An editor's launcher is small; a real CLI is a large single-file executable that is never
    ///     read, so discovery stays fast.
    /// </remarks>
    private const long LauncherScanLimit = 16L * 1024 * 1024;

    /// <summary>
    ///     The folder Visual Studio Code keeps its Copilot launcher in.
    /// </summary>
    private const string ShimFolderName = "copilot-shim";

    /// <summary>
    ///     How much of a failed probe's error output a message quotes, in characters.
    /// </summary>
    private const int ErrorTailLength = 500;

    /// <summary>
    ///     The reason recorded for a candidate that is an editor's launcher.
    /// </summary>
    private const string LauncherReason =
        "a Visual Studio Code Copilot launcher, which forwards to another CLI and may prompt to install one";

    /// <summary>
    ///     The commands that install the Copilot CLI, per installation method.
    /// </summary>
    private const string InstallCommands =
        "'winget install GitHub.Copilot' (Windows), 'brew install --cask copilot-cli' (macOS), "
        + "'npm install -g @github/copilot' (any platform with Node.js), or "
        + "'curl -fsSL https://gh.io/copilot-install | bash' (macOS and Linux)";

    /// <summary>
    ///     The commands that upgrade an installed Copilot CLI, per installation method.
    /// </summary>
    private const string UpgradeCommands =
        "'winget upgrade GitHub.Copilot' for a WinGet installation, 'brew upgrade --cask copilot-cli' for Homebrew, "
        + "'npm install -g @github/copilot@latest' for npm, or the install script again "
        + "('curl -fsSL https://gh.io/copilot-install | bash')";

    /// <summary>
    ///     Why the version a CLI reports interactively can differ from the one AgentKit runs.
    /// </summary>
    private const string BuiltInVersionNote =
        "AgentKit starts the CLI with --no-auto-update, which runs the version built into the installed executable; "
        + "check that version with 'copilot --no-auto-update --version'. A newer version that auto-update or "
        + "'copilot update' downloaded is kept in the CLI's package cache, and 'copilot --version' can report it, "
        + "but AgentKit does not run it; only upgrading the installation itself changes the version AgentKit runs.";

    /// <summary>
    ///     The alternatives to the CLI discovery found or the host named.
    /// </summary>
    private const string Alternatives =
        "Or pass the full path of a supported Copilot CLI as CopilotRuntimeOptions' cliPath, or run the runtime "
        + "the SDK bundles with CopilotRuntimeSource.Bundled, for which the host application references "
        + "Microsoft.Copilot.SDK directly (it provisions several hundred megabytes of runtime into every output folder).";

    /// <summary>
    ///     The directory separators of every platform, so a path from any platform splits into its folders.
    /// </summary>
    private static readonly char[] DirectorySeparators = ['/', '\\'];

    /// <summary>
    ///     The marker in the Visual Studio Code Copilot launcher executable.
    /// </summary>
    private static ReadOnlySpan<byte> RustLauncherMarker => "VSCODE_COPILOT_RUST_SHIM_V1"u8;

    /// <summary>
    ///     The marker in the Visual Studio Code Copilot launcher script.
    /// </summary>
    private static ReadOnlySpan<byte> ScriptLauncherMarker => "vscode-copilot-launcher"u8;

    /// <summary>
    ///     Gets the oldest Copilot CLI version AgentKit runs: the first that speaks the SDK protocol the
    ///     referenced <c>Microsoft.Copilot.SDK</c> requires.
    /// </summary>
    /// <remarks>
    ///     Compared with the version the CLI runs when started with <c>--no-auto-update</c>, which is the
    ///     version built into the installed executable. Tied to the SDK reference: a release of AgentKit that
    ///     updates the SDK revisits it.
    /// </remarks>
    public static Version MinimumVersion { get; } = new(1, 0, 113);

    /// <summary>
    ///     Gets how long the version probe may run before it is stopped.
    /// </summary>
    /// <remarks>
    ///     Generous on purpose: a cold first run of a CLI version unpacks its package into the user's
    ///     cache before the version is printed, and under antivirus scanning has been measured at well
    ///     over half a minute; two minutes leaves margin for a slow disk.
    /// </remarks>
    internal static TimeSpan VersionProbeTimeout { get; } = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     Finds the installed Copilot CLI an installed-CLI runtime runs when the host names none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Searches <c>PATH</c>, then the platform's well-known installation locations, and returns the
    ///     first candidate that exists, can be run and is not an editor's launcher. The CLI's version is
    ///     not checked; <see cref="CopilotRuntime.StartAsync"/> checks it before using the CLI.
    ///     <c>COPILOT_CLI_PATH</c> is not consulted.
    ///     </para>
    ///     <para>
    ///     Side effects: reads the process environment and the file system, and reads small candidate
    ///     files to recognize a launcher. Starts no process.
    ///     </para>
    /// </remarks>
    /// <returns>The CLI found, if any, with every location searched and every candidate passed over.</returns>
    public static CopilotCliSearchResult FindInstalled() => FindInstalled(CopilotCliSearchContext.Current());

    /// <summary>
    ///     Finds the installed Copilot CLI in a given search context.
    /// </summary>
    /// <remarks>
    ///     The production overload passes the current machine; tests pass a fake file system and
    ///     environment for any platform.
    /// </remarks>
    /// <param name="context">The platform, environment and file system to search.</param>
    /// <returns>The CLI found, if any, with every location searched and every candidate passed over.</returns>
    internal static CopilotCliSearchResult FindInstalled(CopilotCliSearchContext context)
    {
        var searched = new List<string>();
        var skipped = new List<string>();
        var seen = new HashSet<string>(context.PathComparer);

        foreach (var candidate in Candidates(context))
        {
            // The same file reached twice - a PATH entry that is also a well-known folder - is looked at once
            if (!seen.Add(candidate))
            {
                continue;
            }

            searched.Add(candidate);
            if (!IsRunnable(candidate, context))
            {
                continue;
            }

            // An editor's launcher forwards elsewhere and may prompt; it is reported, never chosen
            if (IsLauncher(candidate, context))
            {
                skipped.Add($"{candidate} ({LauncherReason})");
                continue;
            }

            return new CopilotCliSearchResult(candidate, searched, skipped);
        }

        return new CopilotCliSearchResult(null, searched, skipped);
    }

    /// <summary>
    ///     Reads the version an installed Copilot CLI runs when the SDK starts it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Runs <c>&lt;cli&gt; --no-auto-update --version</c> — for a <c>.js</c> CLI,
    ///     <c>&lt;node&gt; &lt;cli&gt; --no-auto-update --version</c> with <c>node</c> found on
    ///     <c>PATH</c> — without a shell, in the data directory, within <see cref="VersionProbeTimeout"/>.
    ///     The probe runs under the environment the runtime is spawned with — the host's with its Copilot
    ///     configuration variables withheld — plus <c>COPILOT_HOME</c> set to the data directory. The
    ///     variables the SDK adds when it spawns the runtime itself (the keychain switch and a supplied
    ///     token) are not set; they do not affect <c>--version</c>.
    ///     </para>
    ///     <para>
    ///     Output that names no version is not a refusal: the SDK's protocol handshake at start is the
    ///     authoritative check and fails closed, so <see langword="null"/> is returned and the start
    ///     proceeds. A CLI that cannot be run, exits with an error or runs too long is refused.
    ///     </para>
    /// </remarks>
    /// <param name="cliPath">The CLI's full path.</param>
    /// <param name="dataDirectory">The prepared data directory's full path.</param>
    /// <param name="runtimeEnvironment">
    ///     The environment the runtime is spawned with — the host's with its Copilot configuration
    ///     variables withheld — which, with <c>COPILOT_HOME</c> set, becomes the probe's whole environment.
    /// </param>
    /// <param name="context">The platform and file system, used to find <c>node</c> for a <c>.js</c> CLI.</param>
    /// <param name="runner">Runs the probe process.</param>
    /// <param name="cancellationToken">Cancels the probe, which stops the process.</param>
    /// <returns>The version the CLI reports, or <see langword="null"/> when its output names none.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The CLI is a <c>.js</c> file and <c>node</c> is not on <c>PATH</c>; or it could not be started,
    ///     exited with an error, or did not finish in time. The message names the CLI and the command;
    ///     it, and the error output it quotes, are redacted of the probe environment's secret values
    ///     and any text shaped like a GitHub token, the error output before its tail is cut. For a CLI
    ///     that could not be started, the cause is a redacted copy of the failure
    ///     (<see cref="CopilotRedactedException"/>), never the original.
    /// </exception>
    /// <exception cref="OperationCanceledException">The probe was canceled.</exception>
    internal static async Task<Version?> ReadVersionAsync(
        string cliPath,
        string dataDirectory,
        IReadOnlyDictionary<string, string> runtimeEnvironment,
        CopilotCliSearchContext context,
        CopilotCliProcessRunner runner,
        CancellationToken cancellationToken)
    {
        // Everything the probe could echo is in its environment, so that is what its text is redacted of
        var secrets = CopilotSignInSecrets.FromEnvironment(runtimeEnvironment);
        Func<string, string> redact = text => secrets.Redact(text);
        var startInfo = BuildVersionProbe(cliPath, dataDirectory, runtimeEnvironment, context, redact);
        var command = DescribeCommand(startInfo);

        // Run the probe; a CLI that cannot even be started is explained like any other failed probe
        CopilotCliProcessResult result;
        try
        {
            result = await runner(startInfo, VersionProbeTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                ExplainProbeFailure(cliPath, command, "could not be started: " + failure.Message, standardError: null, redact),
                CopilotSignIn.RedactFailure(failure, new CopilotSignInPlan { Secrets = secrets }));
        }

        if (result.TimedOut)
        {
            throw new InvalidOperationException(ExplainProbeFailure(
                cliPath,
                command,
                $"did not finish within {VersionProbeTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds and was stopped",
                result.StandardError,
                redact));
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(ExplainProbeFailure(
                cliPath,
                command,
                $"exited with code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}",
                result.StandardError,
                redact));
        }

        return ParseVersion(result.StandardOutput);
    }

    /// <summary>
    ///     Reads a version from a CLI's <c>--version</c> output.
    /// </summary>
    /// <remarks>
    ///     The banner <c>GitHub Copilot CLI X.Y.Z.</c> is preferred; otherwise the first
    ///     <c>X.Y.Z</c> in the output is taken. A prerelease suffix is ignored, so the core version is
    ///     what is compared.
    /// </remarks>
    /// <param name="output">The CLI's standard output.</param>
    /// <returns>The version, or <see langword="null"/> when the output names none.</returns>
    internal static Version? ParseVersion(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        var match = BannerVersion().Match(output);
        if (!match.Success)
        {
            match = AnyVersion().Match(output);
        }

        return match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            && int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            && int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var build)
                ? new Version(major, minor, build)
                : null;
    }

    /// <summary>
    ///     Determines whether a CLI version is one AgentKit runs.
    /// </summary>
    /// <param name="version">The CLI's version.</param>
    /// <returns><see langword="true"/> when it is at least <see cref="MinimumVersion"/>.</returns>
    internal static bool IsSupported(Version version) =>
        new Version(version.Major, version.Minor, Math.Max(version.Build, 0)) >= MinimumVersion;

    /// <summary>
    ///     Reads the protocol version a runtime reported from the SDK's protocol-mismatch error.
    /// </summary>
    /// <param name="message">The SDK's error message.</param>
    /// <returns>The runtime's protocol version, or <see langword="null"/> when the message names none.</returns>
    internal static int? ParseServerProtocol(string? message)
    {
        var match = string.IsNullOrEmpty(message) ? Match.Empty : ServerProtocol().Match(message);
        return match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var protocol)
                ? protocol
                : null;
    }

    /// <summary>
    ///     Determines whether a start failure is the SDK's protocol-version mismatch.
    /// </summary>
    /// <param name="message">The start failure's message.</param>
    /// <returns><see langword="true"/> when the message reports a protocol version mismatch.</returns>
    internal static bool IsProtocolMismatch(string? message) =>
        message?.Contains("protocol version mismatch", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    ///     Runs a process to completion, capturing its output, and stops it — with every process it
    ///     started — when it runs too long or the run is canceled.
    /// </summary>
    /// <remarks>
    ///     Standard input is closed at once, so a CLI that would prompt sees end of input instead of
    ///     waiting. Both output streams are read concurrently, so neither can fill and stall the process.
    ///     The process must exit, and its output must close, within <paramref name="timeout"/>: output
    ///     held open past the limit by a process it started counts as running too long. What a stopped
    ///     process started is stopped as far as <see cref="CopilotCliProcessTree"/> reaches on the platform.
    /// </remarks>
    /// <param name="startInfo">The process to run; it must redirect standard input, output and error.</param>
    /// <param name="timeout">How long the process, and its output, may run.</param>
    /// <param name="cancellationToken">Cancels the run, which stops the process.</param>
    /// <returns>The exit code and output, or a result marked as timed out.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">The process could not be started.</exception>
    /// <exception cref="OperationCanceledException">The run was canceled.</exception>
    internal static async Task<CopilotCliProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var tree = CopilotCliProcessTree.Track(process);
        CloseInput(process);

        // Both streams drain while the process runs, so neither pipe fills and stalls it
        var standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);

            // A process that exited can leave its output open through a child that inherited it;
            // the same limit bounds that wait
            await Task.WhenAll(standardOutput, standardError).WaitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Too long or canceled: stop it with what it started, and report what was written before
            tree.Kill();
            cancellationToken.ThrowIfCancellationRequested();
            var drained = Task.WhenAll(standardOutput, standardError);
            await Task.WhenAny(drained, Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None)).ConfigureAwait(false);
            return new CopilotCliProcessResult(
                -1,
                standardOutput.IsCompletedSuccessfully ? standardOutput.Result : string.Empty,
                standardError.IsCompletedSuccessfully ? standardError.Result : string.Empty,
                TimedOut: true);
        }

        return new CopilotCliProcessResult(
            process.ExitCode,
            standardOutput.Result,
            standardError.Result,
            TimedOut: false);
    }

    /// <summary>
    ///     Composes the explanation for an installed CLI that could not be found.
    /// </summary>
    /// <param name="result">What discovery searched and passed over.</param>
    /// <returns>The explanation.</returns>
    internal static string ExplainNotFound(CopilotCliSearchResult result)
    {
        var searched = result.SearchedLocations.Count == 0
            ? "no location (PATH is empty and no well-known installation folder applies)"
            : string.Join("; ", result.SearchedLocations);
        var skipped = result.SkippedCandidates.Count == 0
            ? string.Empty
            : " It passed over: " + string.Join("; ", result.SkippedCandidates) + ".";

        return "AgentKit could not find an installed GitHub Copilot CLI to run as the Copilot runtime. It looked for "
            + searched + "." + skipped + " COPILOT_CLI_PATH is not consulted. Install the Copilot CLI, version "
            + MinimumVersion + " or later, with " + InstallCommands + ". " + Alternatives;
    }

    /// <summary>
    ///     Composes the explanation for a CLI path the host named that does not exist.
    /// </summary>
    /// <param name="cliPath">The path the host named.</param>
    /// <returns>The explanation.</returns>
    internal static string ExplainExplicitPathMissing(string cliPath) =>
        $"The Copilot CLI '{cliPath}' passed as CopilotRuntimeOptions' cliPath does not exist. Pass the full path of an "
        + $"installed Copilot CLI, version {MinimumVersion} or later, or leave cliPath unset to let AgentKit find one. "
        + "Or run the runtime the SDK bundles with CopilotRuntimeSource.Bundled, for which the host application "
        + "references Microsoft.Copilot.SDK directly.";

    /// <summary>
    ///     Composes the explanation for a CLI older than <see cref="MinimumVersion"/>.
    /// </summary>
    /// <param name="cliPath">The CLI's full path.</param>
    /// <param name="installed">The version the CLI runs.</param>
    /// <returns>The explanation.</returns>
    internal static string ExplainTooOld(string cliPath, Version installed) =>
        $"The Copilot CLI at '{cliPath}' is version {installed}, which is too old for AgentKit: it needs version "
        + $"{MinimumVersion} or later, which speaks SDK protocol version {SupportedProtocolVersion}. "
        + BuiltInVersionNote + " Upgrade the installation itself: " + UpgradeCommands + ". " + Alternatives;

    /// <summary>
    ///     Composes the explanation for a runtime the SDK refused for its protocol version.
    /// </summary>
    /// <remarks>
    ///     A runtime older than the supported protocol is a CLI to upgrade; one newer is a CLI this
    ///     release of AgentKit predates, so the remedy is a newer AgentKit, an older CLI or the bundled
    ///     runtime.
    /// </remarks>
    /// <param name="cliPath">The CLI's full path, or <see langword="null"/> when it is not known.</param>
    /// <param name="cliVersion">The version the CLI reported, or <see langword="null"/> when it is not known.</param>
    /// <param name="serverProtocol">The protocol version the runtime reported, or <see langword="null"/> when not known.</param>
    /// <returns>The explanation.</returns>
    internal static string ExplainProtocolMismatch(string? cliPath, Version? cliVersion, int? serverProtocol)
    {
        var subject = "The Copilot CLI"
            + (cliPath is null ? string.Empty : $" at '{cliPath}'")
            + (cliVersion is null ? string.Empty : $" (version {cliVersion})");
        var reported = serverProtocol is null
            ? "does not speak"
            : $"speaks SDK protocol version {serverProtocol.Value.ToString(CultureInfo.InvariantCulture)}, not";

        if (serverProtocol > SupportedProtocolVersion)
        {
            return $"{subject} {reported} version {SupportedProtocolVersion}, the only one this release of AgentKit supports: "
                + "the CLI is newer than this release of AgentKit. Update AgentKit to a release built for that CLI, run a "
                + $"supported CLI (version {MinimumVersion} or later that speaks protocol {SupportedProtocolVersion}) by "
                + "passing its full path as CopilotRuntimeOptions' cliPath, or run the runtime the SDK bundles with "
                + "CopilotRuntimeSource.Bundled.";
        }

        return $"{subject} {reported} SDK protocol version {SupportedProtocolVersion}, which AgentKit requires: it needs "
            + $"Copilot CLI {MinimumVersion} or later. " + BuiltInVersionNote + " Upgrade the installation itself: "
            + UpgradeCommands + ". " + Alternatives;
    }

    /// <summary>
    ///     Builds the version probe's process start information.
    /// </summary>
    /// <param name="cliPath">The CLI's full path.</param>
    /// <param name="dataDirectory">The prepared data directory's full path.</param>
    /// <param name="runtimeEnvironment">The runtime's environment, which becomes the probe's.</param>
    /// <param name="context">The platform and file system, used to find <c>node</c>.</param>
    /// <param name="redact">Removes the probe environment's secrets from the refusal's text.</param>
    /// <returns>The start information: no shell, all streams redirected, the runtime's environment.</returns>
    /// <exception cref="InvalidOperationException">The CLI is a <c>.js</c> file and <c>node</c> is not on <c>PATH</c>.</exception>
    private static ProcessStartInfo BuildVersionProbe(
        string cliPath,
        string dataDirectory,
        IReadOnlyDictionary<string, string> runtimeEnvironment,
        CopilotCliSearchContext context,
        Func<string, string> redact)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = dataDirectory,
        };

        // An npm installation's entry point is a script the SDK runs with node; the probe does the same,
        // with node resolved to a full path so no shell or ambient lookup chooses it
        if (IsScript(cliPath))
        {
            startInfo.FileName = FindOnPath(context, "node") ?? throw new InvalidOperationException(redact(
                $"The Copilot CLI at '{cliPath}' is an npm installation, which runs on Node.js, and AgentKit did not find "
                + $"'{context.ExecutableName("node")}' on PATH. Install Node.js and put it on PATH, or install the Copilot CLI "
                + $"another way: {InstallCommands}."));
            startInfo.ArgumentList.Add(cliPath);
        }
        else
        {
            startInfo.FileName = cliPath;
        }

        startInfo.ArgumentList.Add("--no-auto-update");
        startInfo.ArgumentList.Add("--version");

        // The runtime's own environment: the host's Copilot variables withheld, its home the data directory
        startInfo.Environment.Clear();
        foreach (var (name, value) in runtimeEnvironment)
        {
            startInfo.Environment[name] = value;
        }

        startInfo.Environment["COPILOT_HOME"] = dataDirectory;
        return startInfo;
    }

    /// <summary>
    ///     Composes the explanation for a version probe that failed.
    /// </summary>
    /// <remarks>
    ///     The error output is redacted before its tail is cut, so a secret cut in two can be neither
    ///     quoted nor missed; the whole explanation, which names the CLI's path and command, is then
    ///     redacted too.
    /// </remarks>
    /// <param name="cliPath">The CLI's full path.</param>
    /// <param name="command">The command that was run.</param>
    /// <param name="reason">What went wrong, completing "the command ...".</param>
    /// <param name="standardError">The probe's error output, or <see langword="null"/> when there is none.</param>
    /// <param name="redact">Removes the probe environment's secrets from a text.</param>
    /// <returns>The explanation.</returns>
    private static string ExplainProbeFailure(string cliPath, string command, string reason, string? standardError, Func<string, string> redact)
    {
        var error = standardError is null ? string.Empty : redact(standardError.Trim());
        if (error.Length > ErrorTailLength)
        {
            error = error[^ErrorTailLength..];
        }

        var tail = error.Length == 0 ? string.Empty : " Its error output ends: " + error;

        return redact($"AgentKit could not read the version of the Copilot CLI at '{cliPath}': the command {command} {reason}."
            + tail + $" Check that it is a working Copilot CLI, version {MinimumVersion} or later, with "
            + "'copilot --no-auto-update --version'. " + Alternatives);
    }

    /// <summary>
    ///     Lists every candidate in search order: <c>PATH</c>, then the platform's well-known locations.
    /// </summary>
    /// <param name="context">The search context.</param>
    /// <returns>The candidates' full paths.</returns>
    private static IEnumerable<string> Candidates(CopilotCliSearchContext context)
    {
        foreach (var directory in PathDirectories(context))
        {
            yield return Path.Combine(directory, context.ExecutableName("copilot"));
        }

        foreach (var location in WellKnownLocations(context))
        {
            yield return location;
        }
    }

    /// <summary>
    ///     Lists the platform's well-known Copilot CLI installation locations, in search order.
    /// </summary>
    /// <remarks>
    ///     Windows: the WinGet link and package folders, then npm's global package. macOS: Homebrew
    ///     (Apple silicon, then Intel), the install script's <c>~/.local/bin</c>, then npm's global
    ///     package under each Homebrew prefix. Linux: the install script's <c>~/.local/bin</c>,
    ///     <c>/usr/local/bin</c>, Linuxbrew, then npm's global package under the usual prefixes.
    /// </remarks>
    /// <param name="context">The search context.</param>
    /// <returns>The locations' full paths.</returns>
    private static IEnumerable<string> WellKnownLocations(CopilotCliSearchContext context)
    {
        var npmPackage = Path.Combine("node_modules", "@github", "copilot", "npm-loader.js");
        var root = Path.AltDirectorySeparatorChar.ToString();

        if (context.Platform == CopilotCliPlatform.Windows)
        {
            if (context.LocalAppData is not null)
            {
                var winGet = Path.Combine(context.LocalAppData, "Microsoft", "WinGet");
                yield return Path.Combine(winGet, "Links", "copilot.exe");
                foreach (var package in context.GetDirectories(Path.Combine(winGet, "Packages"), "GitHub.Copilot_*").Order(StringComparer.Ordinal))
                {
                    yield return Path.Combine(package, "copilot.exe");
                }
            }

            if (context.AppData is not null)
            {
                yield return Path.Combine(context.AppData, "npm", npmPackage);
            }

            yield break;
        }

        if (context.Platform == CopilotCliPlatform.MacOS)
        {
            yield return Path.Combine(root, "opt", "homebrew", "bin", "copilot");
            yield return Path.Combine(root, "usr", "local", "bin", "copilot");
            if (context.UserProfile is not null)
            {
                yield return Path.Combine(context.UserProfile, ".local", "bin", "copilot");
            }

            yield return Path.Combine(root, "opt", "homebrew", "lib", npmPackage);
            yield return Path.Combine(root, "usr", "local", "lib", npmPackage);
            yield break;
        }

        if (context.UserProfile is not null)
        {
            yield return Path.Combine(context.UserProfile, ".local", "bin", "copilot");
        }

        yield return Path.Combine(root, "usr", "local", "bin", "copilot");
        yield return Path.Combine(root, "home", "linuxbrew", ".linuxbrew", "bin", "copilot");
        yield return Path.Combine(root, "usr", "local", "lib", npmPackage);
        yield return Path.Combine(root, "usr", "lib", npmPackage);
        if (context.UserProfile is not null)
        {
            yield return Path.Combine(context.UserProfile, ".npm-global", "lib", npmPackage);
        }
    }

    /// <summary>
    ///     Lists the rooted directories on <c>PATH</c>, in order.
    /// </summary>
    /// <remarks>
    ///     Empty, relative and — on Windows — drive-relative entries (such as <c>C:bin</c>) are skipped,
    ///     so no entry is resolved against the current directory; a root-relative Windows entry such as
    ///     <c>\bin</c> is still searched and resolves on the current drive. Surrounding quotes, which
    ///     Windows allows, are removed.
    /// </remarks>
    /// <param name="context">The search context.</param>
    /// <returns>The directories.</returns>
    private static IEnumerable<string> PathDirectories(CopilotCliSearchContext context) =>
        (context.PathVariable ?? string.Empty)
            .Split(context.PathListSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => entry.Trim('"'))
            .Where(entry => IsSearchableDirectory(entry, context.Platform));

    /// <summary>
    ///     Determines whether a <c>PATH</c> entry is rooted, so that it is not resolved against the
    ///     current directory.
    /// </summary>
    /// <remarks>
    ///     A pure string check keyed on the searched platform, not the host, so it judges every platform
    ///     alike on every host. On Windows an entry is searchable when it starts with a separator (a
    ///     root-relative, UNC or device path) or with a drive letter, a colon and a separator; elsewhere
    ///     when it starts with <c>/</c>. A root-relative Windows entry such as <c>\bin</c> is searched by
    ///     design: it names a folder at the root of the current drive, so the current directory's drive
    ///     decides which folder that is, but no folder below the current directory can supply the CLI.
    /// </remarks>
    /// <param name="entry">The <c>PATH</c> entry, unquoted.</param>
    /// <param name="platform">The platform searched.</param>
    /// <returns><see langword="true"/> when the entry is searched.</returns>
    internal static bool IsSearchableDirectory(string entry, CopilotCliPlatform platform)
    {
        if (entry.Length == 0)
        {
            return false;
        }

        if (platform != CopilotCliPlatform.Windows)
        {
            return entry[0] == '/';
        }

        return entry[0] is '\\' or '/'
            || (entry.Length >= 3 && char.IsAsciiLetter(entry[0]) && entry[1] == ':' && entry[2] is '\\' or '/');
    }

    /// <summary>
    ///     Finds an executable on <c>PATH</c>.
    /// </summary>
    /// <param name="context">The search context.</param>
    /// <param name="name">The executable's name without extension.</param>
    /// <returns>The first match's full path, or <see langword="null"/> when there is none.</returns>
    internal static string? FindOnPath(CopilotCliSearchContext context, string name) =>
        PathDirectories(context)
            .Select(directory => Path.Combine(directory, context.ExecutableName(name)))
            .FirstOrDefault(candidate => IsRunnable(candidate, context));

    /// <summary>
    ///     Determines whether a candidate exists and can be run.
    /// </summary>
    /// <remarks>
    ///     A script is run with <c>node</c>, so it needs only to exist; elsewhere than Windows an
    ///     executable also needs an execute bit.
    /// </remarks>
    /// <param name="candidate">The candidate's full path.</param>
    /// <param name="context">The search context.</param>
    /// <returns><see langword="true"/> when the candidate can be run.</returns>
    private static bool IsRunnable(string candidate, CopilotCliSearchContext context) =>
        context.FileExists(candidate)
        && (context.Platform == CopilotCliPlatform.Windows || IsScript(candidate) || context.IsExecutable(candidate));

    /// <summary>
    ///     Determines whether a candidate is an editor's Copilot launcher rather than a CLI.
    /// </summary>
    /// <remarks>
    ///     A launcher lives in a <c>copilot-shim</c> folder, or is a small file carrying one of the
    ///     launcher's markers. A file that cannot be read is not judged a launcher; the version probe
    ///     then decides.
    /// </remarks>
    /// <param name="candidate">The candidate's full path.</param>
    /// <param name="context">The search context.</param>
    /// <returns><see langword="true"/> when the candidate is a launcher.</returns>
    private static bool IsLauncher(string candidate, CopilotCliSearchContext context)
    {
        var comparison = context.Platform == CopilotCliPlatform.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (candidate.Split(DirectorySeparators).Any(segment => string.Equals(segment, ShimFolderName, comparison)))
        {
            return true;
        }

        try
        {
            if (context.GetLength(candidate) > LauncherScanLimit)
            {
                return false;
            }

            var content = context.ReadAllBytes(candidate).AsSpan();
            return content.IndexOf(RustLauncherMarker) >= 0 || content.IndexOf(ScriptLauncherMarker) >= 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Determines whether a CLI path is a script run with <c>node</c>.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> for a <c>.js</c> file.</returns>
    private static bool IsScript(string path) => path.EndsWith(".js", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Describes a probe's command line for a message.
    /// </summary>
    /// <param name="startInfo">The probe.</param>
    /// <returns>The quoted program and arguments.</returns>
    private static string DescribeCommand(ProcessStartInfo startInfo) =>
        string.Join(' ', new[] { startInfo.FileName }.Concat(startInfo.ArgumentList).Select(part => $"'{part}'"));

    /// <summary>
    ///     Closes a started process's standard input, as a best effort.
    /// </summary>
    /// <param name="process">The process.</param>
    private static void CloseInput(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process exited before its input was closed; nothing is waiting on it.
        }
    }


    /// <summary>
    ///     Matches the CLI's version banner.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"Copilot CLI\s+v?(\d+)\.(\d+)\.(\d+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BannerVersion();

    /// <summary>
    ///     Matches any three-part version.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"(\d+)\.(\d+)\.(\d+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AnyVersion();

    /// <summary>
    ///     Matches the protocol version the SDK's mismatch error reports for the runtime.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"server reports version (\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ServerProtocol();
}

/// <summary>
///     The platform families whose Copilot CLI installations discovery knows.
/// </summary>
internal enum CopilotCliPlatform
{
    /// <summary>Windows: <c>copilot.exe</c>, WinGet and npm.</summary>
    Windows,

    /// <summary>macOS: Homebrew, the install script and npm.</summary>
    MacOS,

    /// <summary>Linux and other Unix-like platforms: the install script, Linuxbrew and npm.</summary>
    Linux,
}

/// <summary>
///     The platform, environment and file system Copilot CLI discovery searches.
/// </summary>
/// <remarks>
///     A seam: production searches the current machine (<see cref="Current"/>); tests pass a fake
///     file system for any platform, so every platform's search order is verified on every platform.
/// </remarks>
/// <param name="Platform">The platform family whose conventions apply.</param>
/// <param name="PathVariable">The <c>PATH</c> value, or <see langword="null"/> when unset.</param>
/// <param name="UserProfile">The user's home folder, or <see langword="null"/> when unknown.</param>
/// <param name="LocalAppData">The local application data folder (Windows), or <see langword="null"/>.</param>
/// <param name="AppData">The roaming application data folder (Windows), or <see langword="null"/>.</param>
/// <param name="FileExists">Reports whether a file exists.</param>
/// <param name="GetDirectories">Lists a folder's subfolders matching a pattern; empty when the folder is missing.</param>
/// <param name="IsExecutable">Reports whether a file has an execute bit (not consulted on Windows).</param>
/// <param name="GetLength">Reads a file's length.</param>
/// <param name="ReadAllBytes">Reads a file's content.</param>
internal sealed record CopilotCliSearchContext(
    CopilotCliPlatform Platform,
    string? PathVariable,
    string? UserProfile,
    string? LocalAppData,
    string? AppData,
    Func<string, bool> FileExists,
    Func<string, string, IEnumerable<string>> GetDirectories,
    Func<string, bool> IsExecutable,
    Func<string, long> GetLength,
    Func<string, byte[]> ReadAllBytes)
{
    /// <summary>
    ///     Gets the comparison file paths are deduplicated with on the platform.
    /// </summary>
    internal StringComparer PathComparer =>
        Platform == CopilotCliPlatform.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    ///     Gets the separator between <c>PATH</c> entries on the platform.
    /// </summary>
    internal char PathListSeparator => Platform == CopilotCliPlatform.Windows ? ';' : ':';

    /// <summary>
    ///     Builds the search context of the current machine.
    /// </summary>
    /// <returns>The context: this platform, this process's <c>PATH</c> and folders, the real file system.</returns>
    internal static CopilotCliSearchContext Current()
    {
        var platform = CopilotCliPlatform.Linux;
        if (OperatingSystem.IsWindows())
        {
            platform = CopilotCliPlatform.Windows;
        }
        else if (OperatingSystem.IsMacOS())
        {
            platform = CopilotCliPlatform.MacOS;
        }

        return new CopilotCliSearchContext(
            platform,
            Environment.GetEnvironmentVariable("PATH"),
            Folder(Environment.SpecialFolder.UserProfile),
            platform == CopilotCliPlatform.Windows ? Folder(Environment.SpecialFolder.LocalApplicationData) : null,
            platform == CopilotCliPlatform.Windows ? Folder(Environment.SpecialFolder.ApplicationData) : null,
            File.Exists,
            ListDirectories,
            HasExecuteBit,
            path => new FileInfo(path).Length,
            File.ReadAllBytes);
    }

    /// <summary>
    ///     Gets an executable's file name on the platform.
    /// </summary>
    /// <param name="name">The name without extension.</param>
    /// <returns>The name with <c>.exe</c> on Windows; unchanged elsewhere.</returns>
    internal string ExecutableName(string name) =>
        Platform == CopilotCliPlatform.Windows ? name + ".exe" : name;

    /// <summary>
    ///     Reads a special folder's path.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The path, or <see langword="null"/> when the platform has none.</returns>
    private static string? Folder(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    /// <summary>
    ///     Lists a folder's subfolders matching a pattern.
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <param name="pattern">The name pattern.</param>
    /// <returns>The subfolders' full paths; empty when the folder is missing or unreadable.</returns>
    private static string[] ListDirectories(string path, string pattern)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path, pattern) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    ///     Reports whether a file has an execute bit for anyone.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns><see langword="true"/> on Windows, or when an execute bit is set.</returns>
    private static bool HasExecuteBit(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & anyExecute) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>
///     Runs a process to completion within a time limit.
/// </summary>
/// <remarks>
///     A seam: production passes <see cref="CopilotCli.RunProcessAsync"/>; tests pass a fake that
///     records the start information and returns a scripted result.
/// </remarks>
/// <param name="startInfo">The process to run.</param>
/// <param name="timeout">How long it may run.</param>
/// <param name="cancellationToken">Cancels the run.</param>
/// <returns>The process's exit code and output.</returns>
internal delegate Task<CopilotCliProcessResult> CopilotCliProcessRunner(
    ProcessStartInfo startInfo,
    TimeSpan timeout,
    CancellationToken cancellationToken);

/// <summary>
///     The outcome of a process run by a <see cref="CopilotCliProcessRunner"/>.
/// </summary>
/// <param name="ExitCode">The exit code; meaningless when <paramref name="TimedOut"/>.</param>
/// <param name="StandardOutput">Everything written to standard output.</param>
/// <param name="StandardError">Everything written to standard error.</param>
/// <param name="TimedOut">Whether the process ran too long and was stopped.</param>
internal sealed record CopilotCliProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);
