using System.Text.Json;
using DemaConsulting.AgentKit.Tools.Memory;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Memory;

/// <summary>
///     Unit tests for the <see cref="MemoryReviseTool"/> class.
/// </summary>
/// <remarks>
///     The provenance scenarios below are regression tests for a defect observed in the spike this
///     family comes from: a revision silently preserved the original source, so a fact revised from
///     a different document went on citing the superseded one.
/// </remarks>
public class MemoryReviseToolTests
{
    /// <summary>
    ///     Files one memory directly into a store so a revision test starts from a known state.
    /// </summary>
    /// <param name="store">The store to file into.</param>
    /// <param name="generator">The generator producing the descriptor's vector.</param>
    /// <returns>A task that completes when the memory is held.</returns>
    private static async Task GivenOneMemoryAsync(IMemoryStore store, StubEmbeddingGenerator generator)
    {
        var token = TestContext.Current.CancellationToken;
        var vector = (await generator
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
    ///     Invokes the revise tool.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="memoryId">The identifier to state, or null to omit it.</param>
    /// <param name="descriptor">The descriptor to state, or null to omit it.</param>
    /// <param name="details">The details to state, or null to omit them.</param>
    /// <param name="sourceDocument">The source document to state, or null to omit it.</param>
    /// <param name="sourceLocator">The source locator to state, or null to omit it.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> ReviseAsync(
        AIFunction tool,
        string? memoryId,
        string? descriptor,
        string? details,
        string? sourceDocument = null,
        string? sourceLocator = null)
    {
        var arguments = new AIFunctionArguments();
        if (memoryId is not null)
        {
            arguments["memoryId"] = memoryId;
        }

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

        if (sourceLocator is not null)
        {
            arguments["sourceLocator"] = sourceLocator;
        }

        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Proves a revision replaces the descriptor, the details and the vector, so the memory is
    ///     found by what it now says rather than by what it used to say.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryReviseTool_Revise_KnownMemory_ReplacesDescriptorDetailsAndVector()
    {
        // Arrange: a store holding one memory
        var store = new InMemoryMemoryStore();
        var generator = new StubEmbeddingGenerator();
        await GivenOneMemoryAsync(store, generator);
        var before = await store.FindAsync("mem-tyre", TestContext.Current.CancellationToken);
        var tool = MemoryReviseTool.Create(store, generator);

        // Act: revise the memory onto a different subject
        var result = await ReviseAsync(
            tool,
            "mem-tyre",
            "Gearbox oil change interval.",
            "Every 20000 km.",
            "02-revision.md",
            "Section 9");

        // Assert: everything the memory says, and how it is found, has changed
        Assert.True(Assert.IsType<JsonElement>(result).GetProperty("revised").GetBoolean());

        var after = await store.FindAsync("mem-tyre", TestContext.Current.CancellationToken);
        Assert.NotNull(after);
        Assert.Equal("Gearbox oil change interval.", after.Descriptor);
        Assert.Equal("Every 20000 km.", after.Details);
        Assert.Equal("mem-tyre", after.Id);
        Assert.NotEqual(before!.Embedding.ToArray(), after.Embedding.ToArray());
    }

    /// <summary>
    ///     Proves a revision from a different document replaces the citation instead of silently
    ///     keeping the superseded one. This is the defect this tool was built to fix.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryReviseTool_Revise_StatedProvenance_ReplacesTheSupersededSource()
    {
        // Arrange: a memory read from the first revision of a document
        var store = new InMemoryMemoryStore();
        var generator = new StubEmbeddingGenerator();
        await GivenOneMemoryAsync(store, generator);
        var tool = MemoryReviseTool.Create(store, generator);

        // Act: revise it from a later document
        var result = await ReviseAsync(
            tool,
            "mem-tyre",
            "Rear tyre pressure for touring, revised.",
            "18 psi cold.",
            "02-revision.md",
            "Section 4.2");

        // Assert: the memory cites the document it now reflects, in the store and in the result
        var after = await store.FindAsync("mem-tyre", TestContext.Current.CancellationToken);
        Assert.Equal("02-revision.md", after?.SourceDocument);

        var element = Assert.IsType<JsonElement>(result);
        Assert.True(element.GetProperty("sourceStated").GetBoolean());
        Assert.Equal("02-revision.md", element.GetProperty("sourceDocument").GetString());
        Assert.Equal("Section 4.2", element.GetProperty("sourceLocator").GetString());
    }

    /// <summary>
    ///     Proves an omitted source clears the citation rather than inheriting the old one, because
    ///     inheriting silently is the original defect in a different form.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryReviseTool_Revise_OmittedProvenance_ClearsTheSourceAndReportsIt()
    {
        // Arrange: a memory that currently cites a document
        var store = new InMemoryMemoryStore();
        var generator = new StubEmbeddingGenerator();
        await GivenOneMemoryAsync(store, generator);
        var tool = MemoryReviseTool.Create(store, generator);

        // Act: revise without stating any source
        var result = await ReviseAsync(tool, "mem-tyre", "Rear tyre pressure, with no source.", "18 psi cold.");

        // Assert: no stale citation survives, and the absence is reported rather than assumed
        var after = await store.FindAsync("mem-tyre", TestContext.Current.CancellationToken);
        Assert.Null(after?.SourceDocument);
        Assert.Null(after?.SourceLocator);

        var element = Assert.IsType<JsonElement>(result);
        Assert.False(element.GetProperty("sourceStated").GetBoolean());
        Assert.False(element.TryGetProperty("sourceDocument", out _));
        Assert.False(element.TryGetProperty("sourceLocator", out _));
    }

    /// <summary>
    ///     Proves a revision is not near-duplicate checked, so a memory can be revised into wording
    ///     close to its own — which is what most corrections look like.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryReviseTool_Revise_DescriptorCloseToItsOwn_IsNotRefusedAsADuplicate()
    {
        // Arrange: a store holding one memory
        var store = new InMemoryMemoryStore();
        var generator = new StubEmbeddingGenerator();
        await GivenOneMemoryAsync(store, generator);
        var tool = MemoryReviseTool.Create(store, generator);

        // Act: revise to the identical descriptor with corrected details
        var result = await ReviseAsync(tool, "mem-tyre", "Rear tyre pressure for touring.", "18 psi cold.");

        // Assert: the revision landed rather than being refused against the memory it is revising
        Assert.True(Assert.IsType<JsonElement>(result).GetProperty("revised").GetBoolean());
        var after = await store.FindAsync("mem-tyre", TestContext.Current.CancellationToken);
        Assert.Equal("18 psi cold.", after?.Details);
        Assert.Equal(1, await store.CountAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves an identifier the store does not hold is refused without an embedding call being
    ///     spent on it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryReviseTool_Revise_UnknownIdentifier_IsRefusedWithoutEmbedding()
    {
        // Arrange: a store holding one memory, and a generator counting its calls
        var store = new InMemoryMemoryStore();
        var setup = new StubEmbeddingGenerator();
        await GivenOneMemoryAsync(store, setup);
        var generator = new StubEmbeddingGenerator();
        var tool = MemoryReviseTool.Create(store, generator);

        // Act: name a memory that is not there
        var result = await ReviseAsync(tool, "mem-nope", "A new subject.", "New details.");

        // Assert: a refusal, and nothing was embedded to produce it
        Assert.StartsWith("Denied (TargetNotFound)", Assert.IsType<string>(result), StringComparison.Ordinal);
        Assert.Equal(0, generator.CallCount);
    }

    /// <summary>
    ///     Proves each missing argument is a returned refusal rather than an exception, and that the
    ///     memory is left exactly as it was.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryReviseTool_Revise_MissingArguments_AreReturnedRefusals()
    {
        // Arrange: a store holding one memory
        var store = new InMemoryMemoryStore();
        var generator = new StubEmbeddingGenerator();
        await GivenOneMemoryAsync(store, generator);
        var tool = MemoryReviseTool.Create(store, generator);

        // Act: omit each required argument in turn
        var noIdentifier = await ReviseAsync(tool, null, "A descriptor.", "Some details.");
        var noDescriptor = await ReviseAsync(tool, "mem-tyre", null, "Some details.");
        var noDetails = await ReviseAsync(tool, "mem-tyre", "A descriptor.", null);

        // Assert: all three are refusals and the memory is untouched
        Assert.StartsWith("Denied (", Assert.IsType<string>(noIdentifier), StringComparison.Ordinal);
        Assert.StartsWith("Denied (", Assert.IsType<string>(noDescriptor), StringComparison.Ordinal);
        Assert.StartsWith("Denied (", Assert.IsType<string>(noDetails), StringComparison.Ordinal);

        var unchanged = await store.FindAsync("mem-tyre", TestContext.Current.CancellationToken);
        Assert.Equal("12 psi cold.", unchanged?.Details);
        Assert.Equal("01-initial.md", unchanged?.SourceDocument);
    }

    /// <summary>
    ///     Proves the tool publishes both provenance parameters, so a model can state a source at
    ///     all — the absence of which was the original defect.
    /// </summary>
    [Fact]
    public void MemoryReviseTool_Create_PublishesProvenanceParameters()
    {
        // Act: construct the tool and read the schema a model is shown
        var tool = MemoryReviseTool.Create(new InMemoryMemoryStore(), new StubEmbeddingGenerator());
        var schema = tool.JsonSchema.ToString();

        // Assert: both provenance parameters are offered to the model
        Assert.Contains("sourceDocument", schema, StringComparison.Ordinal);
        Assert.Contains("sourceLocator", schema, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the tool requires its collaborators at construction and is published under the
    ///     family's name.
    /// </summary>
    [Fact]
    public void MemoryReviseTool_Create_RequiresCollaboratorsAndCarriesItsPublishedName()
    {
        // Act / Assert: each omission is named at the point it was made
        Assert.Throws<ArgumentNullException>(
            () => MemoryReviseTool.Create(null!, new StubEmbeddingGenerator()));
        Assert.Throws<ArgumentNullException>(
            () => MemoryReviseTool.Create(new InMemoryMemoryStore(), null!));

        var tool = MemoryReviseTool.Create(new InMemoryMemoryStore(), new StubEmbeddingGenerator());
        Assert.Equal("memory_revise", MemoryReviseTool.ToolName);
        Assert.Equal(MemoryReviseTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }
}
