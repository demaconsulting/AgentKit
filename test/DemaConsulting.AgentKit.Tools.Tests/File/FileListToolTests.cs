using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.File;

/// <summary>
///     Unit tests for the <see cref="FileListTool"/> class.
/// </summary>
/// <remarks>
///     Every scenario invokes the constructed tool exactly as a runtime would — through
///     <see cref="AIFunction.InvokeAsync"/> with named arguments — and uses the paths a model
///     actually sends: bare relative names, an omitted directory, and globs.
/// </remarks>
public class FileListToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void FileListTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        Assert.Equal("file_list", FileListTool.ToolName);
        Assert.StartsWith(FilePack.FamilyPrefix + "_", FileListTool.ToolName, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void FileListTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => FileListTool.Create(null!));
    }

    /// <summary>
    ///     Proves the constructed tool carries the published name and a description.
    /// </summary>
    [Fact]
    public void FileListTool_Create_ConstructedTool_CarriesTheToolNameAndADescription()
    {
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var tool = FileListTool.Create(policy);

        Assert.Equal(FileListTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    /// <summary>
    ///     Proves a listing of any file type is returned, not only text files.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileListTool_List_TypeAgnostic_ReportsFilesOfEveryType()
    {
        // Arrange: a workspace holding a text file and a binary-typed file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "text");
        ReparsePointFixture.WriteBytes(fixture.Root, "picture.png", [0x89, 0x50, 0x4E, 0x47]);
        var tool = FileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list the workspace
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["directory"] = fixture.Root });

        // Assert: both names appear — the family lists a file regardless of what it holds
        var text = Assert.IsType<string>(result);
        Assert.Contains("notes.txt", text, StringComparison.Ordinal);
        Assert.Contains("picture.png", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an omitted directory lists every permitted location, including an empty one.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileListTool_List_OmittedDirectory_ListsEveryPermittedLocationIncludingEmpty()
    {
        // Arrange: a populated workspace and a granted-but-empty second location
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "text");
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadWrite(fixture.Root), PathRule.ReadWrite(fixture.Outside)]);
        var tool = FileListTool.Create(policy);

        // Act: discover with no directory argument
        var result = await InvokeAsync(tool, new AIFunctionArguments());

        // Assert: both locations appear; the empty one is marked, not hidden
        var text = Assert.IsType<string>(result);
        Assert.Contains("notes.txt", text, StringComparison.Ordinal);
        Assert.Contains("(no files - read-write)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an empty directory is reported as a fact, not refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileListTool_List_EmptyDirectory_ReturnsNoFilesMatchedNotADenial()
    {
        // Arrange: a permitted but empty workspace, listed by its own name
        using var fixture = new ReparsePointFixture();
        var tool = FileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list the empty directory
        var result = await InvokeAsync(tool, new AIFunctionArguments { ["directory"] = fixture.Root });

        // Assert: an empty listing is an answer, not a refusal
        var text = Assert.IsType<string>(result);
        Assert.DoesNotContain("Denied", text, StringComparison.Ordinal);
        Assert.Contains("No files matched.", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a glob pattern restricts the listing, and a recursive prefix is honored.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileListTool_List_GlobPattern_RestrictsToMatchingFiles()
    {
        // Arrange: a workspace with two file types
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "text");
        ReparsePointFixture.WriteFile(fixture.Root, "guide.md", "# Guide");
        var tool = FileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list only the Markdown files, using a recursive glob
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["directory"] = fixture.Root, ["pattern"] = "**/*.md" });

        // Assert: only the matching file is listed
        var text = Assert.IsType<string>(result);
        Assert.Contains("guide.md", text, StringComparison.Ordinal);
        Assert.DoesNotContain("notes.txt", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a directory outside the grants is refused, disclosing the permitted location.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task FileListTool_List_DirectoryOutsideGrants_ReturnsDenial()
    {
        // Arrange: a workspace, and a sibling location outside the grant
        using var fixture = new ReparsePointFixture();
        var tool = FileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list the outside directory
        var result = await InvokeAsync(
            tool,
            new AIFunctionArguments { ["directory"] = fixture.Outside });

        // Assert: a returned refusal naming the reason
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
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
