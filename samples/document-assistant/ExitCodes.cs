namespace DemaConsulting.AgentKit.Samples.DocumentAssistant;

/// <summary>
///     The sample's process exit codes, and the decision of what a cancellation means.
/// </summary>
/// <remarks>
///     A cancellation reaches the entry point for one of two reasons: the user pressed Ctrl-C, or a
///     provider did not answer in time — an <see cref="System.Net.Http.HttpClient"/> timeout surfaces as
///     a <see cref="TaskCanceledException"/> although nobody canceled anything. The first is a clean,
///     intentional exit; the second is a failure the user needs to hear about. Only the user's own
///     token tells them apart, so the decision is made here, against that token, where it can be
///     tested.
/// </remarks>
public static class ExitCodes
{
    /// <summary>
    ///     The run succeeded, including a run the user ended with Ctrl-C.
    /// </summary>
    public const int Success = 0;

    /// <summary>
    ///     The run failed: a provider did not respond in time, or an unexpected fault occurred.
    /// </summary>
    public const int Failure = 1;

    /// <summary>
    ///     The command line was not usable.
    /// </summary>
    public const int Usage = 2;

    /// <summary>
    ///     Decides the exit code for a cancellation, and reports one the user did not ask for.
    /// </summary>
    /// <param name="exception">The cancellation that ended the run. Must not be <see langword="null"/>.</param>
    /// <param name="userCancellation">The token Ctrl-C cancels.</param>
    /// <param name="error">Where a failure is reported. Must not be <see langword="null"/>.</param>
    /// <returns>
    ///     <see cref="Success"/>, writing nothing, when the user canceled; otherwise
    ///     <see cref="Failure"/>, after writing the reason to <paramref name="error"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="exception"/> or <paramref name="error"/> is <see langword="null"/>.
    /// </exception>
    public static async Task<int> ReportCancellationAsync(
        OperationCanceledException exception,
        CancellationToken userCancellation,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(error);

        // Ctrl-C: a clean, intentional exit.
        if (userCancellation.IsCancellationRequested)
        {
            return Success;
        }

        // Nobody canceled, so the provider gave up waiting - most often an HTTP client timeout.
        await error.WriteLineAsync($"The provider did not respond in time: {exception.Message}");
        await error.WriteLineAsync("Check that the provider is running and reachable, then try again.");
        return Failure;
    }
}
