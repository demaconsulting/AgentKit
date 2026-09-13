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
