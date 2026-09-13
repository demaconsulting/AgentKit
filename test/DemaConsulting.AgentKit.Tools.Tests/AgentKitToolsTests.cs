using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Image;
using DemaConsulting.AgentKit.Tools.Markdown;
using DemaConsulting.AgentKit.Tools.TextFile;

namespace DemaConsulting.AgentKit.Tools.Tests;

/// <summary>
///     System-level integration tests for the AgentKitTools system.
/// </summary>
public class AgentKitToolsTests
{
    /// <summary>
    ///     Proves that the package composes its tool families through the AgentKitCore contract,
    ///     and that an empty composition — before any family is attached — yields no tools.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_EmptyBuilder_ContributesNoTools()
    {
        // Arrange: a policy governing an otherwise empty composition
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: build the tool list before any family has been attached
        var tools = new ToolPackBuilder(policy).Build();

        // Assert: an empty composition contributes no tools
        Assert.Empty(tools);
    }

    /// <summary>
    ///     Proves that attaching the TextFile pack contributes the text file family to a
    ///     composition, under the one family prefix the pack claims.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_TextFilePack_ContributesTheTextFileFamily()
    {
        // Arrange: a policy governing a composition with the text file family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy).Add(new TextFilePack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the family's seven tools are published, each under the family prefix
        Assert.Equal(
            [
                "text_file_search",
                "text_file_read",
                "text_file_create",
                "text_file_replace",
                "text_file_cut_lines",
                "text_file_copy_lines",
                "text_file_paste_lines"
            ],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that attaching the File pack contributes the type-agnostic file family to a
    ///     composition, under the one family prefix the pack claims.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_FilePack_ContributesTheFileFamily()
    {
        // Arrange: a policy governing a composition with the file family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy).Add(new FilePack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the family's four tools are published, each under the family prefix
        Assert.Equal(
            ["file_list", "file_copy", "file_move", "file_delete"],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that attaching the Markdown pack contributes the markdown family to a composition,
    ///     under the one family prefix the pack claims.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_MarkdownPack_ContributesTheMarkdownFamily()
    {
        // Arrange: a policy governing a composition with the markdown family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy).Add(new MarkdownPack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the family's single tool is published, under the family prefix
        Assert.Equal(
            ["markdown_outline"],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that attaching the Image pack to a vision host contributes the image family to
    ///     a composition, under the one family prefix the pack claims.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_ImagePack_ContributesTheImageFamily()
    {
        // Arrange: a vision host governing a composition with the image family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(new ImagePack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the family's read tool is published, under the family prefix
        Assert.Equal(
            ["image_read"],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that a host that does not declare vision receives none of the image family's
    ///     tools, so a model that cannot see is never offered content it could only fabricate
    ///     around.
    /// </summary>
    [Fact]
    public void AgentKitTools_SystemComposition_ImagePackWithoutVision_ContributesNoTools()
    {
        // Arrange: a host that declares no capability, governing a composition with the image
        // family attached
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var builder = new ToolPackBuilder(policy).Add(new ImagePack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the vision requirement is unmet, so the family contributes nothing
        Assert.Empty(tools);
    }
}
