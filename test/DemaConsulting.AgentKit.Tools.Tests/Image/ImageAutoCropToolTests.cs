using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Image;
using DemaConsulting.AgentKit.Tools.Tests.TextFile;
using DemaConsulting.AgentKit.Tools.TextFile;
using DemaConsulting.CanvasNet.Canvas;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Tests.Image;

/// <summary>
///     Unit tests for the <see cref="ImageAutoCropTool"/> class.
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
///     built by the test through <see cref="ImageTestImages"/>, which states each fixture's
///     background, its content block and its noise in code — so the region a correct trim must
///     produce is known by arithmetic before the library is asked for it.
///     </para>
///     <para>
///     <b>No fixture in this file is white on white.</b> Every background is dark, colored,
///     transparent or dithered, so a tool that assumed the background rather than sampling it
///     fails here rather than passing by coincidence.
///     </para>
/// </remarks>
public class ImageAutoCropToolTests
{
    /// <summary>
    ///     The dark background a screenshot of a dark-themed application has.
    /// </summary>
    private static readonly Rgba32 DarkBackground = new(32, 32, 40, 255);

    /// <summary>
    ///     A saturated background no plausible default could coincide with.
    /// </summary>
    private static readonly Rgba32 ColoredBackground = new(180, 60, 200, 255);

    /// <summary>
    ///     The fully transparent background an export with an alpha channel has.
    /// </summary>
    private static readonly Rgba32 TransparentBackground = new(0, 0, 0, 0);

    /// <summary>
    ///     The content color the solid-block fixtures are drawn in.
    /// </summary>
    private static readonly Rgba32 ContentColor = new(250, 250, 250, 255);

    /// <summary>
    ///     The content block every fixture that sits clear of the edges places its content in.
    /// </summary>
    private static readonly ImageTestImages.Rectangle CenteredContent = new(12, 9, 10, 8);

    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void ImageAutoCropTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        // Arrange / Act: read the published constant
        var name = ImageAutoCropTool.ToolName;

        // Assert: the name is qualified by the family prefix the pack claims
        Assert.Equal("image_auto_crop", name);
        Assert.StartsWith(ImagePack.FamilyPrefix + "_", name, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the constructed tool carries the published name and a description a model can
    ///     choose by.
    /// </summary>
    /// <remarks>
    ///     The description must say that the caller names no region, because that is the only
    ///     thing distinguishing this tool from its sibling, and a model given two region tools
    ///     with interchangeable descriptions will reach for whichever it read first.
    /// </remarks>
    [Fact]
    public void ImageAutoCropTool_Create_ConstructedTool_CarriesTheToolNameAndADescription()
    {
        // Arrange: a policy governing an otherwise irrelevant location
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: construct the tool
        var tool = ImageAutoCropTool.Create(policy);

        // Assert: the published name, and a description stating what the tool decides for itself
        Assert.Equal(ImageAutoCropTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        Assert.Contains("names no region", tool.Description, StringComparison.Ordinal);
        Assert.Contains("border", tool.Description, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing policy is a programming error rather than a denial.
    /// </summary>
    [Fact]
    public void ImageAutoCropTool_Create_NullPolicy_ThrowsArgumentNullException()
    {
        // Arrange / Act / Assert: a tool with no policy cannot be constructed
        Assert.Throws<ArgumentNullException>(() => ImageAutoCropTool.Create(null!));
    }

    /// <summary>
    ///     Proves a trimmed region reaches the caller as real content and not as serialized JSON.
    /// </summary>
    /// <remarks>
    ///     The guarded-delivery guard. Without it the content would arrive as a
    ///     <see cref="JsonElement"/>, the provider would never receive the region, and the model
    ///     would fabricate a description of content it never saw.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_Result_IsContentListNotJsonElement()
    {
        // Arrange: a permitted image with content clear of its edges
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: invoke the tool as the runtime would
        var result = await InvokeAsync(tool, file);

        // Assert: the guard delivered the content list unserialized
        Assert.IsType<List<AIContent>>(result);
        Assert.IsNotType<JsonElement>(result);
    }

    /// <summary>
    ///     Proves a permitted PNG yields its content region as image content.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_PermittedPng_ReturnsTrimmedImageContent()
    {
        // Arrange: a 40x30 image whose content occupies 10x8 pixels in the middle
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: trim it, naming no region at all
        var result = await InvokeAsync(tool, file);

        // Assert: a caption followed by a PNG smaller than the source on both axes
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.Equal(2, content.Count);
        Assert.IsType<TextContent>(content[0]);
        var data = Assert.IsType<DataContent>(content[1]);
        Assert.Equal(ImageMediaTypes.Png, data.MediaType);

        using var decoded = ImageTestImages.Decode(data.Data.ToArray());
        Assert.True(decoded.Width < 40);
        Assert.True(decoded.Height < 30);
    }

    /// <summary>
    ///     Proves the region returned is exactly the content box expanded by the padding.
    /// </summary>
    /// <remarks>
    ///     <b>The arithmetic scenario.</b> The content block is at 12,9 and is 10x8, so the
    ///     content box runs to 21,16; a padding of 8 expands it to 4,1 through 29,24, which is
    ///     26x24 — every edge computed and none of them clamped, because the fixture's margins
    ///     all exceed the padding. Asserting the region rather than merely "something smaller"
    ///     is what makes an off-by-one or a mis-sampled background fail here.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_TrimmedRegion_IsTheContentBoxPlusPadding()
    {
        // Arrange: content at 12,9 sized 10x8, in a 40x30 image with margins wider than 8
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: trim with the padding stated explicitly
        var result = await InvokeAsync(tool, file, padding: 8);

        // Assert: the content box 12,9-21,16 grown by 8 on every side, unclamped
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.Contains("4,1 26x24", Assert.IsType<TextContent>(content[0]).Text, StringComparison.Ordinal);

        using var decoded = ImageTestImages.Decode(Assert.IsType<DataContent>(content[1]).Data.ToArray());
        Assert.Equal(26, decoded.Width);
        Assert.Equal(24, decoded.Height);
    }

    /// <summary>
    ///     Proves the caption states the region, the padding and the source's dimensions.
    /// </summary>
    /// <remarks>
    ///     Each of the three is load-bearing, and more so here than for a caller-named region:
    ///     the model named no region, so the caption is the only way it can learn where in the
    ///     picture it is now looking; the source's dimensions let it decide whether the trim
    ///     achieved anything; and the padding is the one input that moved the answer, so a model
    ///     that wants a tighter result knows which value to change.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_Caption_StatesTheRegionThePaddingAndTheSourceDimensions()
    {
        // Arrange: a 40x30 image with content clear of its edges
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: trim with a padding distinguishable from the default
        var result = await InvokeAsync(tool, file, padding: 2);

        // Assert: the region it chose, the padding it applied, and the space it chose it in
        var content = Assert.IsType<List<AIContent>>(result);
        var caption = Assert.IsType<TextContent>(content[0]).Text;
        Assert.Contains("10,7 14x12", caption, StringComparison.Ordinal);
        Assert.Contains("2 pixels of padding", caption, StringComparison.Ordinal);
        Assert.Contains("40x30", caption, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a bare file name is resolved against the workspace.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_BareFileName_ResolvesAgainstTheWorkspace()
    {
        // Arrange: a workspace holding one image
        using var fixture = new TempDirectoryFixture();
        WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: name it by file name alone, as a model writes it
        var result = await InvokeAsync(tool, "diagram.png");

        // Assert: the region comes back rather than a refusal
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.IsType<DataContent>(content[1]);
    }

    /// <summary>
    ///     Proves a dark background is trimmed from the image's own border rather than from an
    ///     assumed white.
    /// </summary>
    /// <remarks>
    ///     <b>The scenario a white-assuming implementation cannot pass.</b> The margin is nearly
    ///     black and the content is nearly white, so a tool comparing against white would
    ///     classify the margin as content, find the content box to be the whole image, and return
    ///     the picture unchanged. The assertion is the exact region, so returning the whole image
    ///     is a visible failure rather than a silently weaker answer.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_DarkBackground_IsTrimmedFromTheBorderNotFromWhite()
    {
        // Arrange: near-white content on a near-black margin
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "dark.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: trim to the tight content box, so nothing but the classification decides the size
        var result = await InvokeAsync(tool, file, padding: 0);

        // Assert: exactly the content block, not the whole image
        var content = Assert.IsType<List<AIContent>>(result);
        using var decoded = ImageTestImages.Decode(Assert.IsType<DataContent>(content[1]).Data.ToArray());
        Assert.Equal(10, decoded.Width);
        Assert.Equal(8, decoded.Height);
    }

    /// <summary>
    ///     Proves a saturated colored background is trimmed from the border rather than from an
    ///     assumed white.
    /// </summary>
    /// <remarks>
    ///     The second half of the same proof, in the direction a dark background cannot reach: a
    ///     violet margin is far from white and far from black, so neither assumption survives it.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_ColoredBackground_IsTrimmedFromTheBorderNotFromWhite()
    {
        // Arrange: near-white content on a violet margin
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "slide.png", CenteredPng(ColoredBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: trim to the tight content box
        var result = await InvokeAsync(tool, file, padding: 0);

        // Assert: exactly the content block
        var content = Assert.IsType<List<AIContent>>(result);
        using var decoded = ImageTestImages.Decode(Assert.IsType<DataContent>(content[1]).Data.ToArray());
        Assert.Equal(10, decoded.Width);
        Assert.Equal(8, decoded.Height);
    }

    /// <summary>
    ///     Proves a fully transparent margin is trimmed, with the alpha channel deciding it.
    /// </summary>
    /// <remarks>
    ///     <b>The scenario that makes alpha's participation falsifiable.</b> The margin and the
    ///     content differ by 255 in alpha; a classifier comparing only red, green and blue would
    ///     still separate them here, so the fixture goes further — the content is opaque and the
    ///     margin's own red, green and blue are zero, which is what a transparent export commonly
    ///     carries. A tool ignoring alpha on a margin whose color channels were arbitrary would
    ///     read the margin as content; including alpha is what makes the trim work on the most
    ///     common transparent export there is.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_TransparentBackground_IsTrimmedWithAlphaParticipating()
    {
        // Arrange: an opaque content block on a fully transparent margin
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "export.png", CenteredPng(TransparentBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: trim to the tight content box
        var result = await InvokeAsync(tool, file, padding: 0);

        // Assert: exactly the content block, and its alpha survived the round trip
        var content = Assert.IsType<List<AIContent>>(result);
        using var decoded = ImageTestImages.Decode(Assert.IsType<DataContent>(content[1]).Data.ToArray());
        Assert.Equal(10, decoded.Width);
        Assert.Equal(8, decoded.Height);
        Assert.Equal(255, decoded[0, 0].A);
    }

    /// <summary>
    ///     Proves a border carrying a minority of content pixels still yields the background.
    /// </summary>
    /// <remarks>
    ///     <b>The scenario that rules out a corner sample and a mean.</b> The content runs to the
    ///     image's left and top edges, so seventeen of the border's pixels are content — including
    ///     the top-left corner, which a corner-sampling implementation would take as its
    ///     background and then classify the entire margin as content. A mean would land between
    ///     the two colors, matching nothing in the image, and would fail the same way. The mode
    ///     is unmoved by a minority, which is why the answer here is the content block.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_BorderWithAMinorityOfContentPixels_StillSamplesTheBackground()
    {
        // Arrange: content occupying the top-left corner, so part of the border is content
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(
            fixture.Root,
            "flush.png",
            ImageTestImages.ContentOnBackgroundPng(
                40, 30, DarkBackground, new ImageTestImages.Rectangle(0, 0, 10, 8), ContentColor));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: trim to the tight content box
        var result = await InvokeAsync(tool, file, padding: 0);

        // Assert: the content block, which only a modal background sample can produce
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.Contains("0,0 10x8", Assert.IsType<TextContent>(content[0]).Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves two border colors of equal frequency are separated by which reached that count
    ///     first, not by which appeared first.
    /// </summary>
    /// <remarks>
    ///     <b>The scenario that pins the tie-break.</b> The rule matters because the scan order
    ///     is part of the contract, and a tie is the one case where the order alone decides the
    ///     answer. The incumbent is displaced only on a <em>strictly greater</em> count, so a
    ///     value that appears first but is overtaken never regains the lead by drawing level: the
    ///     fixture's border reads
    ///     <c>first × 5, second × 5, second × 3, first × 3</c> and ends eight to eight, with the
    ///     second color chosen. The two possible answers differ in the region reported —
    ///     <c>0,0 5x4</c> against <c>0,1 5x4</c> — so the scenario fails rather than passes
    ///     vacuously if the comparison is ever relaxed to greater-or-equal.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_BorderColorsOfEqualFrequency_ResolveToTheOneThatReachedTheCountFirst()
    {
        // Arrange: a border holding two colors eight pixels each, the first-seen one losing
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(
            fixture.Root,
            "tied.png",
            ImageTestImages.TiedBorderPng(ColoredBackground, DarkBackground, ContentColor));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: trim to the tight content box, so the region is exactly the background's decision
        var result = await InvokeAsync(tool, file, padding: 0);

        // Assert: the box the second color produces, which is not the box the first would
        var content = Assert.IsType<List<AIContent>>(result);
        var caption = Assert.IsType<TextContent>(content[0]).Text;
        Assert.Contains("0,0 5x4", caption, StringComparison.Ordinal);
        Assert.DoesNotContain("0,1 5x4", caption, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a dithered background is still trimmed to the content.
    /// </summary>
    /// <remarks>
    ///     <b>The scenario that makes the tolerance load-bearing.</b> No two background pixels are
    ///     reliably the same exact color, so a tool tolerating nothing classifies almost every
    ///     background pixel as content, the box becomes the whole image and the trim is defeated
    ///     entirely — the stated failure the tolerance exists to prevent. Every dithered value
    ///     stays within three of the stated background, so the largest gap between any two is
    ///     six, inside the tolerance, which is why the correct answer is the content block.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_DitheredBackground_IsStillTrimmedToTheContent()
    {
        // Arrange: a background dithered across the whole image, border included
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(
            fixture.Root,
            "noisy.png",
            ImageTestImages.DitheredBackgroundPng(
                40, 30, new Rgba32(240, 240, 240, 255), CenteredContent, new Rgba32(0, 0, 0, 255)));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: trim to the tight content box
        var result = await InvokeAsync(tool, file, padding: 0);

        // Assert: the content block, which only a tolerant classifier can produce
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.Contains("12,9 10x8", Assert.IsType<TextContent>(content[0]).Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an anti-aliased edge does not survive as a halo around the trimmed region.
    /// </summary>
    /// <remarks>
    ///     <b>The other half of the tolerance's proof, and the discriminating one.</b> The ring
    ///     around the content block differs from the background by four counts per channel —
    ///     inside the tolerance and outside zero. A tool tolerating nothing therefore returns a
    ///     12x10 region, one pixel larger on every side; a tool tolerating eight returns the
    ///     10x8 block. The two answers are different sizes, so the assertion cannot be satisfied
    ///     by both.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_AntiAliasedContentEdge_DoesNotRetainTheHalo()
    {
        // Arrange: a content block wrapped in a one-pixel ring nudged toward the content
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(
            fixture.Root,
            "glyph.png",
            ImageTestImages.AntiAliasedContentPng(
                40, 30, DarkBackground, CenteredContent, ContentColor));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: trim to the tight content box, so only the classification decides the size
        var result = await InvokeAsync(tool, file, padding: 0);

        // Assert: the block itself, not the block plus its halo
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.Contains("12,9 10x8", Assert.IsType<TextContent>(content[0]).Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a JPEG source is trimmed despite the ringing its compression leaves at a hard
    ///     edge.
    /// </summary>
    /// <remarks>
    ///     The assertion is deliberately the robust pair rather than an exact rectangle — the
    ///     region is strictly smaller than the source on both axes, and is at least as large as
    ///     the content block it must wholly contain. The encoder is deterministic, so an exact
    ///     rectangle would pass today; it would also make this scenario fail on an encoder
    ///     revision that changed nothing about the capability being demonstrated.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_JpegSource_IsTrimmedDespiteCompressionArtifacts()
    {
        // Arrange: a 64x48 JPEG whose 24x16 content block rings against a flat field
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(
            fixture.Root,
            "photo.jpg",
            ImageTestImages.ContentOnBackgroundJpeg(
                64,
                48,
                new Rgba32(240, 240, 240, 255),
                new ImageTestImages.Rectangle(20, 16, 24, 16),
                new Rgba32(20, 20, 20, 255)));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: trim to the tight content box
        var result = await InvokeAsync(tool, file, padding: 0);

        // Assert: genuinely trimmed on both axes, and no smaller than the content it must hold
        var content = Assert.IsType<List<AIContent>>(result);
        var data = Assert.IsType<DataContent>(content[1]);
        Assert.Equal(ImageMediaTypes.Png, data.MediaType);

        using var decoded = ImageTestImages.Decode(data.Data.ToArray());
        Assert.True(decoded.Width < 64, "the region should be narrower than the source");
        Assert.True(decoded.Height < 48, "the region should be shorter than the source");
        Assert.True(decoded.Width >= 24, "the region should contain the content block");
        Assert.True(decoded.Height >= 16, "the region should contain the content block");
    }

    /// <summary>
    ///     Proves padding that would run past the left and top edges is clamped, not refused.
    /// </summary>
    /// <remarks>
    ///     Content flush to an edge is ordinary rather than a fault, so the padding simply stops
    ///     at the edge. The content box is 0,0-9,7 and a padding of 8 would reach -8,-8, so the
    ///     region starts at 0,0 and runs to 17,15 — asymmetric padding, which is the honest
    ///     answer and the one an implementation that refused, or one that produced a negative
    ///     origin, cannot give.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_ContentFlushToTheLeftAndTopEdges_ClampsThePadding()
    {
        // Arrange: content occupying the top-left corner of a 40x30 image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(
            fixture.Root,
            "flush.png",
            ImageTestImages.ContentOnBackgroundPng(
                40, 30, DarkBackground, new ImageTestImages.Rectangle(0, 0, 10, 8), ContentColor));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for padding the left and top margins cannot supply
        var result = await InvokeAsync(tool, file, padding: 8);

        // Assert: clamped to the edge rather than refused, and never a negative origin
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.Contains("0,0 18x16", Assert.IsType<TextContent>(content[0]).Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves padding that would run past the right and bottom edges is clamped, not refused.
    /// </summary>
    /// <remarks>
    ///     The mirror of the scenario above, on the axes the first cannot reach: an implementation
    ///     clamping only at zero would pass that one and fail this one by asking the decoder for
    ///     a region extending past the image.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_ContentFlushToTheRightAndBottomEdges_ClampsThePadding()
    {
        // Arrange: content occupying the bottom-right corner of a 40x30 image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(
            fixture.Root,
            "flush.png",
            ImageTestImages.ContentOnBackgroundPng(
                40, 30, DarkBackground, new ImageTestImages.Rectangle(30, 22, 10, 8), ContentColor));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for padding the right and bottom margins cannot supply
        var result = await InvokeAsync(tool, file, padding: 8);

        // Assert: clamped to the far edges rather than refused
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.Contains("22,14 18x16", Assert.IsType<TextContent>(content[0]).Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a padding larger than every margin yields the whole image.
    /// </summary>
    /// <remarks>
    ///     A truthful and predictable answer rather than a refusal: the caller asked for more air
    ///     than the picture has, and the picture is what it gets. Asserting the stated bound
    ///     itself also pins that the bound is accepted rather than refused.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_PaddingLargerThanEveryMargin_ReturnsTheWholeImage()
    {
        // Arrange: a 40x30 image with content clear of its edges
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for the largest padding the tool accepts
        var result = await InvokeAsync(tool, file, padding: 256);

        // Assert: the whole image, clamped on every side
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.Contains("0,0 40x30", Assert.IsType<TextContent>(content[0]).Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a padding of zero yields the tight content box.
    /// </summary>
    /// <remarks>
    ///     Zero is legal and is not read as "omitted": the content box with nothing added is a
    ///     request a caller placing a figure into a tight layout genuinely makes.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_ZeroPadding_ReturnsTheTightContentBox()
    {
        // Arrange: a 40x30 image whose content is exactly 12,9 sized 10x8
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for no padding at all
        var result = await InvokeAsync(tool, file, padding: 0);

        // Assert: the content box itself, distinguishing zero from the default
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.Contains("12,9 10x8", Assert.IsType<TextContent>(content[0]).Text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an omitted padding applies the default rather than none.
    /// </summary>
    /// <remarks>
    ///     The scenario that distinguishes "omitted" from "zero". Both reach the tool as an
    ///     absent value in the sense that neither is a refusal, but they must produce different
    ///     regions, and the fixture's margins exceed the default so the default is unclamped and
    ///     therefore exactly assertable.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_OmittedPadding_AppliesTheDefault()
    {
        // Arrange: a 40x30 image with margins wider than the default padding
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: supply no padding argument at all
        var result = await InvokeAsync(tool, file);

        // Assert: the default of eight was applied, not zero and not the whole image
        var content = Assert.IsType<List<AIContent>>(result);
        var caption = Assert.IsType<TextContent>(content[0]).Text;
        Assert.Contains("4,1 26x24", caption, StringComparison.Ordinal);
        Assert.Contains("8 pixels of padding", caption, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an image that is entirely background is refused, with the reason named.
    /// </summary>
    /// <remarks>
    ///     There is no honest content box for such an image. Returning the whole picture would
    ///     answer a different question while reporting success — the substitution this family
    ///     exists to refuse — so the refusal names the image's dimensions and says that every
    ///     pixel matched the background sampled from its own border, which is the fact the model
    ///     was missing.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_UniformImage_IsRefusedNamingTheReason()
    {
        // Arrange: an image whose every pixel is the same non-white color
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "blank.png", ImageTestImages.UniformPng(24, 18, DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

        // Assert: refused as a malformed request, naming the size and the reason
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("24x18", text, StringComparison.Ordinal);
        Assert.Contains("no content region to trim to", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an entirely-background image with a destination named writes nothing.
    /// </summary>
    /// <remarks>
    ///     The destination had already passed every one of its governance checks by the time the
    ///     trim ran, so this is what proves the write is genuinely the last step rather than one
    ///     that happens to be skipped. A file left behind would be a figure of nothing that the
    ///     agent was told did not exist.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_UniformImageWithADestination_WritesNothing()
    {
        // Arrange: an entirely-background image and a destination the policy would permit
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "blank.png", ImageTestImages.UniformPng(24, 18, DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));
        var destination = Path.Combine(fixture.Root, "figure.png");

        // Act: ask for it to be trimmed into that destination
        var result = await InvokeAsync(tool, file, destination: destination);

        // Assert: refused, and nothing created
        var text = Assert.IsType<string>(result);
        Assert.Contains("no content region to trim to", text, StringComparison.Ordinal);
        Assert.False(System.IO.File.Exists(destination));
    }

    /// <summary>
    ///     Proves the returned region carries the source image's pixels unaltered.
    /// </summary>
    /// <remarks>
    ///     The demonstrable form of "the pixels were preserved exactly": the content block is
    ///     built with a distinct value in every channel of every pixel, including alpha, and
    ///     every pixel of the returned region is compared against the pixel it was supposed to
    ///     come from. An encoder that quietly discarded alpha, or an origin that was off by one,
    ///     fails here.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_ReturnedRegion_CarriesTheSourcePixelsExactly()
    {
        // Arrange: a distinguishable content block on a transparent margin
        using var fixture = new TempDirectoryFixture();
        var sourceBytes = ImageTestImages.DistinguishableContentOnBackgroundPng(
            40, 30, TransparentBackground, CenteredContent);
        var file = WriteBytes(fixture.Root, "detail.png", sourceBytes);
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));
        using var source = ImageTestImages.Decode(sourceBytes);

        // Act: trim to the tight content box, whose origin is not the image's origin
        var result = await InvokeAsync(tool, file, padding: 0);

        // Assert: every pixel of the region, alpha included, is the source's own
        var content = Assert.IsType<List<AIContent>>(result);
        using var decoded = ImageTestImages.Decode(Assert.IsType<DataContent>(content[1]).Data.ToArray());

        Assert.Equal(10, decoded.Width);
        Assert.Equal(8, decoded.Height);
        for (var row = 0; row < decoded.Height; row++)
        {
            for (var column = 0; column < decoded.Width; column++)
            {
                Assert.Equal(source[12 + column, 9 + row], decoded[column, row]);
            }
        }
    }

    /// <summary>
    ///     Proves a negative padding is refused rather than clamped to zero.
    /// </summary>
    /// <remarks>
    ///     A negative padding would shrink the content box and lose content, which is the one
    ///     outcome this family never produces silently. Clamping it to zero would answer a
    ///     different question while reporting success.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_NegativePadding_ReturnsDenial()
    {
        // Arrange: a permitted image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for a padding that would remove content
        var result = await InvokeAsync(tool, file, padding: -1);

        // Assert: refused as a malformed request, stating what the padding means
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("must not be negative", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a padding beyond the stated bound is refused, naming that bound.
    /// </summary>
    /// <remarks>
    ///     Naming the bound is what turns the refusal into the next correct request. The value
    ///     tested is one past the bound, so the bound itself is proven inclusive by the
    ///     scenario that uses it successfully.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_PaddingBeyondTheBound_ReturnsDenialNamingTheBound()
    {
        // Arrange: a permitted image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for one pixel more padding than the tool accepts
        var result = await InvokeAsync(tool, file, padding: 257);

        // Assert: refused, naming the bound the model must come back under
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("at most 256 pixels", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a permitted destination receives the trimmed region as a PNG file and the
    ///     caller receives a text confirmation.
    /// </summary>
    /// <remarks>
    ///     The capability's second central scenario. The written file is read back and decoded,
    ///     so "a PNG was written" is asserted against decoded pixels rather than against a byte
    ///     count that any bytes at all would have satisfied.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_PermittedDestination_WritesThePngAndConfirmsInText()
    {
        // Arrange: a permitted image and a writable workspace to produce the figure into
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: trim it and name where to put the result
        var result = await InvokeAsync(tool, file, padding: 0, destination: "figure.png");

        // Assert: a text confirmation, and a real PNG of the content block's size on disk
        var text = Assert.IsType<string>(result);
        Assert.Contains("Wrote the trimmed region", text, StringComparison.Ordinal);

        var written = Path.Combine(fixture.Root, "figure.png");
        Assert.True(System.IO.File.Exists(written));

        using var decoded = ImageTestImages.Decode(
            await System.IO.File.ReadAllBytesAsync(written, TestContext.Current.CancellationToken));
        Assert.Equal(10, decoded.Width);
        Assert.Equal(8, decoded.Height);
    }

    /// <summary>
    ///     Proves the written file carries the source image's pixels unaltered.
    /// </summary>
    /// <remarks>
    ///     The written counterpart of the inline pixel-fidelity scenario: a figure that is not
    ///     the region it claims to be is a silent wrong answer, and one that would survive every
    ///     size assertion. Alpha is compared too, so an encoder that quietly discarded it fails
    ///     here.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_WrittenFile_CarriesTheSourcePixelsExactly()
    {
        // Arrange: a distinguishable content block on a transparent margin
        using var fixture = new TempDirectoryFixture();
        var sourceBytes = ImageTestImages.DistinguishableContentOnBackgroundPng(
            40, 30, TransparentBackground, CenteredContent);
        var file = WriteBytes(fixture.Root, "detail.png", sourceBytes);
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));
        using var source = ImageTestImages.Decode(sourceBytes);

        // Act: write the tight content box, whose origin is not the image's origin
        var result = await InvokeAsync(tool, file, padding: 0, destination: "figure.png");

        // Assert: every pixel of the written region, alpha included, is the source's own
        Assert.IsType<string>(result);
        using var decoded = ImageTestImages.Decode(
            await System.IO.File.ReadAllBytesAsync(
                Path.Combine(fixture.Root, "figure.png"), TestContext.Current.CancellationToken));

        Assert.Equal(10, decoded.Width);
        Assert.Equal(8, decoded.Height);
        for (var row = 0; row < decoded.Height; row++)
        {
            for (var column = 0; column < decoded.Width; column++)
            {
                Assert.Equal(source[12 + column, 9 + row], decoded[column, row]);
            }
        }
    }

    /// <summary>
    ///     Proves a written region is not also returned as image content.
    /// </summary>
    /// <remarks>
    ///     What keeps a document-preparation loop affordable: an agent trimming six figures would
    ///     otherwise carry six full-resolution images it no longer needs through every subsequent
    ///     turn. The result is a string, so nothing image-shaped can be hiding in it.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_WithDestination_ReturnsNoImageContent()
    {
        // Arrange: a permitted image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: write the region rather than asking to see it
        var result = await InvokeAsync(tool, file, destination: "figure.png");

        // Assert: a plain confirmation, carrying no content list and no data content
        Assert.IsType<string>(result);
        Assert.IsNotType<List<AIContent>>(result);
    }

    /// <summary>
    ///     Proves the confirmation names the destination, the region and the source's dimensions,
    ///     and reports a workspace destination in the workspace's own dialect.
    /// </summary>
    /// <remarks>
    ///     Each of the three is load-bearing: the destination so the model can reference the file
    ///     it just produced, and the region and the source's dimensions so the model learns what
    ///     the tool decided on its behalf. The fourth assertion is what makes the path dialect a
    ///     real choice rather than a coincidence: a destination inside the anchor comes back
    ///     relative, so the workspace root must appear nowhere in the confirmation.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_Confirmation_NamesTheDestinationRegionAndSourceDimensions()
    {
        // Arrange: a permitted image of a known size
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: write the tight content box, whose coordinates are all distinct
        var result = await InvokeAsync(tool, file, padding: 0, destination: "figure.png");

        // Assert: the name it can hand forward, the region it did not name, and the source size
        var text = Assert.IsType<string>(result);
        Assert.Contains("figure.png", text, StringComparison.Ordinal);
        Assert.Contains("12,9 10x8", text, StringComparison.Ordinal);
        Assert.Contains("40x30", text, StringComparison.Ordinal);

        // Assert: the name is relative to the workspace, so no host layout reaches the model
        Assert.DoesNotContain(fixture.Root, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a destination outside the working directory is confirmed by its absolute path.
    /// </summary>
    /// <remarks>
    ///     The configuration the sample runs in: a workspace to read and a separate session
    ///     folder to write into. A relative name would name a location the model cannot reach
    ///     from the anchor, so the absolute path is the only truthful answer.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_DestinationOutsideTheWorkingDirectory_IsConfirmedAbsolutely()
    {
        // Arrange: a readable workspace and a separate writable location
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(AsymmetricPolicy(fixture.Root, fixture.Outside));
        var destination = Path.Combine(fixture.Outside, "figure.png");

        // Act: write the region into the location outside the anchor
        var result = await InvokeAsync(tool, file, destination: destination);

        // Assert: the confirmation names the absolute path, and the file is really there
        var text = Assert.IsType<string>(result);
        Assert.Contains(destination, text, StringComparison.Ordinal);
        Assert.True(System.IO.File.Exists(destination));
    }

    /// <summary>
    ///     Proves a JPEG source written to a destination produces PNG bytes.
    /// </summary>
    /// <remarks>
    ///     The output format is the family's own rather than the source's, whichever outcome was
    ///     asked for. The written file is decoded as a PNG, so a file carrying JPEG bytes under a
    ///     <c>.png</c> name fails here.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_JpegSourceToDestination_WritesPngBytes()
    {
        // Arrange: a permitted JPEG with a content block on a flat field
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(
            fixture.Root,
            "photo.jpg",
            ImageTestImages.ContentOnBackgroundJpeg(
                64,
                48,
                new Rgba32(240, 240, 240, 255),
                new ImageTestImages.Rectangle(20, 16, 24, 16),
                new Rgba32(20, 20, 20, 255)));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: trim it into a new file
        var result = await InvokeAsync(tool, file, padding: 0, destination: "figure.png");

        // Assert: the written file decodes as a PNG that is genuinely smaller than the source
        Assert.IsType<string>(result);
        using var decoded = ImageTestImages.Decode(
            await System.IO.File.ReadAllBytesAsync(
                Path.Combine(fixture.Root, "figure.png"), TestContext.Current.CancellationToken));
        Assert.True(decoded.Width < 64);
        Assert.True(decoded.Height < 48);
    }

    /// <summary>
    ///     Proves omitting the destination still returns the region inline.
    /// </summary>
    /// <remarks>
    ///     The mode that keeps this tool fully useful under a policy permitting no writing
    ///     anywhere, which is why the pack publishes it unconditionally. Nothing is created on
    ///     disk, so the inline outcome is proven to have no side effect.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_OmittedDestination_StillReturnsTheRegionInline()
    {
        // Arrange: a permitted image in a workspace holding nothing else
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: name no destination at all
        var result = await InvokeAsync(tool, file);

        // Assert: the region comes back inline, and nothing was created beside the source
        var content = Assert.IsType<List<AIContent>>(result);
        Assert.IsType<DataContent>(content[1]);
        Assert.Equal(file, Assert.Single(Directory.GetFiles(fixture.Root)));
    }

    /// <summary>
    ///     Proves a destination under a read-only grant is refused, disclosing where the agent
    ///     may write instead.
    /// </summary>
    /// <remarks>
    ///     <b>The scenario the write decision exists for.</b> The source is admitted by the read
    ///     decision and the destination refused by the write decision in the same call, which is
    ///     what keeps a read-wide, write-narrow configuration meaningful rather than decorative.
    ///     Deriving the write from the read would make this call succeed.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_DestinationUnderAReadOnlyGrant_IsRefusedDisclosingTheWritableLocation()
    {
        // Arrange: a read-only workspace holding the image, and a separate writable location
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(AsymmetricPolicy(fixture.Root, fixture.Outside));

        // Act: try to write the region beside the image, which may be read but not written
        var result = await InvokeAsync(tool, file, destination: "figure.png");

        // Assert: refused by the policy, which names the writable location with its level
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.Contains(fixture.Outside, text, StringComparison.Ordinal);
        Assert.Contains("(read-write)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a destination no grant permits at all is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_DestinationOutsideEveryGrant_IsRefused()
    {
        // Arrange: a permitted image, and a destination in a location no grant covers
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: name a destination outside the permitted location
        var result = await InvokeAsync(
            tool, file, destination: Path.Combine(fixture.Outside, "figure.png"));

        // Assert: refused by the policy's own decision
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a refused destination leaves no file behind.
    /// </summary>
    /// <remarks>
    ///     What makes the grant a boundary rather than advice. A refusal that nonetheless wrote
    ///     the file would be the worst of both answers: the model told it failed, and the
    ///     operator's confinement broken anyway.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_RefusedDestination_WritesNothing()
    {
        // Arrange: a read-only workspace holding the image, and a separate writable location
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(AsymmetricPolicy(fixture.Root, fixture.Outside));
        var destination = Path.Combine(fixture.Root, "figure.png");

        // Act: try to write into the read-only location
        var result = await InvokeAsync(tool, file, destination: destination);

        // Assert: refused, and nothing was created
        Assert.IsType<string>(result);
        Assert.False(System.IO.File.Exists(destination));
    }

    /// <summary>
    ///     Proves an existing destination is refused and left exactly as it was.
    /// </summary>
    /// <remarks>
    ///     A trim that clobbered a figure someone already placed would report success while the
    ///     document went on referencing a name that now points at a different picture. The prior
    ///     file's bytes are compared afterwards, so "refused" and "unchanged" are both asserted.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_ExistingDestination_IsRefusedWithoutReplacingIt()
    {
        // Arrange: a permitted image, and a file already occupying the destination name
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var occupied = WriteBytes(fixture.Root, "figure.png", [0x01, 0x02, 0x03]);
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: name the occupied path as the destination
        var result = await InvokeAsync(tool, file, destination: "figure.png");

        // Assert: refused, saying it writes a new file, and the prior file is untouched
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("writes a new file", text, StringComparison.Ordinal);
        Assert.Equal(
            new byte[] { 0x01, 0x02, 0x03 },
            await System.IO.File.ReadAllBytesAsync(occupied, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a destination equal to the source is refused and the source is left intact.
    /// </summary>
    /// <remarks>
    ///     The guarantee that an image can never be consumed by its own trimming. The source is
    ///     proven to exist before the destination is judged, so naming it as the destination
    ///     falls into the already-exists refusal rather than into a silent overwrite.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_DestinationEqualToTheSource_IsRefusedWithoutReplacingIt()
    {
        // Arrange: a permitted image whose bytes are known
        using var fixture = new TempDirectoryFixture();
        var sourceBytes = CenteredPng(DarkBackground);
        var file = WriteBytes(fixture.Root, "diagram.png", sourceBytes);
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: name the source itself as the destination
        var result = await InvokeAsync(tool, file, destination: file);

        // Assert: refused, and the source's own bytes are exactly as they were
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Equal(
            sourceBytes,
            await System.IO.File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Proves a destination not named as a PNG is refused, naming the extension expected.
    /// </summary>
    /// <remarks>
    ///     A region is always encoded as PNG, so a destination named for another format would
    ///     hold PNG bytes under a name that says otherwise. A blank or whitespace-only
    ///     destination has no extension and lands here too, which is deliberate: a destination
    ///     the model did not really intend is never silently read as "no destination".
    /// </remarks>
    /// <param name="destination">The destination the scenario names.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("figure.jpg")]
    [InlineData("figure.gif")]
    [InlineData("figure")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImageAutoCropTool_AutoCrop_NonPngDestination_IsRefusedNamingTheExpectedExtension(
        string destination)
    {
        // Arrange: a permitted image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: name a destination that cannot truthfully hold PNG bytes
        var result = await InvokeAsync(tool, file, destination: destination);

        // Assert: refused as a malformed request, naming the extension to use instead
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("ends in '.png'", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a destination whose extension differs only in case is accepted.
    /// </summary>
    /// <remarks>
    ///     The rule the family already applies to every other extension it reads: a capitalized
    ///     extension names the same content.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_UppercaseDestinationExtension_IsAccepted()
    {
        // Arrange: a permitted image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: name the destination with a capitalized extension
        var result = await InvokeAsync(tool, file, padding: 0, destination: "FIGURE.PNG");

        // Assert: accepted, and the region really was written
        var text = Assert.IsType<string>(result);
        Assert.Contains("Wrote the trimmed region", text, StringComparison.Ordinal);

        using var decoded = ImageTestImages.Decode(
            await System.IO.File.ReadAllBytesAsync(
                Path.Combine(fixture.Root, "FIGURE.PNG"), TestContext.Current.CancellationToken));
        Assert.Equal(10, decoded.Width);
        Assert.Equal(8, decoded.Height);
    }

    /// <summary>
    ///     Proves a destination naming an existing directory is refused.
    /// </summary>
    /// <remarks>
    ///     The directory has to be named <c>.png</c> to reach this check at all, because the
    ///     extension rule is judged first — which is why the fixture creates a directory
    ///     literally named <c>figure.png</c>.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_DestinationIsADirectory_IsRefused()
    {
        // Arrange: a permitted image, and a directory occupying the destination name
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var destination = Path.Combine(fixture.Root, "figure.png");
        Directory.CreateDirectory(destination);
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: name the directory as the destination
        var result = await InvokeAsync(tool, file, destination: destination);

        // Assert: refused with the fact stated and the correction offered
        var text = Assert.IsType<string>(result);
        Assert.Contains(
            "The destination path is a directory, not a file.", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a destination whose parent directory does not exist is refused, and no
    ///     directory is created.
    /// </summary>
    /// <remarks>
    ///     A missing parent is refused rather than materialized: silently creating a tree is a
    ///     side effect the operator never asked for and, on a mistyped path, would scatter
    ///     directories the agent then believes are real.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_DestinationParentMissing_IsRefused()
    {
        // Arrange: a permitted image, and a destination inside a directory that does not exist
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));
        var destination = Path.Combine(fixture.Root, "absent-dir", "figure.png");

        // Act: name it as the destination
        var result = await InvokeAsync(tool, file, destination: destination);

        // Assert: refused, and no directory materialized on the way
        var text = Assert.IsType<string>(result);
        Assert.Contains(
            "The parent directory of the destination path does not exist.",
            text,
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "absent-dir")));
    }

    /// <summary>
    ///     Proves a permitted destination the file system refuses is reported as a plain fact,
    ///     with no file left behind and none of the host's own wording.
    /// </summary>
    /// <remarks>
    ///     The refusal is provoked by a file name longer than a single path component may be —
    ///     255 characters on NTFS, ext4 and APFS alike — so the failure comes from the host
    ///     rather than from anything this unit checked, which is the only way to reach the
    ///     branch. It is the component limit and not the total path length that is exceeded, so
    ///     the scenario does not depend on a host's long-path configuration.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_UnwritableDestination_IsRefusedWithoutDisclosingTheFailure()
    {
        // Arrange: a permitted image, and a destination name no file system will accept
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));
        var destination = new string('a', 300) + ".png";

        // Act: name it as the destination
        var result = await InvokeAsync(tool, file, destination: destination);

        // Assert: refused with the fact alone, nothing written, and no host wording disclosed
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("The trimmed region could not be written.", text, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Root, text, StringComparison.Ordinal);
        Assert.Equal(file, Assert.Single(Directory.GetFiles(fixture.Root)));
    }

    /// <summary>
    ///     Proves every destination refusal this tool composes itself names no host path.
    /// </summary>
    /// <remarks>
    ///     The refusal text reaches a model and the resulting transcript leaves this process, so
    ///     nothing about the host's layout may be composed into one. Only the access policy's own
    ///     refusal discloses paths, and it does so deliberately. The success confirmation is not
    ///     a refusal and is governed by the opposite rule, which the dialect scenarios cover.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_DestinationDenials_NameNoHostPath()
    {
        // Arrange: a permitted image, an occupied name, and a directory named as a png
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "diagram.png", CenteredPng(DarkBackground));
        WriteBytes(fixture.Root, "taken.png", [0x01]);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "folder.png"));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: provoke each refusal this tool composes for a destination
        var refusals = new List<string>
        {
            Assert.IsType<string>(await InvokeAsync(tool, file, destination: "figure.jpg")),
            Assert.IsType<string>(await InvokeAsync(tool, file, destination: "folder.png")),
            Assert.IsType<string>(await InvokeAsync(tool, file, destination: "taken.png")),
            Assert.IsType<string>(await InvokeAsync(tool, file, destination: "absent-dir/figure.png"))
        };

        // Assert: none of them carries a separator, the workspace, or any absolute path
        foreach (var refusal in refusals)
        {
            Assert.Contains("Denied (", refusal, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Root, refusal, StringComparison.Ordinal);
            Assert.DoesNotContain(
                Path.DirectorySeparatorChar.ToString(), refusal, StringComparison.Ordinal);
            Assert.DoesNotContain(
                Path.AltDirectorySeparatorChar.ToString(), refusal, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Proves a file beyond the binary ceiling is refused before anything is parsed.
    /// </summary>
    /// <remarks>
    ///     The fixture is a real image, so a tool that parsed before checking the ceiling would
    ///     succeed rather than fail. The refusal is asserted by its whole text, and the trimmed
    ///     result's refusal by its absence: both messages name the same ceiling, so an assertion
    ///     on the ceiling alone would be satisfied by the second check firing after the first had
    ///     been removed entirely.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_FileLargerThanTheBinaryCeiling_ReturnsDenialNamingTheCeiling()
    {
        // Arrange: a real image, and a binary ceiling far below its size
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "big.png", ImageTestImages.Png(64, 64));
        var tool = ImageAutoCropTool.Create(
            RootedPolicy(fixture.Root, new ToolLimits(maxBinaryBytes: 8)));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

        // Assert: the source file's own refusal, naming the ceiling, and not the result's
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("The file exceeds the 8-byte binary limit.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("The trimmed image is", text, StringComparison.Ordinal);
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
    public async Task ImageAutoCropTool_AutoCrop_EncodedRegionBeyondTheBinaryCeiling_ReturnsDenialNamingTheCeiling()
    {
        // Arrange: a source comfortably inside a ceiling its lossless re-encoding will exceed
        using var fixture = new TempDirectoryFixture();
        var sourceBytes = ImageTestImages.Jpeg(96, 96);
        var file = WriteBytes(fixture.Root, "photo.jpg", sourceBytes);
        var ceiling = sourceBytes.Length + 64;
        var tool = ImageAutoCropTool.Create(
            RootedPolicy(fixture.Root, new ToolLimits(maxBinaryBytes: ceiling)));

        // Act: trim it, with padding enough that the region covers most of the picture
        var result = await InvokeAsync(tool, file);

        // Assert: a refusal naming the ceiling and the size the result would have been
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("The trimmed image is", text, StringComparison.Ordinal);
        Assert.Contains("binary limit", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an image declaring more pixels than the decode budget is refused, from its
    ///     header alone.
    /// </summary>
    /// <remarks>
    ///     <b>Both axes of this fixture are inside the largest extent the decoder accepts; only
    ///     their product is outside the budget.</b> The fixture carries no pixel data at all, so
    ///     a tool that decoded before triaging would produce the undecodable refusal instead —
    ///     which is how "decided from the declared dimensions alone" is asserted rather than
    ///     assumed. It matters more here than for a caller-named region, because this tool must
    ///     scan every pixel and would otherwise allocate the whole buffer to do it.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_HeaderDeclaringMorePixelsThanTheDecodeBudget_IsRefusedNamingTheBudget()
    {
        // Arrange: a header declaring 64 million pixels, and no pixel data whatsoever
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "bomb.png", ImageTestImages.PngHeaderOnly(8000, 8000));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

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
    public async Task ImageAutoCropTool_AutoCrop_HeaderDeclaringAnOversizedAxis_IsRefusedNamingTheBounds()
    {
        // Arrange: a header declaring a very wide, very short image
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "wide.png", ImageTestImages.PngHeaderOnly(9000, 10));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

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
    ///     header reader reports a palette-indexed file as carrying one channel per pixel,
    ///     because that is what the file stores — while decoding it produces four bytes per
    ///     pixel, because every index is resolved into RGBA. A budget computed as
    ///     width times height times channels would under-count this file by a factor of four and
    ///     accept it. Palette-indexed content is also the format that compresses best, so it is
    ///     exactly what a hostile caller would reach for. The image differs from the truecolor
    ///     bomb above only in its declared color type.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_PalettizedPngNearThePixelCeiling_IsRefusedNamingTheBudget()
    {
        // Arrange: the same oversized declaration, as a palette-indexed file
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(
            fixture.Root,
            "indexed-bomb.png",
            ImageTestImages.PngHeaderOnly(8000, 8000, colorType: 3));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

        // Assert: refused for its size, exactly as the truecolor declaration was
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("8000x8000 pixels", text, StringComparison.Ordinal);
        Assert.Contains("16777216 pixels in total", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the pixel ceiling the tool enforces is the one the host configured, not the
    ///     published default.
    /// </summary>
    /// <remarks>
    ///     <b>The scenario that makes a configured ceiling falsifiable.</b> Every other decode
    ///     scenario runs at the default ceiling, so a tool that read the published default in
    ///     place of the policy's own value would pass all of them. This fixture declares 100 by
    ///     100: far above the ceiling this host lowers to and far below the published default, so
    ///     the refusal can only come from the configured value.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_HeaderExceedingAHostLoweredPixelCeiling_IsRefusedNamingThatCeiling()
    {
        // Arrange: a modest declaration, and a host ceiling deliberately below it
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "modest.png", ImageTestImages.PngHeaderOnly(100, 100));
        var tool = ImageAutoCropTool.Create(
            RootedPolicy(fixture.Root, new ToolLimits(maxImagePixels: 4096)));

        // Act: ask for the content region of an image the default ceiling would have admitted
        var result = await InvokeAsync(tool, file);

        // Assert: refused against the host's ceiling, which is the only bound that can refuse it
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (ResourceTooLarge)", text, StringComparison.Ordinal);
        Assert.Contains("100x100 pixels", text, StringComparison.Ordinal);
        Assert.Contains("4096 pixels in total", text, StringComparison.Ordinal);
        Assert.DoesNotContain("16777216", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file whose extension lies about its content is refused without any dimensions
    ///     being stated.
    /// </summary>
    /// <remarks>
    ///     Whether a size is stated is the fact that distinguishes the ways content can fail.
    ///     Nothing was read here, so nothing is claimed — and the refusal names no host path
    ///     either, which is the other half of what this unit composes into a message.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_FileWhoseExtensionLiesAboutItsContent_ReturnsDenialWithoutDimensions()
    {
        // Arrange: a file named .png whose bytes are nothing of the kind
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "mislabeled.png", "this is plain text"u8.ToArray());
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

        // Assert: refused as unreadable content, stating no size and no host path
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("not readable as image/png content", text, StringComparison.Ordinal);
        Assert.DoesNotContain("pixels", text, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Root, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file whose header reads but whose body does not is refused, with the declared
    ///     size stated.
    /// </summary>
    /// <remarks>
    ///     The distinction that makes the refusal useful: the size was learned, so it is stated,
    ///     which tells the model the file is damaged rather than of the wrong kind.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_TruncatedPngBody_ReturnsDenialNamingTheDeclaredDimensions()
    {
        // Arrange: a permitted PNG whose header is sound and whose pixel data stops part-way
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(
            fixture.Root, "damaged.png", ImageTestImages.TruncatedBodyPng(24, 18));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

        // Assert: refused, with the size the header did declare
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("24x18 pixels", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a well-formed PNG declaring interlacing is refused, naming its dimensions.
    /// </summary>
    /// <remarks>
    ///     The one case where a file is entirely sound and still has to be refused: the decoder
    ///     reports from the header that it will not decode it, so the refusal says the file is of
    ///     a kind this tool does not decode rather than that it is damaged.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_Adam7InterlacedPng_ReturnsDenialNamingTheDeclaredDimensions()
    {
        // Arrange: a complete, specification-conforming PNG declaring Adam7 interlacing
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(
            fixture.Root, "interlaced.png", ImageTestImages.Adam7InterlacedPng(24, 18));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

        // Assert: refused as a kind not decoded, with the declared size named
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("24x18 pixels", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an empty file is refused rather than throwing at the model.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_ZeroByteFile_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a permitted file with no bytes at all
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "empty.png", []);
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

        // Assert: a returned refusal, because an exception would end the agent's turn
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a <c>.gif</c> is refused, naming the types a region can be taken from.
    /// </summary>
    /// <remarks>
    ///     The refusal is composed by the family's own media-type map, so the family gives one
    ///     answer to "what is this file" whichever region tool was asked. No sibling tool is
    ///     named: a <c>.gif</c> genuinely is an image, so naming the read tool would offer a
    ///     route to the whole picture after the model asked for one part of it.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_GifFile_ReturnsDenialNamingTheCroppableTypes()
    {
        // Arrange: a permitted .gif, which the family reads but cannot cut
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "animation.gif", [0x47, 0x49, 0x46, 0x38]);
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

        // Assert: refused, stating what is true of the file and what may be trimmed instead
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("png, jpg and jpeg", text, StringComparison.Ordinal);
        Assert.DoesNotContain(ImageReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a WebP file is refused, naming the types a region can be taken from.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_WebpFile_ReturnsDenialNamingTheCroppableTypes()
    {
        // Arrange: a permitted .webp, which the family reads but does not decode
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "photo.webp", [0x52, 0x49, 0x46, 0x46]);
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

        // Assert: refused, naming the types that can be trimmed
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("png, jpg and jpeg", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a PDF is refused, stating that rasterization would be required first.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_PdfFile_ReturnsDenialStatingRasterizationIsRequired()
    {
        // Arrange: a permitted .pdf, which the family reads but cannot decode into pixels
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "report.pdf", [0x25, 0x50, 0x44, 0x46]);
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

        // Assert: refused, stating what would have to happen first
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("paginated document", text, StringComparison.Ordinal);
        Assert.Contains("rasterize", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an SVG is refused with a redirect to the text file reader.
    /// </summary>
    /// <remarks>
    ///     An <c>.svg</c> genuinely is text, so naming the text reader classifies the file rather
    ///     than offering a way around the refusal.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_SvgFile_ReturnsDenialRedirectingToTextFileRead()
    {
        // Arrange: a permitted .svg, which is vector content this family does not read
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "logo.svg", "<svg/>"u8.ToArray());
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

        // Assert: refused, naming the tool that genuinely reads this content
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains(TextFileReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an unknown extension is refused with no redirect at all.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_UnknownExtension_ReturnsDenialWithoutRedirect()
    {
        // Arrange: a permitted file of a type the family has no classification for
        using var fixture = new TempDirectoryFixture();
        var file = WriteBytes(fixture.Root, "archive.zip", [0x50, 0x4B, 0x03, 0x04]);
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, file);

        // Assert: refused, with no honest alternative offered because there is none
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(TextFileReadTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain(ImageReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a path outside the permitted read location is refused.
    /// </summary>
    /// <remarks>
    ///     The primary threat the library exists to control. The decision is made before anything
    ///     is learned about the file, so a refused path never reveals whether it exists.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_PathOutsideTheReadRoot_ReturnsDenial()
    {
        // Arrange: an image outside the permitted location
        using var fixture = new TempDirectoryFixture();
        var outside = WriteBytes(fixture.Outside, "secret.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, outside);

        // Assert: refused by the policy's own decision
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a policy refusal discloses the locations the agent may work in.
    /// </summary>
    /// <remarks>
    ///     A confined model learns where it may look instead of guessing, which is why this one
    ///     refusal deliberately names host paths while everything the tool composes itself names
    ///     none.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_DeniedPath_DenialDisclosesPermittedLocation()
    {
        // Arrange: an image outside the permitted location
        using var fixture = new TempDirectoryFixture();
        var outside = WriteBytes(fixture.Outside, "secret.png", CenteredPng(DarkBackground));
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, outside);

        // Assert: the permitted location is named, with the level it carries
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (PathNotPermitted)", text, StringComparison.Ordinal);
        Assert.Contains(fixture.Root, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a request naming a directory is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_DirectoryPath_ReturnsDenial()
    {
        // Arrange: a permitted directory carrying an image-like name
        using var fixture = new TempDirectoryFixture();
        var directory = Path.Combine(fixture.Root, "pictures");
        Directory.CreateDirectory(directory);
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for its content region
        var result = await InvokeAsync(tool, directory);

        // Assert: refused with the fact stated and no remedy prescribed
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
        Assert.Contains("is a directory, not a file", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing file of a trimmable type is refused as not found.
    /// </summary>
    /// <remarks>
    ///     The type is judged before existence, matching the family's order, so a trimmable
    ///     extension naming no file is refused as not found rather than as an untrimmable type.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_MissingFile_ReturnsDenial()
    {
        // Arrange: a permitted location holding no such file
        using var fixture = new TempDirectoryFixture();
        Directory.CreateDirectory(fixture.Root);
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

        // Act: ask for the content region of a file that does not exist
        var result = await InvokeAsync(tool, Path.Combine(fixture.Root, "absent.png"));

        // Assert: refused as not found
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (TargetNotFound)", text, StringComparison.Ordinal);
        Assert.Contains("The requested file does not exist.", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves omitting the path produces a refusal rather than a framework error.
    /// </summary>
    /// <remarks>
    ///     The scenario that pins every parameter as optional. A parameter with no default is
    ///     required by the function factory, and an omitted argument then fails inside the
    ///     factory before this tool is reached, leaving the model an opaque framework error
    ///     rather than a refusal naming what to supply.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageAutoCropTool_AutoCrop_MissingPathArgument_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a workspace-governed tool, so only the request is at fault
        using var fixture = new TempDirectoryFixture();
        var tool = ImageAutoCropTool.Create(RootedPolicy(fixture.Root));

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
    /// <param name="path">The path the scenario supplies.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ImageAutoCropTool_AutoCrop_BlankPath_ReturnsDenialWithoutThrowing(string path)
    {
        // Arrange: a tool governed by an unrestricted policy, so only the request is at fault
        var tool = ImageAutoCropTool.Create(
            new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]));

        // Act: invoke with a path that names nothing
        var result = await InvokeAsync(tool, path);

        // Assert: a returned refusal, because an exception would end the agent's turn
        var text = Assert.IsType<string>(result);
        Assert.Contains("Denied (InvalidRequest)", text, StringComparison.Ordinal);
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
    ///     Creates a policy that may read one location and write a different one.
    /// </summary>
    /// <remarks>
    ///     <b>This is the configuration the grant distinction is decided in.</b> A policy granting
    ///     read-write over a single root cannot distinguish a write decision from a read decision,
    ///     so a tool that resolved a destination through the read decision would pass every
    ///     scenario written against <see cref="RootedPolicy"/>; only a scenario built on this
    ///     helper can catch it. It is also the shape a real application configures: a workspace to
    ///     read and a separate session folder to produce into.
    /// </remarks>
    /// <param name="readRoot">The location that may be read and is the anchor for relative paths.</param>
    /// <param name="writeRoot">The separate location that may be read and written.</param>
    /// <returns>The constructed policy.</returns>
    private static PathPolicy AsymmetricPolicy(string readRoot, string writeRoot)
    {
        return new PathPolicy(
            readRoot,
            [PathRule.ReadOnly(readRoot), PathRule.ReadWrite(writeRoot)]);
    }

    /// <summary>
    ///     Builds the standard fixture: a 40x30 image whose content block sits at 12,9 and is
    ///     10x8, clear of every edge by more than the default padding.
    /// </summary>
    /// <remarks>
    ///     Named once so that the arithmetic every region assertion depends on is stated in one
    ///     place: the content box runs 12,9 to 21,16, and the margins are 12, 9, 18 and 13
    ///     pixels, all wider than the default padding of eight.
    /// </remarks>
    /// <param name="background">The background color the fixture's margin carries.</param>
    /// <returns>The encoded PNG file.</returns>
    private static byte[] CenteredPng(Rgba32 background)
    {
        return ImageTestImages.ContentOnBackgroundPng(
            40, 30, background, CenteredContent, ContentColor);
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
    ///     Invokes the auto-crop tool exactly as a runtime would.
    /// </summary>
    /// <remarks>
    ///     Both optional arguments are added to the invocation only when one was supplied, so an
    ///     omitted padding and an omitted destination reach the tool as genuinely absent
    ///     arguments rather than as explicit nulls — which is the only way the default-padding
    ///     and inline-result behaviors can be exercised as a model would meet them.
    /// </remarks>
    /// <param name="tool">The tool to invoke.</param>
    /// <param name="path">The path argument to supply.</param>
    /// <param name="padding">The padding to supply, or null to omit the argument.</param>
    /// <param name="destination">The destination to supply, or null to omit the argument.</param>
    /// <returns>The result the tool returned.</returns>
    private static async Task<object?> InvokeAsync(
        AIFunction tool,
        string path,
        int? padding = null,
        string? destination = null)
    {
        var arguments = new AIFunctionArguments { ["path"] = path };

        if (padding is not null)
        {
            arguments["padding"] = padding.Value;
        }

        if (destination is not null)
        {
            arguments["destination"] = destination;
        }

        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }
}
