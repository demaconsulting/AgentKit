using System.Text;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Rendering;
using DemaConsulting.AgentKit.Tools.Visual.Tests.Image;
using DemaConsulting.CanvasNet.Canvas;
using DemaConsulting.CanvasNet.Files;
using DemaConsulting.CanvasNet.Viewer;

namespace DemaConsulting.AgentKit.Tools.Visual.Tests.Rendering;

/// <summary>
///     Unit tests for the <see cref="VisualRenderer"/> class.
/// </summary>
/// <remarks>
///     <para>
///     Every scenario draws through the real rendering library: the renderer's value is in how it
///     drives that library — the background it forces, the sizes it requests, the budgets it
///     passes on, the outcomes it promotes — and a substitute library would verify none of it.
///     </para>
///     <para>
///     The timeout and concurrency scenarios assert outcomes only, never durations. The render a
///     scenario must hold in progress is held deterministically by a file-access delegate that
///     blocks until the test releases it, rather than by a render assumed to be slow enough.
///     </para>
/// </remarks>
public class VisualRendererTests
{
    /// <summary>
    ///     Proves a picture is drawn on an opaque white background.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_Svg_IsDrawnOnOpaqueWhite()
    {
        // Arrange: an SVG whose corner is left blank
        using var fixture = new TempDirectoryFixture();
        var renderer = new VisualRenderer(VisualOptions.Default);

        // Act: draw it
        var outcome = await renderer.RenderAsync(
            RenderSamples.Request(RenderSamples.Svg, "figure.svg", RenderSamples.Policy(fixture.Root)),
            TestContext.Current.CancellationToken);

        // Assert: the blank corner is white and fully opaque
        using var surface = RenderSamples.Decode(outcome.Png!);
        Assert.Equal(new Rgba32(255, 255, 255, 255), surface.GetRowSpan(0)[0]);
    }

    /// <summary>
    ///     Proves the policy's pixel budget bounds the render.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_PictureOverThePixelBudget_FailsAsBudgetExceeded()
    {
        // Arrange: a policy whose pixel budget no preview fits in
        using var fixture = new TempDirectoryFixture();
        var policy = new PathPolicy(fixture.Root, [PathRule.ReadWrite(fixture.Root)], new ToolLimits(maxImagePixels: 1000));
        var renderer = new VisualRenderer(VisualOptions.Default);

        // Act: draw a diagram
        var outcome = await renderer.RenderAsync(
            RenderSamples.Request(RenderSamples.Mermaid, "diagram.mmd", policy),
            TestContext.Current.CancellationToken);

        // Assert: refused by the budget, not drawn
        Assert.Equal(ErrorCodes.BudgetExceeded, outcome.Failure?.Code);
    }

    /// <summary>
    ///     Proves a requested width beyond the largest side is reduced to it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_WidthBeyondTheLargestSide_IsReduced()
    {
        // Arrange: a renderer whose largest side is 300 pixels
        using var fixture = new TempDirectoryFixture();
        var renderer = new VisualRenderer(new VisualOptions { MaxRenderSide = 300, MinPreviewSide = 100 });
        var request = RenderSamples.Request(RenderSamples.Svg, "figure.svg", RenderSamples.Policy(fixture.Root)) with { Width = 5000 };

        // Act: ask for a far wider picture
        var outcome = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

        // Assert: drawn at the largest side
        Assert.Equal(300, outcome.Width);
        Assert.Equal(150, outcome.Height);
    }

    /// <summary>
    ///     Proves a picture whose natural size exceeds the largest side is drawn again at it.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_NaturalSizeBeyondTheLargestSide_IsDrawnAtTheLargestSide()
    {
        // Arrange: an SVG 1000 pixels wide and a largest side of 400
        using var fixture = new TempDirectoryFixture();
        var renderer = new VisualRenderer(new VisualOptions { MaxRenderSide = 400, MinPreviewSide = 100 });
        var wide = RenderSamples.Svg.Replace("width=\"200\" height=\"100\"", "width=\"1000\" height=\"500\"", StringComparison.Ordinal);

        // Act: draw it with no width requested
        var outcome = await renderer.RenderAsync(
            RenderSamples.Request(wide, "figure.svg", RenderSamples.Policy(fixture.Root)),
            TestContext.Current.CancellationToken);

        // Assert: the longer side is the largest side
        Assert.Equal(400, outcome.Width);
        Assert.Equal(200, outcome.Height);
    }

    /// <summary>
    ///     Proves a small vector picture is drawn again at the smallest preview side.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_SmallVectorPicture_IsDrawnAtTheSmallestPreviewSide()
    {
        // Arrange: an SVG 200 pixels wide, under the default smallest preview side of 768
        using var fixture = new TempDirectoryFixture();
        var renderer = new VisualRenderer(VisualOptions.Default);

        // Act: draw it with no width requested
        var outcome = await renderer.RenderAsync(
            RenderSamples.Request(RenderSamples.Svg, "figure.svg", RenderSamples.Policy(fixture.Root)),
            TestContext.Current.CancellationToken);

        // Assert: drawn again, larger, keeping its aspect ratio
        Assert.Equal(768, outcome.Width);
        Assert.Equal(384, outcome.Height);
    }

    /// <summary>
    ///     Proves a raster image is never enlarged, and is reduced to a smaller requested width.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_RasterImage_IsNeverEnlarged()
    {
        // Arrange: a small PNG, and the same PNG asked for at a smaller and a larger width
        using var fixture = new TempDirectoryFixture();
        var renderer = new VisualRenderer(VisualOptions.Default);
        var request = new RenderRequest
        {
            Content = ImageTestImages.Png(400, 200),
            FileName = "photo.png",
            Access = VisualAccess.ToFileAccess(RenderSamples.Policy(fixture.Root)),
            Limits = ToolLimits.Default
        };

        // Act: draw it three ways
        var natural = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);
        var smaller = await renderer.RenderAsync(request with { Width = 100 }, TestContext.Current.CancellationToken);
        var larger = await renderer.RenderAsync(request with { Width = 1600 }, TestContext.Current.CancellationToken);

        // Assert: its own size, reduced, and never enlarged
        Assert.Equal((400, 200), (natural.Width, natural.Height));
        Assert.Equal((100, 50), (smaller.Width, smaller.Height));
        Assert.Equal((400, 200), (larger.Width, larger.Height));
    }

    /// <summary>
    ///     Proves the configured triangle limit bounds an OpenSCAD model.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_ModelOverTheTriangleLimit_IsRefused()
    {
        // Arrange: a sphere, under the default limit and under a limit of 100 triangles
        using var fixture = new TempDirectoryFixture();
        var request = RenderSamples.Request(RenderSamples.ScadSphere, "model.scad", RenderSamples.Policy(fixture.Root));

        // Act: draw it under each
        var admitted = await new VisualRenderer(VisualOptions.Default).RenderAsync(request, TestContext.Current.CancellationToken);
        var refused = await new VisualRenderer(new VisualOptions { ScadMaxTriangles = 100 })
            .RenderAsync(request, TestContext.Current.CancellationToken);

        // Assert: the limit is what refused it
        Assert.True(admitted.Succeeded);
        Assert.Equal(ErrorCodes.ScadError, refused.Failure?.Code);
        Assert.Contains("limit 100", refused.Failure!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves a render that does not finish in time is reported as a timeout.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_RenderBeyondTheTimeout_FailsAsRenderTimeout()
    {
        // Arrange: a model far heavier than a 100 ms timeout allows
        using var fixture = new TempDirectoryFixture();
        var renderer = new VisualRenderer(new VisualOptions { RenderTimeout = TimeSpan.FromMilliseconds(100) });

        // Act: draw it
        var outcome = await renderer.RenderAsync(
            RenderSamples.Request(RenderSamples.HeavyScad(), "model.scad", RenderSamples.Policy(fixture.Root)),
            TestContext.Current.CancellationToken);

        // Assert: a timeout the model can act on
        Assert.Equal(RenderErrorShaper.RenderTimeoutCode, outcome.Failure?.Code);
    }

    /// <summary>
    ///     Proves a render that ignores its cancellation is abandoned after the grace period, and
    ///     keeps its place in the gate until it ends.
    /// </summary>
    /// <remarks>
    ///     The held render is started without being awaited, and the test waits until it has
    ///     actually reached the blocking check before asking for a second render, so the scenario
    ///     never depends on how quickly a render starts. Every wait the test itself makes is
    ///     bounded, so a regression fails the test rather than hanging the run.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_RenderIgnoringCancellation_IsAbandonedAndHoldsTheGate()
    {
        // Arrange: a render held inside the library by a file-access check that ignores the token
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Root, "ok.png", ImageTestImages.Png(20, 20));
        using var blocker = new Blocker();
        var renderer = new VisualRenderer(new VisualOptions { RenderTimeout = TimeSpan.FromSeconds(3) });
        var held = RenderSamples.Request(RenderSamples.SvgWithImage("ok.png"), "figure.svg", RenderSamples.Policy(fixture.Root), fixture.Root)
            with
        { Access = blocker.Policy };
        var light = RenderSamples.Request(RenderSamples.Svg, "figure.svg", RenderSamples.Policy(fixture.Root));

        // Act: hold the first render inside the library; a second render cannot get a place; the
        // held render is abandoned; after it is released and ends, a third render gets a place
        var heldTask = renderer.RenderAsync(held, TestContext.Current.CancellationToken);
        await blocker.WaitForEntryAsync().WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        var waitingOutcome = await renderer.RenderAsync(light, TestContext.Current.CancellationToken);
        var heldOutcome = await heldTask.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        var stillHeld = !blocker.Exited;
        blocker.Release();
        await blocker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        var afterOutcome = await RenderUntilAdmittedAsync(renderer, light);

        // Assert: the waiting render timed out at the gate, the held one was abandoned while still
        // inside the library, and the gate admitted a render once the held one ended
        Assert.Equal(RenderErrorShaper.RenderTimeoutCode, waitingOutcome.Failure?.Code);
        Assert.Equal(RenderErrorShaper.RenderTimeoutCode, heldOutcome.Failure?.Code);
        Assert.True(stillHeld);
        Assert.True(afterOutcome.Succeeded);
    }

    /// <summary>
    ///     Proves the caller's own cancellation is thrown rather than reported as a timeout.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_CallerCancels_ThrowsOperationCanceled()
    {
        // Arrange: a token already canceled
        using var fixture = new TempDirectoryFixture();
        var renderer = new VisualRenderer(VisualOptions.Default);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act / Assert: the cancellation is the caller's, so it is thrown
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.RenderAsync(
            RenderSamples.Request(RenderSamples.Svg, "figure.svg", RenderSamples.Policy(fixture.Root)),
            cancellation.Token));
    }

    /// <summary>
    ///     Proves a feature the library does not draw is a note on a successful render.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_UnsupportedFeature_IsReportedAsANote()
    {
        // Arrange: a model calling a module that does not exist, beside one that draws
        using var fixture = new TempDirectoryFixture();
        var renderer = new VisualRenderer(VisualOptions.Default);

        // Act: draw it
        var outcome = await renderer.RenderAsync(
            RenderSamples.Request("no_such_module();\ncube(10);\n", "model.scad", RenderSamples.Policy(fixture.Root)),
            TestContext.Current.CancellationToken);

        // Assert: drawn, with the library's note located at its line
        Assert.True(outcome.Succeeded);
        Assert.Contains(outcome.Notes, note => note.StartsWith("warning scad-warning", StringComparison.Ordinal)
            && note.Contains("line 1", StringComparison.Ordinal)
            && note.Contains("no_such_module", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves a picture whose embedded image was refused is promoted to a failure that keeps
    ///     every note.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_RefusedPicture_IsPromotedToAFailure()
    {
        // Arrange: an SVG embedding a picture outside the grant
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteBytes(fixture.Outside, "secret.png", ImageTestImages.Png(20, 20));
        var renderer = new VisualRenderer(VisualOptions.Default);

        // Act: draw it
        var outcome = await renderer.RenderAsync(
            RenderSamples.Request(RenderSamples.SvgWithImage("../outside/secret.png"), "figure.svg", RenderSamples.Policy(fixture.Root), fixture.Root),
            TestContext.Current.CancellationToken);

        // Assert: a file-access failure, carrying the note that reported the refusal
        Assert.Equal(ErrorCodes.FileAccessDenied, outcome.Failure?.Code);
        Assert.Equal(VisualRenderer.ImageAccessDeniedCode, outcome.Failure!.Detail);
        Assert.Contains(outcome.Notes, note => note.Contains(VisualRenderer.ImageAccessDeniedCode, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves an empty or almost empty vector picture is promoted to a failure, whatever the
    ///     width it is drawn at, and that the failure names a requested width.
    /// </summary>
    /// <param name="source">The source that draws nothing.</param>
    /// <param name="fileName">The file name it is drawn as.</param>
    /// <param name="width">The width requested, or null for none.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData(RenderSamples.SvgBlank, "figure.svg", null)]
    [InlineData(RenderSamples.SvgBlank, "figure.svg", 30)]
    [InlineData(RenderSamples.MermaidUnsupportedType, "diagram.mmd", null)]
    public async Task VisualRenderer_RenderAsync_EmptyPicture_IsPromotedToAFailure(string source, string fileName, int? width)
    {
        // Arrange: a renderer and a source that draws nothing
        using var fixture = new TempDirectoryFixture();
        var renderer = new VisualRenderer(VisualOptions.Default);
        var request = RenderSamples.Request(source, fileName, RenderSamples.Policy(fixture.Root)) with { Width = width };

        // Act: draw it
        var outcome = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

        // Assert: reported as empty rather than handed to a model to describe, naming any
        // requested width so the model can try another
        Assert.Equal(RenderErrorShaper.EmptyRenderCode, outcome.Failure?.Code);
        if (width is { } requested)
        {
            Assert.Contains($"requested width of {requested} pixels", outcome.Failure!.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("requested width", outcome.Failure!.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Proves a small picture with ink is drawn: emptiness is judged by ink alone, not by the
    ///     picture's natural size or the width requested.
    /// </summary>
    /// <param name="source">The source that draws something.</param>
    /// <param name="fileName">The file name it is drawn as.</param>
    /// <param name="width">The width requested, or null for none.</param>
    /// <param name="expectedWidth">The width the picture is expected to be drawn at.</param>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Theory]
    [InlineData(RenderSamples.SvgIcon, "icon.svg", null, 768)]
    [InlineData(RenderSamples.SvgTwoCircles, "figure.svg", 30, 30)]
    [InlineData(RenderSamples.MermaidTwoNodes, "diagram.mmd", 32, 32)]
    public async Task VisualRenderer_RenderAsync_SmallPictureWithInk_IsDrawn(string source, string fileName, int? width, int expectedWidth)
    {
        // Arrange: a renderer and a small source that draws something
        using var fixture = new TempDirectoryFixture();
        var renderer = new VisualRenderer(VisualOptions.Default);
        var request = RenderSamples.Request(source, fileName, RenderSamples.Policy(fixture.Root)) with { Width = width };

        // Act: draw it
        var outcome = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

        // Assert: drawn at the expected width rather than reported as empty
        Assert.True(outcome.Succeeded, outcome.Failure?.Message);
        Assert.Equal(expectedWidth, outcome.Width);
    }

    /// <summary>
    ///     Proves a blank raster image is not promoted, because it is the file's true content.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_BlankRasterImage_IsDrawn()
    {
        // Arrange: a uniformly white PNG
        using var fixture = new TempDirectoryFixture();
        var renderer = new VisualRenderer(VisualOptions.Default);
        var request = new RenderRequest
        {
            Content = ImageTestImages.UniformPng(50, 50, new Rgba32(255, 255, 255, 255)),
            FileName = "blank.png",
            Access = VisualAccess.ToFileAccess(RenderSamples.Policy(fixture.Root)),
            Limits = ToolLimits.Default
        };

        // Act: draw it
        var outcome = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

        // Assert: drawn as it is
        Assert.True(outcome.Succeeded);
    }

    /// <summary>
    ///     Proves the requested page of a document is drawn and the page count reported, and a
    ///     page the document does not have is a failure.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_DocumentPage_IsDrawnWithThePageCount()
    {
        // Arrange: a two-page PDF
        using var fixture = new TempDirectoryFixture();
        var renderer = new VisualRenderer(VisualOptions.Default);
        var request = new RenderRequest
        {
            Content = RenderSamples.Pdf(2),
            FileName = "report.pdf",
            Access = VisualAccess.ToFileAccess(RenderSamples.Policy(fixture.Root)),
            Limits = ToolLimits.Default
        };

        // Act: draw its second page, and a third it does not have
        var second = await renderer.RenderAsync(request with { Page = 2 }, TestContext.Current.CancellationToken);
        var third = await renderer.RenderAsync(request with { Page = 3 }, TestContext.Current.CancellationToken);

        // Assert: the page drawn and the count reported; the missing page refused
        Assert.Equal((2, 2), (second.Page, second.PageCount));
        Assert.Equal(ErrorCodes.PageOutOfRange, third.Failure?.Code);
    }

    /// <summary>
    ///     Proves a description lists the picture's features and dominant colors when asked for,
    ///     and nothing when not.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_RenderAsync_DescriptionRequested_ListsFeaturesAndColors()
    {
        // Arrange: a flowchart, drawn with and without a description
        using var fixture = new TempDirectoryFixture();
        var renderer = new VisualRenderer(VisualOptions.Default);
        var request = RenderSamples.Request(RenderSamples.Mermaid, "diagram.mmd", RenderSamples.Policy(fixture.Root));

        // Act: draw it both ways
        var described = await renderer.RenderAsync(request with { Describe = true }, TestContext.Current.CancellationToken);
        var plain = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

        // Assert: the labels the diagram drew, and its colors, only when asked for
        Assert.Contains(described.Features, feature => feature.Contains("Publish", StringComparison.Ordinal));
        Assert.NotEmpty(described.DominantColors);
        Assert.All(described.DominantColors, color => Assert.Matches("^#[0-9a-f]{6} [0-9]+%$", color));
        Assert.Empty(plain.Features);
        Assert.Empty(plain.DominantColors);
    }

    /// <summary>
    ///     Proves warming up draws every known format and ignores an unknown one.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task VisualRenderer_WarmUpAsync_KnownAndUnknownFormats_Completes()
    {
        // Arrange: a renderer, and every warm-up format plus one it does not know
        var renderer = new VisualRenderer(VisualOptions.Default);
        var formats = VisualRenderer.WarmUpFormats.Append("no-such-format").ToList();

        // Act: warm up
        var warmUp = renderer.WarmUpAsync(formats, TestContext.Current.CancellationToken);
        await warmUp;

        // Assert: completed, over the five formats the renderer knows
        Assert.True(warmUp.IsCompletedSuccessfully);
        Assert.Equal(5, VisualRenderer.WarmUpFormats.Count);
    }

    /// <summary>
    ///     Draws a render repeatedly until the gate admits it, for the short moment between an
    ///     abandoned render ending and its place being released.
    /// </summary>
    /// <param name="renderer">The renderer.</param>
    /// <param name="request">The render.</param>
    /// <returns>The first outcome that is not a timeout.</returns>
    private static async Task<RenderOutcome> RenderUntilAdmittedAsync(VisualRenderer renderer, RenderRequest request)
    {
        RenderOutcome outcome;
        var attempts = 0;
        do
        {
            outcome = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);
        }
        while (outcome.Failure?.Code == RenderErrorShaper.RenderTimeoutCode && ++attempts < 20);

        return outcome;
    }

    /// <summary>
    ///     A file-access policy that holds the render inside the library until released, ignoring
    ///     the render's cancellation, so a test can hold a render in progress deterministically.
    /// </summary>
    private sealed class Blocker : IDisposable
    {
        /// <summary>
        ///     Released by the test.
        /// </summary>
        private readonly ManualResetEventSlim _release = new(false);

        /// <summary>
        ///     Set when a render reaches the held check.
        /// </summary>
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        ///     Set when the held check returns.
        /// </summary>
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        ///     Initializes a new instance of the <see cref="Blocker"/> class.
        /// </summary>
        public Blocker()
        {
            Policy = FileAccessPolicy.FromDelegate("blocker", _ =>
            {
                _entered.TrySetResult();
                _release.Wait();
                _exited.TrySetResult();
                return FileAccessDecision.Allow;
            });
        }

        /// <summary>
        ///     Gets the policy that holds the render.
        /// </summary>
        public FileAccessPolicy Policy { get; }

        /// <summary>
        ///     Gets a value indicating whether the held check has returned.
        /// </summary>
        public bool Exited => _exited.Task.IsCompleted;

        /// <summary>
        ///     Lets the held render continue.
        /// </summary>
        public void Release()
        {
            _release.Set();
        }

        /// <summary>
        ///     Waits until a render has reached the held check.
        /// </summary>
        /// <returns>A task that completes when a render entered the check.</returns>
        public Task WaitForEntryAsync()
        {
            return _entered.Task;
        }

        /// <summary>
        ///     Waits until the held check has returned.
        /// </summary>
        /// <returns>A task that completes when the check returned.</returns>
        public Task WaitForExitAsync()
        {
            return _exited.Task;
        }

        /// <summary>
        ///     Releases the held render and the event.
        /// </summary>
        public void Dispose()
        {
            _release.Set();
            _release.Dispose();
        }
    }
}
