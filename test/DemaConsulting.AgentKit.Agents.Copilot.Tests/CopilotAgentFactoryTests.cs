using DemaConsulting.AgentKit.Agents.Copilot;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     Unit tests for <see cref="CopilotAgentFactory"/>: construction-time validation, the
///     host-supplied permission handler override, and instruction handling.
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
    ///     Builds a no-op tool carrying the given name.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>An <see cref="AIFunction"/> that returns a fixed string.</returns>
    private static AIFunction MakeTool(string name) =>
        AIFunctionFactory.Create(() => "ok", name);
}
