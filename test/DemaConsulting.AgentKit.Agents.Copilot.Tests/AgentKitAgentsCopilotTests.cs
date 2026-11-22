using DemaConsulting.AgentKit.Agents.Copilot;
using DemaConsulting.AgentKit.Core;
using Microsoft.Copilot;
using Microsoft.Copilot.Rpc;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     System-level integration tests for the AgentKitAgentsCopilot system.
/// </summary>
/// <remarks>
///     These tests exercise the behavioral guarantees the package exists for without a live
///     Copilot CLI: the allow-list is derived from the supplied tools so the two cannot drift
///     apart (the safety-critical property of the whole increment), the default permission
///     handler approves exactly the supplied tools while rejecting everything else, and a real
///     <c>CompactingAgentSession</c> runs a conversation to a rotation on a scripted Copilot
///     runtime. The session configuration is a plain constructable object and the runtime is
///     reached through the package's own turn-channel seam, so all of it is asserted offline.
/// </remarks>
public class AgentKitAgentsCopilotTests
{
    /// <summary>
    ///     The safety-critical assertion: <c>AvailableTools</c> is derived from the same collection
    ///     as <c>Tools</c>, so the published tool set and the allow-list that suppresses the
    ///     runtime's built-in tools cannot diverge. A drift here would silently re-admit shell,
    ///     fetch, or file-editing tools an application meant to withhold.
    /// </summary>
    [Fact]
    public void AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools()
    {
        // Arrange: a set of supplied tools
        var tools = new List<AIFunction>
        {
            MakeTool("doc_read"),
            MakeTool("doc_write"),
            MakeTool("doc_list"),
        };

        // Act: build the session configuration the factory would hand to the runtime
        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        // Assert: the allow-list is exactly the names of the published tools, in the same order,
        // and the published tool set is the same size — the two are derived from one collection
        var expectedNames = tools.Select(tool => tool.Name).ToList();
        Assert.Equal(expectedNames.Select(name => $"custom:{name}"), config.Tools.AvailableTools);
        Assert.Equal(tools.Count, config.Tools.Definitions!.Count);
        Assert.Equal(expectedNames, config.Tools.Definitions.Select(tool => tool.Name).ToList());
    }

    /// <summary>
    ///     Proves the default permission handler approves a supplied tool requested by its exact
    ///     name.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_DefaultPermissionHandler_SuppliedTool_IsApproved()
    {
        var tools = new List<AIFunction> { MakeTool("doc_read") };
        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        var decision = await Decide(config, MakeCustomToolRequest("doc_read"));

        Assert.Equal(PermissionDecision.ApproveOnce().Kind, decision.Kind);
    }

    /// <summary>
    ///     Proves the default permission handler rejects a custom tool that was not supplied.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_DefaultPermissionHandler_UnlistedCustomTool_IsRejected()
    {
        var tools = new List<AIFunction> { MakeTool("doc_read") };
        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        var decision = await Decide(config, MakeCustomToolRequest("doc_delete"));

        Assert.Equal(PermissionDecision.Reject("x").Kind, decision.Kind);
    }

    /// <summary>
    ///     Proves the default permission handler rejects a built-in tool request — one that is not
    ///     a supplied custom tool — because it is never in the allow-list. This is the runtime-tool
    ///     suppression the package exists for, enforced at the permission boundary as well as in the
    ///     allow-list.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_DefaultPermissionHandler_BuiltInTool_IsRejected()
    {
        var tools = new List<AIFunction> { MakeTool("doc_read") };
        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        // A built-in request (shell, read, write, url, ...) arrives as something other than a
        // custom-tool request; the base type stands in for any of them here.
        var decision = await Decide(config, new PermissionRequest { Kind = "shell" });

        Assert.Equal(PermissionDecision.Reject("x").Kind, decision.Kind);
    }

    /// <summary>
    ///     Proves a conversation runs end to end on the Copilot provider session: the answer comes
    ///     back, and the occupancy the session reports is the runtime's own, out of the runtime's own
    ///     limit. Nothing about the window is supplied by the application.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Session_AnswersAndReportsTheRuntimesOccupancy()
    {
        // Arrange: a compacting session over a scripted Copilot runtime
        var runtime = new FakeCopilotRuntime(_ =>
        [
            new ScriptedTurn(
                [CopilotEvents.Usage(1_500, 64_000, 900), CopilotEvents.Assistant("Three files changed.")],
                CopilotEvents.Assistant("Three files changed.")),
        ]);

        await using var session = await CompactingAgentSession.CreateAsync(
            new AgentSessionOptions(new RecordingSummarizer(), "You are a research assistant."),
            new CopilotProviderSessionFactory(runtime.Opener, model: null),
            TestContext.Current.CancellationToken);

        // Act
        var response = await session.SendAsync("what changed?", TestContext.Current.CancellationToken);

        // Assert: the answer, and the runtime's own figures reaching the engine
        Assert.Equal("Three files changed.", response.Text);
        Assert.Equal(64_000, response.Usage.WindowTokens);
        Assert.Equal(1_500, response.Usage.UsedTokens);
        Assert.Equal(900, response.Usage.ConversationTokens);
        Assert.False(response.RotationOccurred);
    }

    /// <summary>
    ///     The scenario this whole increment exists for: the session engine runs on Copilot, rotates
    ///     when the runtime says its window is filling, and seeds the replacement with a consolidated
    ///     record — all driven by figures the runtime reported rather than any the application
    ///     supplied.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Session_RotatesOnTheRuntimesUsage_AndSeedsTheReplacement()
    {
        // Arrange: a first session whose one turn fills the window past the rotation threshold, and a
        // replacement that reports a comfortable one. The window is small so one turn crosses it.
        var runtime = new FakeCopilotRuntime(index => index == 0
            ? [Turn("first answer", currentTokens: 900, tokenLimit: 1_000)]
            : [Turn("replacement answer", currentTokens: 100, tokenLimit: 1_000)]);

        var summarizer = new RecordingSummarizer();
        await using var session = await CompactingAgentSession.CreateAsync(
            new AgentSessionOptions(summarizer, "You are a research assistant.", verbatimTurns: 1),
            new CopilotProviderSessionFactory(runtime.Opener, model: null),
            TestContext.Current.CancellationToken);

        // Act: one turn, which the runtime reports as having filled the window
        var response = await session.SendAsync("first question", TestContext.Current.CancellationToken);

        // Assert: a rotation happened, a consolidation was performed out of session, the replacement
        // was created carrying the consolidated record, and the superseded session was released
        Assert.True(response.RotationOccurred);
        Assert.Equal(1, session.RotationCount);
        Assert.Single(summarizer.Requests);
        Assert.Contains("first question", summarizer.Requests[0].Material, StringComparison.Ordinal);

        Assert.Equal(2, runtime.Configs.Count);

        // The replacement's system message carries the application's instructions and nothing else.
        // The consolidated record is untrusted material - it can contain whatever a tool read off
        // disk - so it travels on the conversation channel rather than the system one, and it does
        // so on the next message rather than in a request of its own.
        var seeded = runtime.Configs[1].SystemMessage!.Content!;
        Assert.Equal("You are a research assistant.", seeded);
        Assert.DoesNotContain(CopilotProviderSessionFactory.RecordOpening, seeded, StringComparison.Ordinal);

        // Sending on the replacement carries the record ahead of the caller's own message, once
        await session.SendAsync("second question", TestContext.Current.CancellationToken);
        var firstPrompt = runtime.Channels[1].Prompts[0];
        Assert.Contains(CopilotProviderSessionFactory.RecordOpening, firstPrompt, StringComparison.Ordinal);
        Assert.Contains(RecordingSummarizer.Record, firstPrompt, StringComparison.Ordinal);
        Assert.EndsWith("second question", firstPrompt, StringComparison.Ordinal);

        // The replacement has sent nothing, so it occupies nothing — which is what stops a
        // rotation cascading. A replacement that inherited its predecessor's figure would cross the
        // threshold again on adoption and rotate forever.
        Assert.Equal(0, response.Usage.UsedTokens);
        Assert.Equal(0, response.Usage.ConversationTokens);

        Assert.Equal(1, runtime.Channels[0].DisposeCount);
        Assert.Equal(0, runtime.Channels[1].DisposeCount);
    }

    /// <summary>
    ///     Proves the runtime's built-in tools are suppressed on the session path exactly as they are
    ///     on the agent path: every session a rotation creates carries the default-safe permission
    ///     handler, which rejects a built-in request because none is one of the seeded tools.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Session_BuiltInToolRequest_IsRejectedOnTheSessionPath()
    {
        // Arrange
        var seed = new ProviderSessionSeed(null, [MakeTool("doc_read")], []);
        var config = CopilotProviderSessionFactory.BuildProviderSessionConfig(seed, _ => { }, model: null);

        // Act: a built-in request arrives as something other than a custom-tool request
        var decision = await Decide(config, new PermissionRequest { Kind = "shell" });

        // Assert
        Assert.Equal(PermissionDecision.Reject("x").Kind, decision.Kind);
    }

    /// <summary>
    ///     Proves every session AgentKit's engine drives on Copilot is created with the engine
    ///     path's infinite-session configuration — the first and every replacement — so no rotation
    ///     can silently hand the conversation back to the runtime's compactor at the runtime's own
    ///     threshold. The value that configuration carries is asserted where it is set, in
    ///     <c>CopilotAgentFactoryTests</c>.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Session_RuntimeCompactionIsHeldClearOfRotationOnEverySessionItBuilds()
    {
        // Arrange: a run that rotates once, so both a first session and a replacement are created
        var runtime = new FakeCopilotRuntime(index => index == 0
            ? [Turn("first answer", currentTokens: 990, tokenLimit: 1_000)]
            : [Turn("replacement answer", currentTokens: 100, tokenLimit: 1_000)]);

        await using var session = await CompactingAgentSession.CreateAsync(
            new AgentSessionOptions(new RecordingSummarizer(), "be concise", verbatimTurns: 1),
            new CopilotProviderSessionFactory(runtime.Opener, model: null),
            TestContext.Current.CancellationToken);

        // Act
        await session.SendAsync("a question", TestContext.Current.CancellationToken);

        // Assert: the threshold is what the runtime honors and therefore what holds it off, so that
        // is what every session must carry. The flag is asserted too, but only as stated intent -
        // it was measured to be inert, so a test that checked it alone would pass against a session
        // the runtime was free to compact at its default of 0.80, just below the engine's 0.70.
        Assert.Equal(2, runtime.Configs.Count);
        Assert.All(
            runtime.Configs,
            config =>
            {
                Assert.True(config.State.InfiniteSessions!.BackgroundCompactionThreshold > 0.90);
                Assert.False(config.State.InfiniteSessions.Enabled);
            });
    }

    /// <summary>
    ///     Proves an agent built by the factory runs a conversation on the Copilot runtime end to end:
    ///     the session it creates is confined to the supplied tools, the tool traffic and the answer
    ///     stream back as they happen, and the session is released when the run ends.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Agent_RunsAndStreamsOnTheRuntime()
    {
        // Arrange: an agent over a scripted runtime whose turn calls a tool and streams its answer
        var runtime = new FakeCopilotRuntime(_ =>
        [
            new ScriptedTurn(
            [
                CopilotEvents.ToolStart("call-1", "doc_read"),
                CopilotEvents.ToolComplete("call-1", "CODEWORD: PERIWINKLE-42"),
                CopilotEvents.Delta("The codeword is ", "m1"),
                CopilotEvents.Delta("PERIWINKLE-42.", "m1"),
                CopilotEvents.Assistant("The codeword is PERIWINKLE-42.", messageId: "m1"),
            ]),
        ]);
        var agent = CopilotAgentFactory.CreateAgent(
            [MakeTool("doc_read")],
            instructions: "be concise",
            onPermissionRequest: null,
            name: "document-assistant",
            model: null,
            workingDirectory: null,
            runtime.Opener,
            runtime.Resumer);

        // Act
        var updates = new List<Microsoft.Agents.AI.AgentResponseUpdate>();
        await foreach (var update in agent.RunStreamingAsync("what is the codeword?", cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        // Assert: confined, streamed in order, released
        Assert.Equal(["custom:doc_read"], Assert.Single(runtime.Configs).Tools.AvailableTools);
        var contents = updates.SelectMany(update => update.Contents).ToList();
        Assert.IsType<FunctionCallContent>(contents[0]);
        Assert.IsType<FunctionResultContent>(contents[1]);
        Assert.Equal(
            "The codeword is PERIWINKLE-42.",
            string.Concat(contents.OfType<TextContent>().Select(text => text.Text)));
        Assert.Equal(1, Assert.Single(runtime.Channels).DisposeCount);
    }

    /// <summary>
    ///     Proves a vision-only application lists only vision models from the service's listing,
    ///     selects one with an effort and a tier, and runs its agent on exactly that choice.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsCopilot_ModelSelection_AgentRunsOnTheSelectedModelAndSettings()
    {
        // Arrange: a listing with the automatic choice, a vision model and a text-only model
        const Core.ModelCapabilities required = Core.ModelCapabilities.Vision | Core.ModelCapabilities.ToolCalls;
        var textOnly = CopilotModelCatalogTests.Haiku();
        textOnly.Id = "text-only";
        textOnly.Capabilities.Supports!.Vision = null;
        var catalog = new CopilotModelCatalog(_ => Task.FromResult<IList<Model>>(
            [new Model { Id = "auto" }, CopilotModelCatalogTests.Haiku(), textOnly]));
        var runtime = new FakeCopilotRuntime(_ => [new ScriptedTurn([CopilotEvents.Assistant("ok")])]);

        // Act: list for the picker, select at session start, and run
        var listed = await catalog.ListAsync(new ModelQuery { Require = required }, TestContext.Current.CancellationToken);
        var selection = await catalog.SelectAsync(
            new ModelSettings(listed[0].Id) { ReasoningEffort = "high", ContextTier = "long_context" },
            required,
            TestContext.Current.CancellationToken);
        var agent = CopilotAgentFactory.CreateAgent(
            [MakeTool("doc_read")],
            instructions: null,
            onPermissionRequest: null,
            name: null,
            model: null,
            workingDirectory: null,
            runtime.Opener,
            runtime.Resumer,
            selection: selection);
        await agent.RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);

        // Assert: only the vision model was offered, and the session ran on the user's choices
        Assert.Equal(["claude-haiku-5.5"], listed.Select(model => model.Id));
        Assert.Equal(HostCapabilities.Vision, selection.HostCapabilities);
        var config = Assert.Single(runtime.Configs);
        Assert.Equal("claude-haiku-5.5", config.Model);
        Assert.Equal("high", config.Inference?.ReasoningEffort);
        Assert.Equal(ContextTier.LongContext, config.Inference?.ContextTier);
    }

    /// <summary>
    ///     Proves a conversation serialized from one agent resumes on another under the same
    ///     confinement: the second agent reopens the same runtime session, with the allow-list and
    ///     the default-deny handler re-applied rather than trusted to the runtime's memory.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Agent_SerializedSession_ResumesUnderTheSameConfinement()
    {
        // Arrange: a conversation that ran once on a first agent, then was serialized
        var runtime = new FakeCopilotRuntime(_ => [new ScriptedTurn([CopilotEvents.Assistant("noted")])]);
        IList<AIFunction> tools = [MakeTool("doc_read")];
        var first = CopilotAgentFactory.CreateAgent(
            tools, null, null, null, null, null, runtime.Opener, runtime.Resumer);
        var session = await first.CreateSessionAsync(TestContext.Current.CancellationToken);
        await first.RunAsync("remember the codeword", session, cancellationToken: TestContext.Current.CancellationToken);
        var serialized = await first.SerializeSessionAsync(session, cancellationToken: TestContext.Current.CancellationToken);

        // Act: a second agent restores the conversation and runs on it
        var second = CopilotAgentFactory.CreateAgent(
            tools, null, null, null, null, null, runtime.Opener, runtime.Resumer);
        var restored = await second.DeserializeSessionAsync(serialized, cancellationToken: TestContext.Current.CancellationToken);
        await second.RunAsync("what was it?", restored, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the same runtime session, resumed with the confinement re-applied
        var (sessionId, config) = Assert.Single(runtime.Resumes);
        Assert.Equal(runtime.Channels[0].SessionId, sessionId);
        Assert.Equal(["custom:doc_read"], config.Tools.AvailableTools);
        var decision = await config.Permissions.OnRequest!(new PermissionRequest { Kind = "shell" }, null!);
        Assert.Equal(PermissionDecision.Reject("x").Kind, decision.Kind);
    }

    /// <summary>
    ///     Proves an application that composes a tool under the name of one of the runtime's built-in
    ///     tools is told so when it builds the agent, rather than when the runtime refuses the session
    ///     on the first run.
    /// </summary>
    [Fact]
    public void AgentKitAgentsCopilot_BuildSessionConfig_BuiltInToolName_IsRefused()
    {
        // Arrange: a composed tool set in which one tool is named like the runtime's file viewer
        var runtime = new FakeCopilotRuntime();
        IList<AIFunction> tools = [MakeTool("doc_read"), MakeTool("view")];

        // Act
        var failure = Assert.Throws<ArgumentException>(() => CopilotAgentFactory.CreateAgent(
            tools, null, null, null, null, null, runtime.Opener, runtime.Resumer));

        // Assert: refused by name, and no session was ever requested
        Assert.Contains("'view'", failure.Message, StringComparison.Ordinal);
        Assert.Empty(runtime.Configs);
    }

    /// <summary>
    ///     Proves every session the package builds — an agent's, created and resumed, a compacting
    ///     session's, and a consolidation's — switches off the runtime's diversion of a large tool
    ///     result to a temporary file no confined tool could read.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_EverySession_DeliversToolResultsInline()
    {
        // Arrange: one runtime serving an agent, a compacting session and a consolidation
        var runtime = new FakeCopilotRuntime(_ => [new ScriptedTurn([CopilotEvents.Usage(100, 64_000), CopilotEvents.Assistant("ok")], CopilotEvents.Assistant("ok"))]);
        var agent = CopilotAgentFactory.CreateAgent(
            [MakeTool("doc_read")], null, null, null, null, null, runtime.Opener, runtime.Resumer);
        var conversation = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Act: an agent's first and second run, an engine session's creation, and a consolidation
        await agent.RunAsync("first", conversation, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("second", conversation, cancellationToken: TestContext.Current.CancellationToken);
        var engineSession = await new CopilotProviderSessionFactory(runtime.Opener, model: null)
            .CreateAsync(new ProviderSessionSeed(null, [MakeTool("doc_read")], []), TestContext.Current.CancellationToken);
        await engineSession.DisposeAsync();

        await new CopilotSummarizer(runtime.Opener, model: null).ConsolidateAsync(
            new ConsolidationRequest(1, "USER: hello", ConsolidationPrompt.MediumInstruction),
            TestContext.Current.CancellationToken);

        // Assert: every configuration the runtime was handed keeps results inline
        Assert.Equal(3, runtime.Configs.Count);
        Assert.All(runtime.Configs, config => Assert.False(config.Tools.LargeOutput!.Enabled));
        Assert.False(Assert.Single(runtime.Resumes).Config.Tools.LargeOutput!.Enabled);
    }

    /// <summary>
    ///     Proves a host whose bundled Copilot runtime cannot start is told why — the runtime's own
    ///     error and the missing direct SDK reference that most often causes it — with the original
    ///     failure kept.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Runtime_FailedStart_IsExplained()
    {
        // Arrange: a start that fails the way the SDK does when no runtime was provisioned
        var cause = new InvalidOperationException(
            $"Copilot runtime wrapper not found at '{Path.Combine(AppContext.BaseDirectory, "runtimes", "native", "copilot-runtime")}'.");

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.StartClientAsync(
            _ => Task.FromException(cause),
            new CopilotRuntimeLaunch(CopilotRuntimeSource.Bundled, null, null),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(cause.Message, failure.Message, StringComparison.Ordinal);
        Assert.Contains("PackageReference to Microsoft.Copilot.SDK", failure.Message, StringComparison.Ordinal);
        Assert.Same(cause, failure.InnerException);
    }

    /// <summary>
    ///     Proves a host that sets no runtime source runs the Copilot CLI installed on the machine: the
    ///     CLI that discovery finds is version-checked and spawned, not the SDK's bundled runtime.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Runtime_DefaultsToTheInstalledCli()
    {
        // Arrange: a machine with a supported CLI on PATH, and default options
        var machine = new FakeCopilotCliMachine();
        var cli = machine.AddFile(Path.Combine("/fake/bin", "copilot"));
        var dataDirectory = Path.Combine(Path.GetTempPath(), "agentkit-copilot-system-tests", Guid.NewGuid().ToString("N"));
        var options = new CopilotRuntimeOptions(dataDirectory);

        // Act
        var launch = await CopilotRuntime.ResolveLaunchAsync(
            options,
            dataDirectory,
            new System.Collections.Hashtable(),
            machine.Context(CopilotCliPlatform.Linux, "/fake/bin"),
            machine.RunAsync,
            TestContext.Current.CancellationToken);
        var clientOptions = CopilotRuntime.BuildClientOptions(options, dataDirectory, new System.Collections.Hashtable(), launch.CliPath);

        // Assert
        Assert.Equal(CopilotRuntimeSource.InstalledCli, options.RuntimeSource);
        Assert.Equal(cli, launch.CliPath);
        Assert.Equal(["--no-auto-update", "--version"], Assert.Single(machine.Started).ArgumentList);
        Assert.Equal(cli, Assert.IsType<SpawnedStdioRuntimeConnection>(clientOptions.Runtime).Path);
    }

    /// <summary>
    ///     Proves a host whose installed Copilot CLI is older than the minimum is refused before any
    ///     runtime starts, with the version found, the version required, how to check the version
    ///     AgentKit runs and how to upgrade.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Runtime_UnsupportedCli_IsRefusedWithUpgradeGuidance()
    {
        // Arrange: the installed CLI runs 1.0.106
        var machine = new FakeCopilotCliMachine
        {
            Result = new(0, "GitHub Copilot CLI 1.0.106.\n", string.Empty, TimedOut: false),
        };
        var cli = machine.AddFile(Path.Combine("/fake/bin", "copilot.exe"));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.ResolveLaunchAsync(
            new CopilotRuntimeOptions("copilot-runtime"),
            Path.Combine("/fake", "data"),
            new System.Collections.Hashtable(),
            machine.Context(CopilotCliPlatform.Windows, "/fake/bin"),
            machine.RunAsync,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(cli, failure.Message, StringComparison.Ordinal);
        Assert.Contains("version 1.0.106", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"{CopilotCli.MinimumVersion} or later", failure.Message, StringComparison.Ordinal);
        Assert.Contains("copilot --no-auto-update --version", failure.Message, StringComparison.Ordinal);
        Assert.Contains("winget upgrade GitHub.Copilot", failure.Message, StringComparison.Ordinal);
        Assert.Contains("CopilotRuntimeSource.Bundled", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a runtime that started but is not signed in is refused with the ways to sign it in,
    ///     whichever source it came from.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Runtime_Unauthenticated_IsRefusedWithSignInGuidance()
    {
        // Arrange
        var status = new GetStatusResponse { Version = "1.0.113", ProtocolVersion = 4 };
        var authentication = new GetAuthStatusResponse { IsAuthenticated = false, StatusMessage = "Not authenticated" };

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.VerifyStartedAsync(
            _ => Task.FromResult(status),
            _ => Task.FromResult(authentication),
            new CopilotSignInPlan(),
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("not signed in", failure.Message, StringComparison.Ordinal);
        Assert.Contains("gh auth login", failure.Message, StringComparison.Ordinal);
        Assert.Contains("GH_TOKEN", failure.Message, StringComparison.Ordinal);
        Assert.Contains("copilot login", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the runtime signs in from the first source that applies — the host's token, then a
    ///     token in the environment, then the user's Copilot CLI sign-in, then the runtime's own
    ///     fallbacks — with the GitHub host that belongs to the source, and that from the Copilot CLI
    ///     the confined runtime receives only the token and the account's host.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Runtime_SignIn_FollowsTheSourceOrderWithTheSourcesHost()
    {
        // Arrange: a user signed in to the Copilot CLI on a GitHub Enterprise Cloud host
        using var machine = new SignInMachine();
        var none = new System.Collections.Hashtable { ["PATH"] = "/usr/bin" };
        var environmentToken = new System.Collections.Hashtable { ["PATH"] = "/usr/bin", ["GH_TOKEN"] = "gho_environment" };

        // Act
        var explicitToken = await machine.ResolveAsync(new CopilotRuntimeOptions(machine.Data, gitHubToken: "gho_host", gitHubHost: "github.com"), environmentToken);
        var environment = await machine.ResolveAsync(new CopilotRuntimeOptions(machine.Data), environmentToken);
        var copilotCli = await machine.ResolveAsync(new CopilotRuntimeOptions(machine.Data), none);
        var turnedOff = await machine.ResolveAsync(new CopilotRuntimeOptions(machine.Data, useCopilotCliSignIn: false), none);
        var clientOptions = CopilotRuntime.BuildClientOptions(new CopilotRuntimeOptions(machine.Data), machine.Data, none, "/usr/bin/copilot", copilotCli);

        // Assert: the order
        Assert.Equal(CopilotSignInSource.GitHubToken, explicitToken.Source);
        Assert.Equal("github.com", explicitToken.HostName);
        Assert.Equal(CopilotSignInSource.EnvironmentVariable, environment.Source);
        Assert.Null(environment.Token);
        Assert.Equal(CopilotSignInSource.CopilotCli, copilotCli.Source);
        Assert.Null(turnedOff.Source);
        Assert.Equal(1, machine.HelpersCreated);

        // Assert: the confined runtime gets the Copilot CLI's token and host, in Empty mode, nothing else
        Assert.Equal(SignInMachine.Token, clientOptions.GitHub.Token);
        Assert.Equal(CopilotClientMode.Empty, clientOptions.Mode);
        var connection = Assert.IsType<SpawnedStdioRuntimeConnection>(clientOptions.Runtime);
        Assert.Equal("example.ghe.com", connection.Environment!["COPILOT_GH_HOST"]);
        Assert.Equal(machine.Data, connection.BaseDirectory);
        Assert.Equal("""{"loggedInUsers":[{"host":"https://example.ghe.com","login":"octo_user"}],"lastLoggedInUser":{"host":"https://example.ghe.com","login":"octo_user"}}""", machine.HelperConfiguration);
    }

    /// <summary>
    ///     Proves a runtime that no source signs in is refused with what each source found, in order,
    ///     and without any token.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Runtime_NoSourceSignsIn_RefusalListsWhatEachSourceFound()
    {
        // Arrange: the Copilot CLI sign-in turned off, no token anywhere, no gh on PATH
        using var machine = new SignInMachine();
        var plan = await machine.ResolveAsync(
            new CopilotRuntimeOptions(machine.Data, useCopilotCliSignIn: false),
            new System.Collections.Hashtable { ["PATH"] = "/usr/bin" });
        var status = new GetStatusResponse { Version = "1.0.114", ProtocolVersion = 4 };
        var authentication = new GetAuthStatusResponse { IsAuthenticated = false, StatusMessage = "Not authenticated" };

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotRuntime.VerifyStartedAsync(
            _ => Task.FromResult(status),
            _ => Task.FromResult(authentication),
            plan,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("1. gitHubToken / gitHubTokenProvider: not supplied.", failure.Message, StringComparison.Ordinal);
        Assert.Contains("2. COPILOT_GITHUB_TOKEN, GH_TOKEN, GITHUB_TOKEN: none set.", failure.Message, StringComparison.Ordinal);
        Assert.Contains("3. Copilot CLI sign-in: turned off by the host", failure.Message, StringComparison.Ordinal);
        Assert.Contains("was not found on PATH", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SignInMachine.Token, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the runtime AgentKit owns is confined before it starts, whatever the host's
    ///     environment holds and whichever executable runs: Empty mode, the installed CLI or the
    ///     bundled runtime spawned over stdio on the prepared data directory with the identical
    ///     environment, none of the host's Copilot configuration variables beyond the pass-through set,
    ///     and no client-level channel of injected capability.
    /// </summary>
    [Fact]
    public void AgentKitAgentsCopilot_Runtime_ClientOptionsAreConfined()
    {
        // Arrange: a hostile host environment and a fresh data directory
        var dataDirectory = Path.Combine(Path.GetTempPath(), "agentkit-copilot-system-tests", Guid.NewGuid().ToString("N"));
        var cli = Path.Combine(Path.GetTempPath(), "copilot-cli", "copilot");
        var host = new System.Collections.Hashtable
        {
            ["COPILOT_CLI_PATH"] = "/elsewhere/copilot",
            ["COPILOT_ALLOW_ALL"] = "1",
            ["COPILOT_SKILLS_DIRS"] = "/shared/skills",
            ["GITHUB_COPILOT_MCP_JSON_FROM_INPUT"] = "{}",
            ["GH_TOKEN"] = "gh",
        };

        try
        {
            // Act
            var prepared = CopilotRuntime.PrepareDataDirectory(dataDirectory);
            var installed = CopilotRuntime.BuildClientOptions(new CopilotRuntimeOptions(dataDirectory), prepared, host, cli);
            var bundled = CopilotRuntime.BuildClientOptions(
                new CopilotRuntimeOptions(dataDirectory, runtimeSource: CopilotRuntimeSource.Bundled),
                prepared,
                host);

            // Assert
            Assert.Equal(cli, Assert.IsType<SpawnedStdioRuntimeConnection>(installed.Runtime).Path);
            Assert.Null(Assert.IsType<SpawnedStdioRuntimeConnection>(bundled.Runtime).Path);
            foreach (var options in new[] { installed, bundled })
            {
                var connection = Assert.IsType<SpawnedStdioRuntimeConnection>(options.Runtime);
                Assert.Equal(CopilotClientMode.Empty, options.Mode);
                Assert.Null(connection.Args);
                Assert.Equal(Path.GetFullPath(dataDirectory), connection.BaseDirectory);
                Assert.NotNull(connection.Environment);
                Assert.DoesNotContain(connection.Environment.Keys, name => name.StartsWith("COPILOT_", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(connection.Environment.Keys, name => name.StartsWith("GITHUB_COPILOT_", StringComparison.OrdinalIgnoreCase));
                Assert.Equal("gh", connection.Environment["GH_TOKEN"]);
                Assert.Empty(options.Plugins.BuiltinDirectories ?? []);
                Assert.Null(options.SessionFs);
                Assert.Null(options.Extensions.LaunchProvider);
            }
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    ///     Proves a host cannot start the runtime on a data directory another tool populated — such as
    ///     a Copilot CLI home that configures an MCP server — so no runtime ever reads it.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_Runtime_ForeignDataDirectory_IsRefused()
    {
        // Arrange: a non-empty data directory AgentKit did not create
        var dataDirectory = Path.Combine(Path.GetTempPath(), "agentkit-copilot-system-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "mcp-config.json"),
            "{\"mcpServers\":{\"probe\":{\"type\":\"local\",\"command\":\"probe\"}}}",
            TestContext.Current.CancellationToken);

        try
        {
            // Act
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => CopilotRuntime.StartAsync(new CopilotRuntimeOptions(dataDirectory), TestContext.Current.CancellationToken));

            // Assert
            Assert.Contains(dataDirectory, failure.Message, StringComparison.Ordinal);
            Assert.Contains("not created by AgentKit", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    /// <summary>
    ///     Proves every session the package opens on a real client — an engine session and an
    ///     agent's — has every ambient MCP server withheld from the model: a server the runtime
    ///     discovers, a non-GitHub one included, is disabled before the session is created; the
    ///     session's installed plugins are cleared; and a session in which a server stays enabled is
    ///     refused and abandoned rather than handed to the model.
    /// </summary>
    [Fact]
    public async Task AgentKitAgentsCopilot_EverySession_AmbientMcpServersAreClosed()
    {
        // Arrange: the configurations an engine session and an agent session are created from
        SessionOptionsBase[] configs =
        [
            CopilotAgentFactory.BuildEngineSessionConfig([MakeTool("doc_read")], instructions: null, model: null),
            CopilotAgentFactory.BuildSessionConfig([MakeTool("doc_read")], instructions: null, onPermissionRequest: null),
        ];

        foreach (var config in configs)
        {
            var steps = new List<string>();
            List<string>? disabledAtOpen = null;

            // Act: open through the production protocol, with a runtime that discovers a
            // non-GitHub server and then keeps it enabled however often it is disabled
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotSessionChannel.OpenConfinedAsync(
                config,
                _ => Task.FromResult<IEnumerable<string>>(["docs-search"]),
                _ =>
                {
                    disabledAtOpen = [.. config.Mcp.DisabledServers!];
                    return Task.FromResult("session-1");
                },
                (_, token) => CopilotSessionChannel.SealAsync(
                    _ =>
                    {
                        steps.Add("clear-plugins");
                        return Task.CompletedTask;
                    },
                    _ => Task.FromResult<IList<McpConfiguredServer>>(
                        [new McpConfiguredServer { Name = "docs-search", Enabled = true }]),
                    (name, _) =>
                    {
                        steps.Add($"disable:{name}");
                        return Task.CompletedTask;
                    },
                    token),
                _ =>
                {
                    steps.Add("abandon");
                    return ValueTask.CompletedTask;
                },
                TestContext.Current.CancellationToken));

            // Assert: named before creation, plugins cleared, disabled, and the session refused
            Assert.Contains("docs-search", disabledAtOpen!);
            Assert.Contains(CopilotAgentFactory.BuiltInGitHubMcpServer, disabledAtOpen!);
            Assert.Equal(["clear-plugins", "disable:docs-search", "abandon"], steps);
            Assert.Contains("'docs-search'", failure.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Proves a tool list composed with retention of large outputs reaches the Copilot runtime
    ///     whole on every session the package builds: the two reader tools are published, are in
    ///     the source-qualified allow-list that suppresses the runtime's own tools, and are approved
    ///     by the default permission handler, while the runtime's own diversion of large results
    ///     stays switched off.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsCopilot_ToolOutput_ReadersArePublishedAllowListedAndApproved()
    {
        // Arrange: a tool list composed with retention
        var tools = ToolOutputSpill.Apply([MakeTool("doc_read")], ToolLimits.Default).ToList();
        string[] expected = ["doc_read", ToolOutputReadTool.ToolName, ToolOutputSearchTool.ToolName];

        // Act: build the options of an agent's created and resumed sessions and of an engine session
        SessionOptionsBase[] configs =
        [
            CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null),
            CopilotAgentFactory.BuildResumeSessionConfig(tools, instructions: null, onPermissionRequest: null),
            CopilotAgentFactory.BuildEngineSessionConfig(tools, instructions: null, model: null),
        ];

        foreach (var config in configs)
        {
            var handler = config.Permissions.OnRequest;
            Assert.NotNull(handler);
            var readDecision = await handler(MakeCustomToolRequest(ToolOutputReadTool.ToolName), null!);
            var searchDecision = await handler(MakeCustomToolRequest(ToolOutputSearchTool.ToolName), null!);

            // Assert: both readers published, allow-listed and approved; no runtime diversion
            Assert.Equal(expected, config.Tools.Definitions!.Select(tool => tool.Name));
            Assert.Equal(expected.Select(name => "custom:" + name), config.Tools.AvailableTools);
            Assert.Equal(PermissionDecision.ApproveOnce().Kind, readDecision.Kind);
            Assert.Equal(PermissionDecision.ApproveOnce().Kind, searchDecision.Kind);
            Assert.False(config.Tools.LargeOutput!.Enabled);
        }
    }

    /// <summary>
    ///     Proves the tools the Copilot runtime is handed retain an oversized result and page it
    ///     back when invoked as the runtime invokes them, across the runs of one conversation.
    /// </summary>
    /// <remarks>
    ///     The scripted runtime replays events but does not invoke tools, so the tools are taken from
    ///     the very options the runtime was handed and invoked directly - the same instances, through
    ///     the same entry point, the runtime would call. The report is invoked from the session the
    ///     first run created and the reader from the session the second run resumed, so the
    ///     reference is shown to survive a run boundary.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitAgentsCopilot_ToolOutput_ConfiguredToolsDeliverPreviewAndPage()
    {
        // Arrange: an oversized tool composed with retention, behind an agent on a scripted runtime
        var report = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(ToolResult.Text(string.Join(
                "\n",
                Enumerable.Range(1, 5000).Select(n => "row " + n))))),
            "report_build",
            "Builds the full report.");
        var tools = ToolOutputSpill.Apply([report], ToolLimits.Default).ToList();
        var runtime = new FakeCopilotRuntime(_ => [new ScriptedTurn([CopilotEvents.Usage(100, 64_000), CopilotEvents.Assistant("ok")], CopilotEvents.Assistant("ok"))]);
        var agent = CopilotAgentFactory.CreateAgent(
            tools, null, null, null, null, null, runtime.Opener, runtime.Resumer);
        var conversation = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);
        await agent.RunAsync("first", conversation, cancellationToken: TestContext.Current.CancellationToken);
        await agent.RunAsync("second", conversation, cancellationToken: TestContext.Current.CancellationToken);
        var created = Assert.Single(runtime.Configs).Tools.Definitions!.OfType<AIFunction>().ToList();
        var resumed = Assert.Single(runtime.Resumes).Config.Tools.Definitions!.OfType<AIFunction>().ToList();

        // Act: invoke the report from the created session, then the reader from the resumed one
        var preview = await created.Single(tool => tool.Name == "report_build")
            .InvokeAsync(new AIFunctionArguments(), TestContext.Current.CancellationToken);
        var page = await resumed.Single(tool => tool.Name == ToolOutputReadTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["id"] = "output-1", ["startLine"] = 1, ["lineCount"] = 2 },
            TestContext.Current.CancellationToken);

        // Assert: a bounded preview naming the reference, and the page it names
        var previewText = Assert.IsType<string>(preview);
        Assert.StartsWith("[Output output-1 from report_build", previewText, StringComparison.Ordinal);
        Assert.True(previewText.Length <= ToolLimits.DefaultMaxResultCharacters);
        Assert.Equal("output-1 lines 1-2 of 5000\n   1| row 1\n   2| row 2", Assert.IsType<string>(page));
    }

    /// <summary>
    ///     Builds a scripted turn that answers and reports the runtime's occupancy.
    /// </summary>
    /// <param name="answer">The answer the turn produces.</param>
    /// <param name="currentTokens">The occupancy the runtime reports.</param>
    /// <param name="tokenLimit">The window the runtime reports.</param>
    /// <returns>The scripted turn.</returns>
    private static ScriptedTurn Turn(string answer, long currentTokens, long tokenLimit)
    {
        var message = CopilotEvents.Assistant(answer);
        return new ScriptedTurn([CopilotEvents.Usage(currentTokens, tokenLimit), message], message);
    }

    /// <summary>
    ///     Invokes the session's permission handler, asserting one is installed.
    /// </summary>
    /// <param name="config">The session whose handler to invoke.</param>
    /// <param name="request">The permission request to adjudicate.</param>
    /// <returns>The handler's decision.</returns>
    private static Task<PermissionDecision> Decide(CreateSessionOptions config, PermissionRequest request)
    {
        var handler = config.Permissions.OnRequest;
        Assert.NotNull(handler);
        return handler(request, null!);
    }

    /// <summary>
    ///     Builds a custom-tool permission request naming the given tool, filling the members the
    ///     SDK requires.
    /// </summary>
    /// <param name="toolName">The name of the tool the request concerns.</param>
    /// <returns>A custom-tool permission request.</returns>
    private static PermissionRequestCustomTool MakeCustomToolRequest(string toolName) => new()
    {
        ToolName = toolName,
        ToolCallId = "call-1",
        ToolDescription = "a tool",
    };

    /// <summary>
    ///     Builds a no-op tool carrying the given name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) =>
        AIFunctionFactory.Create(() => "ok", name);

    /// <summary>
    ///     A summarizer that consolidates deterministically and keeps what it was asked, so a
    ///     rotation in a test is a pure function of its input and the material can be asserted on.
    /// </summary>
    private sealed class RecordingSummarizer : ISummarizer
    {
        /// <summary>
        ///     The record every consolidation returns, distinctive enough to find in a seeded system
        ///     message.
        /// </summary>
        internal const string Record = "CONSOLIDATED-RECORD";

        /// <summary>
        ///     Gets the requests this summarizer was given, in order.
        /// </summary>
        internal List<ConsolidationRequest> Requests { get; } = [];

        /// <inheritdoc/>
        public Task<string> ConsolidateAsync(
            ConsolidationRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Requests.Add(request);
            return Task.FromResult(Record);
        }
    }

    /// <summary>
    ///     A machine whose user signed in to the Copilot CLI on a GitHub Enterprise Cloud host, with a
    ///     fake helper runtime that reports that account and its token. Its folders live under the system
    ///     temporary folder and are deleted when disposed.
    /// </summary>
    private sealed class SignInMachine : IDisposable, ICopilotSignInHelper
    {
        /// <summary>
        ///     The token the fake helper reports for the account.
        /// </summary>
        internal const string Token = "gho_SYSTEM_TEST_0123456789abcdef0123456";

        /// <summary>
        ///     The machine's root folder.
        /// </summary>
        private readonly string _root = Path.Combine(Path.GetTempPath(), "agentkit-copilot-system-tests", Guid.NewGuid().ToString("N"));

        /// <summary>
        ///     The home of the helper created last.
        /// </summary>
        private string? _helperHome;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SignInMachine"/> class, writing the user's
        ///     Copilot CLI configuration.
        /// </summary>
        public SignInMachine()
        {
            Directory.CreateDirectory(Path.Combine(Profile, ".copilot"));
            Directory.CreateDirectory(Data);
            File.WriteAllText(
                Path.Combine(Profile, ".copilot", "config.json"),
                """
                {
                  "mcpServers": { "probe": {} },
                  "loggedInUsers": [ { "host": "https://example.ghe.com", "login": "octo_user" } ],
                  "lastLoggedInUser": { "host": "https://example.ghe.com", "login": "octo_user" }
                }
                """);
        }

        /// <summary>
        ///     Gets the AgentKit data directory.
        /// </summary>
        internal string Data => Path.Combine(_root, "data");

        /// <summary>
        ///     Gets how many helper runtimes were created.
        /// </summary>
        internal int HelpersCreated { get; private set; }

        /// <summary>
        ///     Gets the helper home's <c>config.json</c> as the last helper found it.
        /// </summary>
        internal string? HelperConfiguration { get; private set; }

        /// <summary>
        ///     Gets the user's profile folder.
        /// </summary>
        private string Profile => Path.Combine(_root, "profile");

        /// <summary>
        ///     Resolves the sign-in on this machine.
        /// </summary>
        /// <param name="options">The host's options.</param>
        /// <param name="hostEnvironment">The host's environment.</param>
        /// <returns>The plan.</returns>
        internal Task<CopilotSignInPlan> ResolveAsync(CopilotRuntimeOptions options, System.Collections.IDictionary hostEnvironment)
        {
            var context = new CopilotSignInContext(
                new FakeCopilotCliMachine().Context(CopilotCliPlatform.Linux, "/usr/bin"),
                Profile,
                () => Directory.CreateDirectory(Path.Combine(_root, "helper-" + Guid.NewGuid().ToString("N"))).FullName,
                clientOptions =>
                {
                    HelpersCreated++;
                    _helperHome = Assert.IsType<SpawnedStdioRuntimeConnection>(clientOptions.Runtime).BaseDirectory;
                    return this;
                },
                CopilotSignIn.DeleteHelperHome,
                TimeSpan.FromMinutes(1));
            return CopilotSignIn.ResolveAsync(
                options,
                Data,
                hostEnvironment,
                new CopilotRuntimeLaunch(CopilotRuntimeSource.InstalledCli, "/usr/bin/copilot", null),
                context,
                TestContext.Current.CancellationToken);
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken) =>
            HelperConfiguration = await File.ReadAllTextAsync(Path.Combine(_helperHome!, "config.json"), cancellationToken);

        /// <inheritdoc/>
        public Task<GetAuthStatusResponse> GetAuthStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new GetAuthStatusResponse
            {
                IsAuthenticated = true,
                AuthType = "user",
                Host = "https://example.ghe.com",
                Login = "octo_user",
            });

        /// <inheritdoc/>
        public Task<IList<AccountAllUsers>> GetAllUsersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IList<AccountAllUsers>>(
            [
                new AccountAllUsers { AuthInfo = new AuthInfoUser { Host = "https://example.ghe.com", Login = "octo_user" }, Token = Token },
            ]);

        /// <inheritdoc/>
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>
        ///     Deletes the machine's folders.
        /// </summary>
        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}


