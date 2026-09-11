using DemaConsulting.AgentKit.Agents.Copilot;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     System-level integration tests for the AgentKitAgentsCopilot system.
/// </summary>
/// <remarks>
///     These tests exercise the behavioral guarantees the package exists for without a live
///     Copilot CLI: the allow-list is derived from the supplied tools so the two cannot drift
///     apart (the safety-critical property of the whole increment), and the default permission
///     handler approves exactly the supplied tools while rejecting everything else. The session
///     configuration is a plain constructable object, so both are asserted offline.
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
        Assert.Equal(expectedNames, config.AvailableTools);
        Assert.Equal(tools.Count, config.Tools!.Count);
        Assert.Equal(expectedNames, config.Tools.Select(tool => tool.Name).ToList());
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
    ///     Invokes the session's permission handler, asserting one is installed.
    /// </summary>
    /// <param name="config">The session whose handler to invoke.</param>
    /// <param name="request">The permission request to adjudicate.</param>
    /// <returns>The handler's decision.</returns>
    private static Task<PermissionDecision> Decide(SessionConfig config, PermissionRequest request)
    {
        var handler = config.OnPermissionRequest;
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
}


