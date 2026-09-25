using DemaConsulting.CanvasNet.Codecs;

namespace DemaConsulting.AgentKit.Tools.Image;

/// <summary>
///     Reads the facts an image file declares about itself in its header, without decoding any
///     pixel data.
/// </summary>
/// <remarks>
///     <para>
///     <b>This helper exists so the family can tell a model how big an image is.</b> A model that
///     is going to ask for a region of an image has no other way to learn the coordinate space
///     that region lives in: it cannot see the picture, and a guess produces a request that is
///     either refused or — worse, in a library that clamped — silently answered for a different
///     region than the one asked about. Reporting the declared size is what makes a region
///     request the model can aim.
///     </para>
///     <para>
///     <b>Only a header is read; nothing is decoded.</b> The header of a PNG is 33 bytes and the
///     header of a JPEG is a bounded scan of leading marker segments. No pixel buffer is
///     allocated by anything in this type, which is what lets a caller consult it <em>before</em>
///     deciding whether decoding the file is affordable at all.
///     </para>
///     <para>
///     <b>Every probe is offered, never required.</b> A header this library cannot read costs the
///     caller the dimensions and nothing else: the methods report failure rather than throwing,
///     because for the read tool the enrichment is optional, and for the crop tool the failure is
///     a refusal the tool composes in its own words. Three input classes make this a live rule
///     rather than a defensive one: <c>gif</c>, <c>webp</c> and <c>pdf</c> are types the family
///     reads and this helper has no probe for at all; a truncated or malformed header may still
///     render at a provider; and a JPEG whose frame header sits beyond the bounded probe cap is
///     perfectly decodable yet has no readable header here.
///     </para>
///     <para>
///     <b>No type from the decoding library crosses this boundary.</b> The methods take bytes and
///     report integers and booleans, so the decoder stays an implementation detail of the family
///     rather than appearing in any signature a consuming application or the generated API
///     reference would see.
///     </para>
///     <para>
///     The class is static and stateless and is therefore safe for concurrent use from any number
///     of threads.
///     </para>
/// </remarks>
internal static class ImageProbe
{
    /// <summary>
    ///     The offset, in bytes from the start of a PNG file, of the interlace-method byte.
    /// </summary>
    /// <remarks>
    ///     A PNG begins with an 8-byte signature, then the <c>IHDR</c> chunk frame: a 4-byte
    ///     length and a 4-byte type, followed by the 13-byte payload
    ///     <c>width(4) height(4) bitDepth(1) colorType(1) compression(1) filter(1)
    ///     interlace(1)</c>. The interlace byte is therefore the last payload byte, at
    ///     <c>8 + 4 + 4 + 12</c>.
    /// </remarks>
    private const int PngInterlaceMethodOffset = 28;

    /// <summary>
    ///     Reads the pixel dimensions an image file declares in its header.
    /// </summary>
    /// <remarks>
    ///     Dispatches on the media type the family resolved from the file's extension rather than
    ///     sniffing the bytes, because the extension is what decided how the content will be
    ///     presented and a file whose bytes disagree with its extension is a file the caller
    ///     should be told about rather than one this helper should quietly reinterpret. A media
    ///     type with no header probe — the animated and paginated types the family reads but does
    ///     not decode — simply reports failure, which is the same outcome as an unreadable header
    ///     and needs no separate handling by a caller.
    ///     <para>
    ///     The probe reads from the bytes the caller already holds, so no second read of the file
    ///     occurs and there is no window in which the bytes returned and the bytes probed could
    ///     differ.
    ///     </para>
    /// </remarks>
    /// <param name="data">The file's bytes, as already read.</param>
    /// <param name="mediaType">The media type the file's extension resolved to.</param>
    /// <param name="width">On success, the width in pixels the header declares; otherwise zero.</param>
    /// <param name="height">On success, the height in pixels the header declares; otherwise zero.</param>
    /// <returns>
    ///     <see langword="true"/> when the header was read and declared its dimensions;
    ///     <see langword="false"/> when the media type carries no probe, or the header could not
    ///     be read.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="data"/> or <paramref name="mediaType"/> is
    ///     <see langword="null"/>.
    /// </exception>
    internal static bool TryReadSize(byte[] data, string mediaType, out int width, out int height)
    {
        // Missing arguments are a programming error in the calling tool, not anything a model can
        // provoke: by the time a tool reaches here it has read a permitted file and resolved its
        // type.
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(mediaType);

        width = 0;
        height = 0;

        try
        {
            // The probe reads the bytes already in hand rather than reopening the file, so the
            // content probed is exactly the content the caller will return or decode.
            using var stream = new MemoryStream(data, writable: false);

            // Dispatch on the resolved media type. A type with no probe simply reports failure,
            // which is the same outcome a caller sees for an unreadable header and therefore
            // needs no separate handling.
            ImageInfo info;
            if (string.Equals(mediaType, ImageMediaTypes.Png, StringComparison.Ordinal))
            {
                info = PngCodec.GetInfo(stream);
            }
            else if (string.Equals(mediaType, ImageMediaTypes.Jpeg, StringComparison.Ordinal))
            {
                info = JpegCodec.GetInfo(stream);
            }
            else
            {
                return false;
            }

            width = info.Width;
            height = info.Height;
            return true;
        }
        catch (Exception exception) when (IsUnreadableHeader(exception))
        {
            // An unreadable header costs the caller the dimensions and nothing else. The
            // exception's own text is never surfaced: it is developer-facing and may echo values
            // read out of the file, neither of which belongs in a model's transcript.
            return false;
        }
    }

    /// <summary>
    ///     Determines whether a PNG file declares that its pixels are stored interlaced.
    /// </summary>
    /// <remarks>
    ///     <b>Read from the header rather than inferred from a failed decode.</b> A decoder
    ///     reports an interlaced file and a corrupt file with the same exception type, so the two
    ///     are indistinguishable after the fact, and matching on an exception's message would be
    ///     both fragile and a route for developer-facing text to reach a model. One named byte
    ///     offset lets the caller tell a model the true, specific reason instead of leaving it to
    ///     guess whether its file is damaged.
    ///     <para>
    ///     Call only for a PNG whose header has already been read successfully, since that is what
    ///     establishes that the signature, the chunk length and the header's own checksum are all
    ///     sound and therefore that the byte at this offset is the one the specification puts
    ///     there. A file too short to contain a header reports <see langword="false"/>, leaving
    ///     the caller's ordinary unreadable-content path to handle it.
    ///     </para>
    /// </remarks>
    /// <param name="data">The PNG file's bytes, as already read.</param>
    /// <returns>
    ///     <see langword="true"/> when the header declares an interlace method other than none;
    ///     otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="data"/> is <see langword="null"/>.
    /// </exception>
    internal static bool IsInterlacedPng(byte[] data)
    {
        // A missing argument is a programming error in the calling tool.
        ArgumentNullException.ThrowIfNull(data);

        // A file too short to hold a header cannot be judged here; the caller's unreadable-header
        // path owns that case, and reporting false keeps this method from claiming otherwise.
        if (data.Length <= PngInterlaceMethodOffset)
        {
            return false;
        }

        // Zero is the specification's "no interlacing"; the only other defined value is Adam7.
        return data[PngInterlaceMethodOffset] != 0;
    }

    /// <summary>
    ///     Determines whether an exception raised while probing a header means the header could
    ///     not be read, rather than a defect that should be allowed to propagate.
    /// </summary>
    /// <remarks>
    ///     Enumerated explicitly rather than catching everything, so that an out-of-memory
    ///     condition or a null reference still fails loudly during development instead of being
    ///     reported as a file whose size could not be established.
    ///     <see cref="InvalidDataException"/> is what a codec raises for content that is not of
    ///     its format or is malformed; <see cref="IOException"/> covers a header that ends before
    ///     the codec has read all of it.
    /// </remarks>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the header could not be read;
    ///     otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsUnreadableHeader(Exception exception)
    {
        return exception is InvalidDataException or IOException;
    }
}
