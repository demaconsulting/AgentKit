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
///     just as opaque. This helper names the likely cause in both cases.
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
    ///     The client failed to start. The message names the likely cause and its remedies, and the
    ///     original failure is the <see cref="Exception.InnerException"/>. The host should report the
    ///     message; the client is left not started, and the host still disposes it.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     The start was canceled. It propagates unchanged, because a cancellation is not a fault to
    ///     explain.
    /// </exception>
    /// <example>
    ///     <para>
    ///     Starting a client the host owns, in the recommended Empty mode.
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
            throw new InvalidOperationException(Explain(getEnvironmentVariable(CliPathVariable)), failure);
        }
    }

    /// <summary>
    ///     Composes the explanation for a client that failed to start.
    /// </summary>
    /// <param name="cliPath">The value of <c>COPILOT_CLI_PATH</c>, or <see langword="null"/>/blank when unset.</param>
    /// <returns>The explanation, naming the likely cause and its remedies.</returns>
    internal static string Explain(string? cliPath)
    {
        if (!string.IsNullOrWhiteSpace(cliPath))
        {
            return "The Copilot client could not start. The " + CliPathVariable + " environment variable is set to '"
                + cliPath + "', so in-process hosting loads the Copilot runtime library from beside that path "
                + "instead of the runtime Microsoft.Copilot.SDK provisioned into this application's output; a "
                + "Copilot CLI installation does not ship that library, and every shell the Copilot CLI starts "
                + "sets the variable. To fix it, unset " + CliPathVariable + " for this process, point it at a "
                + "runtime that ships the in-process library, or connect with RuntimeConnection.ForSpawnedStdio "
                + "and an environment that omits it. The inner exception carries the runtime's own error.";
        }

        return "The Copilot client could not start. " + CliPathVariable + " is not set, so the runtime is loaded "
            + "from this application's output. The most common cause is a host application that does not "
            + "reference Microsoft.Copilot.SDK directly: the SDK provisions its runtime only into a project that "
            + "references the package itself. Add a PackageReference to Microsoft.Copilot.SDK to the host "
            + "application. The inner exception carries the runtime's own error.";
    }
}
