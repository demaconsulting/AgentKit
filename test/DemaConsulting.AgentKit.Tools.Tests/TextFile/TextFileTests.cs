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
///     AgentKitCore <see cref="ToolPackBuilder"/> under one access policy, then invoked through
///     the published tool list. They assert the properties that belong to the family as a whole
///     — one prefix, one policy, refusals that are results — rather than any single tool's
///     algorithm, which its own unit tests cover.
/// </remarks>
public class TextFileTests
{
    /// <summary>
    ///     Proves a composition attaching the family publishes a read, a write and a list tool.
    /// </summary>
    [Fact]
    public void TextFile_Family_ComposedThroughBuilder_PublishesReadWriteAndList()
    {
        // Arrange: a composition governed by one policy, with the family attached
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());
        var builder = new ToolPackBuilder(policy).Add(new TextFilePack());

        // Act: compose the tool list
        var tools = builder.Build();

        // Assert: the three tools the family promises, under the one family prefix
        Assert.Equal(3, tools.Count);
        Assert.Equal(
            [TextFileReadTool.ToolName, TextFileWriteTool.ToolName, TextFileListTool.ToolName],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a host declaring no capability still receives the family.
    /// </summary>
    [Fact]
    public void TextFile_Family_HostDeclaringNoCapability_StillReceivesTheFamily()
    {
        // Arrange: a host that declares nothing at all
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());
        var builder = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.None)
            .Add(new TextFilePack());

        // Act: compose the tool list
        var tools = builder.Build();

        // Assert: text file access asks nothing of a host, so nothing is gated away
        Assert.Equal(3, tools.Count);
    }

    /// <summary>
    ///     Proves every tool in the family carries a valid name and a description.
    /// </summary>
    [Fact]
    public void TextFile_Family_EveryTool_CarriesAValidatedNameAndDescription()
    {
        // Arrange: the family composed under one policy
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());
        var tools = new ToolPackBuilder(policy).Add(new TextFilePack()).Build();

        // Act / Assert: every name survives the convention's own validation, and no tool is
        // offered to a model without a description it can choose by
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
    /// <remarks>
    ///     The guarded construction path is what makes this true; without it the result would
    ///     arrive as a <see cref="JsonElement"/> wrapping serialized JSON.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFile_Family_ToolResult_ReachesTheCallerUnserialized()
    {
        // Arrange: the family composed over a permitted location holding a known file
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content");
        var tools = Compose(fixture.Root);

        // Act: read the file through the composed tool
        var result = await InvokeAsync(
            tools,
            TextFileReadTool.ToolName,
            new AIFunctionArguments { ["path"] = file });

        // Assert: plain text, not a JSON wrapping of it
        Assert.Equal("content", Assert.IsType<string>(result));
        Assert.IsNotType<JsonElement>(result);
    }

    /// <summary>
    ///     Proves a read-wide, write-narrow policy permits the read and refuses the write of one
    ///     path.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFile_Family_ReadWideWriteNarrow_PermitsTheReadAndRefusesTheWrite()
    {
        // Arrange: reads permitted beneath the root, writes permitted only outside it
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "original");
        var policy = new PathPolicy(
            PathRule.Rooted(fixture.Root),
            PathRule.Rooted(fixture.Outside));
        var tools = new ToolPackBuilder(policy).Add(new TextFilePack()).Build();

        // Act: read and then attempt to write the same path
        var readResult = await InvokeAsync(
            tools,
            TextFileReadTool.ToolName,
            new AIFunctionArguments { ["path"] = file });
        var writeResult = await InvokeAsync(
            tools,
            TextFileWriteTool.ToolName,
            new AIFunctionArguments { ["path"] = file, ["content"] = "replacement" });

        // Assert: the two rules are judged independently, as the operator configured them
        Assert.Equal("original", Assert.IsType<string>(readResult));
        Assert.Contains(
            "Denied (PathNotPermitted)",
            Assert.IsType<string>(writeResult),
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a path reaching outside the permitted location through a link is refused by
    ///     every tool in the family.
    /// </summary>
    /// <remarks>
    ///     The escaped file is read directly through the link first, so a fixture that failed to
    ///     create a real reparse point cannot make this scenario pass vacuously.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFile_Family_PathBeneathLinkOutsideRoot_IsRefusedByEveryTool()
    {
        // Arrange: a real reparse point inside the permitted root pointing outside it
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "escaped-content");
        var link = fixture.CreateDirectoryLink("escape", fixture.Outside);
        var escapedFile = Path.Combine(link, "secret.txt");
        Assert.Equal("escaped-content", await File.ReadAllTextAsync(
            escapedFile,
            TestContext.Current.CancellationToken));
        var tools = Compose(fixture.Root);

        // Act: attempt the same escape through each of the three tools
        var readResult = await InvokeAsync(
            tools,
            TextFileReadTool.ToolName,
            new AIFunctionArguments { ["path"] = escapedFile });
        var writeResult = await InvokeAsync(
            tools,
            TextFileWriteTool.ToolName,
            new AIFunctionArguments { ["path"] = escapedFile, ["content"] = "overwrite" });
        var listResult = await InvokeAsync(
            tools,
            TextFileListTool.ToolName,
            new AIFunctionArguments { ["directory"] = fixture.Root, ["searchPattern"] = null });

        // Assert: the read and the write are refused, and the listing never mentions the file
        Assert.Contains(
            "Denied (PathNotPermitted)",
            Assert.IsType<string>(readResult),
            StringComparison.Ordinal);
        Assert.Contains(
            "Denied (PathNotPermitted)",
            Assert.IsType<string>(writeResult),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "secret.txt",
            Assert.IsType<string>(listResult),
            StringComparison.Ordinal);
        Assert.Equal(
            "escaped-content",
            await File.ReadAllTextAsync(escapedFile, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a refused request returns a result rather than throwing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFile_Family_DeniedRequest_ReturnsAResultWithoutThrowing()
    {
        // Arrange: the family composed over a permitted location, and a path outside it
        using var fixture = new ReparsePointFixture();
        var outsideFile = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "secret");
        var tools = Compose(fixture.Root);

        // Act: request the refused file
        var result = await InvokeAsync(
            tools,
            TextFileReadTool.ToolName,
            new AIFunctionArguments { ["path"] = outsideFile });

        // Assert: a returned refusal naming its reason, because an exception would end the turn
        var text = Assert.IsType<string>(result);
        Assert.StartsWith("Denied (", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves no refusal the family produces discloses a host path.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFile_Family_DenialText_ContainsNoHostPath()
    {
        // Arrange: the family composed over a permitted location, and a path outside it
        using var fixture = new ReparsePointFixture();
        var outsideFile = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "secret");
        var tools = Compose(fixture.Root);

        // Act: collect the refusals of all three tools for locations outside the root
        var refusals = new[]
        {
            Assert.IsType<string>(await InvokeAsync(
                tools,
                TextFileReadTool.ToolName,
                new AIFunctionArguments { ["path"] = outsideFile })),
            Assert.IsType<string>(await InvokeAsync(
                tools,
                TextFileWriteTool.ToolName,
                new AIFunctionArguments { ["path"] = outsideFile, ["content"] = "x" })),
            Assert.IsType<string>(await InvokeAsync(
                tools,
                TextFileListTool.ToolName,
                new AIFunctionArguments
                {
                    ["directory"] = fixture.Outside,
                    ["searchPattern"] = null
                }))
        };

        // Assert: the transcript leaves this process, so it carries no host layout at all
        Assert.All(refusals, text =>
        {
            Assert.DoesNotContain(fixture.Root, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fixture.Outside, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret.txt", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                Path.DirectorySeparatorChar.ToString(),
                text,
                StringComparison.Ordinal);
        });
    }

    /// <summary>
    ///     Proves a file beyond the policy's read ceiling is refused rather than truncated.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFile_Family_FileBeyondTheReadCeiling_IsRefusedNotTruncated()
    {
        // Arrange: the family composed under a policy carrying a sixteen-byte read ceiling
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteFile(fixture.Root, "big.txt", new string('a', 128));
        var policy = new PathPolicy(
            PathRule.Rooted(fixture.Root),
            PathRule.Rooted(fixture.Root),
            new ToolLimits(maxReadBytes: 16));
        var tools = new ToolPackBuilder(policy).Add(new TextFilePack()).Build();

        // Act: read the oversized file
        var result = await InvokeAsync(
            tools,
            TextFileReadTool.ToolName,
            new AIFunctionArguments { ["path"] = file });

        // Assert: a refusal naming the ceiling, with no part of the file returned
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("16-byte", text, StringComparison.Ordinal);
        Assert.DoesNotContain("aaaa", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a path stated the way a model states it is resolved against the workspace by
    ///     every tool in the family.
    /// </summary>
    /// <remarks>
    ///     The family shares one access policy, so the workspace a bare name is measured from is
    ///     the same for a listing, a read and a write. A family in which the three disagreed
    ///     would let an agent list a name it then could not read.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFile_Family_RelativePathFromAModel_IsResolvedAgainstTheWorkspace()
    {
        // Arrange: the family composed over a workspace holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var tools = Compose(fixture.Root);

        // Act: list the workspace without naming it, then read the name the listing reported
        var listing = await InvokeAsync(
            tools,
            TextFileListTool.ToolName,
            new AIFunctionArguments());
        var read = await InvokeAsync(
            tools,
            TextFileReadTool.ToolName,
            new AIFunctionArguments { ["path"] = "notes.txt" });

        // Assert: the listing names the file relatively and the read accepts that name as given
        Assert.Equal("notes.txt", Assert.IsType<string>(listing));
        Assert.Equal("inside-content", Assert.IsType<string>(read));
    }

    /// <summary>
    ///     Composes the family under a policy rooted at one location.
    /// </summary>
    /// <param name="root">The permitted read and write location.</param>
    /// <returns>The composed tool list.</returns>
    private static IReadOnlyList<AIFunction> Compose(string root)
    {
        var policy = PathPolicy.ForWorkspace(root);
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
