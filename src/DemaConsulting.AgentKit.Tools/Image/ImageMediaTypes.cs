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
