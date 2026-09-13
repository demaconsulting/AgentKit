using DemaConsulting.AgentKit.Tools.Todo;

namespace DemaConsulting.AgentKit.Tools.Tests.Todo;

/// <summary>
///     Unit tests for the <see cref="TodoStore"/> class and the status vocabulary it holds.
/// </summary>
public class TodoStoreTests
{
    /// <summary>
    ///     Proves a new identifier appends a task and reports the size of the list.
    /// </summary>
    [Fact]
    public void TodoStore_Set_NewIdentifier_AppendsTheTask()
    {
        // Arrange: an empty list
        var store = new TodoStore();

        // Act: record two steps
        var first = store.Set("a", "First", TodoStatuses.Pending, null);
        var second = store.Set("b", "Second", TodoStatuses.Pending, null);

        // Assert: both are held, in the order they were written down
        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(["a", "b"], store.Identifiers());
    }

    /// <summary>
    ///     Proves an identifier the list already holds updates that task in place rather than
    ///     appending a second one, so a status change never reorders the plan beneath it.
    /// </summary>
    [Fact]
    public void TodoStore_Set_ExistingIdentifier_UpdatesInPlace()
    {
        // Arrange: three steps in a stated order
        var store = new TodoStore();
        store.Set("a", "First", TodoStatuses.Pending, null);
        store.Set("b", "Second", TodoStatuses.Pending, null);
        store.Set("c", "Third", TodoStatuses.Pending, null);

        // Act: advance the middle one
        var count = store.Set("b", "Second, revised", TodoStatuses.InProgress, "started");

        // Assert: the list is the same length and the same order, with the middle item changed
        Assert.Equal(3, count);
        Assert.Equal(["a", "b", "c"], store.Identifiers());
        var updated = store.Items()[1];
        Assert.Equal("Second, revised", updated.Title);
        Assert.Equal(TodoStatuses.InProgress, updated.Status);
        Assert.Equal("started", updated.Note);
    }

    /// <summary>
    ///     Proves a status the vocabulary does not hold is a programming error, because the tool is
    ///     expected to have refused it before the store is reached.
    /// </summary>
    [Fact]
    public void TodoStore_Set_UnknownStatus_ThrowsArgumentException()
    {
        // Arrange: an empty list
        var store = new TodoStore();

        // Act / Assert: an unrecognized status never reaches the list
        Assert.Throws<ArgumentException>(() => store.Set("a", "First", "finished", null));
    }

    /// <summary>
    ///     Proves removal reports whether anything was removed and what the list now holds.
    /// </summary>
    [Fact]
    public void TodoStore_Remove_KnownAndUnknownIdentifiers_ReportTheOutcome()
    {
        // Arrange: two steps
        var store = new TodoStore();
        store.Set("a", "First", TodoStatuses.Pending, null);
        store.Set("b", "Second", TodoStatuses.Pending, null);

        // Act: remove one that exists and one that does not
        var removed = store.Remove("a", out var afterRemoval);
        var missed = store.Remove("zz", out var afterMiss);

        // Assert: only the real removal changed anything
        Assert.True(removed);
        Assert.Equal(1, afterRemoval);
        Assert.False(missed);
        Assert.Equal(1, afterMiss);
        Assert.Equal(["b"], store.Identifiers());
    }

    /// <summary>
    ///     Proves the reported items are a snapshot, so a later write cannot alter a list a caller
    ///     is already reading.
    /// </summary>
    [Fact]
    public void TodoStore_Items_ReturnsASnapshot()
    {
        // Arrange: one step, read out
        var store = new TodoStore();
        store.Set("a", "First", TodoStatuses.Pending, null);
        var snapshot = store.Items();

        // Act: write a second step
        store.Set("b", "Second", TodoStatuses.Pending, null);

        // Assert: the earlier read is unchanged
        Assert.Single(snapshot);
        Assert.Equal(2, store.Items().Count);
    }

    /// <summary>
    ///     Proves the store rejects an empty identifier or title, which no tool should let through.
    /// </summary>
    [Fact]
    public void TodoStore_Set_EmptyIdentifierOrTitle_ThrowsArgumentException()
    {
        // Arrange: an empty list
        var store = new TodoStore();

        // Act / Assert: neither an anonymous task nor a silent one is representable
        Assert.Throws<ArgumentException>(() => store.Set(string.Empty, "First", TodoStatuses.Pending, null));
        Assert.Throws<ArgumentException>(() => store.Set("a", string.Empty, TodoStatuses.Pending, null));
    }

    /// <summary>
    ///     Proves the status vocabulary is exactly the four statuses the family publishes, compared
    ///     ordinally.
    /// </summary>
    [Fact]
    public void TodoStatuses_IsValid_AcceptsExactlyThePublishedVocabulary()
    {
        // Arrange / Act / Assert: the four statuses are accepted and nothing else is
        Assert.Equal(["pending", "in_progress", "done", "blocked"], TodoStatuses.All);
        Assert.All(TodoStatuses.All, status => Assert.True(TodoStatuses.IsValid(status)));
        Assert.False(TodoStatuses.IsValid("In_Progress"));
        Assert.False(TodoStatuses.IsValid(null));
        Assert.Equal("'pending', 'in_progress', 'done', 'blocked'", TodoStatuses.Describe());
    }
}
