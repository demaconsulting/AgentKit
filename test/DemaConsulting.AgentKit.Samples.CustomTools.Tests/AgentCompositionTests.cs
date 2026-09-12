using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.CustomTools.Tests;

/// <summary>
///     Unit tests for the custom-tools sample: that the two author-written packs compose alongside a
///     shipped pack, that the path-taking Markdown tool observes containment and returns a structured
///     result, that the no-path clock tool returns a structured result, and that the instructions
///     name the run's workspace and tools.
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
    ///     A temporary workspace holding a known Markdown fixture, so section line numbers are
    ///     deterministic.
    /// </summary>
    private readonly string _workspace;

    /// <summary>
    ///     Initializes the fixture, creating a temporary workspace with a known Markdown file.
    /// </summary>
    public AgentCompositionTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "custom-tools-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);

        // A fixture with known headings on known lines: the fenced hash on line 8 must NOT be read
        // as a heading, which is what proves the parser honors fenced code blocks.
        var markdown =
            "# Title\n" +          // line 1, level 1
            "\n" +                 // line 2
            "## Section A\n" +     // line 3, level 2
            "text\n" +             // line 4
            "### Sub\n" +          // line 5, level 3
            "\n" +                 // line 6
            "```text\n" +          // line 7 (fence open)
            "# not a heading\n" +  // line 8 (inside fence)
            "```\n" +              // line 9 (fence close)
            "## Section B\n";      // line 10, level 2
        File.WriteAllText(Path.Combine(_workspace, "doc.md"), markdown);
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
            () => Assert.Contains(MarkdownSectionsTool.ToolName, names),
            () => Assert.Contains(ClockNowTool.ToolName, names),
            () => Assert.Contains("text_file_read", names),
            () => Assert.Contains("text_file_write", names),
            () => Assert.Contains("text_file_list", names),
            () => Assert.All(names, ToolName.Validate));
    }

    /// <summary>
    ///     Proves the Markdown tool returns a structured listing of a file's headings with correct
    ///     levels and 1-based line numbers, ignoring a hash inside a fenced code block.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MarkdownSections_KnownFile_ReturnsStructuredHeadingsWithLineNumbers()
    {
        // Arrange: the composed Markdown tool
        var tool = GetTool(MarkdownSectionsTool.ToolName);

        // Act: list the sections of the known fixture, addressed by its bare relative name
        var result = await InvokeAsync(tool, "doc.md");

        // Assert: a structured result (a JsonElement, since the guard serializes structured data)
        var element = Assert.IsType<JsonElement>(result);
        var sections = element.GetProperty("sections");

        Assert.Multiple(
            // The file lies inside the granted workspace, so it is reported by its bare relative name
            () => Assert.Equal("doc.md", element.GetProperty("path").GetString()),
            () => Assert.Equal(4, element.GetProperty("sectionCount").GetInt32()),
            () => Assert.Equal(4, sections.GetArrayLength()),
            () => AssertSection(sections[0], 1, "Title", 1),
            () => AssertSection(sections[1], 2, "Section A", 3),
            () => AssertSection(sections[2], 3, "Sub", 5),
            // The fenced hash on line 8 is skipped; the next real heading is on line 10
            () => AssertSection(sections[3], 2, "Section B", 10));
    }

    /// <summary>
    ///     Proves a path outside the granted workspace is refused with a returned denial naming the
    ///     containment reason, rather than being read or throwing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MarkdownSections_PathOutsideWorkspace_ReturnsPathNotPermittedDenial()
    {
        // Arrange: the composed Markdown tool, and a path escaping the workspace
        var tool = GetTool(MarkdownSectionsTool.ToolName);

        // Act: request a file above the workspace
        var result = await InvokeAsync(tool, "../escape.md");

        // Assert: a returned refusal, not a thrown exception, naming the containment reason
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a no-argument request is answered as discovery, reporting the searchable locations
    ///     and that relative addressing applies because the granted workspace is the anchor.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MarkdownSections_NoArgument_ReturnsDiscoveryStructure()
    {
        // Arrange: the composed Markdown tool
        var tool = GetTool(MarkdownSectionsTool.ToolName);

        // Act: invoke with no arguments at all
        var result = await tool.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: a structured discovery result naming the searchable workspace and the dialect
        var element = Assert.IsType<JsonElement>(result);
        Assert.Multiple(
            () => Assert.True(element.GetProperty("discovery").GetBoolean()),
            () => Assert.True(element.GetProperty("relativeAddressing").GetBoolean()),
            () => Assert.True(element.GetProperty("searchableLocations").GetArrayLength() >= 1));
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
            () => Assert.Contains("markdown_sections", instructions, StringComparison.Ordinal),
            () => Assert.Contains("clock_now", instructions, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Asserts a section element carries the expected level, title, and line number.
    /// </summary>
    /// <param name="section">The section JSON element.</param>
    /// <param name="level">The expected heading level.</param>
    /// <param name="title">The expected heading title.</param>
    /// <param name="line">The expected 1-based line number.</param>
    private static void AssertSection(JsonElement section, int level, string title, int line)
    {
        Assert.Multiple(
            () => Assert.Equal(level, section.GetProperty("level").GetInt32()),
            () => Assert.Equal(title, section.GetProperty("title").GetString()),
            () => Assert.Equal(line, section.GetProperty("line").GetInt32()));
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
