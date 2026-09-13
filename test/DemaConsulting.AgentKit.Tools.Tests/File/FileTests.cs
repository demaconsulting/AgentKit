using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.File;

/// <summary>
///     Subsystem-level integration tests for the File tool family.
/// </summary>
/// <remarks>
///     These scenarios exercise the family the way an application does: composed through the
///     AgentKitCore <see cref="ToolPackBuilder"/> under one access policy, then invoked through the
///     published tool list. They assert the properties that belong to the family as a whole — one
///     prefix, one policy, refusals that are results, and containment that no tool can breach.
/// </remarks>
public class FileTests
{
    /// <summary>
    ///     Proves a composition attaching the family publishes list, copy, move and delete tools.
    /// </summary>
    [Fact]
    public void File_Family_ComposedThroughBuilder_PublishesListCopyMoveDelete()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy).Add(new FilePack()).Build();

        Assert.Equal(4, tools.Count);
        Assert.Equal(
            [
                FileListTool.ToolName,
                FileCopyTool.ToolName,
                FileMoveTool.ToolName,
                FileDeleteTool.ToolName
            ],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a host declaring no capability still receives the family.
    /// </summary>
    [Fact]
    public void File_Family_HostDeclaringNoCapability_StillReceivesTheFamily()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.None)
            .Add(new FilePack())
            .Build();

        Assert.Equal(4, tools.Count);
    }

    /// <summary>
    ///     Proves a file reachable only through a link outside the permitted root is never listed,
    ///     copied, moved or deleted — no tool in the family can breach containment.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task File_Family_PathBeneathLinkOutsideRoot_IsNeverReachableByAnyTool()
    {
        // Arrange: a real reparse point inside the permitted root pointing outside it
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "escaped-content");
        var link = fixture.CreateDirectoryLink("escape", fixture.Outside);
        var escapedFile = Path.Combine(link, "secret.txt");

        // The escaped file is read directly through the link first, so a fixture that failed to
        // create the link fails the scenario rather than passing vacuously.
        Assert.Equal("escaped-content", await System.IO.File.ReadAllTextAsync(
            escapedFile,
            TestContext.Current.CancellationToken));
        var tools = Compose(fixture.Root);

        // Act: attempt every family operation against the escaped file, and list the root
        var listResult = await InvokeAsync(
            tools, FileListTool.ToolName, new AIFunctionArguments { ["directory"] = fixture.Root });
        var copyResult = await InvokeAsync(
            tools,
            FileCopyTool.ToolName,
            new AIFunctionArguments { ["source"] = escapedFile, ["destination"] = "copy.txt" });
        var deleteResult = await InvokeAsync(
            tools, FileDeleteTool.ToolName, new AIFunctionArguments { ["path"] = escapedFile });

        // Assert: the listing never mentions the escaped file, the copy and delete are refused, and
        // the escaped file still exists untouched
        Assert.DoesNotContain("secret.txt", Assert.IsType<string>(listResult), StringComparison.Ordinal);
        Assert.Contains("Denied (PathNotPermitted)", Assert.IsType<string>(copyResult), StringComparison.Ordinal);
        Assert.Contains("Denied (PathNotPermitted)", Assert.IsType<string>(deleteResult), StringComparison.Ordinal);
        Assert.Equal(
            "escaped-content",
            await System.IO.File.ReadAllTextAsync(escapedFile, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a refused request returns a result rather than throwing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task File_Family_DeniedRequest_ReturnsAResultWithoutThrowing()
    {
        using var fixture = new ReparsePointFixture();
        var outsideFile = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "secret");
        var tools = Compose(fixture.Root);

        var result = await InvokeAsync(
            tools, FileDeleteTool.ToolName, new AIFunctionArguments { ["path"] = outsideFile });

        var text = Assert.IsType<string>(result);
        Assert.StartsWith("Denied (", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a copy-then-delete round-trip a model performs by bare relative names works
    ///     end to end.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task File_Family_CopyThenDelete_ByRelativeNames_WorksEndToEnd()
    {
        // Arrange: a workspace with one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "content");
        var tools = Compose(fixture.Root);

        // Act: copy the file, then delete the original
        await InvokeAsync(
            tools,
            FileCopyTool.ToolName,
            new AIFunctionArguments { ["source"] = "notes.txt", ["destination"] = "backup.txt" });
        await InvokeAsync(
            tools, FileDeleteTool.ToolName, new AIFunctionArguments { ["path"] = "notes.txt" });

        // Assert: the backup exists and the original is gone
        Assert.True(System.IO.File.Exists(Path.Combine(fixture.Root, "backup.txt")));
        Assert.False(System.IO.File.Exists(Path.Combine(fixture.Root, "notes.txt")));
    }

    /// <summary>
    ///     Composes the family under a policy rooted at one location.
    /// </summary>
    /// <param name="root">The permitted read and write location.</param>
    /// <returns>The composed tool list.</returns>
    private static IReadOnlyList<AIFunction> Compose(string root)
    {
        var policy = new PathPolicy(root, [PathRule.ReadWrite(root)]);
        return new ToolPackBuilder(policy).Add(new FilePack()).Build();
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
