using DemaConsulting.AgentKit.Agents.Copilot;
using Microsoft.Copilot;
using Microsoft.Copilot.Rpc;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for <see cref="CopilotAgentFactory"/>: construction-time validation, the
///     host-supplied permission handler override, instruction handling, and model selection.
/// </summary>
public class CopilotAgentFactoryTests
{
    /// <summary>
    ///     Proves a null tool list is refused as a programming error.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_NullTools_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => CopilotAgentFactory.BuildSessionConfig(null!, instructions: null, onPermissionRequest: null));
    }

    /// <summary>
    ///     Proves an empty tool list is refused: an agent that publishes no tools would have an
    ///     empty allow-list, which is a defect in the host rather than a valid configuration.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_EmptyTools_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CopilotAgentFactory.BuildSessionConfig([], instructions: null, onPermissionRequest: null));
    }

    /// <summary>
    ///     Proves a null entry in the tool list is refused.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_NullToolEntry_Throws()
    {
        var tools = new List<AIFunction> { MakeTool("doc_read"), null! };

        Assert.Throws<ArgumentNullException>(
            () => CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null));
    }

    /// <summary>
    ///     Proves two tools sharing a name are refused: both the published tool set and the derived
    ///     allow-list would be ambiguous.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_DuplicateToolNames_Throws()
    {
        var tools = new List<AIFunction> { MakeTool("doc_read"), MakeTool("doc_read") };

        var ex = Assert.Throws<ArgumentException>(
            () => CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null));
        Assert.Contains("doc_read", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a null client is refused before any session is built.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_Create_NullClient_Throws()
    {
        var tools = new List<AIFunction> { MakeTool("doc_read") };

        Assert.Throws<ArgumentNullException>(() => CopilotAgentFactory.Create(null!, tools));
    }

    /// <summary>
    ///     Proves a host-supplied permission handler overrides the safe default: the exact delegate
    ///     the host supplied is the one installed on the session.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_HostHandler_IsInstalled()
    {
        var tools = new List<AIFunction> { MakeTool("doc_read") };
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>> handler =
            (_, _) => Task.FromResult(PermissionDecision.ApproveOnce());

        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: handler);

        Assert.Same(handler, config.Permissions.OnRequest);
    }

    /// <summary>
    ///     Proves supplied instructions are carried as the content of the session's system message,
    ///     and that a session built without instructions carries no system-message content.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_Instructions_CarriedOnSystemMessage()
    {
        var tools = new List<AIFunction> { MakeTool("doc_read") };

        var withInstructions =
            CopilotAgentFactory.BuildSessionConfig(tools, instructions: "be concise", onPermissionRequest: null);
        var withoutInstructions =
            CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        Assert.NotNull(withInstructions.SystemMessage);
        Assert.Equal("be concise", withInstructions.SystemMessage.Content);
        Assert.Null(withoutInstructions.SystemMessage!.Content);
    }

    /// <summary>
    ///     Proves every session the factory builds — an agent's created and resumed sessions and an
    ///     engine-driven session, with and without instructions — removes the runtime's host
    ///     environment context from the system prompt, and carries the instructions, if any, as the
    ///     message's content.
    /// </summary>
    /// <remarks>
    ///     The environment context names the working directory, the operating system, the git root,
    ///     a directory listing and the available tools. Empty mode removes it client-side; AgentKit
    ///     cannot read the client's mode, so it removes it on every session itself.
    /// </remarks>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_EnvironmentContext_IsRemoved()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read") };

        foreach (var instructions in new[] { "be concise", null })
        {
            // Act: every builder, with and without instructions
            SessionOptionsBase[] configs =
            [
                CopilotAgentFactory.BuildSessionConfig(tools, instructions, onPermissionRequest: null),
                CopilotAgentFactory.BuildResumeSessionConfig(tools, instructions, onPermissionRequest: null),
                CopilotAgentFactory.BuildEngineSessionConfig(tools, instructions, model: null),
            ];

            // Assert
            foreach (var config in configs)
            {
                var message = config.SystemMessage;
                Assert.NotNull(message);
                Assert.Equal<SystemMessageMode?>(SystemMessageMode.Customize, message.Mode);
                Assert.NotNull(message.Sections);
                Assert.Equal<SectionOverrideAction?>(
                    SectionOverrideAction.Remove,
                    message.Sections[SystemMessageSection.EnvironmentContext].Action);
                Assert.Equal(instructions, message.Content);
            }
        }
    }

    /// <summary>
    ///     Proves every session the factory builds switches the runtime's experimental features off
    ///     and skips embedding retrieval, a context channel shared across sessions.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_ExperimentalModeAndEmbeddingRetrieval_AreOff()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read") };

        // Act
        SessionOptionsBase[] configs =
        [
            CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null),
            CopilotAgentFactory.BuildResumeSessionConfig(tools, instructions: null, onPermissionRequest: null),
            CopilotAgentFactory.BuildEngineSessionConfig(tools, instructions: null, model: null),
        ];

        // Assert
        Assert.All(configs, config =>
        {
            Assert.False(config.Experiments.EnableExperimentalMode);
            Assert.True(config.State.SkipEmbeddingRetrieval);
        });
    }

    /// <summary>
    ///     Proves every session the factory builds switches the runtime's commit co-author trailers
    ///     off. Off is an Empty-mode default; the SDK forwards the setting in any other mode only when
    ///     it is set, so leaving it unset would hand a non-Empty client's own default to the session.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_GitCoauthorTrailers_AreOff()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read") };

        // Act
        SessionOptionsBase[] configs =
        [
            CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null),
            CopilotAgentFactory.BuildResumeSessionConfig(tools, instructions: null, onPermissionRequest: null),
            CopilotAgentFactory.BuildEngineSessionConfig(tools, instructions: null, model: null),
        ];

        // Assert: set to false on every builder, not merely left unset
        Assert.All(configs, config => Assert.False(config.Git.CoauthorEnabled));
    }

    /// <summary>
    ///     Proves a supplied model name is carried onto the session, so an application can choose
    ///     which Copilot model backs its agent without going around this factory — going around it
    ///     forfeits the built-in suppression and the default-safe permission handler.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_Model_CarriedOnSession()
    {
        // Arrange: a valid tool set and a named model
        var tools = new List<AIFunction> { MakeTool("doc_read") };

        // Act: build the session the factory would hand to the runtime, naming the model
        var config = CopilotAgentFactory.BuildSessionConfig(
            tools,
            instructions: null,
            onPermissionRequest: null,
            model: "gpt-5.4-mini");

        // Assert: the session carries exactly the supplied name
        Assert.Equal("gpt-5.4-mini", config.Model);
    }

    /// <summary>
    ///     Proves omitting the model leaves the session's model at its default, so the runtime
    ///     chooses. This is what makes the parameter purely additive: a caller that says nothing
    ///     about the model gets precisely the behavior it had before the parameter existed.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_NoModel_LeavesSessionModelAtDefault()
    {
        // Arrange: a valid tool set, and an untouched session for the default to compare against
        var tools = new List<AIFunction> { MakeTool("doc_read") };
        var untouched = new CreateSessionOptions();

        // Act: build the session without naming a model
        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        // Assert: the model is whatever a freshly constructed session carries — the factory set
        // nothing, so the runtime applies its own default
        Assert.Equal(untouched.Model, config.Model);
    }

    /// <summary>
    ///     Proves the factory closes both channels of runtime-injected capability on every session
    ///     it builds: skills are disabled and custom instructions are skipped.
    /// </summary>
    /// <remarks>
    ///     Either value reverting to the permissive one would widen the agent beyond what its host
    ///     attached without changing anything the host wrote, so both are asserted here rather than
    ///     left to inspection of the factory.
    /// </remarks>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_InjectedCapability_IsWithheld()
    {
        // Arrange: a valid tool set
        var tools = new List<AIFunction> { MakeTool("doc_read") };

        // Act: build the session the factory would hand to the runtime
        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        // Assert: the agent receives only the capability and direction its host attached
        Assert.False(config.Skills.Enabled);
        Assert.True(config.Instructions.SkipCustomInstructions);

        // The further channels the runtime offers are closed explicitly too, independent of the
        // client mode the host chose.
        Assert.False(config.Tools.LargeOutput!.Enabled);
        Assert.False(config.Tools.Search!.Enabled);
        Assert.False(config.State.Memory!.Enabled);
        Assert.False(config.State.EnableCrossSessionStore);
        Assert.False(config.Hooks.EnableRepositoryHooks);
        Assert.Empty(config.Agents.IncludedBuiltInAgents!);
        Assert.Null(config.Mcp.Servers);
        Assert.Contains(CopilotAgentFactory.BuiltInGitHubMcpServer, config.Mcp.DisabledServers!);
    }

    /// <summary>
    ///     Proves the agent path leaves the Copilot runtime's own compaction alone. This is the one
    ///     deliberate asymmetry between the two configuration paths: a plain agent has no AgentKit
    ///     compactor behind it, so moving the runtime's threshold would remove protection rather than
    ///     prevent a conflict.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_LeavesTheRuntimesCompactionUntouched()
    {
        // Arrange: a valid tool set, and an untouched session for the default to compare against
        var tools = new List<AIFunction> { MakeTool("doc_read") };
        var untouched = new CreateSessionOptions();

        // Act
        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        // Assert: whatever a freshly constructed session carries - the factory set nothing
        Assert.Equal(untouched.State.InfiniteSessions, config.State.InfiniteSessions);
    }

    /// <summary>
    ///     Proves the engine path holds the runtime's own compaction off until well past the point
    ///     the engine rotates.
    /// </summary>
    /// <remarks>
    ///     The threshold is the assertion that matters, because the flag does not work: measured
    ///     against the live runtime, a session created with <c>Enabled = false</c> compacted as soon as
    ///     the threshold was crossed, exactly as one created with it true. The threshold is honored,
    ///     and it is what keeps the two compactors apart — the engine rotates at 0.70, and the
    ///     runtime's default of 0.80 leaves only a tenth of the window between them, which one turn
    ///     returning a large tool result can cross in a single step.
    /// </remarks>
    [Fact]
    public void CopilotAgentFactory_BuildEngineSessionConfig_HoldsTheRuntimesCompactionWellAboveRotation()
    {
        // Arrange / Act
        var config = CopilotAgentFactory.BuildEngineSessionConfig(
            [MakeTool("doc_read")],
            instructions: null,
            model: null);

        // Assert: the honored setting is raised clear of the engine's own rotation point, and the
        // flag is still stated so the intent survives if the runtime ever respects it
        Assert.NotNull(config.State.InfiniteSessions);
        Assert.False(config.State.InfiniteSessions.Enabled);
        Assert.NotNull(config.State.InfiniteSessions.BackgroundCompactionThreshold);
        Assert.True(config.State.InfiniteSessions.BackgroundCompactionThreshold > 0.90);
    }

    /// <summary>
    ///     Proves the engine path accepts a tool list the agent path refuses. A consolidation runs on
    ///     a session that must offer nothing, which is a correct engine-driven session and an
    ///     incorrect agent — and the confinement it produces is the strongest this factory can
    ///     express, not the weakest.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildEngineSessionConfig_EmptyTools_ProducesAnEmptyAllowList()
    {
        // Arrange / Act
        var config = CopilotAgentFactory.BuildEngineSessionConfig([], instructions: null, model: null);

        // Assert: nothing published, nothing allowed, and the injection channels still shut
        Assert.Empty(config.Tools.Definitions!);
        Assert.Empty(config.Tools.AvailableTools!);
        Assert.False(config.Skills.Enabled);
        Assert.True(config.Instructions.SkipCustomInstructions);
    }

    /// <summary>
    ///     Proves both configuration paths derive the allow-list identically, which is the property
    ///     that makes having two builders safe: they differ in what tool lists they accept and in the
    ///     runtime's compaction, and in nothing that decides what a session may call.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BothPaths_DeriveTheSameConfinement()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read"), MakeTool("doc_write") };

        // Act
        var agent = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);
        var engine = CopilotAgentFactory.BuildEngineSessionConfig(tools, instructions: null, model: null);

        // Assert
        Assert.Equal(agent.Tools.AvailableTools, engine.Tools.AvailableTools);
        Assert.Equal(
            agent.Tools.Definitions!.Select(tool => tool.Name),
            engine.Tools.Definitions!.Select(tool => tool.Name));
        Assert.Equal(agent.Skills.Enabled, engine.Skills.Enabled);
        Assert.Equal(agent.Instructions.SkipCustomInstructions, engine.Instructions.SkipCustomInstructions);
    }

    /// <summary>
    ///     Proves the engine path still refuses a tool list that could not produce an unambiguous
    ///     allow-list. Emptiness is the only rule the two paths disagree about.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildEngineSessionConfig_DuplicateToolNames_Throws()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read"), MakeTool("doc_read") };

        // Act / Assert
        var ex = Assert.Throws<ArgumentException>(
            () => CopilotAgentFactory.BuildEngineSessionConfig(tools, instructions: null, model: null));
        Assert.Contains("doc_read", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves every allow-list entry is source-qualified as a custom tool, so the runtime can
    ///     match only a tool this package registered and never a built-in of the same name.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_AllowListIsSourceQualified()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read"), MakeTool("doc_view") };

        // Act
        var config = CopilotAgentFactory.BuildEngineSessionConfig(tools, instructions: null, model: null);

        // Assert: every entry carries the custom qualifier
        Assert.Equal(["custom:doc_read", "custom:doc_view"], config.Tools.AvailableTools);
    }

    /// <summary>
    ///     Proves the runtime's diversion of a large tool result to a temporary file is switched off,
    ///     so a result reaches the model inline rather than as a path no allowed tool can read.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_LargeOutputDiversion_IsDisabled()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read") };

        // Act
        var agent = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);
        var engine = CopilotAgentFactory.BuildEngineSessionConfig(tools, instructions: null, model: null);

        // Assert
        Assert.False(agent.Tools.LargeOutput!.Enabled);
        Assert.False(engine.Tools.LargeOutput!.Enabled);
    }

    /// <summary>
    ///     Proves the runtime's built-in GitHub MCP server is disabled outright on every path, rather
    ///     than merely filtered by the allow-list.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_GitHubMcpServer_IsDisabled()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read") };

        // Act
        var agent = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);
        var resume = CopilotAgentFactory.BuildResumeSessionConfig(tools, instructions: null, onPermissionRequest: null);
        var engine = CopilotAgentFactory.BuildEngineSessionConfig([], instructions: null, model: null);

        // Assert
        Assert.Equal(["github-mcp-server"], agent.Mcp.DisabledServers);
        Assert.Equal(["github-mcp-server"], resume.Mcp.DisabledServers);
        Assert.Equal(["github-mcp-server"], engine.Mcp.DisabledServers);
    }

    /// <summary>
    ///     Proves a supplied working directory is carried onto the session, and that a session built
    ///     without one leaves it to the runtime.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_WorkingDirectory_IsCarriedOrLeftToTheRuntime()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read") };
        var workspace = Path.Combine(Path.GetTempPath(), "workspace");

        // Act
        var carried = CopilotAgentFactory.BuildSessionConfig(
            tools, instructions: null, onPermissionRequest: null, workingDirectory: workspace);
        var blank = CopilotAgentFactory.BuildSessionConfig(
            tools, instructions: null, onPermissionRequest: null, workingDirectory: "  ");
        var omitted = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        // Assert
        Assert.Equal(workspace, carried.WorkingDirectory);
        Assert.Null(blank.WorkingDirectory);
        Assert.Null(omitted.WorkingDirectory);
    }

    /// <summary>
    ///     Proves a resumed agent session is confined exactly as a created one is: the same
    ///     allow-list and definitions, the same withheld channels, the default-deny handler, and the
    ///     same model, working directory and instructions.
    /// </summary>
    [Fact]
    public async Task CopilotAgentFactory_BuildResumeSessionConfig_DerivesTheSameConfinement()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read"), MakeTool("doc_write") };
        var workspace = Path.Combine(Path.GetTempPath(), "workspace");

        // Act
        var created = CopilotAgentFactory.BuildSessionConfig(
            tools, "be concise", onPermissionRequest: null, model: "gpt-5.4-mini", workingDirectory: workspace);
        var resumed = CopilotAgentFactory.BuildResumeSessionConfig(
            tools, "be concise", onPermissionRequest: null, model: "gpt-5.4-mini", workingDirectory: workspace);

        // Assert: what the session may call, and what is withheld from it
        Assert.Equal(created.Tools.AvailableTools, resumed.Tools.AvailableTools);
        Assert.Equal(
            created.Tools.Definitions!.Select(tool => tool.Name),
            resumed.Tools.Definitions!.Select(tool => tool.Name));
        Assert.False(resumed.Skills.Enabled);
        Assert.True(resumed.Instructions.SkipCustomInstructions);
        Assert.False(resumed.Tools.LargeOutput!.Enabled);
        Assert.False(resumed.State.Memory!.Enabled);

        // Assert: the host's own settings
        Assert.Equal("gpt-5.4-mini", resumed.Model);
        Assert.Equal(workspace, resumed.WorkingDirectory);
        Assert.Equal("be concise", resumed.SystemMessage!.Content);

        // Assert: the default-deny handler is installed and rejects a built-in request
        var decision = await resumed.Permissions.OnRequest!(new PermissionRequest { Kind = "shell" }, null!);
        Assert.Equal(PermissionDecision.Reject("x").Kind, decision.Kind);
    }

    /// <summary>
    ///     Proves token streaming is turned on for the agent's sessions, created and resumed, and left
    ///     off for the engine's, which take only a turn's final answer.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_AgentPath_EnablesStreaming()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read") };

        // Act
        var created = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);
        var resumed = CopilotAgentFactory.BuildResumeSessionConfig(tools, instructions: null, onPermissionRequest: null);
        var engine = CopilotAgentFactory.BuildEngineSessionConfig(tools, instructions: null, model: null);

        // Assert
        Assert.True(created.Events.Streaming);
        Assert.True(resumed.Events.Streaming);
        Assert.NotEqual(true, engine.Events.Streaming);
    }

    /// <summary>
    ///     Proves a tool carrying the name of one of the runtime's built-in tools is refused before
    ///     any session exists, on every platform's built-in names and regardless of case.
    /// </summary>
    /// <param name="name">A built-in tool's name, as a supplied tool might carry it.</param>
    [Theory]
    [InlineData("view")]
    [InlineData("bash")]
    [InlineData("powershell")]
    [InlineData("View")]
    [InlineData("task_complete")]
    public void CopilotAgentFactory_BuildSessionConfig_BuiltInToolName_Throws(string name)
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read"), MakeTool(name) };

        // Act
        var failure = Assert.Throws<ArgumentException>(
            () => CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null));

        // Assert: the refusal names the tool and the parameter, and tells the host what to do
        Assert.Equal("tools", failure.ParamName);
        Assert.Contains($"'{name}'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Rename the tool", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the built-in name refusal holds on the engine path and on resumption too, so no
    ///     session this package builds can collide with a built-in.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildEngineSessionConfig_BuiltInToolName_Throws()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("grep") };

        // Act / Assert
        Assert.Throws<ArgumentException>(
            () => CopilotAgentFactory.BuildEngineSessionConfig(tools, instructions: null, model: null));
        Assert.Throws<ArgumentException>(
            () => CopilotAgentFactory.BuildResumeSessionConfig(tools, instructions: null, onPermissionRequest: null));
    }

    /// <summary>
    ///     Proves every session's permission mode is pinned to Manual, so every request reaches the
    ///     permission handler instead of being approved by the runtime without asking it.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_PermissionMode_IsPinnedToManual()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read") };

        // Act
        var created = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);
        var resumed = CopilotAgentFactory.BuildResumeSessionConfig(tools, instructions: null, onPermissionRequest: null);
        var engine = CopilotAgentFactory.BuildEngineSessionConfig([], instructions: null, model: null);

        // Assert
        Assert.Equal(PermissionMode.Manual, created.Permissions.Mode);
        Assert.Equal(PermissionMode.Manual, resumed.Permissions.Mode);
        Assert.Equal(PermissionMode.Manual, engine.Permissions.Mode);
    }

    /// <summary>
    ///     Proves the runtime's image generation capability is switched off: a confined agent receives
    ///     only what its host attached.
    /// </summary>
    [Fact]
    public void CopilotAgentFactory_BuildSessionConfig_ImageGeneration_IsDisabled()
    {
        // Arrange
        var tools = new List<AIFunction> { MakeTool("doc_read") };

        // Act
        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        // Assert
        Assert.False(config.Tools.ImageGeneration!.Enabled);
    }

    /// <summary>
    ///     Builds a no-op tool carrying the given name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) =>
        AIFunctionFactory.Create(() => "ok", name);
}
