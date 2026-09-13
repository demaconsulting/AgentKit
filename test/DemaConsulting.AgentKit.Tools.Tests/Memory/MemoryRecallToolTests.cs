using System.Text.Json;
using DemaConsulting.AgentKit.Tools.Memory;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Memory;

/// <summary>
///     Unit tests for the <see cref="MemoryRecallTool"/> class.
/// </summary>
/// <remarks>
///     The embedding generator is the deterministic offline stub; the store is the real default
///     implementation, because what a recall returns is the interaction between the two.
/// </remarks>
public class MemoryRecallToolTests
{
    /// <summary>
    ///     Files a memory directly into a store, bypassing the file tool so a recall test does not
    ///     depend on the near-duplicate rules of a sibling unit.
    /// </summary>
    /// <param name="store">The store to file into.</param>
    /// <param name="generator">The generator producing the descriptor's vector.</param>
    /// <param name="id">The identifier the memory carries.</param>
    /// <param name="descriptor">The descriptor to embed.</param>
    /// <param name="details">The details to hold.</param>
    /// <returns>A task that completes when the memory is held.</returns>
    private static async Task GivenMemoryAsync(
        IMemoryStore store,
        StubEmbeddingGenerator generator,
        string id,
        string descriptor,
        string details)
    {
        var token = TestContext.Current.CancellationToken;
        var vector = (await generator.GenerateAsync([descriptor], options: null, token))[0].Vector;
        await store.AddAsync(
            new MemoryRecord(id, descriptor, details, "doc-" + id + ".md", "Section 1", vector),
            token);
    }

    /// <summary>
    ///     Invokes the recall tool.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="query">The query to state, or null to omit it.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> RecallAsync(AIFunction tool, string? query)
    {
        var arguments = new AIFunctionArguments();
        if (query is not null)
        {
            arguments["query"] = query;
        }

        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Proves a recall returns each match whole — descriptor, details and both provenance
    ///     fields — because a found memory that cannot be answered from is not a useful result.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryRecallTool_Recall_MatchingQuery_ReturnsDescriptorDetailsAndProvenance()
    {
        // Arrange: a store holding two unrelated memories
        var store = new InMemoryMemoryStore();
        var generator = new StubEmbeddingGenerator();
        await GivenMemoryAsync(store, generator, "mem-tyre", "Rear tyre pressure for touring.", "18 psi cold.");
        await GivenMemoryAsync(store, generator, "mem-oil", "Gearbox oil change interval.", "Every 20000 km.");
        var tool = MemoryRecallTool.Create(store, generator, MemoryOptions.Default);

        // Act: ask about one of them
        var result = await RecallAsync(tool, "Rear tyre pressure for touring.");

        // Assert: the nearest match comes back whole
        var element = Assert.IsType<JsonElement>(result);
        Assert.Equal(2, element.GetProperty("matchCount").GetInt32());

        var nearest = element.GetProperty("matches")[0];
        Assert.Equal("mem-tyre", nearest.GetProperty("memoryId").GetString());
        Assert.Equal("Rear tyre pressure for touring.", nearest.GetProperty("descriptor").GetString());
        Assert.Equal("18 psi cold.", nearest.GetProperty("details").GetString());
        Assert.Equal("doc-mem-tyre.md", nearest.GetProperty("sourceDocument").GetString());
        Assert.Equal("Section 1", nearest.GetProperty("sourceLocator").GetString());
        Assert.Equal(1.0, nearest.GetProperty("similarity").GetDouble(), 6);
    }

    /// <summary>
    ///     Proves the search runs against descriptors alone: a query matching a memory's details
    ///     word for word, but sharing nothing with its descriptor, does not rank it first.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryRecallTool_Recall_SearchesDescriptorsAndNotDetails()
    {
        // Arrange: a memory whose details share no words with its descriptor
        var store = new InMemoryMemoryStore();
        var generator = new StubEmbeddingGenerator();
        await GivenMemoryAsync(store, generator, "mem-a", "Tyre pressures.", "Fourteen sprockets per cassette.");
        await GivenMemoryAsync(store, generator, "mem-b", "Fourteen sprockets per cassette.", "Unrelated payload.");
        var tool = MemoryRecallTool.Create(store, generator, MemoryOptions.Default);

        // Act: query with the words that appear in one memory's details and the other's descriptor
        var result = await RecallAsync(tool, "Fourteen sprockets per cassette.");

        // Assert: the memory whose descriptor matched is the nearest, not the one whose details did
        var element = Assert.IsType<JsonElement>(result);
        Assert.Equal("mem-b", element.GetProperty("matches")[0].GetProperty("memoryId").GetString());
    }

    /// <summary>
    ///     Proves the author's configured count decides how many memories come back, and that no
    ///     argument on the tool lets the model spend more of the author's budget.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryRecallTool_Recall_RecallCountGovernsHowManyComeBack()
    {
        // Arrange: four memories and an author who wants two results
        var store = new InMemoryMemoryStore();
        var generator = new StubEmbeddingGenerator();
        for (var index = 0; index < 4; index++)
        {
            await GivenMemoryAsync(store, generator, "mem-" + index, "Subject number " + index + ".", "Details.");
        }

        var tool = MemoryRecallTool.Create(store, generator, new MemoryOptions(recallCount: 2));

        // Act: recall
        var result = await RecallAsync(tool, "Subject number 1.");

        // Assert: two matches, and the tool publishes no way to ask for more
        var element = Assert.IsType<JsonElement>(result);
        Assert.Equal(2, element.GetProperty("matchCount").GetInt32());
        Assert.DoesNotContain("count", tool.JsonSchema.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Proves an empty store answers with an empty match list rather than a refusal, so a model
    ///     does not conclude the tool is broken the first time it asks.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryRecallTool_Recall_EmptyStore_ReturnsNoMatchesWithoutEmbedding()
    {
        // Arrange: an empty store
        var store = new InMemoryMemoryStore();
        var generator = new StubEmbeddingGenerator();
        var tool = MemoryRecallTool.Create(store, generator, MemoryOptions.Default);

        // Act: recall against nothing
        var result = await RecallAsync(tool, "Anything at all.");

        // Assert: a true empty answer, and no embedding call was spent reaching it
        var element = Assert.IsType<JsonElement>(result);
        Assert.Equal(0, element.GetProperty("matchCount").GetInt32());
        Assert.Empty(element.GetProperty("matches").EnumerateArray());
        Assert.Equal(0, generator.CallCount);
    }

    /// <summary>
    ///     Proves a missing query is a returned refusal rather than an exception.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryRecallTool_Recall_MissingQuery_IsAReturnedRefusal()
    {
        // Arrange: any store
        var tool = MemoryRecallTool.Create(
            new InMemoryMemoryStore(),
            new StubEmbeddingGenerator(),
            MemoryOptions.Default);

        // Act: ask for nothing
        var result = await RecallAsync(tool, null);

        // Assert: a refusal the model can read and act on
        Assert.StartsWith("Denied (", Assert.IsType<string>(result), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the tool requires each of its collaborators at construction.
    /// </summary>
    [Fact]
    public void MemoryRecallTool_Create_MissingCollaborator_ThrowsArgumentNullException()
    {
        // Arrange: one real collaborator of each kind
        var store = new InMemoryMemoryStore();
        var generator = new StubEmbeddingGenerator();

        // Act / Assert: each omission is named
        Assert.Throws<ArgumentNullException>(
            () => MemoryRecallTool.Create(null!, generator, MemoryOptions.Default));
        Assert.Throws<ArgumentNullException>(
            () => MemoryRecallTool.Create(store, null!, MemoryOptions.Default));
        Assert.Throws<ArgumentNullException>(
            () => MemoryRecallTool.Create(store, generator, null!));
    }

    /// <summary>
    ///     Proves the tool is published under the name the family claims and carries a description.
    /// </summary>
    [Fact]
    public void MemoryRecallTool_Create_CarriesItsPublishedNameAndDescription()
    {
        // Act: construct the tool
        var tool = MemoryRecallTool.Create(
            new InMemoryMemoryStore(),
            new StubEmbeddingGenerator(),
            MemoryOptions.Default);

        // Assert: the published name and a non-empty description
        Assert.Equal("memory_recall", MemoryRecallTool.ToolName);
        Assert.Equal(MemoryRecallTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }
}
