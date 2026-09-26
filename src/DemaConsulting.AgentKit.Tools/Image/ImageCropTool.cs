using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.CanvasNet.Canvas;
using DemaConsulting.CanvasNet.Codecs;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Image;

/// <summary>
///     The <c>image_crop</c> tool: returns a rectangular region of one image the access policy
///     permits the agent to read, as a caption followed by the region itself.
/// </summary>
/// <remarks>
///     <para>
///     <b>This tool exists because a model that can see an image still cannot examine part of one
///     closely.</b> A detail occupying a hundredth of a large picture is, at the resolution a
///     provider receives, effectively invisible. Handing the model the region it named — at full
///     resolution, as image content — is what turns "I think that says something" into reading
///     it. The region is named in pixels from the top-left corner, in the coordinate space
///     <see cref="ImageReadTool"/> reports, which is why the two ship as one capability rather
///     than two: a region request the model cannot aim is a region request it will aim wrongly.
///     </para>
///     <para>
///     <b>A region that is not wholly inside the image is refused, never clamped.</b> Clamping
///     would return a different region from the one asked about while reporting success, and the
///     model has no way to detect the substitution: it would then describe, confidently, a part
///     of the picture it never received. The refusal names the image's real dimensions, which is
///     what turns it from a dead end into the fact the model was missing.
///     </para>
///     <para>
///     <b>Nothing is written.</b> The region is returned inline as image content, so this tool
///     adds no write decision, no output path and no filesystem surface of any kind. It is a read
///     capability whose result happens to be derived rather than copied.
///     </para>
///     <para>
///     <b>The decode budget is decided from the header, before any pixel data is allocated.</b>
///     A compressed image well inside the binary ceiling can declare far more pixels than its
///     size suggests, and decoding costs four bytes per pixel whatever the file's own encoding
///     was. This tool therefore reads what the file declares, compares it against both the
///     largest extent it can decode on one axis and the host's pixel ceiling, and refuses before
///     allocating anything — naming both bounds, because a model needs both to form a request
///     that would be accepted.
///     </para>
///     <para>
///     <b>The tool takes its policy at construction, and there is no other way to build it.</b>
///     <see cref="Create"/> is the only factory, it requires a <see cref="PathPolicy"/>, and it
///     builds the tool through <see cref="GuardedToolFactory"/>, on exactly the reasoning
///     recorded for <see cref="ImageReadTool"/>.
///     </para>
///     <para>
///     <b>The delegate is declared <c>Task&lt;object&gt;</c> deliberately.</b> A successful crop
///     returns a caption plus image content — a <see cref="List{T}"/> of
///     <see cref="AIContent"/> — which the underlying function factory would serialize into a
///     <c>JsonElement</c> unless the guarded factory's result passthrough is applied. Serialized,
///     the provider never receives the region, and the model — told a tool returned one — reports
///     that it can see it and then fabricates a description. There is no error to notice. The
///     result is constructed through <see cref="ToolResult.Image"/> and never through
///     <see cref="ToolResult.Structured"/>, which would be serialized to JSON by design.
///     </para>
///     <para>
///     Every refusal is returned rather than thrown, and every message this tool composes itself
///     interpolates only integers and the resolved media type — no absolute path, no permitted
///     location, no directory separator. Only the access policy's own refusal discloses host
///     paths, and it does so deliberately and unchanged. <b>The decoding library's own exception
///     text never reaches a model</b>: it is developer-facing and may echo values read out of the
///     file.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class ImageCropTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     Published as a constant on the unit that defines the tool so that a caller naming it
    ///     cannot drift from the name actually registered.
    /// </remarks>
    public const string ToolName = "image_crop";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    /// <remarks>
    ///     Names <see cref="ImageReadTool"/> as the source of the coordinate space, because that
    ///     is what makes the two tools compose and a description is not a denial — the rule that
    ///     restricts a refusal from naming a sibling tool governs refusals, not the text a model
    ///     reads before it has been refused anything. It carries no exhaustive statement of which
    ///     format variants decode: a rare exception stated here would make a model avoid a
    ///     capability that works on essentially every file it will meet, and the exceptions are
    ///     stated where they are actionable, in the refusals themselves.
    /// </remarks>
    private const string ToolDescription =
        "Returns a rectangular region of a PNG or JPEG file the agent is permitted to read, as "
        + "image content. The region is given in pixels from the top-left corner of the image, "
        + "whose pixel dimensions image_read reports. Paths are relative to the workspace root. "
        + "Returns the cropped region with a caption, or a denial explaining why the request was "
        + "refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    /// <remarks>
    ///     Worded identically to <see cref="ImageReadTool"/>'s, so the family answers the same
    ///     mistake the same way whichever tool the model reached for.
    /// </remarks>
    private const string PathRequired =
        "A file path is required. Supply a path relative to the workspace root, "
        + "for example 'diagram.png'.";

    /// <summary>
    ///     The refusal used when the request omits any part of the region.
    /// </summary>
    /// <remarks>
    ///     Converts what would otherwise be a framework binding error, raised before this tool is
    ///     reached, into something the model can act on. Every region parameter carries a default
    ///     precisely so this refusal is reachable.
    /// </remarks>
    private const string RegionRequired =
        "A crop region is required. Supply x, y, width and height in pixels.";

    /// <summary>
    ///     The refusal used when the region's origin is negative.
    /// </summary>
    /// <remarks>
    ///     States the coordinate convention, which is the one thing about it a model cannot
    ///     observe from the image itself and the most likely reason it produced a negative value.
    /// </remarks>
    private const string OriginMustNotBeNegative =
        "The region's origin is measured in pixels from the top-left corner of the image, so x "
        + "and y must not be negative.";

    /// <summary>
    ///     The refusal used when the region has no extent.
    /// </summary>
    /// <remarks>
    ///     An empty region is a self-contradictory request rather than a resource problem, which
    ///     is why the reason is <see cref="DenialReason.InvalidRequest"/> and not
    ///     <see cref="DenialReason.ResourceTooLarge"/>.
    /// </remarks>
    private const string ExtentMustBePositive =
        "The region's width and height are measured in pixels and must both be greater than "
        + "zero.";

    /// <summary>
    ///     The refusal used when the request names a directory rather than a file.
    /// </summary>
    /// <remarks>
    ///     States the fact and stops, for the same reason <see cref="ImageReadTool"/>'s does.
    /// </remarks>
    private const string PathIsDirectory =
        "The requested path is a directory, not a file.";

    /// <summary>
    ///     The refusal used when the requested file does not exist.
    /// </summary>
    private const string FileNotFound =
        "The requested file does not exist.";

    /// <summary>
    ///     The refusal used when the file exists and is permitted but cannot be read.
    /// </summary>
    private const string FileUnreadable = "The requested file could not be read.";

    /// <summary>
    ///     Creates the <c>image_crop</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="ImagePack"/>, which is the only place the
    ///     <c>image</c> family prefix is claimed. The policy is captured by the returned tool's
    ///     delegate, so the tool cannot later be pointed at a different policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every read this tool performs.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy)
    {
        // A missing policy is a programming error in the composing application, not something a
        // model supplied, so it is surfaced rather than converted into a denial.
        ArgumentNullException.ThrowIfNull(policy);

        // Declared to return Task<object> on purpose; see the type remarks before changing this.
        // Every parameter carries a default so that an omitted argument becomes a refusal this
        // tool composes, rather than a framework error raised before the body is reached.
        var crop = (
                [Description(
                    "The path of the image to take a region of, relative to the workspace root, "
                    + "for example 'diagram.png'.")]
                string? path = null,
                [Description("The region's left edge, in pixels from the image's left edge.")]
                int? x = null,
                [Description("The region's top edge, in pixels from the image's top edge.")]
                int? y = null,
                [Description("The region's width, in pixels.")]
                int? width = null,
                [Description("The region's height, in pixels.")]
                int? height = null,
                CancellationToken cancellationToken = default) =>
            CropAsync(policy, path, x, y, width, height, cancellationToken);

        return GuardedToolFactory.Create(crop, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Extracts one region, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <remarks>
    ///     <b>The order of the checks is the contract, and each step refuses before the next
    ///     allocates anything.</b> The request's own shape is judged first, because a
    ///     self-contradictory region is the model's mistake and needs no file to diagnose. The
    ///     policy decision comes before anything is learned about the file, so a refused path
    ///     never reveals whether it exists. The file's size is judged before it is opened, its
    ///     declared dimensions before it is decoded, and the region against those dimensions
    ///     before a single pixel is allocated.
    /// </remarks>
    /// <param name="policy">The access policy governing the read.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="x">The region's left edge, or null when the model supplied none.</param>
    /// <param name="y">The region's top edge, or null when the model supplied none.</param>
    /// <param name="width">The region's width, or null when the model supplied none.</param>
    /// <param name="height">The region's height, or null when the model supplied none.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The region's content, or a refusal naming its reason.</returns>
    private static async Task<object> CropAsync(
        PathPolicy policy,
        string? path,
        int? x,
        int? y,
        int? width,
        int? height,
        CancellationToken cancellationToken)
    {
        // A malformed request is refused rather than thrown: the model supplied it, so the model
        // is the one that must be told how to correct it.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        // An omitted region reaches here as a null rather than as a framework binding error,
        // which is the whole reason every region parameter carries a default.
        if (x is null || y is null || width is null || height is null)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, RegionRequired);
        }

        // A negative origin or an empty extent is a contradiction in the request itself and needs
        // no file to diagnose, so it is refused before the file system is consulted at all.
        if (x < 0 || y < 0)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, OriginMustNotBeNegative);
        }

        if (width <= 0 || height <= 0)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, ExtentMustBePositive);
        }

        // The single read decision. Resolution and containment both happen inside the policy, so
        // a path outside the permitted location is refused here without this tool making any path
        // judgment of its own.
        if (!policy.TryResolveRead(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // A directory has no image content to take a region of.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsDirectory);
        }

        // The type is decided from the extension before the file system is consulted for size or
        // content, matching the read tool's documented order. A type the family reads but cannot
        // decode, and a type it cannot read at all, are both answered by the one map that owns
        // the question.
        if (!ImageMediaTypes.TryResolveCroppableMediaType(realPath, out var mediaType))
        {
            return ImageMediaTypes.DenyNonCroppableType(realPath);
        }

        // A missing file is refused as such. System.IO.File is qualified because the sibling File
        // tool family occupies the unqualified 'File' name within this assembly.
        if (!System.IO.File.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.TargetNotFound, FileNotFound);
        }

        return await CropPermittedFileAsync(
                policy,
                realPath,
                mediaType,
                new Region(x.Value, y.Value, width.Value, height.Value),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Extracts a region from a file already known to exist, to be permitted, and to carry a
    ///     type a region can be taken from.
    /// </summary>
    /// <remarks>
    ///     The file's size is judged from its directory entry before it is opened, so an
    ///     oversized file is never read into memory merely to discover it was oversized. The
    ///     encoded result is judged against the same ceiling before it is returned, because a
    ///     large region of a compressed source re-encoded losslessly can genuinely exceed a
    ///     ceiling the source file sat well inside.
    /// </remarks>
    /// <param name="policy">The access policy whose ceilings apply.</param>
    /// <param name="realPath">The real location of the permitted file.</param>
    /// <param name="mediaType">The media type the file's extension resolved to.</param>
    /// <param name="region">The region the model asked for.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The region's content, or a refusal naming its reason.</returns>
    private static async Task<object> CropPermittedFileAsync(
        PathPolicy policy,
        string realPath,
        string mediaType,
        Region region,
        CancellationToken cancellationToken)
    {
        byte[] data;

        try
        {
            // Size is judged before the file is opened, so an oversized file is never read into
            // memory merely to discover it was oversized.
            var length = new FileInfo(realPath).Length;
            if (length > policy.Limits.MaxBinaryBytes)
            {
                return ToolResult.Denied(
                    DenialReason.ResourceTooLarge,
                    "The file exceeds the "
                    + Number(policy.Limits.MaxBinaryBytes)
                    + "-byte binary limit.");
            }

            data = await System.IO.File.ReadAllBytesAsync(realPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            // The same explicit classification the read tool uses: a file system failure becomes
            // a refusal the model can act on, while a genuine defect still surfaces.
            return ToolResult.Denied(DenialReason.InvalidRequest, FileUnreadable);
        }

        return CropBytes(policy, data, mediaType, region);
    }

    /// <summary>
    ///     Triages the file's declared dimensions and the requested region, then extracts and
    ///     encodes the region.
    /// </summary>
    /// <remarks>
    ///     <b>Every refusal here is decided before the step that would have made it expensive.</b>
    ///     The header is read first, so a file whose bytes are not of the type its extension
    ///     claims is refused without any size being stated — because none was read. A file the
    ///     header reader reports as one the decoder will not decode is refused next, before any
    ///     budget arithmetic, since no budget could make it decodable. The declared
    ///     size is then checked against the decode budget, so a file declaring more pixels than
    ///     the host sanctioned is refused before a pixel buffer exists. The region is checked
    ///     against the declared size, so a request that could never have been satisfied is
    ///     refused before the decode that would have satisfied nothing. Only then is anything
    ///     decoded.
    /// </remarks>
    /// <param name="policy">The access policy whose ceilings apply.</param>
    /// <param name="data">The file's bytes, as already read.</param>
    /// <param name="mediaType">The media type the file's extension resolved to.</param>
    /// <param name="region">The region the model asked for.</param>
    /// <returns>The region's content, or a refusal naming its reason.</returns>
    private static object CropBytes(PathPolicy policy, byte[] data, string mediaType, Region region)
    {
        // Nothing can be said about a file whose header will not read: it is not content of the
        // type its extension claimed, or it is truncated before its header ends, or it is empty.
        // The refusal states no size, because none was read.
        if (!ImageProbe.TryReadSize(
                data,
                mediaType,
                out var imageWidth,
                out var imageHeight,
                out var canDecode))
        {
            return ToolResult.Denied(
                DenialReason.UnsupportedMediaType,
                "The file is not readable as " + mediaType + " content.");
        }

        // A well-formed file the decoder will not decode is refused in those terms, from the
        // header report rather than from any format knowledge of this library's own. The refusal
        // hands over the declared size directly rather than sending the model to another tool
        // for it, and names no feature, because the report names none.
        if (!canDecode)
        {
            return ToolResult.Denied(
                DenialReason.UnsupportedMediaType,
                "This image is well formed but uses a feature this tool does not decode. The "
                + "image declares " + Size(imageWidth, imageHeight) + " pixels.");
        }

        // The decode budget, decided from the declared dimensions alone. Both bounds are named
        // because a model needs both to form a request that would be accepted: one bounds each
        // axis and the other bounds their product, and neither implies the other. The comparison
        // is done in long arithmetic because the product of two in-range dimensions overflows an
        // int, and the per-axis bound is read from the decoder rather than restated here so the
        // two cannot drift apart.
        if (imageWidth > Surface.MaxDimension
            || imageHeight > Surface.MaxDimension
            || ((long)imageWidth * imageHeight) > policy.Limits.MaxImagePixels)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The image declares " + Size(imageWidth, imageHeight)
                + " pixels. This tool decodes images up to " + Number(Surface.MaxDimension)
                + " pixels on a side and " + Number(policy.Limits.MaxImagePixels)
                + " pixels in total.");
        }

        // The region must lie wholly inside the image. It is REFUSED, never clamped: clamping
        // would return a different region from the one asked about while reporting success, and
        // the model would then describe, confidently, a part of the picture it never received.
        // Naming the image's real dimensions is what turns the refusal into the fact the model
        // was missing. Long arithmetic again, because origin plus extent can overflow an int.
        if ((long)region.X + region.Width > imageWidth
            || (long)region.Y + region.Height > imageHeight)
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "The requested region " + region.Describe()
                + " lies outside the image, which is " + Size(imageWidth, imageHeight)
                + " pixels.");
        }

        return EncodeRegion(policy, data, mediaType, region, imageWidth, imageHeight);
    }

    /// <summary>
    ///     Decodes the image, copies the region out of it and encodes the result.
    /// </summary>
    /// <remarks>
    ///     This is the first step that allocates pixel data, which is why everything decidable
    ///     from the header was decided before it. A decode failure here is a file whose header
    ///     read cleanly and whose body did not, so the refusal states the size — which is exactly
    ///     what distinguishes it from the refusal for a file nothing could be learned about.
    ///     <para>
    ///     <b>The region is encoded as PNG whatever the source was.</b> The encoding is lossless,
    ///     so the model receives the source's pixels rather than a re-compressed approximation of
    ///     them in the very region it asked to examine closely, and it is a format every vision
    ///     provider accepts.
    ///     </para>
    ///     <para>
    ///     A failure inside the encoder, on a pixel buffer this tool constructed, is a defect
    ///     rather than anything a model can provoke, and is allowed to propagate — the same
    ///     dividing line <see cref="IsAccessFailure"/> draws. The encoder therefore sits outside
    ///     the decode's <c>catch</c>: widening that clause to span it would silently report a
    ///     defect as content that could not be decoded.
    ///     </para>
    ///     <para>
    ///     <b>Every pixel buffer this method creates is released.</b> The decoded image is
    ///     disposed as soon as the region has been copied out of it, because the copy is
    ///     independent of its source; the region itself is disposed once it has been encoded,
    ///     which is why the encode precedes the disposal rather than following it.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy whose ceilings apply.</param>
    /// <param name="data">The file's bytes, as already read.</param>
    /// <param name="mediaType">The media type the file's extension resolved to.</param>
    /// <param name="region">The region the model asked for, already known to be inside the image.</param>
    /// <param name="imageWidth">The width the image's header declared.</param>
    /// <param name="imageHeight">The height the image's header declared.</param>
    /// <returns>The region's content, or a refusal naming its reason.</returns>
    private static object EncodeRegion(
        PathPolicy policy,
        byte[] data,
        string mediaType,
        Region region,
        int imageWidth,
        int imageHeight)
    {
        Surface cropped;

        try
        {
            using var source = new MemoryStream(data, writable: false);

            // The decoded image is released as soon as the region has been copied out of it:
            // the copy is independent of its source, so nothing the region needs outlives the
            // surface it came from.
            using var surface = string.Equals(mediaType, ImageMediaTypes.Png, StringComparison.Ordinal)
                ? PngCodec.Load(source)
                : JpegCodec.Load(source);

            cropped = surface.Crop(region.X, region.Y, region.Width, region.Height);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // The header read but the body did not. The size is stated because it was read, which
            // is what makes this refusal distinguishable from the unreadable-header one. The
            // decoder's own message is never surfaced: it is developer-facing and may echo values
            // read out of the file. This clause additionally covers the library's
            // well-formed-but-unsupported exception type, which is the backstop should the
            // header's feasibility report ever lag what the decoder actually accepts.
            return ToolResult.Denied(
                DenialReason.UnsupportedMediaType,
                "The image declares " + Size(imageWidth, imageHeight)
                + " pixels, but its pixel data could not be decoded.");
        }

        // The region is released once it has been encoded. The encoder runs outside the catch
        // above deliberately: a failure on a pixel buffer this tool constructed is a defect, not
        // a refusal.
        using (cropped)
        {
            using var encoded = new MemoryStream();
            PngCodec.Save(cropped, encoded, PngColorType.Rgba);
            var bytes = encoded.ToArray();

            // A large region of a compressed source, re-encoded losslessly, can genuinely exceed
            // a ceiling the source file sat well inside. The same ceiling governs everything this
            // family hands a provider, whatever produced the bytes.
            if (bytes.Length > policy.Limits.MaxBinaryBytes)
            {
                return ToolResult.Denied(
                    DenialReason.ResourceTooLarge,
                    "The cropped image is " + Number(bytes.Length)
                    + " bytes, which exceeds the " + Number(policy.Limits.MaxBinaryBytes)
                    + "-byte binary limit.");
            }

            // The caption restates the region AND the source's dimensions, so a follow-up region
            // can be aimed without reading the whole image again. ToolResult.Image, never
            // ToolResult.Structured: the latter is serialized to JSON by design, which would
            // destroy the content the guarded factory exists to deliver intact.
            return ToolResult.Image(
                bytes,
                ImageMediaTypes.Png,
                "Cropped region " + region.Describe()
                + " of a " + Size(imageWidth, imageHeight) + " " + mediaType
                + " image, returned as " + ImageMediaTypes.Png + ".");
        }
    }

    /// <summary>
    ///     Formats an integer for a message a model reads.
    /// </summary>
    /// <remarks>
    ///     The invariant culture is used so a refusal reads identically on every host, rather
    ///     than acquiring thousands separators a model would then have to interpret.
    /// </remarks>
    /// <param name="value">The value to format.</param>
    /// <returns>The formatted value.</returns>
    private static string Number(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Formats a pair of dimensions the way every message in this family states them.
    /// </summary>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    /// <returns>The formatted dimensions.</returns>
    private static string Size(int width, int height)
    {
        return Number(width) + "x" + Number(height);
    }

    /// <summary>
    ///     Determines whether an exception represents a failure to reach or read a path rather
    ///     than a defect that should be allowed to propagate.
    /// </summary>
    /// <remarks>
    ///     Enumerated explicitly rather than catching everything, so that a null reference or an
    ///     out-of-memory condition still fails loudly during development instead of being
    ///     reported to a model as an unreadable file.
    /// </remarks>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the file could not be reached or
    ///     read; otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsAccessFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException;
    }

    /// <summary>
    ///     The rectangular region a request named, once every part of it is known to be present
    ///     and individually sane.
    /// </summary>
    /// <remarks>
    ///     Carried as one value rather than four loose integers so that the triage steps and the
    ///     messages cannot disagree about which number is which, and so that no method has to
    ///     take four same-typed parameters a caller could transpose.
    /// </remarks>
    /// <param name="X">The region's left edge, in pixels from the image's left edge.</param>
    /// <param name="Y">The region's top edge, in pixels from the image's top edge.</param>
    /// <param name="Width">The region's width, in pixels; always greater than zero.</param>
    /// <param name="Height">The region's height, in pixels; always greater than zero.</param>
    private sealed record Region(int X, int Y, int Width, int Height)
    {
        /// <summary>
        ///     Describes the region the way every message naming it states it.
        /// </summary>
        /// <remarks>
        ///     One form, used by both the success caption and the out-of-bounds refusal, so a
        ///     model reads the same shape whichever it receives.
        /// </remarks>
        /// <returns>The formatted region.</returns>
        public string Describe()
        {
            return Number(X) + "," + Number(Y) + " " + Size(Width, Height);
        }
    }
}
