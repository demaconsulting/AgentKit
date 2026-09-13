using System.Text.Json;
using DemaConsulting.AgentKit.Tools.Memory;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Memory;

/// <summary>
///     Unit tests for the <see cref="MemoryForgetTool"/> class.
/// </summary>
public class MemoryForgetToolTests
{
    /// <summary>
    ///     Files two memories directly into a store so a removal test starts from a known state.
    /// </summary>
    /// <param name="store">The store to file into.</param>
    /// <returns>A task that completes when the memories are held.</returns>
    private static async Task GivenTwoMemoriesAsync(IMemoryStore store)
    {
        var token = TestContext.Current.CancellationToken;
        var generator = new StubEmbeddingGenerator();

        foreach (var (id, descriptor) in new[] { ("mem-a", "Alpha subject."), ("mem-b", "Beta subject.") })
        {
            var vector = (await generator.GenerateAsync([descriptor], options: null, token))[0].Vector;
            await store.AddAsync(new MemoryRecord(id, descriptor, "details", null, null, vector), token);
        }
    }

    /// <summary>
    ///     Invokes the forget tool.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="memoryId">The identifier to state, or null to omit it.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> ForgetAsync(AIFunction tool, string? memoryId)
    {
        var arguments = new AIFunctionArguments();
        if (memoryId is not null)
        {
            arguments["memoryId"] = memoryId;
        }

        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Proves a named memory is removed, that the removal is reported, and that the store's new
    ///     size is stated so the model need not recall to check its own removal.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryForgetTool_Forget_KnownMemory_RemovesItAndReportsTheStoreSize()
    {
        // Arrange: a store holding two memories
        var store = new InMemoryMemoryStore();
        await GivenTwoMemoriesAsync(store);
        var tool = MemoryForgetTool.Create(store);

        // Act: forget one of them
        var result = await ForgetAsync(tool, "mem-a");

        // Assert: removed, reported, and only the other one is left
        var element = Assert.IsType<JsonElement>(result);
        Assert.True(element.GetProperty("forgotten").GetBoolean());
        Assert.Equal("mem-a", element.GetProperty("memoryId").GetString());
        Assert.Equal(1, element.GetProperty("memoryCount").GetInt32());
        Assert.Null(await store.FindAsync("mem-a", TestContext.Current.CancellationToken));
        Assert.NotNull(await store.FindAsync("mem-b", TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves an identifier the store does not hold is refused rather than reported as a quiet
    ///     success, because a model told a fact is gone will stop expecting to see it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryForgetTool_Forget_UnknownIdentifier_IsARefusalAndRemovesNothing()
    {
        // Arrange: a store holding two memories
        var store = new InMemoryMemoryStore();
        await GivenTwoMemoriesAsync(store);
        var tool = MemoryForgetTool.Create(store);

        // Act: name a memory that is not there
        var result = await ForgetAsync(tool, "mem-nope");

        // Assert: a refusal stating the facts, and nothing removed
        var refusal = Assert.IsType<string>(result);
        Assert.StartsWith("Denied (TargetNotFound)", refusal, StringComparison.Ordinal);
        Assert.Contains("2 memories", refusal, StringComparison.Ordinal);
        Assert.Equal(2, await store.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a missing identifier is a returned refusal rather than an exception, so a
    ///     malformed removal never ends the agent's turn.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryForgetTool_Forget_MissingIdentifier_IsAReturnedRefusal()
    {
        // Arrange: a store holding two memories
        var store = new InMemoryMemoryStore();
        await GivenTwoMemoriesAsync(store);
        var tool = MemoryForgetTool.Create(store);

        // Act: name nothing
        var result = await ForgetAsync(tool, null);

        // Assert: a refusal, and both memories are still held
        Assert.StartsWith("Denied (", Assert.IsType<string>(result), StringComparison.Ordinal);
        Assert.Equal(2, await store.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves the tool removes one memory per call and publishes no way to ask for more, so a
    ///     bulk erasure cannot be expressed.
    /// </summary>
    [Fact]
    public void MemoryForgetTool_Create_PublishesOnlyASingleIdentifierParameter()
    {
        // Act: read the schema a model is shown
        var tool = MemoryForgetTool.Create(new InMemoryMemoryStore());
        var schema = tool.JsonSchema.ToString();

        // Assert: one identifier and nothing that could name a set
        Assert.Contains("memoryId", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("query", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("descriptor", schema, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the tool requires a store at construction and is published under the family's name.
    /// </summary>
    [Fact]
    public void MemoryForgetTool_Create_RequiresAStoreAndCarriesItsPublishedName()
    {
        // Act / Assert: a missing store is named at the point it was omitted
        Assert.Throws<ArgumentNullException>(() => MemoryForgetTool.Create(null!));

        var tool = MemoryForgetTool.Create(new InMemoryMemoryStore());
        Assert.Equal("memory_forget", MemoryForgetTool.ToolName);
        Assert.Equal(MemoryForgetTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }
}
