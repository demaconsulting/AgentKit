namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for <see cref="CopilotClientStarter"/>: a start that succeeds passes through, a
///     start that fails is explained according to <c>COPILOT_CLI_PATH</c> with the original failure
///     kept, and a cancellation is not explained at all.
/// </summary>
/// <remarks>
///     The start and the environment are injected through the internal overload, so no test starts a
///     runtime or changes the process environment.
/// </remarks>
public class CopilotClientStarterTests
{
    /// <summary>
    ///     Proves a start that succeeds completes without an error and without consulting the
    ///     environment.
    /// </summary>
    [Fact]
    public async Task CopilotClientStarter_Start_Succeeds_DoesNotThrow()
    {
        // Arrange
        var started = false;
        var consulted = false;

        // Act
        await CopilotClientStarter.StartAsync(
            _ =>
            {
                started = true;
                return Task.CompletedTask;
            },
            _ =>
            {
                consulted = true;
                return null;
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(started);
        Assert.False(consulted);
    }

    /// <summary>
    ///     Proves a start that fails with an error naming the location <c>COPILOT_CLI_PATH</c> points
    ///     to is explained by reporting the runtime's error and naming the variable, its value and
    ///     the remedies, with the original failure kept as the cause.
    /// </summary>
    [Fact]
    public async Task CopilotClientStarter_Start_FailsWithCliPathSet_ExplainsTheOverrideAndKeepsTheCause()
    {
        // Arrange: the error the SDK raises when the runtime library is not beside the variable's path
        var cliPath = Path.Combine(Path.GetTempPath(), "copilot-cli", "copilot");
        var directory = Path.GetDirectoryName(cliPath)!;
        var cause = new InvalidOperationException(
            $"FFI runtime library not found. Looked for '{Path.Combine(directory, "copilot_runtime.dll")}'.");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotClientStarter.StartAsync(
            _ => Task.FromException(cause),
            name => name == "COPILOT_CLI_PATH" ? cliPath : null,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(cause, failure.InnerException);
        Assert.Contains(cause.Message, failure.Message, StringComparison.Ordinal);
        Assert.Contains("COPILOT_CLI_PATH", failure.Message, StringComparison.Ordinal);
        Assert.Contains(cliPath, failure.Message, StringComparison.Ordinal);
        Assert.Contains("To fix it", failure.Message, StringComparison.Ordinal);
        Assert.Contains("unset", failure.Message, StringComparison.Ordinal);
        Assert.Contains("ForSpawnedStdio", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a start that fails for another cause while <c>COPILOT_CLI_PATH</c> is set reports
    ///     the runtime's error and the variable's value without blaming the variable, with the
    ///     original failure kept as the cause.
    /// </summary>
    [Fact]
    public async Task CopilotClientStarter_Start_FailsWithCliPathSetForAnotherCause_ReportsTheCauseWithoutBlamingTheVariable()
    {
        // Arrange: an error that does not name the location the variable points to
        var cliPath = Path.Combine(Path.GetTempPath(), "copilot-cli", "copilot");
        var cause = new InvalidOperationException("Authentication failed: no credential.");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotClientStarter.StartAsync(
            _ => Task.FromException(cause),
            name => name == "COPILOT_CLI_PATH" ? cliPath : null,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(cause, failure.InnerException);
        Assert.Contains(cause.Message, failure.Message, StringComparison.Ordinal);
        Assert.Contains("COPILOT_CLI_PATH", failure.Message, StringComparison.Ordinal);
        Assert.Contains(cliPath, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("To fix it", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a start that fails while <c>COPILOT_CLI_PATH</c> is unset is explained by reporting
    ///     the runtime's error and naming the other common cause: a host that does not reference the
    ///     SDK, so no runtime was provisioned.
    /// </summary>
    [Fact]
    public async Task CopilotClientStarter_Start_FailsWithoutCliPath_ExplainsTheMissingRuntime()
    {
        // Arrange
        var cause = new FileNotFoundException("copilot_runtime");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotClientStarter.StartAsync(
            _ => Task.FromException(cause),
            _ => null,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(cause, failure.InnerException);
        Assert.Contains(cause.Message, failure.Message, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Copilot.SDK", failure.Message, StringComparison.Ordinal);
        Assert.Contains("not set", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a canceled start propagates its cancellation unchanged rather than being explained
    ///     as a fault.
    /// </summary>
    [Fact]
    public async Task CopilotClientStarter_Start_Canceled_PropagatesUnchanged()
    {
        // Arrange
        var canceled = new OperationCanceledException("stopped");

        // Act
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => CopilotClientStarter.StartAsync(
            _ => Task.FromException(canceled),
            _ => "ignored",
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(canceled, failure);
    }

    /// <summary>
    ///     Proves a null client is refused.
    /// </summary>
    [Fact]
    public async Task CopilotClientStarter_Start_NullClient_Throws()
    {
        // Act / Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => CopilotClientStarter.StartAsync(null!, TestContext.Current.CancellationToken));
    }
}
