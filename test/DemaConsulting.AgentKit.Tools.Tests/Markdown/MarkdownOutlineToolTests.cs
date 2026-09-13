using System.Text;
using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Markdown;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Markdown;

/// <summary>
///     Unit tests for the <see cref="MarkdownOutlineTool"/> class.
/// </summary>
/// <remarks>
///     The fixture has known headings on known lines, including a hash inside a fenced code block
///     that must not be read as a heading, so the section line ranges are deterministic and prove
///     the outline composes into the line-addressed text tools.
/// </remarks>
public class MarkdownOutlineToolTests
{
    /// <summary>
    ///     A Markdown fixture with headings on known lines. The fenced hash on line 8 is content.
    /// </summary>
    private const string Markdown =
        "# Title\n" +          // 1  level 1
        "\n" +                 // 2
        "## Section A\n" +     // 3  level 2
        "text\n" +             // 4
        "### Sub\n" +          // 5  level 3
        "\n" +                 // 6
        "```text\n" +          // 7  fence open
        "# not a heading\n" +  // 8  inside fence
        "```\n" +              // 9  fence close
        "## Section B\n" +     // 10 level 2
        "end";                 // 11

    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void MarkdownOutlineTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("markdown_outline", MarkdownOutlineTool.ToolName);
        Assert.StartsWith(MarkdownPack.FamilyPrefix + "_", MarkdownOutlineTool.ToolName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void MarkdownOutlineTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => MarkdownOutlineTool.Create(null!));
    }

    /// <summary>
    ///     Proves the outline reports each section's level, title, and 1-based line range, skipping
    ///     a fenced hash.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MarkdownOutlineTool_Outline_KnownFile_ReportsSectionsWithLineRanges()
    {
        // Arrange: a workspace holding the known fixture
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "guide.md", Markdown);
        var tool = MarkdownOutlineTool.Create(RootedPolicy(fixture.Root));

        // Act: outline the file by its bare relative name
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "guide.md" });

        // Assert: a structured outline reporting each section's range; the fenced hash is skipped
        var element = Assert.IsType<JsonElement>(result);
        var sections = element.GetProperty("sections");
        Assert.Multiple(
            () => Assert.Equal("guide.md", element.GetProperty("path").GetString()),
            () => Assert.Equal(4, element.GetProperty("sectionCount").GetInt32()),
            () => AssertSection(sections[0], 1, "Title", 1, 11),
            () => AssertSection(sections[1], 2, "Section A", 3, 9),
            () => AssertSection(sections[2], 3, "Sub", 5, 9),
            () => AssertSection(sections[3], 2, "Section B", 10, 11));
    }

    /// <summary>
    ///     Proves maxDepth filters which headings are reported while section ends stay correct.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MarkdownOutlineTool_Outline_MaxDepth_FiltersDeeperHeadings()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "guide.md", Markdown);
        var tool = MarkdownOutlineTool.Create(RootedPolicy(fixture.Root));

        // Act: outline to depth 2, excluding the level-3 subsection
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["path"] = "guide.md", ["maxDepth"] = 2 });

        var element = Assert.IsType<JsonElement>(result);
        var sections = element.GetProperty("sections");
        Assert.Multiple(
            () => Assert.Equal(3, element.GetProperty("sectionCount").GetInt32()),
            () => AssertSection(sections[0], 1, "Title", 1, 11),
            // Section A still ends at line 9 even though its level-3 child is not reported
            () => AssertSection(sections[1], 2, "Section A", 3, 9),
            () => AssertSection(sections[2], 2, "Section B", 10, 11));
    }

    /// <summary>
    ///     Proves a file with no headings is an empty outline, not a refusal.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MarkdownOutlineTool_Outline_FileWithoutHeadings_ReturnsEmptyOutline()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "plain.md", "just text\nno headings\n");
        var tool = MarkdownOutlineTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "plain.md" });

        var element = Assert.IsType<JsonElement>(result);
        Assert.Equal(0, element.GetProperty("sectionCount").GetInt32());
        Assert.Equal(0, element.GetProperty("sections").GetArrayLength());
    }

    /// <summary>
    ///     Proves a document larger than the read ceiling is outlined by streaming its headings
    ///     rather than refused, so a large document stays navigable. This is a regression test for
    ///     the windowing defect: the outline is bounded by the result ceiling, not the source size.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MarkdownOutlineTool_Outline_LargeDocument_ReportsHeadingsNotADenial()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "guide.md", LargeDocument());
        var tool = MarkdownOutlineTool.Create(RootedPolicy(fixture.Root, new ToolLimits(maxReadBytes: 64)));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "guide.md" });

        var element = Assert.IsType<JsonElement>(result);
        var sections = element.GetProperty("sections");
        Assert.Multiple(
            () => Assert.Equal(2, element.GetProperty("sectionCount").GetInt32()),
            () => AssertSection(sections[0], 1, "Top", 1, 200),
            () => AssertSection(sections[1], 2, "Late Section", 150, 200));
    }

    /// <summary>
    ///     Proves a path outside the grants is refused with a returned denial.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MarkdownOutlineTool_Outline_PathOutsideGrants_ReturnsDenial()
    {
        using var fixture = new ReparsePointFixture();
        var outsideFile = ReparsePointFixture.WriteFile(fixture.Outside, "secret.md", "# Secret");
        var tool = MarkdownOutlineTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = outsideFile });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing file is refused as target-not-found rather than throwing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task MarkdownOutlineTool_Outline_MissingFile_ReturnsDenial()
    {
        using var fixture = new ReparsePointFixture();
        var tool = MarkdownOutlineTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "absent.md" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Asserts a section element carries the expected level, title, and line range.
    /// </summary>
    /// <param name="section">The section JSON element.</param>
    /// <param name="level">The expected heading level.</param>
    /// <param name="title">The expected heading title.</param>
    /// <param name="startLine">The expected 1-based start line.</param>
    /// <param name="endLine">The expected 1-based end line.</param>
    private static void AssertSection(
        JsonElement section,
        int level,
        string title,
        int startLine,
        int endLine)
    {
        Assert.Multiple(
            () => Assert.Equal(level, section.GetProperty("level").GetInt32()),
            () => Assert.Equal(title, section.GetProperty("title").GetString()),
            () => Assert.Equal(startLine, section.GetProperty("startLine").GetInt32()),
            () => Assert.Equal(endLine, section.GetProperty("endLine").GetInt32()));
    }

    /// <summary>
    ///     Builds a Markdown document whose bytes exceed a small read ceiling, with a heading late in
    ///     the file, so the outline must stream the document rather than gate on its size.
    /// </summary>
    /// <returns>A 200-line document with a level-1 heading first and a level-2 heading on line 150.</returns>
    private static string LargeDocument()
    {
        var builder = new StringBuilder();
        builder.Append("# Top\n");
        for (var line = 2; line <= 149; line++)
        {
            builder.Append("filler text\n");
        }

        builder.Append("## Late Section\n");
        for (var line = 151; line <= 200; line++)
        {
            builder.Append("more text\n");
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Composes a policy over one read location, optionally with tighter limits.
    /// </summary>
    /// <param name="root">The permitted location.</param>
    /// <param name="limits">The tool limits, or null for the defaults.</param>
    /// <returns>The policy.</returns>
    private static PathPolicy RootedPolicy(string root, ToolLimits? limits = null)
    {
        return limits is null
            ? new PathPolicy(root, [PathRule.ReadOnly(root)])
            : new PathPolicy(root, [PathRule.ReadOnly(root)], limits);
    }

    /// <summary>
    ///     Invokes a tool with the supplied arguments, exactly as a runtime would.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="arguments">The arguments to supply.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeAsync(AIFunction tool, AIFunctionArguments arguments)
    {
        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }
}
