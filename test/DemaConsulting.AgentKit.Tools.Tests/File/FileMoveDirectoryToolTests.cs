using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.File;

/// <summary>
///     Unit tests for the <see cref="FileMoveDirectoryTool"/> class.
/// </summary>
/// <remarks>
///     Both endpoints are judged by the write decision, so the policy-governed scenarios are
///     written as a pair — one refusing a read-only source, one refusing a read-only destination.
///     A single scenario would pass even if only one endpoint were being checked. The two
///     link-traversal scenarios are written as the same kind of pair, and for the same reason:
///     a guard applied to one endpoint only would leave the other open.
/// </remarks>
public class FileMoveDirectoryToolTests
{
    /// <summary>
    ///     Proves a source reached through a link out of the grant is refused, and the tree the
    ///     link points at survives whole.
    /// </summary>
    /// <remarks>
    ///     Path resolution is lexical, so <c>escape/victim</c> satisfies the write decision while
    ///     naming a tree outside every grant. The assertion that matters is the last one: the
    ///     content beyond the link is still there.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveDirectoryTool_Move_SourceReachedThroughALink_ReturnsDenialAndLeavesTheTargetIntact()
    {
        // Arrange: a link inside the grant pointing at an ungranted sibling holding a tree
        using var fixture = new TempDirectoryFixture();
        var bait = TempDirectoryFixture.WriteFile(
            Path.Combine(fixture.Outside, "victim"), "bait.txt", "outside-content");
        using var link = DirectoryLink.Create(Path.Combine(fixture.Root, "escape"), fixture.Outside);
        var tool = FileMoveDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: name a source that reaches outside the grant through the link
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "escape/victim", ["destination"] = "taken" });

        // Assert: refused, the offending component is named as the model spelled it, the link's
        // target is disclosed nowhere, and the tree outside the grant is untouched
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.Contains("escape", text, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Outside, text, StringComparison.Ordinal);
        Assert.True(System.IO.File.Exists(bait));
        Assert.True(Directory.Exists(Path.Combine(fixture.Outside, "victim")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "taken")));
    }

    /// <summary>
    ///     Proves a destination reached through a link out of the grant is refused, and nothing
    ///     is placed outside the grant.
    /// </summary>
    /// <remarks>
    ///     Written as the pair of the source scenario above: a guard applied to one endpoint only
    ///     would leave the other open, and a single scenario could not tell the two apart.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveDirectoryTool_Move_DestinationReachedThroughALink_ReturnsDenialAndLeavesTheTargetIntact()
    {
        // Arrange: a permitted tree, and a link inside the grant pointing at an ungranted sibling
        using var fixture = new TempDirectoryFixture();
        var inside = TempDirectoryFixture.WriteFile(
            Path.Combine(fixture.Root, "drafts"), "note.txt", "content");
        var bait = TempDirectoryFixture.WriteFile(fixture.Outside, "bait.txt", "outside-content");
        using var link = DirectoryLink.Create(Path.Combine(fixture.Root, "escape"), fixture.Outside);
        var tool = FileMoveDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: name a destination that reaches outside the grant through the link
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "drafts", ["destination"] = "escape/placed" });

        // Assert: refused, the source is still where it was, and nothing was created outside
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.Contains("escape", text, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Outside, text, StringComparison.Ordinal);
        Assert.True(System.IO.File.Exists(inside));
        Assert.True(System.IO.File.Exists(bait));
        Assert.False(Directory.Exists(Path.Combine(fixture.Outside, "placed")));
    }

    /// <summary>
    ///     Proves a destination that is a genuine child whose name begins with two dots is
    ///     recognized as lying inside the source.
    /// </summary>
    /// <remarks>
    ///     <c>Path.GetRelativePath</c> answers <c>..foo</c> for a real child of that name, which a
    ///     test for a leading <c>..</c> alone reads as climbing out of the source. The containment
    ///     test is therefore made of path segments, and this scenario is what holds it there.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveDirectoryTool_Move_DestinationIsAChildNamedWithLeadingDots_IsTreatedAsInsideTheSource()
    {
        // Arrange: a tree, and a destination inside it whose name begins with two dots
        using var fixture = new TempDirectoryFixture();
        var inside = TempDirectoryFixture.WriteFile(
            Path.Combine(fixture.Root, "drafts"), "note.txt", "content");
        var tool = FileMoveDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: attempt to move the tree into that child
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "drafts", ["destination"] = "drafts/..foo" });

        // Assert: refused as a destination inside the source, rather than attempted and failing
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("inside the directory being moved", text, StringComparison.Ordinal);
        Assert.True(System.IO.File.Exists(inside));
    }

    /// <summary>
    ///     Proves a source holding an entry the policy withholds is refused, naming that entry,
    ///     and the tree stays where it is.
    /// </summary>
    /// <remarks>
    ///     The same defect as the link escapes, by a third route: the tool judged the two paths
    ///     it was <em>named</em> and not what <see cref="Directory.Move(string, string)"/>
    ///     actually relocates. A grant of <c>ReadWrite(root, ["*.key"])</c> permits the directory
    ///     and refuses the file inside it, so a move judged by the named paths alone relocates
    ///     content the operator's policy excludes.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveDirectoryTool_Move_SourceHoldingAnEntryThePolicyWithholds_ReturnsDenialAndLeavesTheTree()
    {
        // Arrange: a permitted directory holding a file the policy's denied pattern rejects
        using var fixture = new TempDirectoryFixture();
        var drafts = Path.Combine(fixture.Root, "drafts");
        var ordinary = TempDirectoryFixture.WriteFile(drafts, "note.txt", "content");
        var withheld = TempDirectoryFixture.WriteFile(drafts, "secret.key", "key-content");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadWrite(fixture.Root, ["*.key"])]);
        var tool = FileMoveDirectoryTool.Create(policy);

        // Act: move the directory, which the write decision permits for both named paths
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "drafts", ["destination"] = "archive" });

        // Assert: refused naming the withheld entry as the model spelled it, nothing relocated,
        // and no host path disclosed
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.Contains("drafts/secret.key", text, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Root, text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "archive")));
        Assert.True(System.IO.File.Exists(ordinary));
        Assert.True(System.IO.File.Exists(withheld));
    }

    /// <summary>
    ///     Proves a move that would land an entry where the policy permits no writing is
    ///     refused, and the tree stays where it is.
    /// </summary>
    /// <remarks>
    ///     The complement of the scenario above, and the half a source-only check would leave
    ///     open. The policy grants the workspace without exclusions and a second location with
    ///     one, so the file is perfectly writable where it stands and refused where it would go —
    ///     a location the policy was never asked about, because no request ever named it. The
    ///     pair is written for the same reason the link pair is.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveDirectoryTool_Move_DestinationWouldHoldAnEntryThePolicyWithholds_ReturnsDenialAndLeavesTheTree()
    {
        // Arrange: a workspace grant with no exclusions, and a second granted location that
        // excludes key material, so only the landing place refuses the file
        using var fixture = new TempDirectoryFixture();
        var drafts = Path.Combine(fixture.Root, "drafts");
        var withheld = TempDirectoryFixture.WriteFile(drafts, "secret.key", "key-content");
        var vault = Path.Combine(fixture.Outside, "vault");
        Directory.CreateDirectory(vault);
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadWrite(fixture.Root), PathRule.ReadWrite(vault, ["*.key"])]);
        var tool = FileMoveDirectoryTool.Create(policy);

        // Act: move the directory into the second granted location, which both write decisions
        // permit for the paths the request names
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments
            {
                ["source"] = "drafts",
                ["destination"] = "../outside/vault/drafts"
            });

        // Assert: refused naming where the entry would have landed, in the model's own
        // spelling, with nothing placed there and the tree still at its source
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.Contains("../outside/vault/drafts/secret.key", text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(vault, "drafts")));
        Assert.True(System.IO.File.Exists(withheld));
        Assert.Equal(
            "key-content",
            await System.IO.File.ReadAllTextAsync(withheld, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void FileMoveDirectoryTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("file_move_directory", FileMoveDirectoryTool.ToolName);
        Assert.StartsWith(
            FilePack.FamilyPrefix + "_", FileMoveDirectoryTool.ToolName, StringComparison.Ordinal);

        ToolName.Validate(FileMoveDirectoryTool.ToolName);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void FileMoveDirectoryTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FileMoveDirectoryTool.Create(null!));
    }

    /// <summary>
    ///     Proves a permitted directory is moved with everything beneath it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveDirectoryTool_Move_PermittedTree_MovesItWithEverythingBeneathIt()
    {
        // Arrange: a tree holding a nested file, and an existing parent to move it under
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(
            Path.Combine(fixture.Root, "drafts", "nested"), "deep.txt", "content");
        Directory.CreateDirectory(Path.Combine(fixture.Root, "archive"));
        var tool = FileMoveDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: move the whole tree
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "drafts", ["destination"] = "archive/drafts" });

        // Assert: the source is gone and the nested content arrived intact
        Assert.IsType<string>(result);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "drafts")));
        Assert.Equal(
            "content",
            await System.IO.File.ReadAllTextAsync(
                Path.Combine(fixture.Root, "archive", "drafts", "nested", "deep.txt"),
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a destination in the same parent renames the directory, and that the tool's
    ///     description says so.
    /// </summary>
    /// <remarks>
    ///     The description assertion is part of the scenario rather than a separate one, because a
    ///     rename a model cannot discover is a capability the library does not really offer.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveDirectoryTool_Move_DestinationInTheSameParent_RenamesTheDirectory()
    {
        // Arrange: a tree to rename in place
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(Path.Combine(fixture.Root, "drafts"), "note.txt", "content");
        var tool = FileMoveDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: move it to a name beside itself
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "drafts", ["destination"] = "final" });

        // Assert: the old name is gone, the new one holds the content, and the description names
        // the rename so a model can find the capability
        Assert.IsType<string>(result);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "drafts")));
        Assert.True(System.IO.File.Exists(Path.Combine(fixture.Root, "final", "note.txt")));
        Assert.Contains("renames the directory", tool.Description, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an existing destination is refused outright, leaving both trees in place.
    /// </summary>
    /// <remarks>
    ///     There is no overwrite flag to test against: replacing a directory would destroy
    ///     everything beneath it, so the refusal is unconditional.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveDirectoryTool_Move_ExistingDestination_ReturnsDenialAndLeavesBoth()
    {
        // Arrange: two trees, each holding a file that would be lost if either were replaced
        using var fixture = new TempDirectoryFixture();
        var sourceFile = TempDirectoryFixture.WriteFile(
            Path.Combine(fixture.Root, "drafts"), "source.txt", "source-content");
        var destinationFile = TempDirectoryFixture.WriteFile(
            Path.Combine(fixture.Root, "final"), "destination.txt", "destination-content");
        var tool = FileMoveDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: attempt to move onto the existing destination
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "drafts", ["destination"] = "final" });

        // Assert: refused, and both trees still hold their own content
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.True(System.IO.File.Exists(sourceFile));
        Assert.Equal(
            "destination-content",
            await System.IO.File.ReadAllTextAsync(
                destinationFile, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a destination inside the directory being moved is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveDirectoryTool_Move_DestinationInsideTheSource_ReturnsDenialAndLeavesTheTree()
    {
        // Arrange: a tree, and a destination nested one level inside it
        using var fixture = new TempDirectoryFixture();
        var inside = TempDirectoryFixture.WriteFile(
            Path.Combine(fixture.Root, "drafts"), "note.txt", "content");
        var tool = FileMoveDirectoryTool.Create(RootedPolicy(fixture.Root));

        // Act: attempt to move the tree into itself
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "drafts", ["destination"] = "drafts/inner" });

        // Assert: refused before any file-system call, and the tree is untouched
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.True(System.IO.File.Exists(inside));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "drafts", "inner")));
    }

    /// <summary>
    ///     Proves a read-only source cannot be moved out of its location.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveDirectoryTool_Move_ReadOnlySource_ReturnsDenialAndLeavesItInPlace()
    {
        // Arrange: a readable but unwritable root holding the tree
        using var fixture = new TempDirectoryFixture();
        var inside = TempDirectoryFixture.WriteFile(
            Path.Combine(fixture.Root, "drafts"), "note.txt", "content");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        var tool = FileMoveDirectoryTool.Create(policy);

        // Act: attempt to move it away
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "drafts", ["destination"] = "final" });

        // Assert: refused, and the tree remains where its operator put it
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.True(System.IO.File.Exists(inside));
    }

    /// <summary>
    ///     Proves a read-only destination cannot receive a directory.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveDirectoryTool_Move_ReadOnlyDestination_ReturnsDenialAndLeavesItInPlace()
    {
        // Arrange: a writable workspace and a readable-but-unwritable second location
        using var fixture = new TempDirectoryFixture();
        var inside = TempDirectoryFixture.WriteFile(
            Path.Combine(fixture.Root, "drafts"), "note.txt", "content");
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadWrite(fixture.Root), PathRule.ReadOnly(fixture.Outside)]);
        var tool = FileMoveDirectoryTool.Create(policy);

        // Act: attempt to move into the location that may only be read
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments
            {
                ["source"] = "drafts",
                ["destination"] = Path.Combine(fixture.Outside, "drafts")
            });

        // Assert: refused, and the source is still in place
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.True(System.IO.File.Exists(inside));
        Assert.False(Directory.Exists(Path.Combine(fixture.Outside, "drafts")));
    }

    /// <summary>
    ///     Proves a missing source is reported with a plain fact that names no other tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveDirectoryTool_Move_MissingSource_ReturnsDenialNamingNoTool()
    {
        using var fixture = new TempDirectoryFixture();
        var tool = FileMoveDirectoryTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "absent", ["destination"] = "final" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(FileListTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a source naming a file is refused and the file is left alone.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveDirectoryTool_Move_SourceIsAFile_ReturnsDenialAndLeavesTheFile()
    {
        using var fixture = new TempDirectoryFixture();
        var file = TempDirectoryFixture.WriteFile(fixture.Root, "notes.txt", "content");
        var tool = FileMoveDirectoryTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "notes.txt", ["destination"] = "moved.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.True(System.IO.File.Exists(file));
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
