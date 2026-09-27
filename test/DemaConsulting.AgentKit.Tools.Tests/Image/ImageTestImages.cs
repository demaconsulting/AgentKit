using System.IO.Compression;
using System.Text;
using DemaConsulting.CanvasNet.Canvas;
using DemaConsulting.CanvasNet.Codecs;

namespace DemaConsulting.AgentKit.Tools.Tests.Image;

/// <summary>
///     Builds the image files the image family's tests are run against.
/// </summary>
/// <remarks>
///     <para>
///     <b>No binary fixture is committed.</b> Every file a test needs is produced here, which
///     keeps each fixture's exact shape — its dimensions, its color type, its declared size and
///     whether its pixel data is even present — stated in code beside the test that depends on
///     it, rather than hidden inside a blob nobody can read in review.
///     </para>
///     <para>
///     The well-formed images are produced through the same decoder the family itself uses, so a
///     test asserting on pixels is asserting against content the library can genuinely produce.
///     The hostile and variant images are hand-built byte arrays, because no encoder will produce
///     a header that declares eight thousand pixels on a side and then supplies no pixel data at
///     all — and that file is precisely what proves a decode budget is judged from the header
///     rather than after the fact.
///     </para>
///     <para>
///     <b>A hand-built PNG must be genuinely well-formed as far as it goes.</b> The header's
///     CRC-32 is validated by any reader, so a synthetic header without a correct one is rejected
///     as corrupt rather than as oversized — and an oversized-image test would then pass for
///     entirely the wrong reason. Pixel data likewise needs a correct zlib wrapper with a valid
///     Adler-32 checksum over the uncompressed scanlines; a bare deflate stream is refused. Both
///     routines are therefore carried here rather than approximated.
///     </para>
/// </remarks>
internal static class ImageTestImages
{
    /// <summary>
    ///     How far an anti-aliased ring is nudged from the background, per channel.
    /// </summary>
    /// <remarks>
    ///     Chosen to sit strictly inside the trim tolerance of eight and strictly outside a
    ///     tolerance of zero, which is what lets one fixture distinguish the two.
    /// </remarks>
    internal const int AntiAliasBlend = 4;

    /// <summary>
    ///     How far a dithered background pixel may sit from the stated background, per channel.
    /// </summary>
    /// <remarks>
    ///     Three, so the largest difference between any two dithered pixels is six — inside the
    ///     trim tolerance of eight, so a tolerant classifier still sees one background, while a
    ///     classifier tolerating nothing sees almost none.
    /// </remarks>
    internal const int DitherAmplitude = 3;

    /// <summary>
    ///     The eight-byte signature every PNG file begins with.
    /// </summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    ///     The four-entry palette a palette-indexed fixture resolves its indices through.
    /// </summary>
    /// <remarks>
    ///     Red, green, blue and yellow: chosen so that a decoded pixel identifies which palette
    ///     entry it came from at a glance, which is what makes a palette-resolution assertion
    ///     readable.
    /// </remarks>
    private static readonly byte[] Palette =
    [
        255, 0, 0,
        0, 255, 0,
        0, 0, 255,
        255, 255, 0
    ];

    /// <summary>
    ///     The table used by the PNG checksum routine.
    /// </summary>
    /// <remarks>
    ///     Built once and shared, because every chunk of every fixture carries a checksum computed from it.
    /// </remarks>
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    /// <summary>
    ///     Builds a well-formed PNG whose every pixel is distinguishable from every other.
    /// </summary>
    /// <remarks>
    ///     The pixel values are a function of the coordinate, including the alpha channel, so a
    ///     region extracted from this image can be checked pixel by pixel against the region it
    ///     was supposed to come from — which is the only demonstrable form of "the pixels were
    ///     preserved exactly". Distinct alpha values matter: an encoder that silently discarded
    ///     alpha would otherwise pass.
    /// </remarks>
    /// <param name="width">The width of the image, in pixels.</param>
    /// <param name="height">The height of the image, in pixels.</param>
    /// <returns>The encoded PNG file.</returns>
    internal static byte[] Png(int width, int height)
    {
        using var surface = BuildDistinguishableSurface(width, height);

        using var stream = new MemoryStream();
        PngCodec.Save(surface, stream, PngColorType.Rgba);
        return stream.ToArray();
    }

    /// <summary>
    ///     Builds a well-formed JPEG of the requested size.
    /// </summary>
    /// <remarks>
    ///     JPEG is lossy, so no test asserts on this image's individual pixels; what it exists to
    ///     prove is that a source of a second format is accepted and that the region comes back
    ///     in the family's own output format rather than the source's.
    /// </remarks>
    /// <param name="width">The width of the image, in pixels.</param>
    /// <param name="height">The height of the image, in pixels.</param>
    /// <returns>The encoded JPEG file.</returns>
    internal static byte[] Jpeg(int width, int height)
    {
        using var surface = BuildDistinguishableSurface(width, height);

        using var stream = new MemoryStream();
        JpegCodec.Save(surface, stream, quality: 90);
        return stream.ToArray();
    }

    /// <summary>
    ///     Builds a PNG that declares a size in its header and then supplies no pixel data at all.
    /// </summary>
    /// <remarks>
    ///     <b>The absence of pixel data is the point.</b> A tool that triaged an image's declared
    ///     size before decoding refuses this file for its size; a tool that decoded first would
    ///     refuse it as undecodable instead. The two refusals are distinguishable, so this
    ///     fixture is what turns "decided from the declared dimensions alone" into something a
    ///     test can observe rather than something a reader has to take on trust.
    /// </remarks>
    /// <param name="width">The width the header declares, in pixels.</param>
    /// <param name="height">The height the header declares, in pixels.</param>
    /// <param name="colorType">
    ///     The PNG color type the header declares. Defaults to 2, truecolor. Pass 3 for
    ///     palette-indexed, which a header reader reports as carrying a single channel.
    /// </param>
    /// <returns>The signature and header chunk, and nothing further.</returns>
    internal static byte[] PngHeaderOnly(int width, int height, byte colorType = 2)
    {
        return Concat(PngSignature, HeaderChunk(width, height, colorType, interlace: 0));
    }

    /// <summary>
    ///     Builds a well-formed, decodable PNG whose pixels are stored as palette indices.
    /// </summary>
    /// <remarks>
    ///     A header reader reports this file as carrying one channel per pixel, because that is
    ///     what the file stores — while decoding it produces four bytes per pixel, because every
    ///     index is resolved through the palette into RGBA. The gap between those two numbers is
    ///     a factor of four, which is exactly why a decode budget computed from a declared
    ///     channel count would under-count this format. This fixture's decoded pixels also pin
    ///     the palette resolution itself.
    /// </remarks>
    /// <param name="width">The width of the image, in pixels.</param>
    /// <param name="height">The height of the image, in pixels.</param>
    /// <returns>The encoded PNG file.</returns>
    internal static byte[] PalettizedPng(int width, int height)
    {
        // One filter byte per scanline followed by one palette index per pixel. Filter type 0 is
        // "no filtering", which keeps the bytes readable against the expectation below.
        var raw = new byte[height * (1 + width)];
        var offset = 0;
        for (var y = 0; y < height; y++)
        {
            raw[offset++] = 0;
            for (var x = 0; x < width; x++)
            {
                raw[offset++] = (byte)((x + y) % 4);
            }
        }

        return Concat(
            PngSignature,
            HeaderChunk(width, height, colorType: 3, interlace: 0),
            Chunk("PLTE", Palette),
            Chunk("IDAT", ZlibCompress(raw)),
            Chunk("IEND", []));
    }

    /// <summary>
    ///     Builds a PNG whose header declares Adam7 interlacing.
    /// </summary>
    /// <remarks>
    ///     Interlacing is the one thing a well-formed, specification-conforming PNG can declare
    ///     that this library will not decode, so it is the one case where a file is entirely
    ///     sound and still has to be refused. The file is complete — header, pixel data and
    ///     terminator — so that nothing but the interlace declaration can be what a refusal is
    ///     responding to. The pixel data is not itself laid out in Adam7 passes, which no code
    ///     path under test reaches: the header reader reports the file as one the decoder will
    ///     not decode, and the tool refuses on that report, long before the layout would matter.
    /// </remarks>
    /// <param name="width">The width of the image, in pixels.</param>
    /// <param name="height">The height of the image, in pixels.</param>
    /// <returns>The encoded PNG file.</returns>
    internal static byte[] Adam7InterlacedPng(int width, int height)
    {
        // One filter byte per scanline followed by three bytes per pixel, as a truecolor file.
        var raw = new byte[height * (1 + (width * 3))];

        return Concat(
            PngSignature,
            HeaderChunk(width, height, colorType: 2, interlace: 1),
            Chunk("IDAT", ZlibCompress(raw)),
            Chunk("IEND", []));
    }

    /// <summary>
    ///     Builds a PNG whose header is sound but whose pixel data stops part-way through.
    /// </summary>
    /// <remarks>
    ///     The distinction this fixture exists to draw is between a file nothing could be learned
    ///     about and a file whose size was learned before its content failed. Its header reads
    ///     cleanly, so a refusal can — and must — state the size; its body does not, so the
    ///     refusal must still be a refusal.
    /// </remarks>
    /// <param name="width">The width the header declares, in pixels.</param>
    /// <param name="height">The height the header declares, in pixels.</param>
    /// <returns>The leading part of an otherwise well-formed PNG file.</returns>
    internal static byte[] TruncatedBodyPng(int width, int height)
    {
        var complete = Png(width, height);

        // Keep the signature and the whole header chunk — the first 33 bytes — plus a few bytes
        // of what follows, so the header reads cleanly and the pixel data cannot.
        return complete[..40];
    }

    /// <summary>
    ///     Builds a well-formed PNG carrying a solid block of content on a solid background.
    /// </summary>
    /// <remarks>
    ///     <b>The fixture whose correct trimming is arithmetic rather than judgment.</b> Both
    ///     colors are stated by the caller, so a tool that assumed the background was white fails
    ///     on every instance built with a dark, colored or transparent one — and the content
    ///     block's rectangle is exactly the content region a trim must find, so the expected
    ///     answer is known before the library is asked. Placing the block against an edge makes
    ///     the same builder produce the flush-to-edge cases, where a minority of the border is
    ///     content and the padding has nowhere to expand into.
    /// </remarks>
    /// <param name="width">The width of the image, in pixels.</param>
    /// <param name="height">The height of the image, in pixels.</param>
    /// <param name="background">The color every pixel outside the content block carries.</param>
    /// <param name="content">The rectangle the content block occupies.</param>
    /// <param name="contentColor">The color every pixel inside the content block carries.</param>
    /// <returns>The encoded PNG file.</returns>
    internal static byte[] ContentOnBackgroundPng(
        int width,
        int height,
        Rgba32 background,
        Rectangle content,
        Rgba32 contentColor)
    {
        using var surface = BuildContentSurface(width, height, background, content, contentColor);

        using var stream = new MemoryStream();
        PngCodec.Save(surface, stream, PngColorType.Rgba);
        return stream.ToArray();
    }

    /// <summary>
    ///     Builds a well-formed JPEG carrying a solid block of content on a solid background.
    /// </summary>
    /// <remarks>
    ///     JPEG is lossy, and the loss is concentrated exactly where this fixture is interesting:
    ///     the hard edge between the block and the flat field rings by a few counts on each side
    ///     of it. That ringing is what a trim tolerance has to absorb, and this is the fixture
    ///     that produces it from a real encoder rather than from a hand-written approximation of
    ///     one.
    /// </remarks>
    /// <param name="width">The width of the image, in pixels.</param>
    /// <param name="height">The height of the image, in pixels.</param>
    /// <param name="background">The color every pixel outside the content block carries.</param>
    /// <param name="content">The rectangle the content block occupies.</param>
    /// <param name="contentColor">The color every pixel inside the content block carries.</param>
    /// <returns>The encoded JPEG file.</returns>
    internal static byte[] ContentOnBackgroundJpeg(
        int width,
        int height,
        Rgba32 background,
        Rectangle content,
        Rgba32 contentColor)
    {
        using var surface = BuildContentSurface(width, height, background, content, contentColor);

        using var stream = new MemoryStream();
        JpegCodec.Save(surface, stream, quality: 90);
        return stream.ToArray();
    }

    /// <summary>
    ///     Builds a well-formed PNG whose every pixel is the same color.
    /// </summary>
    /// <remarks>
    ///     The image with no content at all. A trim has no honest region to return for it, so
    ///     this fixture is what makes the refusal observable rather than asserted about in the
    ///     abstract. The color is the caller's and is deliberately never white in the scenarios
    ///     that use it, so a tool that special-cased white would not escape through this one.
    /// </remarks>
    /// <param name="width">The width of the image, in pixels.</param>
    /// <param name="height">The height of the image, in pixels.</param>
    /// <param name="color">The color every pixel carries.</param>
    /// <returns>The encoded PNG file.</returns>
    internal static byte[] UniformPng(int width, int height, Rgba32 color)
    {
        using var surface = new Surface(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                surface[x, y] = color;
            }
        }

        using var stream = new MemoryStream();
        PngCodec.Save(surface, stream, PngColorType.Rgba);
        return stream.ToArray();
    }

    /// <summary>
    ///     Builds a well-formed PNG whose content block is surrounded by a one-pixel ring blended
    ///     slightly toward the content, as an anti-aliased edge is.
    /// </summary>
    /// <remarks>
    ///     <b>The fixture that makes the trim tolerance falsifiable.</b> The ring differs from
    ///     the background by <see cref="AntiAliasBlend"/> counts per channel, which is inside a
    ///     tolerance of eight and outside a tolerance of zero. A tool that tolerates nothing
    ///     therefore classifies the ring as content and returns a region one pixel larger on
    ///     every side than the content block; a tool that tolerates eight returns the block
    ///     itself. The two answers differ by a measurable amount, so the tolerance is proven to
    ///     be load-bearing rather than merely present.
    /// </remarks>
    /// <param name="width">The width of the image, in pixels.</param>
    /// <param name="height">The height of the image, in pixels.</param>
    /// <param name="background">The color every pixel outside the ring carries.</param>
    /// <param name="content">The rectangle the solid content block occupies.</param>
    /// <param name="contentColor">The color every pixel inside the content block carries.</param>
    /// <returns>The encoded PNG file.</returns>
    internal static byte[] AntiAliasedContentPng(
        int width,
        int height,
        Rgba32 background,
        Rectangle content,
        Rgba32 contentColor)
    {
        using var surface = BuildContentSurface(width, height, background, content, contentColor);

        // The ring is the one-pixel border immediately outside the content block, nudged toward
        // the content by a fixed count per channel. Nudging rather than interpolating keeps the
        // difference from the background exact and therefore assertable.
        var ring = new Rgba32(
            Nudge(background.R, contentColor.R),
            Nudge(background.G, contentColor.G),
            Nudge(background.B, contentColor.B),
            Nudge(background.A, contentColor.A));

        for (var y = content.Y - 1; y <= content.Y + content.Height; y++)
        {
            for (var x = content.X - 1; x <= content.X + content.Width; x++)
            {
                // Inside the block, and outside the image, are both left alone.
                if (x < 0 || y < 0 || x >= width || y >= height)
                {
                    continue;
                }

                if (x >= content.X && x < content.X + content.Width
                    && y >= content.Y && y < content.Y + content.Height)
                {
                    continue;
                }

                surface[x, y] = ring;
            }
        }

        using var stream = new MemoryStream();
        PngCodec.Save(surface, stream, PngColorType.Rgba);
        return stream.ToArray();
    }

    /// <summary>
    ///     Builds a well-formed PNG whose background is dithered by a fixed function of the
    ///     coordinate, across the whole image including its border.
    /// </summary>
    /// <remarks>
    ///     <b>The fixture that reproduces the trim tolerance's own stated failure.</b> No two
    ///     background pixels are reliably the same exact color, so a tool that tolerates nothing
    ///     classifies almost every background pixel as content, the region becomes the whole
    ///     image and the trim is defeated entirely. Every dithered value stays within
    ///     <see cref="DitherAmplitude"/> of the stated background, so the largest difference
    ///     between any two of them is twice that — still inside a tolerance of eight, which is
    ///     what makes the correct answer the content block. The dither covers the border too, so
    ///     the background sampling meets the noise as well as the classifier does.
    /// </remarks>
    /// <param name="width">The width of the image, in pixels.</param>
    /// <param name="height">The height of the image, in pixels.</param>
    /// <param name="background">The color the dithered background varies around.</param>
    /// <param name="content">The rectangle the content block occupies.</param>
    /// <param name="contentColor">The color every pixel inside the content block carries.</param>
    /// <returns>The encoded PNG file.</returns>
    internal static byte[] DitheredBackgroundPng(
        int width,
        int height,
        Rgba32 background,
        Rectangle content,
        Rgba32 contentColor)
    {
        using var surface = new Surface(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x >= content.X && x < content.X + content.Width
                    && y >= content.Y && y < content.Y + content.Height)
                {
                    surface[x, y] = contentColor;
                    continue;
                }

                // A fixed function of the coordinate, so the fixture is byte-identical on every
                // run. The two multipliers are coprime with the modulus, which spreads the
                // offsets evenly rather than banding them into stripes.
                var offset = (((x * 3) + (y * 5)) % ((DitherAmplitude * 2) + 1)) - DitherAmplitude;
                surface[x, y] = new Rgba32(
                    (byte)(background.R + offset),
                    (byte)(background.G + offset),
                    (byte)(background.B + offset),
                    background.A);
            }
        }

        using var stream = new MemoryStream();
        PngCodec.Save(surface, stream, PngColorType.Rgba);
        return stream.ToArray();
    }

    /// <summary>
    ///     Builds a well-formed PNG whose content block carries a distinct value in every channel
    ///     of every pixel, on a solid background.
    /// </summary>
    /// <remarks>
    ///     <b>The fixture that makes "the trimmed region carries the source's pixels" a
    ///     comparison rather than a claim.</b> A solid content block would survive an off-by-one
    ///     origin and an encoder that discarded alpha; a block whose every pixel is a distinct
    ///     function of its absolute coordinate survives neither. The background is fully
    ///     transparent, and every content pixel's alpha is far above it, so every pixel of the
    ///     block is content under any tolerance and the expected region is exactly the block.
    /// </remarks>
    /// <param name="width">The width of the image, in pixels.</param>
    /// <param name="height">The height of the image, in pixels.</param>
    /// <param name="background">The color every pixel outside the content block carries.</param>
    /// <param name="content">The rectangle the content block occupies.</param>
    /// <returns>The encoded PNG file.</returns>
    internal static byte[] DistinguishableContentOnBackgroundPng(
        int width,
        int height,
        Rgba32 background,
        Rectangle content)
    {
        using var surface = new Surface(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inside = x >= content.X && x < content.X + content.Width
                    && y >= content.Y && y < content.Y + content.Height;

                // The distinguishable value is a function of the ABSOLUTE coordinate, so a
                // region taken out of this image can be compared against the source it came
                // from without the comparison having to know where the block was placed.
                surface[x, y] = inside
                    ? new Rgba32(
                        (byte)((x * 7) + 1),
                        (byte)((y * 11) + 3),
                        (byte)((x * y) + 5),
                        (byte)(255 - ((x + y) % 64)))
                    : background;
            }
        }

        using var stream = new MemoryStream();
        PngCodec.Save(surface, stream, PngColorType.Rgba);
        return stream.ToArray();
    }

    /// <summary>
    ///     Reads a PNG file's pixels back for comparison.
    /// </summary>
    /// <remarks>
    ///     Offered here so a test asserting that a region's pixels survived does not have to name
    ///     the decoding library itself; the fixture builder is the one place in the test project
    ///     that knows how an image is encoded, so it is the right place to know how one is read.
    ///     <para>
    ///     <b>The returned buffer is the caller's to dispose.</b> The pixel buffer type is
    ///     disposable, and the production code is documented as honoring that contract so the
    ///     family stays correct when a future release backs the buffer with a pooled array. Test
    ///     code that ignored the contract would teach the opposite pattern, so every caller here
    ///     takes the result under a <c>using</c>.
    ///     </para>
    /// </remarks>
    /// <param name="data">The encoded PNG file.</param>
    /// <returns>The decoded pixel buffer, which the caller disposes.</returns>
    internal static Surface Decode(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        return PngCodec.Load(stream);
    }

    /// <summary>
    ///     Builds a pixel buffer whose every pixel is a distinct function of its coordinate.
    /// </summary>
    /// <param name="width">The width of the surface, in pixels.</param>
    /// <param name="height">The height of the surface, in pixels.</param>
    /// <returns>The populated surface.</returns>
    private static Surface BuildDistinguishableSurface(int width, int height)
    {
        var surface = new Surface(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                surface[x, y] = new Rgba32(
                    (byte)((x * 7) + 1),
                    (byte)((y * 11) + 3),
                    (byte)((x * y) + 5),
                    (byte)(255 - ((x + y) % 64)));
            }
        }

        return surface;
    }

    /// <summary>
    ///     Builds a pixel buffer carrying a solid block of content on a solid background.
    /// </summary>
    /// <remarks>
    ///     Shared by the PNG and JPEG content fixtures so the two differ only in their encoder,
    ///     which is what makes a scenario comparing them a scenario about compression rather than
    ///     about two differently-drawn pictures.
    /// </remarks>
    /// <param name="width">The width of the surface, in pixels.</param>
    /// <param name="height">The height of the surface, in pixels.</param>
    /// <param name="background">The color every pixel outside the content block carries.</param>
    /// <param name="content">The rectangle the content block occupies.</param>
    /// <param name="contentColor">The color every pixel inside the content block carries.</param>
    /// <returns>The populated surface.</returns>
    private static Surface BuildContentSurface(
        int width,
        int height,
        Rgba32 background,
        Rectangle content,
        Rgba32 contentColor)
    {
        var surface = new Surface(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inside = x >= content.X && x < content.X + content.Width
                    && y >= content.Y && y < content.Y + content.Height;
                surface[x, y] = inside ? contentColor : background;
            }
        }

        return surface;
    }

    /// <summary>
    ///     Moves one channel a fixed count from the background toward the content.
    /// </summary>
    /// <remarks>
    ///     The direction follows the content so the ring always lies between the two, as a real
    ///     anti-aliased edge does; when the two channels are equal the ring is too, which
    ///     correctly makes that channel carry no edge at all.
    /// </remarks>
    /// <param name="background">The background's value for this channel.</param>
    /// <param name="content">The content's value for this channel.</param>
    /// <returns>The ring's value for this channel.</returns>
    private static byte Nudge(byte background, byte content)
    {
        if (content > background)
        {
            return (byte)(background + AntiAliasBlend);
        }

        return content < background ? (byte)(background - AntiAliasBlend) : background;
    }

    /// <summary>
    ///     Builds a PNG header chunk declaring the supplied dimensions, color type and interlace
    ///     method.
    /// </summary>
    /// <remarks>
    ///     The payload layout is fixed by the format: width, height, bit depth, color type,
    ///     compression method, filter method, interlace method. Eight bits per sample is used
    ///     throughout, because no fixture here needs a sub-byte or wide sample to make its point.
    /// </remarks>
    /// <param name="width">The width to declare, in pixels.</param>
    /// <param name="height">The height to declare, in pixels.</param>
    /// <param name="colorType">The color type to declare.</param>
    /// <param name="interlace">The interlace method to declare; zero is none, one is Adam7.</param>
    /// <returns>The complete header chunk, carrying its checksum.</returns>
    private static byte[] HeaderChunk(int width, int height, byte colorType, byte interlace)
    {
        var payload = new byte[13];
        WriteBigEndian(payload, 0, width);
        WriteBigEndian(payload, 4, height);
        payload[8] = 8;
        payload[9] = colorType;
        payload[10] = 0;
        payload[11] = 0;
        payload[12] = interlace;

        return Chunk("IHDR", payload);
    }

    /// <summary>
    ///     Frames a payload as a PNG chunk: a length, a type, the payload and a checksum.
    /// </summary>
    /// <remarks>
    ///     The checksum covers the type and the payload but not the length, which is the format's
    ///     own rule; getting it wrong would make every hand-built fixture read as corrupt.
    /// </remarks>
    /// <param name="type">The four-character chunk type.</param>
    /// <param name="payload">The chunk's payload.</param>
    /// <returns>The framed chunk.</returns>
    private static byte[] Chunk(string type, byte[] payload)
    {
        var chunk = new byte[12 + payload.Length];
        WriteBigEndian(chunk, 0, payload.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        payload.CopyTo(chunk, 8);
        WriteBigEndian(chunk, 8 + payload.Length, unchecked((int)Crc32(chunk, 4, 4 + payload.Length)));
        return chunk;
    }

    /// <summary>
    ///     Wraps raw bytes in the compressed-stream framing a PNG's pixel data is carried in.
    /// </summary>
    /// <remarks>
    ///     A bare compressed stream is refused: the framing is a two-byte header declaring the
    ///     method and window size, then the compressed bytes, then a big-endian Adler-32 checksum
    ///     of the <em>uncompressed</em> input.
    /// </remarks>
    /// <param name="raw">The uncompressed scanline bytes.</param>
    /// <returns>The framed, compressed stream.</returns>
    private static byte[] ZlibCompress(byte[] raw)
    {
        using var output = new MemoryStream();

        // Deflate compression, 32 KiB window, no preset dictionary, fastest compression.
        output.WriteByte(0x78);
        output.WriteByte(0x01);

        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        var adler = Adler32(raw);
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)adler);

        return output.ToArray();
    }

    /// <summary>
    ///     Computes the running checksum a compressed pixel stream is terminated with.
    /// </summary>
    /// <param name="data">The uncompressed bytes to checksum.</param>
    /// <returns>The checksum, with its high half first.</returns>
    private static uint Adler32(byte[] data)
    {
        // 65,521 is the largest prime below 65,536, which is the modulus the format specifies.
        const uint modulus = 65521;

        uint low = 1;
        uint high = 0;
        foreach (var value in data)
        {
            low = (low + value) % modulus;
            high = (high + low) % modulus;
        }

        return (high << 16) | low;
    }

    /// <summary>
    ///     Builds the lookup table the chunk checksum is computed from.
    /// </summary>
    /// <returns>The 256-entry table.</returns>
    private static uint[] BuildCrc32Table()
    {
        // The reversed representation of the standard polynomial the format specifies.
        const uint polynomial = 0xEDB88320;

        var table = new uint[256];
        for (var index = 0u; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? polynomial ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }

    /// <summary>
    ///     Computes the checksum a PNG chunk carries over its type and payload.
    /// </summary>
    /// <param name="data">The buffer holding the bytes to checksum.</param>
    /// <param name="start">The index of the first byte to include.</param>
    /// <param name="length">The number of bytes to include.</param>
    /// <returns>The checksum.</returns>
    private static uint Crc32(byte[] data, int start, int length)
    {
        var crc = 0xFFFFFFFFu;
        for (var index = start; index < start + length; index++)
        {
            crc = Crc32Table[(crc ^ data[index]) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>
    ///     Writes a 32-bit value into a buffer with its most significant byte first.
    /// </summary>
    /// <param name="buffer">The buffer to write into.</param>
    /// <param name="offset">The index of the first byte to write.</param>
    /// <param name="value">The value to write.</param>
    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    /// <summary>
    ///     Joins the parts of a file into one buffer.
    /// </summary>
    /// <param name="parts">The parts, in order.</param>
    /// <returns>The joined buffer.</returns>
    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    /// <summary>
    ///     The rectangle a content block occupies within a fixture.
    /// </summary>
    /// <remarks>
    ///     Carried as one value rather than four loose integers so that a scenario stating where
    ///     it put the content and a scenario asserting where the content was found cannot
    ///     disagree about which number is which, and so no builder takes four same-typed
    ///     parameters a caller could transpose.
    /// </remarks>
    /// <param name="X">The block's left edge, in pixels from the image's left edge.</param>
    /// <param name="Y">The block's top edge, in pixels from the image's top edge.</param>
    /// <param name="Width">The block's width, in pixels.</param>
    /// <param name="Height">The block's height, in pixels.</param>
    internal sealed record Rectangle(int X, int Y, int Width, int Height);
}
