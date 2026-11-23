using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Diagram;
using DemaConsulting.AgentKit.Tools.Visual.Image;
using DemaConsulting.AgentKit.Tools.Visual.Rendering;
using DemaConsulting.AgentKit.Tools.Visual.Tests.Image;
using DemaConsulting.AgentKit.Tools.Visual.Tests.Rendering;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Diagram;

/// <summary>
///     Unit tests for the <see cref="DiagramRenderTool"/> class.
/// </summary>
/// <remarks>
///     Every scenario invokes the constructed tool exactly as a runtime would — through
///     <see cref="AIFunction.InvokeAsync"/> with named arguments — drawn by the real rendering
///     library, over a real temporary tree wherever a file is referenced or saved, because what the
///     tool promises is what a model receives: a picture, a description, a report it can act on, or
///     a denial.
/// </remarks>
public class DiagramRenderToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void DiagramRenderTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        // Arrange / Act: read the published constant
        var name = DiagramRenderTool.ToolName;

        // Assert: the name is qualified by the family prefix the pack claims
        Assert.Equal("diagram_render", name);
        Assert.StartsWith(DiagramPack.FamilyPrefix + "_", name, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves each preview mode builds a tool carrying the name and a description of its own
    ///     result, and that missing or empty arguments are programming errors.
    /// </summary>
    [Fact]
    public void DiagramRenderTool_Create_EachPreview_DescribesItsOwnResult()
    {
        // Arrange: the parts a pack supplies
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
        var renderer = new VisualRenderer(VisualOptions.Default);
        string[] formats = ["mermaid", "svg"];
        var catalog = DiagramHelpTool.CatalogFor(formats);

        // Act: build the tool in each mode
        var image = DiagramRenderTool.Create(policy, renderer, DiagramPreview.Image, formats, catalog);
        var text = DiagramRenderTool.Create(policy, renderer, DiagramPreview.Text, formats, catalog);

        // Assert: one name; each description states its own result and the offered languages
        Assert.Equal(DiagramRenderTool.ToolName, image.Name);
        Assert.Equal(DiagramRenderTool.ToolName, text.Name);
        Assert.Contains("returns the picture", image.Description, StringComparison.Ordinal);
        Assert.Contains("returns a description of it", text.Description, StringComparison.Ordinal);
        Assert.Contains("mermaid, svg", image.Description, StringComparison.Ordinal);
        Assert.Contains("Optional path", image.JsonSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("Required path", text.JsonSchema.GetRawText(), StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => DiagramRenderTool.Create(null!, renderer, DiagramPreview.Image, formats, catalog));
        Assert.Throws<ArgumentNullException>(() => DiagramRenderTool.Create(policy, null!, DiagramPreview.Image, formats, catalog));
        Assert.Throws<ArgumentNullException>(() => DiagramRenderTool.Create(policy, renderer, DiagramPreview.Image, null!, catalog));
        Assert.Throws<ArgumentNullException>(() => DiagramRenderTool.Create(policy, renderer, DiagramPreview.Image, formats, null!));
        Assert.Throws<ArgumentException>(() => DiagramRenderTool.Create(policy, renderer, DiagramPreview.Image, [], catalog));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagramRenderTool.Create(policy, renderer, (DiagramPreview)9, formats, catalog));
    }

    /// <summary>
    ///     Proves each offered language is drawn and returned as an opaque PNG, not serialized, with
    ///     a caption naming the language and the size.
    /// </summary>
    /// <param name="format">The language.</param>
    /// <param name="source">A source in it.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("mermaid", RenderSamples.Mermaid)]
    [InlineData("plantuml", RenderSamples.PlantUml)]
    [InlineData("svg", RenderSamples.Svg)]
    [InlineData("vega-lite", RenderSamples.VegaLite)]
    [InlineData("openscad", RenderSamples.Scad)]
    public async Task DiagramRenderTool_Render_EachLanguage_ReturnsAnOpaquePngPicture(string format, string source)
    {
        // Arrange: an image-preview tool
        var tool = CreateTool(DiagramPreview.Image, ReadOnlyTempPolicy());

        // Act: draw the source
        var result = await InvokeAsync(tool, new() { ["format"] = format, ["source"] = source });

        // Assert: a caption naming the language, then an opaque white-cornered PNG
        Assert.IsNotType<JsonElement>(result);
        var content = Assert.IsType<List<AIContent>>(result);
        var caption = Assert.IsType<TextContent>(content[0]).Text;
        Assert.StartsWith("Rendered " + format + " as PNG, ", caption, StringComparison.Ordinal);
        var data = Assert.IsType<DataContent>(content[1]);
        Assert.Equal(ImageMediaTypes.Png, data.MediaType);
        using var surface = RenderSamples.Decode(data.Data.ToArray());
        var corner = surface.GetRowSpan(0)[0];
        Assert.Equal(255, corner.A);
        Assert.Equal((255, 255, 255), (corner.R, corner.G, corner.B));
    }

    /// <summary>
    ///     Proves a picture is saved where <c>save_as</c> names, the caption names the file, and the
    ///     saved bytes are the returned picture.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramRenderTool_Render_SaveAs_SavesThePictureAndNamesIt()
    {
        // Arrange: a writable workspace holding a diagrams folder
        using var fixture = new TempDirectoryFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "diagrams"));
        var tool = CreateTool(DiagramPreview.Image, RenderSamples.Policy(fixture.Root));

        // Act: draw and save
        var result = await InvokeAsync(
            tool,
            new() { ["format"] = "mermaid", ["source"] = RenderSamples.Mermaid, ["save_as"] = "diagrams/flow.png" });

        // Assert: the caption names the saved file, which holds the returned picture
        var content = Assert.IsType<List<AIContent>>(result);
        var caption = Assert.IsType<TextContent>(content[0]).Text;
        Assert.Contains("saved to " + Path.Combine("diagrams", "flow.png") + ".", caption, StringComparison.Ordinal);
        var saved = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "diagrams", "flow.png"), TestContext.Current.CancellationToken);
        Assert.Equal(Assert.IsType<DataContent>(content[1]).Data.ToArray(), saved);
    }

    /// <summary>
    ///     Proves saving again under the same name replaces the earlier file and leaves no temporary
    ///     file behind.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramRenderTool_Render_SaveAsExistingFile_ReplacesIt()
    {
        // Arrange: a writable workspace already holding a file of the name
        using var fixture = new TempDirectoryFixture();
        var target = TempDirectoryFixture.WriteFile(fixture.Root, "flow.png", "an earlier rendering");
        var tool = CreateTool(DiagramPreview.Image, RenderSamples.Policy(fixture.Root));

        // Act: draw and save under the same name
        var result = await InvokeAsync(
            tool,
            new() { ["format"] = "svg", ["source"] = RenderSamples.Svg, ["save_as"] = "flow.png" });

        // Assert: the file now holds the PNG, and the folder holds nothing else
        Assert.IsType<List<AIContent>>(result);
        var saved = await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken);
        Assert.Equal(0x89, saved[0]);
        Assert.Equal([target], Directory.GetFiles(fixture.Root));
    }

    /// <summary>
    ///     Proves a destination that is not a PNG, outside the write grant, or in a missing folder is
    ///     refused before anything is drawn, and nothing is written.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramRenderTool_Render_SaveAsRefusals_WriteNothing()
    {
        // Arrange: a workspace that may be written, and one that may only be read
        using var fixture = new TempDirectoryFixture();
        var writable = CreateTool(DiagramPreview.Image, RenderSamples.Policy(fixture.Root));
        var readOnly = CreateTool(DiagramPreview.Image, new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]));

        // Act: name each kind of unusable destination
        var notPng = await SaveAsync(writable, "flow.jpg");
        var outside = await SaveAsync(writable, "../outside/flow.png");
        var missing = await SaveAsync(writable, "absent/flow.png");
        var notWritable = await SaveAsync(readOnly, "flow.png");

        // Assert: each refused with its reason, and no file anywhere
        Assert.StartsWith("Denied (InvalidRequest)", notPng, StringComparison.Ordinal);
        Assert.StartsWith("Denied (PathNotPermitted)", outside, StringComparison.Ordinal);
        Assert.StartsWith("Denied (TargetNotFound)", missing, StringComparison.Ordinal);
        Assert.StartsWith("Denied (PathNotPermitted)", notWritable, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(fixture.Outside) && Directory.GetFiles(fixture.Outside).Length > 0);
    }

    /// <summary>
    ///     Proves text preview requires <c>save_as</c>.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramRenderTool_Render_TextPreviewWithoutSaveAs_IsRefused()
    {
        // Arrange: a text-preview tool over a writable workspace
        using var fixture = new TempDirectoryFixture();
        var tool = CreateTool(DiagramPreview.Text, RenderSamples.Policy(fixture.Root));

        // Act: draw without naming a destination
        var result = Assert.IsType<string>(await InvokeAsync(tool, new() { ["format"] = "mermaid", ["source"] = RenderSamples.Mermaid }));

        // Assert: refused, saying what to supply
        Assert.StartsWith("Denied (InvalidRequest)", result, StringComparison.Ordinal);
        Assert.Contains("save_as is required", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves text preview saves the picture and returns a description of it in words — its
    ///     size, the labels it contains and its dominant colors — rather than the picture.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramRenderTool_Render_TextPreview_SavesAndDescribesThePicture()
    {
        // Arrange: a text-preview tool over a writable workspace
        using var fixture = new TempDirectoryFixture();
        var tool = CreateTool(DiagramPreview.Text, RenderSamples.Policy(fixture.Root));

        // Act: draw and save
        var result = await InvokeAsync(
            tool,
            new() { ["format"] = "mermaid", ["source"] = RenderSamples.Mermaid, ["save_as"] = "flow.png" });

        // Assert: a structured description naming the file, the size, the labels and the colors
        var description = Assert.IsType<JsonElement>(result);
        Assert.Equal("flow.png", description.GetProperty("savedTo").GetString());
        Assert.Equal("mermaid", description.GetProperty("format").GetString());
        Assert.True(description.GetProperty("width").GetInt32() > 0);
        var features = description.GetProperty("features").EnumerateArray().Select(feature => feature.GetString()).ToList();
        Assert.Contains(features, feature => feature!.Contains("\"Draft\"", StringComparison.Ordinal));
        Assert.Contains(features, feature => feature!.Contains("\"Publish\"", StringComparison.Ordinal));
        Assert.NotEmpty(description.GetProperty("dominantColors").EnumerateArray());
        Assert.True(File.Exists(Path.Combine(fixture.Root, "flow.png")));
    }

    /// <summary>
    ///     Proves a relative reference in inline source resolves against the working directory and
    ///     is drawn when it lies inside the grant.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramRenderTool_Render_InlineReferenceInsideTheGrant_IsDrawn()
    {
        // Arrange: a picture in the working directory, referenced relatively
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "ok.png", ImageTestImages.Png(20, 20));
        var tool = CreateTool(DiagramPreview.Image, new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]));

        // Act: draw an SVG embedding it
        var result = await InvokeAsync(tool, new() { ["format"] = "svg", ["source"] = RenderSamples.SvgWithImage("ok.png") });

        // Assert: drawn, with no note about the picture
        var caption = Assert.IsType<TextContent>(Assert.IsType<List<AIContent>>(result)[0]).Text;
        Assert.DoesNotContain("Notes:", caption, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a reference outside the grant fails the render, naming no host path, and a relative
    ///     reference fails closed when the working directory may not be read.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramRenderTool_Render_InlineReferenceOutsideTheGrant_FailsTheRender()
    {
        // Arrange: a picture outside the grant; and a policy granting only a subfolder of its
        // working directory, with a picture beside that subfolder
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Outside, "secret.png", ImageTestImages.Png(20, 20));
        TempDirectoryFixture.WriteBytes(fixture.Root, "beside.png", ImageTestImages.Png(20, 20));
        var sub = Directory.CreateDirectory(Path.Combine(fixture.Root, "sub")).FullName;
        var granted = CreateTool(DiagramPreview.Image, new PathPolicy(fixture.Root, [PathRule.ReadOnly(fixture.Root)]));
        var ungranted = CreateTool(DiagramPreview.Image, new PathPolicy(fixture.Root, [PathRule.ReadWrite(sub)]));

        // Act: reference outside the grant; and reference relatively without a readable base
        var escape = await InvokeAsync(granted, new() { ["format"] = "svg", ["source"] = RenderSamples.SvgWithImage("../outside/secret.png") });
        var closed = await InvokeAsync(ungranted, new() { ["format"] = "svg", ["source"] = RenderSamples.SvgWithImage("beside.png") });

        // Assert: neither picture is drawn - the escape fails the render naming no host path, and the
        // unresolvable reference is left out with a note saying so
        var escapeText = Assert.IsType<string>(escape);
        Assert.StartsWith("Render failed (file-access-denied", escapeText, StringComparison.Ordinal);
        Assert.Contains("Help: diagram_help topic 'svg/diagnostics'.", escapeText, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\', '/'), escapeText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("svg-image-unresolved", Caption(closed), StringComparison.Ordinal);
        Assert.Contains("'beside.png' was not drawn", Caption(closed), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves malformed requests — an unknown or unoffered language, no source, a width below 1 —
    ///     are refused, each with its reason.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramRenderTool_Render_MalformedRequests_AreRefused()
    {
        // Arrange: a tool offering Mermaid alone
        var tool = CreateTool(DiagramPreview.Image, ReadOnlyTempPolicy(), ["mermaid"]);

        // Act: an unknown language, an unoffered one, no source, a zero width
        var unknown = Assert.IsType<string>(await InvokeAsync(tool, new() { ["format"] = "graphviz", ["source"] = "digraph {}" }));
        var unoffered = Assert.IsType<string>(await InvokeAsync(tool, new() { ["format"] = "svg", ["source"] = RenderSamples.Svg }));
        var noSource = Assert.IsType<string>(await InvokeAsync(tool, new() { ["format"] = "mermaid", ["source"] = "  " }));
        var zeroWidth = Assert.IsType<string>(await InvokeAsync(tool, new() { ["format"] = "mermaid", ["source"] = RenderSamples.Mermaid, ["width"] = 0 }));

        // Assert: each refused as a malformed request; the language refusals list the offer
        Assert.StartsWith("Denied (InvalidRequest)", unknown, StringComparison.Ordinal);
        Assert.Contains("one of: mermaid.", unknown, StringComparison.Ordinal);
        Assert.StartsWith("Denied (InvalidRequest)", unoffered, StringComparison.Ordinal);
        Assert.StartsWith("Denied (InvalidRequest)", noSource, StringComparison.Ordinal);
        Assert.StartsWith("Denied (InvalidRequest)", zeroWidth, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a source larger than the policy's read ceiling is refused naming the ceiling, and a
    ///     picture larger than the binary ceiling is refused rather than returned.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramRenderTool_Render_BeyondTheCeilings_IsRefused()
    {
        // Arrange: one policy whose read ceiling the source exceeds, one whose binary ceiling the
        // picture exceeds
        var smallRead = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)], new ToolLimits(maxReadBytes: 64));
        var smallBinary = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)], new ToolLimits(maxBinaryBytes: 1000));

        // Act: draw the same SVG under each
        var source = Assert.IsType<string>(await InvokeAsync(CreateTool(DiagramPreview.Image, smallRead), new() { ["format"] = "svg", ["source"] = RenderSamples.Svg }));
        var picture = Assert.IsType<string>(await InvokeAsync(CreateTool(DiagramPreview.Image, smallBinary), new() { ["format"] = "svg", ["source"] = RenderSamples.Svg }));

        // Assert: each refused as too large, naming its ceiling
        Assert.StartsWith("Denied (ResourceTooLarge)", source, StringComparison.Ordinal);
        Assert.Contains("64-byte", source, StringComparison.Ordinal);
        Assert.StartsWith("Denied (ResourceTooLarge)", picture, StringComparison.Ordinal);
        Assert.Contains("rendered picture", picture, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a broken source of each language is reported with its code, line and column, the
    ///     offending line with a caret, and the help topic of the language's diagnostics.
    /// </summary>
    /// <param name="format">The language.</param>
    /// <param name="source">A broken source in it.</param>
    /// <param name="code">The code the report must name.</param>
    /// <param name="line">The line the report must point at.</param>
    /// <param name="topic">The help topic the report must name.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("mermaid", RenderSamples.MermaidBadArrow, "mermaid-error", 2, "mermaid/diagnostics")]
    [InlineData("plantuml", RenderSamples.PlantUmlBadStatement, "plantuml-error", 2, "plantuml/diagnostics")]
    [InlineData("vega-lite", RenderSamples.VegaLiteUnknownMark, "vega-lite-error", 1, "vega-lite/diagnostics")]
    [InlineData("openscad", RenderSamples.ScadMissingParenthesis, "scad-error", 2, "scad/diagnostics")]
    [InlineData("svg", RenderSamples.SvgMalformed, "corrupt-input", 1, "svg/diagnostics")]
    public async Task DiagramRenderTool_Render_BrokenSource_IsShapedWithItsLocationAndHelpTopic(
        string format,
        string source,
        string code,
        int line,
        string topic)
    {
        // Arrange: an image-preview tool
        var tool = CreateTool(DiagramPreview.Image, ReadOnlyTempPolicy());

        // Act: draw the broken source
        var result = Assert.IsType<string>(await InvokeAsync(tool, new() { ["format"] = format, ["source"] = source }));

        // Assert: a report, not a denial, locating the failure and naming the topic to read
        var lines = result.Split('\n');
        Assert.StartsWith("Render failed (" + code, lines[0], StringComparison.Ordinal);
        Assert.Contains("at line " + line + ", column ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("  " + line + " | ", lines[1], StringComparison.Ordinal);
        Assert.Contains("^", lines[2], StringComparison.Ordinal);
        Assert.EndsWith("Help: diagram_help topic '" + topic + "'.", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a picture that came out empty is reported as a failure rather than shown.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramRenderTool_Render_EmptyPicture_IsReportedAsAFailure()
    {
        // Arrange: an image-preview tool
        var tool = CreateTool(DiagramPreview.Image, ReadOnlyTempPolicy());

        // Act: draw an SVG that draws nothing
        var result = Assert.IsType<string>(await InvokeAsync(tool, new() { ["format"] = "svg", ["source"] = RenderSamples.SvgBlank }));

        // Assert: reported as empty
        Assert.StartsWith("Render failed (empty-render)", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a note the render reported reaches the caption.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramRenderTool_Render_RenderWithNotes_CaptionListsThem()
    {
        // Arrange: an image-preview tool
        var tool = CreateTool(DiagramPreview.Image, ReadOnlyTempPolicy());

        // Act: draw a model calling a module that does not exist, beside one that draws
        var result = await InvokeAsync(tool, new() { ["format"] = "openscad", ["source"] = "no_such_module();\ncube(10);\n" });

        // Assert: drawn, with the note in the caption
        var caption = Assert.IsType<TextContent>(Assert.IsType<List<AIContent>>(result)[0]).Text;
        Assert.Contains("Notes: warning scad-warning", caption, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a requested width is applied up to the largest render side, keeping the aspect
    ///     ratio.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramRenderTool_Render_RequestedWidth_IsAppliedUpToTheLargestSide()
    {
        // Arrange: a pack-sized renderer with a largest side of 400 pixels
        var tool = CreateTool(DiagramPreview.Image, ReadOnlyTempPolicy(), options: new VisualOptions { MaxRenderSide = 400, MinPreviewSide = 100 });

        // Act: ask for 300 pixels, and for 5000
        var fits = await InvokeAsync(tool, new() { ["format"] = "svg", ["source"] = RenderSamples.Svg, ["width"] = 300 });
        var reduced = await InvokeAsync(tool, new() { ["format"] = "svg", ["source"] = RenderSamples.Svg, ["width"] = 5000 });

        // Assert: 300 wide as asked; 5000 reduced to the largest side
        Assert.Contains("300x150 pixels", Caption(fits), StringComparison.Ordinal);
        Assert.Contains("400x200 pixels", Caption(reduced), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a render that does not finish within the timeout is reported as a timeout.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task DiagramRenderTool_Render_BeyondTheTimeout_IsReportedAsATimeout()
    {
        // Arrange: a 100-millisecond timeout and a model that loops far longer than that
        var tool = CreateTool(
            DiagramPreview.Image,
            new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)], new ToolLimits(maxReadBytes: 1024 * 1024)),
            options: new VisualOptions { RenderTimeout = TimeSpan.FromMilliseconds(100) });

        // Act: draw it
        var result = Assert.IsType<string>(await InvokeAsync(tool, new() { ["format"] = "openscad", ["source"] = RenderSamples.EndlessScad() }));

        // Assert: reported as a timeout, with advice
        Assert.StartsWith("Render failed (render-timeout)", result, StringComparison.Ordinal);
        Assert.Contains("Hint: Simplify the source", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds the tool as a pack builds it.
    /// </summary>
    /// <param name="preview">The preview mode.</param>
    /// <param name="policy">The access policy.</param>
    /// <param name="formats">The offered languages, or null for every language.</param>
    /// <param name="options">The rendering bounds, or null for the defaults.</param>
    /// <returns>The tool.</returns>
    private static AIFunction CreateTool(
        DiagramPreview preview,
        PathPolicy policy,
        IReadOnlyList<string>? formats = null,
        VisualOptions? options = null)
    {
        formats ??= DiagramPackOptions.Default.Formats;
        return DiagramRenderTool.Create(
            policy,
            new VisualRenderer(options ?? VisualOptions.Default),
            preview,
            formats,
            DiagramHelpTool.CatalogFor(formats));
    }

    /// <summary>
    ///     Builds a read-only policy over the temporary folder, for renders that touch no file.
    /// </summary>
    /// <returns>The policy.</returns>
    private static PathPolicy ReadOnlyTempPolicy()
    {
        return new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)]);
    }

    /// <summary>
    ///     Draws a small SVG and saves it to a destination, returning the refusal.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <param name="saveAs">The destination.</param>
    /// <returns>The refusal text.</returns>
    private static async Task<string> SaveAsync(AIFunction tool, string saveAs)
    {
        return Assert.IsType<string>(await InvokeAsync(
            tool,
            new() { ["format"] = "svg", ["source"] = RenderSamples.Svg, ["save_as"] = saveAs }));
    }

    /// <summary>
    ///     Reads the caption of a picture result.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <returns>The caption.</returns>
    private static string Caption(object? result)
    {
        return Assert.IsType<TextContent>(Assert.IsType<List<AIContent>>(result)[0]).Text;
    }

    /// <summary>
    ///     Invokes the tool exactly as a runtime would.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <param name="arguments">The named arguments.</param>
    /// <returns>The result.</returns>
    private static async Task<object?> InvokeAsync(AIFunction tool, AIFunctionArguments arguments)
    {
        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }
}
