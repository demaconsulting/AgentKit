using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.CustomTools.Tests;

/// <summary>
///     Unit tests for the custom-tools sample: that the two author-written packs compose alongside a
///     shipped pack, that the path-taking document-statistics tool observes containment and returns a
///     structured result, that the no-path clock tool returns a structured result, and that the
///     instructions name the run's workspace and tools.
/// </summary>
/// <remarks>
///     These tests exercise the sample's real composition and tool bodies directly — no live
///     provider is involved. Invoking each <see cref="AIFunction"/> the way a runtime would proves
///     the author-written tools behave exactly as a shipped tool does: a structured success, and a
///     returned refusal rather than a thrown exception for a request the policy denies.
/// </remarks>
public sealed class AgentCompositionTests : IDisposable
{
    /// <summary>
    ///     A temporary workspace holding a known text fixture, so the counts are deterministic.
    /// </summary>
    private readonly string _workspace;

    /// <summary>
    ///     Initializes the fixture, creating a temporary workspace with a known text file.
    /// </summary>
    public AgentCompositionTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "custom-tools-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);

        // A fixture with known counts: 5 words across 2 lines, 24 characters including newlines.
        File.WriteAllText(Path.Combine(_workspace, "doc.txt"), "one two three\nfour five\n");
    }

    /// <summary>
    ///     Releases the temporary workspace.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    /// <summary>
    ///     Proves both custom packs compose alongside the shipped pack, and every published name is
    ///     a valid, family-prefixed tool name — which is what <see cref="ToolPackBuilder.Build"/>
    ///     verified when it accepted the packs.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildTools_ComposesCustomPacksWithShippedPack()
    {
        // Arrange / Act: compose the whole tool set the sample builds
        var tools = AgentComposition.BuildTools(_workspace);
        var names = tools.Select(tool => tool.Name).ToList();

        // Assert: both custom tools and the shipped text-file tools are present, and every name is a
        // valid family-prefixed tool name
        Assert.Multiple(
            () => Assert.Contains(DocStatsWordCountTool.ToolName, names),
            () => Assert.Contains(ClockNowTool.ToolName, names),
            () => Assert.Contains("text_file_search", names),
            () => Assert.Contains("text_file_read", names),
            () => Assert.Contains("text_file_replace", names),
            () => Assert.All(names, ToolName.Validate));
    }

    /// <summary>
    ///     Proves the document-statistics tool returns a structured count of a file's words, lines
    ///     and characters, addressed by its bare relative name.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DocStatsWordCount_KnownFile_ReturnsStructuredCounts()
    {
        // Arrange: the composed document-statistics tool
        var tool = GetTool(DocStatsWordCountTool.ToolName);

        // Act: count the known fixture, addressed by its bare relative name
        var result = await InvokeAsync(tool, "doc.txt");

        // Assert: a structured result carrying the deterministic counts
        var element = Assert.IsType<JsonElement>(result);
        Assert.Multiple(
            // The file lies inside the granted workspace, so it is reported by its bare relative name
            () => Assert.Equal("doc.txt", element.GetProperty("path").GetString()),
            () => Assert.Equal(24, element.GetProperty("characters").GetInt32()),
            () => Assert.Equal(5, element.GetProperty("words").GetInt32()),
            () => Assert.Equal(2, element.GetProperty("lines").GetInt32()));
    }

    /// <summary>
    ///     Proves a path outside the granted workspace is refused with a returned denial naming the
    ///     containment reason, rather than being read or throwing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DocStatsWordCount_PathOutsideWorkspace_ReturnsPathNotPermittedDenial()
    {
        // Arrange: the composed document-statistics tool, and a path escaping the workspace
        var tool = GetTool(DocStatsWordCountTool.ToolName);

        // Act: request a file above the workspace
        var result = await InvokeAsync(tool, "../escape.txt");

        // Assert: a returned refusal, not a thrown exception, naming the containment reason
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a no-argument request is answered as discovery, reporting the inspectable locations
    ///     and that relative addressing applies because the granted workspace is the anchor.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DocStatsWordCount_NoArgument_ReturnsDiscoveryStructure()
    {
        // Arrange: the composed document-statistics tool
        var tool = GetTool(DocStatsWordCountTool.ToolName);

        // Act: invoke with no arguments at all
        var result = await tool.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: a structured discovery result naming the inspectable workspace and the dialect
        var element = Assert.IsType<JsonElement>(result);
        Assert.Multiple(
            () => Assert.True(element.GetProperty("discovery").GetBoolean()),
            () => Assert.True(element.GetProperty("relativeAddressing").GetBoolean()),
            () => Assert.True(element.GetProperty("inspectableLocations").GetArrayLength() >= 1));
    }

    /// <summary>
    ///     Proves the no-path clock tool returns a structured result naming both the local and UTC
    ///     times, without consulting any policy.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ClockNow_ReturnsStructuredLocalAndUtcTime()
    {
        // Arrange: the composed clock tool
        var tool = GetTool(ClockNowTool.ToolName);

        // Act: invoke with no arguments
        var result = await tool.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: a structured result carrying both instants and the local time zone
        var element = Assert.IsType<JsonElement>(result);
        Assert.Multiple(
            () => Assert.False(string.IsNullOrEmpty(element.GetProperty("localTime").GetString())),
            () => Assert.False(string.IsNullOrEmpty(element.GetProperty("utcTime").GetString())),
            () => Assert.False(string.IsNullOrEmpty(element.GetProperty("timeZone").GetString())));
    }

    /// <summary>
    ///     Proves the instructions name the run's workspace and the custom tools it carries.
    /// </summary>
    [Fact]
    public void AgentComposition_BuildInstructions_NamesWorkspaceAndCustomTools()
    {
        // Arrange / Act: build the instructions for a run over the fixture workspace
        var instructions = AgentComposition.BuildInstructions(_workspace);

        // Assert: the workspace and both custom tools are named
        Assert.Multiple(
            () => Assert.Contains(_workspace, instructions, StringComparison.Ordinal),
            () => Assert.Contains("docstats_wordcount", instructions, StringComparison.Ordinal),
            () => Assert.Contains("clock_now", instructions, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Gets a composed tool by name from the sample's full tool set.
    /// </summary>
    /// <param name="name">The tool name to find.</param>
    /// <returns>The composed tool.</returns>
    private AIFunction GetTool(string name)
    {
        return AgentComposition.BuildTools(_workspace).Single(tool => tool.Name == name);
    }

    /// <summary>
    ///     Invokes a tool with a single path argument, exactly as a runtime would.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="path">The path argument to supply.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeAsync(AIFunction tool, string path)
    {
        return await tool.InvokeAsync(
            new AIFunctionArguments { ["path"] = path },
            TestContext.Current.CancellationToken);
    }
}
