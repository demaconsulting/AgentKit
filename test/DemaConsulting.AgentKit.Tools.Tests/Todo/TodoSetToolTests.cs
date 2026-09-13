using DemaConsulting.AgentKit.Tools.Todo;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Todo;

/// <summary>
///     Unit tests for the <see cref="TodoSetTool"/> class.
/// </summary>
public class TodoSetToolTests
{
    /// <summary>
    ///     Proves the tool carries the published name and a description.
    /// </summary>
    [Fact]
    public void TodoSetTool_Create_CarriesTheNameAndDescription()
    {
        // Arrange / Act: build the tool over a list of its own
        var tool = TodoSetTool.Create(new TodoStore());

        // Assert: the model can find and choose it
        Assert.Equal("todo_set", tool.Name);
        Assert.Equal(TodoSetTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    /// <summary>
    ///     Proves a missing store is a programming error in the composing family.
    /// </summary>
    [Fact]
    public void TodoSetTool_Create_NullStore_ThrowsArgumentNullException()
    {
        // Act / Assert: a tool with no list to write to cannot be built
        Assert.Throws<ArgumentNullException>(() => TodoSetTool.Create(null!));
    }

    /// <summary>
    ///     Proves a first write records the step as pending and states the size of the list.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TodoSetTool_Set_OmittedStatus_RecordsPendingAndReportsTheCount()
    {
        // Arrange: an empty list
        var store = new TodoStore();
        var tool = TodoSetTool.Create(store);

        // Act: write one step down, stating no status
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["id"] = "phase1", ["title"] = "Survey the repository" },
            TestContext.Current.CancellationToken);

        // Assert: the result says what the list now holds, and the list agrees
        Assert.Equal("Task 'phase1' is pending. The list now has 1 item.", result);
        Assert.Equal(TodoStatuses.Pending, store.Items()[0].Status);
    }

    /// <summary>
    ///     Proves a write against an identifier the list already holds updates it rather than
    ///     adding a second task, and that the reported count says so.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TodoSetTool_Set_ExistingIdentifier_UpsertsAndKeepsTheCount()
    {
        // Arrange: a list holding one step
        var store = new TodoStore();
        var tool = TodoSetTool.Create(store);
        await tool.InvokeAsync(
            new AIFunctionArguments { ["id"] = "phase1", ["title"] = "Survey the repository" },
            TestContext.Current.CancellationToken);

        // Act: advance that same step
        var result = await tool.InvokeAsync(
            new AIFunctionArguments
            {
                ["id"] = "phase1",
                ["title"] = "Survey the repository",
                ["status"] = "in_progress",
                ["note"] = "reading the design docs",
            },
            TestContext.Current.CancellationToken);

        // Assert: one task, advanced in place, with its note kept
        Assert.Equal("Task 'phase1' is in_progress. The list now has 1 item.", result);
        Assert.Single(store.Items());
        Assert.Equal("reading the design docs", store.Items()[0].Note);
    }

    /// <summary>
    ///     Proves every published status can be recorded, so a step can run its whole life cycle.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TodoSetTool_Set_EveryPublishedStatus_IsRecorded()
    {
        // Arrange: a list holding one step
        var store = new TodoStore();
        var tool = TodoSetTool.Create(store);

        // Act / Assert: each transition is accepted and observable
        foreach (var status in TodoStatuses.All)
        {
            var result = await tool.InvokeAsync(
                new AIFunctionArguments { ["id"] = "phase1", ["title"] = "Survey", ["status"] = status },
                TestContext.Current.CancellationToken);

            Assert.Equal("Task 'phase1' is " + status + ". The list now has 1 item.", result);
            Assert.Equal(status, store.Items()[0].Status);
        }
    }

    /// <summary>
    ///     Proves a status outside the published vocabulary is refused as a returned value naming
    ///     the vocabulary, and leaves the list untouched.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TodoSetTool_Set_UnknownStatus_IsRefusedAndLeavesTheListUnchanged()
    {
        // Arrange: an empty list
        var store = new TodoStore();
        var tool = TodoSetTool.Create(store);

        // Act: state a status the family does not publish
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["id"] = "phase1", ["title"] = "Survey", ["status"] = "finished" },
            TestContext.Current.CancellationToken);

        // Assert: a refusal naming the vocabulary, and nothing written
        var text = Assert.IsType<string>(result);
        Assert.StartsWith("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("'pending', 'in_progress', 'done', 'blocked'", text, StringComparison.Ordinal);
        Assert.Empty(store.Items());
    }

    /// <summary>
    ///     Proves a request missing an identifier or a title is refused rather than thrown.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TodoSetTool_Set_MissingIdentifierOrTitle_IsRefused()
    {
        // Arrange: an empty list
        var store = new TodoStore();
        var tool = TodoSetTool.Create(store);

        // Act: omit each mandatory argument in turn
        var noId = await tool.InvokeAsync(
            new AIFunctionArguments { ["title"] = "Survey" },
            TestContext.Current.CancellationToken);
        var noTitle = await tool.InvokeAsync(
            new AIFunctionArguments { ["id"] = "phase1" },
            TestContext.Current.CancellationToken);

        // Assert: both are returned refusals, and nothing was written
        Assert.StartsWith("Denied (InvalidRequest)", Assert.IsType<string>(noId), StringComparison.Ordinal);
        Assert.StartsWith("Denied (InvalidRequest)", Assert.IsType<string>(noTitle), StringComparison.Ordinal);
        Assert.Empty(store.Items());
    }
}
