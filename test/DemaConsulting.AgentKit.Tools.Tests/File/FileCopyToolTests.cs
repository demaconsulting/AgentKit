using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.File;

/// <summary>
///     Unit tests for the <see cref="FileCopyTool"/> class.
/// </summary>
public class FileCopyToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void FileCopyTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("file_copy", FileCopyTool.ToolName);
        Assert.StartsWith(FilePack.FamilyPrefix + "_", FileCopyTool.ToolName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void FileCopyTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FileCopyTool.Create(null!));
    }

    /// <summary>
    ///     Proves a permitted copy addressed by bare relative names duplicates the file's content.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileCopyTool_Copy_RelativePaths_CopiesTheFileContent()
    {
        // Arrange: a workspace with a source file, addressed the way a model addresses it
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "notes.txt", "original");
        var tool = FileCopyTool.Create(RootedPolicy(fixture.Root));

        // Act: copy using bare relative names
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "notes.txt", ["destination"] = "copy.txt" });

        // Assert: the copy exists with the original content, and the original remains
        Assert.IsType<string>(result);
        Assert.Equal("original", await ReadAsync(Path.Combine(fixture.Root, "copy.txt")));
        Assert.True(System.IO.File.Exists(Path.Combine(fixture.Root, "notes.txt")));
    }

    /// <summary>
    ///     Proves a copy refuses to overwrite an existing destination unless overwrite is true.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileCopyTool_Copy_ExistingDestinationWithoutOverwrite_ReturnsDenialAndLeavesItUnchanged()
    {
        // Arrange: a source and an existing destination
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "notes.txt", "source");
        TempDirectoryFixture.WriteFile(fixture.Root, "existing.txt", "kept");
        var tool = FileCopyTool.Create(RootedPolicy(fixture.Root));

        // Act: copy over the existing destination without permitting the overwrite
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "notes.txt", ["destination"] = "existing.txt" });

        // Assert: refused, and the destination is untouched
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Equal("kept", await ReadAsync(Path.Combine(fixture.Root, "existing.txt")));
    }

    /// <summary>
    ///     Proves a copy overwrites an existing destination when overwrite is explicitly true.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileCopyTool_Copy_ExistingDestinationWithOverwrite_ReplacesIt()
    {
        // Arrange: a source and an existing destination
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "notes.txt", "source");
        TempDirectoryFixture.WriteFile(fixture.Root, "existing.txt", "old");
        var tool = FileCopyTool.Create(RootedPolicy(fixture.Root));

        // Act: copy with overwrite permitted
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments
            {
                ["source"] = "notes.txt",
                ["destination"] = "existing.txt",
                ["overwrite"] = true
            });

        // Assert: the destination now holds the source content
        Assert.IsType<string>(result);
        Assert.Equal("source", await ReadAsync(Path.Combine(fixture.Root, "existing.txt")));
    }

    /// <summary>
    ///     Proves a source a read grant permits but a write grant does not can still be copied into
    ///     a writable location — the two decisions are judged per endpoint.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileCopyTool_Copy_ReadOnlySourceToWritableDestination_IsPermitted()
    {
        // Arrange: reads permitted beneath the root, writes permitted only in a sibling location
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "notes.txt", "content");
        var destination = Path.Combine(fixture.Outside, "copy.txt");
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadOnly(fixture.Root), PathRule.ReadWrite(fixture.Outside)]);
        var tool = FileCopyTool.Create(policy);

        // Act: copy the read-only source into the writable location, both by absolute path
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments
            {
                ["source"] = Path.Combine(fixture.Root, "notes.txt"),
                ["destination"] = destination
            });

        // Assert: the copy succeeds
        Assert.IsType<string>(result);
        Assert.Equal("content", await ReadAsync(destination));
    }

    /// <summary>
    ///     Proves a copy whose destination a write grant does not cover is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileCopyTool_Copy_DestinationOutsideWriteGrant_ReturnsDenial()
    {
        // Arrange: a read-only root, so no write is permitted anywhere within it
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "notes.txt", "content");
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]);
        var tool = FileCopyTool.Create(policy);

        // Act: attempt to copy within the read-only location
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "notes.txt", ["destination"] = "copy.txt" });

        // Assert: the destination write is refused
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing source is refused with a plain fact that names no other tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileCopyTool_Copy_MissingSource_ReturnsDenialNamingNoTool()
    {
        using var fixture = new TempDirectoryFixture();
        var tool = FileCopyTool.Create(RootedPolicy(fixture.Root));

        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["source"] = "absent.txt", ["destination"] = "copy.txt" });

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
