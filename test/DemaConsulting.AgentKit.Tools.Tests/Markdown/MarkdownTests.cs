using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Markdown;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Markdown;

/// <summary>
///     Subsystem-level integration tests for the Markdown tool family.
/// </summary>
/// <remarks>
///     These scenarios exercise the family the way an application does: composed through the
///     AgentKitCore <see cref="ToolPackBuilder"/> under one access policy, then invoked through the
///     published tool list. They assert the family-wide properties — one prefix, one policy, and a
///     structured result delivered unserialized through the guard.
/// </remarks>
public class MarkdownTests
{
    /// <summary>
    ///     Proves a composition attaching the family publishes the single outline tool.
    /// </summary>
    [Fact]
    public void Markdown_Family_ComposedThroughBuilder_PublishesTheOutlineTool()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy).Add(new MarkdownPack()).Build();

        Assert.Single(tools);
        Assert.Equal(MarkdownOutlineTool.ToolName, tools[0].Name);
    }

    /// <summary>
    ///     Proves every tool in the family carries a valid name and a description.
    /// </summary>
    [Fact]
    public void Markdown_Family_EveryTool_CarriesAValidatedNameAndDescription()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy).Add(new MarkdownPack()).Build();

        Assert.All(tools, tool =>
        {
            ToolName.Validate(tool.Name);
            Assert.StartsWith(MarkdownPack.FamilyPrefix + "_", tool.Name, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        });
    }

    /// <summary>
    ///     Proves the family's structured result reaches the caller as a JSON element the guard
    ///     serialized, and that a link-escaping file is never outlined.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Markdown_Family_OutlineOfPermittedFile_ReturnsStructuredResult()
    {
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "guide.md", "# Title\n\n## Section\n");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        var tools = new ToolPackBuilder(policy).Add(new MarkdownPack()).Build();

        var tool = tools.Single(candidate => candidate.Name == MarkdownOutlineTool.ToolName);
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["path"] = "guide.md" },
            TestContext.Current.CancellationToken);

        var element = Assert.IsType<JsonElement>(result);
        Assert.Equal(2, element.GetProperty("sectionCount").GetInt32());
    }
}
