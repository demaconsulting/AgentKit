using DemaConsulting.AgentKit.Tools.Memory;

namespace DemaConsulting.AgentKit.Tools.Tests.Memory;

/// <summary>
///     Unit tests for the <see cref="IMemoryStore"/> contract as the default
///     <see cref="InMemoryMemoryStore"/> implements it.
/// </summary>
/// <remarks>
///     Nothing is mocked. The store is the unit under test and holds no collaborators, so a
///     substitute would only prove a substitute consistent with itself.
/// </remarks>
public class MemoryStoreTests
{
    /// <summary>
    ///     Builds a memory with a stated vector, so a test can control similarity exactly.
    /// </summary>
    /// <param name="id">The identifier the memory carries.</param>
    /// <param name="vector">The vector the memory's descriptor is taken to have produced.</param>
    /// <returns>The memory.</returns>
    private static MemoryRecord Memory(string id, params float[] vector)
    {
        return new MemoryRecord(id, "descriptor " + id, "details " + id, "doc-" + id, "§1", vector);
    }

    /// <summary>
    ///     Proves a filed memory is held and counted, which is the store's whole reason to exist.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InMemoryMemoryStore_AddAsync_NewMemory_IsHeldAndCounted()
    {
        // Arrange: an empty store
        var store = new InMemoryMemoryStore();
        var token = TestContext.Current.CancellationToken;

        // Act: file two memories
        await store.AddAsync(Memory("a", 1.0f, 0.0f), token);
        await store.AddAsync(Memory("b", 0.0f, 1.0f), token);

        // Assert: both are held and both are counted
        Assert.Equal(2, await store.CountAsync(token));
        Assert.NotNull(await store.FindAsync("a", token));
        Assert.NotNull(await store.FindAsync("b", token));
    }

    /// <summary>
    ///     Proves an identifier the store already holds is a programming error rather than a silent
    ///     overwrite, because identifiers are assigned by the caller and cannot legitimately repeat.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InMemoryMemoryStore_AddAsync_DuplicateIdentifier_ThrowsArgumentException()
    {
        // Arrange: a store already holding an identifier
        var store = new InMemoryMemoryStore();
        var token = TestContext.Current.CancellationToken;
        await store.AddAsync(Memory("a", 1.0f, 0.0f), token);

        // Act / Assert: filing the same identifier again names the defect
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.AddAsync(Memory("a", 0.0f, 1.0f), token));
    }

    /// <summary>
    ///     Proves an identifier the store does not hold is reported as a miss rather than as an
    ///     exception, so a tool can turn it into a refusal the model can read.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InMemoryMemoryStore_FindAsync_UnknownIdentifier_ReportsNull()
    {
        // Arrange: a store holding something else
        var store = new InMemoryMemoryStore();
        var token = TestContext.Current.CancellationToken;
        await store.AddAsync(Memory("a", 1.0f, 0.0f), token);

        // Act / Assert: the miss is an ordinary answer
        Assert.Null(await store.FindAsync("b", token));
    }

    /// <summary>
    ///     Proves a replacement keeps the memory where it was and reports that it landed, and that a
    ///     replacement of an identifier the store does not hold changes nothing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InMemoryMemoryStore_ReplaceAsync_KnownAndUnknownIdentifiers_ReportTheOutcome()
    {
        // Arrange: three memories in a stated order
        var store = new InMemoryMemoryStore();
        var token = TestContext.Current.CancellationToken;
        await store.AddAsync(Memory("a", 1.0f, 0.0f), token);
        await store.AddAsync(Memory("b", 0.0f, 1.0f), token);
        await store.AddAsync(Memory("c", 1.0f, 1.0f), token);

        // Act: replace the middle one, and attempt one that is not held
        var replaced = await store.ReplaceAsync(
            new MemoryRecord("b", "revised", "revised details", "new.md", "§9", new[] { 0.0f, 1.0f }),
            token);
        var missed = await store.ReplaceAsync(Memory("z", 1.0f, 0.0f), token);

        // Assert: the hit landed in place, the miss changed nothing
        Assert.True(replaced);
        Assert.False(missed);
        Assert.Equal(3, await store.CountAsync(token));

        var current = await store.FindAsync("b", token);
        Assert.NotNull(current);
        Assert.Equal("revised", current.Descriptor);
        Assert.Equal("new.md", current.SourceDocument);
    }

    /// <summary>
    ///     Proves removal reports whether anything was removed, so a tool never tells a model that a
    ///     fact is gone from a store that still returns it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InMemoryMemoryStore_RemoveAsync_KnownAndUnknownIdentifiers_ReportTheOutcome()
    {
        // Arrange: two memories
        var store = new InMemoryMemoryStore();
        var token = TestContext.Current.CancellationToken;
        await store.AddAsync(Memory("a", 1.0f, 0.0f), token);
        await store.AddAsync(Memory("b", 0.0f, 1.0f), token);

        // Act: remove one that exists and one that does not
        var removed = await store.RemoveAsync("a", token);
        var missed = await store.RemoveAsync("z", token);

        // Assert: the hit shrank the store, the miss left it alone
        Assert.True(removed);
        Assert.False(missed);
        Assert.Equal(1, await store.CountAsync(token));
        Assert.Null(await store.FindAsync("a", token));
    }

    /// <summary>
    ///     Proves a search reports cosine similarity, nearest first, and no more than the number of
    ///     matches asked for — the three properties the near-duplicate decision and recall both rely
    ///     on.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InMemoryMemoryStore_SearchAsync_ReportsCosineSimilarityNearestFirst()
    {
        // Arrange: three memories pointing in three known directions
        var store = new InMemoryMemoryStore();
        var token = TestContext.Current.CancellationToken;
        await store.AddAsync(Memory("orthogonal", 0.0f, 1.0f), token);
        await store.AddAsync(Memory("identical", 1.0f, 0.0f), token);
        await store.AddAsync(Memory("diagonal", 1.0f, 1.0f), token);

        // Act: search along the first axis
        var matches = await store.SearchAsync(new[] { 2.0f, 0.0f }, 2, token);

        // Assert: nearest first, capped at the requested count, with cosine as the score
        Assert.Equal(2, matches.Count);
        Assert.Equal("identical", matches[0].Memory.Id);
        Assert.Equal(1.0, matches[0].Similarity, 6);
        Assert.Equal("diagonal", matches[1].Memory.Id);
        Assert.Equal(1.0 / Math.Sqrt(2.0), matches[1].Similarity, 6);
    }

    /// <summary>
    ///     Proves an empty store and a zero count both answer with no matches rather than failing,
    ///     because an empty answer is a true answer.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InMemoryMemoryStore_SearchAsync_EmptyStoreOrZeroCount_ReturnsNoMatches()
    {
        // Arrange: an empty store, and a store holding one memory
        var empty = new InMemoryMemoryStore();
        var populated = new InMemoryMemoryStore();
        var token = TestContext.Current.CancellationToken;
        await populated.AddAsync(Memory("a", 1.0f, 0.0f), token);

        // Act: search each
        var fromEmpty = await empty.SearchAsync(new[] { 1.0f, 0.0f }, 5, token);
        var noneWanted = await populated.SearchAsync(new[] { 1.0f, 0.0f }, 0, token);

        // Assert: both are empty rather than refused
        Assert.Empty(fromEmpty);
        Assert.Empty(noneWanted);
    }

    /// <summary>
    ///     Proves a vector of a different length is refused rather than scored, because mixing two
    ///     embedding models in one store produces numbers that look ordinary and mean nothing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InMemoryMemoryStore_MismatchedDimension_ThrowsArgumentException()
    {
        // Arrange: a store whose held vectors are two long
        var store = new InMemoryMemoryStore();
        var token = TestContext.Current.CancellationToken;
        await store.AddAsync(Memory("a", 1.0f, 0.0f), token);

        // Act / Assert: neither a longer file nor a longer search is silently scored
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.AddAsync(Memory("b", 1.0f, 0.0f, 1.0f), token));
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.SearchAsync(new[] { 1.0f, 0.0f, 1.0f }, 1, token));
    }

    /// <summary>
    ///     Proves a search for a vector holding no values is refused, because it has no direction to
    ///     compare.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InMemoryMemoryStore_SearchAsync_EmptyVector_ThrowsArgumentException()
    {
        // Arrange: any store
        var store = new InMemoryMemoryStore();
        var token = TestContext.Current.CancellationToken;

        // Act / Assert: a vector with nothing in it is a defect, not a query
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.SearchAsync(ReadOnlyMemory<float>.Empty, 1, token));
    }

    /// <summary>
    ///     Proves a memory whose vector is all zeros scores zero rather than dividing by zero, so a
    ///     degenerate memory cannot take a search down with it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InMemoryMemoryStore_SearchAsync_ZeroVector_ScoresZero()
    {
        // Arrange: a store holding a memory with no direction at all
        var store = new InMemoryMemoryStore();
        var token = TestContext.Current.CancellationToken;
        await store.AddAsync(Memory("degenerate", 0.0f, 0.0f), token);

        // Act: search against it
        var matches = await store.SearchAsync(new[] { 1.0f, 0.0f }, 1, token);

        // Assert: reported as maximally distant rather than as an error
        Assert.Equal(0.0, Assert.Single(matches).Similarity);
    }

    /// <summary>
    ///     Proves the store rejects a missing memory and a blank identifier as programming errors,
    ///     because a tool refuses those before the store is reached.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task InMemoryMemoryStore_Validation_NullOrEmptyArguments_Throw()
    {
        // Arrange: any store
        var store = new InMemoryMemoryStore();
        var token = TestContext.Current.CancellationToken;

        // Act / Assert: each malformed call names the defect at the point it was made
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.AddAsync(null!, token));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.ReplaceAsync(null!, token));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.FindAsync(string.Empty, token));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.RemoveAsync(string.Empty, token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await store.SearchAsync(new[] { 1.0f }, -1, token));
    }
}
