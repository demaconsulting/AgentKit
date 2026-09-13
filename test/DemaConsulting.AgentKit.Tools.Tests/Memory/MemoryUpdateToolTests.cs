using System.Text.Json;
using DemaConsulting.AgentKit.Tools.Memory;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Memory;

/// <summary>
///     Unit tests for the <see cref="MemoryUpdateTool"/> class.
/// </summary>
public class MemoryUpdateToolTests
{
    /// <summary>
    ///     Files one memory directly into a store so an update test starts from a known state.
    /// </summary>
    /// <param name="store">The store to file into.</param>
    /// <returns>A task that completes when the memory is held.</returns>
    private static async Task GivenOneMemoryAsync(IMemoryStore store)
    {
        var token = TestContext.Current.CancellationToken;
        var vector = (await new StubEmbeddingGenerator()
            .GenerateAsync(["Rear tyre pressure for touring."], options: null, token))[0].Vector;

        await store.AddAsync(
            new MemoryRecord(
                "mem-tyre",
                "Rear tyre pressure for touring.",
                "12 psi cold.",
                "01-initial.md",
                "Section 4.2",
                vector),
            token);
    }

    /// <summary>
    ///     Invokes the update tool.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="memoryId">The identifier to state, or null to omit it.</param>
    /// <param name="details">The details to state, or null to omit them.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> UpdateAsync(AIFunction tool, string? memoryId, string? details)
    {
        var arguments = new AIFunctionArguments();
        if (memoryId is not null)
        {
            arguments["memoryId"] = memoryId;
        }

        if (details is not null)
        {
            arguments["details"] = details;
        }

        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Proves an update replaces the details and leaves the descriptor, the provenance and the
    ///     vector exactly as they were.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryUpdateTool_Update_KnownMemory_ReplacesDetailsAlone()
    {
        // Arrange: a store holding one memory
        var store = new InMemoryMemoryStore();
        await GivenOneMemoryAsync(store);
        var before = await store.FindAsync("mem-tyre", TestContext.Current.CancellationToken);
        var tool = MemoryUpdateTool.Create(store);

        // Act: correct the payload
        var result = await UpdateAsync(tool, "mem-tyre", "12 psi cold, measured at the valve.");

        // Assert: the details changed and nothing else did
        var element = Assert.IsType<JsonElement>(result);
        Assert.True(element.GetProperty("updated").GetBoolean());

        var after = await store.FindAsync("mem-tyre", TestContext.Current.CancellationToken);
        Assert.NotNull(after);
        Assert.Equal("12 psi cold, measured at the valve.", after.Details);
        Assert.Equal(before!.Descriptor, after.Descriptor);
        Assert.Equal(before.SourceDocument, after.SourceDocument);
        Assert.Equal(before.SourceLocator, after.SourceLocator);
        Assert.Equal(before.Embedding.ToArray(), after.Embedding.ToArray());
    }

    /// <summary>
    ///     Proves the result echoes back what the memory is still filed under, which is the fact
    ///     most likely to be wrong in the model's own head after an update.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryUpdateTool_Update_ResultEchoesDescriptorAndProvenance()
    {
        // Arrange: a store holding one memory
        var store = new InMemoryMemoryStore();
        await GivenOneMemoryAsync(store);
        var tool = MemoryUpdateTool.Create(store);

        // Act: update
        var result = await UpdateAsync(tool, "mem-tyre", "18 psi cold.");

        // Assert: the unchanged parts are stated rather than left for the model to assume
        var element = Assert.IsType<JsonElement>(result);
        Assert.Equal("Rear tyre pressure for touring.", element.GetProperty("descriptor").GetString());
        Assert.Equal("01-initial.md", element.GetProperty("sourceDocument").GetString());
        Assert.Equal("Section 4.2", element.GetProperty("sourceLocator").GetString());
    }

    /// <summary>
    ///     Proves an identifier the store does not hold is a refusal that states what happened and
    ///     how many memories are held, and prescribes nothing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryUpdateTool_Update_UnknownIdentifier_IsARefusalStatingFactsOnly()
    {
        // Arrange: a store holding one memory
        var store = new InMemoryMemoryStore();
        await GivenOneMemoryAsync(store);
        var tool = MemoryUpdateTool.Create(store);

        // Act: name a memory that is not there
        var result = await UpdateAsync(tool, "mem-nope", "Some details.");

        // Assert: a refusal naming the facts and no remedy
        var refusal = Assert.IsType<string>(result);
        Assert.StartsWith("Denied (TargetNotFound)", refusal, StringComparison.Ordinal);
        Assert.Contains("mem-nope", refusal, StringComparison.Ordinal);
        Assert.Contains("1 memory", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(MemoryRecallTool.ToolName, refusal, StringComparison.Ordinal);
        Assert.DoesNotContain(MemoryFileTool.ToolName, refusal, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing identifier and missing details are returned refusals rather than
    ///     exceptions.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryUpdateTool_Update_MissingArguments_AreReturnedRefusals()
    {
        // Arrange: a store holding one memory
        var store = new InMemoryMemoryStore();
        await GivenOneMemoryAsync(store);
        var tool = MemoryUpdateTool.Create(store);

        // Act: omit each required argument in turn
        var noIdentifier = await UpdateAsync(tool, null, "Some details.");
        var noDetails = await UpdateAsync(tool, "mem-tyre", null);

        // Assert: both are refusals and the memory is untouched
        Assert.StartsWith("Denied (", Assert.IsType<string>(noIdentifier), StringComparison.Ordinal);
        Assert.StartsWith("Denied (", Assert.IsType<string>(noDetails), StringComparison.Ordinal);

        var unchanged = await store.FindAsync("mem-tyre", TestContext.Current.CancellationToken);
        Assert.Equal("12 psi cold.", unchanged?.Details);
    }

    /// <summary>
    ///     Proves the tool requires a store at construction and is published under the family's name.
    /// </summary>
    [Fact]
    public void MemoryUpdateTool_Create_RequiresAStoreAndCarriesItsPublishedName()
    {
        // Act / Assert: a missing store is named at the point it was omitted
        Assert.Throws<ArgumentNullException>(() => MemoryUpdateTool.Create(null!));

        var tool = MemoryUpdateTool.Create(new InMemoryMemoryStore());
        Assert.Equal("memory_update", MemoryUpdateTool.ToolName);
        Assert.Equal(MemoryUpdateTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }
}
