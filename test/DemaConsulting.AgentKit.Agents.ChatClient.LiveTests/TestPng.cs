using System.IO.Compression;
using System.Text;

namespace DemaConsulting.AgentKit.Agents.ChatClient.LiveTests;

/// <summary>
///     Encodes the test image the live suite needs: a PNG of a number in a 5-by-7 block font on a
///     speckled background.
/// </summary>
/// <remarks>
///     Generated rather than checked in, so the suite carries no binary asset and the image's content
///     is stated in code: a model that has really seen it can read the number, and its file name does
///     not reveal it. The light speckle keeps the digits legible while defeating compression, so the
///     PNG is large — about 316 KB at the scale the suite uses — which is what makes a tool message
///     carrying it as base64 text visible on the wire. The speckle is seeded, so every run encodes the
///     same bytes.
/// </remarks>
internal static class TestPng
{
    /// <summary>
    ///     The 5-by-7 glyphs of the ten digits, one string per row, <c>1</c> for ink.
    /// </summary>
    private static readonly Dictionary<char, string[]> Font = new()
    {
        ['0'] = ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
        ['3'] = ["11111", "00010", "00100", "00010", "00001", "10001", "01110"],
        ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ['5'] = ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
        ['6'] = ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
        ['9'] = ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
    };

    /// <summary>
    ///     Encodes a PNG showing a number, black on a light speckled background.
    /// </summary>
    /// <param name="digits">The digits to draw.</param>
    /// <param name="scale">The pixels per font cell.</param>
    /// <returns>The PNG file's bytes.</returns>
    internal static byte[] Number(string digits, int scale)
    {
        const int margin = 2;
        var width = (((digits.Length * 6) - 1) + (2 * margin)) * scale;
        var height = (7 + (2 * margin)) * scale;
        var speckle = new Random(7319);

        // Raw scanlines: a filter byte of zero, then one RGB triple per pixel
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < width; x++)
            {
                if (IsInk(digits, scale, margin, x, y))
                {
                    raw.Write([0, 0, 0]);
                }
                else
                {
                    raw.Write([(byte)speckle.Next(200, 256), (byte)speckle.Next(200, 256), (byte)speckle.Next(200, 256)]);
                }
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
    ///     Determines whether a pixel lies on a digit's ink.
    /// </summary>
    /// <param name="digits">The digits drawn.</param>
    /// <param name="scale">The pixels per font cell.</param>
    /// <param name="margin">The cells of margin around the digits.</param>
    /// <param name="x">The pixel column.</param>
    /// <param name="y">The pixel row.</param>
    /// <returns><see langword="true"/> for ink.</returns>
    private static bool IsInk(string digits, int scale, int margin, int x, int y)
    {
        var column = (x / scale) - margin;
        var row = (y / scale) - margin;
        if (column < 0 || row < 0 || row >= 7)
        {
            return false;
        }

        var digit = column / 6;
        var cell = column % 6;
        return digit < digits.Length && cell < 5 && Font[digits[digit]][row][cell] == '1';
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
