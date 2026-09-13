using System.Text;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The shared line model the text file family reads, searches and edits through, so that a
///     line the read tool numbers is the same line the cut tool removes and the paste tool restores.
/// </summary>
/// <remarks>
///     <para>
///     <b>This is one line model, used by every text tool, on purpose.</b> Reading a window, searching
///     for a pattern, cutting a range and pasting it back must all agree on what a line is and where
///     it begins, or a round-trip would not reproduce the original file. Centralizing the model here
///     — rather than letting each tool split lines its own way — is what makes the cut/paste
///     round-trip exact and the line numbers consistent between tools.
///     </para>
///     <para>
///     <b>A line keeps its own terminator.</b> <see cref="Split"/> returns each line together with
///     the newline that ends it (<c>\n</c> or <c>\r\n</c>); the final line keeps whatever terminator
///     it had, which may be none. Concatenating the returned lines reproduces the original text
///     byte for byte, which is precisely what lets the cut tool capture a raw slice and the paste
///     tool restore it without altering a single character. <see cref="Content"/> is the companion
///     that strips the terminator for display, because a numbered read line and a search hit show the
///     content without the newline that ends it.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
internal static class TextLines
{
    /// <summary>
    ///     The separator reported paths are normalized to on every platform.
    /// </summary>
    private const char ReportedSeparator = '/';

    /// <summary>
    ///     The number of characters <see cref="EnumerateLines"/> reads per block while streaming.
    /// </summary>
    private const int ReadBlockSize = 8192;

    /// <summary>
    ///     Splits text into lines, each keeping the terminator that ends it.
    /// </summary>
    /// <remarks>
    ///     Each returned line ends with <c>\n</c> or <c>\r\n</c>, except the final line when the text
    ///     did not end with a newline. Concatenating the result reproduces <paramref name="text"/>
    ///     exactly. Empty text yields no lines, so an empty file is zero lines rather than one empty
    ///     line.
    /// </remarks>
    /// <param name="text">The text to split.</param>
    /// <returns>The lines, in order, each including its terminator.</returns>
    public static IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = new List<string>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            // Include the newline in the line so concatenation reproduces the original exactly.
            lines.Add(text[start..(index + 1)]);
            start = index + 1;
        }

        // A trailing segment with no newline is the final line; empty text contributes nothing.
        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }

    /// <summary>
    ///     Streams text into lines, each keeping the terminator that ends it, without holding the
    ///     whole source in memory.
    /// </summary>
    /// <remarks>
    ///     Splits on <c>\n</c> exactly as <see cref="Split"/> does, so a line this reader numbers is
    ///     the same line <see cref="Split"/> would number and the cut and paste tools address. Each
    ///     yielded line ends with <c>\n</c> or <c>\r\n</c>, except the final line when the text did
    ///     not end with a newline; empty input yields no lines. A bare <c>\r</c> is <em>not</em> a
    ///     terminator, so this must be used in place of <see cref="TextReader.ReadLine"/>, which also
    ///     breaks on <c>\r</c> and would drift the line numbering on files with lone carriage
    ///     returns. Reading a window by streaming through this method holds only the current line and
    ///     the caller's window, never the entire file.
    /// </remarks>
    /// <param name="reader">The reader whose characters are split into lines.</param>
    /// <returns>The lines, in order, each including its terminator.</returns>
    public static IEnumerable<string> EnumerateLines(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return EnumerateLinesIterator(reader);
    }

    /// <summary>
    ///     Streams the lines of a reader, split on <c>\n</c>, after the argument has been validated.
    /// </summary>
    /// <param name="reader">The reader whose characters are split into lines.</param>
    /// <returns>The lines, in order, each including its terminator.</returns>
    private static IEnumerable<string> EnumerateLinesIterator(TextReader reader)
    {
        var buffer = new char[ReadBlockSize];
        var line = new StringBuilder();
        int read;

        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                line.Append(character);

                // Emit only on a line feed, matching Split exactly; a bare carriage return stays in
                // the line so line numbers never drift on lone-carriage-return files.
                if (character != '\n')
                {
                    continue;
                }

                yield return line.ToString();
                line.Clear();
            }
        }

        // A trailing segment with no line feed is the final line; empty input yields nothing.
        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    /// <summary>
    ///     Returns a line's content without the newline that terminates it.
    /// </summary>
    /// <remarks>
    ///     Strips a trailing <c>\r\n</c> or <c>\n</c> so a numbered read line or a search hit shows
    ///     the content a person would see, without the terminator that is part of the raw line.
    /// </remarks>
    /// <param name="line">A line as returned by <see cref="Split"/>.</param>
    /// <returns>The line's content, without its terminator.</returns>
    public static string Content(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return line[..^2];
        }

        if (line.EndsWith('\n'))
        {
            return line[..^1];
        }

        return line;
    }

    /// <summary>
    ///     Returns the character offset at which a 1-based line begins.
    /// </summary>
    /// <remarks>
    ///     A <paramref name="lineNumber"/> equal to one past the last line returns the length of the
    ///     text, which is the append position the paste tool uses. This is the offset arithmetic the
    ///     cut and paste tools share so a captured slice is removed and restored at exactly the same
    ///     character boundary.
    /// </remarks>
    /// <param name="lines">The lines as returned by <see cref="Split"/>.</param>
    /// <param name="lineNumber">The 1-based line whose start offset is wanted.</param>
    /// <returns>The character offset of the start of the line.</returns>
    public static int OffsetOfLine(IReadOnlyList<string> lines, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var offset = 0;
        for (var index = 0; index < lineNumber - 1 && index < lines.Count; index++)
        {
            offset += lines[index].Length;
        }

        return offset;
    }

    /// <summary>
    ///     Normalizes a path's separators to a forward slash on every platform.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The path with every separator rendered as a forward slash.</returns>
    public static string ToForwardSlash(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return path
            .Replace(Path.DirectorySeparatorChar, ReportedSeparator)
            .Replace(Path.AltDirectorySeparatorChar, ReportedSeparator);
    }
}
