using System.Diagnostics;
using System.Globalization;
using System.Text;
using DemaConsulting.CanvasNet.Canvas;
using DemaConsulting.CanvasNet.Codecs;
using DemaConsulting.CanvasNet.Files;
using DemaConsulting.CanvasNet.ImageSources;
using DemaConsulting.CanvasNet.Positions;
using DemaConsulting.CanvasNet.Viewer;
using DemaConsulting.CanvasNet.Viewer.Imaging;

namespace DemaConsulting.AgentKit.Tools.Visual.Rendering;

/// <summary>
///     Draws a document or diagram to an opaque PNG for a model, within the pack's time,
///     concurrency and size bounds, and reports everything the render said about the picture.
/// </summary>
/// <remarks>
///     <para>
///     <b>One renderer per pack instance.</b> It owns the pack's render gate — a semaphore of
///     <see cref="VisualOptions.MaxConcurrentRenders"/> places — and is otherwise immutable, so
///     it is safe for concurrent use from any number of threads. Every composition the pack is
///     added to shares the gate by design.
///     </para>
///     <para>
///     <b>Opaque white, always.</b> The library's default background is transparent, and a vision
///     model shown dark text on a transparent PNG sees it on black and cannot read it
///     (<see href="https://github.com/demaconsulting/CanvasNet/issues/168">CanvasNet #168</see>).
///     Every render therefore flattens onto white, and a picture that still carries transparency
///     is flattened again before it is encoded.
///     </para>
///     <para>
///     <b>Lenient, with every note reported.</b> A feature the library does not draw is a note,
///     not a failure, because the notes of a lenient render carry more for a model to act on than
///     the error of a strict one. Two outcomes are promoted to failures anyway, because a model
///     shown the picture would be misled: a picture whose embedded image the file-access policy
///     refused (<c>file-access-denied</c>), and a picture that is empty or almost empty
///     (<c>empty-render</c>) — less than <see cref="NearEmptyInkFraction"/> of its pixels different
///     from its top-left corner, whatever its size. Emptiness is judged by ink alone, because a
///     small icon or a small requested width is not empty. A raster image is never promoted for
///     being empty: a blank photograph is the true content of its file.
///     </para>
///     <para>
///     <b>Sizing.</b> A width the caller asks for is reduced to
///     <see cref="VisualOptions.MaxRenderSide"/>. Without one, a vector picture whose longer side
///     is below <see cref="VisualOptions.MinPreviewSide"/> is drawn again at that size, because
///     small labels are read poorly; a picture whose longer side exceeds the largest side is drawn
///     again at the largest side. A vector picture is drawn again rather than resampled, so it is
///     sharp at every size; a raster image is never enlarged.
///     </para>
///     <para>
///     <b>Timeout, as defense in depth.</b> The render runs on the thread pool under a token that
///     is canceled at <see cref="VisualOptions.RenderTimeout"/>, which the library honors. The
///     caller additionally stops waiting a short grace period later and reports
///     <c>render-timeout</c> even if the render has not yet noticed the token; the abandoned render
///     keeps its place in the gate until it ends, so abandoned renders cannot pile up. Time spent
///     waiting for the gate counts against the timeout.
///     </para>
/// </remarks>
internal sealed class VisualRenderer
{
    /// <summary>
    ///     The share of pixels that must differ from the background for a picture not to be
    ///     considered almost empty.
    /// </summary>
    internal const double NearEmptyInkFraction = 0.0005;

    /// <summary>
    ///     The most features a description lists.
    /// </summary>
    internal const int MaxFeatures = 50;

    /// <summary>
    ///     The most dominant colors a description lists.
    /// </summary>
    internal const int MaxDominantColors = 4;

    /// <summary>
    ///     The largest content the library accepts as a stream, in bytes.
    /// </summary>
    internal const int MaxContentBytes = 16 * 1024 * 1024;

    /// <summary>
    ///     The diagnostic code the library gives a picture its file-access policy refused.
    /// </summary>
    internal const string ImageAccessDeniedCode = "image-access-denied";

    /// <summary>
    ///     How long the caller keeps waiting after the timeout before it abandons a render.
    /// </summary>
    internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     The background every picture is drawn on.
    /// </summary>
    internal static readonly Rgba32 White = new(255, 255, 255, 255);

    /// <summary>
    ///     The extensions of raster formats, which are never enlarged nor promoted for being empty.
    /// </summary>
    private static readonly string[] RasterExtensions =
        [".bmp", ".gif", ".j2c", ".j2k", ".jp2", ".jpeg", ".jpg", ".png", ".tif", ".tiff"];

    /// <summary>
    ///     A small source per inline format, drawn by <see cref="WarmUpAsync"/>.
    /// </summary>
    private static readonly Dictionary<string, (string FileName, string Source)> WarmUpSamples =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["mermaid"] = ("warmup.mmd", "flowchart LR\n  a[Start] --> b[End]\n"),
            ["plantuml"] = ("warmup.puml", "@startuml\nAlice -> Bob : hello\n@enduml\n"),
            ["svg"] = ("warmup.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"40\" height=\"40\"><rect x=\"4\" y=\"4\" width=\"32\" height=\"32\" fill=\"#336699\"/></svg>"),
            ["vega-lite"] = ("warmup.vl.json", "{\"data\":{\"values\":[{\"a\":\"x\",\"b\":1}]},\"mark\":\"bar\",\"encoding\":{\"x\":{\"field\":\"a\",\"type\":\"nominal\"},\"y\":{\"field\":\"b\",\"type\":\"quantitative\"}}}"),
            ["openscad"] = ("warmup.scad", "cube([4, 4, 4]);\n")
        };

    /// <summary>
    ///     The pack's render gate.
    /// </summary>
    private readonly SemaphoreSlim _gate;

    /// <summary>
    ///     Initializes a new instance of the <see cref="VisualRenderer"/> class.
    /// </summary>
    /// <param name="options">The pack's rendering controls. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    internal VisualRenderer(VisualOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Options = options;
        _gate = new SemaphoreSlim(options.MaxConcurrentRenders, options.MaxConcurrentRenders);
    }

    /// <summary>
    ///     Gets the formats <see cref="WarmUpAsync"/> can draw a sample of.
    /// </summary>
    internal static IReadOnlyCollection<string> WarmUpFormats => WarmUpSamples.Keys;

    /// <summary>
    ///     Gets the rendering controls this renderer applies.
    /// </summary>
    internal VisualOptions Options { get; }

    /// <summary>
    ///     Determines whether a file name names a raster format.
    /// </summary>
    /// <param name="fileName">The file name. Must not be null.</param>
    /// <returns>True for a raster format.</returns>
    internal static bool IsRaster(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return RasterExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Draws one document or diagram.
    /// </summary>
    /// <remarks>
    ///     Never throws for anything the content or the library did wrong: every failure, a
    ///     timeout included, is a <see cref="RenderOutcome"/> carrying a classified failure. Only
    ///     the caller's own cancellation is thrown.
    /// </remarks>
    /// <param name="request">What to draw. Must not be null.</param>
    /// <param name="cancellationToken">The caller's token; canceling it abandons the render.</param>
    /// <returns>The picture, or the classified failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="cancellationToken"/> is canceled.
    /// </exception>
    internal async Task<RenderOutcome> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timeout = Options.RenderTimeout;
        var clock = Stopwatch.StartNew();

        // Waiting for a place counts against the timeout; a render that cannot start in time has
        // timed out like one that cannot finish in time.
        if (!await _gate.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
        {
            return RenderOutcome.Failed(RenderErrorShaper.Timeout(timeout));
        }

        var remaining = timeout - clock.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            _gate.Release();
            return RenderOutcome.Failed(RenderErrorShaper.Timeout(timeout));
        }

        // The token the library honors. It is disposed, and the place in the gate released, only
        // when the render itself ends, however long after the caller stopped waiting.
        var renderCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        renderCancellation.CancelAfter(remaining);
        var work = Task.Run(() => Render(request, renderCancellation.Token), CancellationToken.None);
        _ = work.ContinueWith(
            finished =>
            {
                // Observed so that an abandoned render's exception is never unobserved.
                _ = finished.Exception;
                renderCancellation.Dispose();
                _gate.Release();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            return await work.WaitAsync(remaining + Grace, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The render has not ended by the end of the grace period; it is abandoned, not awaited.
            return RenderOutcome.Failed(RenderErrorShaper.Timeout(timeout));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout's own cancellation, not the caller's.
            return RenderOutcome.Failed(RenderErrorShaper.Timeout(timeout));
        }
    }

    /// <summary>
    ///     Draws a small sample of each named format and discards the pictures, so that the
    ///     library's first-use cost is paid before a model is waiting.
    /// </summary>
    /// <remarks>
    ///     The samples go through the gate and the timeout like any render, so warming up never
    ///     exceeds the pack's concurrency bound. They reference no file, and the file-access
    ///     policy they are drawn under refuses every file anyway. A sample that fails is ignored:
    ///     warming up is an optimization, and a failure will be reported by the first real render
    ///     that meets it.
    /// </remarks>
    /// <param name="formats">The formats to warm up; unknown names are ignored. Must not be null.</param>
    /// <param name="cancellationToken">A token that stops the warm-up.</param>
    /// <returns>A task that completes when every sample was drawn.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="formats"/> is null.</exception>
    internal async Task WarmUpAsync(IEnumerable<string> formats, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(formats);

        var refuseAll = FileAccessPolicy.Combine(
            FileAccessPolicy.NoSymlinks,
            FileAccessPolicy.FromDelegate(
                VisualAccess.DelegateName,
                _ => FileAccessDecision.Deny(VisualAccess.OutsideReason, VisualAccess.OutsideHint)));

        foreach (var format in formats.Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (!WarmUpSamples.TryGetValue(format, out var sample))
            {
                continue;
            }

            _ = await RenderAsync(
                    new RenderRequest
                    {
                        Content = Encoding.UTF8.GetBytes(sample.Source),
                        FileName = sample.FileName,
                        Access = refuseAll,
                        Limits = DemaConsulting.AgentKit.Core.ToolLimits.Default
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Draws the content on the calling thread, sizing, promoting and encoding the result.
    /// </summary>
    /// <param name="request">What to draw.</param>
    /// <param name="cancellationToken">The token that ends the render at the timeout.</param>
    /// <returns>The picture, or the classified failure.</returns>
    private RenderOutcome Render(RenderRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var raster = IsRaster(request.FileName);
            var width = request.Width is { } requested ? Math.Clamp(requested, 1, Options.MaxRenderSide) : (int?)null;

            // A vector picture is drawn at the requested width; a raster first at its own size,
            // so that it is never enlarged.
            using var first = Draw(request, raster ? null : width, null, cancellationToken);
            var target = TargetSize(first.Surface.Width, first.Surface.Height, raster, width);

            if (target is null)
            {
                return Finish(first, request, raster);
            }

            using var second = Draw(request, target.Value.Width, target.Value.Height, cancellationToken);
            return Finish(second, request, raster);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The content is model-authored and the library a prerelease: a defect it raises on
            // hostile input must become a failure the model is told about, not the end of the
            // agent's turn.
            return RenderOutcome.Failed(RenderErrorShaper.Classify(exception));
        }
    }

    /// <summary>
    ///     Decides whether a first picture must be drawn again at another size.
    /// </summary>
    /// <param name="width">The first picture's width.</param>
    /// <param name="height">The first picture's height.</param>
    /// <param name="raster">Whether the content is a raster image.</param>
    /// <param name="requested">The clamped width the caller asked for, or null.</param>
    /// <returns>The width or height to draw again at, or null to keep the first picture.</returns>
    private (int? Width, int? Height)? TargetSize(int width, int height, bool raster, int? requested)
    {
        var longSide = Math.Max(width, height);

        // A raster narrower than a requested width is reduced to it, never enlarged.
        if (raster && requested is { } wanted && wanted < width)
        {
            return FitLongSide(width, height, Math.Max(1, (int)Math.Round((double)wanted * longSide / width)));
        }

        // Too large on its longer side, whatever was requested.
        if (longSide > Options.MaxRenderSide)
        {
            return FitLongSide(width, height, Options.MaxRenderSide);
        }

        // A vector picture too small to read is drawn larger, unless a width was requested.
        if (!raster && requested is null && longSide < Options.MinPreviewSide)
        {
            return FitLongSide(width, height, Options.MinPreviewSide);
        }

        return null;
    }

    /// <summary>
    ///     Expresses a longer-side size as the width or height to request.
    /// </summary>
    /// <param name="width">The current width.</param>
    /// <param name="height">The current height.</param>
    /// <param name="longSide">The longer side wanted.</param>
    /// <returns>The width when the picture is wider than tall, otherwise the height.</returns>
    private static (int? Width, int? Height) FitLongSide(int width, int height, int longSide)
    {
        return width >= height ? (longSide, null) : (null, longSide);
    }

    /// <summary>
    ///     Calls the library once.
    /// </summary>
    /// <param name="request">What to draw.</param>
    /// <param name="width">The width to draw at, or null.</param>
    /// <param name="height">The height to draw at, or null.</param>
    /// <param name="cancellationToken">The token that ends the render.</param>
    /// <returns>The library's picture; the caller disposes it.</returns>
    private ViewerImage Draw(RenderRequest request, int? width, int? height, CancellationToken cancellationToken)
    {
        var options = new ViewerRenderOptions
        {
            Width = width,
            Height = height,
            Page = request.Page,
            Background = White,
            ThrowOnUnsupportedFeature = false,
            MaxDecodedPixels = request.Limits.MaxImagePixels,
            Scad = new ScadViewOptions { MaxTriangles = Options.ScadMaxTriangles },
            Positions = request.Describe ? PositionSelection.All : null,
            Folder = request.Folder,
            FileAccess = request.Access,
            CancellationToken = cancellationToken
        };

        using var content = new MemoryStream(request.Content, writable: false);
        return CanvasNetViewer.Render(content, request.FileName, options);
    }

    /// <summary>
    ///     Promotes a misleading picture to a failure, or encodes it and collects its report.
    /// </summary>
    /// <param name="image">The final picture.</param>
    /// <param name="request">What was drawn.</param>
    /// <param name="raster">Whether the content is a raster image.</param>
    /// <returns>The outcome.</returns>
    private static RenderOutcome Finish(ViewerImage image, RenderRequest request, bool raster)
    {
        var notes = image.Diagnostics.Select(FormatNote).ToList();

        // A picture whose embedded image was refused would show the model a hole it cannot see
        // the reason for.
        var denied = image.Diagnostics.FirstOrDefault(note => note.Code == ImageAccessDeniedCode);
        if (denied is not null)
        {
            var message = RenderErrorShaper.CleanMessage(denied.Message, out _, out _, out _, out _, out _);
            return RenderOutcome.Failed(new RenderFailure(
                ErrorCodes.FileAccessDenied,
                ImageAccessDeniedCode,
                message,
                null,
                denied.Location?.File,
                denied.Location?.Line,
                denied.Location?.Column,
                denied.Location?.Pointer,
                notes));
        }

        // An empty picture would be described by a model as whatever it expected to see.
        var surface = image.Surface;
        if (!raster)
        {
            var ink = InkFraction(surface);
            if (ink < NearEmptyInkFraction)
            {
                // Emptiness is judged by ink alone: a small icon or a small requested width is
                // not empty. At a width of a pixel or two even a real picture shows nothing, so
                // the model is told the width it asked for and that another may show the picture.
                var widthAdvice = request.Width is { } requestedWidth
                    ? " at the requested width of " + requestedWidth.ToString(CultureInfo.InvariantCulture)
                      + " pixels; a larger width, or none, may show it"
                    : string.Empty;
                return RenderOutcome.Failed(new RenderFailure(
                    RenderErrorShaper.EmptyRenderCode,
                    null,
                    "The picture is empty or almost empty ("
                    + surface.Width.ToString(CultureInfo.InvariantCulture) + "x"
                    + surface.Height.ToString(CultureInfo.InvariantCulture) + " pixels, "
                    + (ink * 100).ToString("0.###", CultureInfo.InvariantCulture) + "% drawn)"
                    + widthAdvice + ".",
                    null,
                    null,
                    null,
                    null,
                    null,
                    notes));
            }
        }

        var png = Encode(surface);
        var features = request.Describe ? image.Positions.Select(FormatFeature).ToList() : [];
        var truncated = image.PositionsTruncated || features.Count > MaxFeatures;
        var colors = request.Describe
            ? surface.Analyze(White, MaxDominantColors).Colors.Take(MaxDominantColors).Select(FormatColor).ToList()
            : [];

        return RenderOutcome.Success(
            png,
            surface.Width,
            surface.Height,
            image.Format,
            image.Page,
            image.PageCount,
            notes,
            features.Take(MaxFeatures),
            truncated,
            colors);
    }

    /// <summary>
    ///     Encodes a picture as an opaque PNG.
    /// </summary>
    /// <param name="surface">The picture.</param>
    /// <returns>The PNG bytes.</returns>
    private static byte[] Encode(Surface surface)
    {
        using var output = new MemoryStream();
        if (surface.HasTransparency())
        {
            // The library flattened onto the background already; this guards a source that did
            // not, so no transparent pixel ever reaches a model.
            using var flat = surface.FlattenOver(White);
            PngCodec.Save(flat, output, PngColorType.Rgb);
        }
        else
        {
            PngCodec.Save(surface, output, PngColorType.Rgb);
        }

        return output.ToArray();
    }

    /// <summary>
    ///     Measures the share of pixels that differ from the picture's top-left corner.
    /// </summary>
    /// <param name="surface">The picture.</param>
    /// <returns>The share, from 0 to 1.</returns>
    private static double InkFraction(Surface surface)
    {
        var total = (long)surface.Width * surface.Height;
        if (total == 0)
        {
            return 0;
        }

        var background = surface.GetRowSpan(0)[0];
        long drawn = 0;
        for (var y = 0; y < surface.Height; y++)
        {
            foreach (var pixel in surface.GetRowSpan(y))
            {
                if (!pixel.Equals(background))
                {
                    drawn++;
                }
            }
        }

        return (double)drawn / total;
    }

    /// <summary>
    ///     Formats one note of the render for a model.
    /// </summary>
    /// <param name="note">The note.</param>
    /// <returns>The formatted note.</returns>
    internal static string FormatNote(ImageSourceDiagnostic note)
    {
        var message = RenderErrorShaper.CleanMessage(note.Message, out _, out var line, out var column, out _, out var detail);
        line = note.Location?.Line ?? line;
        column = note.Location?.Column ?? column;

        // A finer code is reported beside the note's code. The library also uses the detail
        // field for a key-value record of a refused file - its requested and real paths - which
        // is host detail and is never shown.
        if (note.Detail is { } finer && RenderErrorShaper.IsCode(finer))
        {
            detail = finer;
        }

        var text = new StringBuilder();
        text.Append(note.Severity.ToString().ToLowerInvariant()).Append(' ').Append(note.Code);
        if (detail is not null && !string.Equals(detail, note.Code, StringComparison.Ordinal))
        {
            text.Append('/').Append(detail);
        }

        if (line is { } lineNumber)
        {
            text.Append(" at line ").Append(lineNumber.ToString(CultureInfo.InvariantCulture));
            if (column is { } columnNumber)
            {
                text.Append(", column ").Append(columnNumber.ToString(CultureInfo.InvariantCulture));
            }
        }

        return text.Append(": ").Append(RenderErrorShaper.Rewrite(message)).ToString();
    }

    /// <summary>
    ///     Formats one feature of a diagram or chart for a model.
    /// </summary>
    /// <param name="position">The feature.</param>
    /// <returns>The formatted feature: kind, id, text and the edge it joins.</returns>
    private static string FormatFeature(ImagePosition position)
    {
        var text = new StringBuilder();
        text.Append(position.Kind).Append(' ').Append(position.Id);
        if (!string.IsNullOrEmpty(position.Text))
        {
            text.Append(" \"").Append(position.Text).Append('"');
        }

        if (!string.IsNullOrEmpty(position.From) || !string.IsNullOrEmpty(position.To))
        {
            text.Append(' ').Append(position.From).Append("->").Append(position.To);
        }

        return text.ToString();
    }

    /// <summary>
    ///     Formats one dominant color for a model.
    /// </summary>
    /// <param name="color">The color and its share.</param>
    /// <returns>The color as <c>#rrggbb nn%</c>.</returns>
    private static string FormatColor(DominantColor color)
    {
        return "#"
            + color.Color.R.ToString("x2", CultureInfo.InvariantCulture)
            + color.Color.G.ToString("x2", CultureInfo.InvariantCulture)
            + color.Color.B.ToString("x2", CultureInfo.InvariantCulture)
            + " "
            + Math.Round(color.Fraction * 100).ToString(CultureInfo.InvariantCulture)
            + "%";
    }
}
