using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Subsystem-level integration tests for the TextFile tool family.
/// </summary>
/// <remarks>
///     These scenarios exercise the family the way an application does: composed through the
///     AgentKitCore <see cref="ToolPackBuilder"/> under one access policy, then invoked through the
///     published tool list. They assert the properties that belong to the family as a whole — one
///     prefix, one policy, refusals that are results, containment no tool can breach, and the
///     search-read-edit loop working end to end by the paths a model actually sends.
/// </remarks>
public class TextFileTests
{
    /// <summary>
    ///     Proves a composition attaching the family publishes the seven content tools in order.
    /// </summary>
    [Fact]
    public void TextFile_Family_ComposedThroughBuilder_PublishesTheSevenContentTools()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy).Add(new TextFilePack()).Build();

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
    ///     Proves a host declaring no capability still receives the family.
    /// </summary>
    [Fact]
    public void TextFile_Family_HostDeclaringNoCapability_StillReceivesTheFamily()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.None)
            .Add(new TextFilePack())
            .Build();

        Assert.Equal(7, tools.Count);
    }

    /// <summary>
    ///     Proves every tool in the family carries a valid name and a description.
    /// </summary>
    [Fact]
    public void TextFile_Family_EveryTool_CarriesAValidatedNameAndDescription()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy).Add(new TextFilePack()).Build();

        Assert.All(tools, tool =>
        {
            ToolName.Validate(tool.Name);
            Assert.StartsWith(TextFilePack.FamilyPrefix + "_", tool.Name, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        });
    }

    /// <summary>
    ///     Proves a family tool's result reaches the caller in the form the tool returned it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFile_Family_ToolResult_ReachesTheCallerUnserialized()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content");
        var tools = Compose(fixture.Root);

        var result = await InvokeAsync(
            tools, TextFileReadTool.ToolName, new AIFunctionArguments { ["path"] = "note.txt" });

        Assert.IsType<string>(result);
        Assert.IsNotType<JsonElement>(result);
    }

    /// <summary>
    ///     Proves a read-wide, write-narrow policy permits the read and refuses the edit of one path.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFile_Family_ReadWideWriteNarrow_PermitsTheReadAndRefusesTheEdit()
    {
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "original");
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadOnly(fixture.Root), PathRule.ReadWrite(fixture.Outside)]);
        var tools = new ToolPackBuilder(policy).Add(new TextFilePack()).Build();

        var readResult = await InvokeAsync(
            tools, TextFileReadTool.ToolName, new AIFunctionArguments { ["path"] = file });
        var editResult = await InvokeAsync(
            tools,
            TextFileReplaceTool.ToolName,
            new AIFunctionArguments { ["path"] = file, ["oldText"] = "original", ["newText"] = "x" });

        Assert.Contains("original", Assert.IsType<string>(readResult), StringComparison.Ordinal);
        Assert.Contains(
            "Denied (PathNotPermitted)",
            Assert.IsType<string>(editResult),
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a path reaching outside the permitted location through a link is refused by every
    ///     editing tool and never surfaced by search.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFile_Family_PathBeneathLinkOutsideRoot_IsRefusedByEveryTool()
    {
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "escaped-content");
        var link = fixture.CreateDirectoryLink("escape", fixture.Outside);
        var escapedFile = Path.Combine(link, "secret.txt");
        Assert.Equal("escaped-content", await System.IO.File.ReadAllTextAsync(
            escapedFile, TestContext.Current.CancellationToken));
        var tools = Compose(fixture.Root);

        var readResult = await InvokeAsync(
            tools, TextFileReadTool.ToolName, new AIFunctionArguments { ["path"] = escapedFile });
        var replaceResult = await InvokeAsync(
            tools,
            TextFileReplaceTool.ToolName,
            new AIFunctionArguments { ["path"] = escapedFile, ["oldText"] = "escaped-content", ["newText"] = "x" });
        var searchResult = await InvokeAsync(
            tools, TextFileSearchTool.ToolName, new AIFunctionArguments { ["pattern"] = "escaped-content" });

        Assert.Contains("Denied (PathNotPermitted)", Assert.IsType<string>(readResult), StringComparison.Ordinal);
        Assert.Contains("Denied (PathNotPermitted)", Assert.IsType<string>(replaceResult), StringComparison.Ordinal);
        Assert.Equal("No matches.", Assert.IsType<string>(searchResult));
        Assert.Equal(
            "escaped-content",
            await System.IO.File.ReadAllTextAsync(escapedFile, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a refused request returns a result rather than throwing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFile_Family_DeniedRequest_ReturnsAResultWithoutThrowing()
    {
        using var fixture = new ReparsePointFixture();
        var outsideFile = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "secret");
        var tools = Compose(fixture.Root);

        var result = await InvokeAsync(
            tools, TextFileReadTool.ToolName, new AIFunctionArguments { ["path"] = outsideFile });

        Assert.StartsWith("Denied (", Assert.IsType<string>(result), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the search-read-edit loop works end to end by the bare relative names a model
    ///     sends, sharing one workspace across all seven tools.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFile_Family_SearchReadEditLoop_WorksByRelativeNames()
    {
        using var fixture = new ReparsePointFixture();
        var tools = Compose(fixture.Root);

        // Create a file, search it, read it, then edit it — all by bare relative name.
        await InvokeAsync(
            tools,
            TextFileCreateTool.ToolName,
            new AIFunctionArguments { ["path"] = "notes.txt", ["content"] = "alpha\nbeta\ngamma\n" });
        var searchResult = await InvokeAsync(
            tools, TextFileSearchTool.ToolName, new AIFunctionArguments { ["pattern"] = "beta" });
        var readResult = await InvokeAsync(
            tools, TextFileReadTool.ToolName, new AIFunctionArguments { ["path"] = "notes.txt" });
        await InvokeAsync(
            tools,
            TextFileReplaceTool.ToolName,
            new AIFunctionArguments { ["path"] = "notes.txt", ["oldText"] = "beta", ["newText"] = "BETA" });

        Assert.Contains("notes.txt:2:beta", Assert.IsType<string>(searchResult), StringComparison.Ordinal);
        Assert.StartsWith("notes.txt lines 1-3 of 3", Assert.IsType<string>(readResult), StringComparison.Ordinal);
        Assert.Equal(
            "alpha\nBETA\ngamma\n",
            await System.IO.File.ReadAllTextAsync(
                Path.Combine(fixture.Root, "notes.txt"), TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves the owner's proven large-block duplication scenario end to end: a few hundred lines
    ///     are copied into the default buffer, a new file is created, the block is pasted into it, and
    ///     a short read confirms it — with the copied lines never entering the assertion by count
    ///     alone. The source is left byte-identical.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFile_Family_LargeBlockDuplication_CopiesCreatesAndPastes()
    {
        using var fixture = new ReparsePointFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "data"));

        // A non-trivial fixture: 500 numbered lines, each ending with a newline.
        var builder = new System.Text.StringBuilder();
        for (var number = 1; number <= 500; number++)
        {
            builder.Append("line").Append(number).Append('\n');
        }

        var original = builder.ToString();
        var largePath = Path.Combine(fixture.Root, "data", "large.txt");
        await System.IO.File.WriteAllTextAsync(largePath, original, TestContext.Current.CancellationToken);

        // The exact 301-line block a copy of lines 200-500 must reproduce.
        var expectedBlock = new System.Text.StringBuilder();
        for (var number = 200; number <= 500; number++)
        {
            expectedBlock.Append("line").Append(number).Append('\n');
        }

        var tools = Compose(fixture.Root);

        var copyResult = await InvokeAsync(
            tools,
            TextFileCopyLinesTool.ToolName,
            new AIFunctionArguments { ["path"] = "data/large.txt", ["startLine"] = 200, ["endLine"] = 500 });
        await InvokeAsync(
            tools,
            TextFileCreateTool.ToolName,
            new AIFunctionArguments { ["path"] = "data/extract.txt", ["content"] = string.Empty });
        var pasteResult = await InvokeAsync(
            tools,
            TextFilePasteLinesTool.ToolName,
            new AIFunctionArguments { ["path"] = "data/extract.txt", ["atLine"] = 1 });
        var readResult = await InvokeAsync(
            tools,
            TextFileReadTool.ToolName,
            new AIFunctionArguments { ["path"] = "data/extract.txt", ["lineCount"] = 5 });

        Assert.Contains("Copied 301 lines (200-500)", Assert.IsType<string>(copyResult), StringComparison.Ordinal);
        Assert.Contains("data/large.txt is unchanged", Assert.IsType<string>(copyResult), StringComparison.Ordinal);
        Assert.Contains("Pasted 301 lines", Assert.IsType<string>(pasteResult), StringComparison.Ordinal);
        Assert.Contains("line200", Assert.IsType<string>(readResult), StringComparison.Ordinal);

        // The extract holds exactly the copied block, and the source is byte-identical.
        Assert.Equal(
            expectedBlock.ToString(),
            await System.IO.File.ReadAllTextAsync(
                Path.Combine(fixture.Root, "data", "extract.txt"), TestContext.Current.CancellationToken));
        Assert.Equal(
            original,
            await System.IO.File.ReadAllTextAsync(largePath, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Composes the family under a policy rooted at one location.
    /// </summary>
    /// <param name="root">The permitted read and write location.</param>
    /// <returns>The composed tool list.</returns>
    private static IReadOnlyList<AIFunction> Compose(string root)
    {
        var policy = new PathPolicy(root, [PathRule.ReadWrite(root)]);
        return new ToolPackBuilder(policy).Add(new TextFilePack()).Build();
    }

    /// <summary>
    ///     Invokes one composed tool by name, exactly as a runtime would.
    /// </summary>
    /// <param name="tools">The composed tool list.</param>
    /// <param name="name">The name of the tool to invoke.</param>
    /// <param name="arguments">The arguments to supply.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeAsync(
        IReadOnlyList<AIFunction> tools,
        string name,
        AIFunctionArguments arguments)
    {
        var tool = tools.Single(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal));
        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }
}
