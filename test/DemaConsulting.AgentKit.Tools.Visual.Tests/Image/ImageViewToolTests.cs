using System.Text;
using System.Text.Json;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Image;
using DemaConsulting.AgentKit.Tools.Visual.Rendering;
using DemaConsulting.AgentKit.Tools.Visual.Tests.Rendering;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Image;

/// <summary>
///     Unit tests for the <see cref="ImageViewTool"/> class.
/// </summary>
/// <remarks>
///     Every scenario invokes the constructed tool exactly as a runtime would — through
///     <see cref="AIFunction.InvokeAsync"/> with named arguments — over real files in a temporary
///     tree, drawn by the real rendering library, because what the tool promises is what a model
///     receives: a picture, a report it can act on, or a denial.
/// </remarks>
public class ImageViewToolTests
{
    /// <summary>
    ///     Proves the published tool name is the family-qualified name the pack claims.
    /// </summary>
    [Fact]
    public void ImageViewTool_ToolName_Constant_IsTheFamilyQualifiedName()
    {
        // Arrange / Act: read the published constant
        var name = ImageViewTool.ToolName;

        // Assert: the name is qualified by the family prefix the pack claims
        Assert.Equal("image_view", name);
        Assert.StartsWith(ImagePack.FamilyPrefix + "_", name, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the constructed tool carries the published name and a description, and that a
    ///     missing policy or renderer is a programming error.
    /// </summary>
    [Fact]
    public void ImageViewTool_Create_ConstructedTool_CarriesTheToolNameAndADescription()
    {
        // Arrange: a policy and a renderer
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadOnly)]);
        var renderer = new VisualRenderer(VisualOptions.Default);

        // Act: construct the tool
        var tool = ImageViewTool.Create(policy, renderer);

        // Assert: the model sees the name and a description; nothing is built without either part
        Assert.Equal(ImageViewTool.ToolName, tool.Name);
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        Assert.Throws<ArgumentNullException>(() => ImageViewTool.Create(null!, renderer));
        Assert.Throws<ArgumentNullException>(() => ImageViewTool.Create(policy, null!));
    }

    /// <summary>
    ///     Proves each supported kind of file is drawn and returned as PNG content, not serialized.
    /// </summary>
    /// <param name="fileName">The file to draw.</param>
    /// <param name="format">The format the caption must name.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData("figure.svg", "svg")]
    [InlineData("flow.mmd", "mermaid")]
    [InlineData("sequence.puml", "plantuml")]
    [InlineData("chart.vl.json", "vega-lite")]
    [InlineData("part.scad", "scad")]
    [InlineData("report.pdf", "pdf")]
    [InlineData("deck.pptx", "pptx")]
    [InlineData("drawing.vsdx", "vsdx")]
    [InlineData("photo.png", "png")]
    public async Task ImageViewTool_View_SupportedFile_ReturnsAPngPicture(string fileName, string format)
    {
        // Arrange: a permitted file of the kind
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, fileName, Sample(fileName));
        var tool = CreateTool(RenderSamples.Policy(fixture.Root));

        // Act: view it by its workspace-relative name
        var result = await InvokeAsync(tool, new() { ["path"] = fileName });

        // Assert: a caption naming the format, then an opaque PNG
        Assert.IsNotType<JsonElement>(result);
        var content = Assert.IsType<List<AIContent>>(result);
        var caption = Assert.IsType<TextContent>(content[0]).Text;
        Assert.StartsWith("Rendered " + format, caption, StringComparison.Ordinal);
        var data = Assert.IsType<DataContent>(content[1]);
        Assert.Equal(ImageMediaTypes.Png, data.MediaType);
        using var surface = RenderSamples.Decode(data.Data.ToArray());
        Assert.Equal(255, surface.GetRowSpan(0)[0].A);
    }

    /// <summary>
    ///     Proves the requested page of a document is drawn, and the caption states it of the
    ///     page count.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageViewTool_View_SecondPage_CaptionStatesThePageOfTheCount()
    {
        // Arrange: a two-page PDF
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "report.pdf", RenderSamples.Pdf(2));
        var tool = CreateTool(RenderSamples.Policy(fixture.Root));

        // Act: view its second page
        var result = await InvokeAsync(tool, new() { ["path"] = "report.pdf", ["page"] = 2 });

        // Assert: the caption states which page of how many
        var caption = Assert.IsType<TextContent>(Assert.IsType<List<AIContent>>(result)[0]).Text;
        Assert.StartsWith("Rendered pdf page 2 of 2 as PNG", caption, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a page the document does not have is reported, and a page below 1 is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageViewTool_View_PageOutOfRange_IsReported()
    {
        // Arrange: a two-page PDF
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "report.pdf", RenderSamples.Pdf(2));
        var tool = CreateTool(RenderSamples.Policy(fixture.Root));

        // Act: ask for a third page, and for page zero
        var missing = Assert.IsType<string>(await InvokeAsync(tool, new() { ["path"] = "report.pdf", ["page"] = 3 }));
        var invalid = Assert.IsType<string>(await InvokeAsync(tool, new() { ["path"] = "report.pdf", ["page"] = 0 }));

        // Assert: the missing page is a report naming the count; page zero a malformed request
        Assert.StartsWith("Render failed (page-out-of-range)", missing, StringComparison.Ordinal);
        Assert.Contains("2 pages", missing, StringComparison.Ordinal);
        Assert.StartsWith("Denied (InvalidRequest)", invalid, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a requested width is applied, and a width below 1 is refused.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageViewTool_View_RequestedWidth_IsApplied()
    {
        // Arrange: a permitted SVG
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "figure.svg", RenderSamples.Svg);
        var tool = CreateTool(RenderSamples.Policy(fixture.Root));

        // Act: ask for 300 pixels, and for none
        var result = await InvokeAsync(tool, new() { ["path"] = "figure.svg", ["width"] = 300 });
        var invalid = Assert.IsType<string>(await InvokeAsync(tool, new() { ["path"] = "figure.svg", ["width"] = 0 }));

        // Assert: drawn at the width, keeping the aspect ratio; zero refused
        var caption = Assert.IsType<TextContent>(Assert.IsType<List<AIContent>>(result)[0]).Text;
        Assert.Contains("300x150 pixels", caption, StringComparison.Ordinal);
        Assert.StartsWith("Denied (InvalidRequest)", invalid, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file outside the policy is refused before anything is learned about it, with
    ///     the same refusal whether or not it exists.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageViewTool_View_PathOutsideThePolicy_IsRefusedBeforeAnyRead()
    {
        // Arrange: one existing and one missing file outside the grant
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Outside, "secret.svg", RenderSamples.Svg);
        var tool = CreateTool(RenderSamples.Policy(fixture.Root));

        // Act: ask for each
        var existing = Assert.IsType<string>(await InvokeAsync(tool, new() { ["path"] = "../outside/secret.svg" }));
        var missing = Assert.IsType<string>(await InvokeAsync(tool, new() { ["path"] = "../outside/missing.svg" }));

        // Assert: the policy's refusal, identical apart from the name, so existence is not revealed
        Assert.StartsWith("Denied (PathNotPermitted)", existing, StringComparison.Ordinal);
        Assert.StartsWith("Denied (PathNotPermitted)", missing, StringComparison.Ordinal);
        Assert.Equal(existing.Replace("secret", "missing", StringComparison.Ordinal), missing);
    }

    /// <summary>
    ///     Proves a missing path, a directory and a missing file are each refused as such.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageViewTool_View_MalformedRequests_AreRefused()
    {
        // Arrange: a workspace holding one directory
        using var fixture = new TempDirectoryFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "docs.svg"));
        var tool = CreateTool(RenderSamples.Policy(fixture.Root));

        // Act: no path, a directory, a missing file
        var noPath = Assert.IsType<string>(await InvokeAsync(tool, []));
        var directory = Assert.IsType<string>(await InvokeAsync(tool, new() { ["path"] = "docs.svg" }));
        var missing = Assert.IsType<string>(await InvokeAsync(tool, new() { ["path"] = "absent.svg" }));

        // Assert: each refused with its reason
        Assert.StartsWith("Denied (InvalidRequest)", noPath, StringComparison.Ordinal);
        Assert.StartsWith("Denied (InvalidRequest)", directory, StringComparison.Ordinal);
        Assert.StartsWith("Denied (TargetNotFound)", missing, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file of a kind the tool cannot draw is refused as an unsupported type.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageViewTool_View_UnsupportedType_IsRefused()
    {
        // Arrange: a permitted word-processor document
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "letter.docx", "not drawable");
        var tool = CreateTool(RenderSamples.Policy(fixture.Root));

        // Act: view it
        var result = Assert.IsType<string>(await InvokeAsync(tool, new() { ["path"] = "letter.docx" }));

        // Assert: refused as a type, before any render
        Assert.StartsWith("Denied (UnsupportedMediaType)", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a file beyond the binary ceiling is refused naming the ceiling, never drawn in
    ///     part.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageViewTool_View_FileBeyondTheBinaryCeiling_IsRefused()
    {
        // Arrange: a ceiling smaller than the file
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "figure.svg", RenderSamples.Svg);
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadWrite(fixture.Root)], new ToolLimits(maxBinaryBytes: 64));
        var tool = CreateTool(policy);

        // Act: view it
        var result = Assert.IsType<string>(await InvokeAsync(tool, new() { ["path"] = "figure.svg" }));

        // Assert: refused, naming the ceiling
        Assert.StartsWith("Denied (ResourceTooLarge)", result, StringComparison.Ordinal);
        Assert.Contains("64-byte", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a picture larger than the binary ceiling is refused rather than returned.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageViewTool_View_PictureBeyondTheBinaryCeiling_IsRefused()
    {
        // Arrange: a ceiling the small source fits under and its drawing does not
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "figure.svg", RenderSamples.Svg);
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadWrite(fixture.Root)], new ToolLimits(maxBinaryBytes: 1000));
        var tool = CreateTool(policy);

        // Act: view it
        var result = Assert.IsType<string>(await InvokeAsync(tool, new() { ["path"] = "figure.svg" }));

        // Assert: refused as too large a result
        Assert.StartsWith("Denied (ResourceTooLarge)", result, StringComparison.Ordinal);
        Assert.Contains("rendered picture", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a broken file is reported with its code, location and the offending line, names
    ///     no help topic, and discloses no host path.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageViewTool_View_BrokenFile_IsReportedWithTheOffendingLine()
    {
        // Arrange: a Mermaid file with a malformed arrow
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "flow.mmd", RenderSamples.MermaidBadArrow);
        var tool = CreateTool(RenderSamples.Policy(fixture.Root));

        // Act: view it
        var result = Assert.IsType<string>(await InvokeAsync(tool, new() { ["path"] = "flow.mmd" }));

        // Assert: a report the model can act on, not a denial
        Assert.StartsWith("Render failed (mermaid-error/mermaid-syntax-error) at line 2, column 5:", result, StringComparison.Ordinal);
        Assert.Contains("  2 |   A -> B", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Help:", result, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\', '/'), result, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Proves a note the render reported reaches the caption.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageViewTool_View_RenderWithNotes_CaptionListsThem()
    {
        // Arrange: a model calling a module that does not exist, beside one that draws
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "part.scad", "no_such_module();\ncube(10);\n");
        var tool = CreateTool(RenderSamples.Policy(fixture.Root));

        // Act: view it
        var result = await InvokeAsync(tool, new() { ["path"] = "part.scad" });

        // Assert: drawn, with the note in the caption
        var caption = Assert.IsType<TextContent>(Assert.IsType<List<AIContent>>(result)[0]).Text;
        Assert.Contains("Notes: warning scad-warning", caption, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a picture the document embeds from outside the grant fails the view.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task ImageViewTool_View_ReferenceOutsideTheGrant_FailsTheView()
    {
        // Arrange: a permitted SVG embedding a picture outside the grant
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Outside, "secret.png", ImageTestImages.Png(20, 20));
        TempDirectoryFixture.WriteFile(fixture.Root, "figure.svg", RenderSamples.SvgWithImage("../outside/secret.png"));
        var tool = CreateTool(RenderSamples.Policy(fixture.Root));

        // Act: view it
        var result = Assert.IsType<string>(await InvokeAsync(tool, new() { ["path"] = "figure.svg" }));

        // Assert: reported as a file-access failure
        Assert.StartsWith("Render failed (file-access-denied", result, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Builds the content of a sample file of the named kind.
    /// </summary>
    /// <param name="fileName">The file's name.</param>
    /// <returns>The content.</returns>
    private static byte[] Sample(string fileName)
    {
        var extension = fileName[(fileName.IndexOf('.', StringComparison.Ordinal) + 1)..];
        return extension switch
        {
            "svg" => Encoding.UTF8.GetBytes(RenderSamples.Svg),
            "mmd" => Encoding.UTF8.GetBytes(RenderSamples.Mermaid),
            "puml" => Encoding.UTF8.GetBytes(RenderSamples.PlantUml),
            "vl.json" => Encoding.UTF8.GetBytes(RenderSamples.VegaLite),
            "scad" => Encoding.UTF8.GetBytes(RenderSamples.Scad),
            "pdf" => RenderSamples.Pdf(1),
            "pptx" => RenderSamples.Pptx(),
            "vsdx" => RenderSamples.Vsdx(),
            _ => ImageTestImages.Png(60, 40)
        };
    }

    /// <summary>
    ///     Builds the tool over a fresh renderer.
    /// </summary>
    /// <param name="policy">The access policy.</param>
    /// <returns>The tool.</returns>
    private static AIFunction CreateTool(PathPolicy policy)
    {
        return ImageViewTool.Create(policy, new VisualRenderer(VisualOptions.Default));
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
