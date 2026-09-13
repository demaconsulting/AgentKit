using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Memory;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Memory;

/// <summary>
///     Unit tests for the <see cref="MemoryPack"/> class.
/// </summary>
public class MemoryPackTests
{
    /// <summary>
    ///     Builds a policy for a composition that never consults one.
    /// </summary>
    /// <returns>A permissive policy.</returns>
    private static PathPolicy AnyPolicy()
    {
        return new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
    }

    /// <summary>
    ///     Proves the pack publishes its family prefix as a constant and through the contract.
    /// </summary>
    [Fact]
    public void MemoryPack_FamilyPrefix_IsPublishedAsConstantAndContract()
    {
        Assert.Equal("memory", MemoryPack.FamilyPrefix);
        Assert.Equal(
            MemoryPack.FamilyPrefix,
            ((IToolPack)new MemoryPack(new StubEmbeddingGenerator())).FamilyPrefix);
    }

    /// <summary>
    ///     Proves the pack requires no host capability, because the capability it really needs — an
    ///     embedding backend — is a constructor argument rather than a host declaration.
    /// </summary>
    [Fact]
    public void MemoryPack_RequiredCapabilities_IsNone()
    {
        Assert.Equal(
            HostCapabilities.None,
            new MemoryPack(new StubEmbeddingGenerator()).RequiredCapabilities);
    }

    /// <summary>
    ///     Proves an application cannot construct the family without an embedding backend, which is
    ///     what makes a capability flag unnecessary.
    /// </summary>
    [Fact]
    public void MemoryPack_Constructor_NullGenerator_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MemoryPack(null!));
    }

    /// <summary>
    ///     Proves an application that configures nothing runs under the published defaults, and one
    ///     that configures something runs under that.
    /// </summary>
    [Fact]
    public void MemoryPack_Options_DefaultOrSupplied_AreVisibleToTheApplication()
    {
        // Act: one pack configured and one not
        var defaulted = new MemoryPack(new StubEmbeddingGenerator());
        var configured = new MemoryPack(
            new StubEmbeddingGenerator(),
            new MemoryOptions(nearDuplicateThreshold: 0.95, recallCount: 2));

        // Assert: the author can read back the controls the family is running under
        Assert.Equal(MemoryOptions.DefaultNearDuplicateThreshold, defaulted.Options.NearDuplicateThreshold);
        Assert.Equal(0.95, configured.Options.NearDuplicateThreshold);
        Assert.Equal(2, configured.Options.RecallCount);
    }

    /// <summary>
    ///     Proves the pack creates its five tools in the order a model sees them.
    /// </summary>
    [Fact]
    public void MemoryPack_CreateTools_RegistersTheFamilyInOrder()
    {
        // Act: create the family's tools
        var tools = new MemoryPack(new StubEmbeddingGenerator()).CreateTools(AnyPolicy()).ToList();

        // Assert: file, recall, update, revise, forget, in that order
        Assert.Equal(
            ["memory_file", "memory_recall", "memory_update", "memory_revise", "memory_forget"],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves the pack requires a policy to create its tools, even though its tools never
    ///     consult one.
    /// </summary>
    [Fact]
    public void MemoryPack_CreateTools_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new MemoryPack(new StubEmbeddingGenerator()).CreateTools(null!).ToList());
    }

    /// <summary>
    ///     Proves that with no store supplied, each composition gets memories of its own — the
    ///     property that keeps a delegated agent from reading or overwriting its parent's.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryPack_CreateTools_NoStoreSupplied_ProducesIndependentMemories()
    {
        // Arrange: one pack instance, composed twice
        var pack = new MemoryPack(new StubEmbeddingGenerator());
        var first = pack.CreateTools(AnyPolicy()).ToList();
        var second = pack.CreateTools(AnyPolicy()).ToList();

        // Act: file into the first composition only, then recall from the second
        await first.Single(tool => tool.Name == MemoryFileTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["descriptor"] = "Alpha subject.", ["details"] = "Alpha details." },
            TestContext.Current.CancellationToken);

        var recalled = await second.Single(tool => tool.Name == MemoryRecallTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["query"] = "Alpha subject." },
            TestContext.Current.CancellationToken);

        // Assert: the second composition has memories of its own, and they are empty
        Assert.Equal(0, Assert.IsType<JsonElement>(recalled).GetProperty("matchCount").GetInt32());
    }

    /// <summary>
    ///     Proves that a supplied store is shared by every composition, which is how an author
    ///     expresses memories that outlive one agent.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MemoryPack_CreateTools_SuppliedStore_IsSharedAcrossCompositions()
    {
        // Arrange: one pack over a store the author owns, composed twice
        var store = new InMemoryMemoryStore();
        var pack = new MemoryPack(new StubEmbeddingGenerator(), options: null, store: store);
        var first = pack.CreateTools(AnyPolicy()).ToList();
        var second = pack.CreateTools(AnyPolicy()).ToList();

        // Act: file into the first composition, recall from the second
        await first.Single(tool => tool.Name == MemoryFileTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["descriptor"] = "Alpha subject.", ["details"] = "Alpha details." },
            TestContext.Current.CancellationToken);

        var recalled = await second.Single(tool => tool.Name == MemoryRecallTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["query"] = "Alpha subject." },
            TestContext.Current.CancellationToken);

        // Assert: the memory the author's store holds is visible to both compositions
        var element = Assert.IsType<JsonElement>(recalled);
        Assert.Equal(1, element.GetProperty("matchCount").GetInt32());
        Assert.Equal("Alpha details.", element.GetProperty("matches")[0].GetProperty("details").GetString());
    }

    /// <summary>
    ///     Proves the pack publishes the instruction an application should give an agent that
    ///     carries the family, so the wording that ships cannot drift from the wording that works.
    /// </summary>
    [Fact]
    public void MemoryPack_SuggestedInstruction_NamesTheFilingAndRecallingTools()
    {
        // Assert: the instruction names both imperatives the family depends on
        Assert.Contains(MemoryFileTool.ToolName, MemoryPack.SuggestedInstruction, StringComparison.Ordinal);
        Assert.Contains(MemoryRecallTool.ToolName, MemoryPack.SuggestedInstruction, StringComparison.Ordinal);
        Assert.Contains("per document", MemoryPack.SuggestedInstruction, StringComparison.Ordinal);
    }
}
