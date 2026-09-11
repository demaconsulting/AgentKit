using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFileWriteTool"/> class.
/// </summary>
/// <remarks>
///     Every scenario invokes the constructed tool through <see cref="AIFunction.InvokeAsync"/>
///     rather than calling an internal method directly, because delivery of the result through
///     the guarded factory is part of what is under verification. The scenario proving a
///     readable path is not thereby writable is the one that makes the independent read and
///     write rules observable at the tool level.
/// </remarks>
public class TextFileWriteToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void TextFileWriteTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        // Arrange / Act: read the published constant
        var name = TextFileWriteTool.ToolName;

        // Assert: the name is qualified by the family prefix the pack claims
        Assert.Equal("text_file_write", name);
        Assert.StartsWith(TextFilePack.FamilyPrefix + "_", name, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the constructed tool carries the published name and a non-empty description.
    /// </summary>
    [Fact]
    public void TextFileWriteTool_Create_ConstructedTool_CarriesTheToolNameAndADescription()
    {
        // Arrange: a policy governing an otherwise irrelevant location
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());

        // Act: construct the tool
        var tool = TextFileWriteTool.Create(policy);

        // Assert: the model sees the published name and a description it can choose by
        Assert.Equal(TextFileWriteTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void TextFileWriteTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        // Arrange / Act / Assert: a tool with no policy cannot be constructed
        Assert.Throws<ArgumentNullException>(() => TextFileWriteTool.Create(null!));
    }

    /// <summary>
    ///     Proves the confirmation reaches the caller as plain text rather than serialized JSON.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_PermittedPath_ResultIsPlainTextNotJsonElement()
    {
        // Arrange: a permitted destination inside the write location
        using var fixture = new ReparsePointFixture();
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root));

        // Act: write the file
        var result = await InvokeAsync(tool, Path.Combine(fixture.Root, "note.txt"), "content");

        // Assert: the guard delivered the result unserialized
        Assert.IsType<string>(result);
        Assert.IsNotType<JsonElement>(result);
    }

    /// <summary>
    ///     Proves a permitted write puts the content on disk and confirms it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_PermittedPath_WritesTheContentAndConfirms()
    {
        // Arrange: a permitted destination
        using var fixture = new ReparsePointFixture();
        var target = Path.Combine(fixture.Root, "note.txt");
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root));

        // Act: write known content
        var result = await InvokeAsync(tool, target, "written-content");

        // Assert: the file holds the content, and the confirmation names a count, not a location
        var text = Assert.IsType<string>(result);
        Assert.Equal("Wrote 15 characters.", text);
        Assert.Equal(
            "written-content",
            await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves empty content writes an empty file rather than being refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_EmptyContent_WritesAnEmptyFile()
    {
        // Arrange: a permitted destination
        using var fixture = new ReparsePointFixture();
        var target = Path.Combine(fixture.Root, "empty.txt");
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root));

        // Act: write an empty string, which is a real outcome rather than a mistake
        var result = await InvokeAsync(tool, target, string.Empty);

        // Assert: an empty file exists and the write was confirmed
        Assert.Equal("Wrote 0 characters.", Assert.IsType<string>(result));
        Assert.True(File.Exists(target));
        Assert.Equal(
            string.Empty,
            await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a path the read rule permits is refused for writing when the write rule does
    ///     not permit it.
    /// </summary>
    /// <remarks>
    ///     This is the scenario that makes the independent read and write rules observable. The
    ///     file is first read successfully through the read tool, so the refusal cannot be
    ///     explained away as the path being unreachable.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_ReadableButNotWritablePath_ReturnsDenial()
    {
        // Arrange: reads permitted beneath the root, writes permitted only outside it
        using var fixture = new ReparsePointFixture();
        var target = ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "original");
        var policy = new PathPolicy(
            PathRule.Rooted(fixture.Root),
            PathRule.Rooted(fixture.Outside));
        var readTool = TextFileReadTool.Create(policy);
        var writeTool = TextFileWriteTool.Create(policy);
        var readResult = await readTool.InvokeAsync(
            new AIFunctionArguments { ["path"] = target },
            TestContext.Current.CancellationToken);
        Assert.Equal("original", Assert.IsType<string>(readResult));

        // Act: attempt to write the path that was just read successfully
        var result = await InvokeAsync(writeTool, target, "replacement");

        // Assert: refused, and the file on disk is untouched
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.Equal(
            "original",
            await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a path outside the permitted write location is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_PathOutsideTheWriteRoot_ReturnsDenial()
    {
        // Arrange: a destination in a sibling directory the write rule does not permit
        using var fixture = new ReparsePointFixture();
        var target = Path.Combine(fixture.Outside, "intruder.txt");
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root));

        // Act: attempt the write
        var result = await InvokeAsync(tool, target, "content");

        // Assert: refused, and nothing was created outside the permitted location
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.False(File.Exists(target));
    }

    /// <summary>
    ///     Proves a destination reached through a link that leaves the permitted location is
    ///     refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_PathBeneathLinkOutsideRoot_ReturnsDenial()
    {
        // Arrange: a real reparse point inside the permitted root pointing outside it
        using var fixture = new ReparsePointFixture();
        var link = fixture.CreateDirectoryLink("escape", fixture.Outside);
        var target = Path.Combine(link, "intruder.txt");
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root));

        // Act: attempt a write through a path that looks contained
        var result = await InvokeAsync(tool, target, "content");

        // Assert: refused on its real location, and no file appears outside the root
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(fixture.Outside, "intruder.txt")));
    }

    /// <summary>
    ///     Proves writing an existing file replaces its content rather than appending to it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_ExistingFile_ReplacesItsContent()
    {
        // Arrange: an existing permitted file with known content
        using var fixture = new ReparsePointFixture();
        var target = ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "original-content");
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root));

        // Act: write different content to the same path
        var result = await InvokeAsync(tool, target, "new");

        // Assert: replacement, not concatenation
        Assert.IsType<string>(result);
        Assert.Equal(
            "new",
            await File.ReadAllTextAsync(target, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a missing parent directory is refused and no directory is created.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_MissingParentDirectory_ReturnsDenialAndCreatesNothing()
    {
        // Arrange: a destination two levels below a directory that does not exist
        using var fixture = new ReparsePointFixture();
        var missingParent = Path.Combine(fixture.Root, "absent");
        var target = Path.Combine(missingParent, "note.txt");
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root));

        // Act: attempt the write
        var result = await InvokeAsync(tool, target, "content");

        // Assert: refused, and the tool materialized no part of the tree
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(missingParent));
        Assert.False(File.Exists(target));
    }

    /// <summary>
    ///     Proves a directory path is refused rather than written over.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_DirectoryPath_ReturnsDenial()
    {
        // Arrange: the permitted directory itself as the destination
        using var fixture = new ReparsePointFixture();
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root));

        // Act: attempt to write to the directory
        var result = await InvokeAsync(tool, fixture.Root, "content");

        // Assert: refused as malformed, and the directory still exists
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.Root));
    }

    /// <summary>
    ///     Proves an empty path is refused rather than throwing at the model.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_EmptyPath_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a tool governed by an unrestricted policy, so only the request is at fault
        var tool = TextFileWriteTool.Create(
            new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted()));

        // Act: invoke with an empty path, as a confused model would
        var result = await InvokeAsync(tool, string.Empty, "content");

        // Assert: a returned refusal, because an exception would end the agent's turn
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves absent content is refused rather than throwing at the model.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_NullContent_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a permitted destination and a request that omits its content
        using var fixture = new ReparsePointFixture();
        var target = Path.Combine(fixture.Root, "note.txt");
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root));

        // Act: invoke with no content supplied at all
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["path"] = target, ["content"] = null },
            TestContext.Current.CancellationToken);

        // Assert: a returned refusal, and no file created
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.False(File.Exists(target));
    }

    /// <summary>
    ///     Proves a refusal discloses no host location.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileWriteTool_Write_DeniedPath_DenialTextContainsNoHostDetail()
    {
        // Arrange: a destination outside the permitted write location
        using var fixture = new ReparsePointFixture();
        var target = Path.Combine(fixture.Outside, "intruder.txt");
        var tool = TextFileWriteTool.Create(RootedPolicy(fixture.Root));

        // Act: attempt the refused write
        var result = await InvokeAsync(tool, target, "content");

        // Assert: neither the requested path, the permitted location nor a separator appears
        var text = Assert.IsType<string>(result);
        Assert.DoesNotContain(fixture.Root, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.Outside, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("intruder.txt", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar.ToString(),
            text,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates a policy permitting reads and writes only beneath one location.
    /// </summary>
    /// <param name="root">The permitted location.</param>
    /// <returns>The constructed policy.</returns>
    private static PathPolicy RootedPolicy(string root)
    {
        return new PathPolicy(PathRule.Rooted(root), PathRule.Rooted(root));
    }

    /// <summary>
    ///     Invokes the write tool exactly as a runtime would.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="path">The path argument to supply.</param>
    /// <param name="content">The content argument to supply.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeAsync(AIFunction tool, string path, string content)
    {
        return await tool.InvokeAsync(
            new AIFunctionArguments { ["path"] = path, ["content"] = content },
            TestContext.Current.CancellationToken);
    }
}

