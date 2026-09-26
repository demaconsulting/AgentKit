using System.Diagnostics.CodeAnalysis;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;

namespace DemaConsulting.AgentKit.Tools.Image;

/// <summary>
///     The mapping from a file's extension to the media type the image family reads, together
///     with the refusals it composes for a file whose type it cannot read.
/// </summary>
/// <remarks>
///     <para>
///     The family reads the visual content a vision host can render: the raster image types
///     <c>png</c>, <c>jpg</c>/<c>jpeg</c>, <c>gif</c> and <c>webp</c>, and the paginated
///     document type <c>pdf</c>. The type is decided from the extension rather than by
///     inspecting the bytes, because the media type is what a provider is told the content is,
///     and a caller that named a <c>.png</c> is asking for it to be delivered as one.
///     </para>
///     <para>
///     <b>The set a region can be extracted from is narrower than the set that can be read, and
///     this unit owns both answers.</b> Extracting a region means decoding, so it is confined to
///     the still raster formats the family can decode — <c>png</c>, <c>jpg</c> and <c>jpeg</c>.
///     Holding both questions here is what stops the family giving two different answers to
///     "what is this file", and confining the croppable set to the intersection of what the
///     family reads and what it can decode is what stops a region request becoming an accidental
///     format conversion for content the read tool itself refuses.
///     </para>
///     <para>
///     <b>A refusal states a fact and stops; naming a sibling tool is permitted only when the
///     naming <em>is</em> the fact.</b> The library's rule is that a denial never prescribes a way
///     around itself, because a denial that suggested another tool was measured pushing a model
///     into a destructive workaround the user had explicitly forbidden. A media-type refusal is the
///     one place a tool name survives that rule, and it survives it on the same basis as the
///     established precedent in <see cref="TextFileReadTool"/>, whose binary-content refusal names
///     <c>image_read</c>: what is being stated is what the file <em>is</em>, and the named tool is
///     simply the reader for that kind of content — not an alternative route to the content this
///     refusal withheld. An <c>.svg</c> genuinely is text, so naming <c>text_file_read</c> is a
///     classification of the file, and the two refusals are kept symmetric rather than one being
///     stripped and the other left. An <c>.svgz</c> is that same content gzip-compressed, which no
///     tool reads as such, so its refusal states only that. Any other extension is stated as
///     unsupported, with nothing further.
///     </para>
///     <para>
///     Every refusal message is a compile-time constant carrying no host detail: no absolute
///     path, no permitted location, no directory separator. The refusal text reaches a model and
///     the resulting transcript leaves this process, so nothing about the host's layout may be
///     composed into it.
///     </para>
///     <para>
///     The class is static and stateless and is therefore safe for concurrent use from any
///     number of threads.
///     </para>
/// </remarks>
public static class ImageMediaTypes
{
    /// <summary>
    ///     The media type of a PNG image.
    /// </summary>
    public const string Png = "image/png";

    /// <summary>
    ///     The media type of a JPEG image, carried by both the <c>.jpg</c> and <c>.jpeg</c>
    ///     extensions.
    /// </summary>
    public const string Jpeg = "image/jpeg";

    /// <summary>
    ///     The media type of a GIF image.
    /// </summary>
    public const string Gif = "image/gif";

    /// <summary>
    ///     The media type of a WebP image.
    /// </summary>
    public const string Webp = "image/webp";

    /// <summary>
    ///     The media type of a PDF document.
    /// </summary>
    /// <remarks>
    ///     A PDF is not an <c>image/</c> media type, so a result carrying it is returned through
    ///     <see cref="ToolResult.Binary"/> rather than <see cref="ToolResult.Image"/>, which
    ///     guards on the <c>image/</c> prefix. It is included in the family because it is visual
    ///     content a vision host can render.
    /// </remarks>
    public const string Pdf = "application/pdf";

    /// <summary>
    ///     The set of file types a region can be extracted from, named as a model would write
    ///     them.
    /// </summary>
    /// <remarks>
    ///     Interpolated into each refusal for a type that cannot be cropped, so a model is told
    ///     what it may ask for rather than only what it may not. Written as extensions rather
    ///     than media types because an extension is what the model has in front of it.
    /// </remarks>
    private const string CroppableTypes = "png, jpg and jpeg";

    /// <summary>
    ///     The refusal used when the request names a GIF file.
    /// </summary>
    /// <remarks>
    ///     <b>The reason is that the frame is unknowable, not that the file is animated.</b> A
    ///     single-frame <c>.gif</c> is the common case, so refusing one as "animated" would state
    ///     something untrue about the very file the model named. What is true is that the format
    ///     may carry any number of frames and nothing this family reads reports how many, so a
    ///     region could only be taken from the first and returned as though it were the whole
    ///     answer — a partial answer to a different question, which is the failure this family
    ///     exists to refuse.
    ///     <para>
    ///     <b>Names no sibling tool.</b> A <c>.gif</c> genuinely is an image, so naming the read
    ///     tool would not be a classification of the file — it would be a route to the content
    ///     this refusal withheld, and one that hands the model the whole image after it asked to
    ///     examine a part closely. Reasoning confidently about a region it never examined is the
    ///     failure this family exists to prevent, so the refusal states what the file is and what
    ///     the alternative types are, and stops.
    ///     </para>
    /// </remarks>
    private const string GifFrameIsUnknowable =
        "A .gif file may hold more than one frame and this tool cannot tell how many, so a region "
        + "of it would silently be a region of the first frame alone. A region can be taken from a "
        + CroppableTypes + " file.";

    /// <summary>
    ///     The refusal used when the request names a WebP file.
    /// </summary>
    /// <remarks>
    ///     Its own wording rather than a shared one, because the reason is its own: WebP is a
    ///     format this family hands to a provider without decoding, not a format whose frame
    ///     count it cannot establish. Names no sibling tool, on the same basis as
    ///     <see cref="GifFrameIsUnknowable"/>.
    /// </remarks>
    private const string WebpNotDecoded =
        "A .webp file is a raster image this tool does not decode, so no region can be taken "
        + "from it. A region can be taken from a " + CroppableTypes + " file.";

    /// <summary>
    ///     The refusal used when the request names a PDF.
    /// </summary>
    /// <remarks>
    ///     A PDF is the one admitted type that <em>looks</em> croppable and is not, so it earns
    ///     its own refusal rather than falling into a generic one: without the reason stated, a
    ///     model would reasonably retry with different coordinates. Taking a region of one would
    ///     require choosing a page and a rasterization resolution, and no tool in this family
    ///     does either.
    /// </remarks>
    private const string PdfRequiresRasterization =
        "A .pdf file is a paginated document, and a region of one could only be taken by "
        + "choosing a page and a resolution to rasterize it at, which no tool here does. "
        + "A region can be taken from a " + CroppableTypes + " file.";

    /// <summary>
    ///     The refusal used when the request names an <c>.svg</c> file.
    /// </summary>
    private const string SvgIsText =
        "An .svg file is text and vector content, not a raster image.";

    /// <summary>
    ///     The refusal used when the request names an <c>.svgz</c> file.
    /// </summary>
    private const string SvgzIsCompressed =
        "An .svgz file is gzip-compressed vector content that no tool in this family can read.";

    /// <summary>
    ///     The refusal used when the request names any other unsupported extension.
    /// </summary>
    private const string TypeUnsupported =
        "The requested file's type is not one this tool can read.";

    /// <summary>
    ///     Resolves the media type the family reads a file's extension as.
    /// </summary>
    /// <remarks>
    ///     The extension is matched case-insensitively, because a capitalized extension names the
    ///     same content as a lower-case one and refusing it would surprise a caller for no reason.
    /// </remarks>
    /// <param name="path">The requested path, whose extension decides the type.</param>
    /// <param name="mediaType">
    ///     On success, the media type the file is read as; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the extension names a type the family reads; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    public static bool TryResolveMediaType(string path, [NotNullWhen(true)] out string? mediaType)
    {
        // A missing path is a programming error in the caller; the tool that calls this has
        // already refused an absent path as a malformed request before reaching here.
        ArgumentNullException.ThrowIfNull(path);

        // The extension is the whole basis for the decision; it is lowered once so the switch
        // below can be a set of plain ordinal comparisons.
        mediaType = Extension(path) switch
        {
            ".png" => Png,
            ".jpg" or ".jpeg" => Jpeg,
            ".gif" => Gif,
            ".webp" => Webp,
            ".pdf" => Pdf,
            _ => null
        };

        return mediaType is not null;
    }

    /// <summary>
    ///     Composes the refusal for a file whose extension the family cannot read.
    /// </summary>
    /// <remarks>
    ///     Only called once <see cref="TryResolveMediaType"/> has reported the type is
    ///     unsupported, so the refusal it returns is always an
    ///     <see cref="DenialReason.UnsupportedMediaType"/>. The extension decides which of the
    ///     three refusals applies and whether naming a sibling reader is a classification of the
    ///     file rather than a prescribed remedy.
    /// </remarks>
    /// <param name="path">The requested path, whose extension the refusal is chosen from.</param>
    /// <returns>The composed refusal, naming the reader for the content's actual kind where the
    ///     extension identifies one.</returns>
    public static object DenyUnsupportedType(string path)
    {
        // A missing path is a programming error in the caller.
        ArgumentNullException.ThrowIfNull(path);

        return Extension(path) switch
        {
            // An .svg genuinely is text, so naming the text reader states what the file is rather
            // than offering a way around the refusal — the same basis on which TextFileReadTool's
            // binary-content refusal names image_read.
            ".svg" => ToolResult.Denied(
                DenialReason.UnsupportedMediaType,
                SvgIsText,
                TextFileReadTool.ToolName),

            // An .svgz is the same content compressed; no tool reads it as such, and there is no
            // classification that names a reader, so the refusal states only that.
            ".svgz" => ToolResult.Denied(
                DenialReason.UnsupportedMediaType,
                SvgzIsCompressed),

            // Any other extension has no better tool to name.
            _ => ToolResult.Denied(
                DenialReason.UnsupportedMediaType,
                TypeUnsupported)
        };
    }

    /// <summary>
    ///     Resolves the media type of a file the family can extract a region from.
    /// </summary>
    /// <remarks>
    ///     <b>The croppable set is narrower than the readable set, deliberately.</b> The family
    ///     reads the visual content a vision host can render; it extracts a region only from the
    ///     still raster formats it can decode. Keeping both questions in this one unit is what
    ///     stops the family giving two different answers to "what is this file", and keeping the
    ///     croppable set to the intersection of what the family reads and what it can decode is
    ///     what stops a region request becoming an accidental format conversion for content the
    ///     read tool itself refuses.
    ///     <para>
    ///     The extension is matched case-insensitively, for the same reason
    ///     <see cref="TryResolveMediaType"/> matches it that way.
    ///     </para>
    /// </remarks>
    /// <param name="path">The requested path, whose extension decides the type.</param>
    /// <param name="mediaType">
    ///     On success, the media type the file is read as; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the extension names a type a region can be extracted from;
    ///     otherwise <see langword="false"/>.
    /// </returns>
    public static bool TryResolveCroppableMediaType(string path, [NotNullWhen(true)] out string? mediaType)
    {
        // A missing path is a programming error in the caller; the tool that calls this has
        // already refused an absent path as a malformed request before reaching here.
        ArgumentNullException.ThrowIfNull(path);

        mediaType = Extension(path) switch
        {
            ".png" => Png,
            ".jpg" or ".jpeg" => Jpeg,
            _ => null
        };

        return mediaType is not null;
    }

    /// <summary>
    ///     Composes the refusal for a file whose extension the family cannot extract a region
    ///     from.
    /// </summary>
    /// <remarks>
    ///     Only called once <see cref="TryResolveCroppableMediaType"/> has reported the type
    ///     uncroppable, so the refusal it returns is always an
    ///     <see cref="DenialReason.UnsupportedMediaType"/>. A type the family cannot read at all
    ///     is delegated to <see cref="DenyUnsupportedType"/>, so the family gives one answer to
    ///     "what is this file" rather than two; a type the family reads but cannot cut earns its
    ///     own refusal here, stating what the format is and which formats a region can be taken
    ///     from.
    ///     <para>
    ///     <b>None of the refusals composed here names a sibling tool.</b> The one place a
    ///     media-type refusal may name one is where the naming <em>is</em> the statement of what
    ///     the file is — an <c>.svg</c> is text, so the text reader is its reader. A <c>.gif</c>
    ///     genuinely is an image, so naming the image reader would not classify it; it would
    ///     offer a route to the content this refusal withheld, handing the model a whole image
    ///     after it asked to examine one part of it closely. A model that then described that
    ///     part confidently would be describing what it never examined, which is precisely the
    ///     failure this family exists to prevent.
    ///     </para>
    /// </remarks>
    /// <param name="path">The requested path, whose extension the refusal is chosen from.</param>
    /// <returns>The composed refusal, stating what the content is and what may be cropped.</returns>
    public static object DenyNonCroppableType(string path)
    {
        // A missing path is a programming error in the caller.
        ArgumentNullException.ThrowIfNull(path);

        return Extension(path) switch
        {
            // A file that may hold any number of frames, none of which this family can count.
            ".gif" => ToolResult.Denied(DenialReason.UnsupportedMediaType, GifFrameIsUnknowable),

            // A still raster image the family hands to a provider without ever decoding it.
            ".webp" => ToolResult.Denied(DenialReason.UnsupportedMediaType, WebpNotDecoded),

            // The one admitted type that looks croppable and is not; without the reason stated a
            // model would reasonably retry with different coordinates.
            ".pdf" => ToolResult.Denied(DenialReason.UnsupportedMediaType, PdfRequiresRasterization),

            // Anything the family cannot read at all is classified by the one map that owns that
            // question, so the family answers "what is this file" in a single voice.
            _ => DenyUnsupportedType(path)
        };
    }

    /// <summary>
    ///     Extracts a path's extension, lowered for a case-insensitive comparison.
    /// </summary>
    /// <remarks>
    ///     Lowering with the invariant culture keeps the decision identical on every host,
    ///     rather than varying with a machine's configured culture.
    /// </remarks>
    /// <param name="path">The path whose extension is wanted.</param>
    /// <returns>The lowered extension, including its leading dot, or an empty string.</returns>
    private static string Extension(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant();
    }
}
