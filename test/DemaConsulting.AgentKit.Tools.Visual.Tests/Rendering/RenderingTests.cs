using DemaConsulting.AgentKit.Tools.Visual.Rendering;
using DemaConsulting.AgentKit.Tools.Visual.Tests.Image;
using DemaConsulting.CanvasNet.Canvas;
using DemaConsulting.CanvasNet.Viewer;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Rendering;

/// <summary>
///     Subsystem tests for the Rendering subsystem: the access mapping, the renderer, the error
///     shaper and the destination working together the way a visual tool drives them.
/// </summary>
/// <remarks>
///     Each scenario passes the output of one unit to the next — the mapped policy into the
///     renderer, the renderer's failure into the shaper, the renderer's picture to the
///     destination — so the scenarios verify the hand-offs the unit tests cannot.
/// </remarks>
public class RenderingTests
{
    /// <summary>
    ///     Proves a document whose reference lies inside the grant is drawn, opaque, with the
    ///     reference resolved against its folder.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Rendering_Pipeline_ConfinedReference_IsDrawnOnOpaqueWhite()
    {
        // Arrange: a picture beside the document, inside the grant
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "ok.png", ImageTestImages.Png(40, 20));
        var policy = RenderSamples.Policy(fixture.Root);

        // Act: map the policy, draw, and decode the result
        var outcome = await new VisualRenderer(VisualOptions.Default).RenderAsync(
            RenderSamples.Request(RenderSamples.SvgWithImage("ok.png"), "figure.svg", policy, fixture.Root),
            TestContext.Current.CancellationToken);
        using var surface = RenderSamples.Decode(outcome.Png!);

        // Assert: drawn, with every pixel opaque
        Assert.True(outcome.Succeeded);
        Assert.Equal(255, surface.GetRowSpan(surface.Height - 1)[surface.Width - 1].A);
    }

    /// <summary>
    ///     Proves a broken source becomes text naming the code, the line and the column with a
    ///     caret.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Rendering_Pipeline_BrokenSource_BecomesActionableText()
    {
        // Arrange: a Mermaid source with a malformed arrow
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);

        // Act: draw, then shape the failure
        var outcome = await new VisualRenderer(VisualOptions.Default).RenderAsync(
            RenderSamples.Request(RenderSamples.MermaidBadArrow, "diagram.mmd", policy),
            TestContext.Current.CancellationToken);
        var text = RenderErrorShaper.Shape(
            outcome.Failure!,
            new ShapeContext(policy, null, RenderSamples.MermaidBadArrow, "diagram.mmd", null));

        // Assert: the code, the location and the offending line
        Assert.StartsWith("Render failed (mermaid-error/mermaid-syntax-error) at line 2, column 5:", text, StringComparison.Ordinal);
        Assert.Contains("  2 |   A -> B", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a reference outside the grant fails the render with text that discloses no host
    ///     path.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Rendering_Pipeline_ReferenceOutsideTheGrant_FailsWithoutHostPaths()
    {
        // Arrange: a picture outside the grant
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Outside, "secret.png", ImageTestImages.Png(40, 20));
        var policy = RenderSamples.Policy(fixture.Root);

        // Act: draw a document escaping to it, then shape the failure
        var outcome = await new VisualRenderer(VisualOptions.Default).RenderAsync(
            RenderSamples.Request(RenderSamples.SvgWithImage("../outside/secret.png"), "figure.svg", policy, fixture.Root),
            TestContext.Current.CancellationToken);
        var text = RenderErrorShaper.Shape(outcome.Failure!, new ShapeContext(policy, fixture.Root, null, "figure.svg", null));

        // Assert: a file-access failure stated without the host's layout
        Assert.StartsWith("Render failed (" + ErrorCodes.FileAccessDenied, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\', '/'), text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Proves a drawn picture can be saved through the destination and read back as the same
    ///     opaque PNG.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task Rendering_Pipeline_DrawnPicture_IsSavedToAPermittedDestination()
    {
        // Arrange: a writable workspace and a drawn diagram
        using var fixture = new TempDirectoryFixture();
        var policy = RenderSamples.Policy(fixture.Root);
        var outcome = await new VisualRenderer(VisualOptions.Default).RenderAsync(
            RenderSamples.Request(RenderSamples.PlantUml, "diagram.puml", policy),
            TestContext.Current.CancellationToken);

        // Act: resolve and write the destination
        Assert.True(RenderDestination.TryResolve(policy, "diagram.png", out var destination, out _));
        var written = await RenderDestination.TryWriteAsync(outcome.Png!, destination, TestContext.Current.CancellationToken);

        // Assert: the saved bytes are the picture, opaque white at the corner
        Assert.True(written);
        var saved = await System.IO.File.ReadAllBytesAsync(destination.RealPath, TestContext.Current.CancellationToken);
        Assert.Equal(outcome.Png, saved);
        using var surface = RenderSamples.Decode(saved);
        Assert.Equal(new Rgba32(255, 255, 255, 255), surface.GetRowSpan(0)[0]);
    }
}
