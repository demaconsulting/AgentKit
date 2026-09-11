using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFileReadTool"/> class.
/// </summary>
/// <remarks>
///     Every scenario invokes the constructed tool exactly as a runtime would — through
///     <see cref="AIFunction.InvokeAsync"/> with named arguments — rather than calling an
///     internal method directly, because the delivery of the result through the guarded factory
///     is part of what is under verification.
/// </remarks>
public class TextFileReadToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void TextFileReadTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        // Arrange / Act: read the published constant
        var name = TextFileReadTool.ToolName;

        // Assert: the name is qualified by the family prefix the pack claims
        Assert.Equal("text_file_read", name);
        Assert.StartsWith(TextFilePack.FamilyPrefix + "_", name, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the constructed tool carries the published name and a non-empty description.
    /// </summary>
    [Fact]
    public void TextFileReadTool_Create_ConstructedTool_CarriesTheToolNameAndADescription()
    {
        // Arrange: a policy governing an otherwise irrelevant location
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());

        // Act: construct the tool
        var tool = TextFileReadTool.Create(policy);

        // Assert: the model sees the published name and a description it can choose by
        Assert.Equal(TextFileReadTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void TextFileReadTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        // Arrange / Act / Assert: a tool with no policy cannot be constructed
        Assert.Throws<ArgumentNullException>(() => TextFileReadTool.Create(null!));
    }

    /// <summary>
    ///     Proves the result reaches the caller as plain text rather than as serialized JSON.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_PermittedFile_ResultIsPlainTextNotJsonElement()
    {
        // Arrange: a permitted file with known content
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content");
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: invoke the tool as the runtime would
        var result = await InvokeAsync(tool, file);

        // Assert: the guard delivered the result unserialized
        Assert.IsType<string>(result);
        Assert.IsNotType<JsonElement>(result);
    }

    /// <summary>
    ///     Proves a permitted file's content is what the tool returns.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_PermittedFile_ReturnsTheFileContents()
    {
        // Arrange: a permitted file whose content identifies it unambiguously
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "the-real-content");
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: read the file
        var result = await InvokeAsync(tool, file);

        // Assert: the content of the real file, not a plausible-looking substitute
        Assert.Equal("the-real-content", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves an empty file reads as empty text rather than as a refusal.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_EmptyFile_ReturnsEmptyTextNotDenial()
    {
        // Arrange: a permitted but empty file
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteFile(fixture.Root, "empty.txt", string.Empty);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: read the empty file
        var result = await InvokeAsync(tool, file);

        // Assert: an empty file is a legitimate outcome, so reporting a refusal would be a lie
        Assert.Equal(string.Empty, Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a path outside the permitted read location is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_PathOutsideTheReadRoot_ReturnsDenial()
    {
        // Arrange: a file in a sibling directory the read rule does not permit
        using var fixture = new ReparsePointFixture();
        var outsideFile = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "secret");
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: request the file outside the permitted location
        var result = await InvokeAsync(tool, outsideFile);

        // Assert: refused, and the content never reaches the model
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file reached through a link that leaves the permitted location is refused.
    /// </summary>
    /// <remarks>
    ///     The escaped file is first read directly through the link to prove the link really
    ///     bridges the two directories; without that step a broken fixture would make this
    ///     scenario pass vacuously.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_FileBeneathLinkOutsideRoot_ReturnsDenial()
    {
        // Arrange: a real reparse point inside the permitted root pointing outside it
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "escaped-content");
        var link = fixture.CreateDirectoryLink("escape", fixture.Outside);
        var escapedPath = Path.Combine(link, "secret.txt");
        Assert.Equal("escaped-content", await File.ReadAllTextAsync(
            escapedPath,
            TestContext.Current.CancellationToken));
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: request the escaped file through a path that looks contained
        var result = await InvokeAsync(tool, escapedPath);

        // Assert: refused on its real location, not on how the path was spelled
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("escaped-content", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing file is refused with a redirect to the listing tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_MissingFile_ReturnsDenialRedirectingToList()
    {
        // Arrange: a permitted location containing no such file
        using var fixture = new ReparsePointFixture();
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: request a file that does not exist
        var result = await InvokeAsync(tool, Path.Combine(fixture.Root, "absent.txt"));

        // Assert: refused, and pointed at the tool that would have found the right name
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.Contains(TextFileListTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a directory is refused with a redirect to the listing tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_DirectoryPath_ReturnsDenialRedirectingToList()
    {
        // Arrange: a permitted directory rather than a file
        using var fixture = new ReparsePointFixture();
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: request the directory itself
        var result = await InvokeAsync(tool, fixture.Root);

        // Assert: refused as malformed, and redirected to the tool that lists a directory
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains(TextFileListTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file beyond the read ceiling is refused with the ceiling named.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_FileLargerThanTheReadCeiling_ReturnsDenialNamingTheCeiling()
    {
        // Arrange: a file larger than an eight-byte read ceiling
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteFile(fixture.Root, "big.txt", new string('a', 64));
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root, new ToolLimits(maxReadBytes: 8)));

        // Act: read the oversized file
        var result = await InvokeAsync(tool, file);

        // Assert: a refusal naming the ceiling, never a truncated file
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("8-byte", text, StringComparison.Ordinal);
        Assert.DoesNotContain("aaaa", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file exactly at the read ceiling is read rather than refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_FileAtTheReadCeiling_IsRead()
    {
        // Arrange: a five-byte file and a five-byte read ceiling
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteFile(fixture.Root, "exact.txt", "12345");
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root, new ToolLimits(maxReadBytes: 5)));

        // Act: read the file sitting exactly on the boundary
        var result = await InvokeAsync(tool, file);

        // Assert: the ceiling is inclusive, so the file is returned
        Assert.Equal("12345", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves text beyond the result ceiling is refused rather than truncated.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_TextBeyondTheResultCeiling_ReturnsDenialRatherThanTruncatedText()
    {
        // Arrange: a file within the read ceiling but beyond the tighter result ceiling
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteFile(fixture.Root, "wide.txt", new string('b', 50));
        var limits = new ToolLimits(maxReadBytes: 1024, maxResultCharacters: 10);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root, limits));

        // Act: read the file
        var result = await InvokeAsync(tool, file);

        // Assert: a refusal naming the result ceiling, with no partial content returned
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("10-character", text, StringComparison.Ordinal);
        Assert.DoesNotContain("bbbb", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an empty path is refused rather than throwing at the model.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_EmptyPath_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a tool governed by an unrestricted policy, so only the request is at fault
        var tool = TextFileReadTool.Create(
            new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted()));

        // Act: invoke with an empty path, as a confused model would
        var result = await InvokeAsync(tool, string.Empty);

        // Assert: a returned refusal, because an exception would end the agent's turn
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a whitespace-only path is refused rather than throwing at the model.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_WhitespacePath_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a tool governed by an unrestricted policy
        var tool = TextFileReadTool.Create(
            new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted()));

        // Act: invoke with a path consisting only of whitespace
        var result = await InvokeAsync(tool, "   ");

        // Assert: a returned refusal rather than an exception from the policy
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a refusal discloses no host location.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_DeniedPath_DenialTextContainsNoHostDetail()
    {
        // Arrange: a file outside the permitted location, so the refusal has something to leak
        using var fixture = new ReparsePointFixture();
        var outsideFile = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "secret");
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: request the refused file
        var result = await InvokeAsync(tool, outsideFile);

        // Assert: neither the requested path, the permitted location nor a separator appears
        var text = Assert.IsType<string>(result);
        Assert.DoesNotContain(fixture.Root, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.Outside, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret.txt", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar.ToString(),
            text,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates a policy permitting reads and writes only beneath one location.
    /// </summary>
    /// <param name="root">The permitted location.</param>
    /// <param name="limits">The ceilings to apply, or null for the published defaults.</param>
    /// <returns>The constructed policy.</returns>
    private static PathPolicy RootedPolicy(string root, ToolLimits? limits = null)
    {
        return new PathPolicy(
            PathRule.Rooted(root),
            PathRule.Rooted(root),
            limits ?? ToolLimits.Default);
    }

    /// <summary>
    ///     Invokes the read tool exactly as a runtime would.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="path">The path argument to supply.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeAsync(AIFunction tool, string path)
    {
        return await tool.InvokeAsync(
            new AIFunctionArguments { ["path"] = path },
            TestContext.Current.CancellationToken);
    }
}

