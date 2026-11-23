namespace DemaConsulting.AgentKit.Tools.Visual.Diagram;

/// <summary>
///     The diagram languages the diagram family draws, with the file name each is drawn as and the
///     help tag its cards carry.
/// </summary>
/// <remarks>
///     <para>
///     One table, so that the option that names a format, the file name the renderer chooses the
///     format by, the warm-up sample and the help topic a failure points to cannot disagree about
///     which languages exist. A format is named the way a model names it — <c>openscad</c>, not the
///     library's <c>scad</c> tag — and matched case-insensitively.
///     </para>
///     <para>
///     The class is static and holds only immutable data, so it is safe for concurrent use from any
///     number of threads.
///     </para>
/// </remarks>
internal static class DiagramFormats
{
    /// <summary>
    ///     The Mermaid format name.
    /// </summary>
    internal const string Mermaid = "mermaid";

    /// <summary>
    ///     The PlantUML format name.
    /// </summary>
    internal const string PlantUml = "plantuml";

    /// <summary>
    ///     The SVG format name.
    /// </summary>
    internal const string Svg = "svg";

    /// <summary>
    ///     The Vega-Lite format name.
    /// </summary>
    internal const string VegaLite = "vega-lite";

    /// <summary>
    ///     The OpenSCAD format name.
    /// </summary>
    internal const string OpenScad = "openscad";

    /// <summary>
    ///     The help tags of the formats the rendering library also reads but the diagram family never
    ///     draws from inline source; their cards are always hidden from the model.
    /// </summary>
    internal static readonly IReadOnlyList<string> DocumentTags = Array.AsReadOnly(["pdf", "pptx", "vsdx", "raster"]);

    /// <summary>
    ///     Every known format, in the order the defaults list them, with its file name and help tag.
    /// </summary>
    private static readonly (string Name, string FileName, string HelpTag)[] Known =
    [
        (Mermaid, "diagram.mmd", "mermaid"),
        (PlantUml, "diagram.puml", "plantuml"),
        (Svg, "diagram.svg", "svg"),
        (VegaLite, "diagram.vl.json", "vega-lite"),
        (OpenScad, "diagram.scad", "scad")
    ];

    /// <summary>
    ///     Gets every known format name, in the order the defaults list them.
    /// </summary>
    internal static IReadOnlyList<string> All { get; } = Array.AsReadOnly(Known.Select(format => format.Name).ToArray());

    /// <summary>
    ///     Finds the canonical name of a format, matched case-insensitively and ignoring surrounding
    ///     white space.
    /// </summary>
    /// <param name="name">The name to look up, or null.</param>
    /// <returns>The canonical name, or null when no format has that name.</returns>
    internal static string? Canonical(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var trimmed = name.Trim();
        return Known.Where(format => string.Equals(format.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            .Select(format => format.Name)
            .FirstOrDefault();
    }

    /// <summary>
    ///     Gets the file name an inline source of a format is drawn as; its extension chooses the
    ///     format in the rendering library, and it is the only name the library's messages use.
    /// </summary>
    /// <param name="format">A canonical format name.</param>
    /// <returns>The file name.</returns>
    /// <exception cref="ArgumentException">Thrown when the format is unknown.</exception>
    internal static string FileName(string format)
    {
        return Find(format).FileName;
    }

    /// <summary>
    ///     Gets the help tag a format's cards carry in the rendering library's help catalog.
    /// </summary>
    /// <param name="format">A canonical format name.</param>
    /// <returns>The help tag.</returns>
    /// <exception cref="ArgumentException">Thrown when the format is unknown.</exception>
    internal static string HelpTag(string format)
    {
        return Find(format).HelpTag;
    }

    /// <summary>
    ///     Finds a known format by its canonical name.
    /// </summary>
    /// <param name="format">A canonical format name.</param>
    /// <returns>The format's entry.</returns>
    /// <exception cref="ArgumentException">Thrown when the format is unknown.</exception>
    private static (string Name, string FileName, string HelpTag) Find(string format)
    {
        var matches = Known.Where(known => string.Equals(known.Name, format, StringComparison.Ordinal)).ToList();
        return matches.Count == 1
            ? matches[0]
            : throw new ArgumentException("The format '" + format + "' is not a known diagram format.", nameof(format));
    }
}
