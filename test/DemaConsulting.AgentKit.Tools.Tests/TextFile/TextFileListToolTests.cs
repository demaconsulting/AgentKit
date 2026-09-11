using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextFileListTool"/> class.
/// </summary>
/// <remarks>
///     The scenario proving an escaped file is not listed is a security control rather than a
///     convenience check: it is the subsystem-level equivalent of the enumeration filtering the
///     access policy already proves, and it fails loudly rather than skipping when the platform
///     cannot create a reparse point.
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
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());

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
    /// <remarks>
    ///     The delegate behind this tool is synchronous, so the scenario also confirms the guard
    ///     applies to a synchronous tool exactly as it does to an asynchronous one.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_PermittedDirectory_ResultIsPlainTextNotJsonElement()
    {
        // Arrange: a permitted directory holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content");
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list the directory
        var result = await InvokeAsync(tool, fixture.Root, null);

        // Assert: the guard delivered the result unserialized
        Assert.IsType<string>(result);
        Assert.IsNotType<JsonElement>(result);
    }

    /// <summary>
    ///     Proves the permitted files beneath a directory are listed.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_PermittedDirectory_ListsThePermittedFiles()
    {
        // Arrange: two files in a permitted directory
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "one.txt", "1");
        ReparsePointFixture.WriteFile(fixture.Root, "two.txt", "2");
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list the directory
        var result = await InvokeAsync(tool, fixture.Root, null);

        // Assert: both files appear
        var text = Assert.IsType<string>(result);
        Assert.Equal("one.txt\ntwo.txt", text);
    }

    /// <summary>
    ///     Proves a file reachable only by following a link out of the permitted location is not
    ///     listed.
    /// </summary>
    /// <remarks>
    ///     Recursive enumeration by the operating system follows the link, so an implementation
    ///     calling the file system directly would advertise the escaped file even though reading
    ///     it is refused. The escaped file is first read through the link on disk, so a fixture
    ///     that failed to create the link cannot turn this scenario into a vacuous pass.
    /// </remarks>
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
        var result = await InvokeAsync(tool, fixture.Root, null);

        // Assert: the escaped file is absent while the permitted one is present
        var text = Assert.IsType<string>(result);
        Assert.Contains("inside.txt", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.txt", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves listed names are relative to the requested directory.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_PermittedDirectory_NamesAreRelativeToTheRequestedDirectory()
    {
        // Arrange: a file one level below the requested directory
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(Path.Combine(fixture.Root, "sub"), "child.txt", "child");
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list the root
        var result = await InvokeAsync(tool, fixture.Root, null);

        // Assert: a relative, platform-neutral name that discloses no host layout
        var text = Assert.IsType<string>(result);
        Assert.Equal("sub/child.txt", text);
        Assert.DoesNotContain(fixture.Root, text, StringComparison.OrdinalIgnoreCase);
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
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list the directory
        var result = await InvokeAsync(tool, fixture.Root, null);

        // Assert: ordinal order, so the same tree always produces the same listing
        Assert.Equal("a.txt\nb.txt\nc.txt", Assert.IsType<string>(result));
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
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list only the text files
        var result = await InvokeAsync(tool, fixture.Root, "*.txt");

        // Assert: the pattern is honored
        Assert.Equal("note.txt", Assert.IsType<string>(result));
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
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list with no pattern supplied at all
        var result = await InvokeAsync(tool, fixture.Root, null);

        // Assert: "everything here" is the least surprising reading of an absent pattern
        Assert.Equal("data.json\nnote.txt", Assert.IsType<string>(result));
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
        var result = await InvokeAsync(tool, fixture.Root, "*.absent");

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
        var result = await InvokeAsync(tool, fixture.Root, null);

        // Assert: a refusal naming the ceiling, never a truncated listing
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("5-character", text, StringComparison.Ordinal);
        Assert.DoesNotContain("first-file.txt", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a directory outside the permitted read location is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_DirectoryOutsideTheReadRoot_ReturnsDenial()
    {
        // Arrange: a sibling directory the read rule does not permit
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
    ///     Proves an omitted directory lists the workspace root rather than being refused.
    /// </summary>
    /// <remarks>
    ///     A model exploring a workspace for the first time has no directory name to supply, and
    ///     what it sends instead is a question this tool can answer. Refusing it merely sends the
    ///     model guessing at locations it has no business exploring.
    /// </remarks>
    /// <param name="directory">The spelling of "no directory" the model supplied.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TextFileListTool_List_OmittedDirectory_ListsTheWorkspaceRoot(string? directory)
    {
        // Arrange: a workspace holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content");
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list with no directory named
        var result = await InvokeAsync(tool, directory, null);

        // Assert: the workspace itself is what "no directory" means
        Assert.Equal("note.txt", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves the literal words a model sends for "no directory" list the workspace root.
    /// </summary>
    /// <remarks>
    ///     A model whose schema marks an argument optional frequently sends the word its own
    ///     runtime prints for absence rather than omitting the argument. Reading that as a
    ///     directory name refuses a well-formed request.
    /// </remarks>
    /// <param name="directory">The placeholder spelling the model supplied.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("None")]
    [InlineData("null")]
    public async Task TextFileListTool_List_PlaceholderDirectory_ListsTheWorkspaceRoot(string directory)
    {
        // Arrange: a workspace holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content");
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: list with the placeholder a model sends in place of an argument
        var result = await InvokeAsync(tool, directory, null);

        // Assert: treated exactly as an omitted argument is
        Assert.Equal("note.txt", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves that omitting the directory argument entirely does not raise an error.
    /// </summary>
    /// <remarks>
    ///     A parameter with no default fails inside the function factory before the tool body is
    ///     reached, and the model sees an opaque framework error rather than anything it can act
    ///     on. This scenario pins the argument as optional, which is where that failure lived.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_MissingDirectoryArgument_DoesNotThrow()
    {
        // Arrange: a workspace holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content");
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: invoke with no arguments at all, as a model omitting them would
        var result = await tool.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: an answer, produced by the tool rather than refused by the framework
        Assert.Equal("note.txt", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a bare relative directory name lists that directory beneath the workspace.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_BareRelativeDirectory_ListsThatDirectory()
    {
        // Arrange: one file in a subdirectory and one elsewhere in the workspace
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(Path.Combine(fixture.Root, "sub"), "child.txt", "child");
        ReparsePointFixture.WriteFile(fixture.Root, "top.txt", "top");
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: name the subdirectory the way a model names it
        var result = await InvokeAsync(tool, "sub", null);

        // Assert: the named subdirectory, not the whole workspace
        Assert.Equal("child.txt", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a refusal discloses no host location.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_DeniedDirectory_DenialTextContainsNoHostDetail()
    {
        // Arrange: a directory outside the permitted read location
        using var fixture = new ReparsePointFixture();
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root));

        // Act: request the refused listing
        var result = await InvokeAsync(tool, fixture.Outside, null);

        // Assert: neither the requested path, the permitted location nor a separator appears
        var text = Assert.IsType<string>(result);
        Assert.DoesNotContain(fixture.Root, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.Outside, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar.ToString(),
            text,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates a policy permitting reads and writes only beneath one workspace, and
    ///     interpreting relative requests against it.
    /// </summary>
    /// <remarks>
    ///     Built through the workspace shorthand deliberately: it is the configuration the
    ///     documentation recommends, so the tests exercise what a host actually builds.
    /// </remarks>
    /// <param name="root">The permitted location.</param>
    /// <param name="limits">The ceilings to apply, or null for the published defaults.</param>
    /// <returns>The constructed policy.</returns>
    private static PathPolicy RootedPolicy(string root, ToolLimits? limits = null)
    {
        return PathPolicy.ForWorkspace(root, limits ?? ToolLimits.Default);
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

