using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.CanvasNet.Canvas;
using DemaConsulting.CanvasNet.Codecs;

namespace DemaConsulting.AgentKit.Tools.Image;

/// <summary>
///     Admits a file the access policy has already permitted into pixels the host has sanctioned
///     paying for, refusing rather than throwing at every step that could cost more than the host
///     allowed.
/// </summary>
/// <remarks>
///     <para>
///     <b>This helper exists because admitting an image is the same problem for every tool that
///     decodes one, and getting it wrong is a resource-exhaustion defect rather than a cosmetic
///     one.</b> Reading a file within the binary ceiling from the handle its size was taken on,
///     refusing a header that declares more pixels than the host sanctioned <em>before</em> a
///     pixel buffer exists, and distinguishing a header that would not read from a body that
///     would not decode are three decisions no tool in the family may make differently from its
///     neighbor. Stating them once means a later correction reaches every caller, rather than
///     reaching the tool whose copy somebody remembered.
///     </para>
///     <para>
///     <b>The steps are separate on purpose.</b> A tool may need the image's declared dimensions
///     before it decides whether to decode at all — <see cref="ImageCropTool"/> judges the
///     caller's region against them and refuses an impossible request without paying for a
///     decode. Fusing the triage and the decode into one call would move that refusal to the far
///     side of an allocation it exists to avoid, so the two are offered as
///     <see cref="ReadWithinCeilingAsync"/> plus <see cref="TryTriage"/>, and then
///     <see cref="TryDecode"/>.
///     </para>
///     <para>
///     <b>No surface this helper produces is disposed by this helper.</b> A decoded pixel buffer
///     is returned to the caller, which holds it under its own <c>using</c> for exactly as long
///     as it needs it. Ownership is handed over at the return rather than shared, so no tool's
///     memory profile depends on anything here.
///     </para>
///     <para>
///     <b>The header report is reduced to primitives before it arrives.</b> Dimensions and decode
///     feasibility come from <see cref="ImageProbe.TryReadSize"/>, which is the only place in the
///     library the decoding library's own header type is named. Nothing here observes what a file
///     declares about its channels or its transparency: a decode costs four bytes per pixel
///     whatever the file's own encoding was, so a budget computed from a declared channel count
///     would under-count a palette-indexed file by a factor of four — which is exactly the format
///     a hostile caller would reach for.
///     </para>
///     <para>
///     <b>Neither the decoding library's own exception text nor the operating system's ever
///     reaches a model.</b> Both are developer-facing and may echo values read out of the file or
///     details of the host's own layout, so every refusal composed here is this library's own
///     wording carrying at most an integer and the resolved media type.
///     </para>
///     <para>
///     The class is static and stateless and is therefore safe for concurrent use from any number
///     of threads.
///     </para>
/// </remarks>
internal static class ImageAdmission
{
    /// <summary>
    ///     The refusal used when a file that exists and is permitted cannot be read.
    /// </summary>
    /// <remarks>
    ///     Deliberately carries no remedy: the helper does not know why the operating system
    ///     refused, and a guess would be worse than silence.
    /// </remarks>
    internal const string FileUnreadable = "The requested file could not be read.";

    /// <summary>
    ///     Reads a permitted file's bytes, refusing one larger than the policy's binary ceiling
    ///     before any of its content is read.
    /// </summary>
    /// <remarks>
    ///     The file is opened once and the size the ceiling is judged against is read from that
    ///     same open handle rather than from a separate directory lookup. Judging a size and then
    ///     reopening the path to read it would leave a window in which the file could grow or be
    ///     replaced, and the larger content would be loaded despite the ceiling. Exactly the
    ///     count that was validated is then read, so a file growing under the read cannot enlarge
    ///     what is loaded; a file that shrinks instead ends the read early, which surfaces as an
    ///     <see cref="EndOfStreamException"/> — an <see cref="IOException"/>, and therefore
    ///     already an access failure by <see cref="IsAccessFailure"/>.
    ///     <para>
    ///     The result is a union rather than a pair of out parameters because the method is
    ///     asynchronous: a refusal is a string, as every refusal in this library is, and the
    ///     bytes are a <see cref="T:System.Byte[]"/>, so the caller discriminates by type without
    ///     a nullable it has to prove is not null.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy whose binary ceiling applies.</param>
    /// <param name="realPath">The real location of the already-permitted file.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>
    ///     The file's bytes as a <see cref="T:System.Byte[]"/> when it was read, or a refusal
    ///     naming its reason.
    /// </returns>
    internal static async Task<object> ReadWithinCeilingAsync(
        PathPolicy policy,
        string realPath,
        CancellationToken cancellationToken)
    {
        try
        {
            // The file is opened once, and the size the ceiling is judged against is read from
            // that same open handle rather than from a separate directory lookup.
            await using var stream = new FileStream(
                realPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            // Nothing is allocated before the ceiling has passed, so an oversized file is still
            // never read into memory merely to discover it was oversized.
            var length = stream.Length;
            if (length > policy.Limits.MaxBinaryBytes)
            {
                return ToolResult.Denied(
                    DenialReason.ResourceTooLarge,
                    "The file exceeds the "
                    + Number(policy.Limits.MaxBinaryBytes)
                    + "-byte binary limit.");
            }

            // Exactly the count that was validated is read, so a file growing under the read
            // cannot enlarge what is loaded.
            var data = new byte[length];
            await stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
            return data;
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            // A file system failure becomes a refusal the model can act on, while a genuine
            // defect still surfaces.
            return ToolResult.Denied(DenialReason.InvalidRequest, FileUnreadable);
        }
    }

    /// <summary>
    ///     Reads what a file declares about itself and decides, from that declaration alone,
    ///     whether decoding it is something this host sanctioned.
    /// </summary>
    /// <remarks>
    ///     <b>Every refusal here is decided before the step that would have made it expensive.</b>
    ///     The header is read first, so a file whose bytes are not of the type its extension
    ///     claims is refused without any size being stated — because none was read. A file the
    ///     header reader reports as one the decoder will not decode is refused next, before any
    ///     budget arithmetic, since no budget could make it decodable. The declared size is then
    ///     checked against the decode budget, so a file declaring more pixels than the host
    ///     sanctioned is refused before a pixel buffer exists.
    ///     <para>
    ///     Both bounds are named in the budget refusal because a model needs both to form a
    ///     request that would be accepted: one bounds each axis and the other bounds their
    ///     product, and neither implies the other. The comparison is done in long arithmetic
    ///     because the product of two in-range dimensions overflows an <see cref="int"/>, and the
    ///     per-axis bound is read from the decoder rather than restated here so the two cannot
    ///     drift apart.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy whose decode budget applies.</param>
    /// <param name="data">The file's bytes, as already read.</param>
    /// <param name="mediaType">The media type the file's extension resolved to.</param>
    /// <param name="width">On success, the width in pixels the header declares; otherwise zero.</param>
    /// <param name="height">On success, the height in pixels the header declares; otherwise zero.</param>
    /// <param name="denial">
    ///     On refusal, the composed refusal naming its reason; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the file declares a size this host will decode; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    internal static bool TryTriage(
        PathPolicy policy,
        byte[] data,
        string mediaType,
        out int width,
        out int height,
        [NotNullWhen(false)] out object? denial)
    {
        // Nothing can be said about a file whose header will not read: it is not content of the
        // type its extension claimed, or it is truncated before its header ends, or it is empty.
        // The refusal states no size, because none was read.
        if (!ImageProbe.TryReadSize(data, mediaType, out width, out height, out var canDecode))
        {
            denial = ToolResult.Denied(
                DenialReason.UnsupportedMediaType,
                "The file is not readable as " + mediaType + " content.");
            return false;
        }

        // A well-formed file the decoder will not decode is refused in those terms, from the
        // header report rather than from any format knowledge of this library's own. The refusal
        // hands over the declared size directly rather than sending the model to another tool
        // for it, and names no feature, because the report names none.
        if (!canDecode)
        {
            denial = ToolResult.Denied(
                DenialReason.UnsupportedMediaType,
                "This image is well formed but uses a feature this tool does not decode. The "
                + "image declares " + Size(width, height) + " pixels.");
            return false;
        }

        // The decode budget, decided from the declared dimensions alone and therefore before any
        // pixel data exists.
        if (width > Surface.MaxDimension
            || height > Surface.MaxDimension
            || ((long)width * height) > policy.Limits.MaxImagePixels)
        {
            denial = ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The image declares " + Size(width, height)
                + " pixels. This tool decodes images up to " + Number(Surface.MaxDimension)
                + " pixels on a side and " + Number(policy.Limits.MaxImagePixels)
                + " pixels in total.");
            return false;
        }

        denial = null;
        return true;
    }

    /// <summary>
    ///     Decodes a file whose declared size has already been admitted, into the pixel buffer
    ///     the caller then owns.
    /// </summary>
    /// <remarks>
    ///     This is the first step that allocates pixel data, which is why everything decidable
    ///     from the header was decided before it. A failure here is a file whose header read
    ///     cleanly and whose body did not, so the refusal states the size — which is exactly what
    ///     distinguishes it from the refusal for a file nothing could be learned about. The
    ///     catch additionally covers the library's well-formed-but-unsupported exception type,
    ///     which is the backstop should the header's feasibility report ever lag what the decoder
    ///     actually accepts.
    ///     <para>
    ///     <b>The returned surface belongs to the caller.</b> Nothing here disposes it, because
    ///     nothing here knows how long the caller needs it; every caller takes it under a
    ///     <c>using</c>.
    ///     </para>
    /// </remarks>
    /// <param name="data">The file's bytes, as already read.</param>
    /// <param name="mediaType">The media type the file's extension resolved to.</param>
    /// <param name="width">The width the image's header declared.</param>
    /// <param name="height">The height the image's header declared.</param>
    /// <param name="surface">
    ///     On success, the decoded pixel buffer, which the caller disposes; otherwise
    ///     <see langword="null"/>.
    /// </param>
    /// <param name="denial">
    ///     On refusal, the composed refusal naming its reason; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the file's pixel data was decoded; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    internal static bool TryDecode(
        byte[] data,
        string mediaType,
        int width,
        int height,
        [NotNullWhen(true)] out Surface? surface,
        [NotNullWhen(false)] out object? denial)
    {
        try
        {
            using var source = new MemoryStream(data, writable: false);

            surface = string.Equals(mediaType, ImageMediaTypes.Png, StringComparison.Ordinal)
                ? PngCodec.Load(source)
                : JpegCodec.Load(source);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // The header read but the body did not. The size is stated because it was read, which
            // is what makes this refusal distinguishable from the unreadable-header one. The
            // decoder's own message is never surfaced: it is developer-facing and may echo values
            // read out of the file.
            surface = null;
            denial = ToolResult.Denied(
                DenialReason.UnsupportedMediaType,
                "The image declares " + Size(width, height)
                + " pixels, but its pixel data could not be decoded.");
            return false;
        }

        denial = null;
        return true;
    }

    /// <summary>
    ///     Formats an integer for a message a model reads.
    /// </summary>
    /// <remarks>
    ///     The invariant culture is used so a refusal reads identically on every host, rather
    ///     than acquiring thousands separators a model would then have to interpret. Stated once
    ///     here, beside the refusals that carry most of the family's numbers, so no two tools can
    ///     state a ceiling in two dialects.
    /// </remarks>
    /// <param name="value">The value to format.</param>
    /// <returns>The formatted value.</returns>
    internal static string Number(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Formats a pair of dimensions the way every message in this family states them.
    /// </summary>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    /// <returns>The formatted dimensions.</returns>
    internal static string Size(int width, int height)
    {
        return Number(width) + "x" + Number(height);
    }

    /// <summary>
    ///     Determines whether an exception represents a failure to reach, read or write a path
    ///     rather than a defect that should be allowed to propagate.
    /// </summary>
    /// <remarks>
    ///     Enumerated explicitly rather than catching everything, so that a null reference or an
    ///     out-of-memory condition still fails loudly during development instead of being
    ///     reported to a model as an unreadable file. Stated once and consulted by
    ///     <see cref="ImageDestination"/> as well, so the family draws one line between a file
    ///     system failure a model can act on and a defect a developer must fix.
    /// </remarks>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the path could not be reached, read or
    ///     written; otherwise <see langword="false"/>.
    /// </returns>
    internal static bool IsAccessFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException;
    }
}
