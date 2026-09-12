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
    ///     Proves a named directory matching nothing reports an empty listing rather than a refusal.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_NoMatches_ReturnsAnEmptyListingNotADenial()
    {
        // Arrange: a permitted directory holding nothing the pattern matches
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "text");
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: name the directory and give a pattern nothing matches — the named-directory path,
        // distinct from discovery, which now reports every permitted location including an empty one
        var result = await InvokeAsync(tool, policy.WorkingDirectory, "*.absent");

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
    ///     Proves a discovery listing over a single empty read-write location reports that location
    ///     under its absolute header with a marker naming its access level, not as "No files matched.".
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_Discovery_SingleEmptyLocation_AppearsWithItsAccessLevel()
    {
        // Arrange: a granted, read-write working directory holding no matching file
        using var fixture = new ReparsePointFixture();
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: discover, the way a model with no directory name would
        var result = await InvokeAsync(tool, null, null);

        // Assert: the empty location appears under its header with its access level, not hidden
        var text = Assert.IsType<string>(result);
        Assert.Equal(EmptyBlock(policy.WorkingDirectory, "(no files - read-write)"), text);
        Assert.NotEqual("No files matched.", text);
    }

    /// <summary>
    ///     Proves the marker beneath an empty read-only location names read-only, so a model does
    ///     not attempt a write the policy would refuse there.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_Discovery_ReadOnlyEmptyLocation_MarkerNamesReadOnly()
    {
        // Arrange: a populated read-write anchor and a separate, empty read-only location
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "here.txt", "1");
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadWrite(fixture.Root), PathRule.ReadOnly(fixture.Outside)]);
        var tool = TextFileListTool.Create(policy);

        // Act: discover across both grants
        var result = await InvokeAsync(tool, null, null);

        // Assert: the empty read-only location appears with the read-only marker
        var text = Assert.IsType<string>(result);
        Assert.Contains(
            EmptyBlock(RealPathResolver.Resolve(fixture.Outside), "(no files - read-only)"),
            text,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an empty location is reported wherever it falls in ordinal order — first, middle
    ///     and last — and can never be confused with an adjacent populated block or its header.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_Discovery_EmptyLocationsAmongPopulated_EachAppearsInFirstMiddleLast()
    {
        // Arrange: five granted locations whose ordinal order is empty, populated, empty,
        // populated, empty — so an empty block occupies the first, a middle and the last position
        using var fixture = new ReparsePointFixture();
        var one = Path.Combine(fixture.Root, "1e");
        var two = Path.Combine(fixture.Root, "2p");
        var three = Path.Combine(fixture.Root, "3e");
        var four = Path.Combine(fixture.Root, "4p");
        var five = Path.Combine(fixture.Root, "5e");
        Directory.CreateDirectory(one);
        Directory.CreateDirectory(three);
        Directory.CreateDirectory(five);
        ReparsePointFixture.WriteFile(two, "a.txt", "a");
        ReparsePointFixture.WriteFile(four, "b.txt", "b");
        var policy = new PathPolicy(
            fixture.Root,
            [
                PathRule.ReadWrite(one),
                PathRule.ReadWrite(two),
                PathRule.ReadWrite(three),
                PathRule.ReadWrite(four),
                PathRule.ReadWrite(five)
            ]);
        var tool = TextFileListTool.Create(policy);

        // Act: discover across all five grants
        var result = await InvokeAsync(tool, null, null);

        // Assert: the exact joined listing, so an empty block is never taken for an adjacent header
        var expected = string.Join(
            "\n\n",
            EmptyBlock(RealPathResolver.Resolve(one), "(no files - read-write)"),
            Block(RealPathResolver.Resolve(two), "a.txt"),
            EmptyBlock(RealPathResolver.Resolve(three), "(no files - read-write)"),
            Block(RealPathResolver.Resolve(four), "b.txt"),
            EmptyBlock(RealPathResolver.Resolve(five), "(no files - read-write)"));
        Assert.Equal(expected, Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a discovery listing in which every granted location is empty reports each one with
    ///     a marker rather than collapsing to "No files matched.".
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_Discovery_EveryLocationEmpty_AllAppearWithMarkers()
    {
        // Arrange: three granted locations, all empty
        using var fixture = new ReparsePointFixture();
        var alpha = Path.Combine(fixture.Root, "alpha");
        var bravo = Path.Combine(fixture.Root, "bravo");
        var charlie = Path.Combine(fixture.Root, "charlie");
        Directory.CreateDirectory(alpha);
        Directory.CreateDirectory(bravo);
        Directory.CreateDirectory(charlie);
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadWrite(alpha), PathRule.ReadWrite(bravo), PathRule.ReadWrite(charlie)]);
        var tool = TextFileListTool.Create(policy);

        // Act: discover across all three empty grants
        var result = await InvokeAsync(tool, null, null);

        // Assert: not a refusal; each location present under its header, blocks blank-line separated
        var text = Assert.IsType<string>(result);
        Assert.NotEqual("No files matched.", text);
        Assert.Contains(EmptyBlock(RealPathResolver.Resolve(alpha), "(no files - read-write)"), text, StringComparison.Ordinal);
        Assert.Contains(EmptyBlock(RealPathResolver.Resolve(bravo), "(no files - read-write)"), text, StringComparison.Ordinal);
        Assert.Contains(EmptyBlock(RealPathResolver.Resolve(charlie), "(no files - read-write)"), text, StringComparison.Ordinal);
        Assert.Contains("\n\n", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a location holding only a subdirectory, and no matching file, is reported as empty
    ///     — the listing reports files, so subdirectories alone leave the location empty.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_Discovery_LocationWithOnlySubdirectories_AppearsAsEmpty()
    {
        // Arrange: a granted location whose only content is an empty subdirectory
        using var fixture = new ReparsePointFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "sub"));
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: discover the location
        var result = await InvokeAsync(tool, null, null);

        // Assert: no file matched, so the location renders as an empty block
        Assert.Equal(
            EmptyBlock(policy.WorkingDirectory, "(no files - read-write)"),
            Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves adding empty-location reporting does not change how a populated block renders: the
    ///     populated block is byte-identical to the pre-change rendering.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_Discovery_PopulatedAndEmpty_PopulatedBlockRendersExactlyAsBefore()
    {
        // Arrange: a populated anchor and a separate, empty granted location
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "content");
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadWrite(fixture.Root), PathRule.ReadWrite(fixture.Outside)]);
        var tool = TextFileListTool.Create(policy);

        // Act: discover across both grants
        var result = await InvokeAsync(tool, null, null);

        // Assert: the populated block is exactly the block the pre-change tool produced
        var text = Assert.IsType<string>(result);
        Assert.Contains(Block(policy.WorkingDirectory, "note.txt"), text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a policy carrying no grants still reports "No files matched." for discovery — there
    ///     is genuinely no permitted location to report.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_Discovery_NoGrants_ReturnsNoFilesMatched()
    {
        // Arrange: a policy with an empty grant set
        using var fixture = new ReparsePointFixture();
        var tool = TextFileListTool.Create(new PathPolicy(fixture.Root, []));

        // Act: discover with nothing granted
        var result = await InvokeAsync(tool, null, null);

        // Assert: the zero-grants contract is preserved
        Assert.Equal("No files matched.", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves the empty-location markers count toward the result ceiling: a discovery whose
    ///     headers and markers exceed the ceiling is refused, naming it, rather than truncated.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_Discovery_EmptyMarkersBeyondResultCeiling_ReturnsDenialNamingTheCeiling()
    {
        // Arrange: an empty location whose header and marker exceed a five-character ceiling
        using var fixture = new ReparsePointFixture();
        var limits = new ToolLimits(maxResultCharacters: 5);
        var tool = TextFileListTool.Create(RootedPolicy(fixture.Root, limits));

        // Act: discover the empty location
        var result = await InvokeAsync(tool, null, null);

        // Assert: a refusal naming the ceiling, never a truncated marker
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("5-character", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a discovery listing whose search pattern matches nothing in a populated location
    ///     still reports that location, so a narrow pattern never hides a location from the agent.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileListTool_List_Discovery_PatternMatchesNothing_LocationStillAppears()
    {
        // Arrange: a granted location that holds a file the pattern will not match
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "note.txt", "text");
        var policy = RootedPolicy(fixture.Root);
        var tool = TextFileListTool.Create(policy);

        // Act: discover with a pattern nothing matches — emptiness here comes from the pattern,
        // not from the location, and the agent must still learn the location exists
        var result = await InvokeAsync(tool, null, "*.absent");

        // Assert: the location is reported as empty rather than omitted
        Assert.Equal(
            EmptyBlock(policy.WorkingDirectory, "(no files - read-write)"),
            Assert.IsType<string>(result));
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
    ///     Builds the expected empty-location block: the absolute header, forward-slashed, then a
    ///     single marker line naming the access level.
    /// </summary>
    /// <param name="header">The absolute location header.</param>
    /// <param name="marker">The access-level marker expected beneath the header.</param>
    /// <returns>The expected empty-location block text.</returns>
    private static string EmptyBlock(string header, string marker)
    {
        return ToForwardSlash(header) + "\n" + marker;
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
