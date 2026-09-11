using System.Text;
using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Image;
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
    ///     A real PNG file header: the eight-byte signature followed by a length field, a
    ///     NUL-bearing chunk, and a distinctive ASCII marker. The NUL bytes and invalid UTF-8
    ///     make it unambiguously binary, the marker would only surface if the bytes were decoded
    ///     and returned, and its <c>.png</c> name resolves to a type the image tool reads.
    /// </summary>
    private static readonly byte[] PngHeaderBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D,
        0x70, 0x69, 0x78, 0x65, 0x6C, 0x2D, 0x6D, 0x61, 0x72, 0x6B, 0x65, 0x72
    ];

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
    ///     Proves a bare file name — the path a model actually writes — is read from the
    ///     workspace.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_BareFileName_ReturnsTheFileContents()
    {
        // Arrange: a workspace holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for the file by name alone
        var result = await InvokeAsync(tool, "notes.txt");

        // Assert: the file's text, not a refusal
        Assert.Equal("inside-content", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a name prefixed with the current-directory token is read from the workspace.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_DotSlashFileName_ReturnsTheFileContents()
    {
        // Arrange: a workspace holding one file
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for it with the leading token a model often adds
        var result = await InvokeAsync(tool, "./notes.txt");

        // Assert: the same file the bare name reaches
        Assert.Equal("inside-content", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a nested relative path is read from the workspace.
    /// </summary>
    /// <remarks>
    ///     The forward slash is deliberate: it is the separator a model writes regardless of the
    ///     host platform, and it is the separator the listing tool reports names with.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_NestedRelativePath_ReturnsTheFileContents()
    {
        // Arrange: a file one level below the workspace root
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(Path.Combine(fixture.Root, "sub"), "child.txt", "nested");
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for it the way a listing would have named it
        var result = await InvokeAsync(tool, "sub/child.txt");

        // Assert: the nested file's text
        Assert.Equal("nested", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves an absolute path inside the workspace is still read.
    /// </summary>
    /// <remarks>
    ///     Interpreting a bare name against the workspace must not withdraw the absolute form,
    ///     which a host composing paths itself still relies on.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_AbsolutePathInsideRoot_ReturnsTheFileContents()
    {
        // Arrange: a workspace holding one file, addressed absolutely
        using var fixture = new ReparsePointFixture();
        var path = ReparsePointFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for the file by its absolute location
        var result = await InvokeAsync(tool, path);

        // Assert: the same content the bare name returns
        Assert.Equal("inside-content", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves that omitting the path argument produces a refusal rather than a framework
    ///     error.
    /// </summary>
    /// <remarks>
    ///     A parameter with no default fails inside the function factory before the tool body is
    ///     reached, leaving the model an opaque error it cannot act on. A read really does need
    ///     a path, so the correct answer is a refusal that says which one to supply.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_MissingPathArgument_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a workspace-governed tool, so only the request is at fault
        using var fixture = new ReparsePointFixture();
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: invoke with no arguments at all
        var result = await tool.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: a refusal composed by the tool, naming what to supply
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("workspace root", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a refusal tells the model what form a path should take.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_DeniedPath_DenialStatesTheExpectedPathForm()
    {
        // Arrange: a file outside the workspace
        using var fixture = new ReparsePointFixture();
        var outsideFile = ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "secret");
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: request the refused file
        var result = await InvokeAsync(tool, outsideFile);

        // Assert: guidance the model can act on, without any host location in it
        var text = Assert.IsType<string>(result);
        Assert.Contains("workspace root", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar.ToString(),
            text,
            StringComparison.Ordinal);
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
    ///     Proves a binary image file is refused as unsupported media, redirecting to the image
    ///     read tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_BinaryImageFile_ReturnsDenialRedirectingToImageRead()
    {
        // Arrange: a real PNG header — magic bytes plus a NUL-bearing chunk and a marker
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteBytes(fixture.Root, "picture.png", PngHeaderBytes);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: read the image as if it were text
        var result = await InvokeAsync(tool, file);

        // Assert: refused as unsupported media, pointed at the tool that can read it, no bytes leaked
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains(ImageReadTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain("pixel-marker", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a binary image requested by a bare relative name — the form a model actually
    ///     writes — is refused with the image redirect.
    /// </summary>
    /// <remarks>
    ///     A model names a file relative to the workspace, so the guard must reach the same
    ///     verdict for <c>picture.png</c> as for its absolute location; a fixture that only ever
    ///     tested absolute paths would miss the very request a model makes.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_BinaryImageByRelativePath_ReturnsDenialRedirectingToImageRead()
    {
        // Arrange: a workspace holding one PNG, addressed by name alone
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteBytes(fixture.Root, "picture.png", PngHeaderBytes);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for it the way a model phrases it, with no location
        var result = await InvokeAsync(tool, "picture.png");

        // Assert: the same refusal and redirect the absolute form receives
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains(ImageReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a binary file whose type no tool in the family can read is refused without a
    ///     redirect.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_BinaryNonImageFile_ReturnsDenialWithoutRedirect()
    {
        // Arrange: bytes containing a NUL under an extension no image tool resolves
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteBytes(
            fixture.Root,
            "data.bin",
            [0x00, 0x01, 0x02, 0x03, 0x00, 0xFF]);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: read the binary file
        var result = await InvokeAsync(tool, file);

        // Assert: refused as unsupported media, but with no tool to honestly redirect to
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(ImageReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a mark-less file that is not valid UTF-8 yet contains no NUL is refused by the
    ///     UTF-8 validation rather than the NUL rule.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_InvalidUtf8WithoutBom_ReturnsDenial()
    {
        // Arrange: 0xC0 is never a valid UTF-8 lead byte; the sequence carries no NUL
        using var fixture = new ReparsePointFixture();
        var file = ReparsePointFixture.WriteBytes(
            fixture.Root,
            "garbled.bin",
            [0x41, 0x42, 0xC0, 0xC0, 0x43]);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: read the invalid-UTF-8 file
        var result = await InvokeAsync(tool, file);

        // Assert: refused as binary, reached through the UTF-8 branch rather than the NUL branch
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a UTF-16 little-endian text file with a byte-order mark still reads correctly.
    /// </summary>
    /// <remarks>
    ///     This is the exact regression the naive "a NUL byte means binary" heuristic would
    ///     cause: UTF-16 text legitimately contains NUL bytes yet decodes correctly today, so the
    ///     byte-order-mark check must take precedence.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_Utf16BomTextFile_ReturnsTheFileContents()
    {
        // Arrange: UTF-16 LE content preceded by its byte-order mark
        using var fixture = new ReparsePointFixture();
        byte[] bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("utf16-le-content")];
        var file = ReparsePointFixture.WriteBytes(fixture.Root, "unicode.txt", bytes);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: read the encoded text file
        var result = await InvokeAsync(tool, file);

        // Assert: the mark decides text, and the decode path returns the content
        Assert.Equal("utf16-le-content", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a UTF-16 big-endian text file with a byte-order mark still reads correctly.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_Utf16BigEndianBomTextFile_ReturnsTheFileContents()
    {
        // Arrange: UTF-16 BE content preceded by its byte-order mark
        using var fixture = new ReparsePointFixture();
        byte[] bytes =
        [
            .. Encoding.BigEndianUnicode.GetPreamble(),
            .. Encoding.BigEndianUnicode.GetBytes("utf16-be-content")
        ];
        var file = ReparsePointFixture.WriteBytes(fixture.Root, "unicode-be.txt", bytes);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: read the encoded text file
        var result = await InvokeAsync(tool, file);

        // Assert: the big-endian mark path also decides text
        Assert.Equal("utf16-be-content", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a UTF-32 little-endian text file with a byte-order mark still reads correctly.
    /// </summary>
    /// <remarks>
    ///     Guards the byte-order-mark ordering: the UTF-16 LE mark <c>FF FE</c> is a prefix of the
    ///     UTF-32 LE mark <c>FF FE 00 00</c>, so a guard that tested the shorter mark first would
    ///     misread this file and its trailing NUL bytes as binary.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_Utf32BomTextFile_ReturnsTheFileContents()
    {
        // Arrange: UTF-32 LE content preceded by its four-byte byte-order mark
        using var fixture = new ReparsePointFixture();
        byte[] bytes = [.. Encoding.UTF32.GetPreamble(), .. Encoding.UTF32.GetBytes("utf32-le-content")];
        var file = ReparsePointFixture.WriteBytes(fixture.Root, "unicode32.txt", bytes);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: read the encoded text file
        var result = await InvokeAsync(tool, file);

        // Assert: the four-byte mark is recognized before the two-byte one, so the file is text
        Assert.Equal("utf32-le-content", Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a plain multi-line UTF-8 text file with no byte-order mark still reads
    ///     correctly.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_Utf8TextFile_ReturnsTheFileContents()
    {
        // Arrange: mark-less UTF-8 with a non-ASCII character, the ordinary text case
        using var fixture = new ReparsePointFixture();
        const string content = "first line\nsecond line \u2248 approx\n";
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        var file = ReparsePointFixture.WriteBytes(fixture.Root, "notes.txt", bytes);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: read the text file
        var result = await InvokeAsync(tool, file);

        // Assert: valid UTF-8 is text and reads back unchanged
        Assert.Equal(content, Assert.IsType<string>(result));
    }

    /// <summary>
    ///     Proves a valid UTF-8 file whose multi-byte character straddles the sniff-window
    ///     boundary is read rather than misclassified as binary.
    /// </summary>
    /// <remarks>
    ///     The file is built from three-byte characters and made larger than the sniff window, so
    ///     the window edge necessarily cuts one character in half. Reading it correctly proves the
    ///     boundary mitigation that buffers, rather than rejects, a split trailing sequence.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task TextFileReadTool_Read_LargeValidUtf8_ReadsAcrossWindowBoundary()
    {
        // Arrange: 2000 three-byte characters (6000 bytes) — larger than the 4096-byte window,
        // so the window boundary lands in the middle of a character
        using var fixture = new ReparsePointFixture();
        var content = new string('\u2248', 2000);
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        var file = ReparsePointFixture.WriteBytes(fixture.Root, "wide-utf8.txt", bytes);
        var tool = TextFileReadTool.Create(RootedPolicy(fixture.Root));

        // Act: read the file whose character is split by the window edge
        var result = await InvokeAsync(tool, file);

        // Assert: a split character does not make a valid UTF-8 file look binary
        Assert.Equal(content, Assert.IsType<string>(result));
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

