using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Todo;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Todo;

/// <summary>
///     Unit tests for the <see cref="TodoPack"/> class.
/// </summary>
public class TodoPackTests
{
    /// <summary>
    ///     Proves the pack publishes its family prefix as a constant and through the contract.
    /// </summary>
    [Fact]
    public void TodoPack_FamilyPrefix_IsPublishedAsConstantAndContract()
    {
        Assert.Equal("todo", TodoPack.FamilyPrefix);
        Assert.Equal(TodoPack.FamilyPrefix, ((IToolPack)new TodoPack()).FamilyPrefix);
    }

    /// <summary>
    ///     Proves the pack requires no host capability, so every host receives the family.
    /// </summary>
    [Fact]
    public void TodoPack_RequiredCapabilities_IsNone()
    {
        Assert.Equal(HostCapabilities.None, new TodoPack().RequiredCapabilities);
    }

    /// <summary>
    ///     Proves the pack creates its three tools in the order a model sees them.
    /// </summary>
    [Fact]
    public void TodoPack_CreateTools_RegistersTheFamilyInOrder()
    {
        // Arrange: any policy, since the family touches no files
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: create the family's tools
        var tools = new TodoPack().CreateTools(policy).ToList();

        // Assert: list, set, remove, in that order
        Assert.Equal(["todo_list", "todo_set", "todo_remove"], tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves the pack requires a policy to create its tools, even though its tools never
    ///     consult one.
    /// </summary>
    [Fact]
    public void TodoPack_CreateTools_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TodoPack().CreateTools(null!).ToList());
    }

    /// <summary>
    ///     Proves each call allocates a task list of its own, so two compositions of the same pack
    ///     instance never share a list. This is the property that keeps a delegated agent's list out
    ///     of its parent's.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TodoPack_CreateTools_CalledTwice_ProducesIndependentLists()
    {
        // Arrange: one pack instance, composed twice
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var pack = new TodoPack();
        var first = pack.CreateTools(policy).ToList();
        var second = pack.CreateTools(policy).ToList();

        // Act: write into the first composition's list only
        await first.Single(tool => tool.Name == TodoSetTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["id"] = "phase1", ["title"] = "Survey" },
            TestContext.Current.CancellationToken);

        var secondResult = await second.Single(tool => tool.Name == TodoSetTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["id"] = "other", ["title"] = "Something else" },
            TestContext.Current.CancellationToken);

        // Assert: the second composition saw a list of its own, holding one item rather than two
        Assert.Equal("Task 'other' is pending. The list now has 1 item.", secondResult);
    }

    /// <summary>
    ///     Proves the pack publishes the instruction an application should give an agent that
    ///     carries the family, so the text that was measured cannot drift from the text that ships.
    /// </summary>
    [Fact]
    public void TodoPack_SuggestedInstruction_NamesTheToolsAndTheStatusTransitions()
    {
        // Assert: the published instruction names the write tool and both transitions it asks for
        Assert.Contains(TodoSetTool.ToolName, TodoPack.SuggestedInstruction, StringComparison.Ordinal);
        Assert.Contains("in_progress", TodoPack.SuggestedInstruction, StringComparison.Ordinal);
        Assert.Contains("done", TodoPack.SuggestedInstruction, StringComparison.Ordinal);
    }
}
