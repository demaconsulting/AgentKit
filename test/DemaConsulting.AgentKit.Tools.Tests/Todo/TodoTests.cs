using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Todo;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Todo;

/// <summary>
///     Subsystem-level integration tests for the todo tool family.
/// </summary>
/// <remarks>
///     These scenarios exercise the family the way an application does: composed through the
///     AgentKitCore <see cref="ToolPackBuilder"/> under one access policy, then invoked through the
///     published tool list. They assert the family-wide properties — one prefix, one list shared by
///     the three tools, and a plan that can be written down, advanced and closed out from the
///     agent's side of the boundary.
/// </remarks>
public class TodoTests
{
    /// <summary>
    ///     Proves a composition attaching the family publishes the three tools under one prefix.
    /// </summary>
    [Fact]
    public void Todo_Family_ComposedThroughBuilder_PublishesTheFamily()
    {
        // Arrange / Act: attach the family
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy).Add(new TodoPack()).Build();

        // Assert: three tools, in the order the pack states
        Assert.Equal(
            [TodoListTool.ToolName, TodoSetTool.ToolName, TodoRemoveTool.ToolName],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves every tool in the family carries a valid name and a description.
    /// </summary>
    [Fact]
    public void Todo_Family_EveryTool_CarriesAValidatedNameAndDescription()
    {
        // Arrange / Act: attach the family
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy).Add(new TodoPack()).Build();

        // Assert: each name is well formed, carries the family prefix, and is described
        Assert.All(tools, tool =>
        {
            ToolName.Validate(tool.Name);
            Assert.StartsWith(TodoPack.FamilyPrefix + "_", tool.Name, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        });
    }

    /// <summary>
    ///     Proves a whole plan can be written down, advanced, corrected and closed out through the
    ///     published tools alone, with each tool observing the same list.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Todo_Family_MultiPhasePlan_IsRecordedAdvancedAndClosedOut()
    {
        // Arrange: the family as an agent receives it
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy).Add(new TodoPack()).Build();
        var list = tools.Single(tool => tool.Name == TodoListTool.ToolName);
        var set = tools.Single(tool => tool.Name == TodoSetTool.ToolName);
        var remove = tools.Single(tool => tool.Name == TodoRemoveTool.ToolName);

        // Act: plan three phases, run the first, abandon the third
        foreach (var phase in new[] { "phase1", "phase2", "phase3" })
        {
            await set.InvokeAsync(
                new AIFunctionArguments { ["id"] = phase, ["title"] = "Do " + phase },
                TestContext.Current.CancellationToken);
        }

        await set.InvokeAsync(
            new AIFunctionArguments { ["id"] = "phase1", ["title"] = "Do phase1", ["status"] = "in_progress" },
            TestContext.Current.CancellationToken);
        await set.InvokeAsync(
            new AIFunctionArguments { ["id"] = "phase1", ["title"] = "Do phase1", ["status"] = "done" },
            TestContext.Current.CancellationToken);
        await remove.InvokeAsync(
            new AIFunctionArguments { ["id"] = "phase3" },
            TestContext.Current.CancellationToken);

        var result = await list.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: one list, seen the same way by all three tools
        var element = Assert.IsType<JsonElement>(result);
        Assert.Equal(2, element.GetProperty("itemCount").GetInt32());

        var items = element.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["phase1", "phase2"], items.Select(item => item.GetProperty("id").GetString()));
        Assert.Equal("done", items[0].GetProperty("status").GetString());
        Assert.Equal("pending", items[1].GetProperty("status").GetString());
    }

    /// <summary>
    ///     Proves the family's refusals are returned values rather than exceptions, so a denied
    ///     request never ends the agent's turn.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Todo_Family_RefusedRequests_AreReturnedValues()
    {
        // Arrange: the family as an agent receives it
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy).Add(new TodoPack()).Build();

        // Act: ask for two things the family cannot do
        var unknownStatus = await tools.Single(tool => tool.Name == TodoSetTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["id"] = "a", ["title"] = "A", ["status"] = "almost" },
            TestContext.Current.CancellationToken);
        var unknownTask = await tools.Single(tool => tool.Name == TodoRemoveTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["id"] = "a" },
            TestContext.Current.CancellationToken);

        // Assert: both came back as refusal text the model can read and act on
        Assert.StartsWith("Denied (", Assert.IsType<string>(unknownStatus), StringComparison.Ordinal);
        Assert.StartsWith("Denied (", Assert.IsType<string>(unknownTask), StringComparison.Ordinal);
    }
}
