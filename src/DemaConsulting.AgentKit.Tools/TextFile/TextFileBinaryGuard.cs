using System.Text;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The shared text-versus-binary decision the text file family makes from a file's leading
///     bytes, so a read never decodes binary content into garbled text and a search never surfaces
///     it.
/// </summary>
/// <remarks>
///     <para>
///     <b>The signal is in the bytes, not the name.</b> A recognized byte-order mark means text; a
///     mark-less window carrying a NUL byte means binary; otherwise the window must be valid UTF-8.
///     The mark check comes first so a legitimately encoded UTF-16 or UTF-32 file, which carries
///     NUL bytes yet decodes correctly, is not misclassified.
///     </para>
///     <para>
///     <b>Only a leading window is read.</b> The decision is taken from at most
///     <see cref="SniffByteCount"/> leading bytes, so it never requires materializing a whole file —
///     the property that lets the read and search tools page and stream files larger than the read
///     ceiling. A NUL byte beyond the sniff window is therefore not detected, which is a deliberate,
///     documented bound rather than a whole-file scan.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
internal static class TextFileBinaryGuard
{
    /// <summary>
    ///     The number of leading bytes sniffed to decide whether a file is text or binary.
    /// </summary>
    internal const int SniffByteCount = 4096;

    /// <summary>
    ///     Determines whether a permitted file's content is binary rather than text, by sniffing a
    ///     leading window.
    /// </summary>
    /// <param name="realPath">The real location of the permitted file.</param>
    /// <param name="length">The file's already-known length.</param>
    /// <param name="cancellationToken">A token that cancels the sniff read.</param>
    /// <returns>
    ///     <see langword="true"/> when the leading window indicates binary content; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    internal static async Task<bool> IsBinaryAsync(
        string realPath,
        long length,
        CancellationToken cancellationToken)
    {
        var window = new byte[SniffByteCount];
        int count;

        await using (var stream = new FileStream(
            realPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: SniffByteCount,
            useAsync: true))
        {
            count = await stream
                .ReadAtLeastAsync(window, window.Length, throwOnEndOfStream: false, cancellationToken)
                .ConfigureAwait(false);
        }

        // A recognized byte-order mark is text; this must precede the NUL rule, which UTF-16 and
        // UTF-32 text would otherwise trip.
        if (HasRecognizedBom(window, count))
        {
            return false;
        }

        // A NUL byte in a mark-less window is the classic binary signal.
        if (Array.IndexOf<byte>(window, 0, 0, count) >= 0)
        {
            return true;
        }

        // Otherwise the window must be valid UTF-8 to be treated as text.
        var reachedEof = length <= SniffByteCount;
        return !IsValidUtf8(window, count, reachedEof);
    }

    /// <summary>
    ///     Determines whether the leading bytes of a window begin with a byte-order mark the decode
    ///     path recognizes.
    /// </summary>
    /// <param name="window">The sniffed leading bytes.</param>
    /// <param name="count">The number of valid bytes in <paramref name="window"/>.</param>
    /// <returns>
    ///     <see langword="true"/> when a recognized mark is present; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static bool HasRecognizedBom(byte[] window, int count)
    {
        // UTF-32 LE (FF FE 00 00) and UTF-32 BE (00 00 FE FF) — tested before the two-byte marks.
        if (count >= 4 && window[0] == 0xFF && window[1] == 0xFE && window[2] == 0x00 && window[3] == 0x00)
        {
            return true;
        }

        if (count >= 4 && window[0] == 0x00 && window[1] == 0x00 && window[2] == 0xFE && window[3] == 0xFF)
        {
            return true;
        }

        // UTF-16 LE (FF FE) and UTF-16 BE (FE FF).
        if (count >= 2 && window[0] == 0xFF && window[1] == 0xFE)
        {
            return true;
        }

        if (count >= 2 && window[0] == 0xFE && window[1] == 0xFF)
        {
            return true;
        }

        // UTF-8 (EF BB BF).
        return count >= 3 && window[0] == 0xEF && window[1] == 0xBB && window[2] == 0xBF;
    }

    /// <summary>
    ///     Determines whether a window of bytes is valid UTF-8, tolerating a multi-byte sequence
    ///     split by the window boundary when the window has not reached end of file.
    /// </summary>
    /// <param name="window">The sniffed leading bytes.</param>
    /// <param name="count">The number of valid bytes in <paramref name="window"/>.</param>
    /// <param name="reachedEof">
    ///     <see langword="true"/> when the window spans the whole file, so the decoder is flushed.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when the window is valid UTF-8; otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsValidUtf8(byte[] window, int count, bool reachedEof)
    {
        var decoder = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetDecoder();

        try
        {
            decoder.GetCharCount(window, 0, count, flush: reachedEof);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
