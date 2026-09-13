using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFilePack"/> class.
/// </summary>
/// <remarks>
///     The pack is the only public way to obtain the family's tools, so these scenarios verify
///     what a composing application can observe: the prefix claimed, the capability required, the
///     tools produced, the shared cut/paste buffer, and that the policy the composition supplied is
///     the one governing them.
/// </remarks>
public class TextFilePackTests
{
    /// <summary>
    ///     Proves the published family prefix constant is the family name.
    /// </summary>
    [Fact]
    public void TextFilePack_FamilyPrefix_Constant_IsTextFile()
    {
        Assert.Equal("text_file", TextFilePack.FamilyPrefix);
    }

    /// <summary>
    ///     Proves the contract reports the same prefix the class publishes as a constant.
    /// </summary>
    [Fact]
    public void TextFilePack_FamilyPrefix_Contract_ReportsTheDeclaredConstant()
    {
        IToolPack pack = new TextFilePack();
        Assert.Equal(TextFilePack.FamilyPrefix, pack.FamilyPrefix);
    }

    /// <summary>
    ///     Proves the family asks nothing of its host.
    /// </summary>
    [Fact]
    public void TextFilePack_RequiredCapabilities_Pack_RequiresNoHostCapability()
    {
        Assert.Equal(HostCapabilities.None, new TextFilePack().RequiredCapabilities);
    }

    /// <summary>
    ///     Proves the pack creates the seven tools in the fixed, documented order.
    /// </summary>
    [Fact]
    public void TextFilePack_CreateTools_Policy_CreatesTheSevenToolsInOrder()
    {
        var pack = new TextFilePack();
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        var tools = pack.CreateTools(policy).ToList();

        Assert.Equal(7, tools.Count);
        Assert.Equal(
            [
                TextFileSearchTool.ToolName,
                TextFileReadTool.ToolName,
                TextFileCreateTool.ToolName,
                TextFileReplaceTool.ToolName,
                TextFileCutLinesTool.ToolName,
                TextFileCopyLinesTool.ToolName,
                TextFilePasteLinesTool.ToolName
            ],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves the pack honors the contract obligation to return no null tool.
    /// </summary>
    [Fact]
    public void TextFilePack_CreateTools_Policy_ReturnsNoNullTool()
    {
        var pack = new TextFilePack();
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        var tools = pack.CreateTools(policy).ToList();

        Assert.NotEmpty(tools);
        Assert.All(tools, Assert.NotNull);
    }

    /// <summary>
    ///     Proves every tool the pack creates carries the family prefix it declares.
    /// </summary>
    [Fact]
    public void TextFilePack_CreateTools_EveryTool_CarriesTheFamilyPrefix()
    {
        var pack = new TextFilePack();
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        var tools = pack.CreateTools(policy).ToList();

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
        var pack = new TextFilePack();
        Assert.Throws<ArgumentNullException>(() => pack.CreateTools(null!).ToList());
    }

    /// <summary>
    ///     Proves the cut and paste tools one composition produces share a buffer, so a range cut
    ///     through one is pasteable through the other.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFilePack_CreateTools_CutAndPaste_ShareOneBufferPerComposition()
    {
        using var fixture = new ReparsePointFixture();
        var path = ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "one\ntwo\nthree\n");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadWrite(fixture.Root)]);
        var tools = new ToolPackBuilder(policy).Add(new TextFilePack()).Build();
        var cut = tools.Single(tool => tool.Name == TextFileCutLinesTool.ToolName);
        var paste = tools.Single(tool => tool.Name == TextFilePasteLinesTool.ToolName);

        await cut.InvokeAsync(
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 2, ["endLine"] = 2 },
            TestContext.Current.CancellationToken);
        await paste.InvokeAsync(
            new AIFunctionArguments { ["path"] = "note.txt", ["atLine"] = 2 },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            "one\ntwo\nthree\n",
            await System.IO.File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves two separate compositions do not share buffer slots.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFilePack_CreateTools_TwoCompositions_DoNotShareBufferSlots()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "one\ntwo\n");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadWrite(fixture.Root)]);

        var first = new ToolPackBuilder(policy).Add(new TextFilePack()).Build();
        await first.Single(tool => tool.Name == TextFileCutLinesTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["path"] = "note.txt", ["startLine"] = 1, ["endLine"] = 1 },
            TestContext.Current.CancellationToken);

        var second = new ToolPackBuilder(policy).Add(new TextFilePack()).Build();
        var result = await second.Single(tool => tool.Name == TextFilePasteLinesTool.ToolName).InvokeAsync(
            new AIFunctionArguments { ["path"] = "note.txt" },
            TestContext.Current.CancellationToken);

        Assert.Contains("Denied (TargetNotFound)", Assert.IsType<string>(result), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the policy the composer supplies is the one governing the created tools.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFilePack_CreateTools_SuppliedPolicy_GovernsTheCreatedTools()
    {
        using var fixture = new ReparsePointFixture();
        var permitted = ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "permitted");
        var refused = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "secret");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadWrite(fixture.Root)]);
        var readTool = new TextFilePack().CreateTools(policy)
            .Single(tool => tool.Name == TextFileReadTool.ToolName);

        var permittedResult = await InvokeReadAsync(readTool, permitted);
        var refusedResult = await InvokeReadAsync(readTool, refused);

        Assert.Contains("permitted", Assert.IsType<string>(permittedResult), StringComparison.Ordinal);
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
