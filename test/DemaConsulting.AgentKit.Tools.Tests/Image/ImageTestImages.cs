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
        var surface = BuildDistinguishableSurface(width, height);

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
        var surface = BuildDistinguishableSurface(width, height);

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
    ///     path under test reaches: a reader refuses on the header's declaration long before the
    ///     layout would matter.
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
    ///     Reads a PNG file's pixels back for comparison.
    /// </summary>
    /// <remarks>
    ///     Offered here so a test asserting that a region's pixels survived does not have to name
    ///     the decoding library itself; the fixture builder is the one place in the test project
    ///     that knows how an image is encoded, so it is the right place to know how one is read.
    /// </remarks>
    /// <param name="data">The encoded PNG file.</param>
    /// <returns>The decoded pixel buffer.</returns>
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
}
