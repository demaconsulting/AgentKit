using DemaConsulting.AgentKit.Tools.Todo;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Todo;

/// <summary>
///     Unit tests for the <see cref="TodoRemoveTool"/> class.
/// </summary>
public class TodoRemoveToolTests
{
    /// <summary>
    ///     Proves the tool carries the published name and a description.
    /// </summary>
    [Fact]
    public void TodoRemoveTool_Create_CarriesTheNameAndDescription()
    {
        // Arrange / Act: build the tool over a list of its own
        var tool = TodoRemoveTool.Create(new TodoStore());

        // Assert: the model can find and choose it
        Assert.Equal("todo_remove", tool.Name);
        Assert.Equal(TodoRemoveTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    /// <summary>
    ///     Proves a missing store is a programming error in the composing family.
    /// </summary>
    [Fact]
    public void TodoRemoveTool_Create_NullStore_ThrowsArgumentNullException()
    {
        // Act / Assert: a tool with no list to remove from cannot be built
        Assert.Throws<ArgumentNullException>(() => TodoRemoveTool.Create(null!));
    }

    /// <summary>
    ///     Proves removing a step drops it and states the size of the list, using the singular noun
    ///     when one task remains.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TodoRemoveTool_Remove_KnownIdentifier_DropsItAndReportsTheCount()
    {
        // Arrange: a list holding two steps
        var store = new TodoStore();
        store.Set("phase1", "Survey", TodoStatuses.Done, null);
        store.Set("phase2", "Draft", TodoStatuses.Pending, null);
        var tool = TodoRemoveTool.Create(store);

        // Act: drop one
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["id"] = "phase2" },
            TestContext.Current.CancellationToken);

        // Assert: the result says what the list now holds, and the list agrees
        Assert.Equal("Task 'phase2' was removed. The list now has 1 item.", result);
        Assert.Equal(["phase1"], store.Identifiers());
    }

    /// <summary>
    ///     Proves an identifier the list does not hold is refused with a plain statement of fact
    ///     that names the identifiers the list does hold and prescribes no other tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TodoRemoveTool_Remove_UnknownIdentifier_IsRefusedAndNamesWhatTheListHolds()
    {
        // Arrange: a list holding two steps
        var store = new TodoStore();
        store.Set("phase1", "Survey", TodoStatuses.Done, null);
        store.Set("phase2", "Draft", TodoStatuses.Pending, null);
        var tool = TodoRemoveTool.Create(store);

        // Act: name a step that is not there
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["id"] = "phase9" },
            TestContext.Current.CancellationToken);

        // Assert: a returned refusal stating the fact, naming the list, and redirecting nowhere
        var text = Assert.IsType<string>(result);
        Assert.Equal(
            "Denied (TargetNotFound): No task carries the id 'phase9'. The list holds 'phase1', 'phase2'.",
            text);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
        Assert.Equal(2, store.Items().Count);
    }

    /// <summary>
    ///     Proves a removal from an empty list states that the list is empty rather than naming
    ///     nothing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TodoRemoveTool_Remove_EmptyList_StatesTheListIsEmpty()
    {
        // Arrange: a list nothing has been written to
        var tool = TodoRemoveTool.Create(new TodoStore());

        // Act: name any step
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["id"] = "phase1" },
            TestContext.Current.CancellationToken);

        // Assert: the refusal states the fact without listing an empty collection
        Assert.Equal(
            "Denied (TargetNotFound): No task carries the id 'phase1'. The list is empty.",
            Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a request missing an identifier is refused rather than thrown.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TodoRemoveTool_Remove_MissingIdentifier_IsRefused()
    {
        // Arrange: a list holding one step
        var store = new TodoStore();
        store.Set("phase1", "Survey", TodoStatuses.Done, null);
        var tool = TodoRemoveTool.Create(store);

        // Act: state no identifier
        var result = await tool.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: a returned refusal, and nothing removed
        Assert.StartsWith(
            "Denied (InvalidRequest)",
            Assert.IsType<string>(result),
            StringComparison.Ordinal);
        Assert.Single(store.Items());
    }
}
