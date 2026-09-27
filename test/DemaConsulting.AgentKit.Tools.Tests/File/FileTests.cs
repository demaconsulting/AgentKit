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
    ///     Proves a composition attaching the family publishes every tool the family has.
    /// </summary>
    [Fact]
    public void File_Family_ComposedThroughBuilder_PublishesTheWholeFamily()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tools = new ToolPackBuilder(policy).Add(new FilePack()).Build();

        Assert.Equal(7, tools.Count);
        Assert.Equal(
            [
                FileListTool.ToolName,
                FileCopyTool.ToolName,
                FileMoveTool.ToolName,
                FileDeleteTool.ToolName,
                FileCreateDirectoryTool.ToolName,
                FileMoveDirectoryTool.ToolName,
                FileDeleteDirectoryTool.ToolName
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

        Assert.Equal(7, tools.Count);
    }

    /// <summary>
    ///     Proves a file or directory outside the permitted root is never listed, copied, moved,
    ///     deleted, created over or removed — no tool in the family can breach containment.
    /// </summary>
    /// <remarks>
    ///     The bait is placed where an unguarded operation would genuinely find it: the grant covers
    ///     only a workspace subdirectory, and the file sits in its ungranted parent, which is the
    ///     directory the listing request names. Every leg therefore fails if the read or write
    ///     decision stops being consulted, rather than holding by path arithmetic alone.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task File_Family_PathOutsideRoot_IsNeverReachableByAnyTool()
    {
        // Arrange: grant only a workspace subdirectory, and bait its ungranted parent
        using var fixture = new TempDirectoryFixture();
        var workspace = Path.Combine(fixture.Root, "workspace");
        Directory.CreateDirectory(workspace);
        var outsideFile = TempDirectoryFixture.WriteFile(
            fixture.Root,
            "secret.txt",
            "outside-content");
        var outsideDirectory = Path.Combine(fixture.Root, "secrets");
        TempDirectoryFixture.WriteFile(outsideDirectory, "held.txt", "outside-content");
        var tools = Compose(workspace);

        // Act: list the ungranted parent, then attempt every mutating operation against the bait
        var listResult = await InvokeAsync(
            tools, FileListTool.ToolName, new AIFunctionArguments { ["directory"] = fixture.Root });
        var copyResult = await InvokeAsync(
            tools,
            FileCopyTool.ToolName,
            new AIFunctionArguments { ["source"] = outsideFile, ["destination"] = "copy.txt" });
        var moveResult = await InvokeAsync(
            tools,
            FileMoveTool.ToolName,
            new AIFunctionArguments { ["source"] = outsideFile, ["destination"] = "moved.txt" });
        var deleteResult = await InvokeAsync(
            tools, FileDeleteTool.ToolName, new AIFunctionArguments { ["path"] = outsideFile });
        var createDirectoryResult = await InvokeAsync(
            tools,
            FileCreateDirectoryTool.ToolName,
            new AIFunctionArguments { ["path"] = Path.Combine(fixture.Root, "made") });
        var moveDirectoryResult = await InvokeAsync(
            tools,
            FileMoveDirectoryTool.ToolName,
            new AIFunctionArguments { ["source"] = outsideDirectory, ["destination"] = "taken" });
        var deleteDirectoryResult = await InvokeAsync(
            tools,
            FileDeleteDirectoryTool.ToolName,
            new AIFunctionArguments { ["path"] = outsideDirectory });

        // Assert: the listing is refused and never names the bait; every mutating request is
        // refused; and both baits still exist untouched
        var listText = Assert.IsType<string>(listResult);
        Assert.Contains("Denied (PathNotPermitted)", listText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.txt", listText, StringComparison.Ordinal);
        Assert.Contains("Denied (PathNotPermitted)", Assert.IsType<string>(copyResult), StringComparison.Ordinal);
        Assert.Contains("Denied (PathNotPermitted)", Assert.IsType<string>(moveResult), StringComparison.Ordinal);
        Assert.Contains("Denied (PathNotPermitted)", Assert.IsType<string>(deleteResult), StringComparison.Ordinal);
        Assert.Contains(
            "Denied (PathNotPermitted)",
            Assert.IsType<string>(createDirectoryResult),
            StringComparison.Ordinal);
        Assert.Contains(
            "Denied (PathNotPermitted)",
            Assert.IsType<string>(moveDirectoryResult),
            StringComparison.Ordinal);
        Assert.Contains(
            "Denied (PathNotPermitted)",
            Assert.IsType<string>(deleteDirectoryResult),
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "made")));
        Assert.True(Directory.Exists(outsideDirectory));
        Assert.Equal(
            "outside-content",
            await System.IO.File.ReadAllTextAsync(outsideFile, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a refused request returns a result rather than throwing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task File_Family_DeniedRequest_ReturnsAResultWithoutThrowing()
    {
        using var fixture = new TempDirectoryFixture();
        var outsideFile = TempDirectoryFixture.WriteFile(fixture.Outside, "secret.txt", "secret");
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
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "notes.txt", "content");
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
    ///     Proves an agent can create a directory, move — and thereby rename — it, and remove it
    ///     with everything beneath it, through the composed family and by bare relative names.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task File_Family_DirectoryLifecycle_CreateMoveDelete_WorksEndToEnd()
    {
        // Arrange: an empty workspace the family is composed over
        using var fixture = new TempDirectoryFixture();
        var tools = Compose(fixture.Root);

        // Act: create a nested directory, put a file in it, rename it beside itself, then remove
        // the whole thing
        await InvokeAsync(
            tools,
            FileCreateDirectoryTool.ToolName,
            new AIFunctionArguments { ["path"] = "reports/drafts" });
        TempDirectoryFixture.WriteFile(
            Path.Combine(fixture.Root, "reports", "drafts"), "note.txt", "content");
        await InvokeAsync(
            tools,
            FileMoveDirectoryTool.ToolName,
            new AIFunctionArguments
            {
                ["source"] = "reports/drafts",
                ["destination"] = "reports/final"
            });
        var renamedHoldsTheFile = System.IO.File.Exists(
            Path.Combine(fixture.Root, "reports", "final", "note.txt"));
        var deleteResult = await InvokeAsync(
            tools,
            FileDeleteDirectoryTool.ToolName,
            new AIFunctionArguments { ["path"] = "reports" });

        // Assert: the rename carried the content, the removal reported its scale, and nothing of
        // the tree is left
        Assert.True(renamedHoldsTheFile);
        Assert.Contains(
            "3 entries", Assert.IsType<string>(deleteResult), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "reports")));
    }

    /// <summary>
    ///     Proves a recursive removal composed through the family never follows a link out of the
    ///     directory it was given.
    /// </summary>
    /// <remarks>
    ///     The security property verified here belongs to the family rather than to one unit: an
    ///     application attaches the pack, and what it must be able to rely on is that no tool it
    ///     received can remove content the request never named. The link is real — a junction on
    ///     Windows, a symbolic link elsewhere — because the decision under test is made about real
    ///     reparse points.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task File_Family_RecursiveDelete_LinkOutOfTheGrant_IsNeverFollowed()
    {
        // Arrange: a permitted tree holding a link to an ungranted sibling that holds a file
        using var fixture = new TempDirectoryFixture();
        var tree = Path.Combine(fixture.Root, "build");
        Directory.CreateDirectory(tree);
        var outsideFile = TempDirectoryFixture.WriteFile(
            fixture.Outside, "secret.txt", "outside-content");
        using var link = DirectoryLink.Create(Path.Combine(tree, "escape"), fixture.Outside);
        var tools = Compose(fixture.Root);

        // Act: ask the composed family to remove the whole tree
        var result = await InvokeAsync(
            tools, FileDeleteDirectoryTool.ToolName, new AIFunctionArguments { ["path"] = "build" });

        // Assert: refused, the tree is untouched, and what lay beyond the link is intact
        Assert.Contains(
            "Denied (InvalidRequest)", Assert.IsType<string>(result), StringComparison.Ordinal);
        Assert.True(Directory.Exists(tree));
        Assert.Equal(
            "outside-content",
            await System.IO.File.ReadAllTextAsync(outsideFile, TestContext.Current.CancellationToken));
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
