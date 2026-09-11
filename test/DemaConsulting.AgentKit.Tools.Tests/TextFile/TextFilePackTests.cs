using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFilePack"/> class.
/// </summary>
/// <remarks>
///     The pack is the only public way to obtain the family's tools, so these scenarios verify
///     what a composing application can observe: the prefix claimed, the capability required,
///     the tools produced, and that the policy the composition supplied is the one governing
///     them.
/// </remarks>
public class TextFilePackTests
{
    /// <summary>
    ///     Proves the published family prefix constant is the family name.
    /// </summary>
    [Fact]
    public void TextFilePack_FamilyPrefix_Constant_IsTextFile()
    {
        // Arrange / Act: read the published constant
        var prefix = TextFilePack.FamilyPrefix;

        // Assert: the family the tool names are qualified by
        Assert.Equal("text_file", prefix);
    }

    /// <summary>
    ///     Proves the contract reports the same prefix the class publishes as a constant.
    /// </summary>
    [Fact]
    public void TextFilePack_FamilyPrefix_Contract_ReportsTheDeclaredConstant()
    {
        // Arrange: the pack seen through the contract a composer uses
        IToolPack pack = new TextFilePack();

        // Act: read the prefix through the contract
        var prefix = pack.FamilyPrefix;

        // Assert: the constant and the contract cannot drift apart
        Assert.Equal(TextFilePack.FamilyPrefix, prefix);
    }

    /// <summary>
    ///     Proves the family asks nothing of its host.
    /// </summary>
    [Fact]
    public void TextFilePack_RequiredCapabilities_Pack_RequiresNoHostCapability()
    {
        // Arrange: the pack
        var pack = new TextFilePack();

        // Act: read what it requires of a host
        var required = pack.RequiredCapabilities;

        // Assert: requiring nothing means every host receives the family
        Assert.Equal(HostCapabilities.None, required);
    }

    /// <summary>
    ///     Proves the pack creates the read, write and list tools in a fixed order.
    /// </summary>
    [Fact]
    public void TextFilePack_CreateTools_Policy_CreatesTheReadWriteAndListTools()
    {
        // Arrange: a pack and a policy to govern its tools
        var pack = new TextFilePack();
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());

        // Act: create the family's tools
        var tools = pack.CreateTools(policy).ToList();

        // Assert: exactly three tools, in the order the model will see them
        Assert.Equal(3, tools.Count);
        Assert.Equal(TextFileReadTool.ToolName, tools[0].Name);
        Assert.Equal(TextFileWriteTool.ToolName, tools[1].Name);
        Assert.Equal(TextFileListTool.ToolName, tools[2].Name);
    }

    /// <summary>
    ///     Proves the pack honors the contract obligation to return no null tool.
    /// </summary>
    [Fact]
    public void TextFilePack_CreateTools_Policy_ReturnsNoNullTool()
    {
        // Arrange: a pack and a policy to govern its tools
        var pack = new TextFilePack();
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());

        // Act: create the family's tools
        var tools = pack.CreateTools(policy).ToList();

        // Assert: the collection the composer receives contains no hole
        Assert.NotEmpty(tools);
        Assert.All(tools, Assert.NotNull);
    }

    /// <summary>
    ///     Proves every tool the pack creates carries the family prefix it declares.
    /// </summary>
    [Fact]
    public void TextFilePack_CreateTools_EveryTool_CarriesTheFamilyPrefix()
    {
        // Arrange: a pack and a policy to govern its tools
        var pack = new TextFilePack();
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());

        // Act: create the family's tools
        var tools = pack.CreateTools(policy).ToList();

        // Assert: the declaration the prefix collision check depends on is true
        Assert.All(
            tools,
            tool => Assert.StartsWith(
                TextFilePack.FamilyPrefix + "_",
                tool.Name,
                StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void TextFilePack_CreateTools_NullPolicy_ThrowsArgumentNullException()
    {
        // Arrange: a pack with no policy to hand its tools
        var pack = new TextFilePack();

        // Act / Assert: a family with no policy cannot be created
        Assert.Throws<ArgumentNullException>(() => pack.CreateTools(null!).ToList());
    }

    /// <summary>
    ///     Proves the policy the composer supplies is the one governing the created tools.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFilePack_CreateTools_SuppliedPolicy_GovernsTheCreatedTools()
    {
        // Arrange: a pack whose tools are created from a policy rooted at one location
        using var fixture = new ReparsePointFixture();
        var permitted = ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "permitted");
        var refused = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "secret");
        var policy = new PathPolicy(PathRule.Rooted(fixture.Root), PathRule.Rooted(fixture.Root));
        var readTool = new TextFilePack().CreateTools(policy).First();

        // Act: read one path the policy permits and one it does not
        var permittedResult = await InvokeReadAsync(readTool, permitted);
        var refusedResult = await InvokeReadAsync(readTool, refused);

        // Assert: the supplied policy governs both decisions
        Assert.Equal("permitted", Assert.IsType<string>(permittedResult));
        Assert.Contains(
            "Denied (PathNotPermitted)",
            Assert.IsType<string>(refusedResult),
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Invokes a read tool exactly as a runtime would.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="path">The path argument to supply.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeReadAsync(AIFunction tool, string path)
    {
        return await tool.InvokeAsync(
            new AIFunctionArguments { ["path"] = path },
            TestContext.Current.CancellationToken);
    }
}

