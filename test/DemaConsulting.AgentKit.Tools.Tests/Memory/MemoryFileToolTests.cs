using System.Text.Json;
using DemaConsulting.AgentKit.Tools.Memory;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Memory;

/// <summary>
///     Unit tests for the <see cref="MemoryFileTool"/> class.
/// </summary>
/// <remarks>
///     The embedding generator is a deterministic offline stub; see
///     <see cref="StubEmbeddingGenerator"/> for why a real backend would prove the wrong thing. The
///     store is the real default implementation, because the near-duplicate decision is the
///     interaction between the tool and a store and a substitute would hide it.
/// </remarks>
public class MemoryFileToolTests
{
    /// <summary>
    ///     Files one memory through the tool and returns the raw result.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="descriptor">The descriptor to file, or null to omit it.</param>
    /// <param name="details">The details to file, or null to omit them.</param>
    /// <param name="sourceDocument">The source document to state, or null to omit it.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> FileAsync(
        AIFunction tool,
        string? descriptor,
        string? details,
        string? sourceDocument = null)
    {
        var arguments = new AIFunctionArguments();
        if (descriptor is not null)
        {
            arguments["descriptor"] = descriptor;
        }

        if (details is not null)
        {
            arguments["details"] = details;
        }

        if (sourceDocument is not null)
        {
            arguments["sourceDocument"] = sourceDocument;
        }

        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Proves a filed memory is stored, reported as stored, and given an identifier the model
    ///     can address it by afterwards.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryFileTool_File_NewDescriptor_StoresTheMemory()
    {
        // Arrange: an empty store and the file tool over it
        var store = new InMemoryMemoryStore();
        var tool = MemoryFileTool.Create(store, new StubEmbeddingGenerator(), MemoryOptions.Default);

        // Act: file one memory
        var result = await FileAsync(tool, "Rear tyre pressure for the touring model.", "18 psi cold.");

        // Assert: stored, identified and counted
        var element = Assert.IsType<JsonElement>(result);
        Assert.True(element.GetProperty("stored").GetBoolean());
        Assert.StartsWith("mem-", element.GetProperty("memoryId").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, element.GetProperty("memoryCount").GetInt32());
        Assert.Equal(1, await store.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves only the descriptor is embedded: two memories whose details differ wildly but
    ///     whose descriptors are unrelated are both stored, because details never reach the vector.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryFileTool_File_OnlyTheDescriptorIsEmbedded()
    {
        // Arrange: a generator that counts how many texts it is asked to embed
        var store = new InMemoryMemoryStore();
        var generator = new StubEmbeddingGenerator();
        var tool = MemoryFileTool.Create(store, generator, MemoryOptions.Default);

        // Act: file one memory whose details are long and whose descriptor is short
        await FileAsync(tool, "Rear tyre pressure.", "A very long passage that shares no words at all.");

        // Assert: exactly one text was embedded, and the memory holds the descriptor's vector
        Assert.Equal(1, generator.CallCount);
        var stored = Assert.Single(
            await store.SearchAsync(
                (await new StubEmbeddingGenerator().GenerateAsync(
                    ["Rear tyre pressure."],
                    options: null,
                    TestContext.Current.CancellationToken))[0].Vector,
                1,
                TestContext.Current.CancellationToken));
        Assert.Equal(1.0, stored.Similarity, 6);
    }

    /// <summary>
    ///     Proves a descriptor at or above the configured threshold is not stored, and that the
    ///     result says so in a field rather than in a sentence.
    /// </summary>
    /// <remarks>
    ///     This is the defect fix: the spike returned prose here and the model went on to assert it
    ///     had stored the fact.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryFileTool_File_NearDuplicateDescriptor_IsNotStoredAndNamesTheConflict()
    {
        // Arrange: a store already holding a statement of one fact
        var store = new InMemoryMemoryStore();
        var tool = MemoryFileTool.Create(store, new StubEmbeddingGenerator(), MemoryOptions.Default);
        var first = await FileAsync(tool, "The rear tyre pressure for the touring model.", "12 psi cold.");
        var firstId = Assert.IsType<JsonElement>(first).GetProperty("memoryId").GetString();

        // Act: file a contradicting statement of the same fact
        var result = await FileAsync(tool, "The rear tyre pressure for the touring model.", "18 psi cold.");

        // Assert: not stored, and the conflict is named in fields the model cannot misread
        var element = Assert.IsType<JsonElement>(result);
        Assert.False(element.GetProperty("stored").GetBoolean());
        Assert.Equal("near_duplicate", element.GetProperty("reason").GetString());
        Assert.Equal(firstId, element.GetProperty("conflictingMemoryId").GetString());
        Assert.Equal(
            "The rear tyre pressure for the touring model.",
            element.GetProperty("conflictingDescriptor").GetString());
        Assert.Equal("12 psi cold.", element.GetProperty("conflictingDetails").GetString());
        Assert.True(element.GetProperty("similarity").GetDouble() >= 0.88);
        Assert.Equal(0.88, element.GetProperty("threshold").GetDouble());
        Assert.Equal(1, await store.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves the near-duplicate result prescribes no remedy, which is the project-wide rule
    ///     that a tool result states facts and never tells a model what to do next.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryFileTool_File_NearDuplicateResult_NamesNoOtherTool()
    {
        // Arrange: a store already holding the fact
        var store = new InMemoryMemoryStore();
        var tool = MemoryFileTool.Create(store, new StubEmbeddingGenerator(), MemoryOptions.Default);
        await FileAsync(tool, "The rear tyre pressure for the touring model.", "12 psi cold.");

        // Act: provoke the near-duplicate result
        var result = await FileAsync(tool, "The rear tyre pressure for the touring model.", "18 psi cold.");

        // Assert: no sibling tool is named anywhere in the result
        var rendered = Assert.IsType<JsonElement>(result).GetRawText();
        Assert.DoesNotContain(MemoryReviseTool.ToolName, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(MemoryUpdateTool.ToolName, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(MemoryForgetTool.ToolName, rendered, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the author's threshold is what decides, not a constant inside the tool: the same
    ///     pair of descriptors is a duplicate under one configuration and not under another.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryFileTool_File_ThresholdGovernsTheDecision()
    {
        // Arrange: two stores, identical but for the author's configured threshold
        var strictStore = new InMemoryMemoryStore();
        var looseStore = new InMemoryMemoryStore();
        var strict = MemoryFileTool.Create(
            strictStore,
            new StubEmbeddingGenerator(),
            new MemoryOptions(nearDuplicateThreshold: 1.0));
        var loose = MemoryFileTool.Create(
            looseStore,
            new StubEmbeddingGenerator(),
            new MemoryOptions(nearDuplicateThreshold: 0.2));

        await FileAsync(strict, "The rear tyre pressure for the touring model.", "12 psi.");
        await FileAsync(loose, "The rear tyre pressure for the touring model.", "12 psi.");

        // Act: file a partly overlapping descriptor against each
        var strictResult = await FileAsync(strict, "The front tyre pressure for the racing model.", "20 psi.");
        var looseResult = await FileAsync(loose, "The front tyre pressure for the racing model.", "20 psi.");

        // Assert: the same texts land differently because the author configured differently
        Assert.True(Assert.IsType<JsonElement>(strictResult).GetProperty("stored").GetBoolean());
        Assert.False(Assert.IsType<JsonElement>(looseResult).GetProperty("stored").GetBoolean());
    }

    /// <summary>
    ///     Proves the provenance a model states is held, and that a blank one is held as an absence
    ///     rather than as a source that says nothing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryFileTool_File_Provenance_IsHeldAsStated()
    {
        // Arrange: an empty store
        var store = new InMemoryMemoryStore();
        var tool = MemoryFileTool.Create(store, new StubEmbeddingGenerator(), MemoryOptions.Default);

        // Act: file one memory citing a document and one citing nothing
        var cited = await FileAsync(tool, "Alpha subject matter.", "Alpha details.", "01-initial.md");
        var blankSource = await FileAsync(tool, "Beta unrelated topic.", "Beta details.", "   ");

        // Assert: the citation is held, the blank one is an honest absence
        var citedMemory = await store.FindAsync(
            Assert.IsType<JsonElement>(cited).GetProperty("memoryId").GetString()!,
            TestContext.Current.CancellationToken);
        var blankSourceMemory = await store.FindAsync(
            Assert.IsType<JsonElement>(blankSource).GetProperty("memoryId").GetString()!,
            TestContext.Current.CancellationToken);

        Assert.Equal("01-initial.md", citedMemory?.SourceDocument);
        Assert.Null(blankSourceMemory?.SourceDocument);
    }

    /// <summary>
    ///     Proves a missing descriptor and missing details are returned refusals rather than
    ///     exceptions, so a malformed call never ends the agent's turn.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryFileTool_File_MissingArguments_AreReturnedRefusals()
    {
        // Arrange: an empty store
        var store = new InMemoryMemoryStore();
        var tool = MemoryFileTool.Create(store, new StubEmbeddingGenerator(), MemoryOptions.Default);

        // Act: omit each required argument in turn
        var noDescriptor = await FileAsync(tool, null, "Some details.");
        var noDetails = await FileAsync(tool, "A descriptor.", null);

        // Assert: both came back as refusal text, and nothing was stored
        Assert.StartsWith("Denied (", Assert.IsType<string>(noDescriptor), StringComparison.Ordinal);
        Assert.StartsWith("Denied (", Assert.IsType<string>(noDetails), StringComparison.Ordinal);
        Assert.Equal(0, await store.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves an embedding backend that answers without producing a vector is reported to the
    ///     host rather than turned into a refusal the model would keep retrying against.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryFileTool_File_GeneratorProducesNothing_ThrowsInvalidOperationException()
    {
        // Arrange: a generator standing in for a misbehaving backend
        var store = new InMemoryMemoryStore();
        var tool = MemoryFileTool.Create(
            store,
            new StubEmbeddingGenerator(producesNothing: true),
            MemoryOptions.Default);

        // Act / Assert: the host fault surfaces as a fault
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await FileAsync(tool, "A descriptor.", "Some details."));
    }

    /// <summary>
    ///     Proves the tool requires each of its collaborators at construction, so a composition that
    ///     forgot one is told at the point it forgot.
    /// </summary>
    [Fact]
    public void MemoryFileTool_Create_MissingCollaborator_ThrowsArgumentNullException()
    {
        // Arrange: one real collaborator of each kind
        var store = new InMemoryMemoryStore();
        var generator = new StubEmbeddingGenerator();

        // Act / Assert: each omission is named
        Assert.Throws<ArgumentNullException>(
            () => MemoryFileTool.Create(null!, generator, MemoryOptions.Default));
        Assert.Throws<ArgumentNullException>(
            () => MemoryFileTool.Create(store, null!, MemoryOptions.Default));
        Assert.Throws<ArgumentNullException>(
            () => MemoryFileTool.Create(store, generator, null!));
    }

    /// <summary>
    ///     Proves the tool is published under the name the family claims and carries a description
    ///     the model can choose it by.
    /// </summary>
    [Fact]
    public void MemoryFileTool_Create_CarriesItsPublishedNameAndDescription()
    {
        // Act: construct the tool
        var tool = MemoryFileTool.Create(
            new InMemoryMemoryStore(),
            new StubEmbeddingGenerator(),
            MemoryOptions.Default);

        // Assert: the published name and a non-empty description
        Assert.Equal("memory_file", MemoryFileTool.ToolName);
        Assert.Equal(MemoryFileTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }
}
