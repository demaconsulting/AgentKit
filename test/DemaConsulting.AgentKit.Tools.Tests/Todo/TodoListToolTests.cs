using System.Text.Json;
using DemaConsulting.AgentKit.Tools.Todo;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Todo;

/// <summary>
///     Unit tests for the <see cref="TodoListTool"/> class.
/// </summary>
public class TodoListToolTests
{
    /// <summary>
    ///     Proves the tool carries the published name and a description.
    /// </summary>
    [Fact]
    public void TodoListTool_Create_CarriesTheNameAndDescription()
    {
        // Arrange / Act: build the tool over a list of its own
        var tool = TodoListTool.Create(new TodoStore());

        // Assert: the model can find and choose it
        Assert.Equal("todo_list", tool.Name);
        Assert.Equal(TodoListTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    /// <summary>
    ///     Proves a missing store is a programming error in the composing family.
    /// </summary>
    [Fact]
    public void TodoListTool_Create_NullStore_ThrowsArgumentNullException()
    {
        // Act / Assert: a tool with no list to report cannot be built
        Assert.Throws<ArgumentNullException>(() => TodoListTool.Create(null!));
    }

    /// <summary>
    ///     Proves an empty list is reported as an empty list rather than refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TodoListTool_List_EmptyList_ReportsNoItems()
    {
        // Arrange: a list nothing has been written to
        var tool = TodoListTool.Create(new TodoStore());

        // Act: ask for the list
        var result = await tool.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: a true answer, not a refusal
        var element = Assert.IsType<JsonElement>(result);
        Assert.Equal(0, element.GetProperty("itemCount").GetInt32());
        Assert.Empty(element.GetProperty("items").EnumerateArray());
    }

    /// <summary>
    ///     Proves the list reports every field of every task, in the order the steps were written
    ///     down, because the order is the sequence.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TodoListTool_List_PopulatedList_ReportsItemsInOrder()
    {
        // Arrange: three steps, the middle one advanced after the third was written
        var store = new TodoStore();
        store.Set("phase1", "Survey", TodoStatuses.Done, null);
        store.Set("phase2", "Draft", TodoStatuses.Pending, null);
        store.Set("phase3", "Review", TodoStatuses.Pending, null);
        store.Set("phase2", "Draft", TodoStatuses.InProgress, "halfway");
        var tool = TodoListTool.Create(store);

        // Act: ask for the list
        var result = await tool.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: three items, in their original order, with the advanced one still second
        var element = Assert.IsType<JsonElement>(result);
        Assert.Equal(3, element.GetProperty("itemCount").GetInt32());

        var items = element.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["phase1", "phase2", "phase3"], items.Select(item => item.GetProperty("id").GetString()));
        Assert.Equal("in_progress", items[1].GetProperty("status").GetString());
        Assert.Equal("halfway", items[1].GetProperty("note").GetString());
        Assert.Equal("Survey", items[0].GetProperty("title").GetString());
    }
}
