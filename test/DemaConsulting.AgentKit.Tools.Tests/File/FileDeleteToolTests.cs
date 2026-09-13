using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.File;

/// <summary>
///     Unit tests for the <see cref="FileDeleteTool"/> class.
/// </summary>
public class FileDeleteToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims, and is a
    ///     valid name even though the bare verb 'delete' is reserved.
    /// </summary>
    [Fact]
    public void FileDeleteTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("file_delete", FileDeleteTool.ToolName);
        Assert.StartsWith(FilePack.FamilyPrefix + "_", FileDeleteTool.ToolName, StringComparison.Ordinal);

        // The prefixed name is accepted even though a bare 'delete' is reserved.
        ToolName.Validate(FileDeleteTool.ToolName);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void FileDeleteTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FileDeleteTool.Create(null!));
    }

    /// <summary>
    ///     Proves a permitted single file is deleted.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteTool_Delete_PermittedFile_RemovesIt()
    {
        // Arrange: a workspace holding a file, addressed by its bare relative name
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "content");
        var tool = FileDeleteTool.Create(RootedPolicy(fixture.Root));

        // Act: delete it
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "notes.txt" });

        // Assert: the file is gone
        Assert.IsType<string>(result);
        Assert.False(System.IO.File.Exists(Path.Combine(fixture.Root, "notes.txt")));
    }

    /// <summary>
    ///     Proves a directory is refused and left in place: the tool never deletes a directory or
    ///     recurses.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteTool_Delete_DirectoryPath_ReturnsDenialAndLeavesItInPlace()
    {
        // Arrange: a directory holding a file, so a recursive delete would destroy the file too
        using var fixture = new ReparsePointFixture();
        var subdirectory = Path.Combine(fixture.Root, "keep");
        Directory.CreateDirectory(subdirectory);
        ReparsePointFixture.WriteFile(subdirectory, "inside.txt", "content");
        var tool = FileDeleteTool.Create(RootedPolicy(fixture.Root));

        // Act: attempt to delete the directory
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "keep" });

        // Assert: refused, and both the directory and its file remain
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.True(Directory.Exists(subdirectory));
        Assert.True(System.IO.File.Exists(Path.Combine(subdirectory, "inside.txt")));
    }

    /// <summary>
    ///     Proves a deletion outside the write grant is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteTool_Delete_ReadOnlyLocation_ReturnsDenialAndLeavesTheFile()
    {
        // Arrange: a read-only root, so no deletion is permitted
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "content");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        var tool = FileDeleteTool.Create(policy);

        // Act: attempt to delete within the read-only location
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "notes.txt" });

        // Assert: refused, and the file remains
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.True(System.IO.File.Exists(Path.Combine(fixture.Root, "notes.txt")));
    }

    /// <summary>
    ///     Proves a missing file is reported with a plain fact that names no other tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileDeleteTool_Delete_MissingFile_ReturnsDenialNamingNoTool()
    {
        using var fixture = new ReparsePointFixture();
        var tool = FileDeleteTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(tool, new AIFunctionArguments { ["path"] = "absent.txt" });

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
