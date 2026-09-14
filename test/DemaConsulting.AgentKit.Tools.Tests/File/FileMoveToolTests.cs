using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.File;

/// <summary>
///     Unit tests for the <see cref="FileMoveTool"/> class.
/// </summary>
public class FileMoveToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void FileMoveTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("file_move", FileMoveTool.ToolName);
        Assert.StartsWith(FilePack.FamilyPrefix + "_", FileMoveTool.ToolName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void FileMoveTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FileMoveTool.Create(null!));
    }

    /// <summary>
    ///     Proves a permitted move relocates the file and removes the source.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveTool_Move_RelativePaths_MovesTheFileAndRemovesTheSource()
    {
        // Arrange: a workspace with a source file
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "notes.txt", "content");
        var tool = FileMoveTool.Create(RootedPolicy(fixture.Root));

        // Act: move using bare relative names
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "notes.txt", ["destination"] = "moved.txt" });

        // Assert: the destination holds the content and the source is gone
        Assert.IsType<string>(result);
        Assert.Equal("content", await ReadAsync(Path.Combine(fixture.Root, "moved.txt")));
        Assert.False(System.IO.File.Exists(Path.Combine(fixture.Root, "notes.txt")));
    }

    /// <summary>
    ///     Proves a move refuses to overwrite an existing destination unless overwrite is true.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveTool_Move_ExistingDestinationWithoutOverwrite_ReturnsDenialAndLeavesBoth()
    {
        // Arrange: a source and an existing destination
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "notes.txt", "source");
        TempDirectoryFixture.WriteFile(fixture.Root, "existing.txt", "kept");
        var tool = FileMoveTool.Create(RootedPolicy(fixture.Root));

        // Act: move over the existing destination without permitting the overwrite
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "notes.txt", ["destination"] = "existing.txt" });

        // Assert: refused; both files remain unchanged
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Equal("kept", await ReadAsync(Path.Combine(fixture.Root, "existing.txt")));
        Assert.True(System.IO.File.Exists(Path.Combine(fixture.Root, "notes.txt")));
    }

    /// <summary>
    ///     Proves a move of a read-only source is refused: a move needs write permission on the
    ///     source it removes.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveTool_Move_ReadOnlySource_ReturnsDenialAndLeavesItInPlace()
    {
        // Arrange: reads permitted beneath the root, writes permitted only in a sibling location
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "notes.txt", "content");
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadOnly(fixture.Root), PathRule.ReadWrite(fixture.Outside)]);
        var tool = FileMoveTool.Create(policy);

        // Act: attempt to move the read-only source out to the writable location
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments
            {
                ["source"] = Path.Combine(fixture.Root, "notes.txt"),
                ["destination"] = Path.Combine(fixture.Outside, "moved.txt")
            });

        // Assert: the source write is refused and the original stays in place
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.True(System.IO.File.Exists(Path.Combine(fixture.Root, "notes.txt")));
    }

    /// <summary>
    ///     Proves a missing source is refused with a plain fact that names no other tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileMoveTool_Move_MissingSource_ReturnsDenialNamingNoTool()
    {
        using var fixture = new TempDirectoryFixture();
        var tool = FileMoveTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "absent.txt", ["destination"] = "moved.txt" });

        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(FileListTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
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
    ///     Reads a file's text with the test's cancellation token.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The file's text.</returns>
    private static async Task<string> ReadAsync(string path)
    {
        return await System.IO.File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
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
