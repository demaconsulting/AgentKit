using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.File;

/// <summary>
///     Unit tests for the <see cref="FilePack"/> class.
/// </summary>
public class FilePackTests
{
    /// <summary>
    ///     Proves the pack publishes its family prefix as a constant and through the contract.
    /// </summary>
    [Fact]
    public void FilePack_FamilyPrefix_IsPublishedAsConstantAndContract()
    {
        Assert.Equal("file", FilePack.FamilyPrefix);
        Assert.Equal(FilePack.FamilyPrefix, ((IToolPack)new FilePack()).FamilyPrefix);
    }

    /// <summary>
    ///     Proves the pack requires no host capability, so every host receives the family.
    /// </summary>
    [Fact]
    public void FilePack_RequiredCapabilities_IsNone()
    {
        Assert.Equal(HostCapabilities.None, new FilePack().RequiredCapabilities);
    }

    /// <summary>
    ///     Proves the pack creates its four tools in the documented order.
    /// </summary>
    [Fact]
    public void FilePack_CreateTools_RegistersListCopyMoveDeleteInOrder()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new FilePack().CreateTools(policy).ToList();

        Assert.Equal(
            ["file_list", "file_copy", "file_move", "file_delete"],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves the pack requires a policy to create its tools.
    /// </summary>
    [Fact]
    public void FilePack_CreateTools_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FilePack().CreateTools(null!).ToList());
    }

    /// <summary>
    ///     Proves every tool the pack publishes carries a valid, family-prefixed name and a
    ///     description.
    /// </summary>
    [Fact]
    public void FilePack_CreateTools_EveryTool_CarriesAValidatedNameAndDescription()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy).Add(new FilePack()).Build();

        Assert.All(tools, tool =>
        {
            ToolName.Validate(tool.Name);
            Assert.StartsWith(FilePack.FamilyPrefix + "_", tool.Name, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        });
    }
}
