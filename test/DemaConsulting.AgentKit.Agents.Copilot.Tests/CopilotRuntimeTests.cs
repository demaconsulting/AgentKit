using System.Collections;
using Microsoft.Copilot;
using Microsoft.Extensions.Logging.Abstractions;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for <see cref="CopilotRuntime"/> and <see cref="CopilotRuntimeOptions"/>: the client
///     options AgentKit builds are confined, the runtime's environment withholds the host's Copilot
///     configuration variables, the data directory is one AgentKit owns, a failed start is explained
///     and releases the client, and disposal is idempotent.
/// </summary>
/// <remarks>
///     No test starts a runtime or changes the process environment: the host environment is passed
///     in, the start and disposal are injected through the internal seams, and every data directory
///     is created under the system temporary folder and deleted afterwards.
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
    ///     Proves the client options spawn the bundled runtime over stdio on the prepared data
    ///     directory, with no runtime path, arguments or child working directory of the host's.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildClientOptions_SpawnsTheBundledRuntimeOverStdio()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var options = new CopilotRuntimeOptions(directory.Path);

        // Act
        var clientOptions = CopilotRuntime.BuildClientOptions(options, directory.Path, new Hashtable());

        // Assert
        var connection = Assert.IsType<SpawnedStdioRuntimeConnection>(clientOptions.Runtime);
        Assert.Null(connection.Path);
        Assert.Null(connection.Args);
        Assert.Null(connection.WorkingDirectory);
        Assert.Equal(directory.Path, connection.BaseDirectory);
        Assert.NotNull(connection.Environment);
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
    ///     options.
    /// </summary>
    [Fact]
    public void CopilotRuntime_BuildClientOptions_CarriesTokenLogLevelAndLogger()
    {
        // Arrange
        using var directory = new TemporaryDirectory();
        var logger = NullLogger.Instance;
        var options = new CopilotRuntimeOptions(directory.Path, "gho_token", CopilotLogLevel.Warning, logger);

        // Act
        var clientOptions = CopilotRuntime.BuildClientOptions(options, directory.Path, new Hashtable());

        // Assert
        Assert.Equal("gho_token", clientOptions.GitHub.Token);
        Assert.Equal(CopilotLogLevel.Warning, clientOptions.Observability.LogLevel);
        Assert.Same(logger, clientOptions.Observability.Logger);
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
    ///     Proves a start that fails is explained — the runtime's own error first, then the missing
    ///     direct SDK reference as the common cause of a runtime that was not found, then every source
    ///     the runtime authenticates from — with the original failure kept as the cause, including a
    ///     failure whose message is null.
    /// </summary>
    [Fact]
    public async Task CopilotRuntime_StartClient_FailedStart_ExplainsTheMissingRuntimeAndKeepsTheCause()
    {
        // Arrange: the error the SDK raises when the bundled runtime is not in the output
        var cause = new InvalidOperationException("Copilot runtime wrapper not found at 'runtimes/win-x64/native/copilot-runtime.exe'.");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.StartClientAsync(
            _ => Task.FromException(cause),
            TestContext.Current.CancellationToken));
        var nullMessage = new NullMessageException();
        var nullFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.StartClientAsync(
            _ => Task.FromException(nullMessage),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Same(cause, failure.InnerException);
        Assert.Contains(cause.Message, failure.Message, StringComparison.Ordinal);
        Assert.Contains("PackageReference to Microsoft.Copilot.SDK", failure.Message, StringComparison.Ordinal);
        Assert.Contains(
            "CopilotRuntimeOptions, COPILOT_GITHUB_TOKEN, GH_TOKEN, GITHUB_TOKEN or the signed-in GitHub CLI user",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Same(nullMessage, nullFailure.InnerException);
        Assert.Contains("Microsoft.Copilot.SDK", nullFailure.Message, StringComparison.Ordinal);
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
