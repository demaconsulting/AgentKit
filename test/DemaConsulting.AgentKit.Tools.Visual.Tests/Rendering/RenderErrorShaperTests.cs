using System.Text.RegularExpressions;
using System.Xml;
using DemaConsulting.AgentKit.Tools.Visual.Rendering;
using DemaConsulting.CanvasNet.Codecs;
using DemaConsulting.CanvasNet.Files;
using DemaConsulting.CanvasNet.ImageSources;
using DemaConsulting.CanvasNet.VegaLite;
using DemaConsulting.CanvasNet.Viewer;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Rendering;

/// <summary>
///     Unit tests for the <see cref="RenderErrorShaper"/> class.
/// </summary>
/// <remarks>
///     <para>
///     The format scenarios draw the real broken sources the rendering spike recorded, through the
///     real library, so they verify the shaper against the exceptions and messages the library
///     actually produces rather than against a hand-written imitation of them.
///     </para>
///     <para>
///     The classification scenarios construct exception chains directly, because each must show
///     that the type — and not the message text — decided the code, and a real render cannot
///     produce a misleading message on demand.
///     </para>
/// </remarks>
public class RenderErrorShaperTests
{
    /// <summary>
    ///     Proves a syntax error in each format is shaped with its code, line, column and a caret
    ///     under the offending column.
    /// </summary>
    /// <param name="source">The broken source.</param>
    /// <param name="fileName">The file name it is drawn as.</param>
    /// <param name="code">The expected failure code.</param>
    /// <param name="line">The expected line.</param>
    /// <param name="column">The expected column.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData(RenderSamples.MermaidBadArrow, "diagram.mmd", ErrorCodes.MermaidError, 2, 5)]
    [InlineData(RenderSamples.PlantUmlBadStatement, "diagram.puml", ErrorCodes.PlantUmlError, 2, 1)]
    [InlineData(RenderSamples.VegaLiteUnknownMark, "chart.vl.json", ErrorCodes.VegaLiteError, 1, 41)]
    [InlineData(RenderSamples.ScadMissingParenthesis, "model.scad", ErrorCodes.ScadError, 2, 1)]
    public async Task RenderErrorShaper_Shape_SyntaxError_StatesCodeLineColumnAndCaret(
        string source,
        string fileName,
        string code,
        int line,
        int column)
    {
        // Arrange: the failure a real render of the broken source produces
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        var outcome = await new VisualRenderer(VisualOptions.Default)
            .RenderAsync(RenderSamples.Request(source, fileName, policy), TestContext.Current.CancellationToken);

        // Act: shape it with the source in hand
        var text = RenderErrorShaper.Shape(outcome.Failure!, new ShapeContext(policy, null, source, fileName, null));

        // Assert: code, location, the offending line and a caret under its column
        var lines = text.Split('\n');
        Assert.StartsWith("Render failed (" + code, lines[0], StringComparison.Ordinal);
        Assert.Contains($"at line {line}, column {column}", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("could not be drawn", text, StringComparison.Ordinal);
        Assert.StartsWith($"  {line} | ", lines[1], StringComparison.Ordinal);
        Assert.Equal(lines[1].IndexOf(" | ", StringComparison.Ordinal) + 3 + column - 1, lines[2].IndexOf('^', StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves a malformed SVG is shaped as corrupt input located by the XML reader's position.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task RenderErrorShaper_Shape_MalformedSvg_IsCorruptInputWithItsPosition()
    {
        // Arrange: the failure a real render of a malformed SVG produces
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        var outcome = await new VisualRenderer(VisualOptions.Default)
            .RenderAsync(RenderSamples.Request(RenderSamples.SvgMalformed, "figure.svg", policy), TestContext.Current.CancellationToken);

        // Act: shape it
        var text = RenderErrorShaper.Shape(outcome.Failure!, new ShapeContext(policy, null, RenderSamples.SvgMalformed, "figure.svg", null));

        // Assert: classified by the XML exception, located on line 1 with a caret
        Assert.StartsWith("Render failed (corrupt-input) at line 1, column ", text, StringComparison.Ordinal);
        Assert.Contains("  1 | <svg", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the type of an exception decides its code, not the words of its message.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Classify_MisleadingMessage_IsClassifiedByType()
    {
        // Arrange: a Vega-Lite specification error whose message speaks of a limit
        var exception = new ImageSourceException(
            "The image 'chart.vl.json' could not be drawn by the 'vega-lite' source: chart.vl.json:2:1: "
            + "The specification is nested deeper than the limit of 64 levels.",
            "chart.vl.json",
            null!,
            new VegaLiteSpecException("The specification is nested deeper than the limit of 64 levels."));

        // Act: classify it
        var failure = RenderErrorShaper.Classify(exception);

        // Assert: a Vega-Lite error, not a budget problem
        Assert.Equal(ErrorCodes.VegaLiteError, failure.Code);
        Assert.Equal(2, failure.Line);
        Assert.Equal(1, failure.Column);
    }

    /// <summary>
    ///     Proves a refused file anywhere in the chain makes the failure a file-access failure,
    ///     located by the prefix the message carries.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Classify_RefusedFileInTheChain_IsFileAccessDenied()
    {
        // Arrange: a data file refused deep inside a Vega-Lite render
        var exception = new ImageSourceException(
            "The image 'chart.vl.json' could not be drawn by the 'vega-lite' source: chart.vl.json:3:20 /data/url: "
            + "The data file '../outside/data.csv' was denied.",
            "chart.vl.json",
            null!,
            new VegaLiteRenderException(
                "The data file was denied.",
                new FileReferenceException(FileReferenceFailureKind.AccessDenied, "../outside/data.csv", "denied", null!)));

        // Act: classify it
        var failure = RenderErrorShaper.Classify(exception);

        // Assert: the refusal decided the code; the prefix gave the location and pointer
        Assert.Equal(ErrorCodes.FileAccessDenied, failure.Code);
        Assert.Equal((3, 20, "/data/url"), (failure.Line, failure.Column, failure.Pointer));
        Assert.Equal("The data file '../outside/data.csv' was denied.", failure.Message);
    }

    /// <summary>
    ///     Proves each kind of library exception is given its stable code.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Classify_KnownExceptionTypes_AreGivenTheirCodes()
    {
        // Arrange / Act: one exception of each kind
        var budget = RenderErrorShaper.Classify(new ImageSourceBudgetException(100, 10));
        var unsupported = RenderErrorShaper.Classify(new UnsupportedImageFeatureException("filter", "text is not drawn"));
        var corrupt = RenderErrorShaper.Classify(new InvalidDataException("bad", new XmlException("bad")));
        var viewer = RenderErrorShaper.Classify(new ViewerException(ErrorCodes.PageOutOfRange, "page 3 does not exist"));
        var unknown = RenderErrorShaper.Classify(new InvalidOperationException("something else"));

        // Assert: each code
        Assert.Equal(ErrorCodes.BudgetExceeded, budget.Code);
        Assert.Equal(ErrorCodes.UnsupportedFeature, unsupported.Code);
        Assert.Equal(ErrorCodes.CorruptInput, corrupt.Code);
        Assert.Equal(ErrorCodes.PageOutOfRange, viewer.Code);
        Assert.Equal(ErrorCodes.OperationFailed, unknown.Code);
    }

    /// <summary>
    ///     Proves a help topic is named only when the help tool serves it.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Shape_HelpTopic_IsNamedOnlyWhenItExists()
    {
        // Arrange: a failure, and two help catalogs — one serving the topic, one not
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        var failure = Failure(ErrorCodes.MermaidError, "bad arrow");
        var serving = new HelpReference("diagram_help", "mermaid", topic => topic == "mermaid/diagnostics");
        var silent = new HelpReference("diagram_help", "mermaid", _ => false);

        // Act: shape with each, and with none
        var named = RenderErrorShaper.Shape(failure, new ShapeContext(policy, null, null, null, serving));
        var unnamed = RenderErrorShaper.Shape(failure, new ShapeContext(policy, null, null, null, silent));
        var none = RenderErrorShaper.Shape(failure, new ShapeContext(policy, null, null, null, null));

        // Assert: only a topic the help tool will actually serve is named
        Assert.EndsWith("Help: diagram_help topic 'mermaid/diagnostics'.", named, StringComparison.Ordinal);
        Assert.DoesNotContain("Help:", unnamed, StringComparison.Ordinal);
        Assert.DoesNotContain("Help:", none, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves host-oriented hints and phrases are replaced by advice the model can follow.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Shape_HostOrientedText_IsRewritten()
    {
        // Arrange: a refusal worded for a host, a command-line hint, and a usable library hint
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        var refusal = Failure(
            ErrorCodes.FileAccessDenied,
            "image '../x.png' was not drawn: access denied by the file-access policy 'roots+no-symlinks+agentkit-read': "
            + "the path as written is outside the allowed roots. Hint: add the folder the file is in to the allowed roots");
        var cli = Failure(ErrorCodes.PlantUmlError, "bad", "Without --strict the image is left out.");
        var usable = Failure(ErrorCodes.MermaidError, "bad", "Close the bracket.");

        // Act: shape each
        var refusalText = RenderErrorShaper.Shape(refusal, new ShapeContext(policy, null, null, null, null));
        var cliText = RenderErrorShaper.Shape(cli, new ShapeContext(policy, null, null, null, null));
        var usableText = RenderErrorShaper.Shape(usable, new ShapeContext(policy, null, null, null, null));

        // Assert: host words gone, the model's own situation stated, a usable hint kept
        Assert.Contains("by the access policy", refusalText, StringComparison.Ordinal);
        Assert.Contains("outside the locations the agent may read", refusalText, StringComparison.Ordinal);
        Assert.DoesNotContain("allowed roots", refusalText, StringComparison.Ordinal);
        Assert.Contains("Hint: Reference only files inside the locations you may read", refusalText, StringComparison.Ordinal);
        Assert.DoesNotContain("Hint:", cliText, StringComparison.Ordinal);
        Assert.Contains("Hint: Close the bracket.", usableText, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the working directory, the grant roots and the input folder are removed from text
    ///     bound for a model.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Shape_HostPaths_AreScrubbed()
    {
        // Arrange: a failure naming a file inside and a file outside the workspace by full path
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        var inside = Path.Combine(fixture.Root, "sub", "a.png");
        var outside = Path.Combine(fixture.Outside, "b.png");
        var failure = Failure(ErrorCodes.OperationFailed, "cannot read '" + inside + "' or '" + outside.Replace('\\', '/') + "'");

        // Act: shape it with the outside folder as the input's folder
        var text = RenderErrorShaper.Shape(failure, new ShapeContext(policy, fixture.Outside, null, null, null));

        // Assert: relative inside the workspace, a placeholder outside it
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\', '/'), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\', '/').Replace('\\', '/'), text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(".", "sub", "a.png"), text, StringComparison.Ordinal);
        Assert.Contains("<outside>/b.png", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a host path the library reports in another case is still scrubbed where the
    ///     platform's file names ignore case, as the access policy treats them.
    /// </summary>
    /// <remarks>
    ///     Windows and macOS ignore case in file names, so a renderer may report the workspace in a
    ///     different case from the one the policy holds. Elsewhere the two spellings name different
    ///     folders and the scenario does not apply.
    /// </remarks>
    [Fact]
    public void RenderErrorShaper_Shape_HostPathInOtherCase_IsScrubbedWhereNamesIgnoreCase()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(),
            "File names ignore case only on Windows and macOS.");

        // Arrange: a failure naming a file inside the workspace with its full path in upper case
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        var inside = Path.Combine(fixture.Root, "sub", "a.png").ToUpperInvariant();
        var failure = Failure(ErrorCodes.OperationFailed, "cannot read '" + inside + "'");

        // Act: shape it
        var text = RenderErrorShaper.Shape(failure, new ShapeContext(policy, null, null, null, null));

        // Assert: the host path is gone in any case, and the file is named relative to the workspace
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\', '/'), text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine(".", "SUB", "A.PNG"), text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a sibling of a known folder, whose name merely starts with the folder's name, is
    ///     not partially replaced as though it lay inside the folder.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Scrub_SiblingOfARoot_IsNotPartiallyReplaced()
    {
        // Arrange: a policy over the workspace, and texts naming its "-backup" sibling both ways
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        var sibling = fixture.Root + "-backup" + Path.DirectorySeparatorChar + "k.txt";
        var text = "cannot read '" + sibling + "' or '" + sibling.Replace('\\', '/') + "'";

        // Act: scrub it
        var scrubbed = RenderErrorShaper.Scrub(text, policy, null);

        // Assert: the sibling is replaced whole, never turned into a relative-looking ".-backup"
        Assert.DoesNotContain(".-backup", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Root, scrubbed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.Root.Replace('\\', '/'), scrubbed, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("cannot read '<path>' or '<path>'", scrubbed);
    }

    /// <summary>
    ///     Proves a known folder written with a device prefix, as a <c>file:</c> URI or with
    ///     percent-encoded names is replaced whole, prefix and all.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Scrub_RootInDevicePrefixAndFileUriForms_IsReplacedWhole()
    {
        // Arrange: a workspace whose name holds a space, named in each alternative spelling
        using var fixture = new TempDirectoryFixture();
        var workspace = Path.Combine(fixture.Root, "My Docs");
        Directory.CreateDirectory(workspace);
        var policy = RenderSamples.Policy(workspace);
        var file = Path.Combine(workspace, "a.png");
        var device = @"\\?\" + file;
        var deviceSlashes = "//?/" + file.Replace('\\', '/');
        var uri = new Uri(file).AbsoluteUri;

        // Act: scrub each
        var scrubbedDevice = RenderErrorShaper.Scrub("cannot read '" + device + "'", policy, null);
        var scrubbedSlashes = RenderErrorShaper.Scrub("cannot read '" + deviceSlashes + "'", policy, null);
        var scrubbedUri = RenderErrorShaper.Scrub("cannot read '" + uri + "'", policy, null);

        // Assert: each becomes the workspace-relative form, with no prefix residue
        Assert.Contains("%20", uri, StringComparison.Ordinal);
        Assert.Equal("cannot read '" + Path.Combine(".", "a.png") + "'", scrubbedDevice);
        Assert.Equal("cannot read './a.png'", scrubbedSlashes);
        Assert.Equal("cannot read './a.png'", scrubbedUri);
    }

    /// <summary>
    ///     Proves an absolute path under none of the known folders is replaced by a placeholder,
    ///     while a JSON pointer and a relative path are left as written.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Scrub_UnknownAbsolutePath_IsReplacedWithAPlaceholder()
    {
        // Arrange: a text naming host paths in every form, beside a pointer and a relative path
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        var temporary = Path.Combine(Path.GetTempPath(), "other", "x.png");
        var text = "at line 3 (/data/url): cannot read '" + temporary + "', "
            + @"C:\Users\someone\secret.txt, \\server\share\x.png, '/home/someone/y.png', "
            + "file:///home/someone/z.png or '../x.png'";

        // Act: scrub it
        var scrubbed = RenderErrorShaper.Scrub(text, policy, null);

        // Assert: every host path is gone; the pointer and the relative path survive
        Assert.Equal(
            "at line 3 (/data/url): cannot read '<path>', <path>, <path>, '<path>', <path> or '../x.png'",
            scrubbed);
    }

    /// <summary>
    ///     Proves a host path carried only by an inner exception, or by an outer message naming a
    ///     folder the policy does not know, never reaches the model.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Shape_InnerExceptionHostPath_NeverReachesTheModel()
    {
        // Arrange: a read failure whose inner I/O error names a host file outside every grant,
        // and a viewer failure whose own message embeds the same host file
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        var host = Path.Combine(Path.GetTempPath(), "cache", "locked.png");
        var io = new IOException("The process cannot access the file '" + host + "' because it is being used by another process.");
        var nested = new ImageSourceException(
            "The image 'a.puml' could not be drawn by the 'plantuml' source: the included file 'locked.png' cannot be read.",
            "a.puml",
            null!,
            new FileReferenceException(FileReferenceFailureKind.CannotRead, "locked.png", "cannot read", io));
        var folded = new ViewerException(ErrorCodes.OperationFailed, "Reading failed: " + io.Message, null, io);

        // Act: classify and shape each
        var nestedText = RenderErrorShaper.Shape(RenderErrorShaper.Classify(nested), new ShapeContext(policy, null, null, null, null));
        var foldedText = RenderErrorShaper.Shape(RenderErrorShaper.Classify(folded), new ShapeContext(policy, null, null, null, null));

        // Assert: neither names the host file, nor the temporary folder it lies in
        var temporary = Path.GetTempPath().TrimEnd('\\', '/');
        foreach (var text in new[] { nestedText, foldedText })
        {
            Assert.DoesNotContain(temporary, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(temporary.Replace('\\', '/'), text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("being used by another process", nestedText, StringComparison.Ordinal);
        Assert.Contains("'locked.png'", nestedText, StringComparison.Ordinal);
        Assert.Contains("'<path>'", foldedText, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a timeout is shaped with its own code and advice, and every note is listed.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Shape_TimeoutWithNotes_ListsTheNotes()
    {
        // Arrange: a timeout carrying two notes
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        var failure = RenderErrorShaper.Timeout(TimeSpan.FromSeconds(2)) with { Notes = ["warning a: first", "unsupported b: second"] };

        // Act: shape it
        var text = RenderErrorShaper.Shape(failure, new ShapeContext(policy, null, null, null, null));

        // Assert: code, seconds, advice and notes
        Assert.StartsWith("Render failed (render-timeout): The render did not finish within 2 seconds.", text, StringComparison.Ordinal);
        Assert.Contains("Hint: Simplify the source", text, StringComparison.Ordinal);
        Assert.Contains("Notes: warning a: first; unsupported b: second", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a long hostile text crafted against each host-path expression is scrubbed
    ///     quickly, without a timeout and without leaving a raw path behind.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Scrub_HostileLongInput_FinishesQuicklyWithoutRawText()
    {
        // Arrange: about 100 KB aimed at the quoted POSIX, drive, network and URI expressions
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        var quoted = "'" + string.Concat(Enumerable.Repeat("/a", 50000)) + " x";
        var drive = "C:/" + new string(',', 100000) + "x";
        var network = @"\\" + new string('a', 100000);
        var uri = "file://" + new string(',', 100000);

        foreach (var input in new[] { quoted, drive, network, uri })
        {
            // Act: scrub it against the clock
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var scrubbed = RenderErrorShaper.Scrub(input, policy, null);
            stopwatch.Stop();

            // Assert: linear time, and never the withheld text a timeout would give
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Scrub took {stopwatch.Elapsed}.");
            Assert.NotEqual(RenderErrorShaper.WithheldText, scrubbed);
        }

        // Assert: the drive and URI paths are replaced, leaving only the trailing punctuation
        var scrubbedDrive = RenderErrorShaper.Scrub(drive, policy, null);
        var scrubbedUri = RenderErrorShaper.Scrub(uri, policy, null);
        Assert.Equal("<path>", scrubbedDrive);
        Assert.DoesNotContain("C:/", scrubbedDrive, StringComparison.Ordinal);
        Assert.StartsWith("<path>", scrubbedUri, StringComparison.Ordinal);
        Assert.DoesNotContain("file:", scrubbedUri, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a quoted JSON pointer and a regular expression escape are kept, while quoted
    ///     paths shaped like host paths are replaced.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Scrub_QuotedJsonPointerAndWebPaths_ArePreserved()
    {
        // Arrange: quoted pointers, an escape, and quoted host paths
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        const string pointers = "field \"/data/values/0\" and \"/encoding/x/field\"";
        const string escape = @"pattern ""\\d+\\s""";

        // Act: scrub each
        var scrubbedPointers = RenderErrorShaper.Scrub(pointers, policy, null);
        var scrubbedEscape = RenderErrorShaper.Scrub(escape, policy, null);
        var scrubbedHome = RenderErrorShaper.Scrub("cannot read '/home/u/y.png'", policy, null);
        var scrubbedTemporary = RenderErrorShaper.Scrub("cannot read '/tmp/cache/x'", policy, null);
        var scrubbedNetwork = RenderErrorShaper.Scrub("cannot read '//server/share/x'", policy, null);

        // Assert: pointers and escape unchanged; host paths replaced
        Assert.Equal(pointers, scrubbedPointers);
        Assert.Equal(escape, scrubbedEscape);
        Assert.Equal("cannot read '<path>'", scrubbedHome);
        Assert.Equal("cannot read '<path>'", scrubbedTemporary);
        Assert.Equal("cannot read '<path>'", scrubbedNetwork);
    }

    /// <summary>
    ///     Proves the source excerpt is shown as written, so the caret stays under its column.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Shape_SourceExcerpt_IsNotScrubbed()
    {
        // Arrange: inline source whose line holds path-like text, failing at its second token
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        const string source = "graph TD\nA:/x --> '/home/u/y.png'";
        var failure = new RenderFailure(ErrorCodes.MermaidError, null, "bad arrow", null, null, 2, 6, null, []);

        // Act: shape it with the source in hand
        var text = RenderErrorShaper.Shape(failure, new ShapeContext(policy, null, source, "diagram.mmd", null));

        // Assert: the excerpt is verbatim and the caret is under column 6
        var lines = text.Split('\n');
        Assert.Equal("  2 | A:/x --> '/home/u/y.png'", lines[1]);
        Assert.Equal(lines[1].IndexOf(" | ", StringComparison.Ordinal) + 3 + 5, lines[2].IndexOf('^', StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves a scrub that reaches its time limit withholds the text rather than returning it
    ///     as written.
    /// </summary>
    [Fact]
    public void RenderErrorShaper_Scrub_Timeout_ReturnsWithheldTextNeverRaw()
    {
        // Arrange: a scrub that times out
        static string TimedOut() => throw new RegexMatchTimeoutException("raw C:\\secret", "pattern", TimeSpan.FromSeconds(1));

        // Act: guard it
        var guarded = RenderErrorShaper.Guard(TimedOut);

        // Assert: the withheld text, never the raw input
        Assert.Equal(RenderErrorShaper.WithheldText, guarded);
        Assert.DoesNotContain("secret", guarded, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds a failure with no location.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="message">The message.</param>
    /// <param name="hint">The library hint, or null.</param>
    /// <returns>The failure.</returns>
    private static RenderFailure Failure(string code, string message, string? hint = null)
    {
        return new RenderFailure(code, null, message, hint, null, null, null, null, []);
    }
}
