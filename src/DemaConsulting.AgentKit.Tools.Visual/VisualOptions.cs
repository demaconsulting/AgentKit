using System.Globalization;

namespace DemaConsulting.AgentKit.Tools.Visual;

/// <summary>
///     The rendering controls a visual tool pack carries in addition to the limits of the access
///     policy: how long a render may take, how large a picture may be, how small a vector picture
///     may be when it is shown to a model, how many triangles a 3D model may produce, and how many
///     renders may run at once.
/// </summary>
/// <remarks>
///     <para>
///     <b>Every value is checked when it is set.</b> Each <c>init</c> accessor refuses a value
///     outside its documented range with an <see cref="ArgumentOutOfRangeException"/>, so an
///     instance that exists is an instance whose every value is applied as stated. No value is
///     silently clamped or ignored. The one relation between two values — that
///     <see cref="MinPreviewSide"/> may not exceed <see cref="MaxRenderSide"/> — cannot be checked
///     by either accessor alone, because an object initializer sets them one at a time; the pack
///     that receives the options checks it instead.
///     </para>
///     <para>
///     <b>What these options do not carry.</b> The policy's <c>ToolLimits</c> already bound the
///     rest: the pixel budget of a render is <c>ToolLimits.MaxImagePixels</c>, the largest input
///     file and the largest returned picture are <c>ToolLimits.MaxBinaryBytes</c>, and the longest
///     inline source is <c>ToolLimits.MaxReadBytes</c>. Repeating any of them here would give a
///     host two settings for one limit.
///     </para>
///     <para>
///     The record is immutable once built and is therefore safe to share between packs and
///     threads.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     A pack whose renders are given less time and a smaller largest side than the defaults.
///     </para>
///     <code>
///     var options = new VisualOptions
///     {
///         RenderTimeout = TimeSpan.FromSeconds(10),
///         MaxRenderSide = 1600
///     };
///
///     var pack = new DemaConsulting.AgentKit.Tools.Visual.Image.ImagePack(options);
///     </code>
/// </example>
public sealed record VisualOptions
{
    /// <summary>
    ///     The largest side, in pixels, the rendering library can produce.
    /// </summary>
    private const int RenderSideLimit = 8192;

    /// <summary>
    ///     The smallest permitted <see cref="MaxRenderSide"/>.
    /// </summary>
    private const int MinimumMaxRenderSide = 16;

    /// <summary>
    ///     The longest permitted <see cref="RenderTimeout"/>.
    /// </summary>
    private static readonly TimeSpan MaximumRenderTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     Backing store for <see cref="RenderTimeout"/>.
    /// </summary>
    private readonly TimeSpan _renderTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Backing store for <see cref="MaxRenderSide"/>.
    /// </summary>
    private readonly int _maxRenderSide = 2048;

    /// <summary>
    ///     Backing store for <see cref="MinPreviewSide"/>.
    /// </summary>
    private readonly int _minPreviewSide = 768;

    /// <summary>
    ///     Backing store for <see cref="ScadMaxTriangles"/>.
    /// </summary>
    private readonly long _scadMaxTriangles = 500_000;

    /// <summary>
    ///     Backing store for <see cref="MaxConcurrentRenders"/>.
    /// </summary>
    private readonly int _maxConcurrentRenders = 1;

    /// <summary>
    ///     Gets the options with every value at its default.
    /// </summary>
    public static VisualOptions Default { get; } = new();

    /// <summary>
    ///     Gets the longest time one render may take, including the time it waits for its turn.
    ///     The default is 30 seconds.
    /// </summary>
    /// <remarks>
    ///     A render that does not finish in time is reported to the model as a
    ///     <c>render-timeout</c> failure it can act on by simplifying the source. The value must be
    ///     greater than zero and at most 10 minutes.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when the value is zero or negative, or longer than 10 minutes.
    /// </exception>
    public TimeSpan RenderTimeout
    {
        get => _renderTimeout;
        init
        {
            // A zero or negative timeout would fail every render; an unbounded one would let a
            // single render hold the pack's render gate indefinitely.
            if (value <= TimeSpan.Zero || value > MaximumRenderTimeout)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(RenderTimeout),
                    value,
                    "The render timeout must be greater than zero and at most "
                    + MaximumRenderTimeout.TotalMinutes.ToString(CultureInfo.InvariantCulture)
                    + " minutes.");
            }

            _renderTimeout = value;
        }
    }

    /// <summary>
    ///     Gets the largest width or height, in pixels, of a rendered picture. The default is
    ///     2048.
    /// </summary>
    /// <remarks>
    ///     A picture whose natural size is larger is drawn again at this size on its longer side,
    ///     and a width a model asks for is reduced to it. The value must be from 16 to 8192, the
    ///     largest side the rendering library can produce.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when the value is below 16 or above 8192.
    /// </exception>
    public int MaxRenderSide
    {
        get => _maxRenderSide;
        init
        {
            // The library refuses any side above its own maximum before it reads a byte, so a
            // larger value could never be honored.
            if (value < MinimumMaxRenderSide || value > RenderSideLimit)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxRenderSide),
                    value,
                    "The largest render side must be from "
                    + MinimumMaxRenderSide.ToString(CultureInfo.InvariantCulture)
                    + " to "
                    + RenderSideLimit.ToString(CultureInfo.InvariantCulture)
                    + " pixels.");
            }

            _maxRenderSide = value;
        }
    }

    /// <summary>
    ///     Gets the smallest longer side, in pixels, a vector picture is drawn at when no width is
    ///     requested. The default is 768.
    /// </summary>
    /// <remarks>
    ///     A diagram, a drawing or a document page whose natural size is smaller is drawn again at
    ///     this size, because small labels are read poorly by vision models. A raster image is
    ///     never enlarged. The value must be from 1 to 8192 and may not exceed
    ///     <see cref="MaxRenderSide"/>; the pack that receives the options checks that relation.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when the value is below 1 or above 8192.
    /// </exception>
    public int MinPreviewSide
    {
        get => _minPreviewSide;
        init
        {
            // The relation to MaxRenderSide is checked by the pack, because an initializer sets
            // the two values one at a time.
            if (value < 1 || value > RenderSideLimit)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MinPreviewSide),
                    value,
                    "The smallest preview side must be from 1 to "
                    + RenderSideLimit.ToString(CultureInfo.InvariantCulture)
                    + " pixels.");
            }

            _minPreviewSide = value;
        }
    }

    /// <summary>
    ///     Gets the most triangles an OpenSCAD model may produce before its render is refused. The
    ///     default is 500,000.
    /// </summary>
    /// <remarks>
    ///     The triangle count is what drives the memory a 3D model needs: a model of a few lines
    ///     can produce millions of triangles and hundreds of megabytes of geometry. The library's
    ///     own default of two million is lowered here because a model-authored part rarely needs
    ///     more than a fraction of it. The value must be at least 1.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when the value is below 1.
    /// </exception>
    public long ScadMaxTriangles
    {
        get => _scadMaxTriangles;
        init
        {
            // The library refuses a limit below one, so the refusal is made here, at the line
            // that set it, rather than inside the first render.
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ScadMaxTriangles),
                    value,
                    "The triangle limit must be at least 1.");
            }

            _scadMaxTriangles = value;
        }
    }

    /// <summary>
    ///     Gets the number of renders one pack instance runs at the same time. The default is 1.
    /// </summary>
    /// <remarks>
    ///     A further render waits for its turn, and the wait counts against
    ///     <see cref="RenderTimeout"/>. The bound applies per pack instance and is shared by every
    ///     composition that instance is added to. It exists because a render that outlives its
    ///     timeout is abandoned rather than stopped by force, and keeps its place until it
    ///     notices the cancellation; the bound keeps abandoned renders from accumulating. The
    ///     value must be at least 1.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when the value is below 1.
    /// </exception>
    public int MaxConcurrentRenders
    {
        get => _maxConcurrentRenders;
        init
        {
            // A bound of zero would admit no render at all.
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxConcurrentRenders),
                    value,
                    "The number of concurrent renders must be at least 1.");
            }

            _maxConcurrentRenders = value;
        }
    }
}
