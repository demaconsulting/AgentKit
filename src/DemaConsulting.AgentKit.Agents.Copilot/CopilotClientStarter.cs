using Microsoft.Copilot;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     Starts a host's <see cref="CopilotClient"/>, turning a runtime that cannot load into an error
///     that says why and what to do.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why this exists.</b> In-process hosting loads the Copilot runtime from the application's
///     output, where <c>Microsoft.Copilot.SDK</c> provisions it at build time — unless the
///     <c>COPILOT_CLI_PATH</c> environment variable is set, in which case the runtime is loaded from
///     beside that path instead. Every shell the Copilot CLI starts sets that variable to the CLI's
///     own installation, which does not ship the in-process runtime library, so an application
///     launched from such a shell fails to start with an error about a missing library that names
///     neither the variable nor the remedy. The other common failure — a host application that does
///     not reference <c>Microsoft.Copilot.SDK</c> itself, so the runtime was never provisioned — is
///     just as opaque. This helper reports the runtime's own error and names <c>COPILOT_CLI_PATH</c>
///     as the cause only when that error names the location the variable points to; otherwise it
///     reports the variable's value without blaming it, or, when the variable is unset, names the
///     missing direct SDK reference as the likely cause of a runtime that was not found.
///     </para>
///     <para>
///     <b>It diagnoses and changes nothing.</b> The host owns the client and chooses its connection;
///     this helper never alters the connection and never changes the process environment, because
///     pointing <c>COPILOT_CLI_PATH</c> at a valid runtime is a supported configuration and changing
///     the environment from a library is a global side effect. It is optional: a host that starts
///     the client itself loses only the diagnosis.
///     </para>
///     <para>
///     Stateless and safe for concurrent use; it reads one environment variable, and only when the
///     start has already failed.
///     </para>
/// </remarks>
public static class CopilotClientStarter
{
    /// <summary>
    ///     The environment variable that redirects in-process hosting to another runtime.
    /// </summary>
    internal const string CliPathVariable = "COPILOT_CLI_PATH";

    /// <summary>
    ///     Starts a Copilot client, explaining a failure to start.
    /// </summary>
    /// <param name="client">The host's client to start. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes when the client has started.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The client failed to start. The message reports the runtime's own error and names
    ///     <c>COPILOT_CLI_PATH</c> as the cause, with its remedies, only when that error names the
    ///     location the variable points to; the original failure is the
    ///     <see cref="Exception.InnerException"/>. The host should report the message; the client is
    ///     left not started, and the host still disposes it.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     The start was canceled. It propagates unchanged, because a cancellation is not a fault to
    ///     explain.
    /// </exception>
    /// <example>
    ///     <para>
    ///     Starting a client the host owns, in Empty mode, which the integration requires.
    ///     </para>
    ///     <code>
    ///     public async Task&lt;CopilotClient&gt; StartClientAsync(string dataDirectory, CancellationToken cancellationToken)
    ///     {
    ///         var client = new CopilotClient(new CopilotClientOptions
    ///         {
    ///             Mode = CopilotClientMode.Empty,
    ///             Runtime = RuntimeConnection.ForInProcess(baseDirectory: dataDirectory),
    ///             GitHub = new ClientGitHubOptions { UseLoggedInUser = true },
    ///         });
    ///
    ///         try
    ///         {
    ///             await CopilotClientStarter.StartAsync(client, cancellationToken);
    ///             return client;
    ///         }
    ///         catch
    ///         {
    ///             await client.DisposeAsync();
    ///             throw;
    ///         }
    ///     }
    ///     </code>
    /// </example>
    public static Task StartAsync(CopilotClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        return StartAsync(client.StartAsync, Environment.GetEnvironmentVariable, cancellationToken);
    }

    /// <summary>
    ///     Runs a start and explains its failure, with the start and the environment supplied.
    /// </summary>
    /// <remarks>
    ///     The test seam: the SDK's client cannot be made to fail on demand without a broken runtime,
    ///     and a test must not change the process environment, so both are injected.
    /// </remarks>
    /// <param name="start">Starts the client.</param>
    /// <param name="getEnvironmentVariable">Reads an environment variable, returning <see langword="null"/> when it is unset.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes when the start has completed.</returns>
    /// <exception cref="InvalidOperationException">The start failed; see <see cref="StartAsync(CopilotClient, CancellationToken)"/>.</exception>
    /// <exception cref="OperationCanceledException">The start was canceled.</exception>
    internal static async Task StartAsync(
        Func<CancellationToken, Task> start,
        Func<string, string?> getEnvironmentVariable,
        CancellationToken cancellationToken)
    {
        try
        {
            await start(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw new InvalidOperationException(Explain(getEnvironmentVariable(CliPathVariable), failure), failure);
        }
    }

    /// <summary>
    ///     Composes the explanation for a client that failed to start.
    /// </summary>
    /// <remarks>
    ///     Every explanation begins with the runtime's own error. The variable is blamed only when the
    ///     failure names the location it points to — the SDK names every path it searched — so a
    ///     failure with another cause, which is common because every Copilot CLI shell sets the
    ///     variable, is not sent after the wrong fix.
    /// </remarks>
    /// <param name="cliPath">The value of <c>COPILOT_CLI_PATH</c>, or <see langword="null"/>/blank when unset.</param>
    /// <param name="failure">The failure the start raised.</param>
    /// <returns>The explanation, carrying the runtime's error and, where it identifies one, the cause and its remedies.</returns>
    internal static string Explain(string? cliPath, Exception failure)
    {
        var opening = "The Copilot client could not start: " + failure.Message + " ";

        if (string.IsNullOrWhiteSpace(cliPath))
        {
            return opening + CliPathVariable + " is not set, so the runtime is loaded from this application's "
                + "output. If the error is that the runtime library was not found, the most common cause is a "
                + "host application that does not reference Microsoft.Copilot.SDK directly: the SDK provisions "
                + "its runtime only into a project that references the package itself, so add a "
                + "PackageReference to Microsoft.Copilot.SDK to the host application. The inner exception "
                + "carries the runtime's own error.";
        }

        if (NamesLocation(failure, cliPath))
        {
            return opening + "The error names the location " + CliPathVariable + " points to. The variable is "
                + "set to '" + cliPath + "', so the runtime is loaded from beside that path instead of from the "
                + "runtime Microsoft.Copilot.SDK provisioned into this application's output; a Copilot CLI "
                + "installation does not ship the in-process runtime library, and every shell the Copilot CLI "
                + "starts sets the variable. To fix it, unset " + CliPathVariable + " for this process, point it "
                + "at a runtime that ships the in-process library, or connect with "
                + "RuntimeConnection.ForSpawnedStdio and an environment that omits it. The inner exception "
                + "carries the runtime's own error.";
        }

        return opening + CliPathVariable + " is set to '" + cliPath + "', but the error does not name that "
            + "location, so the variable is not shown to be the cause; the inner exception carries the "
            + "runtime's own error. If the error is that the in-process runtime library could not be loaded, "
            + "unset " + CliPathVariable + " for this process: a Copilot CLI installation, which every Copilot "
            + "CLI shell points it at, does not ship that library.";
    }

    /// <summary>
    ///     Determines whether a failure names the location <c>COPILOT_CLI_PATH</c> points to.
    /// </summary>
    /// <remarks>
    ///     The location is the variable's value itself or the full-path directory containing it; the
    ///     directory is skipped when it cannot be resolved or is a filesystem root, which would match
    ///     almost any path. Every message in the failure's inner-exception chain is searched, ignoring
    ///     case on Windows, whose paths are case-insensitive, and matching case elsewhere.
    /// </remarks>
    /// <param name="failure">The failure the start raised.</param>
    /// <param name="cliPath">The value of <c>COPILOT_CLI_PATH</c>; not blank.</param>
    /// <returns><see langword="true"/> when a message in the chain contains the value or its directory.</returns>
    internal static bool NamesLocation(Exception failure, string cliPath)
    {
        var needles = new List<string> { cliPath };
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(cliPath));
            if (!string.IsNullOrEmpty(directory) &&
                !string.Equals(directory, Path.GetPathRoot(directory), StringComparison.Ordinal))
            {
                needles.Add(directory);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // The value is not a usable path; only the value itself can be named.
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        for (var current = failure; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (needles.Exists(needle => message.Contains(needle, comparison)))
            {
                return true;
            }
        }

        return false;
    }
}
