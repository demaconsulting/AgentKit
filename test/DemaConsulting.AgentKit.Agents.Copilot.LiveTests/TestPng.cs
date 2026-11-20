using System.IO.Compression;
using System.Text;

namespace DemaConsulting.AgentKit.Agents.Copilot.LiveTests;

/// <summary>
///     Encodes the one test image the live suite needs: a PNG whose left half is blue and whose
///     right half is yellow.
/// </summary>
/// <remarks>
///     Generated rather than checked in, so the suite carries no binary asset and the image's
///     content is stated in code: a model that has really seen it can name both colors and where
///     each is, and one that has not cannot guess both. Uncompressed RGB rows, deflated with zlib,
///     in the three chunks a valid PNG needs.
/// </remarks>
internal static class TestPng
{
    /// <summary>
    ///     Encodes a PNG with a blue left half and a yellow right half.
    /// </summary>
    /// <param name="width">The image width in pixels. Must be positive.</param>
    /// <param name="height">The image height in pixels. Must be positive.</param>
    /// <returns>The PNG file's bytes.</returns>
    internal static byte[] SplitBlueYellow(int width, int height)
    {
        // Raw scanlines: a filter byte of zero, then one RGB triple per pixel
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < width; x++)
            {
                raw.Write(x < width / 2 ? [0, 0, 255] : [255, 255, 0]);
            }
        }

        // The image data is the zlib-deflated scanlines
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        // Signature, header (8-bit truecolor), data, end
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>
    ///     Writes one PNG chunk: length, type, data and the CRC of type and data.
    /// </summary>
    /// <param name="output">The stream to write to.</param>
    /// <param name="type">The four-letter chunk type.</param>
    /// <param name="data">The chunk data.</param>
    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        output.Write(length);

        var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        output.Write(body);

        var crc = new byte[4];
        WriteBigEndian(crc, 0, unchecked((int)Crc32(body)));
        output.Write(crc);
    }

    /// <summary>
    ///     Writes a 32-bit value in network byte order.
    /// </summary>
    /// <param name="buffer">The buffer to write into.</param>
    /// <param name="offset">The offset of the first byte.</param>
    /// <param name="value">The value.</param>
    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    /// <summary>
    ///     Computes the CRC-32 PNG chunks carry (the reflected 0xEDB88320 polynomial).
    /// </summary>
    /// <param name="data">The bytes to checksum.</param>
    /// <returns>The checksum.</returns>
    private static uint Crc32(byte[] data)
    {
        const uint polynomial = 0xEDB88320u;
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? polynomial ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
