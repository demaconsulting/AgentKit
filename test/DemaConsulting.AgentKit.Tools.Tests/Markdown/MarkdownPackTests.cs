using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Markdown;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Markdown;

/// <summary>
///     Unit tests for the <see cref="MarkdownPack"/> class.
/// </summary>
public class MarkdownPackTests
{
    /// <summary>
    ///     Proves the pack publishes its family prefix as a constant and through the contract.
    /// </summary>
    [Fact]
    public void MarkdownPack_FamilyPrefix_IsPublishedAsConstantAndContract()
    {
        Assert.Equal("markdown", MarkdownPack.FamilyPrefix);
        Assert.Equal(MarkdownPack.FamilyPrefix, ((IToolPack)new MarkdownPack()).FamilyPrefix);
    }

    /// <summary>
    ///     Proves the pack requires no host capability, so every host receives the family.
    /// </summary>
    [Fact]
    public void MarkdownPack_RequiredCapabilities_IsNone()
    {
        Assert.Equal(HostCapabilities.None, new MarkdownPack().RequiredCapabilities);
    }

    /// <summary>
    ///     Proves the pack creates its single outline tool.
    /// </summary>
    [Fact]
    public void MarkdownPack_CreateTools_RegistersTheOutlineTool()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new MarkdownPack().CreateTools(policy).ToList();

        Assert.Equal(["markdown_outline"], tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a policy that permits no writing anywhere still receives the family's single
    ///     tool, because reading a document's section structure is a read and nothing else.
    /// </summary>
    [Fact]
    public void MarkdownPack_CreateTools_ReadOnlyPolicy_PublishesItsTool()
    {
        // Arrange: every grant is read-only, so nothing anywhere may be written
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)]);

        // Act: create the family's tools under that policy
        var tools = new MarkdownPack().CreateTools(policy).ToList();

        // Assert: the family is unaffected — it has no write-performing tool to withhold
        Assert.Equal(["markdown_outline"], tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves the pack requires a policy to create its tools.
    /// </summary>
    [Fact]
    public void MarkdownPack_CreateTools_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MarkdownPack().CreateTools(null!).ToList());
    }
}
