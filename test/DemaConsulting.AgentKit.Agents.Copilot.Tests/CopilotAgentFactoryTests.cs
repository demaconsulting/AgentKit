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
    ///     Builds a no-op tool carrying the given name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) =>
        AIFunctionFactory.Create(() => "ok", name);
}
