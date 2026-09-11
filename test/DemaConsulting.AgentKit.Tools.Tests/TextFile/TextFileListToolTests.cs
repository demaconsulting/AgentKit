using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFileListTool"/> class.
/// </summary>
/// <remarks>
///     <para>
///     A listing is grouped under an absolute location header, with the matching names given
///     beneath it relative to that header — an O(1) header rather than a full absolute path on
///     every file. When the working directory is granted and the request lies within it, the names
///     are bare working-directory-relative names the model can hand straight back; an absolute
///     request, or a location outside the working directory, is addressed by its absolute header.
///     </para>
///     <para>
///     The scenario proving an escaped file is not listed is a security control rather than a
///     convenience check: it fails loudly rather than skipping when the platform cannot create a
///     reparse point.
///     </para>
/// </remarks>
public class TextFileListToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void TextFileListTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        // Arrange / Act: read the published constant
        var name = TextFileListTool.ToolName;

        // Assert: the name is qualified by the family prefix the pack claims
        Assert.Equal("text_file_list", name);
        Assert.StartsWith(TextFilePack.FamilyPrefix + "_", name, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the constructed tool carries the published name and a non-empty description.
    /// </summary>
    [Fact]
    public void TextFileListTool_Create_ConstructedTool_CarriesTheToolNameAndADescription()
    {
        // Arrange: a policy governing an otherwise irrelevant location
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: construct the tool
        var tool = TextFileListTool.Create(policy);

        // Assert: the model sees the published name and a description it can choose by
        Assert.Equal(TextFileListTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void TextFileListTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        // Arrange / Act / Assert: a tool with no policy cannot be constructed
        Assert.Throws<ArgumentNullException>(() => TextFileListTool.Create(null!));
    }

    /// <summary>
    ///     Proves the listing reaches the caller as plain text rather than as serialized JSON.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_PermittedDirectory_ResultIsPlainTextNotJsonElement()
    {
        // Arrange: a permitted directory holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content");
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list the directory
        var result = await InvokeAsync(tool, null, null);

        // Assert: the guard delivered the result unserialized
        Assert.IsType<string>(result);
        Assert.IsNotType<JsonElement>(result);
    }

    /// <summary>
    ///     Proves a discovery listing of a single granted working directory reports bare relative
    ///     names beneath the absolute working-directory header.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_Discovery_ListsRelativeNamesUnderTheAnchorHeader()
    {
        // Arrange: two files in the granted working directory
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "one.txt", "1");
        ReparsePointFixture.WriteFile(fixture.Root, "two.txt", "2");
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: discover, the way a model with no directory name would
        var result = await InvokeAsync(tool, null, null);

        // Assert: the absolute header, then bare relative names beneath it
        Assert.Equal(Block(policy.WorkingDirectory, "one.txt", "two.txt"), Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a file reachable only by following a link out of the permitted location is not
    ///     listed.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_LinkToOutsideRoot_DoesNotListEscapedFile()
    {
        // Arrange: a permitted file, plus a real reparse point leading to a file outside
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "inside.txt", "inside");
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "escaped-content");
        var link = fixture.CreateDirectoryLink("escape", fixture.Outside);
        Assert.Equal("escaped-content", await File.ReadAllTextAsync(
            Path.Combine(link, "secret.txt"),
            TestContext.Current.CancellationToken));
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list the permitted root, which the operating system will walk through the link
        var result = await InvokeAsync(tool, null, null);

        // Assert: the escaped file is absent while the permitted one is present
        var text = Assert.IsType<string>(result);
        Assert.Contains("inside.txt", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.txt", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an absolute directory request lists names beneath that directory's absolute header
    ///     — the absolute dialect mirrors an absolute caller.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_AbsoluteDirectory_ListsUnderTheAbsoluteHeader()
    {
        // Arrange: a file one level below the requested directory
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(Path.Combine(fixture.Root, "sub"), "child.txt", "child");
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: list the root by its absolute path
        var result = await InvokeAsync(tool, policy.WorkingDirectory, null);

        // Assert: the absolute header, then the name relative to it
        Assert.Equal(Block(policy.WorkingDirectory, "sub/child.txt"), Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves the listing order is deterministic rather than file-system dependent.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_SeveralFiles_AreListedInDeterministicOrder()
    {
        // Arrange: files created in an order other than their sorted order
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "c.txt", "c");
        ReparsePointFixture.WriteFile(fixture.Root, "a.txt", "a");
        ReparsePointFixture.WriteFile(fixture.Root, "b.txt", "b");
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: list the directory
        var result = await InvokeAsync(tool, null, null);

        // Assert: ordinal order, so the same tree always produces the same listing
        Assert.Equal(
            Block(policy.WorkingDirectory, "a.txt", "b.txt", "c.txt"),
            Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a search pattern narrows the listing to the matching files.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_SearchPattern_LimitsTheListingToMatchingFiles()
    {
        // Arrange: files of two different extensions
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "text");
        ReparsePointFixture.WriteFile(fixture.Root, "data.json", "{}");
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: list only the text files
        var result = await InvokeAsync(tool, null, "*.txt");

        // Assert: the pattern is honored
        Assert.Equal(Block(policy.WorkingDirectory, "note.txt"), Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves an absent search pattern lists every permitted file.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_NoSearchPattern_ListsEveryPermittedFile()
    {
        // Arrange: files of two different extensions
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "text");
        ReparsePointFixture.WriteFile(fixture.Root, "data.json", "{}");
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: list with no pattern supplied at all
        var result = await InvokeAsync(tool, null, null);

        // Assert: "everything here" is the least surprising reading of an absent pattern
        Assert.Equal(
            Block(policy.WorkingDirectory, "data.json", "note.txt"),
            Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a directory matching nothing reports an empty listing rather than a refusal.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_NoMatches_ReturnsAnEmptyListingNotADenial()
    {
        // Arrange: a permitted directory holding nothing the pattern matches
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "text");
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list with a pattern nothing matches
        var result = await InvokeAsync(tool, null, "*.absent");

        // Assert: an answer, not a refusal — nothing about the request was wrong
        var text = Assert.IsType<string>(result);
        Assert.Equal("No files matched.", text);
        Assert.DoesNotContain("Denied", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a listing beyond the result ceiling is refused with the ceiling named.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_ListingBeyondTheResultCeiling_ReturnsDenialNamingTheCeiling()
    {
        // Arrange: a listing longer than a five-character result ceiling
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "first-file.txt", "1");
        ReparsePointFixture.WriteFile(fixture.Root, "second-file.txt", "2");
        var limits = new ToolLimits(maxResultCharacters: 5);
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root, limits));

        // Act: list the directory
        var result = await InvokeAsync(tool, null, null);

        // Assert: a refusal naming the ceiling, never a truncated listing
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("5-character", text, StringComparison.Ordinal);
        Assert.DoesNotContain("first-file.txt", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a directory outside every permitted location is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_DirectoryOutsideTheReadRoot_ReturnsDenial()
    {
        // Arrange: a sibling directory no grant permits
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "secret");
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list the directory outside the permitted location
        var result = await InvokeAsync(tool, fixture.Outside, null);

        // Assert: a refusal rather than an empty listing, so the model learns why
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.txt", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an omitted directory lists every permitted location rather than being refused.
    /// </summary>
    /// <param name="directory">The spelling of "no directory" the model supplied.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TextFileListTool_List_OmittedDirectory_ListsTheAnchor(string? directory)
    {
        // Arrange: a granted working directory holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content");
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: list with no directory named
        var result = await InvokeAsync(tool, directory, null);

        // Assert: the granted working directory, reported as an absolute header and a bare name
        Assert.Equal(Block(policy.WorkingDirectory, "note.txt"), Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves the literal words a model sends for "no directory" list every permitted location.
    /// </summary>
    /// <param name="directory">The placeholder spelling the model supplied.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("None")]
    [InlineData("null")]
    public async Task TextFileListTool_List_PlaceholderDirectory_ListsTheAnchor(string directory)
    {
        // Arrange: a granted working directory holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content");
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: list with the placeholder a model sends in place of an argument
        var result = await InvokeAsync(tool, directory, null);

        // Assert: treated exactly as an omitted argument is
        Assert.Equal(Block(policy.WorkingDirectory, "note.txt"), Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves that omitting the directory argument entirely does not raise an error.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_MissingDirectoryArgument_DoesNotThrow()
    {
        // Arrange: a granted working directory holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content");
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: invoke with no arguments at all, as a model omitting them would
        var result = await tool.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: an answer, produced by the tool rather than refused by the framework
        Assert.Equal(Block(policy.WorkingDirectory, "note.txt"), Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a bare relative directory lists that directory under the working directory, with
    ///     names given relative to the working directory — the relative dialect mirrors the caller.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_BareRelativeDirectory_ListsUnderTheAnchor()
    {
        // Arrange: one file in a subdirectory and one elsewhere in the workspace
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(Path.Combine(fixture.Root, "sub"), "child.txt", "child");
        ReparsePointFixture.WriteFile(fixture.Root, "top.txt", "top");
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: name the subdirectory the way a model names it
        var result = await InvokeAsync(tool, "sub", null);

        // Assert: the named subdirectory only, its name relative to the working directory
        Assert.Equal(Block(policy.WorkingDirectory, "sub/child.txt"), Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a discovery listing over two grants reports each location under its own absolute
    ///     header — teaching the model to address the non-anchor location absolutely.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_TwoGrants_Discovery_ListsEachUnderItsAbsoluteHeader()
    {
        // Arrange: the anchor is granted, and a second location is granted elsewhere
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "here.txt", "1");
        ReparsePointFixture.WriteFile(fixture.Outside, "there.txt", "2");
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadWrite(fixture.Root), PathRule.ReadOnly(fixture.Outside)]);
        var tool = TextFileListTool.Create(policy);

        // Act: discover across both grants
        var result = await InvokeAsync(tool, null, null);

        // Assert: both locations appear, each under its own absolute header
        var text = Assert.IsType<string>(result);
        Assert.Contains(Block(policy.WorkingDirectory, "here.txt"), text, StringComparison.Ordinal);
        Assert.Contains(
            Block(RealPathResolver.Resolve(fixture.Outside), "there.txt"),
            text,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a refusal discloses the permitted location so a confined model learns where it may
    ///     read.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_DeniedDirectory_DenialDisclosesPermittedLocation()
    {
        // Arrange: a directory outside the permitted read location
        using var fixture = new ReparsePointFixture();
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: request the refused listing
        var result = await InvokeAsync(tool, fixture.Outside, null);

        // Assert: the request is echoed and the permitted location is named with its level
        var text = Assert.IsType<string>(result);
        Assert.Contains(fixture.Outside, text, StringComparison.Ordinal);
        Assert.Contains(policy.WorkingDirectory, text, StringComparison.Ordinal);
        Assert.Contains("(read-write)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates a policy whose working directory is also its single read-write grant.
    /// </summary>
    /// <param name="root">The location that is both the anchor and the grant.</param>
    /// <param name="limits">The ceilings to apply, or null for the published defaults.</param>
    /// <returns>The constructed policy.</returns>
    private static PathPolicy RootedPolicy(string root, ToolLimits? limits = null)
    {
        return new PathPolicy(root, [PathRule.ReadWrite(root)], limits ?? ToolLimits.Default);
    }

    /// <summary>
    ///     Builds the expected listing block: the absolute header, forward-slashed, then its names.
    /// </summary>
    /// <param name="header">The absolute location header.</param>
    /// <param name="names">The names expected beneath the header, in order.</param>
    /// <returns>The expected block text.</returns>
    private static string Block(string header, params string[] names)
    {
        return ToForwardSlash(header) + "\n" + string.Join("\n", names);
    }

    /// <summary>
    ///     Normalizes a path's separators to a forward slash, as the tool reports them.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The forward-slashed path.</returns>
    private static string ToForwardSlash(string path)
    {
        return path
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    /// <summary>
    ///     Invokes the list tool exactly as a runtime would.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="directory">The directory argument to supply.</param>
    /// <param name="searchPattern">The search pattern argument to supply.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeAsync(
        AIFunction tool,
        string? directory,
        string? searchPattern)
    {
        return await tool.InvokeAsync(
            new AIFunctionArguments
            {
                ["directory"] = directory,
                ["searchPattern"] = searchPattern
            },
            TestContext.Current.CancellationToken);
    }
}
