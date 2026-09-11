using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Image;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Image;

/// <summary>
///     Unit tests for the <see cref="ImageReadTool"/> class.
/// </summary>
/// <remarks>
///     Every scenario invokes the constructed tool exactly as a runtime would — through
///     <see cref="AIFunction.InvokeAsync"/> with named arguments — rather than calling an
///     internal method directly, because the delivery of the result through the guarded factory
///     is the load-bearing property this unit exists to provide: without it the image content
///     would be serialized into a <see cref="JsonElement"/> before a provider ever saw it.
///     The reparse-point scenario reuses the fixture defined in the TextFile tests, an internal
///     type in this same assembly, rather than a copy.
/// </remarks>
public class ImageReadToolTests
{
    /// <summary>
    ///     One byte sequence standing in for image content; the tool does not parse it, so
    ///     arbitrary bytes suffice and their distinctness is what proves the real file was read.
    /// </summary>
    private static readonly byte[] SampleBytes = [0x89, 0x50, 0x4E, 0x47, 0x01, 0x02, 0x03, 0x04];

    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void ImageReadTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        // Arrange / Act: read the published constant
        var name = ImageReadTool.ToolName;

        // Assert: the name is qualified by the family prefix the pack claims
        Assert.Equal("image_read", name);
        Assert.StartsWith(ImagePack.FamilyPrefix + "_", name, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the constructed tool carries the published name and a non-empty description.
    /// </summary>
    [Fact]
    public void ImageReadTool_Create_ConstructedTool_CarriesTheToolNameAndADescription()
    {
        // Arrange: a policy governing an otherwise irrelevant location
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: construct the tool
        var tool = ImageReadTool.Create(policy);

        // Assert: the model sees the published name and a description it can choose by
        Assert.Equal(ImageReadTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void ImageReadTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        // Arrange / Act / Assert: a tool with no policy cannot be constructed
        Assert.Throws<ArgumentNullException>(() => ImageReadTool.Create(null!));
    }

    /// <summary>
    ///     Proves an image result reaches the caller as real content and not as serialized JSON.
    /// </summary>
    /// <remarks>
    ///     This is the proof the family exists to make. Without the guarded construction path the
    ///     content would arrive as a <see cref="JsonElement"/> wrapping a <c>data:</c> URI, the
    ///     provider would never receive the image, and the model would fabricate a description of
    ///     content it never saw. The negative assertion names exactly that failure.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageReadTool_Read_SupportedImage_ResultIsContentListNotJsonElement()
    {
        // Arrange: a permitted image file
        using var fixture = new ReparsePointFixture();
        var file = WriteBytes(fixture.Root, "picture.png", SampleBytes);
        var tool = ImageReadTool.Create(RootedPolicy(fixture.Root));

        // Act: invoke the tool as the runtime would
        var result = await InvokeAsync(tool, file);

        // Assert: the guard delivered the content list unserialized
        Assert.IsType<List<AIContent>>(result);
        Assert.IsNotType<JsonElement>(result);
    }

    /// <summary>
    ///     Proves a permitted image is returned as data content carrying its media type and bytes.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageReadTool_Read_SupportedImage_ReturnsDataContentWithMediaType()
    {
        // Arrange: a permitted PNG whose bytes identify it unambiguously
        using var fixture = new ReparsePointFixture();
        var file = WriteBytes(fixture.Root, "picture.png", SampleBytes);
        var tool = ImageReadTool.Create(RootedPolicy(fixture.Root));

        // Act: read the image
        var result = await InvokeAsync(tool, file);

        // Assert: a caption followed by the image, carrying the media type and the real bytes
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.Equal(2, content.Count);
        Assert.IsType<TextContent>(content[0]);
        var data = Assert.IsType<DataContent>(content[1]);
        Assert.Equal(ImageMediaTypes.Png, data.MediaType);
        Assert.Equal(SampleBytes, data.Data.ToArray());
    }

    /// <summary>
    ///     Proves a permitted PDF is returned as binary content carrying the PDF media type.
    /// </summary>
    /// <remarks>
    ///     A PDF is not an <c>image/</c> media type, so it is dispatched through
    ///     <see cref="ToolResult.Binary"/> rather than <see cref="ToolResult.Image"/>, which
    ///     would throw. The result shape is identical, so the marshalling guard applies equally.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageReadTool_Read_PermittedPdf_ReturnsBinaryContentWithPdfMediaType()
    {
        // Arrange: a permitted PDF whose bytes identify it unambiguously
        using var fixture = new ReparsePointFixture();
        var file = WriteBytes(fixture.Root, "document.pdf", SampleBytes);
        var tool = ImageReadTool.Create(RootedPolicy(fixture.Root));

        // Act: read the document
        var result = await InvokeAsync(tool, file);

        // Assert: the caption-plus-content shape survives unserialized, carrying the PDF
        // media type and bytes. The caption is asserted as well as the data, because a
        // regression that preserved the bytes while dropping or reordering the caption would
        // otherwise pass unnoticed.
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.IsNotType<JsonElement>(result);
        Assert.Equal(2, content.Count);
        Assert.IsType<TextContent>(content[0]);
        var data = Assert.IsType<DataContent>(content[1]);
        Assert.Equal(ImageMediaTypes.Pdf, data.MediaType);
        Assert.Equal(SampleBytes, data.Data.ToArray());
    }

    /// <summary>
    ///     Proves a path outside the permitted read location is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageReadTool_Read_PathOutsideTheReadRoot_ReturnsDenial()
    {
        // Arrange: an image in a sibling directory no grant permits reading
        using var fixture = new ReparsePointFixture();
        var outsideFile = WriteBytes(fixture.Outside, "secret.png", SampleBytes);
        var tool = ImageReadTool.Create(RootedPolicy(fixture.Root));

        // Act: request the file outside the permitted location
        var result = await InvokeAsync(tool, outsideFile);

        // Assert: refused before anything is learned about the file
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
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
    public async Task ImageReadTool_Read_FileBeneathLinkOutsideRoot_ReturnsDenial()
    {
        // Arrange: a real reparse point inside the permitted root pointing outside it
        using var fixture = new ReparsePointFixture();
        WriteBytes(fixture.Outside, "secret.png", SampleBytes);
        var link = fixture.CreateDirectoryLink("escape", fixture.Outside);
        var escapedPath = Path.Combine(link, "secret.png");
        Assert.Equal(SampleBytes, await File.ReadAllBytesAsync(
            escapedPath,
            TestContext.Current.CancellationToken));
        var tool = ImageReadTool.Create(RootedPolicy(fixture.Root));

        // Act: request the escaped file through a path that looks contained
        var result = await InvokeAsync(tool, escapedPath);

        // Assert: refused on its real location, not on how the path was spelled
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an unsupported type is refused with a redirect to the text file read tool.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageReadTool_Read_UnsupportedType_ReturnsDenialRedirectingToTextFileRead()
    {
        // Arrange: a permitted but vector-text file this family cannot read
        using var fixture = new ReparsePointFixture();
        var file = WriteBytes(fixture.Root, "diagram.svg", SampleBytes);
        var tool = ImageReadTool.Create(RootedPolicy(fixture.Root));

        // Act: request the .svg file
        var result = await InvokeAsync(tool, file);

        // Assert: refused as an unsupported type and pointed at the tool that can read it
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains(TextFileReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a directory is refused as a malformed request.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageReadTool_Read_DirectoryPath_ReturnsDenial()
    {
        // Arrange: a permitted directory rather than a file
        using var fixture = new ReparsePointFixture();
        var tool = ImageReadTool.Create(RootedPolicy(fixture.Root));

        // Act: request the directory itself
        var result = await InvokeAsync(tool, fixture.Root);

        // Assert: refused as malformed, since a directory has no visual content
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing file of a supported type is refused as not found.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageReadTool_Read_MissingFile_ReturnsDenial()
    {
        // Arrange: a permitted location containing no such file
        using var fixture = new ReparsePointFixture();
        var tool = ImageReadTool.Create(RootedPolicy(fixture.Root));

        // Act: request a supported-type file that does not exist
        var result = await InvokeAsync(tool, Path.Combine(fixture.Root, "absent.png"));

        // Assert: refused as not found
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file beyond the binary ceiling is refused with the ceiling named.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageReadTool_Read_FileLargerThanTheBinaryCeiling_ReturnsDenialNamingTheCeiling()
    {
        // Arrange: a file larger than an eight-byte binary ceiling
        using var fixture = new ReparsePointFixture();
        var file = WriteBytes(fixture.Root, "big.png", new byte[64]);
        var tool = ImageReadTool.Create(RootedPolicy(fixture.Root, new ToolLimits(maxBinaryBytes: 8)));

        // Act: read the oversized file
        var result = await InvokeAsync(tool, file);

        // Assert: a refusal naming the ceiling, never a truncated image
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("8-byte", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file exactly at the binary ceiling is read rather than refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageReadTool_Read_FileAtTheBinaryCeiling_IsRead()
    {
        // Arrange: an eight-byte file and an eight-byte binary ceiling
        using var fixture = new ReparsePointFixture();
        var file = WriteBytes(fixture.Root, "exact.png", SampleBytes);
        var tool = ImageReadTool.Create(RootedPolicy(fixture.Root, new ToolLimits(maxBinaryBytes: 8)));

        // Act: read the file sitting exactly on the boundary
        var result = await InvokeAsync(tool, file);

        // Assert: the ceiling is inclusive, so the image is returned
        var content = Assert.IsType<List<AIContent>>(result);
        var data = Assert.IsType<DataContent>(content[1]);
        Assert.Equal(SampleBytes, data.Data.ToArray());
    }

    /// <summary>
    ///     Proves an empty path is refused rather than throwing at the model.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageReadTool_Read_EmptyPath_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a tool governed by an unrestricted policy, so only the request is at fault
        var tool = ImageReadTool.Create(
            new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]));

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
    public async Task ImageReadTool_Read_WhitespacePath_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a tool governed by an unrestricted policy
        var tool = ImageReadTool.Create(
            new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]));

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
    public async Task ImageReadTool_Read_BareFileName_ReturnsTheImageContent()
    {
        // Arrange: a workspace holding one image
        using var fixture = new ReparsePointFixture();
        WriteBytes(fixture.Root, "picture.png", SampleBytes);
        var tool = ImageReadTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for the image by name alone
        var result = await InvokeAsync(tool, "picture.png");

        // Assert: the caption and the real bytes, not a refusal
        var content = Assert.IsType<List<AIContent>>(result);
        var data = Assert.IsType<DataContent>(content[1]);
        Assert.Equal(SampleBytes, data.Data.ToArray());
    }

    /// <summary>
    ///     Proves that omitting the path argument produces a refusal rather than a framework
    ///     error.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageReadTool_Read_MissingPathArgument_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a workspace-governed tool, so only the request is at fault
        using var fixture = new ReparsePointFixture();
        var tool = ImageReadTool.Create(RootedPolicy(fixture.Root));

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
    ///     Proves a policy refusal discloses the permitted location so a confined model learns
    ///     where it may look.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageReadTool_Read_DeniedPath_DenialDisclosesPermittedLocation()
    {
        // Arrange: a file outside the permitted location
        using var fixture = new ReparsePointFixture();
        var outsideFile = WriteBytes(fixture.Outside, "secret.png", SampleBytes);
        var policy = RootedPolicy(fixture.Root);
        var tool = ImageReadTool.Create(policy);

        // Act: request the refused file
        var result = await InvokeAsync(tool, outsideFile);

        // Assert: the request is echoed and the permitted location is named with its level
        var text = Assert.IsType<string>(result);
        Assert.Contains(outsideFile, text, StringComparison.Ordinal);
        Assert.Contains(policy.WorkingDirectory, text, StringComparison.Ordinal);
        Assert.Contains("(read-write)", text, StringComparison.Ordinal);
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
        return new PathPolicy(root, [PathRule.ReadWrite(root)], limits ?? ToolLimits.Default);
    }

    /// <summary>
    ///     Writes a file with known bytes into a directory of the temporary tree.
    /// </summary>
    /// <remarks>
    ///     The tool returns content rather than text, so the fixture's text writer is not used;
    ///     the tool does not parse the bytes, so any distinctive sequence proves the real file
    ///     was reached.
    /// </remarks>
    /// <param name="directory">The directory to write into; created when it does not exist.</param>
    /// <param name="fileName">The name of the file to write.</param>
    /// <param name="content">The bytes to write.</param>
    /// <returns>The full path of the written file.</returns>
    private static string WriteBytes(string directory, string fileName, byte[] content)
    {
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, fileName);
        File.WriteAllBytes(filePath, content);
        return filePath;
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
