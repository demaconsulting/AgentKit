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
}
