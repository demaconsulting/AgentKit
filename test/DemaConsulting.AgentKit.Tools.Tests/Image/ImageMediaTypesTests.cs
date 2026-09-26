using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Image;
using DemaConsulting.AgentKit.Tools.TextFile;

namespace DemaConsulting.AgentKit.Tools.Tests.Image;

/// <summary>
///     Unit tests for the <see cref="ImageMediaTypes"/> class.
/// </summary>
/// <remarks>
///     These scenarios verify the two things the mapping owns: which extensions resolve to which
///     media types, and how a file whose type the family cannot read is refused — including the
///     redirects that turn a refusal into the model's next step.
/// </remarks>
public class ImageMediaTypesTests
{
    /// <summary>
    ///     Proves each supported extension resolves to the media type the family reads it as.
    /// </summary>
    /// <param name="fileName">A file name carrying the extension under test.</param>
    /// <param name="expected">The media type the extension must resolve to.</param>
    [Theory]
    [InlineData("picture.png", ImageMediaTypes.Png)]
    [InlineData("picture.jpg", ImageMediaTypes.Jpeg)]
    [InlineData("picture.jpeg", ImageMediaTypes.Jpeg)]
    [InlineData("picture.gif", ImageMediaTypes.Gif)]
    [InlineData("picture.webp", ImageMediaTypes.Webp)]
    [InlineData("document.pdf", ImageMediaTypes.Pdf)]
    public void ImageMediaTypes_TryResolveMediaType_SupportedExtension_ReturnsMediaType(
        string fileName,
        string expected)
    {
        // Act: resolve the media type from the extension
        var resolved = ImageMediaTypes.TryResolveMediaType(fileName, out var mediaType);

        // Assert: the extension is supported and resolves to the expected media type
        Assert.True(resolved);
        Assert.Equal(expected, mediaType);
    }

    /// <summary>
    ///     Proves an extension is matched without regard to its case.
    /// </summary>
    [Fact]
    public void ImageMediaTypes_TryResolveMediaType_ExtensionCaseInsensitive_ReturnsMediaType()
    {
        // Act: resolve a capitalized extension, which names the same content as a lower-case one
        var resolved = ImageMediaTypes.TryResolveMediaType("PICTURE.PNG", out var mediaType);

        // Assert: the type resolves exactly as the lower-case form would
        Assert.True(resolved);
        Assert.Equal(ImageMediaTypes.Png, mediaType);
    }

    /// <summary>
    ///     Proves an unsupported extension does not resolve to a media type.
    /// </summary>
    [Fact]
    public void ImageMediaTypes_TryResolveMediaType_UnsupportedExtension_ReturnsFalse()
    {
        // Act: resolve an extension the family does not read
        var resolved = ImageMediaTypes.TryResolveMediaType("archive.zip", out var mediaType);

        // Assert: reported as unsupported, with no media type
        Assert.False(resolved);
        Assert.Null(mediaType);
    }

    /// <summary>
    ///     Proves an <c>.svg</c> file is refused with a redirect to the text file read tool.
    /// </summary>
    [Fact]
    public void ImageMediaTypes_DenyUnsupportedType_Svg_RedirectsToTextFileRead()
    {
        // Act: refuse a vector-text file that a text tool, not this family, should read
        var text = Assert.IsType<string>(ImageMediaTypes.DenyUnsupportedType("diagram.svg"));

        // Assert: refused as an unsupported type, stating what the file is; naming the reader for
        // that kind of content is a classification, not a prescribed way around the refusal
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("is text and vector content", text, StringComparison.Ordinal);
        Assert.Contains(TextFileReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an <c>.svgz</c> file is refused without a redirect, since no tool can read it.
    /// </summary>
    [Fact]
    public void ImageMediaTypes_DenyUnsupportedType_Svgz_RefusesWithoutRedirect()
    {
        // Act: refuse the gzip-compressed vector content no tool in the family reads
        var text = Assert.IsType<string>(ImageMediaTypes.DenyUnsupportedType("diagram.svgz"));

        // Assert: refused as an unsupported type, and naming no tool a retry would only fail at
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(TextFileReadTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves any other unsupported extension is refused without a redirect.
    /// </summary>
    [Fact]
    public void ImageMediaTypes_DenyUnsupportedType_UnknownType_RefusesWithoutRedirect()
    {
        // Act: refuse an extension with no better tool to name
        var text = Assert.IsType<string>(ImageMediaTypes.DenyUnsupportedType("archive.zip"));

        // Assert: refused as an unsupported type, with no redirect invented
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tool instead", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves each type a region can be extracted from resolves to its media type.
    /// </summary>
    [Theory]
    [InlineData("picture.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    public void ImageMediaTypes_TryResolveCroppableMediaType_CroppableExtension_ReturnsMediaType(
        string fileName,
        string expected)
    {
        // Act: resolve the croppable type
        var resolved = ImageMediaTypes.TryResolveCroppableMediaType(fileName, out var mediaType);

        // Assert: the still raster formats the family can decode resolve to their media types
        Assert.True(resolved);
        Assert.Equal(expected, mediaType);
    }

    /// <summary>
    ///     Proves a croppable extension is matched without regard to case.
    /// </summary>
    [Fact]
    public void ImageMediaTypes_TryResolveCroppableMediaType_ExtensionCaseInsensitive_ReturnsMediaType()
    {
        // Act: resolve a capitalized extension
        var resolved = ImageMediaTypes.TryResolveCroppableMediaType("PICTURE.PNG", out var mediaType);

        // Assert: a capitalized extension names the same content as its lower-case form
        Assert.True(resolved);
        Assert.Equal(ImageMediaTypes.Png, mediaType);
    }

    /// <summary>
    ///     Proves a type the family reads but cannot cut does not resolve as croppable.
    /// </summary>
    /// <remarks>
    ///     The croppable set is narrower than the readable set, deliberately: extracting a region
    ///     means decoding, so it is confined to the still raster formats the family can decode.
    /// </remarks>
    [Theory]
    [InlineData("animation.gif")]
    [InlineData("picture.webp")]
    [InlineData("document.pdf")]
    [InlineData("archive.zip")]
    public void ImageMediaTypes_TryResolveCroppableMediaType_NonCroppableExtension_ReturnsFalse(string fileName)
    {
        // Act: try to resolve a type no region can be extracted from
        var resolved = ImageMediaTypes.TryResolveCroppableMediaType(fileName, out var mediaType);

        // Assert: reported as uncroppable, so the crop tool refuses it rather than attempting a
        // decode it cannot complete
        Assert.False(resolved);
        Assert.Null(mediaType);
    }

    /// <summary>
    ///     Proves a <c>.gif</c> is refused because its frame cannot be established, naming the
    ///     types that can be cropped.
    /// </summary>
    [Fact]
    public void ImageMediaTypes_DenyNonCroppableType_Gif_StatesTheFrameIsUnknowableAndNamesTheCroppableTypes()
    {
        // Act: refuse a format whose frame count nothing this family reads reports
        var text = Assert.IsType<string>(ImageMediaTypes.DenyNonCroppableType("animation.gif"));

        // Assert: states what is actually true of the file — more than one frame is possible and
        // the tool cannot tell — and what may be cropped, and names no sibling tool: a .gif
        // genuinely is an image, so naming the reader would be a route to the content this
        // refusal withheld rather than a classification of the file
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("may hold more than one frame", text, StringComparison.Ordinal);
        Assert.Contains("cannot tell how many", text, StringComparison.Ordinal);
        Assert.Contains("png, jpg and jpeg", text, StringComparison.Ordinal);
        Assert.DoesNotContain(ImageReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a <c>.webp</c> is refused in its own words, naming the types that can be cropped.
    /// </summary>
    [Fact]
    public void ImageMediaTypes_DenyNonCroppableType_Webp_StatesItIsNotDecodedAndNamesTheCroppableTypes()
    {
        // Act: refuse a still raster image this family hands over without decoding
        var text = Assert.IsType<string>(ImageMediaTypes.DenyNonCroppableType("picture.webp"));

        // Assert: its own reason rather than the .gif's, and no sibling tool named
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("does not decode", text, StringComparison.Ordinal);
        Assert.Contains("png, jpg and jpeg", text, StringComparison.Ordinal);
        Assert.DoesNotContain(ImageReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a <c>.pdf</c> is refused with a statement that rasterization would be required.
    /// </summary>
    [Fact]
    public void ImageMediaTypes_DenyNonCroppableType_Pdf_StatesRasterizationIsRequired()
    {
        // Act: refuse the one admitted type that looks croppable and is not
        var text = Assert.IsType<string>(ImageMediaTypes.DenyNonCroppableType("document.pdf"));

        // Assert: names the page-and-resolution choice no tool here makes, so a model does not
        // retry with different coordinates
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.Contains("paginated document", text, StringComparison.Ordinal);
        Assert.Contains("rasterize", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an <c>.svg</c> is refused exactly as the readable-type map refuses it.
    /// </summary>
    /// <remarks>
    ///     The family must give one answer to "what is this file". Delegating a type the family
    ///     cannot read at all back to the readable-type refusal is what keeps the two in step.
    /// </remarks>
    [Fact]
    public void ImageMediaTypes_DenyNonCroppableType_Svg_RedirectsToTextFileRead()
    {
        // Act: refuse a vector-text file through the croppable-type path
        var text = Assert.IsType<string>(ImageMediaTypes.DenyNonCroppableType("diagram.svg"));

        // Assert: the identical refusal the readable-type path composes
        Assert.Equal(
            Assert.IsType<string>(ImageMediaTypes.DenyUnsupportedType("diagram.svg")),
            text);
        Assert.Contains("is text and vector content", text, StringComparison.Ordinal);
        Assert.Contains(TextFileReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an <c>.svgz</c> is refused through the croppable path exactly as before.
    /// </summary>
    [Fact]
    public void ImageMediaTypes_DenyNonCroppableType_Svgz_RefusesWithoutRedirect()
    {
        // Act: refuse the gzip-compressed vector content through the croppable-type path
        var text = Assert.IsType<string>(ImageMediaTypes.DenyNonCroppableType("diagram.svgz"));

        // Assert: unchanged behavior, with no tool named
        Assert.Equal(
            Assert.IsType<string>(ImageMediaTypes.DenyUnsupportedType("diagram.svgz")),
            text);
        Assert.DoesNotContain(TextFileReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an extension the family cannot read at all is refused without a redirect.
    /// </summary>
    [Fact]
    public void ImageMediaTypes_DenyNonCroppableType_UnknownType_RefusesWithoutRedirect()
    {
        // Act: refuse an extension with no better tool to name
        var text = Assert.IsType<string>(ImageMediaTypes.DenyNonCroppableType("archive.zip"));

        // Assert: refused as unsupported, with no redirect invented
        Assert.Contains("Denied (UnsupportedMediaType)", text, StringComparison.Ordinal);
        Assert.DoesNotContain(TextFileReadTool.ToolName, text, StringComparison.Ordinal);
        Assert.DoesNotContain(ImageReadTool.ToolName, text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a missing path is a programming error rather than a refusal.
    /// </summary>
    [Fact]
    public void ImageMediaTypes_TryResolveCroppableMediaType_NullPath_ThrowsArgumentNullException()
    {
        // Act / Assert: the calling tool has already refused an absent path before reaching here
        Assert.Throws<ArgumentNullException>(
            () => ImageMediaTypes.TryResolveCroppableMediaType(null!, out _));
    }
}
