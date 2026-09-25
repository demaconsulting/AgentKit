using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Image;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Image;

/// <summary>
///     Unit tests for the <see cref="ImageCropTool"/> class.
/// </summary>
/// <remarks>
///     <para>
///     Every scenario invokes the constructed tool exactly as a runtime would — through
///     <see cref="AIFunction.InvokeAsync"/> with named arguments — rather than calling an
///     internal method directly, because the delivery of the image result through the guarded
///     factory is a load-bearing property of this unit: without it the region would be serialized
///     into a <see cref="JsonElement"/> before a provider ever saw it.
///     </para>
///     <para>
///     Nothing is mocked. The access policy and the file system are real, and every image is
///     built by the test through <see cref="ImageTestImages"/> so the expected answer — a size, a
///     pixel value, a refusal — is known independently of the library that produces it.
///     </para>
/// </remarks>
public class ImageCropToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void ImageCropTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        // Arrange / Act: read the published constant
        var name = ImageCropTool.ToolName;

        // Assert: the name is qualified by the family prefix the pack claims
        Assert.Equal("image_crop", name);
        Assert.StartsWith(ImagePack.FamilyPrefix + "_", name, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the constructed tool carries the published name and a non-empty description.
    /// </summary>
    [Fact]
    public void ImageCropTool_Create_ConstructedTool_CarriesTheToolNameAndADescription()
    {
        // Arrange: a policy governing an otherwise irrelevant location
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: construct the tool
        var tool = ImageCropTool.Create(policy);

        // Assert: the model sees the published name and a description it can choose by, naming
        // the tool the coordinate space comes from so the two compose
        Assert.Equal(ImageCropTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        Assert.Contains(ImageReadTool.ToolName, tool.Description, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void ImageCropTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        // Arrange / Act / Assert: a tool with no policy cannot be constructed
        Assert.Throws<ArgumentNullException>(() => ImageCropTool.Create(null!));
    }

    /// <summary>
    ///     Proves a permitted PNG yields the requested region as image content.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_PermittedPng_ReturnsCroppedImageContent()
    {
        // Arrange: a permitted PNG larger than the region asked for
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(40, 30));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region well inside the image
        var result = await InvokeAsync(tool, file, 5, 6, 10, 7);

        // Assert: a caption followed by a PNG whose decoded size is the region asked for
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.Equal(2, content.Count);
        Assert.IsType<TextContent>(content[0]);
        var data = Assert.IsType<DataContent>(content[1]);
        Assert.Equal(ImageMediaTypes.Png, data.MediaType);

        var decoded = ImageTestImages.Decode(data.Data.ToArray());
        Assert.Equal(10, decoded.Width);
        Assert.Equal(7, decoded.Height);
    }

    /// <summary>
    ///     Proves a permitted JPEG yields its region as PNG content.
    /// </summary>
    /// <remarks>
    ///     The family's output format is its own rather than the source's: PNG is lossless, so a
    ///     region the model asked to examine closely carries no re-compression artifacts, and it
    ///     is a format every vision provider accepts.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_PermittedJpeg_ReturnsPngContent()
    {
        // Arrange: a permitted JPEG
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "photo.jpg", ImageTestImages.Jpeg(48, 32));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region of it
        var result = await InvokeAsync(tool, file, 8, 4, 16, 12);

        // Assert: the result is PNG, not the source's own format
        var content = Assert.IsType<List<AIContent>>(result);
        var data = Assert.IsType<DataContent>(content[1]);
        Assert.Equal(ImageMediaTypes.Png, data.MediaType);

        var decoded = ImageTestImages.Decode(data.Data.ToArray());
        Assert.Equal(16, decoded.Width);
        Assert.Equal(12, decoded.Height);
    }

    /// <summary>
    ///     Proves the returned region carries the source image's pixels unaltered.
    /// </summary>
    /// <remarks>
    ///     The demonstrable form of "the pixels were preserved exactly": the source is built with
    ///     a distinct value in every channel of every pixel, including alpha, and every pixel of
    ///     the returned region is compared against the pixel it was supposed to come from. An
    ///     encoder that quietly discarded alpha, or an offset that was off by one, fails here.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_ReturnedRegion_CarriesTheSourcePixelsExactly()
    {
        // Arrange: a permitted PNG whose every pixel is distinguishable from every other
        using var fixture = new TempDirectoryFixture();
        var sourceBytes = ImageTestImages.Png(16, 16);
        var file = WriteBytes(fixture.Root, "detail.png", sourceBytes);
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));
        var source = ImageTestImages.Decode(sourceBytes);

        // Act: take a region whose origin is not the image's origin
        var result = await InvokeAsync(tool, file, 4, 5, 6, 7);

        // Assert: every pixel of the region, alpha included, is the source's own
        var content = Assert.IsType<List<AIContent>>(result);
        var data = Assert.IsType<DataContent>(content[1]);
        var decoded = ImageTestImages.Decode(data.Data.ToArray());

        Assert.Equal(6, decoded.Width);
        Assert.Equal(7, decoded.Height);
        for (var row = 0; row < decoded.Height; row++)
        {
            for (var column = 0; column < decoded.Width; column++)
            {
                Assert.Equal(source[4 + column, 5 + row], decoded[column, row]);
            }
        }
    }

    /// <summary>
    ///     Proves a region result reaches the caller as real content and not as serialized JSON.
    /// </summary>
    /// <remarks>
    ///     The guarded-delivery guard. Without it the content would arrive as a
    ///     <see cref="JsonElement"/>, the provider would never receive the region, and the model
    ///     would fabricate a description of content it never saw.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_Result_IsContentListNotJsonElement()
    {
        // Arrange: a permitted image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(20, 20));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: invoke the tool as the runtime would
        var result = await InvokeAsync(tool, file, 0, 0, 4, 4);

        // Assert: the guard delivered the content list unserialized
        Assert.IsType<List<AIContent>>(result);
        Assert.IsNotType<JsonElement>(result);
    }

    /// <summary>
    ///     Proves a bare file name is resolved against the workspace.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_BareFileName_ResolvesAgainstTheWorkspace()
    {
        // Arrange: a workspace holding one image
        using var fixture = new TempDirectoryFixture();
        WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(20, 20));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region by name alone, as a model writes it
        var result = await InvokeAsync(tool, "picture.png", 1, 1, 3, 3);

        // Assert: the region comes back rather than a refusal
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.IsType<DataContent>(content[1]);
    }

    /// <summary>
    ///     Proves a region covering the whole image is accepted.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_WholeImageRegion_IsAccepted()
    {
        // Arrange: a permitted image of a known size
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(20, 14));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for exactly the whole image, which is the inclusive upper boundary
        var result = await InvokeAsync(tool, file, 0, 0, 20, 14);

        // Assert: accepted, so the bound is inclusive rather than something a caller must guess
        var content = Assert.IsType<List<AIContent>>(result);
        var decoded = ImageTestImages.Decode(Assert.IsType<DataContent>(content[1]).Data.ToArray());
        Assert.Equal(20, decoded.Width);
        Assert.Equal(14, decoded.Height);
    }

    /// <summary>
    ///     Proves a single-pixel region at the far corner is accepted.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_SinglePixelRegion_IsAccepted()
    {
        // Arrange: a permitted image of a known size
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(20, 14));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: the minimum valid extent, at the last addressable pixel
        var result = await InvokeAsync(tool, file, 19, 13, 1, 1);

        // Assert: accepted, pinning the other end of the same boundary
        var content = Assert.IsType<List<AIContent>>(result);
        var decoded = ImageTestImages.Decode(Assert.IsType<DataContent>(content[1]).Data.ToArray());
        Assert.Equal(1, decoded.Width);
        Assert.Equal(1, decoded.Height);
    }

    /// <summary>
    ///     Proves a region extending past the image's right edge is refused, naming the real size.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_RegionExtendingPastTheRightEdge_IsRefusedNamingTheDimensions()
    {
        // Arrange: a permitted image of a known size
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(20, 14));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region one pixel too wide
        var result = await InvokeAsync(tool, file, 5, 2, 16, 4);

        // Assert: refused, with the image's real dimensions named so the model can correct itself
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("20x14 pixels", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a region extending past the image's bottom edge is refused, naming the real size.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_RegionExtendingPastTheBottomEdge_IsRefusedNamingTheDimensions()
    {
        // Arrange: a permitted image of a known size
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(20, 14));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region one pixel too tall
        var result = await InvokeAsync(tool, file, 2, 5, 4, 10);

        // Assert: the other axis is checked too, not only the first
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("20x14 pixels", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an origin sitting exactly at the image's width is refused.
    /// </summary>
    /// <remarks>
    ///     The off-by-one, and the case in which the decoder's own argument-level complaint would
    ///     name the wrong parameter — it reports the width rather than the origin. Validating the
    ///     region here rather than re-interpreting the decoder's exception is what lets the
    ///     refusal describe the mistake the model actually made.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_OriginAtTheImageWidth_IsRefusedNamingTheDimensions()
    {
        // Arrange: a permitted image of a known size
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(20, 14));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: an origin one past the last addressable column
        var result = await InvokeAsync(tool, file, 20, 0, 1, 1);

        // Assert: refused, naming the real size
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("20x14 pixels", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an origin sitting exactly at the image's height is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_OriginAtTheImageHeight_IsRefusedNamingTheDimensions()
    {
        // Arrange: a permitted image of a known size
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(20, 14));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: an origin one past the last addressable row
        var result = await InvokeAsync(tool, file, 0, 14, 1, 1);

        // Assert: refused, naming the real size
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("20x14 pixels", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a region lying wholly outside the image is refused, naming the real size.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_RegionEntirelyOutsideTheImage_IsRefusedNamingTheDimensions()
    {
        // Arrange: a permitted image of a known size
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(20, 14));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: a region that shares no pixel with the image at all
        var result = await InvokeAsync(tool, file, 500, 500, 10, 10);

        // Assert: refused, and told where the image actually is
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("20x14 pixels", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an out-of-bounds region is refused rather than quietly reduced to a valid one.
    /// </summary>
    /// <remarks>
    ///     The explicit anti-clamping proof. Clamping would return a different region from the
    ///     one asked about while reporting success, and the model has no way to detect the
    ///     substitution: it would then describe, confidently, a part of the picture it never
    ///     received. Asserting the result is a refusal and specifically <em>not</em> content is
    ///     what pins that.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_OutOfBoundsRegion_IsNotClamped()
    {
        // Arrange: a permitted image of a known size
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(20, 14));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region that overflows both axes
        var result = await InvokeAsync(tool, file, 10, 10, 40, 40);

        // Assert: a refusal, and specifically not content clamped to what would have fitted
        Assert.IsType<string>(result);
        Assert.IsNotType<List<AIContent>>(result);
    }

    /// <summary>
    ///     Proves a negative origin is refused, stating the coordinate convention.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public async Task ImageCropTool_Crop_NegativeOrigin_ReturnsDenial(int x, int y)
    {
        // Arrange: a permitted image, so only the request can be at fault
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(20, 14));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region whose origin is behind the image's top-left corner
        var result = await InvokeAsync(tool, file, x, y, 4, 4);

        // Assert: refused, and told the convention it cannot observe from the picture itself
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("top-left", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a region with no extent is refused.
    /// </summary>
    /// <remarks>
    ///     An empty region is a self-contradictory request rather than a resource problem, so the
    ///     reason asserted is the malformed-request one and not the too-large one.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-3, 4)]
    public async Task ImageCropTool_Crop_NonPositiveExtent_ReturnsDenial(int width, int height)
    {
        // Arrange: a permitted image, so only the request can be at fault
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(20, 14));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region with no area
        var result = await InvokeAsync(tool, file, 1, 1, width, height);

        // Assert: refused as a malformed request
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("greater than zero", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves omitting the region produces a refusal rather than a framework error.
    /// </summary>
    /// <remarks>
    ///     The scenario that pins every region parameter as optional. A parameter with no default
    ///     is required by the function factory, and an omitted argument then fails inside the
    ///     factory before this tool is reached, leaving the model an opaque framework error
    ///     rather than a refusal naming what to supply.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_MissingRegionArguments_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a permitted image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.png", ImageTestImages.Png(20, 14));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: supply a path and nothing else
        var result = await tool.InvokeAsync(
            new AIFunctionArguments { ["path"] = file },
            TestContext.Current.CancellationToken);

        // Assert: a refusal naming the four values to supply
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("x, y, width and height", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves omitting the path produces a refusal rather than a framework error.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_MissingPathArgument_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a workspace-governed tool, so only the request is at fault
        using var fixture = new TempDirectoryFixture();
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

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
    ///     Proves an empty or whitespace-only path is refused rather than throwing at the model.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImageCropTool_Crop_BlankPath_ReturnsDenialWithoutThrowing(string path)
    {
        // Arrange: a tool governed by an unrestricted policy, so only the request is at fault
        var tool = ImageCropTool.Create(
            new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]));

        // Act: invoke with a path that names nothing
        var result = await InvokeAsync(tool, path, 0, 0, 1, 1);

        // Assert: a returned refusal, because an exception would end the agent's turn
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an image declaring more pixels than the decode budget is refused, from its
    ///     header alone.
    /// </summary>
    /// <remarks>
    ///     <b>Both axes of this fixture are inside the largest extent the decoder accepts; only
    ///     their product is outside the budget.</b> That is the case a per-axis bound alone
    ///     cannot catch, and it is the reason the budget is a pixel count rather than a
    ///     dimension. The fixture carries <em>no pixel data at all</em>, so a tool that decoded
    ///     before triaging would produce the undecodable refusal instead — which is how "decided
    ///     from the declared dimensions alone" is asserted rather than assumed.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_HeaderDeclaringMorePixelsThanTheDecodeBudget_IsRefusedNamingTheBudget()
    {
        // Arrange: a header declaring 64 million pixels, and no pixel data whatsoever
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "bomb.png", ImageTestImages.PngHeaderOnly(8000, 8000));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a small region of it
        var result = await InvokeAsync(tool, file, 0, 0, 10, 10);

        // Assert: refused for its size, naming both bounds, and not for being undecodable
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("8000x8000 pixels", text, StringComparison.Ordinal);
        Assert.Contains("8192 pixels on a side", text, StringComparison.Ordinal);
        Assert.Contains("16777216 pixels in total", text, StringComparison.Ordinal);
        Assert.DoesNotContain("could not be decoded", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an image declaring an oversized single axis is refused, even when its pixel
    ///     product is within the budget.
    /// </summary>
    /// <remarks>
    ///     The mirror of the scenario above: 9000 by 10 is ninety thousand pixels, far inside the
    ///     budget, but one axis exceeds the largest extent the decoder will allocate. Proves both
    ///     bounds are checked rather than only the product.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_HeaderDeclaringAnOversizedAxis_IsRefusedNamingTheBounds()
    {
        // Arrange: a header declaring a very wide, very short image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "wide.png", ImageTestImages.PngHeaderOnly(9000, 10));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a small region of it
        var result = await InvokeAsync(tool, file, 0, 0, 5, 5);

        // Assert: refused for the axis, with both bounds named
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("9000x10 pixels", text, StringComparison.Ordinal);
        Assert.Contains("8192 pixels on a side", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the decode budget is estimated from a fixed four bytes per pixel rather than
    ///     from what a file declares about its own channels.
    /// </summary>
    /// <remarks>
    ///     <b>The regression guard for the single most likely wrong simplification here.</b> A
    ///     header reader reports a palette-indexed file as carrying <em>one</em> channel per
    ///     pixel, because that is what the file stores — while decoding it produces four bytes
    ///     per pixel, because every index is resolved into RGBA. A budget computed as
    ///     width × height × channels would therefore under-count this file by a factor of four
    ///     and accept it. Palette-indexed content is also the format that compresses best, so it
    ///     is exactly what a hostile caller would reach for. The image differs from the
    ///     truecolor bomb above only in its declared color type, so nothing but the estimate can
    ///     be what changes the answer.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_PalettizedPngNearThePixelCeiling_IsRefusedNamingTheBudget()
    {
        // Arrange: the same oversized declaration, as a palette-indexed file
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(
            fixture.Root,
            "indexed-bomb.png",
            ImageTestImages.PngHeaderOnly(8000, 8000, colorType: 3));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a small region of it
        var result = await InvokeAsync(tool, file, 0, 0, 10, 10);

        // Assert: refused for its size, exactly as the truecolor declaration was
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("8000x8000 pixels", text, StringComparison.Ordinal);
        Assert.Contains("16777216 pixels in total", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a palette-indexed image within the budget is cropped successfully.
    /// </summary>
    /// <remarks>
    ///     Pins the decoder's palette resolution: the fixture's pixel at the requested origin is
    ///     palette entry two, opaque blue, and the returned region's first pixel must be that
    ///     color rather than a raw index.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_PalettizedPng_ReturnsCroppedImageContent()
    {
        // Arrange: a permitted palette-indexed PNG whose indices cycle with the coordinate
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "indexed.png", ImageTestImages.PalettizedPng(12, 8));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: take a region whose first pixel is index (1 + 1) % 4 == 2, opaque blue
        var result = await InvokeAsync(tool, file, 1, 1, 4, 4);

        // Assert: the region comes back with its indices resolved through the palette
        var content = Assert.IsType<List<AIContent>>(result);
        var data = Assert.IsType<DataContent>(content[1]);
        var decoded = ImageTestImages.Decode(data.Data.ToArray());

        Assert.Equal(4, decoded.Width);
        Assert.Equal(4, decoded.Height);
        Assert.Equal(0, decoded[0, 0].R);
        Assert.Equal(0, decoded[0, 0].G);
        Assert.Equal(255, decoded[0, 0].B);
        Assert.Equal(255, decoded[0, 0].A);
    }

    /// <summary>
    ///     Proves a file whose header reads but whose body does not is refused, with the declared
    ///     size stated.
    /// </summary>
    /// <remarks>
    ///     The distinction that makes the refusal useful: the size <em>was</em> learned, so it is
    ///     stated, which tells the model the file is damaged rather than of the wrong kind.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_TruncatedPngBody_ReturnsDenialNamingTheDeclaredDimensions()
    {
        // Arrange: a permitted PNG whose header is sound and whose pixel data stops part-way
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "cut.png", ImageTestImages.TruncatedBodyPng(24, 18));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region well inside the declared bounds
        var result = await InvokeAsync(tool, file, 2, 2, 4, 4);

        // Assert: refused as undecodable, stating the size the header did give up
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("24x18 pixels", text, StringComparison.Ordinal);
        Assert.Contains("could not be decoded", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file whose extension disagrees with its content is refused without any size
    ///     being stated.
    /// </summary>
    /// <remarks>
    ///     Nothing was read, so nothing is claimed — the counterpart to the truncated-body
    ///     scenario. Also asserts that no text from the decoding library reaches the model: its
    ///     messages are developer-facing and may echo values read out of the file.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_FileWhoseExtensionLiesAboutItsContent_ReturnsDenialWithoutDimensions()
    {
        // Arrange: JPEG bytes in a file named .png
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "liar.png", ImageTestImages.Jpeg(24, 16));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region of it
        var result = await InvokeAsync(tool, file, 0, 0, 4, 4);

        // Assert: refused as unreadable, claiming no size and echoing no library text
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("not readable as image/png content", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d+x\d+", text);
        Assert.DoesNotContain("PNG signature", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SOI marker", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Proves an interlaced PNG is refused clearly, naming the reason and the declared size.
    /// </summary>
    /// <remarks>
    ///     Interlacing is the one thing a well-formed, specification-conforming file of this type
    ///     can declare that this tool will not decode, so the file is not damaged and must not be
    ///     described as though it were. The refusal names the true reason and hands over the
    ///     declared size directly — rather than naming the read tool, which would be a route to
    ///     the content this refusal withheld rather than a statement of what the file is.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_Adam7InterlacedPng_IsRefusedNamingTheDimensions()
    {
        // Arrange: a permitted PNG declaring Adam7 interlacing
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "woven.png", ImageTestImages.Adam7InterlacedPng(64, 32));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region of it
        var result = await InvokeAsync(tool, file, 4, 4, 8, 8);

        // Assert: refused for interlacing specifically, with the size stated and no tool named
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("Adam7 interlacing", text, StringComparison.Ordinal);
        Assert.Contains("64x32 pixels", text, StringComparison.Ordinal);
        Assert.DoesNotContain(ImageReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an empty file is refused without throwing.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_ZeroByteFile_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a permitted file with no content at all
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "empty.png", []);
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region of it
        var result = await InvokeAsync(tool, file, 0, 0, 1, 1);

        // Assert: a returned refusal rather than an exception from the decoder
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a path outside the permitted read location is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_PathOutsideTheReadRoot_ReturnsDenial()
    {
        // Arrange: an image in a sibling directory no grant permits reading
        using var fixture = new TempDirectoryFixture();
        var outsideFile = WriteBytes(fixture.Outside, "secret.png", ImageTestImages.Png(20, 20));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: request a region of the file outside the permitted location
        var result = await InvokeAsync(tool, outsideFile, 0, 0, 4, 4);

        // Assert: refused before anything is learned about the file
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a policy refusal discloses the permitted location.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_DeniedPath_DenialDisclosesPermittedLocation()
    {
        // Arrange: a file outside the permitted location
        using var fixture = new TempDirectoryFixture();
        var outsideFile = WriteBytes(fixture.Outside, "secret.png", ImageTestImages.Png(20, 20));
        var policy = RootedPolicy(fixture.Root);
        var tool = ImageCropTool.Create(policy);

        // Act: request the refused file
        var result = await InvokeAsync(tool, outsideFile, 0, 0, 4, 4);

        // Assert: the request is echoed and the permitted location is named with its level
        var text = Assert.IsType<string>(result);
        Assert.Contains(outsideFile, text, StringComparison.Ordinal);
        Assert.Contains(policy.WorkingDirectory, text, StringComparison.Ordinal);
        Assert.Contains("(read-write)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a directory is refused as a malformed request.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_DirectoryPath_ReturnsDenial()
    {
        // Arrange: a permitted directory rather than a file
        using var fixture = new TempDirectoryFixture();
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: request a region of the directory itself
        var result = await InvokeAsync(tool, fixture.Root, 0, 0, 4, 4);

        // Assert: refused as malformed, stating the fact and prescribing nothing
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("The requested path is a directory, not a file.", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing file of a croppable type is refused as not found.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_MissingFile_ReturnsDenial()
    {
        // Arrange: a permitted location containing no such file
        using var fixture = new TempDirectoryFixture();
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: request a region of a croppable-type file that does not exist
        var result = await InvokeAsync(tool, Path.Combine(fixture.Root, "absent.png"), 0, 0, 4, 4);

        // Assert: refused as not found, proving the type is judged before existence
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.Contains("The requested file does not exist.", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file beyond the binary ceiling is refused before anything is parsed.
    /// </summary>
    /// <remarks>
    ///     The fixture is a real image, so a tool that parsed before checking the ceiling would
    ///     succeed rather than fail; asserting the ceiling's refusal is therefore evidence the
    ///     size was judged first.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_FileLargerThanTheBinaryCeiling_ReturnsDenialNamingTheCeiling()
    {
        // Arrange: a real image, and a binary ceiling far below its size
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "big.png", ImageTestImages.Png(64, 64));
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root, new ToolLimits(maxBinaryBytes: 8)));

        // Act: ask for a small region of it
        var result = await InvokeAsync(tool, file, 0, 0, 4, 4);

        // Assert: a refusal naming the ceiling
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("8-byte binary limit", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an encoded region larger than the binary ceiling is refused.
    /// </summary>
    /// <remarks>
    ///     A real case rather than a theoretical one: a region re-encoded losslessly can exceed a
    ///     ceiling the source file sat well inside. The ceiling here is chosen to admit the
    ///     source file and refuse the result, so only the second check can be what fires.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_EncodedRegionBeyondTheBinaryCeiling_ReturnsDenialNamingTheCeiling()
    {
        // Arrange: a source comfortably inside a ceiling its lossless re-encoding will exceed
        using var fixture = new TempDirectoryFixture();
        var sourceBytes = ImageTestImages.Jpeg(96, 96);
        var file = WriteBytes(fixture.Root, "photo.jpg", sourceBytes);
        var ceiling = sourceBytes.Length + 64;
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root, new ToolLimits(maxBinaryBytes: ceiling)));

        // Act: ask for the whole image, whose PNG encoding is far larger than the JPEG source
        var result = await InvokeAsync(tool, file, 0, 0, 96, 96);

        // Assert: a refusal naming the ceiling and the size the result would have been
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("The cropped image is", text, StringComparison.Ordinal);
        Assert.Contains("binary limit", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an animated raster file is refused, naming the types a region can be taken from.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_GifFile_ReturnsDenialNamingTheCroppableTypes()
    {
        // Arrange: a permitted .gif, which the family reads but cannot cut
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "animation.gif", [0x47, 0x49, 0x46, 0x38]);
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region of it
        var result = await InvokeAsync(tool, file, 0, 0, 4, 4);

        // Assert: refused, stating what the format is and what may be cropped instead
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("animated raster image", text, StringComparison.Ordinal);
        Assert.Contains("png, jpg and jpeg", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the animated-raster refusal names no sibling tool.
    /// </summary>
    /// <remarks>
    ///     The explicit no-redirect assertion. A <c>.gif</c> genuinely is an image, so naming the
    ///     read tool would not classify the file — it would offer a route to the content this
    ///     refusal withheld, handing the model a whole image after it asked to examine one part
    ///     closely. A model describing that part afterwards would be describing what it never
    ///     examined.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_GifDenial_NamesNoSiblingTool()
    {
        // Arrange: a permitted .gif
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "animation.gif", [0x47, 0x49, 0x46, 0x38]);
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region of it
        var result = await InvokeAsync(tool, file, 0, 0, 4, 4);

        // Assert: no tool name appears anywhere in the refusal
        var text = Assert.IsType<string>(result);
        Assert.DoesNotContain(ImageReadTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain(TextFileReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a WebP file is refused, naming the types a region can be taken from.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_WebpFile_ReturnsDenialNamingTheCroppableTypes()
    {
        // Arrange: a permitted .webp, which the family reads but does not decode
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "picture.webp", [0x52, 0x49, 0x46, 0x46]);
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region of it
        var result = await InvokeAsync(tool, file, 0, 0, 4, 4);

        // Assert: refused with its own wording, naming the croppable set and no tool
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("does not decode", text, StringComparison.Ordinal);
        Assert.Contains("png, jpg and jpeg", text, StringComparison.Ordinal);
        Assert.DoesNotContain(ImageReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a PDF is refused with a statement that rasterization would be required.
    /// </summary>
    /// <remarks>
    ///     A PDF is the one admitted type that looks croppable and is not, so without the reason
    ///     stated a model would reasonably retry with different coordinates.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_PdfFile_ReturnsDenialStatingRasterizationIsRequired()
    {
        // Arrange: a permitted .pdf
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "document.pdf", [0x25, 0x50, 0x44, 0x46]);
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region of it
        var result = await InvokeAsync(tool, file, 0, 0, 4, 4);

        // Assert: refused, naming the page-and-resolution choice no tool here makes
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("paginated document", text, StringComparison.Ordinal);
        Assert.Contains("rasterize", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an svg is refused exactly as the read tool refuses it.
    /// </summary>
    /// <remarks>
    ///     The regression guard for the family giving one answer to "what is this file". An
    ///     <c>.svg</c> genuinely is text, so naming the text reader is a classification rather
    ///     than a way around the refusal — and this tool delegates to the same map rather than
    ///     inventing its own answer.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_SvgFile_ReturnsDenialRedirectingToTextFileRead()
    {
        // Arrange: a permitted .svg
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.svg", [0x3C, 0x73, 0x76, 0x67]);
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region of it
        var result = await InvokeAsync(tool, file, 0, 0, 4, 4);

        // Assert: the same refusal the read tool composes, naming the reader for that content
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("is text and vector content", text, StringComparison.Ordinal);
        Assert.Contains(TextFileReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an extension the family cannot read at all is refused without a redirect.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageCropTool_Crop_UnknownExtension_ReturnsDenialWithoutRedirect()
    {
        // Arrange: a permitted file of a type the family does not read
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "archive.zip", [0x50, 0x4B, 0x03, 0x04]);
        var tool = ImageCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a region of it
        var result = await InvokeAsync(tool, file, 0, 0, 4, 4);

        // Assert: refused as unsupported, with no tool invented to send the model to
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(TextFileReadTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain(ImageReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates a policy permitting reads and writes only beneath one workspace, and
    ///     interpreting relative requests against it.
    /// </summary>
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
    /// <param name="directory">The directory to write into; created when it does not exist.</param>
    /// <param name="fileName">The name of the file to write.</param>
    /// <param name="content">The bytes to write.</param>
    /// <returns>The full path of the written file.</returns>
    private static string WriteBytes(string directory, string fileName, byte[] content)
    {
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, fileName);
        System.IO.File.WriteAllBytes(filePath, content);
        return filePath;
    }

    /// <summary>
    ///     Invokes the crop tool exactly as a runtime would.
    /// </summary>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="path">The path argument to supply.</param>
    /// <param name="x">The region's left edge.</param>
    /// <param name="y">The region's top edge.</param>
    /// <param name="width">The region's width.</param>
    /// <param name="height">The region's height.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeAsync(
        AIFunction tool,
        string path,
        int x,
        int y,
        int width,
        int height)
    {
        return await tool.InvokeAsync(
            new AIFunctionArguments
            {
                ["path"] = path,
                ["x"] = x,
                ["y"] = y,
                ["width"] = width,
                ["height"] = height
            },
            TestContext.Current.CancellationToken);
    }
}
