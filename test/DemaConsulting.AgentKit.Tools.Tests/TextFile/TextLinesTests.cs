using DemaConsulting.AgentKit.Tools.TextFile;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Unit tests for the <see cref="TextLines"/> class.
/// </summary>
/// <remarks>
///     The streaming reader must number lines exactly as <see cref="TextLines.Split"/> does, or a
///     line the read, search and outline tools stream would drift from the line the cut and paste
///     tools address. These scenarios lock that equivalence across line-feed, carriage-return
///     line-feed, and lone-carriage-return endings.
/// </remarks>
public class TextLinesTests
{
    /// <summary>
    ///     Proves the streaming reader reproduces the split reader's lines for line-feed endings.
    /// </summary>
    [Fact]
    public void TextLines_EnumerateLines_LineFeedEndings_ReproducesSplit()
    {
        const string text = "one\ntwo\nthree\n";

        AssertEnumerateMatchesSplit(text);
    }

    /// <summary>
    ///     Proves the streaming reader reproduces the split reader's lines for carriage-return
    ///     line-feed endings.
    /// </summary>
    [Fact]
    public void TextLines_EnumerateLines_CarriageReturnLineFeedEndings_ReproducesSplit()
    {
        const string text = "one\r\ntwo\r\nthree";

        AssertEnumerateMatchesSplit(text);
    }

    /// <summary>
    ///     Proves a bare carriage return is not a line break, so the streaming reader keeps it in the
    ///     line exactly as the split reader does and the line numbering never drifts.
    /// </summary>
    [Fact]
    public void TextLines_EnumerateLines_LoneCarriageReturn_IsNotALineBreak()
    {
        const string text = "one\rtwo\nthree";

        AssertEnumerateMatchesSplit(text);

        // A lone carriage return keeps the two segments on one line, so the file is two lines.
        using var reader = new StringReader(text);
        Assert.Equal(2, TextLines.EnumerateLines(reader).Count());
    }

    /// <summary>
    ///     Proves empty text yields no lines, so an empty file is zero lines rather than one empty
    ///     line.
    /// </summary>
    [Fact]
    public void TextLines_EnumerateLines_EmptyText_YieldsNoLines()
    {
        using var reader = new StringReader(string.Empty);

        Assert.Empty(TextLines.EnumerateLines(reader));
    }

    /// <summary>
    ///     Proves the offset-to-line mapping agrees with <see cref="TextLines.OffsetOfLine"/>, so a
    ///     span an editing tool reports addresses the same line the cut and paste tools do.
    /// </summary>
    /// <param name="offset">The character offset to map.</param>
    /// <param name="expected">The 1-based line the offset falls on.</param>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(7, 2)]
    [InlineData(8, 3)]
    [InlineData(11, 3)]
    public void TextLines_LineOfOffset_WithinText_NamesTheLineTheOffsetFallsOn(int offset, int expected)
    {
        // Arrange: three lines of four characters each, terminators included
        const string text = "one\ntwo\nsix\n";

        // Act / Assert: the offset maps to the line the split model numbers it as
        Assert.Equal(expected, TextLines.LineOfOffset(text, offset));
    }

    /// <summary>
    ///     Proves an offset outside the text is clamped rather than throwing, since a tool reporting
    ///     an append position legitimately passes the text's own length.
    /// </summary>
    [Fact]
    public void TextLines_LineOfOffset_OffsetOutsideTheText_IsClamped()
    {
        const string text = "one\ntwo";

        Assert.Equal(1, TextLines.LineOfOffset(text, -5));
        Assert.Equal(2, TextLines.LineOfOffset(text, text.Length));
        Assert.Equal(2, TextLines.LineOfOffset(text, 500));
    }

    /// <summary>
    ///     Proves the last line of an insertion depends on whether the inserted text ends with a
    ///     terminator, because unterminated text runs on into the line that followed it.
    /// </summary>
    /// <param name="inserted">The inserted text.</param>
    /// <param name="expected">The 1-based last line the insertion occupies, starting at line 10.</param>
    [Theory]
    [InlineData("", 10)]
    [InlineData("alpha", 10)]
    [InlineData("alpha\n", 10)]
    [InlineData("alpha\nbeta", 11)]
    [InlineData("alpha\nbeta\n", 11)]
    [InlineData("alpha\nbeta\ngamma\n", 12)]
    public void TextLines_LastLineOfInsertedText_Terminator_DecidesTheLastLine(string inserted, int expected)
    {
        Assert.Equal(expected, TextLines.LastLineOfInsertedText(10, inserted));
    }

    /// <summary>
    ///     Proves a one-line span reads as a single line rather than a degenerate range, so a model
    ///     is not left to work out that a range of one is a single line.
    /// </summary>
    [Fact]
    public void TextLines_DescribeSpan_SingleLine_ReadsAsOneLine()
    {
        Assert.Equal("line 40", TextLines.DescribeSpan(40, 40));
        Assert.Equal("lines 40-44", TextLines.DescribeSpan(40, 44));
    }

    /// <summary>
    ///     Asserts the streaming reader yields exactly the same lines, including terminators, as the
    ///     split reader.
    /// </summary>
    /// <param name="text">The text to compare.</param>
    private static void AssertEnumerateMatchesSplit(string text)
    {
        using var reader = new StringReader(text);
        var streamed = TextLines.EnumerateLines(reader).ToList();
        var split = TextLines.Split(text);

        Assert.Equal(split, streamed);
    }
}
