using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.File;

/// <summary>
///     Unit tests for the <see cref="FileDeleteDirectoryTool"/> class.
/// </summary>
/// <remarks>
///     <para>
///     The three link scenarios are listed first because they were written first. They are the
///     scenarios with the worst consequence if the unit is wrong — content outside the permitted
///     location destroyed by a request that never named it — so they were made to fail against an
///     implementation whose walk followed links before the guard that stops it was written.
///     </para>
///     <para>
///     A link is created by <see cref="DirectoryLink"/>, which fails the test rather than skipping
///     when the platform refuses: a skipped test leaves no entry in the results, and the security
///     requirement would appear covered with no evidence behind it.
///     </para>
/// </remarks>
public class FileDeleteDirectoryToolTests
{
    /// <summary>
    ///     Proves a link found inside the tree is refused, and what it points at is untouched.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_TreeContainingALinkOutsideTheGrant_ReturnsDenialAndLeavesTheLinkTargetIntact()
    {
        // Arrange: a permitted tree holding a link to an ungranted sibling that holds a file
        using var fixture = new TempDirectoryFixture();
        var tree = Path.Combine(fixture.Root, "build");
        Directory.CreateDirectory(tree);
        var outsideFile = TempDirectoryFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        using var link = DirectoryLink.Create(Path.Combine(tree, "escape"), fixture.Outside);
        var tool = FileDeleteDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for the whole tree, which the walk would have to enter the link to remove
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "build" });

        // Assert: refused, the offending entry is named as the model spelled it, the link's
        // target is disclosed nowhere, and the file beyond the link still holds its content
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("build/escape", text, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Outside, text, StringComparison.Ordinal);
        Assert.True(System.IO.File.Exists(outsideFile));
        Assert.Equal(
            "outside-content",
            await System.IO.File.ReadAllTextAsync(outsideFile, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves the refusal is taken before anything is removed, so a tree holding a link is
    ///     left whole rather than half destroyed.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_TreeContainingALinkOutsideTheGrant_RemovesNothingFromTheTree()
    {
        // Arrange: a permitted tree holding both an ordinary file and a link out of the grant
        using var fixture = new TempDirectoryFixture();
        var tree = Path.Combine(fixture.Root, "build");
        var inside = TempDirectoryFixture.WriteFile(tree, "output.txt", "inside-content");
        using var link = DirectoryLink.Create(Path.Combine(tree, "escape"), fixture.Outside);
        var tool = FileDeleteDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for the whole tree
        await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "build" });

        // Assert: the directory, its ordinary file and the link entry are all still there
        Assert.True(Directory.Exists(tree));
        Assert.True(System.IO.File.Exists(inside));
        Assert.True(Directory.Exists(link.Path));
    }

    /// <summary>
    ///     Proves a named directory that is itself a link is removed as the link alone.
    /// </summary>
    /// <remarks>
    ///     This is what keeps the refusal above from being a dead end. Were a link never
    ///     removable, a workspace containing one would be permanently undeletable by an agent,
    ///     because the single-file delete tool refuses a directory and a link is a directory.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_DirectoryThatIsItselfALink_RemovesTheLinkAndNotItsTarget()
    {
        // Arrange: a link inside the permitted location pointing at an ungranted sibling
        using var fixture = new TempDirectoryFixture();
        var outsideFile = TempDirectoryFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        using var link = DirectoryLink.Create(Path.Combine(fixture.Root, "linked"), fixture.Outside);
        var tool = FileDeleteDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: name the link directly
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "linked" });

        // Assert: the link is gone, the target directory and its file are not
        Assert.IsType<string>(result);
        Assert.DoesNotContain("Denied", (string)result, StringComparison.Ordinal);
        Assert.False(Directory.Exists(link.Path));
        Assert.True(Directory.Exists(fixture.Outside));
        Assert.Equal(
            "outside-content",
            await System.IO.File.ReadAllTextAsync(outsideFile, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void FileDeleteDirectoryTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("file_delete_directory", FileDeleteDirectoryTool.ToolName);
        Assert.StartsWith(
            FilePack.FamilyPrefix + "_", FileDeleteDirectoryTool.ToolName, StringComparison.Ordinal);

        ToolName.Validate(FileDeleteDirectoryTool.ToolName);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void FileDeleteDirectoryTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FileDeleteDirectoryTool.Create(null!));
    }

    /// <summary>
    ///     Proves a permitted tree is removed whole and the result reports how many entries went.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_PermittedTree_RemovesItAndReportsTheEntryCount()
    {
        // Arrange: a tree of four entries — the named directory, a nested directory and two files
        using var fixture = new TempDirectoryFixture();
        var tree = Path.Combine(fixture.Root, "build");
        TempDirectoryFixture.WriteFile(tree, "output.txt", "content");
        TempDirectoryFixture.WriteFile(Path.Combine(tree, "nested"), "deep.txt", "content");
        var tool = FileDeleteDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: remove it
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "build" });

        // Assert: gone, and the exact count is reported so the model knows the scale of what it did
        var text = Assert.IsType<string>(result);
        Assert.Contains("4 entries", text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(tree));
    }

    /// <summary>
    ///     Proves an empty directory is removed, which is the smallest tree the tool handles.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_EmptyDirectory_RemovesIt()
    {
        using var fixture = new TempDirectoryFixture();
        var empty = Path.Combine(fixture.Root, "empty");
        Directory.CreateDirectory(empty);
        var tool = FileDeleteDirectoryTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "empty" });

        Assert.IsType<string>(result);
        Assert.False(Directory.Exists(empty));
    }

    /// <summary>
    ///     Proves a tree larger than the configured ceiling is refused, naming both numbers.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_TreeExceedingTheEntryCeiling_ReturnsDenialNamingTheCountAndTheLimit()
    {
        // Arrange: a four-entry tree under a host that permits two entries per removal
        using var fixture = new TempDirectoryFixture();
        var tree = Path.Combine(fixture.Root, "build");
        TempDirectoryFixture.WriteFile(tree, "one.txt", "content");
        TempDirectoryFixture.WriteFile(Path.Combine(tree, "nested"), "two.txt", "content");
        var tool = FileDeleteDirectoryTool.Create(BoundedPolicy(fixture.Root, 2));

        // Act: ask for the whole tree
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "build" });

        // Assert: refused with the real count, not "more than", and with the host's own ceiling
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("4", text, StringComparison.Ordinal);
        Assert.Contains("2", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the ceiling is enforced before anything is removed, never part way through.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_TreeExceedingTheEntryCeiling_RemovesNothing()
    {
        // Arrange: the same oversized tree
        using var fixture = new TempDirectoryFixture();
        var tree = Path.Combine(fixture.Root, "build");
        var first = TempDirectoryFixture.WriteFile(tree, "one.txt", "content");
        var second = TempDirectoryFixture.WriteFile(Path.Combine(tree, "nested"), "two.txt", "content");
        var tool = FileDeleteDirectoryTool.Create(BoundedPolicy(fixture.Root, 2));

        // Act: ask for the whole tree
        await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "build" });

        // Assert: every entry survives, so no half-destroyed tree is left behind
        Assert.True(Directory.Exists(tree));
        Assert.True(System.IO.File.Exists(first));
        Assert.True(System.IO.File.Exists(second));
    }

    /// <summary>
    ///     Proves a ceiling of zero forbids recursive removal entirely.
    /// </summary>
    /// <remarks>
    ///     The named directory counts as an entry, so even an empty one costs a single entry and
    ///     a zero ceiling refuses it. That is what makes zero the expressible way for a host to
    ///     attach the family and withhold this capability.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_ZeroEntryCeiling_RefusesEvenAnEmptyDirectory()
    {
        using var fixture = new TempDirectoryFixture();
        var empty = Path.Combine(fixture.Root, "empty");
        Directory.CreateDirectory(empty);
        var tool = FileDeleteDirectoryTool.Create(BoundedPolicy(fixture.Root, 0));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "empty" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.True(Directory.Exists(empty));
    }

    /// <summary>
    ///     Proves a removal outside the write grant is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_ReadOnlyLocation_ReturnsDenialAndLeavesTheTree()
    {
        // Arrange: a readable but unwritable root, so no removal is permitted
        using var fixture = new TempDirectoryFixture();
        var tree = Path.Combine(fixture.Root, "build");
        var inside = TempDirectoryFixture.WriteFile(tree, "output.txt", "content");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        var tool = FileDeleteDirectoryTool.Create(policy);

        // Act: attempt the removal within the read-only location
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "build" });

        // Assert: refused, and the tree is intact
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.True(Directory.Exists(tree));
        Assert.True(System.IO.File.Exists(inside));
    }

    /// <summary>
    ///     Proves a missing directory is reported with a plain fact that names no other tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_MissingDirectory_ReturnsDenialNamingNoTool()
    {
        using var fixture = new TempDirectoryFixture();
        var tool = FileDeleteDirectoryTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "absent" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(FileListTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a path naming a file is refused and the file is left alone.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_PathIsAFile_ReturnsDenialAndLeavesTheFile()
    {
        using var fixture = new TempDirectoryFixture();
        var file = TempDirectoryFixture.WriteFile(fixture.Root, "notes.txt", "content");
        var tool = FileDeleteDirectoryTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "notes.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.True(System.IO.File.Exists(file));
    }

    /// <summary>
    ///     Proves a tree holding exactly as many entries as the ceiling permits is removed whole.
    /// </summary>
    /// <remarks>
    ///     The boundary the walk's early abandon sits on. Paths stop being retained once the
    ///     running count passes the ceiling, so a plan of exactly the ceiling must still be
    ///     complete — an off-by-one there would leave the last entry behind while the
    ///     confirmation reported the whole tree removed, which is a silent partial removal
    ///     reported as a success. The largest permitted tree is the only place that shows.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_TreeExactlyAtTheEntryCeiling_RemovesEveryEntry()
    {
        // Arrange: a four-entry tree under a host that permits exactly four entries per removal
        using var fixture = new TempDirectoryFixture();
        var tree = Path.Combine(fixture.Root, "build");
        TempDirectoryFixture.WriteFile(tree, "one.txt", "content");
        TempDirectoryFixture.WriteFile(Path.Combine(tree, "nested"), "two.txt", "content");
        var tool = FileDeleteDirectoryTool.Create(BoundedPolicy(fixture.Root, 4));

        // Act: ask for the whole tree
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "build" });

        // Assert: accepted, all four reported, and nothing at all left behind
        var text = Assert.IsType<string>(result);
        Assert.DoesNotContain("Denied", text, StringComparison.Ordinal);
        Assert.Contains("4 entries", text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(tree));
    }

    /// <summary>
    ///     Proves a file-system failure during the planning walk is returned as a refusal rather
    ///     than thrown out of the tool.
    /// </summary>
    /// <remarks>
    ///     The walk is the one part of this unit that touches an arbitrary tree, so it is the one
    ///     part that meets a directory the process may not enumerate, or a child that has gone
    ///     since the parent was listed. The family's load-bearing rule is that a refusal is a
    ///     result and never an exception, and an exception escaping here would leave the agent
    ///     runtime — not the tool — deciding what the model is told.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_TreeHoldingAnUnreadableDirectory_ReturnsDenialRatherThanThrowing()
    {
        // Arrange: a permitted tree whose sub-directory cannot be looked inside
        using var fixture = new TempDirectoryFixture();
        var tree = Path.Combine(fixture.Root, "build");
        var inside = TempDirectoryFixture.WriteFile(tree, "output.txt", "content");
        var closedPath = Path.Combine(tree, "closed");
        Directory.CreateDirectory(closedPath);
        using var closed = RestrictedDirectory.Unreadable(closedPath);
        var tool = FileDeleteDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for the whole tree, which the walk cannot plan
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "build" });

        // Assert: a returned refusal carrying no host path, and nothing removed — the walk
        // mutates nothing, so the plain refusal is the whole truth
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("could not be deleted", text, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Root, text, StringComparison.Ordinal);
        Assert.True(Directory.Exists(tree));
        Assert.True(System.IO.File.Exists(inside));
    }

    /// <summary>
    ///     Proves a removal that fails part way through says how much of the tree is already
    ///     gone.
    /// </summary>
    /// <remarks>
    ///     Every other refusal this unit composes states that nothing was deleted, so a bare
    ///     "the directory could not be deleted" would be read as "the tree is intact". Naming the
    ///     figure is what lets a model tell a tree it still has from one it partly lost — the
    ///     phase-two failure the two-phase design bounds but cannot prevent.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteDirectoryTool_Delete_RemovalFailingPartWayThrough_ReportsHowManyEntriesWereRemoved()
    {
        // Arrange: a four-entry tree whose nested file cannot be removed. The walk plans it
        // whole, so the failure lands in phase two after the first file has already gone.
        using var fixture = new TempDirectoryFixture();
        var tree = Path.Combine(fixture.Root, "build");
        var removable = TempDirectoryFixture.WriteFile(tree, "output.txt", "content");
        var nested = Path.Combine(tree, "nested");
        TempDirectoryFixture.WriteFile(nested, "kept.txt", "content");
        using var locked = RestrictedDirectory.ContentsUndeletable(nested);
        var tool = FileDeleteDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for the whole tree
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "build" });

        // Assert: refused, naming what went and what the plan covered, and the first file really
        // is gone — so the figure describes the tree as it now is
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("1 of 4 entries were removed", text, StringComparison.Ordinal);
        Assert.False(System.IO.File.Exists(removable));
        Assert.True(Directory.Exists(nested));
    }

    /// <summary>
    ///     Composes a policy over one read-write location.
    /// </summary>
    /// <param name="root">The permitted location.</param>
    /// <returns>The policy.</returns>
    private static PathPolicy RootedPolicy(string root)
    {
        return new PathPolicy(root, [PathRule.ReadWrite(root)]);
    }

    /// <summary>
    ///     Composes a policy over one read-write location, carrying a host-chosen removal ceiling.
    /// </summary>
    /// <param name="root">The permitted location.</param>
    /// <param name="maxDeleteEntries">The ceiling on entries one removal may take.</param>
    /// <returns>The policy.</returns>
    private static PathPolicy BoundedPolicy(string root, int maxDeleteEntries)
    {
        return new PathPolicy(
            root,
            [PathRule.ReadWrite(root)],
            new ToolLimits(maxDeleteEntries: maxDeleteEntries));
    }

    /// <summary>
    ///     Invokes a tool with the supplied arguments, exactly as a runtime would.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="arguments">The arguments to supply.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeAsync(AIFunction tool, AIFunctionArguments arguments)
    {
        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }
}
