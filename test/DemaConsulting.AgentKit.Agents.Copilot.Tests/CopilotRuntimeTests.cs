using System.Collections;
using Microsoft.Copilot;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for <see cref="CopilotRuntime"/> and <see cref="CopilotRuntimeOptions"/>: the client
///     options AgentKit builds are confined for either runtime source, the runtime's environment
///     withholds the host's Copilot configuration variables, the data directory is one AgentKit owns,
///     the runtime executable is resolved and an installed CLI's version checked, a failed or
///     unauthenticated start is explained and releases the client, and disposal is idempotent.
/// </summary>
/// <remarks>
///     No test starts a runtime or changes the process environment: the host environment is passed
///     in, discovery and the version probe run on a fake machine, the start, status reads and
///     disposal are injected through the internal seams, and every data directory is created under
///     the system temporary folder and deleted afterwards.
/// </remarks>
public class CopilotRuntimeTests
{
    /// <summary>
    ///     Proves the client options AgentKit builds force <c>CopilotClientMode.Empty</c>.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildClientOptions_ForcesEmptyMode()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var options = new CopilotRuntimeOptions(directory.Path);

        // Act
        var clientOptions = CopilotRuntime.BuildClientOptions(options, directory.Path, new Hashtable());

        // Assert
        Assert.Equal(CopilotClientMode.Empty, clientOptions.Mode);
    }

    /// <summary>
    ///     Proves the client options for the bundled runtime spawn the runtime the SDK provisioned over
    ///     stdio on the prepared data directory, with no runtime path, arguments or child working
    ///     directory of the host's.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildClientOptions_Bundled_SpawnsTheProvisionedRuntimeOverStdio()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var options = new CopilotRuntimeOptions(directory.Path, runtimeSource: CopilotRuntimeSource.Bundled);

        // Act
        var clientOptions = CopilotRuntime.BuildClientOptions(options, directory.Path, new Hashtable(), cliPath: null);

        // Assert
        var connection = Assert.IsType<SpawnedStdioRuntimeConnection>(clientOptions.Runtime);
        Assert.Null(connection.Path);
        Assert.Null(connection.Args);
        Assert.Null(connection.WorkingDirectory);
        Assert.Equal(directory.Path, connection.BaseDirectory);
        Assert.NotNull(connection.Environment);
    }

    /// <summary>
    ///     Proves the client options for the installed CLI spawn exactly the resolved CLI over stdio,
    ///     with the same mode, data directory and environment as the bundled runtime and no runtime
    ///     arguments or child working directory.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildClientOptions_InstalledCli_SpawnsTheResolvedCliOverStdio()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var cli = Path.Combine(directory.Path, "copilot.exe");
        var host = new Hashtable { ["COPILOT_CLI_PATH"] = "/elsewhere/copilot", ["PATH"] = "/usr/bin" };

        // Act
        var installed = CopilotRuntime.BuildClientOptions(new CopilotRuntimeOptions(directory.Path), directory.Path, host, cli);
        var bundled = CopilotRuntime.BuildClientOptions(
            new CopilotRuntimeOptions(directory.Path, runtimeSource: CopilotRuntimeSource.Bundled),
            directory.Path,
            host);

        // Assert
        var connection = Assert.IsType<SpawnedStdioRuntimeConnection>(installed.Runtime);
        var bundledConnection = Assert.IsType<SpawnedStdioRuntimeConnection>(bundled.Runtime);
        Assert.Equal(cli, connection.Path);
        Assert.Null(connection.Args);
        Assert.Null(connection.WorkingDirectory);
        Assert.Equal(CopilotClientMode.Empty, installed.Mode);
        Assert.Equal(bundled.Mode, installed.Mode);
        Assert.Equal(bundledConnection.BaseDirectory, connection.BaseDirectory);
        Assert.Equal(bundledConnection.Environment!.OrderBy(entry => entry.Key, StringComparer.Ordinal), connection.Environment!.OrderBy(entry => entry.Key, StringComparer.Ordinal));
    }

    /// <summary>
    ///     Proves the client options leave unset every client-level channel through which capability
    ///     could be injected or the runtime redirected.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildClientOptions_LeavesEveryCapabilityChannelUnset()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var options = new CopilotRuntimeOptions(directory.Path, "token", CopilotLogLevel.Debug, NullLogger.Instance);

        // Act
        var clientOptions = CopilotRuntime.BuildClientOptions(options, directory.Path, new Hashtable());

        // Assert
        Assert.Empty(clientOptions.Plugins.BuiltinDirectories ?? []);
        Assert.Null(clientOptions.SessionFs);
        Assert.Null(clientOptions.InternalTestOptions);
        Assert.Null(clientOptions.Extensions.LaunchProvider);
        Assert.Null(clientOptions.Installations.ConfirmationHandler);
        Assert.Null(clientOptions.Models.RequestHandler);
        Assert.Null(clientOptions.Models.OnListModels);
        Assert.Null(clientOptions.Observability.Telemetry);
        Assert.Null(clientOptions.Network);
        Assert.Null(clientOptions.Entra);
        Assert.Null(clientOptions.GitHub.OnTelemetry);
    }

    /// <summary>
    ///     Proves the neutral options a host may set — token, log level and logger — reach the client
    ///     options, the logger only through the redacting logger, and no logger as none.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildClientOptions_CarriesTokenLogLevelAndLogger()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var logger = new CapturingLogger();
        var options = new CopilotRuntimeOptions(directory.Path, "gho_token", CopilotLogLevel.Warning, logger);

        // Act
        var clientOptions = CopilotRuntime.BuildClientOptions(options, directory.Path, new Hashtable());
        var withoutLogger = CopilotRuntime.BuildClientOptions(new CopilotRuntimeOptions(directory.Path), directory.Path, new Hashtable());
        clientOptions.Observability.Logger!.Log(LogLevel.Information, new EventId(1, "Sdk"), "started", null, (state, _) => state);

        // Assert
        Assert.Equal("gho_token", clientOptions.GitHub.Token);
        Assert.Equal(CopilotLogLevel.Warning, clientOptions.Observability.LogLevel);
        Assert.IsType<CopilotRedactingLogger>(clientOptions.Observability.Logger);
        Assert.Equal("started", Assert.Single(logger.Entries).Message);
        Assert.Null(withoutLogger.Observability.Logger);
    }

    /// <summary>
    ///     Proves every entry the SDK writes to the host's logger arrives redacted — its message, its
    ///     structured values, its scope and its exception, whose data and an aggregate's second inner
    ///     exception quote the secret — with the environment's secrets known when no sign-in is given,
    ///     and with a session token the sign-in learns after the client was configured.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildClientOptions_Logger_RedactsTheSdksEntries()
    {
        // Arrange: one client without a sign-in, whose environment holds the secret, and one whose
        // sign-in knows the secret and learns a session token after the client was configured
        const string secret = "s3cr3t-value-xyz";
        const string sessionToken = "s3cr3t-session-abc";
        using var directory = new TemporaryDirectory();
        var environmentLog = new CapturingLogger();
        var planLog = new CapturingLogger();
        var plan = CopilotSignInTests.PlanKnowing(secret);
        var fromEnvironment = CopilotRuntime.BuildClientOptions(
            new CopilotRuntimeOptions(directory.Path, logger: environmentLog),
            directory.Path,
            new Hashtable { ["GITHUB_ENTERPRISE_TOKEN"] = secret });
        var fromPlan = CopilotRuntime.BuildClientOptions(
            new CopilotRuntimeOptions(directory.Path, logger: planLog),
            directory.Path,
            new Hashtable(),
            signIn: plan);
        var failure = new InvalidOperationException(
            "CopilotClient.StartAsync failed",
            new AggregateException("both failed", new IOException("first"), new IOException($"second {secret} {sessionToken}")));
        failure.Data["token"] = secret;

        // Act: entries in the SDK's shape, under a scope naming the CLI's path
        plan.Secrets.Add(sessionToken);
        foreach (var sdkLogger in new[] { fromEnvironment.Observability.Logger!, fromPlan.Observability.Logger! })
        {
            using (sdkLogger.BeginScope(new Dictionary<string, object?> { ["CliPath"] = $"/opt/{secret}/{sessionToken}/copilot" }))
            {
                sdkLogger.Log(
                    LogLevel.Error,
                    new EventId(5, "StartFailed"),
                    new Dictionary<string, object?> { ["Error"] = $"stderr: {secret} {sessionToken}", ["Elapsed"] = 12 },
                    failure,
                    (_, _) => $"CopilotClient.StartAsync failed: {secret} {sessionToken}");
            }
        }

        // Assert
        var environmentEntry = Assert.Single(environmentLog.Entries);
        var planEntry = Assert.Single(planLog.Entries);
        Assert.Equal("CopilotClient.StartAsync failed: <redacted> s3cr3t-session-abc", environmentEntry.Message);
        Assert.Equal("CopilotClient.StartAsync failed: <redacted> <redacted>", planEntry.Message);
        Assert.Contains(planEntry.State, pair => pair.Key == "Elapsed" && Equals(pair.Value, 12));
        Assert.Contains(Assert.Single(planLog.Scopes).State, pair => pair.Key == "CliPath");
        Assert.IsType<CopilotRedactedException>(planEntry.Exception);
        SecretAssert.NoSecret(environmentLog, secret);
        SecretAssert.NoSecret(planLog, secret, sessionToken);
    }

    /// <summary>
    ///     Proves the runtime's environment omits every <c>COPILOT_</c> and <c>GITHUB_COPILOT_</c>
    ///     configuration variable of the host's, including a runtime redirection, an approve-all
    ///     switch, an instructions directory and an injected MCP configuration.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildRuntimeEnvironment_RemovesCopilotConfigurationVariables()
    {
        // Arrange: a host environment carrying Copilot configuration
        var host = new Hashtable
        {
            ["COPILOT_CLI_PATH"] = "/opt/copilot/copilot",
            ["COPILOT_ALLOW_ALL"] = "1",
            ["COPILOT_CUSTOM_INSTRUCTIONS_DIRS"] = "/shared/instructions",
            ["GITHUB_COPILOT_MCP_JSON_FROM_INPUT"] = "{}",
            ["PATH"] = "/usr/bin",
        };

        // Act
        var environment = CopilotRuntime.BuildRuntimeEnvironment(host);

        // Assert
        Assert.DoesNotContain("COPILOT_CLI_PATH", environment.Keys);
        Assert.DoesNotContain("COPILOT_ALLOW_ALL", environment.Keys);
        Assert.DoesNotContain("COPILOT_CUSTOM_INSTRUCTIONS_DIRS", environment.Keys);
        Assert.DoesNotContain("GITHUB_COPILOT_MCP_JSON_FROM_INPUT", environment.Keys);
        Assert.Equal("/usr/bin", environment["PATH"]);
    }

    /// <summary>
    ///     Proves a Copilot configuration variable written in mixed case is withheld on Windows, whose
    ///     environment variable names are case-insensitive.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildRuntimeEnvironment_MixedCaseOnWindows_IsRemoved()
    {
        // Arrange
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Environment variable names are case-insensitive only on Windows.");
        var host = new Hashtable { ["Copilot_Cli_Path"] = "C:\\copilot\\copilot.exe", ["github_copilot_x"] = "1" };

        // Act
        var environment = CopilotRuntime.BuildRuntimeEnvironment(host);

        // Assert
        Assert.Empty(environment);
    }

    /// <summary>
    ///     Proves every other host variable, and the documented pass-through set of credential,
    ///     network and telemetry-off variables, reaches the runtime unchanged.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildRuntimeEnvironment_KeepsHostVariablesAndThePassThroughSet()
    {
        // Arrange
        var host = new Hashtable
        {
            ["PATH"] = "/usr/bin",
            ["GH_TOKEN"] = "gh",
            ["GITHUB_TOKEN"] = "github",
            ["HTTPS_PROXY"] = "http://proxy:8080",
        };
        foreach (var name in CopilotRuntime.PassThroughVariables)
        {
            host[name] = $"value-of-{name}";
        }

        // Act
        var environment = CopilotRuntime.BuildRuntimeEnvironment(host);

        // Assert
        Assert.Equal("/usr/bin", environment["PATH"]);
        Assert.Equal("gh", environment["GH_TOKEN"]);
        Assert.Equal("github", environment["GITHUB_TOKEN"]);
        Assert.Equal("http://proxy:8080", environment["HTTPS_PROXY"]);
        Assert.All(CopilotRuntime.PassThroughVariables, name => Assert.Equal($"value-of-{name}", environment[name]));
    }

    /// <summary>
    ///     Proves the pass-through set is exactly the documented credential, network and
    ///     telemetry-off variables, so a widening of it is a visible change.
    /// </summary>
    [Fact]
    public void CopilotRuntime_PassThroughVariables_AreTheDocumentedSet()
    {
        // Arrange
        string[] documented =
        [
            "COPILOT_GH_HOST",
            "COPILOT_GITHUB_TELEMETRY_DISABLED",
            "COPILOT_GITHUB_TOKEN",
            "COPILOT_PROXY_KERBEROS",
            "COPILOT_PROXY_KERBEROS_SPN",
            "COPILOT_SYSTEM_PROXY",
            "COPILOT_TELEMETRY_DISABLED",
        ];

        // Act
        var passThrough = CopilotRuntime.PassThroughVariables.Order(StringComparer.Ordinal).ToArray();

        // Assert
        Assert.Equal(documented, passThrough);
    }

    /// <summary>
    ///     Proves the client options carry the withheld environment rather than inheriting the host's.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildClientOptions_CarriesTheWithheldEnvironment()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var host = new Hashtable { ["COPILOT_CLI_PATH"] = "/elsewhere/copilot", ["PATH"] = "/usr/bin" };

        // Act
        var clientOptions = CopilotRuntime.BuildClientOptions(new CopilotRuntimeOptions(directory.Path), directory.Path, host);

        // Assert
        var connection = Assert.IsType<SpawnedStdioRuntimeConnection>(clientOptions.Runtime);
        Assert.NotNull(connection.Environment);
        Assert.False(connection.Environment.ContainsKey("COPILOT_CLI_PATH"));
        Assert.Equal("/usr/bin", connection.Environment["PATH"]);
    }

    /// <summary>
    ///     Proves a missing data directory is created, with its parents, and marked as AgentKit's.
    /// </summary>
    [Fact]
    public void CopilotRuntime_PrepareDataDirectory_Missing_IsCreatedAndMarked()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var dataDirectory = Path.Combine(directory.Path, "nested", "copilot-runtime");

        // Act
        var prepared = CopilotRuntime.PrepareDataDirectory(dataDirectory);

        // Assert
        Assert.Equal(Path.GetFullPath(dataDirectory), prepared);
        Assert.True(Directory.Exists(prepared));
        Assert.Equal(ExpectedMarker, File.ReadAllBytes(Path.Combine(prepared, CopilotRuntime.OwnershipMarkerFileName)));
    }

    /// <summary>
    ///     Proves an existing empty data directory is accepted and marked as AgentKit's.
    /// </summary>
    [Fact]
    public void CopilotRuntime_PrepareDataDirectory_Empty_IsMarked()
    {
        // Arrange
        using var directory = new TemporaryDirectory();

        // Act
        var prepared = CopilotRuntime.PrepareDataDirectory(directory.Path);

        // Assert
        Assert.Equal(ExpectedMarker, File.ReadAllBytes(Path.Combine(prepared, CopilotRuntime.OwnershipMarkerFileName)));
    }

    /// <summary>
    ///     Proves the marker AgentKit writes is the versioned format-1 marker, byte for byte: UTF-8
    ///     without a byte order mark, LF line endings, a signature and format version on its first
    ///     line, and a final line feed.
    /// </summary>
    [Fact]
    public void CopilotRuntime_PrepareDataDirectory_Fresh_WritesExactlyTheVersionedMarker()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var dataDirectory = Path.Combine(directory.Path, "copilot-runtime");

        // Act
        var prepared = CopilotRuntime.PrepareDataDirectory(dataDirectory);

        // Assert
        var written = File.ReadAllBytes(Path.Combine(prepared, CopilotRuntime.OwnershipMarkerFileName));
        Assert.Equal(ExpectedMarker, written);
        Assert.Equal(ExpectedMarker, CopilotRuntime.OwnershipMarkerContent.ToArray());
        Assert.NotEqual(0xEF, written[0]);
        Assert.DoesNotContain((byte)'\r', written);
        var text = System.Text.Encoding.UTF8.GetString(written);
        Assert.StartsWith("AgentKit Copilot runtime data directory, marker format 1\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a data directory whose marker is a regular file holding anything other than exactly
    ///     the marker AgentKit writes is refused as carrying an invalid marker, and the marker is left
    ///     as it was rather than overwritten.
    /// </summary>
    /// <param name="variant">The kind of content the marker holds.</param>
    [Theory]
    [InlineData("empty")]
    [InlineData("byte-order-mark")]
    [InlineData("crlf")]
    [InlineData("extra-line-feed")]
    [InlineData("no-final-line-feed")]
    [InlineData("earlier-unversioned")]
    public void CopilotRuntime_PrepareDataDirectory_MarkerWithOtherContent_IsRefusedAsInvalid(string variant)
    {
        // Arrange: a data directory whose marker holds content AgentKit does not write
        using var directory = new TemporaryDirectory();
        var marker = Path.Combine(directory.Path, CopilotRuntime.OwnershipMarkerFileName);
        var content = MarkerVariant(variant);
        File.WriteAllBytes(marker, content);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => CopilotRuntime.PrepareDataDirectory(directory.Path));

        // Assert
        Assert.Contains(directory.Path, failure.Message, StringComparison.Ordinal);
        Assert.Contains(InvalidMarkerPhrase, failure.Message, StringComparison.Ordinal);
        Assert.Equal(content, File.ReadAllBytes(marker));
    }

    /// <summary>
    ///     Proves a data directory whose marker begins with the expected bytes but is far larger is
    ///     refused as carrying an invalid marker.
    /// </summary>
    [Fact]
    public void CopilotRuntime_PrepareDataDirectory_MarkerOversized_IsRefusedAsInvalid()
    {
        // Arrange: the expected marker followed by a megabyte (1 MiB) of padding
        using var directory = new TemporaryDirectory();
        var marker = Path.Combine(directory.Path, CopilotRuntime.OwnershipMarkerFileName);
        File.WriteAllBytes(marker, [.. ExpectedMarker, .. new byte[1024 * 1024]]);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => CopilotRuntime.PrepareDataDirectory(directory.Path));

        // Assert
        Assert.Contains(directory.Path, failure.Message, StringComparison.Ordinal);
        Assert.Contains(InvalidMarkerPhrase, failure.Message, StringComparison.Ordinal);
        Assert.Contains("size", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a data directory whose marker entry is a directory is refused as carrying an invalid
    ///     marker, not reported as a directory without one.
    /// </summary>
    [Fact]
    public void CopilotRuntime_PrepareDataDirectory_MarkerIsADirectory_IsRefusedAsInvalid()
    {
        // Arrange: a directory named like the marker, with a file in it
        using var directory = new TemporaryDirectory();
        var marker = Path.Combine(directory.Path, CopilotRuntime.OwnershipMarkerFileName);
        Directory.CreateDirectory(marker);
        File.WriteAllBytes(Path.Combine(marker, "content"), ExpectedMarker);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => CopilotRuntime.PrepareDataDirectory(directory.Path));

        // Assert
        Assert.Contains(directory.Path, failure.Message, StringComparison.Ordinal);
        Assert.Contains(InvalidMarkerPhrase, failure.Message, StringComparison.Ordinal);
        Assert.Contains("is a directory", failure.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(marker));
    }

    /// <summary>
    ///     Proves a data directory whose marker is a symbolic link is refused as carrying an invalid
    ///     marker, even when the link's target holds exactly the marker AgentKit writes.
    /// </summary>
    /// <remarks>
    ///     Skipped on a Windows session that lacks the privilege to create symbolic links; it runs on
    ///     Linux, macOS and elevated or Developer Mode Windows.
    /// </remarks>
    [Fact]
    public void CopilotRuntime_PrepareDataDirectory_MarkerIsASymbolicLink_IsRefusedAsInvalid()
    {
        // Arrange: a marker that links to a valid marker outside the data directory
        using var directory = new TemporaryDirectory();
        using var elsewhere = new TemporaryDirectory();
        var target = Path.Combine(elsewhere.Path, "marker");
        File.WriteAllBytes(target, ExpectedMarker);
        var marker = Path.Combine(directory.Path, CopilotRuntime.OwnershipMarkerFileName);
        try
        {
            File.CreateSymbolicLink(marker, target);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("Creating a symbolic link needs SeCreateSymbolicLinkPrivilege, which this Windows session lacks.");
        }

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => CopilotRuntime.PrepareDataDirectory(directory.Path));

        // Assert
        Assert.Contains(directory.Path, failure.Message, StringComparison.Ordinal);
        Assert.Contains(InvalidMarkerPhrase, failure.Message, StringComparison.Ordinal);
        Assert.Contains("symbolic link", failure.Message, StringComparison.Ordinal);
        Assert.Equal(ExpectedMarker, File.ReadAllBytes(target));
    }

    /// <summary>
    ///     Proves that when another writer has already written a valid marker by the time AgentKit
    ///     marks the directory, the existing marker is accepted and not overwritten.
    /// </summary>
    [Fact]
    public void CopilotRuntime_MarkDataDirectory_MarkerWrittenConcurrentlyValid_IsAcceptedNotOverwritten()
    {
        // Arrange: a racing writer already wrote the marker
        using var directory = new TemporaryDirectory();
        var marker = Path.Combine(directory.Path, CopilotRuntime.OwnershipMarkerFileName);
        File.WriteAllBytes(marker, ExpectedMarker);
        var writtenAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(marker, writtenAt);

        // Act
        CopilotRuntime.MarkDataDirectory(directory.Path);

        // Assert
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(marker));
        Assert.Equal(ExpectedMarker, File.ReadAllBytes(marker));
    }

    /// <summary>
    ///     Proves that when another writer has left a partial marker by the time AgentKit marks the
    ///     directory, the directory is refused as carrying an invalid marker and the partial marker is
    ///     left as it was.
    /// </summary>
    [Fact]
    public void CopilotRuntime_MarkDataDirectory_MarkerWrittenConcurrentlyInvalid_IsRefusedNotOverwritten()
    {
        // Arrange: a racing writer left half a marker
        using var directory = new TemporaryDirectory();
        var marker = Path.Combine(directory.Path, CopilotRuntime.OwnershipMarkerFileName);
        var partial = ExpectedMarker[..(ExpectedMarker.Length / 2)];
        File.WriteAllBytes(marker, partial);

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => CopilotRuntime.MarkDataDirectory(directory.Path));

        // Assert
        Assert.Contains(directory.Path, failure.Message, StringComparison.Ordinal);
        Assert.Contains(InvalidMarkerPhrase, failure.Message, StringComparison.Ordinal);
        Assert.Equal(partial, File.ReadAllBytes(marker));
    }

    /// <summary>
    ///     Proves that a missing data directory another writer populates after AgentKit created it and
    ///     before AgentKit marks it is refused as not created by AgentKit, left without a marker and
    ///     with its content untouched, and refused again on the next start.
    /// </summary>
    [Fact]
    public void CopilotRuntime_PrepareDataDirectory_MissingPopulatedBeforeMarking_IsRefusedAndLeftUnmarked()
    {
        // Arrange: a writer drops Copilot configuration into the directory just created
        using var directory = new TemporaryDirectory();
        var dataDirectory = Path.Combine(directory.Path, "nested", "copilot-runtime");
        var configuration = Path.Combine(dataDirectory, "mcp-config.json");
        const string content = "{\"mcpServers\":{}}";

        // Act
        var failure = Assert.Throws<InvalidOperationException>(
            () => CopilotRuntime.PrepareDataDirectory(dataDirectory, path => File.WriteAllText(Path.Combine(path, "mcp-config.json"), content)));

        // Assert
        Assert.Contains(dataDirectory, failure.Message, StringComparison.Ordinal);
        Assert.Contains("not created by AgentKit", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(InvalidMarkerPhrase, failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(dataDirectory, CopilotRuntime.OwnershipMarkerFileName)));
        Assert.Equal(content, File.ReadAllText(configuration));
        var nextStart = Assert.Throws<InvalidOperationException>(() => CopilotRuntime.PrepareDataDirectory(dataDirectory));
        Assert.Contains("not created by AgentKit", nextStart.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that an empty data directory another writer populates - here with a subdirectory -
    ///     after AgentKit found it empty and before AgentKit marks it is refused as not created by
    ///     AgentKit, left without a marker and with its content untouched, and refused again on the
    ///     next start.
    /// </summary>
    [Fact]
    public void CopilotRuntime_PrepareDataDirectory_EmptyPopulatedBeforeMarking_IsRefusedAndLeftUnmarked()
    {
        // Arrange: a writer creates a subdirectory with a file in the directory found empty
        using var directory = new TemporaryDirectory();
        var state = Path.Combine(directory.Path, "session-state", "state.json");
        const string content = "{}";

        // Act
        var failure = Assert.Throws<InvalidOperationException>(
            () => CopilotRuntime.PrepareDataDirectory(directory.Path, path =>
            {
                Directory.CreateDirectory(Path.Combine(path, "session-state"));
                File.WriteAllText(Path.Combine(path, "session-state", "state.json"), content);
            }));

        // Assert
        Assert.Contains(directory.Path, failure.Message, StringComparison.Ordinal);
        Assert.Contains("not created by AgentKit", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(InvalidMarkerPhrase, failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(directory.Path, CopilotRuntime.OwnershipMarkerFileName)));
        Assert.Equal(content, File.ReadAllText(state));
        var nextStart = Assert.Throws<InvalidOperationException>(() => CopilotRuntime.PrepareDataDirectory(directory.Path));
        Assert.Contains("not created by AgentKit", nextStart.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a valid marker another writer created first is the reuse path: accepted as it
    ///     is, with content beside it, without being overwritten or the directory re-checked.
    /// </summary>
    [Fact]
    public void CopilotRuntime_MarkDataDirectory_MarkerWrittenConcurrentlyValidWithContent_IsAccepted()
    {
        // Arrange: a racing AgentKit writer already marked the directory and its runtime wrote to it
        using var directory = new TemporaryDirectory();
        var marker = Path.Combine(directory.Path, CopilotRuntime.OwnershipMarkerFileName);
        File.WriteAllBytes(marker, ExpectedMarker);
        var writtenAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(marker, writtenAt);
        var state = Path.Combine(directory.Path, "session-state", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(state)!);
        File.WriteAllText(state, "{}");

        // Act
        CopilotRuntime.MarkDataDirectory(directory.Path);

        // Assert
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(marker));
        Assert.Equal(ExpectedMarker, File.ReadAllBytes(marker));
        Assert.Equal("{}", File.ReadAllText(state));
    }

    /// <summary>
    ///     Proves that when the re-enumeration after writing the marker fails and the delete of the
    ///     marker fails too, the failure propagates and the marker left behind is invalid, so the
    ///     next start refuses the directory rather than trusting it.
    /// </summary>
    [Fact]
    public void CopilotRuntime_MarkDataDirectory_ReEnumerationAndDeleteFail_LeavesInvalidMarker()
    {
        // Arrange: an empty data directory whose re-enumeration and marker delete will both fail
        using var directory = new TemporaryDirectory();
        var marker = Path.Combine(directory.Path, CopilotRuntime.OwnershipMarkerFileName);
        const string message = "enumeration failed";

        // Act
        var failure = Assert.Throws<IOException>(
            () => CopilotRuntime.MarkDataDirectory(directory.Path, _ => throw new IOException(message), _ => { }));

        // Assert
        Assert.Equal(message, failure.Message);
        Assert.True(File.Exists(marker));
        Assert.Equal(0, new FileInfo(marker).Length);
        var nextStart = Assert.Throws<InvalidOperationException>(() => CopilotRuntime.PrepareDataDirectory(directory.Path));
        Assert.Contains(directory.Path, nextStart.Message, StringComparison.Ordinal);
        Assert.Contains(InvalidMarkerPhrase, nextStart.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a data directory AgentKit marked earlier is accepted with the runtime's own content
    ///     in it, so conversations persist across restarts.
    /// </summary>
    [Fact]
    public void CopilotRuntime_PrepareDataDirectory_Marked_IsAcceptedWithRuntimeContent()
    {
        // Arrange: a directory AgentKit prepared, which the runtime has since written to
        using var directory = new TemporaryDirectory();
        CopilotRuntime.PrepareDataDirectory(directory.Path);
        Directory.CreateDirectory(Path.Combine(directory.Path, "session-state"));
        File.WriteAllText(Path.Combine(directory.Path, "session-state", "state.json"), "{}");

        // Act
        var prepared = CopilotRuntime.PrepareDataDirectory(directory.Path);

        // Assert
        Assert.Equal(directory.Path, prepared);
        Assert.True(File.Exists(Path.Combine(directory.Path, "session-state", "state.json")));
    }

    /// <summary>
    ///     Proves a non-empty data directory AgentKit did not create — such as a Copilot CLI home that
    ///     configures an MCP server — is refused, naming the directory, and left unmarked.
    /// </summary>
    [Fact]
    public void CopilotRuntime_PrepareDataDirectory_UnmarkedNonEmpty_IsRefused()
    {
        // Arrange: a foreign data directory
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "mcp-config.json"), "{\"mcpServers\":{}}");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => CopilotRuntime.PrepareDataDirectory(directory.Path));

        // Assert
        Assert.Contains(directory.Path, failure.Message, StringComparison.Ordinal);
        Assert.Contains("not created by AgentKit", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(InvalidMarkerPhrase, failure.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(directory.Path, CopilotRuntime.OwnershipMarkerFileName)));
    }

    /// <summary>
    ///     Proves a data directory path that names an existing file is refused.
    /// </summary>
    [Fact]
    public void CopilotRuntime_PrepareDataDirectory_PathIsAFile_IsRefused()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var file = Path.Combine(directory.Path, "not-a-directory");
        File.WriteAllText(file, "x");

        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => CopilotRuntime.PrepareDataDirectory(file));

        // Assert
        Assert.Contains(file, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing or blank data directory is refused by the options.
    /// </summary>
    [Fact]
    public void CopilotRuntimeOptions_Constructor_BlankDataDirectory_Throws()
    {
        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => new CopilotRuntimeOptions(null!));
        Assert.Throws<ArgumentException>(() => new CopilotRuntimeOptions(" "));
    }

    /// <summary>
    ///     Proves a data directory that is not a valid path is refused naming the parameter, with the
    ///     platform's own failure kept as the cause.
    /// </summary>
    [Fact]
    public void CopilotRuntimeOptions_Constructor_InvalidDataDirectory_ThrowsNamingTheParameter()
    {
        // Act
        var failure = Assert.Throws<ArgumentException>(() => new CopilotRuntimeOptions("copilot\0runtime"));

        // Assert
        Assert.Equal("dataDirectory", failure.ParamName);
        Assert.IsType<ArgumentException>(failure.InnerException);
    }

    /// <summary>
    ///     Proves a relative data directory is resolved to a full path when the options are built.
    /// </summary>
    [Fact]
    public void CopilotRuntimeOptions_Constructor_RelativeDataDirectory_IsResolvedToAFullPath()
    {
        // Act
        var options = new CopilotRuntimeOptions("copilot-runtime");

        // Assert
        Assert.Equal(Path.GetFullPath("copilot-runtime"), options.DataDirectory);
    }

    /// <summary>
    ///     Proves a blank token is treated as none, so the logged-in user is used.
    /// </summary>
    [Fact]
    public void CopilotRuntimeOptions_Constructor_BlankToken_IsNull()
    {
        // Act
        var options = new CopilotRuntimeOptions("copilot-runtime", gitHubToken: " ");

        // Assert
        Assert.Null(options.GitHubToken);
    }

    /// <summary>
    ///     Proves the options default to the installed Copilot CLI, found by discovery.
    /// </summary>
    [Fact]
    public void CopilotRuntimeOptions_Constructor_Defaults_InstalledCliWithNoPath()
    {
        // Act
        var options = new CopilotRuntimeOptions("copilot-runtime");

        // Assert
        Assert.Equal(CopilotRuntimeSource.InstalledCli, options.RuntimeSource);
        Assert.Null(options.CliPath);
    }

    /// <summary>
    ///     Proves a fully qualified CLI path is kept, normalized, for the installed CLI.
    /// </summary>
    [Fact]
    public void CopilotRuntimeOptions_Constructor_FullyQualifiedCliPath_IsKept()
    {
        // Arrange
        var cli = Path.Combine(Path.GetTempPath(), "tools", "..", "copilot", "copilot.exe");

        // Act
        var options = new CopilotRuntimeOptions("copilot-runtime", cliPath: cli);

        // Assert
        Assert.Equal(Path.GetFullPath(cli), options.CliPath);
        Assert.Equal(CopilotRuntimeSource.InstalledCli, options.RuntimeSource);
    }

    /// <summary>
    ///     Proves a CLI path together with the bundled runtime is refused naming the parameter.
    /// </summary>
    [Fact]
    public void CopilotRuntimeOptions_Constructor_CliPathWithBundled_Throws()
    {
        // Arrange
        var cli = Path.Combine(Path.GetTempPath(), "copilot.exe");

        // Act
        var failure = Assert.Throws<ArgumentException>(
            () => new CopilotRuntimeOptions("copilot-runtime", runtimeSource: CopilotRuntimeSource.Bundled, cliPath: cli));

        // Assert
        Assert.Equal("cliPath", failure.ParamName);
        Assert.Contains("Bundled", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a CLI path that is not fully qualified is refused, so the executable never depends on
    ///     the current directory.
    /// </summary>
    [Fact]
    public void CopilotRuntimeOptions_Constructor_RelativeCliPath_Throws()
    {
        // Act
        var failure = Assert.Throws<ArgumentException>(
            () => new CopilotRuntimeOptions("copilot-runtime", cliPath: Path.Combine("tools", "copilot")));

        // Assert
        Assert.Equal("cliPath", failure.ParamName);
        Assert.Contains("fully qualified", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a CLI path naming a batch file is refused, in any case, because Windows would run it
    ///     through <c>cmd.exe</c>, and the refusal points to <c>copilot.exe</c> or <c>npm-loader.js</c>.
    /// </summary>
    /// <param name="fileName">The batch file's name.</param>
    [Theory]
    [InlineData("copilot.cmd")]
    [InlineData("copilot.CMD")]
    [InlineData("copilot.bat")]
    public void CopilotRuntimeOptions_Constructor_BatchFileCliPath_Throws(string fileName)
    {
        // Arrange
        var cli = Path.Combine(Path.GetTempPath(), fileName);

        // Act
        var failure = Assert.Throws<ArgumentException>(() => new CopilotRuntimeOptions("copilot-runtime", cliPath: cli));

        // Assert
        Assert.Equal("cliPath", failure.ParamName);
        Assert.Contains("batch file", failure.Message, StringComparison.Ordinal);
        Assert.Contains("copilot.exe", failure.Message, StringComparison.Ordinal);
        Assert.Contains("npm-loader.js", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a runtime source that is not a defined value is refused.
    /// </summary>
    [Fact]
    public void CopilotRuntimeOptions_Constructor_UndefinedSource_Throws()
    {
        // Act
        var failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CopilotRuntimeOptions("copilot-runtime", runtimeSource: (CopilotRuntimeSource)7));

        // Assert
        Assert.Equal("runtimeSource", failure.ParamName);
    }

    /// <summary>
    ///     Proves a blank CLI path is treated as none, for either source.
    /// </summary>
    [Fact]
    public void CopilotRuntimeOptions_Constructor_BlankCliPath_IsNull()
    {
        // Act
        var installed = new CopilotRuntimeOptions("copilot-runtime", cliPath: " ");
        var bundled = new CopilotRuntimeOptions("copilot-runtime", runtimeSource: CopilotRuntimeSource.Bundled, cliPath: string.Empty);

        // Assert
        Assert.Null(installed.CliPath);
        Assert.Null(bundled.CliPath);
        Assert.Equal(CopilotRuntimeSource.Bundled, bundled.RuntimeSource);
    }

    /// <summary>
    ///     Proves missing options are refused.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_StartAsync_NullOptions_Throws()
    {
        // Act / Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => CopilotRuntime.StartAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a start that fails on the bundled runtime is explained — the runtime's own error
    ///     first, then the missing direct SDK reference as the common cause of a runtime that was not
    ///     found, then every source the runtime authenticates from — with a redacted copy of the
    ///     original failure, carrying its type name and message, kept as the cause, and that a start
    ///     whose launch is not known, including one whose failure message is null, is explained for
    ///     both sources.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_StartClient_FailedStart_ExplainsTheMissingRuntimeAndKeepsTheCause()
    {
        // Arrange: the error the SDK raises when the bundled runtime is not in the output
        var cause = new InvalidOperationException("Copilot runtime wrapper not found at 'runtimes/win-x64/native/copilot-runtime.exe'.");
        var bundled = new CopilotRuntimeLaunch(CopilotRuntimeSource.Bundled, null, null);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.StartClientAsync(
            _ => Task.FromException(cause),
            bundled,
            signIn: null,
            TestContext.Current.CancellationToken));
        var nullMessage = new NullMessageException();
        var nullFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.StartClientAsync(
            _ => Task.FromException(nullMessage),
            TestContext.Current.CancellationToken));

        // Assert
        AssertRedactedCopyOf(cause, failure.InnerException);
        Assert.Contains(cause.Message, failure.Message, StringComparison.Ordinal);
        Assert.Contains("PackageReference to Microsoft.Copilot.SDK", failure.Message, StringComparison.Ordinal);
        Assert.Contains(
            "gitHubToken or gitHubTokenProvider, COPILOT_GITHUB_TOKEN, GH_TOKEN or GITHUB_TOKEN, the user's Copilot CLI sign-in",
            failure.Message,
            StringComparison.Ordinal);
        AssertRedactedCopyOf(nullMessage, nullFailure.InnerException);
        Assert.Contains("Microsoft.Copilot.SDK", nullFailure.Message, StringComparison.Ordinal);
        Assert.Contains("installed Copilot CLI", nullFailure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an installed CLI that fails to start is explained with its path and version, and is
    ///     not blamed on a missing SDK package reference.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_StartClient_InstalledCliFailure_DoesNotBlameTheSdkReference()
    {
        // Arrange
        var cause = new IOException("The pipe has been ended.");
        var launch = new CopilotRuntimeLaunch(CopilotRuntimeSource.InstalledCli, "/fake/bin/copilot", new Version(1, 0, 114));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.StartClientAsync(
            _ => Task.FromException(cause),
            launch,
            signIn: null,
            TestContext.Current.CancellationToken));

        // Assert
        AssertRedactedCopyOf(cause, failure.InnerException);
        Assert.Contains(cause.Message, failure.Message, StringComparison.Ordinal);
        Assert.Contains("'/fake/bin/copilot' (version 1.0.114)", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference", failure.Message, StringComparison.Ordinal);
        Assert.Contains("GH_TOKEN", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the SDK's refusal of a runtime on an older protocol is explained as a CLI too old,
    ///     with upgrade guidance, keeping the SDK's error.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_StartClient_ProtocolMismatchOlderServer_IsExplainedAsTooOld()
    {
        // Arrange: the error SDK 2.0.1 raises for a CLI on protocol 3
        var cause = new InvalidOperationException(
            "SDK protocol version mismatch: SDK supports versions 4-4, but server reports version 3. Please update your SDK or server to ensure compatibility.");
        var launch = new CopilotRuntimeLaunch(CopilotRuntimeSource.InstalledCli, "/fake/bin/copilot", null);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.StartClientAsync(
            _ => Task.FromException(cause),
            launch,
            signIn: null,
            TestContext.Current.CancellationToken));

        // Assert
        AssertRedactedCopyOf(cause, failure.InnerException);
        Assert.Contains(cause.Message, failure.Message, StringComparison.Ordinal);
        Assert.Contains("speaks SDK protocol version 3", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"{CopilotCli.MinimumVersion} or later", failure.Message, StringComparison.Ordinal);
        Assert.Contains("winget upgrade GitHub.Copilot", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference to Microsoft.Copilot.SDK to the host", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the SDK's refusal of a runtime on a newer protocol is explained as a CLI newer than
    ///     this release of AgentKit.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_StartClient_ProtocolMismatchNewerServer_IsExplainedAsTooNew()
    {
        // Arrange
        var cause = new InvalidOperationException(
            "SDK protocol version mismatch: SDK supports versions 4-4, but server reports version 5. Please update your SDK or server to ensure compatibility.");
        var launch = new CopilotRuntimeLaunch(CopilotRuntimeSource.InstalledCli, "/fake/bin/copilot", new Version(1, 2, 0));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.StartClientAsync(
            _ => Task.FromException(cause),
            launch,
            signIn: null,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("speaks SDK protocol version 5", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Update AgentKit", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the whole start-failure explanation is redacted, the installed CLI's path the cause
    ///     names included, for an ordinary failure and for a protocol mismatch alike.
    /// </summary>
    /// <param name="mismatch">Whether the failure is the SDK's protocol-version mismatch.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopilotRuntime_StartClient_InstalledCliPathQuotingASecret_IsRedacted(bool mismatch)
    {
        // Arrange
        const string secret = "s3cr3t-value-xyz";
        var cause = new InvalidOperationException(mismatch
            ? "SDK protocol version mismatch: SDK supports versions 4-4, but server reports version 3. Please update your SDK or server to ensure compatibility."
            : "The pipe has been ended.");
        var launch = new CopilotRuntimeLaunch(CopilotRuntimeSource.InstalledCli, $"/opt/{secret}/bin/copilot", new Version(1, 0, 114));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.StartClientAsync(
            _ => Task.FromException(cause),
            launch,
            CopilotSignInTests.PlanKnowing(secret),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("/opt/<redacted>/bin/copilot", failure.Message, StringComparison.Ordinal);
        Assert.Contains(mismatch ? "speaks SDK protocol version 3" : "(version 1.0.114)", failure.Message, StringComparison.Ordinal);
        SecretAssert.NoSecret(failure, secret);
    }

    /// <summary>
    ///     Proves a canceled start propagates its cancellation unchanged rather than being explained
    ///     as a fault.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_StartClient_Canceled_PropagatesUnchanged()
    {
        // Arrange
        var canceled = new OperationCanceledException("stopped");

        // Act
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => CopilotRuntime.StartClientAsync(
            _ => Task.FromException(canceled),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(canceled, failure);
    }

    /// <summary>
    ///     Proves a canceled start carrying a cause or a token is rebuilt as a new cancellation of the
    ///     same kind — an <see cref="OperationCanceledException"/>, or a
    ///     <see cref="TaskCanceledException"/> kept as one — with the same token, a redacted message and
    ///     a redacted copy as its cause, so nothing in its chain carries a secret.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_StartClient_CanceledCarryingASecret_IsRebuiltRedacted()
    {
        // Arrange
        const string secret = "s3cr3t-start-cancel-xyz";
        const string tokenShaped = "gho_STOPPED0123456789abcdefABCDEF0123";
        var plan = CopilotSignInTests.PlanKnowing(secret);
        using var source = new CancellationTokenSource();
        var canceled = new OperationCanceledException(
            $"stopped {secret}",
            new IOException($"pipe {secret} {tokenShaped}"),
            source.Token);
        var taskCanceled = new TaskCanceledException("task stopped", new IOException($"pipe {secret}"), source.Token);

        // Act
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => CopilotRuntime.StartClientAsync(
            _ => Task.FromException(canceled),
            launch: null,
            plan,
            TestContext.Current.CancellationToken));
        var taskFailure = await Assert.ThrowsAsync<TaskCanceledException>(() => CopilotRuntime.StartClientAsync(
            _ => Task.FromException(taskCanceled),
            launch: null,
            plan,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.NotSame(canceled, failure);
        Assert.Equal(source.Token, failure.CancellationToken);
        Assert.Equal("stopped <redacted>", failure.Message);
        var copy = Assert.IsType<CopilotRedactedException>(failure.InnerException);
        Assert.Equal(typeof(OperationCanceledException).FullName, copy.OriginalTypeName);
        SecretAssert.NoSecret(failure, secret, tokenShaped);
        Assert.NotSame(taskCanceled, taskFailure);
        Assert.Equal(source.Token, taskFailure.CancellationToken);
        Assert.IsType<CopilotRedactedException>(taskFailure.InnerException);
        SecretAssert.NoSecret(taskFailure, secret);
    }

    /// <summary>
    ///     Proves a start that succeeds completes without an error.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_StartClient_Succeeds_DoesNotThrow()
    {
        // Arrange
        var started = false;

        // Act
        await CopilotRuntime.StartClientAsync(
            _ =>
            {
                started = true;
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(started);
    }

    /// <summary>
    ///     Proves a client whose start fails is disposed before the failure propagates, so no runtime
    ///     process is left behind.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_StartOwned_FailedStart_DisposesTheClient()
    {
        // Arrange
        var owned = new object();
        object? disposed = null;
        var cause = new InvalidOperationException("no runtime");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.StartOwnedAsync(
            () => owned,
            (_, _) => Task.FromException(cause),
            client =>
            {
                disposed = client;
                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(cause, failure);
        Assert.Same(owned, disposed);
    }

    /// <summary>
    ///     Proves a client whose start is canceled is disposed and the cancellation propagates.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_StartOwned_Canceled_DisposesTheClient()
    {
        // Arrange
        var owned = new object();
        object? disposed = null;

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CopilotRuntime.StartOwnedAsync(
            () => owned,
            (_, _) => Task.FromException(new OperationCanceledException()),
            client =>
            {
                disposed = client;
                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(owned, disposed);
    }

    /// <summary>
    ///     Proves a client whose start succeeds is returned and not disposed.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_StartOwned_Succeeds_ReturnsTheClientUndisposed()
    {
        // Arrange
        var owned = new object();
        var disposed = false;

        // Act
        var started = await CopilotRuntime.StartOwnedAsync(
            () => owned,
            (_, _) => Task.CompletedTask,
            _ =>
            {
                disposed = true;
                return ValueTask.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(owned, started);
        Assert.False(disposed);
    }

    /// <summary>
    ///     Proves disposal is idempotent and the client is unavailable afterwards.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_DisposeAsync_IsIdempotentAndClientIsThenUnavailable()
    {
        // Arrange: a runtime over a client that was never started, which spawns nothing
        using var directory = new TemporaryDirectory();
        var client = new CopilotClient(CopilotRuntime.BuildClientOptions(
            new CopilotRuntimeOptions(directory.Path),
            directory.Path,
            new Hashtable()));
        var runtime = new CopilotRuntime(client, directory.Path);
        Assert.Same(client, runtime.Client);

        // Act
        await runtime.DisposeAsync();
        await runtime.DisposeAsync();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => runtime.Client);
        Assert.Equal(directory.Path, runtime.DataDirectory);
    }

    /// <summary>
    ///     Proves a missing or blank session identifier is refused before the runtime is reached.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_DeleteSessionAsync_BlankSessionId_Throws()
    {
        // Arrange: a runtime over a client that was never started
        using var directory = new TemporaryDirectory();
        await using var runtime = new CopilotRuntime(
            new CopilotClient(CopilotRuntime.BuildClientOptions(new CopilotRuntimeOptions(directory.Path), directory.Path, new Hashtable())),
            directory.Path);

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => runtime.DeleteSessionAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.DeleteSessionAsync(" ", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a session cannot be deleted through a runtime that has been disposed.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_DeleteSessionAsync_AfterDisposal_Throws()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var runtime = new CopilotRuntime(
            new CopilotClient(CopilotRuntime.BuildClientOptions(new CopilotRuntimeOptions(directory.Path), directory.Path, new Hashtable())),
            directory.Path);
        await runtime.DisposeAsync();

        // Act / Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => runtime.DeleteSessionAsync("session", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a deletion the runtime refuses or fails — an RPC error quoting a secret in its
    ///     message, data and error data, the same error wrapped in an
    ///     <see cref="InvalidOperationException"/> that also quotes a token-shaped value, and an I/O
    ///     failure thrown synchronously — is relayed with this runtime's sign-in plan as a new
    ///     exception of the kind a host catches, with a redacted message, a redacted copy carrying no
    ///     data as its cause, and nothing in its chain carrying a secret.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_DeleteSessionAsync_RuntimeFailureQuotingASecret_IsRelayedRedacted()
    {
        // Arrange: a runtime whose sign-in knows the secret, and deletions the runtime fails
        const string secret = "s3cr3t-delete-xyz";
        const string tokenShaped = "gho_DELETE0123456789abcdefABCDEF012345";
        const string sessionId = "session-to-delete";
        using var directory = new TemporaryDirectory();
        await using var runtime = new CopilotRuntime(NewClient(directory.Path), directory.Path, signIn: CopilotSignInTests.PlanKnowing(secret));
        using var source = new CancellationTokenSource();
        var seen = new List<(string SessionId, CancellationToken Token)>();
        CopilotSignInTests.RpcErrorException NewRpcError()
        {
            var rpc = new CopilotSignInTests.RpcErrorException($"delete {sessionId} refused {secret}", new { token = secret });
            rpc.Data["token"] = secret;
            return rpc;
        }

        // Act
        var rpcFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RelayDeleteSessionAsync(
            (id, token) =>
            {
                seen.Add((id, token));
                return Task.FromException(NewRpcError());
            },
            sessionId,
            source.Token));
        var wrappedFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.RelayDeleteSessionAsync(
            (id, token) =>
            {
                seen.Add((id, token));
                return Task.FromException(new InvalidOperationException($"Session error: {secret} {tokenShaped}", NewRpcError()));
            },
            sessionId,
            source.Token));
        var ioFailure = await Assert.ThrowsAsync<IOException>(() => runtime.RelayDeleteSessionAsync(
            (id, token) =>
            {
                seen.Add((id, token));
                throw new IOException($"pipe broke on {secret}");
            },
            sessionId,
            source.Token));

        // Assert
        Assert.All(seen, call =>
        {
            Assert.Equal(sessionId, call.SessionId);
            Assert.Equal(source.Token, call.Token);
        });
        Assert.Equal(3, seen.Count);
        Assert.Equal($"delete {sessionId} refused <redacted>", rpcFailure.Message);
        Assert.Equal("Session error: <redacted> gho_<redacted>", wrappedFailure.Message);
        Assert.Equal("pipe broke on <redacted>", ioFailure.Message);
        var expectedTypes = new[] { typeof(CopilotSignInTests.RpcErrorException), typeof(InvalidOperationException), typeof(IOException) };
        var failures = new Exception[] { rpcFailure, wrappedFailure, ioFailure };
        for (var index = 0; index < failures.Length; index++)
        {
            var copy = Assert.IsType<CopilotRedactedException>(failures[index].InnerException);
            Assert.Equal(expectedTypes[index].FullName, copy.OriginalTypeName);
            for (Exception? current = failures[index]; current is not null; current = current.InnerException)
            {
                Assert.Empty(current.Data);
            }

            SecretAssert.NoSecret(failures[index], secret, tokenShaped);
        }
    }

    /// <summary>
    ///     Proves a deletion canceled without a cause or anything to redact reaches the caller as the
    ///     same cancellation.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_DeleteSessionAsync_CleanCancellation_PassesUnchanged()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        await using var runtime = new CopilotRuntime(NewClient(directory.Path), directory.Path, signIn: CopilotSignInTests.PlanKnowing("s3cr3t-unused"));
        var canceled = new OperationCanceledException("canceled");

        // Act
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => runtime.RelayDeleteSessionAsync(
            (_, _) => Task.FromException(canceled),
            "session",
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(canceled, failure);
    }

    /// <summary>
    ///     Proves the bundled runtime is launched without discovery or a version probe.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_ResolveLaunch_Bundled_SkipsDiscoveryAndTheVersionProbe()
    {
        // Arrange: a machine with a CLI on PATH that must not be chosen
        var machine = new FakeCopilotCliMachine();
        machine.AddFile(Path.Combine("/fake/bin", "copilot"));
        var options = new CopilotRuntimeOptions("copilot-runtime", runtimeSource: CopilotRuntimeSource.Bundled);

        // Act
        var launch = await CopilotRuntime.ResolveLaunchAsync(
            options,
            FakeDataDirectory,
            new Hashtable(),
            machine.Context(CopilotCliPlatform.Linux, "/fake/bin"),
            machine.RunAsync,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new CopilotRuntimeLaunch(CopilotRuntimeSource.Bundled, null, null), launch);
        Assert.Empty(machine.Started);
    }

    /// <summary>
    ///     Proves a CLI path the host names wins over the one discovery would find, and is probed.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_ResolveLaunch_ExplicitPath_WinsOverDiscovery()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        machine.AddFile(Path.Combine("/fake/bin", "copilot"));
        var named = machine.AddFile(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pinned", "copilot")));
        var options = new CopilotRuntimeOptions("copilot-runtime", cliPath: named);

        // Act
        var launch = await CopilotRuntime.ResolveLaunchAsync(
            options,
            FakeDataDirectory,
            new Hashtable(),
            machine.Context(CopilotCliPlatform.Linux, "/fake/bin"),
            machine.RunAsync,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(named, launch.CliPath);
        Assert.Equal(named, Assert.Single(machine.Started).FileName);
    }

    /// <summary>
    ///     Proves a CLI path the host names that does not exist is refused before anything runs.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_ResolveLaunch_ExplicitPathMissing_IsRefused()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        var named = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "missing", "copilot"));
        var options = new CopilotRuntimeOptions("copilot-runtime", cliPath: named);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.ResolveLaunchAsync(
            options,
            FakeDataDirectory,
            new Hashtable(),
            machine.Context(CopilotCliPlatform.Linux),
            machine.RunAsync,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(named, failure.Message, StringComparison.Ordinal);
        Assert.Contains("does not exist", failure.Message, StringComparison.Ordinal);
        Assert.Empty(machine.Started);
    }

    /// <summary>
    ///     Proves a machine without a CLI is refused with where AgentKit looked and how to install one.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_ResolveLaunch_NothingFound_IsRefusedWithInstallGuidance()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.ResolveLaunchAsync(
            new CopilotRuntimeOptions("copilot-runtime"),
            FakeDataDirectory,
            new Hashtable(),
            machine.Context(CopilotCliPlatform.Windows, "/fake/bin"),
            machine.RunAsync,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(Path.Combine("/fake/bin", "copilot.exe"), failure.Message, StringComparison.Ordinal);
        Assert.Contains("winget install GitHub.Copilot", failure.Message, StringComparison.Ordinal);
        Assert.Contains("CopilotRuntimeSource.Bundled", failure.Message, StringComparison.Ordinal);
        Assert.Empty(machine.Started);
    }

    /// <summary>
    ///     Proves <c>COPILOT_CLI_PATH</c> in the host's environment never chooses the CLI: discovery
    ///     ignores it, so a machine with nothing else installed is refused.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_ResolveLaunch_CopilotCliPathVariable_IsIgnored()
    {
        // Arrange: the variable names a CLI that exists, but no discovery location holds one
        var machine = new FakeCopilotCliMachine();
        var ambient = machine.AddFile(Path.Combine("/fake/ambient", "copilot"));
        var host = new Hashtable { ["COPILOT_CLI_PATH"] = ambient };

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.ResolveLaunchAsync(
            new CopilotRuntimeOptions("copilot-runtime"),
            FakeDataDirectory,
            host,
            machine.Context(CopilotCliPlatform.Linux, "/fake/bin"),
            machine.RunAsync,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("could not find an installed GitHub Copilot CLI", failure.Message, StringComparison.Ordinal);
        Assert.Empty(machine.Started);
    }

    /// <summary>
    ///     Proves a CLI older than the minimum is refused with upgrade guidance before any client exists.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_ResolveLaunch_TooOldCli_IsRefusedBeforeAnyClientExists()
    {
        // Arrange: the installed CLI reports 1.0.106
        var machine = new FakeCopilotCliMachine
        {
            Result = new(0, "GitHub Copilot CLI 1.0.106.\nRun 'copilot update' to check for updates.\n", string.Empty, TimedOut: false),
        };
        var cli = machine.AddFile(Path.Combine("/fake/bin", "copilot"));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.ResolveLaunchAsync(
            new CopilotRuntimeOptions("copilot-runtime"),
            FakeDataDirectory,
            new Hashtable(),
            machine.Context(CopilotCliPlatform.Linux, "/fake/bin"),
            machine.RunAsync,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(cli, failure.Message, StringComparison.Ordinal);
        Assert.Contains("1.0.106", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"{CopilotCli.MinimumVersion} or later", failure.Message, StringComparison.Ordinal);
        Assert.Contains("copilot --no-auto-update --version", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves every launch refusal — a named CLI that does not exist, no CLI found, a CLI too old,
    ///     and a CLI whose version check failed — is redacted of the host environment's secret values
    ///     and the host's token, wherever a path or the check's error output quotes them.
    /// </summary>
    /// <param name="kind">The refusal: <c>missing</c>, <c>notFound</c>, <c>tooOld</c> or <c>probe</c>.</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("notFound")]
    [InlineData("tooOld")]
    [InlineData("probe")]
    public async Task CopilotRuntime_ResolveLaunch_RefusalQuotingAnEnvironmentSecret_IsRedacted(string kind)
    {
        // Arrange: a GitHub Enterprise token, not shaped like a GitHub token, and the host's token,
        // each also part of the folder a path names
        const string secret = "s3cr3t-value-xyz";
        const string hostToken = "s3cr3t-host-token";
        var folder = $"/opt/{secret}/{hostToken}/bin";
        var machine = new FakeCopilotCliMachine
        {
            Result = kind == "probe"
                ? new(3, string.Empty, $"cannot run from {folder}", TimedOut: false)
                : new(0, "GitHub Copilot CLI 1.0.106.\n", string.Empty, TimedOut: false),
        };
        if (kind is "tooOld" or "probe")
        {
            machine.AddFile(Path.Combine(folder, "copilot"));
        }

        var named = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"missing-{secret}-{hostToken}", "copilot"));
        var options = kind == "missing"
            ? new CopilotRuntimeOptions("copilot-runtime", gitHubToken: hostToken, cliPath: named)
            : new CopilotRuntimeOptions("copilot-runtime", gitHubToken: hostToken);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.ResolveLaunchAsync(
            options,
            FakeDataDirectory,
            new Hashtable { ["GH_ENTERPRISE_TOKEN"] = secret },
            machine.Context(CopilotCliPlatform.Linux, folder),
            machine.RunAsync,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(kind == "missing" ? "missing-<redacted>-<redacted>" : "/opt/<redacted>/<redacted>/bin", failure.Message, StringComparison.Ordinal);
        SecretAssert.NoSecret(failure, secret, hostToken);
    }

    /// <summary>
    ///     Proves a supported CLI is launched with its path and the version it reported.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_ResolveLaunch_SupportedCli_CarriesPathAndVersion()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine();
        var cli = machine.AddFile(Path.Combine("/fake/bin", "copilot"));

        // Act
        var launch = await CopilotRuntime.ResolveLaunchAsync(
            new CopilotRuntimeOptions("copilot-runtime"),
            FakeDataDirectory,
            new Hashtable(),
            machine.Context(CopilotCliPlatform.Linux, "/fake/bin"),
            machine.RunAsync,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new CopilotRuntimeLaunch(CopilotRuntimeSource.InstalledCli, cli, new Version(1, 0, 113)), launch);
    }

    /// <summary>
    ///     Proves the installed CLI's version probe runs under the same withheld environment the runtime
    ///     gets, so a host's Copilot configuration variable never reaches the CLI on that path either.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_ResolveLaunch_ProbesUnderTheWithheldEnvironment()
    {
        // Arrange: a host environment carrying Copilot configuration
        var machine = new FakeCopilotCliMachine();
        machine.AddFile(Path.Combine("/fake/bin", "copilot"));
        var host = new Hashtable { ["COPILOT_ALLOW_ALL"] = "1", ["COPILOT_GITHUB_TOKEN"] = "token", ["PATH"] = "/fake/bin" };

        // Act
        await CopilotRuntime.ResolveLaunchAsync(
            new CopilotRuntimeOptions("copilot-runtime"),
            FakeDataDirectory,
            host,
            machine.Context(CopilotCliPlatform.Linux, "/fake/bin"),
            machine.RunAsync,
            TestContext.Current.CancellationToken);

        // Assert
        var environment = Assert.Single(machine.Started).Environment;
        Assert.Equal(
            CopilotRuntime.BuildRuntimeEnvironment(host).Append(new("COPILOT_HOME", FakeDataDirectory)).OrderBy(entry => entry.Key, StringComparer.Ordinal),
            environment.Select(entry => new KeyValuePair<string, string>(entry.Key, entry.Value!)).OrderBy(entry => entry.Key, StringComparer.Ordinal));
        Assert.False(environment.ContainsKey("COPILOT_ALLOW_ALL"));
    }

    /// <summary>
    ///     Proves a CLI whose output names no version is launched, leaving the decision to the SDK's
    ///     protocol handshake.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_ResolveLaunch_NoVersionReported_DefersToTheHandshake()
    {
        // Arrange
        var machine = new FakeCopilotCliMachine { Result = new(0, "development build", string.Empty, TimedOut: false) };
        var cli = machine.AddFile(Path.Combine("/fake/bin", "copilot"));

        // Act
        var launch = await CopilotRuntime.ResolveLaunchAsync(
            new CopilotRuntimeOptions("copilot-runtime"),
            FakeDataDirectory,
            new Hashtable(),
            machine.Context(CopilotCliPlatform.Linux, "/fake/bin"),
            machine.RunAsync,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(cli, launch.CliPath);
        Assert.Null(launch.CliVersion);
    }

    /// <summary>
    ///     Proves a runtime that started but is not signed in is refused, with the runtime's own status,
    ///     every sign-in source AgentKit tried with what it found, and every way to sign it in.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_VerifyStarted_Unauthenticated_IsRefusedWithSignInGuidance()
    {
        // Arrange
        var status = new GetStatusResponse { Version = "1.0.113", ProtocolVersion = 4 };
        var authentication = new GetAuthStatusResponse { IsAuthenticated = false, StatusMessage = "Not authenticated" };
        var plan = new CopilotSignInPlan
        {
            EnvironmentFinding = "none set.",
            CopilotCliFinding = "no Copilot CLI configuration found.",
            FallbackFinding = "left to the runtime.",
        };

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.VerifyStartedAsync(
            _ => Task.FromResult(status),
            _ => Task.FromResult(authentication),
            plan,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("Not authenticated", failure.Message, StringComparison.Ordinal);
        Assert.Contains("1. gitHubToken / gitHubTokenProvider: not supplied.", failure.Message, StringComparison.Ordinal);
        Assert.Contains("2. COPILOT_GITHUB_TOKEN, GH_TOKEN, GITHUB_TOKEN: none set.", failure.Message, StringComparison.Ordinal);
        Assert.Contains("3. Copilot CLI sign-in: no Copilot CLI configuration found.", failure.Message, StringComparison.Ordinal);
        Assert.Contains("4. Runtime fallbacks", failure.Message, StringComparison.Ordinal);
        Assert.Contains("copilot login", failure.Message, StringComparison.Ordinal);
        Assert.Contains("gh auth login", failure.Message, StringComparison.Ordinal);
        Assert.Contains("gitHubHost", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("is not used", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a signed-in runtime's status is returned.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_VerifyStarted_Authenticated_ReturnsTheStatus()
    {
        // Arrange
        var status = new GetStatusResponse { Version = "1.0.114", ProtocolVersion = 4 };
        var authentication = new GetAuthStatusResponse { IsAuthenticated = true, AuthType = "gh-cli" };

        // Act
        var verified = await CopilotRuntime.VerifyStartedAsync(
            _ => Task.FromResult(status),
            _ => Task.FromResult(authentication),
            new CopilotSignInPlan(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(status, verified.Status);
        Assert.Same(authentication, verified.Authentication);
    }

    /// <summary>
    ///     Proves a status or authentication read that fails quoting a known secret — not shaped like a
    ///     GitHub token — and a token-shaped value is relayed as a new exception of the kind a host
    ///     catches, with a redacted message, a redacted copy naming the original type as its cause,
    ///     and no secret anywhere in its chain.
    /// </summary>
    /// <param name="failingRead">Which read fails: <c>status</c> or <c>authentication</c>.</param>
    /// <param name="kind">The failure the read raises: <c>io</c> or <c>rpc</c>.</param>
    [Theory]
    [InlineData("status", "io")]
    [InlineData("status", "rpc")]
    [InlineData("authentication", "io")]
    [InlineData("authentication", "rpc")]
    public async Task CopilotRuntime_VerifyStarted_ReadFailureQuotingASecret_IsRelayedRedacted(string failingRead, string kind)
    {
        // Arrange
        const string secret = "s3cr3t-value-xyz";
        const string shaped = "gho_VERIFY0123456789abcdefABCDEF0123456";
        var plan = CopilotSignInTests.PlanKnowing(secret);
        Exception NewFailure()
        {
            if (kind == "io")
            {
                return new IOException($"pipe broke reading {secret} {shaped}");
            }

            var rpc = new CopilotSignInTests.RpcErrorException($"read refused {secret} {shaped}", new { token = secret });
            rpc.Data["token"] = secret;
            return rpc;
        }

        Func<CancellationToken, Task<GetStatusResponse>> readStatus = _ => failingRead == "status"
            ? Task.FromException<GetStatusResponse>(NewFailure())
            : Task.FromResult(new GetStatusResponse { Version = "1.0.114", ProtocolVersion = 4 });
        Func<CancellationToken, Task<GetAuthStatusResponse>> readAuthentication = _ => failingRead == "authentication"
            ? Task.FromException<GetAuthStatusResponse>(NewFailure())
            : Task.FromResult(new GetAuthStatusResponse { IsAuthenticated = true });

        // Act
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => CopilotRuntime.VerifyStartedAsync(
            readStatus,
            readAuthentication,
            plan,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType(kind == "io" ? typeof(IOException) : typeof(InvalidOperationException), failure);
        Assert.Contains("<redacted> gho_<redacted>", failure.Message, StringComparison.Ordinal);
        var copy = Assert.IsType<CopilotRedactedException>(failure.InnerException);
        Assert.Equal(kind == "io" ? typeof(IOException).FullName : typeof(CopilotSignInTests.RpcErrorException).FullName, copy.OriginalTypeName);
        SecretAssert.NoSecret(failure, secret, shaped);
    }

    /// <summary>
    ///     Proves a clean cancellation or disposal of either read — no cause and nothing to redact —
    ///     reaches the caller as the very same instance.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_VerifyStarted_CleanCancellation_PassesUnchanged()
    {
        // Arrange
        var canceled = new OperationCanceledException("canceled");
        var disposed = new ObjectDisposedException("CopilotClient");
        var status = new GetStatusResponse { Version = "1.0.114", ProtocolVersion = 4 };

        // Act
        var statusCancellation = await Assert.ThrowsAsync<OperationCanceledException>(() => CopilotRuntime.VerifyStartedAsync(
            _ => Task.FromException<GetStatusResponse>(canceled),
            _ => Task.FromResult(new GetAuthStatusResponse { IsAuthenticated = true }),
            new CopilotSignInPlan(),
            TestContext.Current.CancellationToken));
        var authenticationDisposal = await Assert.ThrowsAsync<ObjectDisposedException>(() => CopilotRuntime.VerifyStartedAsync(
            _ => Task.FromResult(status),
            _ => Task.FromException<GetAuthStatusResponse>(disposed),
            new CopilotSignInPlan(),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(canceled, statusCancellation);
        Assert.Same(disposed, authenticationDisposal);
    }

    /// <summary>
    ///     Proves the runtime reports the CLI it launched and the version and protocol the started
    ///     runtime reported, and that a runtime not started by <c>StartAsync</c> reports none.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_Properties_ReportTheLaunchAndTheStatus()
    {
        // Arrange: runtimes over clients that were never started
        using var directory = new TemporaryDirectory();
        var launch = new CopilotRuntimeLaunch(CopilotRuntimeSource.InstalledCli, "/fake/bin/copilot", new Version(1, 0, 114));
        var status = new GetStatusResponse { Version = "1.0.114", ProtocolVersion = 4 };

        // Act
        await using var started = new CopilotRuntime(NewClient(directory.Path), directory.Path, launch, status);
        await using var bare = new CopilotRuntime(NewClient(directory.Path), directory.Path);

        // Assert
        Assert.Equal("/fake/bin/copilot", started.CliPath);
        Assert.Equal("1.0.114", started.RuntimeVersion);
        Assert.Equal(4, started.ProtocolVersion);
        Assert.Null(bare.CliPath);
        Assert.Equal(string.Empty, bare.RuntimeVersion);
        Assert.Equal(0, bare.ProtocolVersion);
    }

    /// <summary>
    ///     Proves the runtime reports how it signed in — the source classified from the runtime's own
    ///     report, and the host and login it reported — and that a runtime built without a sign-in
    ///     reports none.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_Properties_ReportTheSignIn()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var plan = new CopilotSignInPlan { Source = CopilotSignInSource.CopilotCli, Token = "gho_token", HostName = "example.ghe.com" };
        var authentication = new GetAuthStatusResponse { IsAuthenticated = true, AuthType = "token", Host = "https://example.ghe.com", Login = "octo_user" };

        // Act
        await using var signedIn = new CopilotRuntime(NewClient(directory.Path), directory.Path, signIn: plan, authentication: authentication);
        await using var bare = new CopilotRuntime(NewClient(directory.Path), directory.Path);

        // Assert
        Assert.Equal(CopilotSignInSource.CopilotCli, signedIn.SignInSource);
        Assert.Equal("https://example.ghe.com", signedIn.GitHubHost);
        Assert.Equal("octo_user", signedIn.GitHubLogin);
        Assert.Equal(CopilotSignInSource.Unknown, bare.SignInSource);
        Assert.Null(bare.GitHubHost);
        Assert.Null(bare.GitHubLogin);
    }

    /// <summary>
    ///     Proves the client options carry the resolved sign-in: its token, and its host as the
    ///     runtime's <c>COPILOT_GH_HOST</c>, overriding the host environment's.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildClientOptions_SignInPlan_CarriesTheTokenAndHost()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var plan = new CopilotSignInPlan { Source = CopilotSignInSource.CopilotCli, Token = "gho_cli", HostName = "example.ghe.com" };
        var host = new Hashtable { ["COPILOT_GH_HOST"] = "other.ghe.com", ["PATH"] = "/usr/bin" };

        // Act
        var clientOptions = CopilotRuntime.BuildClientOptions(new CopilotRuntimeOptions(directory.Path), directory.Path, host, signIn: plan);

        // Assert
        Assert.Equal("gho_cli", clientOptions.GitHub.Token);
        var environment = Assert.IsType<SpawnedStdioRuntimeConnection>(clientOptions.Runtime).Environment!;
        Assert.Equal("example.ghe.com", environment["COPILOT_GH_HOST"]);
        Assert.Equal(CopilotClientMode.Empty, clientOptions.Mode);
    }

    /// <summary>
    ///     Proves a sign-in that hands over no token and names no host leaves the runtime's environment
    ///     exactly as before and sets no token, so the runtime's own sources decide.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildClientOptions_NoTokenOrHost_LeavesTheEnvironmentUnchanged()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var host = new Hashtable { ["COPILOT_GH_HOST"] = "other.ghe.com", ["GH_TOKEN"] = "gh", ["PATH"] = "/usr/bin" };

        // Act
        var clientOptions = CopilotRuntime.BuildClientOptions(
            new CopilotRuntimeOptions(directory.Path),
            directory.Path,
            host,
            signIn: new CopilotSignInPlan { Source = CopilotSignInSource.EnvironmentVariable });

        // Assert
        Assert.Null(clientOptions.GitHub.Token);
        Assert.Equal(
            CopilotRuntime.BuildRuntimeEnvironment(host).OrderBy(entry => entry.Key, StringComparer.Ordinal),
            Assert.IsType<SpawnedStdioRuntimeConnection>(clientOptions.Runtime).Environment!.OrderBy(entry => entry.Key, StringComparer.Ordinal));
    }

    /// <summary>
    ///     Proves the options' GitHub host is normalized from each accepted form, and that a blank host
    ///     means none.
    /// </summary>
    /// <param name="host">The host as passed.</param>
    /// <param name="expected">The normalized host, or <see langword="null"/>.</param>
    [Theory]
    [InlineData("example.ghe.com", "https://example.ghe.com")]
    [InlineData("https://example.ghe.com", "https://example.ghe.com")]
    [InlineData("https://example.ghe.com/", "https://example.ghe.com")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void CopilotRuntimeOptions_GitHubHost_IsNormalized(string? host, string? expected)
    {
        // Act
        var options = new CopilotRuntimeOptions("copilot-runtime", gitHubHost: host);

        // Assert
        Assert.Equal(expected, options.GitHubHost);
    }

    /// <summary>
    ///     Proves a GitHub host that is not an https host without a path, query, user name or port is
    ///     refused, naming the parameter.
    /// </summary>
    /// <param name="host">The host as passed.</param>
    [Theory]
    [InlineData("http://example.ghe.com")]
    [InlineData("https://example.ghe.com/api")]
    [InlineData("https://example.ghe.com:8443")]
    [InlineData("https://user@example.ghe.com")]
    public void CopilotRuntimeOptions_InvalidGitHubHost_Throws(string host)
    {
        // Act
        var failure = Assert.Throws<ArgumentException>(() => new CopilotRuntimeOptions("copilot-runtime", gitHubHost: host));

        // Assert
        Assert.Equal("gitHubHost", failure.ParamName);
    }

    /// <summary>
    ///     Proves a token and a token provider together are refused, since one of them would be ignored.
    /// </summary>
    [Fact]
    public void CopilotRuntimeOptions_TokenAndProvider_Throws()
    {
        // Act
        var failure = Assert.Throws<ArgumentException>(() => new CopilotRuntimeOptions(
            "copilot-runtime",
            gitHubToken: "gho_token",
            gitHubTokenProvider: (_, _) => Task.FromResult(new CopilotGitHubToken("gho_token"))));

        // Assert
        Assert.Equal("gitHubTokenProvider", failure.ParamName);
    }

    /// <summary>
    ///     Proves the sign-in options default to no host, no provider and the Copilot CLI sign-in
    ///     allowed, and that each value passed is kept.
    /// </summary>
    [Fact]
    public void CopilotRuntimeOptions_SignInOptions_DefaultAndKeepTheirValues()
    {
        // Arrange
        Func<CopilotGitHubTokenRequest, CancellationToken, Task<CopilotGitHubToken>> provider =
            (_, _) => Task.FromResult(new CopilotGitHubToken("gho_token"));

        // Act
        var defaults = new CopilotRuntimeOptions("copilot-runtime");
        var chosen = new CopilotRuntimeOptions("copilot-runtime", gitHubTokenProvider: provider, useCopilotCliSignIn: false);

        // Assert
        Assert.Null(defaults.GitHubHost);
        Assert.Null(defaults.GitHubTokenProvider);
        Assert.True(defaults.UseCopilotCliSignIn);
        Assert.Same(provider, chosen.GitHubTokenProvider);
        Assert.False(chosen.UseCopilotCliSignIn);
    }

    /// <summary>
    ///     Proves a session opened on a runtime started with a token provider gets a session provider,
    ///     and one on a runtime without leaves the session's GitHub options untouched.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_ApplySessionSignIn_SetsTheProviderOnlyWhenTheRuntimeHasOne()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var plan = new CopilotSignInPlan
        {
            Source = CopilotSignInSource.GitHubTokenProvider,
            Token = "gho_token",
            Provider = (_, _) => Task.FromResult(new CopilotGitHubToken("gho_token")),
        };
        await using var withProvider = new CopilotRuntime(NewClient(directory.Path), directory.Path, signIn: plan);
        await using var without = new CopilotRuntime(NewClient(directory.Path), directory.Path, signIn: new CopilotSignInPlan());
        var create = new CreateSessionOptions();
        var resume = new ResumeSessionOptions();
        var untouched = new CreateSessionOptions();

        // Act
        withProvider.ApplySessionSignIn(create);
        withProvider.ApplySessionSignIn(resume);
        without.ApplySessionSignIn(untouched);

        // Assert
        Assert.NotNull(create.GitHub.TokenProvider);
        Assert.NotNull(resume.GitHub.TokenProvider);
        Assert.Null(create.GitHub.Token);
        Assert.Null(untouched.GitHub.TokenProvider);
        Assert.Null(untouched.GitHub.Token);
    }

    /// <summary>
    ///     Proves a start failure that quotes the runtime's start token or a session token the host's
    ///     provider returned is explained without either, even when neither is shaped like a GitHub
    ///     token, and that the cause kept is a redacted copy of the failure whose whole chain —
    ///     including a nested inner exception and every inner exception of an aggregate — carries
    ///     neither token.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_StartClient_FailureQuotingASignInToken_IsRedacted()
    {
        // Arrange: a runtime started with a token, whose provider has handed a session its token
        const string startToken = "s3cr3t-start-xyz";
        const string sessionToken = "s3cr3t-session-xyz";
        using var directory = new TemporaryDirectory();
        var plan = new CopilotSignInPlan
        {
            Source = CopilotSignInSource.GitHubTokenProvider,
            Token = startToken,
            Provider = (_, _) => Task.FromResult(new CopilotGitHubToken(sessionToken)),
        };
        await using var runtime = new CopilotRuntime(NewClient(directory.Path), directory.Path, signIn: plan);
        var session = new CreateSessionOptions();
        runtime.ApplySessionSignIn(session);
        await session.GitHub.TokenProvider!(new GitHubTokenProviderArgs { Host = "https://example.ghe.com", Reason = GitHubTokenRequestReason.Initial });
        var aggregate = new AggregateException(
            "both failed",
            new IOException($"pipe broke on {startToken}"),
            new UnauthorizedAccessException($"refused {sessionToken}"));
        var cause = new InvalidOperationException(
            $"connection lost (start {startToken}, session {sessionToken})",
            new InvalidOperationException($"handshake with {sessionToken}", aggregate));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.StartClientAsync(
            _ => Task.FromException(cause),
            launch: null,
            runtime.SignIn,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.False(failure.Message.Contains(startToken, StringComparison.Ordinal), "The explanation carries the start token.");
        Assert.False(failure.Message.Contains(sessionToken, StringComparison.Ordinal), "The explanation carries the session token.");
        Assert.Contains("connection lost (start <redacted>, session <redacted>)", failure.Message, StringComparison.Ordinal);
        var copy = Assert.IsType<CopilotRedactedException>(failure.InnerException);
        Assert.Equal(typeof(InvalidOperationException).FullName, copy.OriginalTypeName);
        Assert.Equal("connection lost (start <redacted>, session <redacted>)", copy.Message);
        Assert.Equal(typeof(AggregateException).FullName, Assert.IsType<CopilotRedactedException>(copy.InnerException?.InnerException).OriginalTypeName);
        SecretAssert.NoSecret(failure, startToken, sessionToken);
    }

    /// <summary>
    ///     Proves a provider's token must not be blank and must report more than one hour of life, and
    ///     that its string form never carries the token.
    /// </summary>
    [Fact]
    public void CopilotGitHubToken_Constructor_ValidatesAndRedacts()
    {
        // Act
        var token = new CopilotGitHubToken("gho_SECRET_VALUE", TimeSpan.FromHours(2));
        var unknown = new CopilotGitHubToken("gho_SECRET_VALUE");

        // Assert
        Assert.Throws<ArgumentException>(() => new CopilotGitHubToken("  "));
        Assert.Throws<ArgumentNullException>(() => new CopilotGitHubToken(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CopilotGitHubToken("gho_x", TimeSpan.FromHours(1)));
        Assert.Equal("gho_SECRET_VALUE", token.Token);
        Assert.Equal(TimeSpan.FromHours(2), token.ExpiresIn);
        Assert.Null(unknown.ExpiresIn);
        Assert.DoesNotContain("SECRET_VALUE", token.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET_VALUE", unknown.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds a client that is never started, so it spawns nothing.
    /// </summary>
    /// <param name="dataDirectory">The data directory.</param>
    /// <returns>The client.</returns>
    private static CopilotClient NewClient(string dataDirectory) =>
        new(CopilotRuntime.BuildClientOptions(new CopilotRuntimeOptions(dataDirectory), dataDirectory, new Hashtable()));

    /// <summary>
    ///     Asserts that an inner exception is the redacted copy of a cause, not the cause itself: its
    ///     original type name and its message, which held no secret, are kept.
    /// </summary>
    /// <param name="cause">The failure the start raised.</param>
    /// <param name="inner">The inner exception the explanation carries.</param>
    private static void AssertRedactedCopyOf(Exception cause, Exception? inner)
    {
        var copy = Assert.IsType<CopilotRedactedException>(inner);
        Assert.NotSame(cause, inner);
        Assert.Equal(cause.GetType().FullName, copy.OriginalTypeName);
        Assert.Equal(cause.Message ?? string.Empty, copy.Message);
    }

    /// <summary>
    ///     A fake data directory for launch resolution, which never touches it.
    /// </summary>
    private static readonly string FakeDataDirectory = Path.Combine("/fake", "data");

    /// <summary>
    ///     Builds the content of a marker that is not the one AgentKit writes.
    /// </summary>
    /// <param name="variant">The kind of content.</param>
    /// <returns>The marker's bytes.</returns>
    private static byte[] MarkerVariant(string variant) => variant switch
    {
        "empty" => [],
        "byte-order-mark" => [0xEF, 0xBB, 0xBF, .. ExpectedMarker],
        "crlf" => System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(ExpectedMarker).Replace("\n", "\r\n", StringComparison.Ordinal)),
        "extra-line-feed" => [.. ExpectedMarker, (byte)'\n'],
        "no-final-line-feed" => ExpectedMarker[..^1],
        "earlier-unversioned" => System.Text.Encoding.UTF8.GetBytes(
            "This directory is the data directory of a GitHub Copilot runtime started by AgentKit.\n"
            + "AgentKit created it and reuses it. Do not point the Copilot CLI or another tool at it, and do\n"
            + "not add Copilot configuration to it.\n"),
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown marker variant."),
    };

    /// <summary>
    ///     The phrase that identifies a refusal for an invalid marker, as distinct from a directory
    ///     without one.
    /// </summary>
    private const string InvalidMarkerPhrase = "is not the marker AgentKit writes";

    /// <summary>
    ///     The exact bytes of the format-1 marker, written out here independently of the library so a
    ///     change to the marker's format is a deliberate change to this test as well.
    /// </summary>
    private static readonly byte[] ExpectedMarker =
        ("AgentKit Copilot runtime data directory, marker format 1\n"u8
        + "This directory is the data directory of a GitHub Copilot runtime started by AgentKit, which created\n"u8
        + "it and reuses it. Do not point the Copilot CLI or another tool at it, and do not edit this file.\n"u8).ToArray();

    /// <summary>
    ///     A directory under the system temporary folder, deleted when disposed.
    /// </summary>
    private sealed class TemporaryDirectory : IDisposable
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TemporaryDirectory"/> class, creating the
        ///     directory.
        /// </summary>
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentkit-copilot-runtime-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        /// <summary>
        ///     Gets the directory's full path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        ///     Deletes the directory and everything in it.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    /// <summary>
    ///     An exception whose message is null, as a third-party exception may override it to be.
    /// </summary>
    public sealed class NullMessageException : Exception
    {
        /// <inheritdoc/>
        public override string Message => null!;
    }
}
