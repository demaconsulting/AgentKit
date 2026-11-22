namespace DemaConsulting.AgentKit.Samples.DocumentAssistant.Tests;

/// <summary>
///     Unit tests for the document-assistant sample's exit codes and its handling of a cancellation.
/// </summary>
/// <remarks>
///     Ctrl-C and a provider timeout both reach the entry point as an
///     <see cref="OperationCanceledException"/>; only the user's own token tells them apart. These
///     scenarios pin that a Ctrl-C ends the run silently with success, and a timeout is reported and
///     fails the run.
/// </remarks>
public class ExitCodesTests
{
    /// <summary>
    ///     Proves a cancellation the user asked for with Ctrl-C exits cleanly and prints nothing.
    /// </summary>
    [Fact]
    public async Task ExitCodes_ReportCancellation_CtrlC_ExitsCleanlyAndSilently()
    {
        // Arrange: the user's token is canceled, as Ctrl-C does
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var error = new StringWriter();

        // Act
        var code = await ExitCodes.ReportCancellationAsync(
            new OperationCanceledException(cancellation.Token),
            cancellation.Token,
            error);

        // Assert
        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal(0, code);
        Assert.Empty(error.ToString());
    }

    /// <summary>
    ///     Proves a cancellation nobody asked for - a provider timeout - is reported and fails the run.
    /// </summary>
    [Fact]
    public async Task ExitCodes_ReportCancellation_ProviderTimedOut_ReportsAndExitsNonZero()
    {
        // Arrange: the user's token is untouched, and the HTTP client gave up waiting
        using var cancellation = new CancellationTokenSource();
        using var error = new StringWriter();
        var timeout = new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 120 seconds elapsing.",
            new TimeoutException());

        // Act
        var code = await ExitCodes.ReportCancellationAsync(timeout, cancellation.Token, error);

        // Assert
        Assert.Equal(ExitCodes.Failure, code);
        Assert.NotEqual(0, code);
        Assert.Contains("did not respond in time", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(timeout.Message, error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the exit codes keep their documented values.
    /// </summary>
    [Fact]
    public void ExitCodes_Values_MatchTheDocumentedCodes()
    {
        // Arrange / Act / Assert
        Assert.Equal(0, ExitCodes.Success);
        Assert.Equal(1, ExitCodes.Failure);
        Assert.Equal(2, ExitCodes.Usage);
    }
}
