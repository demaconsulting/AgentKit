using System.Collections.Concurrent;
using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using Microsoft.Agents.AI;
using Microsoft.Copilot;
using Microsoft.Copilot.Rpc;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot.LiveTests;

/// <summary>
///     Live integration scenarios for the AgentKit Copilot package against the real Copilot runtime.
/// </summary>
/// <remarks>
///     <para>
///     Supplementary verification evidence: each scenario re-measures a property the package's
///     design depends on but the hermetic unit tests can only assume — what the runtime actually
///     exposes, refuses, approves, delivers and compacts. Wherever possible the assertion is on the
///     runtime's events and metadata rather than on the model's prose, which varies between runs.
///     </para>
///     <para>
///     Every scenario skips unless <c>AGENTKIT_COPILOT_LIVE_MODEL</c> names the model to use. The
///     suite is outside the solution and the merge gate's test run, and is not linked to the
///     requirements trace, because it never runs in the pipeline that builds the trace matrix.
///     </para>
/// </remarks>
[Collection(CopilotLiveCollection.Name)]
public sealed class CopilotLiveTests
{
    /// <summary>
    ///     The name of the non-GitHub MCP server L12 configures in a client's data directory.
    /// </summary>
    private const string AmbientMcpProbe = "agentkit-ambient-probe";

    /// <summary>
    ///     The name of the user agent L13 defines in a client's data directory.
    /// </summary>
    private const string UserAgentProbe = "agentkit-probe-user";

    /// <summary>
    ///     The name of the project agent L13 defines in the session's workspace.
    /// </summary>
    private const string WorkspaceAgentProbe = "agentkit-probe-workspace";

    /// <summary>
    ///     How long one turn may take before the scenario gives up.
    /// </summary>
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromMinutes(4);

    /// <summary>
    ///     The run's shared state.
    /// </summary>
    private readonly CopilotLiveFixture _live;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotLiveTests"/> class.
    /// </summary>
    /// <param name="live">The run's shared state.</param>
    public CopilotLiveTests(CopilotLiveFixture live)
    {
        _live = live;
    }

    /// <summary>
    ///     L1: the confinement the package builds exposes exactly the supplied tools and none of the
    ///     runtime's built-ins.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Confinement_EmptyModeCustomAllowList_ExposesOnlySuppliedTools()
    {
        // Arrange
        _live.RequireOptIn();
        var config = CopilotAgentFactory.BuildEngineSessionConfig(_live.ReadTools, "You are a concise assistant.", _live.Model);

        // Act: no inference is needed, only the runtime's own account of the session's tools
        var names = await CurrentToolNamesAsync(config);

        // Assert
        Assert.Equal(_live.ReadTools.Select(tool => tool.Name).Order(), names.Order());
    }

    /// <summary>
    ///     L2: a control showing the <c>custom:</c> qualifier is load-bearing — a bare-name allow-list
    ///     admits a built-in that carries a listed name.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Confinement_BareAllowList_WouldAdmitBuiltIns()
    {
        // Arrange: the package's confinement, with the qualified allow-list replaced by bare names
        _live.RequireOptIn();
        var config = CopilotAgentFactory.BuildEngineSessionConfig(_live.ReadTools, null, _live.Model);
        config.Tools.AvailableTools = [.. _live.ReadTools.Select(tool => tool.Name), "view", "powershell", "bash"];

        // Act
        var names = await CurrentToolNamesAsync(config);

        // Assert: at least one built-in got in
        Assert.Contains(names, name => name is "view" or "powershell" or "bash");
    }

    /// <summary>
    ///     L3: every built-in tool the runtime lists on this platform is reserved by the package's
    ///     name guard, so a runtime that adds a built-in is caught here rather than by an application.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_BuiltInToolNames_AreAllReserved()
    {
        // Arrange
        _live.RequireOptIn();

        // Act
        var listed = await _live.Client.Rpc.Tools.ListAsync(_live.Model!, TestContext.Current.CancellationToken);
        var names = listed.Tools.Select(tool => tool.Name).ToList();
        _live.Log($"L3 runtime built-ins on {Environment.OSVersion.Platform}: {string.Join(", ", names)}");

        // Assert
        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, name => !CopilotAgentFactory.ReservedBuiltInToolNames.Contains(name));
    }

    /// <summary>
    ///     L4: a tool named like a built-in is refused by the factory before any session exists, and
    ///     the runtime itself refuses such a session — the failure the guard brings forward.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_CollisionGuard_ToolNamedLikeABuiltIn_IsRefusedBeforeSessionCreation()
    {
        // Arrange
        _live.RequireOptIn();
        var impostor = AIFunctionFactory.Create(() => "impostor result", "view", "A custom tool named like a built-in.");

        // Act / Assert: the package refuses at composition
        Assert.Throws<ArgumentException>(() => CopilotAgentFactory.Create(_live.Client, [impostor], model: _live.Model));

        // Act / Assert: the control - a session configured by hand - is refused by the runtime
        var control = CopilotAgentFactory.BuildEngineSessionConfig(_live.ReadTools, null, _live.Model);
        control.Tools.Definitions = [impostor];
        control.Tools.AvailableTools = ["custom:view"];
        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => _live.Client.CreateSessionAsync(control, TestContext.Current.CancellationToken));
        _live.Log($"L4 runtime refusal: {failure.GetType().Name}: {failure.Message}");
        Assert.Contains("view", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     L5: an AgentKit tool reads inside its path policy and is denied outside it, on the real
    ///     runtime.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_CustomTool_ReadsInsideAndIsDeniedOutside()
    {
        // Arrange
        _live.RequireOptIn();
        var config = CopilotAgentFactory.BuildEngineSessionConfig(_live.ReadTools, "You are a concise assistant.", _live.Model);

        // Act
        var run = await RunTurnsAsync(
            config,
            "Use text_file_read to read notes.txt and tell me the CODEWORD it contains. Then use text_file_read to read "
            + $"{Path.Combine(_live.Outside, "secret.txt")} and tell me exactly what the tool returned.");

        // Assert: on the tool results the runtime reported, not on the prose
        var results = run.Events.OfType<ToolExecutionCompleteEvent>()
            .Select(complete => complete.Data.Result?.Content ?? complete.Data.Error?.Message ?? string.Empty)
            .ToList();
        Assert.Contains(results, result => result.Contains(CopilotLiveFixture.Codeword, StringComparison.Ordinal));
        Assert.Contains(results, result => result.Contains("Denied", StringComparison.Ordinal));
        Assert.DoesNotContain(results, result => result.Contains(CopilotLiveFixture.OutsideCodeword, StringComparison.Ordinal));
    }

    /// <summary>
    ///     L6: the default-deny handler approves only custom-tool requests for supplied tools — every
    ///     call of a supplied tool reaches it, and nothing else is approved.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_DefaultDeny_OnlyCustomToolRequestsAreApproved()
    {
        // Arrange: the default handler, wrapped to record each decision it makes
        _live.RequireOptIn();
        var config = CopilotAgentFactory.BuildEngineSessionConfig(_live.ReadTools, "You are a concise assistant.", _live.Model);
        var decisions = new ConcurrentQueue<(PermissionRequest Request, PermissionDecision Decision)>();
        var deny = config.Permissions.OnRequest!;
        config.Permissions.OnRequest = async (request, invocation) =>
        {
            var decision = await deny(request, invocation);
            decisions.Enqueue((request, decision));
            _live.Log($"L6 permission {request.Kind} -> {decision.Kind}");
            return decision;
        };

        // Act
        await RunTurnsAsync(
            config,
            "Use text_file_read to read notes.txt. Then try to run the shell command `whoami` with any shell tool you have, "
            + "and report what happened.");

        // Assert
        var approved = decisions.Where(entry => entry.Decision.Kind == PermissionDecision.ApproveOnce().Kind).ToList();
        Assert.NotEmpty(approved);
        Assert.All(approved, entry =>
        {
            var custom = Assert.IsType<PermissionRequestCustomTool>(entry.Request);
            Assert.Contains(custom.ToolName, _live.ReadTools.Select(tool => tool.Name));
        });
    }

    /// <summary>
    ///     L7: with the runtime's large-output diversion switched off, a result over its 20 KiB
    ///     threshold reaches the model inline rather than as a temporary-file reference.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_LargeOutput_ThirtyKilobyteResult_IsDeliveredInline()
    {
        // Arrange: a tool returning about 30 KB whose last line is a marker
        _live.RequireOptIn();
        var big = AIFunctionFactory.Create(
            () => string.Concat(Enumerable.Range(0, 600).Select(i => $"line {i:D4}: filler text to make the output large.\n"))
                + "FINAL-MARKER: ZEBRA-77",
            "big_output",
            "Returns a large block of text whose last line contains a final marker.");
        var config = CopilotAgentFactory.BuildEngineSessionConfig([big], "You are a concise assistant.", _live.Model);

        // Act
        var run = await RunTurnsAsync(config, "Call big_output once and report the exact text of its last line.");

        // Assert
        var content = Assert.Single(run.Events.OfType<ToolExecutionCompleteEvent>()).Data.Result?.Content ?? string.Empty;
        Assert.True(content.Length > 20_480, $"The result was {content.Length} characters.");
        Assert.EndsWith("FINAL-MARKER: ZEBRA-77", content.TrimEnd(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     L8: an image an AgentKit tool returns reaches the model as an image.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_ImageDelivery_ImageReadResult_ReachesTheModel()
    {
        // Arrange
        _live.RequireOptIn();
        var config = CopilotAgentFactory.BuildEngineSessionConfig(_live.ReadTools, "You are a concise assistant.", _live.Model);

        // Act
        var run = await RunTurnsAsync(
            config,
            "Use image_read to read split.png. Name the two colors you see and which side of the image each is on.");

        // Assert: the runtime forwarded binary content to the model, and the answer names both colors
        var complete = run.Events.OfType<ToolExecutionCompleteEvent>().Last(evt => evt.Data.Result is not null);
        Assert.NotEmpty(complete.Data.Result!.BinaryResultsForLlm ?? []);
        var answer = run.Answers[^1];
        Assert.Contains("blue", answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("yellow", answer, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     L9: AgentKit's session engine rotates on the runtime's own usage figures, and the
    ///     conversation survives the rotation.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Usage_RotationOnTheRuntimesUsage_PreservesTheCodeword()
    {
        // Arrange: a ceiling low enough that the runtime's reported occupancy forces rotation
        _live.RequireOptIn();
        var options = new AgentSessionOptions(
            new CopilotSummarizer(_live.Client, _live.Model),
            instructions: "You are a concise assistant confined to the permitted locations.",
            tools: [.. _live.ReadTools]);
        await using var session = await CompactingAgentSession.CreateAsync(
            options,
            new CopilotProviderSessionFactory(_live.Client, _live.Model, maxWindowTokens: 2500, workingDirectory: _live.Sandbox),
            TestContext.Current.CancellationToken);

        // Act
        var lastAnswer = string.Empty;
        foreach (var message in new[]
                 {
                     "Read notes.txt with text_file_read and tell me the CODEWORD.",
                     "Write three sentences about periwinkle flowers.",
                     "What was the CODEWORD from notes.txt? Answer from memory, without calling any tool.",
                 })
        {
            var reply = await session.SendAsync(message, TestContext.Current.CancellationToken);
            _live.Log($"L9 turn: rotation={reply.RotationOccurred} rotations={session.RotationCount} used={reply.Usage.UsedTokens} window={reply.Usage.WindowTokens}");
            Assert.True(reply.Usage.WindowTokens > 0);
            lastAnswer = reply.Text;
        }

        // Assert
        Assert.True(session.RotationCount >= 1);
        Assert.Contains(CopilotLiveFixture.Codeword, lastAnswer, StringComparison.Ordinal);
    }

    /// <summary>
    ///     L10a: the agent streams the root agent's text as it arrives.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Agent_Streams_TextDeltas()
    {
        // Arrange
        _live.RequireOptIn();
        var agent = CreateAgent();
        var session = (CopilotAgentSession)await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        try
        {
            // Act
            var updates = new List<AgentResponseUpdate>();
            await foreach (var update in agent.RunStreamingAsync(
                "Read notes.txt with text_file_read and tell me the CODEWORD in one sentence.",
                session,
                cancellationToken: TestContext.Current.CancellationToken))
            {
                updates.Add(update);
            }

            // Assert: the text arrived as streamed deltas, after a call of the supplied tool
            var contents = updates.SelectMany(update => update.Contents).ToList();
            Assert.Contains(contents, content => content is FunctionCallContent { Name: "text_file_read" });
            Assert.Contains(contents, content => content is TextContent { RawRepresentation: AssistantMessageDeltaEvent });
            Assert.Contains(
                CopilotLiveFixture.Codeword,
                string.Concat(contents.OfType<TextContent>().Select(text => text.Text)),
                StringComparison.Ordinal);
        }
        finally
        {
            await DeleteAsync(session);
        }
    }

    /// <summary>
    ///     L10b: a serialized conversation resumes on a new agent instance — the runtime kept the
    ///     session the first agent released, and the new agent reopened it.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Agent_SerializedSession_ResumesOnANewAgent()
    {
        // Arrange: a conversation that learned a fact on a first agent, then was serialized
        _live.RequireOptIn();
        var first = CreateAgent();
        var session = await first.CreateSessionAsync(TestContext.Current.CancellationToken);
        await first.RunAsync(
            "Remember this fact for later: the vault color is VERMILION. Reply with OK only.",
            session,
            cancellationToken: TestContext.Current.CancellationToken);
        var serialized = await first.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken);

        var restored = (CopilotAgentSession)await CreateAgent().DeserializeSessionAsync(serialized, cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            // Act: a new agent instance asks about the fact
            var answer = await CreateAgent().RunAsync(
                "What is the vault color I told you? Answer with the color only, without calling any tool.",
                restored,
                cancellationToken: TestContext.Current.CancellationToken);

            // Assert
            Assert.Contains("VERMILION", answer.Text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await DeleteAsync(restored);
        }
    }

    /// <summary>
    ///     L10c: an image passed to the agent as inline data reaches the model.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Agent_ImageAttachment_ReachesTheModel()
    {
        // Arrange
        _live.RequireOptIn();
        var agent = CreateAgent();
        var session = (CopilotAgentSession)await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        var message = new ChatMessage(
            ChatRole.User,
            [
                new TextContent("The attached image has two colors. Name both, without calling any tool."),
                new DataContent(TestPng.SplitBlueYellow(64, 64), "image/png"),
            ]);

        try
        {
            // Act
            var answer = await agent.RunAsync(message, session, cancellationToken: TestContext.Current.CancellationToken);
            _live.Log($"L10c answer: {answer.Text}");

            // Assert
            Assert.Contains("blue", answer.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("yellow", answer.Text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await DeleteAsync(session);
        }
    }

    /// <summary>
    ///     L11: a client that cannot start because <c>COPILOT_CLI_PATH</c> points elsewhere is
    ///     explained by the starter, carrying the runtime's error, which names the variable's folder,
    ///     and naming the variable and the remedy.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_ClientStarter_AmbientCopilotCliPath_IsExplained()
    {
        // Arrange: a bogus COPILOT_CLI_PATH, restored whatever happens
        _live.RequireOptIn();
        const string variable = "COPILOT_CLI_PATH";
        var previous = Environment.GetEnvironmentVariable(variable);
        var bogus = Path.Combine(_live.Root, "no-runtime", OperatingSystem.IsWindows() ? "copilot.exe" : "copilot");
        Environment.SetEnvironmentVariable(variable, bogus);

        try
        {
            await using var client = new CopilotClient(
                CopilotLiveFixture.CreateClientOptions(Path.Combine(_live.Root, "second-home")));

            // Act
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => CopilotClientStarter.StartAsync(client, TestContext.Current.CancellationToken));
            _live.Log($"L11 explanation: {failure.Message} | cause: {failure.InnerException?.Message}");

            // Assert: the runtime's error names the variable's folder, so the starter blames the
            // variable and gives the remedies
            Assert.NotNull(failure.InnerException);
            Assert.Contains(failure.InnerException.Message, failure.Message, StringComparison.Ordinal);
            Assert.Contains(variable, failure.Message, StringComparison.Ordinal);
            Assert.Contains(bogus, failure.Message, StringComparison.Ordinal);
            Assert.Contains("To fix it", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    /// <summary>
    ///     L12a: an MCP server configured in the data directory of a client in CodingAgent mode — the
    ///     mode the integration's precondition excludes, and in which the SDK supplies none of its
    ///     Empty-mode defaults — is disabled on a session opened through the production channel, and
    ///     the session exposes exactly the supplied tools.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Confinement_AmbientMcpServerOnACodingAgentClient_IsDisabled()
    {
        // Arrange: a CodingAgent client whose data directory configures a non-GitHub MCP server
        _live.RequireOptIn();
        await using var client = await StartAmbientMcpClientAsync("ambient-home-a");
        var config = CopilotAgentFactory.BuildEngineSessionConfig(_live.ReadTools, null, _live.Model, _live.Sandbox);

        // Act: open the session exactly as production opens one
        var session = await CopilotSessionChannel.CreateConfinedSessionAsync(client, config, TestContext.Current.CancellationToken);
        try
        {
            var configured = await session.Rpc.Mcp.ListConfiguredAsync(TestContext.Current.CancellationToken);
            _live.Log($"L12a configured: [{Describe(configured.Servers)}] disabled at creation: [{string.Join(", ", config.Mcp.DisabledServers ?? [])}]");
            var metadata = await session.Rpc.Tools.GetCurrentMetadataAsync(TestContext.Current.CancellationToken);
            var names = (metadata.Tools ?? []).Select(tool => tool.Name).ToList();
            _live.Log($"L12a tools: [{string.Join(", ", names)}]");

            // Assert: the probe was named before creation, no server is enabled, and the tool set is
            // exactly the supplied tools
            Assert.Contains(AmbientMcpProbe, config.Mcp.DisabledServers ?? []);
            Assert.DoesNotContain(configured.Servers, server => server.Enabled);
            Assert.Equal(_live.ReadTools.Select(tool => tool.Name).Order(), names.Order());
        }
        finally
        {
            await session.DisposeAsync();
            await client.DeleteSessionAsync(session.SessionId, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     L12b: a control showing the channel's disabling of the server is load-bearing — the same configuration
    ///     handed to the runtime directly, bypassing the production channel, leaves the ambient
    ///     server enabled.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Confinement_AmbientMcpServerWithoutTheChannel_IsEnabled()
    {
        // Arrange
        _live.RequireOptIn();
        await using var client = await StartAmbientMcpClientAsync("ambient-home-b");
        var config = CopilotAgentFactory.BuildEngineSessionConfig(_live.ReadTools, null, _live.Model, _live.Sandbox);

        // Act: create the session on the client directly
        var session = await client.CreateSessionAsync(config, TestContext.Current.CancellationToken);
        try
        {
            var configured = await session.Rpc.Mcp.ListConfiguredAsync(TestContext.Current.CancellationToken);
            _live.Log($"L12b configured: [{Describe(configured.Servers)}]");

            // Assert: without the channel, the ambient server is enabled for the session
            Assert.Contains(configured.Servers, server => server.Name == AmbientMcpProbe && server.Enabled);
        }
        finally
        {
            await session.DisposeAsync();
            await client.DeleteSessionAsync(session.SessionId, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     L12c: the seal alone disables an ambient MCP server the session enabled at creation — the
    ///     production channel's discovery step is replaced with one that finds nothing, so the server
    ///     is enabled when the session opens, and the production seal's session-scoped disable is what
    ///     disables it. This is the path a server from a source discovery does not cover takes.
    /// </summary>
    /// <remarks>
    ///     The server's command does not exist, so "enabled" here is the runtime's configuration
    ///     state, not a running process: the scenario shows the session-scoped disable withdraws the
    ///     server's enablement and the re-listing confirms it, not that a live process is terminated.
    /// </remarks>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Confinement_AmbientMcpServerEnabledAtCreation_IsDisabledByTheSeal()
    {
        // Arrange
        _live.RequireOptIn();
        await using var client = await StartAmbientMcpClientAsync("ambient-home-c");
        var config = CopilotAgentFactory.BuildEngineSessionConfig(_live.ReadTools, null, _live.Model, _live.Sandbox);
        IList<McpConfiguredServer> beforeSeal = [];

        // Act: open through the production channel with discovery finding nothing, recording the
        // listing the production seal starts from
        var session = await CopilotSessionChannel.OpenConfinedAsync(
            config,
            _ => Task.FromResult<IEnumerable<string>>([]),
            token => client.CreateSessionAsync(config, token),
            async (opened, token) =>
            {
                beforeSeal = (await opened.Rpc.Mcp.ListConfiguredAsync(token)).Servers;
                await CopilotSessionChannel.SealSessionAsync(opened, token);
            },
            opened => CopilotSessionChannel.ReleaseAsync(
                opened.DisposeAsync,
                () => client.DeleteSessionAsync(opened.SessionId, CancellationToken.None)),
            TestContext.Current.CancellationToken);
        try
        {
            var configured = await session.Rpc.Mcp.ListConfiguredAsync(TestContext.Current.CancellationToken);
            var metadata = await session.Rpc.Tools.GetCurrentMetadataAsync(TestContext.Current.CancellationToken);
            var names = (metadata.Tools ?? []).Select(tool => tool.Name).ToList();
            _live.Log($"L12c before seal: [{Describe(beforeSeal)}] after seal: [{Describe(configured.Servers)}] disabled at creation: [{string.Join(", ", config.Mcp.DisabledServers ?? [])}]");
            _live.Log($"L12c tools: [{string.Join(", ", names)}]");

            // Assert: the probe was not named at creation, so the seal alone is responsible; it was
            // enabled when the seal began; it is disabled afterwards with no server enabled; and the
            // tool set is exactly the supplied tools
            Assert.DoesNotContain(AmbientMcpProbe, config.Mcp.DisabledServers ?? []);
            Assert.Contains(beforeSeal, server => server.Name == AmbientMcpProbe && server.Enabled);
            Assert.Contains(configured.Servers, server => server.Name == AmbientMcpProbe && !server.Enabled);
            Assert.DoesNotContain(configured.Servers, server => server.Enabled);
            Assert.Equal(_live.ReadTools.Select(tool => tool.Name).Order(), names.Order());
        }
        finally
        {
            await session.DisposeAsync();
            await client.DeleteSessionAsync(session.SessionId, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     L12d: a session created without the production channel, with the ambient MCP server
    ///     enabled and one short turn taken so the runtime persists it, is resumed through the
    ///     production channel's resume path — the server is named before the resume, no server is
    ///     enabled on the resumed session, and it exposes exactly the supplied tools.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Confinement_AmbientMcpServerOnAResumedSession_IsDisabled()
    {
        // Arrange: a session created directly, so the probe is enabled on it, then released
        _live.RequireOptIn();
        await using var client = await StartAmbientMcpClientAsync("ambient-home-d");
        var created = await client.CreateSessionAsync(
            CopilotAgentFactory.BuildEngineSessionConfig(_live.ReadTools, null, _live.Model, _live.Sandbox),
            TestContext.Current.CancellationToken);
        var sessionId = created.SessionId;
        var createdConfigured = await created.Rpc.Mcp.ListConfiguredAsync(TestContext.Current.CancellationToken);
        _live.Log($"L12d created directly: [{Describe(createdConfigured.Servers)}]");

        // Measured: the runtime persists a session only once it has taken a turn, and refuses to
        // resume one that never has ("Session not found"), so one short turn makes it resumable
        var reply = await created.SendAndWaitAsync("Reply with OK only.", TurnTimeout, TestContext.Current.CancellationToken);
        _live.Log($"L12d turn before release: {reply?.Data?.Content}");
        await created.DisposeAsync();
        var resume = CopilotAgentFactory.BuildResumeSessionConfig(_live.ReadTools, null, null, _live.Model, _live.Sandbox);

        try
        {
            // Act: resume exactly as production resumes one
            await using var session = await CopilotSessionChannel.ResumeConfinedSessionAsync(
                client,
                sessionId,
                resume,
                TestContext.Current.CancellationToken);
            var configured = await session.Rpc.Mcp.ListConfiguredAsync(TestContext.Current.CancellationToken);
            var metadata = await session.Rpc.Tools.GetCurrentMetadataAsync(TestContext.Current.CancellationToken);
            var names = (metadata.Tools ?? []).Select(tool => tool.Name).ToList();
            _live.Log($"L12d resumed: [{Describe(configured.Servers)}] disabled at resume: [{string.Join(", ", resume.Mcp.DisabledServers ?? [])}]");
            _live.Log($"L12d tools: [{string.Join(", ", names)}]");

            // Assert: the same session, the probe named before the resume, no server enabled, and
            // exactly the supplied tools
            Assert.Equal(sessionId, session.SessionId);
            Assert.Contains(createdConfigured.Servers, server => server.Name == AmbientMcpProbe && server.Enabled);
            Assert.Contains(AmbientMcpProbe, resume.Mcp.DisabledServers ?? []);
            Assert.DoesNotContain(configured.Servers, server => server.Enabled);
            Assert.Equal(_live.ReadTools.Select(tool => tool.Name).Order(), names.Order());
        }
        finally
        {
            await client.DeleteSessionAsync(sessionId, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     L13: agent definitions — a user agent in the data directory of a client in CodingAgent
    ///     mode and a project agent in the session's workspace, both of which the runtime's own
    ///     discovery reports — are withheld from a session opened through the production channel,
    ///     which lists no agent, built-ins included, and exposes exactly the supplied tools.
    /// </summary>
    /// <remarks>
    ///     No model turn is taken: the session's agent listing and tool set are what decide whether
    ///     a definition could be selected or delegated to. Discovery finding both definitions is the
    ///     control that keeps the scenario from passing on a fixture the runtime ignores.
    /// </remarks>
    [Fact]
    public async Task AgentKitAgentsCopilotLive_Confinement_AgentDefinitionsOnACodingAgentClient_AreWithheld()
    {
        // Arrange: a CodingAgent client whose data directory defines a user agent, and a workspace
        // that defines a project agent, both under the run's root
        _live.RequireOptIn();
        var workspace = Path.Combine(_live.Root, "agents-workspace");
        Assert.StartsWith(_live.Root, workspace, StringComparison.Ordinal);
        Directory.CreateDirectory(Path.Combine(workspace, ".github", "agents"));
        await File.WriteAllTextAsync(
            Path.Combine(workspace, ".github", "agents", $"{WorkspaceAgentProbe}.agent.md"),
            AgentDefinition(WorkspaceAgentProbe),
            TestContext.Current.CancellationToken);
        await using var client = await StartAgentDefinitionsClientAsync("agents-home", workspace);
        var config = CopilotAgentFactory.BuildEngineSessionConfig(_live.ReadTools, null, _live.Model, workspace);

        // Act: open the session exactly as production opens one
        var session = await CopilotSessionChannel.CreateConfinedSessionAsync(client, config, TestContext.Current.CancellationToken);
        try
        {
            var listed = await session.Rpc.Agent.ListAsync(
                new SessionAgentListRequest { IncludeBuiltInAgents = true },
                TestContext.Current.CancellationToken);
            var agents = (listed.Agents ?? []).Select(agent => $"{agent.Name}({agent.Source})").ToList();
            var metadata = await session.Rpc.Tools.GetCurrentMetadataAsync(TestContext.Current.CancellationToken);
            var names = (metadata.Tools ?? []).Select(tool => tool.Name).ToList();
            _live.Log($"L13 session agents: [{string.Join(", ", agents)}]");
            _live.Log($"L13 tools: [{string.Join(", ", names)}]");

            // Assert: no agent definition and no built-in sub-agent reached the session, and the
            // tool set is exactly the supplied tools
            Assert.Empty(agents);
            Assert.Equal(_live.ReadTools.Select(tool => tool.Name).Order(), names.Order());
        }
        finally
        {
            await session.DisposeAsync();
            await client.DeleteSessionAsync(session.SessionId, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Builds an agent over the run's client, confined to the read tools and the sandbox.
    /// </summary>
    /// <returns>The agent.</returns>
    private AIAgent CreateAgent() =>
        CopilotAgentFactory.Create(
            _live.Client,
            _live.ReadTools,
            "You are a concise assistant confined to the permitted locations.",
            name: "live-agent",
            model: _live.Model,
            workingDirectory: _live.Sandbox);

    /// <summary>
    ///     Starts a second client, in CodingAgent mode, whose data directory under the run's root
    ///     configures one MCP server that is not GitHub's, and confirms the runtime discovers it.
    /// </summary>
    /// <remarks>
    ///     The data directory is created under the run's root and never the user's own Copilot home,
    ///     and the server's command names a file that does not exist, so nothing runs even if the
    ///     runtime starts it. The client is spawned out of process rather than hosted in-process
    ///     beside the run's own client. Discovery finding the server is the control that keeps L12a
    ///     from passing vacuously on a fixture the runtime ignores.
    /// </remarks>
    /// <param name="name">The data directory's folder name under the run's root.</param>
    /// <returns>The started client. The caller disposes it.</returns>
    private async Task<CopilotClient> StartAmbientMcpClientAsync(string name)
    {
        var home = Path.Combine(_live.Root, name);
        Assert.StartsWith(_live.Root, home, StringComparison.Ordinal);
        Directory.CreateDirectory(home);
        var server = new Dictionary<string, object>
        {
            ["type"] = "local",
            ["command"] = Path.Combine(_live.Root, "no-such-server", "server.exe"),
            ["args"] = Array.Empty<string>(),
            ["tools"] = new[] { "*" },
        };
        await File.WriteAllTextAsync(
            Path.Combine(home, "mcp-config.json"),
            JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["mcpServers"] = new Dictionary<string, object> { [AmbientMcpProbe] = server },
            }),
            TestContext.Current.CancellationToken);

        var client = new CopilotClient(new CopilotClientOptions
        {
            Mode = CopilotClientMode.CodingAgent,
            Runtime = RuntimeConnection.ForSpawnedStdio(baseDirectory: home),
            GitHub = new ClientGitHubOptions { UseLoggedInUser = true },
        });
        try
        {
            await CopilotClientStarter.StartAsync(client, TestContext.Current.CancellationToken);
            var discovered = await client.Rpc.Mcp.DiscoverAsync(_live.Sandbox, cancellationToken: TestContext.Current.CancellationToken);
            _live.Log($"L12 discovered on {name}: [{string.Join(", ", discovered.Servers.Select(server => $"{server.Name}({server.Source},enabled={server.Enabled})"))}]");
            Assert.Contains(discovered.Servers, server => server.Name == AmbientMcpProbe);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    ///     Starts a second client, in CodingAgent mode, whose data directory under the run's root
    ///     defines one user agent, and confirms the runtime discovers it and the workspace's project
    ///     agent.
    /// </summary>
    /// <remarks>
    ///     The data directory is created under the run's root and never the user's own Copilot home,
    ///     and the client is spawned out of process rather than hosted in-process beside the run's
    ///     own client. Discovery finding both definitions is the control that keeps L13 from passing
    ///     vacuously on a fixture the runtime ignores.
    /// </remarks>
    /// <param name="name">The data directory's folder name under the run's root.</param>
    /// <param name="workspace">The workspace whose project agent discovery must also find.</param>
    /// <returns>The started client. The caller disposes it.</returns>
    private async Task<CopilotClient> StartAgentDefinitionsClientAsync(string name, string workspace)
    {
        var home = Path.Combine(_live.Root, name);
        Assert.StartsWith(_live.Root, home, StringComparison.Ordinal);
        Directory.CreateDirectory(Path.Combine(home, "agents"));
        await File.WriteAllTextAsync(
            Path.Combine(home, "agents", $"{UserAgentProbe}.agent.md"),
            AgentDefinition(UserAgentProbe),
            TestContext.Current.CancellationToken);

        var client = new CopilotClient(new CopilotClientOptions
        {
            Mode = CopilotClientMode.CodingAgent,
            Runtime = RuntimeConnection.ForSpawnedStdio(baseDirectory: home),
            GitHub = new ClientGitHubOptions { UseLoggedInUser = true },
        });
        try
        {
            await CopilotClientStarter.StartAsync(client, TestContext.Current.CancellationToken);
            var discovered = await client.Rpc.Agents.DiscoverAsync(
                null,
                [workspace],
                cancellationToken: TestContext.Current.CancellationToken);
            _live.Log($"L13 discovered on {name}: [{string.Join(", ", discovered.Agents.Select(agent => $"{agent.Name}({agent.Source})"))}]");
            Assert.Contains(discovered.Agents, agent => agent.Name == UserAgentProbe);
            Assert.Contains(discovered.Agents, agent => agent.Name == WorkspaceAgentProbe);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    ///     Composes a minimal agent definition file.
    /// </summary>
    /// <param name="name">The agent's name.</param>
    /// <returns>The definition's content: front matter naming the agent, and its instructions.</returns>
    private static string AgentDefinition(string name) =>
        $"---\nname: {name}\ndescription: Probe agent.\n---\nReply with PROBE.\n";

    /// <summary>
    ///     Describes a session's effective MCP configuration for the log.
    /// </summary>
    /// <param name="servers">The configured servers.</param>
    /// <returns>One entry per server: its name, source and enablement.</returns>
    private static string Describe(IEnumerable<McpConfiguredServer> servers) =>
        string.Join(", ", servers.Select(server => $"{server.Name}({server.Source},enabled={server.Enabled})"));

    /// <summary>
    ///     Deletes an agent conversation's persisted runtime session, as a host that is finished with
    ///     it does.
    /// </summary>
    /// <param name="session">The conversation.</param>
    /// <returns>A task that completes when the session is deleted.</returns>
    private async Task DeleteAsync(CopilotAgentSession session)
    {
        if (session.SessionId is { } sessionId)
        {
            await _live.Client.DeleteSessionAsync(sessionId, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Creates a session from a configuration and returns the names of the tools the runtime says
    ///     it exposes, deleting the session afterwards.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <returns>The exposed tool names.</returns>
    private async Task<List<string>> CurrentToolNamesAsync(CreateSessionOptions config)
    {
        var session = await _live.Client.CreateSessionAsync(config, TestContext.Current.CancellationToken);
        try
        {
            var metadata = await session.Rpc.Tools.GetCurrentMetadataAsync(TestContext.Current.CancellationToken);
            var names = (metadata.Tools ?? []).Select(tool => tool.Name).ToList();
            _live.Log($"Runtime tool set for [{string.Join(", ", config.Tools.AvailableTools ?? [])}]: [{string.Join(", ", names)}]");
            return names;
        }
        finally
        {
            await session.DisposeAsync();
            await _live.Client.DeleteSessionAsync(session.SessionId, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Creates a session from a configuration, takes the given turns on it, and deletes it,
    ///     recording every event the runtime dispatched.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="prompts">The prompts, one per turn.</param>
    /// <returns>The events and the answers.</returns>
    private async Task<LiveRun> RunTurnsAsync(CreateSessionOptions config, params string[] prompts)
    {
        var events = new ConcurrentQueue<SessionEvent>();
        config.Events.OnEvent = evt =>
        {
            events.Enqueue(evt);
            if (evt is ToolExecutionStartEvent or ToolExecutionCompleteEvent or SessionErrorEvent)
            {
                _live.Log($"  [{evt.Type}] {Shorten(evt.ToJson(), 400)}");
            }
        };

        var answers = new List<string>();
        var session = await _live.Client.CreateSessionAsync(config, TestContext.Current.CancellationToken);
        try
        {
            foreach (var prompt in prompts)
            {
                _live.Log($"  >> {prompt}");
                var answer = await session.SendAndWaitAsync(prompt, TurnTimeout, TestContext.Current.CancellationToken);
                answers.Add(answer?.Data?.Content ?? string.Empty);
                _live.Log($"  << {Shorten(answers[^1], 600)}");
            }
        }
        finally
        {
            await session.DisposeAsync();
            await _live.Client.DeleteSessionAsync(session.SessionId, TestContext.Current.CancellationToken);
        }

        return new LiveRun([.. events], answers);
    }

    /// <summary>
    ///     Shortens text for the log.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="max">The longest text kept whole.</param>
    /// <returns>The text, shortened and on one line.</returns>
    private static string Shorten(string text, int max) =>
        (text.Length <= max ? text : $"{text[..max]}...(+{text.Length - max})").ReplaceLineEndings(" | ");

    /// <summary>
    ///     What one scripted run on the real runtime produced.
    /// </summary>
    /// <param name="Events">Every event the runtime dispatched, in order.</param>
    /// <param name="Answers">The final answer of each turn.</param>
    private sealed record LiveRun(IReadOnlyList<SessionEvent> Events, IReadOnlyList<string> Answers);
}
