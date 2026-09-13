using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Memory;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Memory;

/// <summary>
///     Subsystem-level integration tests for the memory tool family.
/// </summary>
/// <remarks>
///     These scenarios exercise the family the way an application does: composed through the
///     AgentKitCore <see cref="ToolPackBuilder"/> under one access policy and one embedding
///     generator, then invoked through the published tool list. They assert the family-wide
///     properties — one prefix, one store shared by the five tools, a fact that can be filed,
///     found, corrected, re-sourced and dropped, and refusals that come back as values.
/// </remarks>
public class MemoryTests
{
    /// <summary>
    ///     Composes the family the way an application does.
    /// </summary>
    /// <param name="generator">The embedding generator the application supplies.</param>
    /// <param name="options">The controls the author configures, or null for the defaults.</param>
    /// <returns>The published tool list.</returns>
    private static IReadOnlyList<AIFunction> Compose(
        StubEmbeddingGenerator generator,
        MemoryOptions? options = null)
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        return new ToolPackBuilder(policy).Add(new MemoryPack(generator, options)).Build();
    }

    /// <summary>
    ///     Invokes one published tool by name.
    /// </summary>
    /// <param name="tools">The published tool list.</param>
    /// <param name="name">The name of the tool to invoke.</param>
    /// <param name="arguments">The arguments to state.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeAsync(
        IReadOnlyList<AIFunction> tools,
        string name,
        AIFunctionArguments arguments)
    {
        return await tools.Single(tool => tool.Name == name)
            .InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Proves a composition attaching the family publishes the five tools under one prefix.
    /// </summary>
    [Fact]
    public void Memory_Family_ComposedThroughBuilder_PublishesTheFamily()
    {
        // Arrange / Act: attach the family
        var tools = Compose(new StubEmbeddingGenerator());

        // Assert: five tools, in the order the pack states
        Assert.Equal(
            [
                MemoryFileTool.ToolName,
                MemoryRecallTool.ToolName,
                MemoryUpdateTool.ToolName,
                MemoryReviseTool.ToolName,
                MemoryForgetTool.ToolName
            ],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves every tool in the family carries a valid name and a description.
    /// </summary>
    [Fact]
    public void Memory_Family_EveryTool_CarriesAValidatedNameAndDescription()
    {
        // Arrange / Act: attach the family
        var tools = Compose(new StubEmbeddingGenerator());

        // Assert: each name is well formed, carries the family prefix, and is described
        Assert.All(tools, tool =>
        {
            ToolName.Validate(tool.Name);
            Assert.StartsWith(MemoryPack.FamilyPrefix + "_", tool.Name, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        });
    }

    /// <summary>
    ///     Proves a fact can be filed, found again, corrected, re-sourced and dropped through the
    ///     published tools alone, with every tool observing the same store.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Memory_Family_AFact_IsFiledRecalledCorrectedAndForgotten()
    {
        // Arrange: the family as an agent receives it
        var tools = Compose(new StubEmbeddingGenerator());

        // Act: file a fact
        var filed = await InvokeAsync(
            tools,
            MemoryFileTool.ToolName,
            new AIFunctionArguments
            {
                ["descriptor"] = "Rear tyre pressure for the touring model.",
                ["details"] = "12 psi cold.",
                ["sourceDocument"] = "01-initial.md",
                ["sourceLocator"] = "Section 4.2",
            });
        var memoryId = Assert.IsType<JsonElement>(filed).GetProperty("memoryId").GetString();

        // ... correct what it says, without changing what it is about
        await InvokeAsync(
            tools,
            MemoryUpdateTool.ToolName,
            new AIFunctionArguments
            {
                ["memoryId"] = memoryId!,
                ["details"] = "12 psi cold, measured at the valve.",
            });

        // ... then re-source it from a later document
        await InvokeAsync(
            tools,
            MemoryReviseTool.ToolName,
            new AIFunctionArguments
            {
                ["memoryId"] = memoryId!,
                ["descriptor"] = "Rear tyre pressure for the touring model.",
                ["details"] = "18 psi cold, measured at the valve.",
                ["sourceDocument"] = "02-revision.md",
                ["sourceLocator"] = "Section 4.2",
            });

        var recalled = await InvokeAsync(
            tools,
            MemoryRecallTool.ToolName,
            new AIFunctionArguments { ["query"] = "Rear tyre pressure for the touring model." });

        // Assert: one store, seen the same way by every tool, carrying the revised fact and the
        // document that fact actually came from
        var match = Assert.IsType<JsonElement>(recalled).GetProperty("matches")[0];
        Assert.Equal(memoryId, match.GetProperty("memoryId").GetString());
        Assert.Equal("18 psi cold, measured at the valve.", match.GetProperty("details").GetString());
        Assert.Equal("02-revision.md", match.GetProperty("sourceDocument").GetString());

        // Act: drop it
        var forgotten = await InvokeAsync(
            tools,
            MemoryForgetTool.ToolName,
            new AIFunctionArguments { ["memoryId"] = memoryId! });

        var afterForgetting = await InvokeAsync(
            tools,
            MemoryRecallTool.ToolName,
            new AIFunctionArguments { ["query"] = "Rear tyre pressure for the touring model." });

        // Assert: the store is empty and says so rather than refusing
        Assert.Equal(0, Assert.IsType<JsonElement>(forgotten).GetProperty("memoryCount").GetInt32());
        Assert.Equal(0, Assert.IsType<JsonElement>(afterForgetting).GetProperty("matchCount").GetInt32());
    }

    /// <summary>
    ///     Proves the family stops a contradicting restatement of a fact it already holds, and hands
    ///     the model the memory it conflicts with rather than a sentence about it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Memory_Family_ContradictingRestatement_IsNotStoredAndNamesTheConflict()
    {
        // Arrange: the family, already holding one statement of a fact
        var tools = Compose(new StubEmbeddingGenerator());
        await InvokeAsync(
            tools,
            MemoryFileTool.ToolName,
            new AIFunctionArguments
            {
                ["descriptor"] = "Rear tyre pressure for the touring model.",
                ["details"] = "12 psi cold.",
            });

        // Act: file a contradicting statement of the same fact
        var second = await InvokeAsync(
            tools,
            MemoryFileTool.ToolName,
            new AIFunctionArguments
            {
                ["descriptor"] = "Rear tyre pressure for the touring model.",
                ["details"] = "18 psi cold.",
            });

        // Assert: nothing stored, and the conflict handed over in fields
        var element = Assert.IsType<JsonElement>(second);
        Assert.False(element.GetProperty("stored").GetBoolean());
        Assert.Equal("12 psi cold.", element.GetProperty("conflictingDetails").GetString());

        var recalled = await InvokeAsync(
            tools,
            MemoryRecallTool.ToolName,
            new AIFunctionArguments { ["query"] = "Rear tyre pressure for the touring model." });
        Assert.Equal(1, Assert.IsType<JsonElement>(recalled).GetProperty("matchCount").GetInt32());
    }

    /// <summary>
    ///     Proves a correction that does not change what a memory is about costs no embedding call,
    ///     while a revision does — the distinction the two tools exist to draw.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Memory_Family_UpdateCostsNoEmbeddingCallAndRevisionDoes()
    {
        // Arrange: the family over a generator counting how often it is asked to embed
        var generator = new StubEmbeddingGenerator();
        var tools = Compose(generator);
        var filed = await InvokeAsync(
            tools,
            MemoryFileTool.ToolName,
            new AIFunctionArguments { ["descriptor"] = "Alpha subject.", ["details"] = "Alpha details." });
        var memoryId = Assert.IsType<JsonElement>(filed).GetProperty("memoryId").GetString();
        var afterFiling = generator.CallCount;

        // Act: correct the details, then change the subject
        await InvokeAsync(
            tools,
            MemoryUpdateTool.ToolName,
            new AIFunctionArguments { ["memoryId"] = memoryId!, ["details"] = "Corrected details." });
        var afterUpdate = generator.CallCount;

        await InvokeAsync(
            tools,
            MemoryReviseTool.ToolName,
            new AIFunctionArguments
            {
                ["memoryId"] = memoryId!,
                ["descriptor"] = "Beta subject entirely.",
                ["details"] = "Beta details.",
            });
        var afterRevision = generator.CallCount;

        // Assert: the update embedded nothing; the revision embedded exactly the new descriptor
        Assert.Equal(afterFiling, afterUpdate);
        Assert.Equal(afterUpdate + 1, afterRevision);
    }

    /// <summary>
    ///     Proves a revision makes a memory findable by what it now says rather than by what it used
    ///     to say.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Memory_Family_RevisedMemory_IsFoundByItsNewWording()
    {
        // Arrange: the family holding one memory
        var tools = Compose(new StubEmbeddingGenerator());
        var filed = await InvokeAsync(
            tools,
            MemoryFileTool.ToolName,
            new AIFunctionArguments { ["descriptor"] = "Tyre pressures.", ["details"] = "12 psi." });
        var memoryId = Assert.IsType<JsonElement>(filed).GetProperty("memoryId").GetString();

        // Act: revise it onto an entirely different subject, then search for that subject
        await InvokeAsync(
            tools,
            MemoryReviseTool.ToolName,
            new AIFunctionArguments
            {
                ["memoryId"] = memoryId!,
                ["descriptor"] = "Gearbox oil intervals.",
                ["details"] = "Every 20000 km.",
            });

        var byNewWording = await InvokeAsync(
            tools,
            MemoryRecallTool.ToolName,
            new AIFunctionArguments { ["query"] = "Gearbox oil intervals." });

        // Assert: the new wording finds it at full similarity
        var match = Assert.IsType<JsonElement>(byNewWording).GetProperty("matches")[0];
        Assert.Equal(memoryId, match.GetProperty("memoryId").GetString());
        Assert.Equal(1.0, match.GetProperty("similarity").GetDouble(), 6);
    }

    /// <summary>
    ///     Proves the family's refusals are returned values rather than exceptions, so a denied
    ///     request never ends the agent's turn.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Memory_Family_RefusedRequests_AreReturnedValues()
    {
        // Arrange: the family as an agent receives it
        var tools = Compose(new StubEmbeddingGenerator());

        // Act: ask for four things the family cannot do
        var noDescriptor = await InvokeAsync(
            tools,
            MemoryFileTool.ToolName,
            new AIFunctionArguments { ["details"] = "Some details." });
        var noQuery = await InvokeAsync(tools, MemoryRecallTool.ToolName, new AIFunctionArguments());
        var unknownUpdate = await InvokeAsync(
            tools,
            MemoryUpdateTool.ToolName,
            new AIFunctionArguments { ["memoryId"] = "mem-nope", ["details"] = "Some details." });
        var unknownForget = await InvokeAsync(
            tools,
            MemoryForgetTool.ToolName,
            new AIFunctionArguments { ["memoryId"] = "mem-nope" });

        // Assert: all four came back as refusal text the model can read and act on
        Assert.StartsWith("Denied (", Assert.IsType<string>(noDescriptor), StringComparison.Ordinal);
        Assert.StartsWith("Denied (", Assert.IsType<string>(noQuery), StringComparison.Ordinal);
        Assert.StartsWith("Denied (", Assert.IsType<string>(unknownUpdate), StringComparison.Ordinal);
        Assert.StartsWith("Denied (", Assert.IsType<string>(unknownForget), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves no refusal or non-storage result in the family prescribes a remedy, which is the
    ///     project-wide rule that a tool result states facts and never tells a model what to do next.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Memory_Family_DeniedResults_PrescribeNoRemedy()
    {
        // Arrange: the family holding one memory
        var tools = Compose(new StubEmbeddingGenerator());
        await InvokeAsync(
            tools,
            MemoryFileTool.ToolName,
            new AIFunctionArguments { ["descriptor"] = "Alpha subject.", ["details"] = "Alpha details." });

        // Act: collect every refusal and non-storage result the family can produce
        var results = new List<string>
        {
            Assert.IsType<JsonElement>(
                await InvokeAsync(
                    tools,
                    MemoryFileTool.ToolName,
                    new AIFunctionArguments { ["descriptor"] = "Alpha subject.", ["details"] = "Other." }))
                .GetRawText(),
            Assert.IsType<string>(
                await InvokeAsync(
                    tools,
                    MemoryForgetTool.ToolName,
                    new AIFunctionArguments { ["memoryId"] = "mem-nope" })),
            Assert.IsType<string>(
                await InvokeAsync(
                    tools,
                    MemoryUpdateTool.ToolName,
                    new AIFunctionArguments { ["memoryId"] = "mem-nope", ["details"] = "x" })),
        };

        // Assert: none of them names another tool to run
        Assert.All(results, text =>
        {
            foreach (var name in tools.Select(tool => tool.Name))
            {
                Assert.DoesNotContain(name, text, StringComparison.Ordinal);
            }
        });
    }
}
