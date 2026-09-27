using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.File;

/// <summary>
///     Unit tests for the <see cref="FilePack"/> class.
/// </summary>
/// <remarks>
///     Because the published set depends on whether the policy permits writing anywhere, every
///     scenario that states a count also states the policy shape it holds under.
/// </remarks>
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
    ///     Proves the pack creates every tool it publishes in the documented order when the policy
    ///     permits writing, which is the condition under which the whole family is published.
    /// </summary>
    [Fact]
    public void FilePack_CreateTools_WriteGrantingPolicy_RegistersEveryToolInOrder()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new FilePack().CreateTools(policy).ToList();

        Assert.Equal(7, tools.Count);
        Assert.Equal(
            [
                "file_list",
                "file_copy",
                "file_move",
                "file_delete",
                "file_create_directory",
                "file_move_directory",
                "file_delete_directory"
            ],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a policy that permits no writing anywhere receives only the listing tool, so no
    ///     tool is offered whose only possible outcome would be a refusal.
    /// </summary>
    [Fact]
    public void FilePack_CreateTools_ReadOnlyPolicy_PublishesOnlyTheListTool()
    {
        // Arrange: every grant is read-only, so nothing anywhere may be written
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)]);

        // Act: ask the pack what it publishes under that policy
        var tools = new FilePack().CreateTools(policy).ToList();

        // Assert: listing survives; the six file-system-changing tools are withheld
        Assert.Single(tools);
        Assert.Equal(["file_list"], tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a read-only workspace paired with a writable session location publishes every
    ///     tool, because the write question is asked of the policy as a whole rather than of the
    ///     location relative names anchor to.
    /// </summary>
    [Fact]
    public void FilePack_CreateTools_ReadOnlyWorkspaceWithWritableSession_PublishesEveryTool()
    {
        // Arrange: the anchor is granted read-only; a separate location is granted read-write
        using var fixture = new TempDirectoryFixture();
        var session = Path.Combine(fixture.Outside, "session");
        Directory.CreateDirectory(session);
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadOnly(fixture.Root), PathRule.ReadWrite(session)]);

        // Act: ask the pack what it publishes under the mixed policy
        var tools = new FilePack().CreateTools(policy).ToList();

        // Assert: all seven, in the documented order — writing is possible, just not at the anchor
        Assert.Equal(7, tools.Count);
        Assert.Equal(
            [
                "file_list",
                "file_copy",
                "file_move",
                "file_delete",
                "file_create_directory",
                "file_move_directory",
                "file_delete_directory"
            ],
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
