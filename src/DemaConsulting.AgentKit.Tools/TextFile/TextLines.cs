using System.Globalization;
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
///     <b>Every line model question an editing tool asks is answered here, including where a
///     change landed.</b> An edit shifts the numbering of every line below it, so the tools that
///     mutate a file report the line span they affected and the file's new total using
///     <see cref="LineOfOffset"/>, <see cref="LastLineOfInsertedText"/> and
///     <see cref="DescribeSpan"/>. Evaluation found that without that span a model re-read the
///     whole file after every edit purely to re-derive the numbering.
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
    ///     Returns the 1-based number of the line a character offset falls on.
    /// </summary>
    /// <remarks>
    ///     This is the inverse of <see cref="OffsetOfLine"/>, and it exists so that an editing tool
    ///     can say where its change landed in the numbering <see cref="TextFileReadTool"/> prints.
    ///     Reporting the affected line span is what lets a model issue its next line-addressed
    ///     request without first re-reading the file to re-derive the numbering an edit shifted.
    ///     The offset is clamped to the text, so an offset at or past the end names the last line
    ///     a character could occupy rather than throwing.
    /// </remarks>
    /// <param name="text">The text the offset indexes into.</param>
    /// <param name="offset">The character offset whose line is wanted.</param>
    /// <returns>The 1-based line number, never less than one.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public static int LineOfOffset(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Counting the line feeds before the offset matches Split exactly, so the number reported
        // is the number the read, cut and paste tools address.
        var limit = Math.Clamp(offset, 0, text.Length);
        return text.AsSpan(0, limit).Count('\n') + 1;
    }

    /// <summary>
    ///     Returns the 1-based number of the last line a run of inserted text occupies.
    /// </summary>
    /// <remarks>
    ///     Whether the inserted text ends with a terminator decides the answer: text that ends with
    ///     a newline finishes the line that newline terminates, while text that does not runs on
    ///     into the line that previously began at the insertion point, so that line is the last one
    ///     the insertion occupies. Empty inserted text occupies no line of its own, so the first
    ///     line is returned unchanged and the caller reports a single position.
    /// </remarks>
    /// <param name="firstLine">The 1-based line the insertion begins on.</param>
    /// <param name="inserted">The inserted text, as written into the file.</param>
    /// <returns>The 1-based last line the insertion occupies, never less than
    ///     <paramref name="firstLine"/>.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="inserted"/> is <see langword="null"/>.
    /// </exception>
    public static int LastLineOfInsertedText(int firstLine, string inserted)
    {
        ArgumentNullException.ThrowIfNull(inserted);

        var breaks = inserted.AsSpan().Count('\n');

        // A trailing terminator closes the final inserted line rather than starting another.
        if (inserted.EndsWith('\n'))
        {
            breaks--;
        }

        return firstLine + Math.Max(breaks, 0);
    }

    /// <summary>
    ///     Renders a 1-based inclusive line span as the phrase a confirmation names it by.
    /// </summary>
    /// <remarks>
    ///     A one-line span reads as "line 12" rather than "lines 12-12", because a model reading a
    ///     degenerate range has to work out that it is degenerate. The phrase omits the word "the"
    ///     so a caller can place it in whatever sentence it is composing.
    /// </remarks>
    /// <param name="firstLine">The 1-based first line of the span.</param>
    /// <param name="lastLine">The 1-based last line of the span, inclusive.</param>
    /// <returns>The phrase naming the span, for example <c>lines 40-44</c> or <c>line 12</c>.</returns>
    public static string DescribeSpan(int firstLine, int lastLine)
    {
        return firstLine == lastLine
            ? "line " + firstLine.ToString(CultureInfo.InvariantCulture)
            : "lines " + firstLine.ToString(CultureInfo.InvariantCulture) + "-"
              + lastLine.ToString(CultureInfo.InvariantCulture);
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
