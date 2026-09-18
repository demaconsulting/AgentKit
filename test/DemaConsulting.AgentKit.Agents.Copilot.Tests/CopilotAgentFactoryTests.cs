using DemaConsulting.AgentKit.Agents.Copilot;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
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

        Assert.Same(handler, config.OnPermissionRequest);
    }

    /// <summary>
    ///     Proves supplied instructions are carried onto the session's system message, and that a
    ///     session built without instructions carries none.
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
        Assert.Null(withoutInstructions.SystemMessage);
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
        var untouched = new SessionConfig();

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
        Assert.False(config.EnableSkills);
        Assert.True(config.SkipCustomInstructions);
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
        var untouched = new SessionConfig();

        // Act
        var config = CopilotAgentFactory.BuildSessionConfig(tools, instructions: null, onPermissionRequest: null);

        // Assert: whatever a freshly constructed session carries - the factory set nothing
        Assert.Equal(untouched.InfiniteSessions, config.InfiniteSessions);
    }

    /// <summary>
    ///     Proves the engine path holds the runtime's own compaction off until well past the point
    ///     the engine rotates.
    /// </summary>
    /// <remarks>
    ///     The threshold is the assertion that matters, because the flag does not work: measured
    ///     against SDK 1.0.11, a session created with <c>Enabled = false</c> compacted as soon as
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
        Assert.NotNull(config.InfiniteSessions);
        Assert.False(config.InfiniteSessions.Enabled);
        Assert.NotNull(config.InfiniteSessions.BackgroundCompactionThreshold);
        Assert.True(config.InfiniteSessions.BackgroundCompactionThreshold > 0.90);
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
        Assert.Empty(config.Tools!);
        Assert.Empty(config.AvailableTools!);
        Assert.False(config.EnableSkills);
        Assert.True(config.SkipCustomInstructions);
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
        Assert.Equal(agent.AvailableTools, engine.AvailableTools);
        Assert.Equal(
            agent.Tools!.Select(tool => tool.Name),
            engine.Tools!.Select(tool => tool.Name));
        Assert.Equal(agent.EnableSkills, engine.EnableSkills);
        Assert.Equal(agent.SkipCustomInstructions, engine.SkipCustomInstructions);
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
    ///     Builds a no-op tool carrying the given name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) =>
        AIFunctionFactory.Create(() => "ok", name);
}
