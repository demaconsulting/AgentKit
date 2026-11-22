namespace DemaConsulting.AgentKit.Tools.Visual.Diagram;

/// <summary>
///     The options a <see cref="DiagramPack"/> carries: the rendering bounds its drawings observe,
///     and the diagram languages it offers.
/// </summary>
/// <remarks>
///     <para>
///     <b>Every value is checked when it is set.</b> <see cref="Visual"/> may not be null, and
///     <see cref="Formats"/> must name at least one known language, each once. A value outside
///     that is refused where it was set, with an <see cref="ArgumentException"/>, so an instance
///     that exists is one whose every value is applied as stated: <c>diagram_render</c> accepts
///     exactly the configured languages and <c>diagram_help</c> serves the help of exactly those.
///     </para>
///     <para>
///     <b>The format list is a private copy.</b> The list a caller assigns is copied when it is
///     set, and exposed as a read-only view of that copy, so changing the caller's list afterwards
///     changes nothing here and nothing here can be changed through the exposed list. Each name
///     is stored in its canonical lower-case form.
///     </para>
///     <para>
///     <b>Why the preview mode is not an option.</b> Whether the model sees the picture or a
///     description of it is chosen by the pack's constructor or by <see cref="DiagramPack.For"/>,
///     which reads it from the host's capabilities. Were it an option here, <c>For</c> would have to
///     override a value the caller had set, and an accepted option would then be silently ignored.
///     </para>
///     <para>
///     The record is immutable once built and is therefore safe to share between packs and
///     threads.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     A pack offering only Mermaid and PlantUML, with a shorter render timeout than the default.
///     </para>
///     <code>
///     var options = new DiagramPackOptions
///     {
///         Formats = ["mermaid", "plantuml"],
///         Visual = new VisualOptions { RenderTimeout = TimeSpan.FromSeconds(10) }
///     };
///
///     var pack = new DiagramPack(DiagramPreview.Image, options);
///     </code>
/// </example>
public sealed record DiagramPackOptions
{
    /// <summary>
    ///     The format list every instance starts with: every known language.
    /// </summary>
    private static readonly IReadOnlyList<string> DefaultFormats = DiagramFormats.All;

    /// <summary>
    ///     Backing store for <see cref="Visual"/>.
    /// </summary>
    private readonly VisualOptions _visual = VisualOptions.Default;

    /// <summary>
    ///     Backing store for <see cref="Formats"/>: a read-only view of a private copy.
    /// </summary>
    private readonly IReadOnlyList<string> _formats = DefaultFormats;

    /// <summary>
    ///     Gets the default options: the default rendering bounds and every known language.
    /// </summary>
    public static DiagramPackOptions Default { get; } = new();

    /// <summary>
    ///     Gets the rendering bounds the pack's drawings observe.
    /// </summary>
    /// <remarks>
    ///     Defaults to <see cref="VisualOptions.Default"/>. The pack that receives the options checks
    ///     that <see cref="VisualOptions.MinPreviewSide"/> does not exceed
    ///     <see cref="VisualOptions.MaxRenderSide"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when set to <see langword="null"/>.</exception>
    public VisualOptions Visual
    {
        get => _visual;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _visual = value;
        }
    }

    /// <summary>
    ///     Gets the diagram languages the pack offers, in canonical lower-case form.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The known languages are <c>mermaid</c>, <c>plantuml</c>, <c>svg</c>, <c>vega-lite</c> and
    ///     <c>openscad</c>; the default is all five, in that order. A name is matched
    ///     case-insensitively and stored in its canonical form.
    ///     </para>
    ///     <para>
    ///     The assigned list is copied, and the property exposes a read-only view of the copy.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when set to <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the list is empty, or names a language that is unknown, null or listed twice.
    /// </exception>
    public IReadOnlyList<string> Formats
    {
        get => _formats;
        init
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(Formats));
            }

            // Copied first, so the checks and the stored list see the same names even if the
            // caller's list changes while they run.
            var problem = Canonicalize([.. value], out var canonical);
            if (problem is not null)
            {
                throw new ArgumentException(problem, nameof(Formats));
            }

            _formats = Array.AsReadOnly(canonical);
        }
    }

    /// <summary>
    ///     Puts a copied format list in canonical form, reporting the first problem with it.
    /// </summary>
    /// <param name="copy">The caller's list, copied.</param>
    /// <param name="canonical">The canonical names, valid only when no problem is reported.</param>
    /// <returns>
    ///     Null when the list is valid; otherwise why it is not: it is empty, or names a language
    ///     that is unknown, null or listed twice.
    /// </returns>
    private static string? Canonicalize(string[] copy, out string[] canonical)
    {
        canonical = new string[copy.Length];
        if (copy.Length == 0)
        {
            return "At least one diagram format is required.";
        }

        for (var index = 0; index < copy.Length; index++)
        {
            var name = DiagramFormats.Canonical(copy[index]);
            if (name is null)
            {
                return "The diagram format '" + (copy[index] ?? "<null>") + "' is unknown. The known formats are "
                    + string.Join(", ", DiagramFormats.All) + ".";
            }

            if (canonical.Take(index).Contains(name, StringComparer.Ordinal))
            {
                return "The diagram format '" + name + "' is listed more than once.";
            }

            canonical[index] = name;
        }

        return null;
    }
}
