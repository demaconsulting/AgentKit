using DemaConsulting.AgentKit.Core;
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
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());

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
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());
        var builder = new ToolPackBuilder(policy).Add(new TextFilePack());

        // Act: build the tool list
        var tools = builder.Build();

        // Assert: the family's three tools are published, each under the family prefix
        Assert.Equal(
            ["text_file_read", "text_file_write", "text_file_list"],
            tools.Select(tool => tool.Name));
    }
}
