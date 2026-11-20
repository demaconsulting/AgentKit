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
        Assert.Contains("is not shown to be the cause", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("To fix it", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a start that fails with an exception whose message is null is still reported, with
    ///     the original failure kept as the cause, rather than the diagnosis throwing and masking it.
    /// </summary>
    [Fact]
    public async Task CopilotClientStarter_Start_FailsWithNullMessage_ReportsWithoutThrowing()
    {
        // Arrange: a failure whose message is null while the variable is set
        var cliPath = Path.Combine(Path.GetTempPath(), "copilot-cli", "copilot");
        var cause = new NullMessageException();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotClientStarter.StartAsync(
            _ => Task.FromException(cause),
            name => name == "COPILOT_CLI_PATH" ? cliPath : null,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(cause, failure.InnerException);
        Assert.Contains("COPILOT_CLI_PATH", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("To fix it", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a null message in the failure's chain is skipped and the rest of the chain is still
    ///     searched for the location <c>COPILOT_CLI_PATH</c> points to.
    /// </summary>
    [Fact]
    public void CopilotClientStarter_NamesLocation_NullMessageInChain_SearchesTheRest()
    {
        // Arrange: a null-message failure wrapping the error that names the location
        var cliPath = Path.Combine(Path.GetTempPath(), "copilot-cli", "copilot");
        var directory = Path.GetDirectoryName(cliPath)!;
        var inner = new InvalidOperationException(
            $"FFI runtime library not found. Looked for '{Path.Combine(directory, "copilot_runtime.dll")}'.");
        var failure = new NullMessageException(inner);

        // Act
        var names = CopilotClientStarter.NamesLocation(failure, cliPath);

        // Assert
        Assert.True(names);
    }

    /// <summary>
    ///     Proves a bare relative value of <c>COPILOT_CLI_PATH</c> is not itself matched, so an error
    ///     that merely contains the same word does not blame the variable.
    /// </summary>
    [Fact]
    public void CopilotClientStarter_NamesLocation_BareRelativeValue_DoesNotMatchTheBareValue()
    {
        // Arrange: an unrelated error containing the bare value as a word
        var failure = new InvalidOperationException("Copilot authentication failed for copilot.");

        // Act
        var names = CopilotClientStarter.NamesLocation(failure, "copilot");

        // Assert
        Assert.False(names);
    }

    /// <summary>
    ///     Proves a bare relative value of <c>COPILOT_CLI_PATH</c> is matched through the full path the
    ///     SDK resolves it to and prints.
    /// </summary>
    [Fact]
    public void CopilotClientStarter_NamesLocation_BareRelativeValue_MatchesTheFullPathTheSdkSearched()
    {
        // Arrange: the error the SDK raises, naming the full path it resolved the relative value to
        var directory = Path.GetDirectoryName(Path.GetFullPath("copilot"))!;
        var failure = new InvalidOperationException(
            $"FFI runtime library not found. Looked for '{Path.Combine(directory, "copilot_runtime.dll")}'.");

        // Act
        var names = CopilotClientStarter.NamesLocation(failure, "copilot");

        // Assert
        Assert.True(names);
    }

    /// <summary>
    ///     Proves an error naming a sibling directory whose name begins with the variable's directory
    ///     name does not blame the variable.
    /// </summary>
    [Fact]
    public void CopilotClientStarter_NamesLocation_SiblingDirectory_DoesNotMatch()
    {
        // Arrange: the variable points into 'cli'; the error names a file in 'cli2'
        var temp = Path.GetTempPath();
        var cliPath = Path.Combine(temp, "cli", "copilot");
        var failure = new InvalidOperationException(
            $"FFI runtime library not found. Looked for '{Path.Combine(temp, "cli2", "copilot_runtime.dll")}'.");

        // Act
        var names = CopilotClientStarter.NamesLocation(failure, cliPath);

        // Assert
        Assert.False(names);
    }

    /// <summary>
    ///     Proves a variable pointing at a file in a filesystem root is not matched, because the root
    ///     would match almost any path.
    /// </summary>
    [Fact]
    public void CopilotClientStarter_NamesLocation_RootDirectory_IsNotMatched()
    {
        // Arrange: the variable points at a file in the root; the error names another file there
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var cliPath = Path.Combine(root, "copilot");
        var failure = new InvalidOperationException(
            $"FFI runtime library not found. Looked for '{Path.Combine(root, "copilot_runtime.dll")}'.");

        // Act
        var names = CopilotClientStarter.NamesLocation(failure, cliPath);

        // Assert
        Assert.False(names);
    }

    /// <summary>
    ///     Proves the location is found when only an inner exception in the failure's chain names it.
    /// </summary>
    [Fact]
    public void CopilotClientStarter_NamesLocation_MatchOnlyInInnerException_ReturnsTrue()
    {
        // Arrange: a generic outer failure wrapping the error that names the location
        var cliPath = Path.Combine(Path.GetTempPath(), "copilot-cli", "copilot");
        var directory = Path.GetDirectoryName(cliPath)!;
        var inner = new InvalidOperationException(
            $"FFI runtime library not found. Looked for '{Path.Combine(directory, "copilot_runtime.dll")}'.");
        var failure = new InvalidOperationException("Start failed.", inner);

        // Act
        var names = CopilotClientStarter.NamesLocation(failure, cliPath);

        // Assert
        Assert.True(names);
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

    /// <summary>
    ///     An exception whose message is null, as a third-party exception may override it to be.
    /// </summary>
    public sealed class NullMessageException : Exception
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="NullMessageException"/> class.
        /// </summary>
        public NullMessageException()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="NullMessageException"/> class wrapping an inner exception.
        /// </summary>
        /// <param name="innerException">The wrapped exception.</param>
        public NullMessageException(Exception innerException)
            : base(null, innerException)
        {
        }

        /// <inheritdoc/>
        public override string Message => null!;
    }
}
