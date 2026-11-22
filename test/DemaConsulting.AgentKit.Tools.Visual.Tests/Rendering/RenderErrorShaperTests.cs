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
