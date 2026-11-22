using System.ComponentModel;
using System.Globalization;
using System.Text;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Image;
using DemaConsulting.AgentKit.Tools.Visual.Rendering;
using DemaConsulting.CanvasNet.Viewer.Help;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Visual.Diagram;

/// <summary>
///     The <c>diagram_render</c> tool: draws diagram source the model supplies inline — Mermaid,
///     PlantUML, SVG, Vega-Lite or OpenSCAD — and shows the model the result, optionally saving the
///     picture as a PNG under the write grant.
/// </summary>
/// <remarks>
///     <para>
///     <b>Source is given inline, never as a path.</b> A model drawing a diagram is writing its
///     source anyway, and taking it inline lets one call draw, look and redraw without a file
///     write in between. A source file already in the workspace is drawn by <c>image_view</c>.
///     The source must fit within the policy's read ceiling, the same bound a file the agent reads
///     observes.
///     </para>
///     <para>
///     <b>Two preview modes, two declarations.</b> In <see cref="DiagramPreview.Image"/> mode the
///     picture is returned as image content with a caption, and <c>save_as</c> is optional. In
///     <see cref="DiagramPreview.Text"/> mode, for a model that cannot see, the picture is saved and
///     the model receives a description in words — the picture's size, the nodes, edges, labels and
///     marks it contains, and its dominant colors — so <c>save_as</c> is required. Each mode is
///     built from its own delegate, because a parameter's description is a compile-time attribute:
///     one declaration can then never call <c>save_as</c> optional in one place and required in
///     another.
///     </para>
///     <para>
///     <b>References are confined.</b> A relative reference in inline source — an SVG picture, a
///     PlantUML include, a Vega-Lite data file, an OpenSCAD include — resolves against the working
///     directory when the access policy permits reading it, and fails closed otherwise. Every
///     referenced file is judged by the file-access policy <see cref="VisualAccess"/> maps from the
///     access policy, a picture whose embedded image was refused fails the render, and nothing is
///     fetched from the network.
///     </para>
///     <para>
///     <b>A failed render is a result the model can act on.</b> The text names the code, the line
///     and column, the offending source line with a caret, a hint, every note, and the
///     <c>diagram_help</c> topic of the format's diagnostics; see <see cref="RenderErrorShaper"/>. A
///     picture that came out empty or almost empty is reported the same way. Path, permission and
///     size problems remain denials.
///     </para>
///     <para>
///     <b>Saving replaces an earlier rendering.</b> The model draws, looks, fixes and saves again
///     under the same name, so <c>save_as</c> replaces an existing file — atomically, under the
///     write grant — rather than refusing the second save; see <see cref="RenderDestination"/>. The
///     destination is checked before anything is drawn.
///     </para>
///     <para>
///     The class is static; a constructed tool captures only the immutable policy, the pack's
///     renderer and its filtered help catalog, each safe for concurrent use, so the tool is safe
///     for concurrent use from any number of threads.
///     </para>
/// </remarks>
public static class DiagramRenderTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    public const string ToolName = "diagram_render";

    /// <summary>
    ///     The refusal used when the request carries no source.
    /// </summary>
    private const string SourceRequired = "The diagram source is required: supply the complete source text.";

    /// <summary>
    ///     The refusal used when the requested width is below 1.
    /// </summary>
    private const string WidthInvalid = "The width must be 1 pixel or greater.";

    /// <summary>
    ///     The refusal used in text preview when the request names no destination.
    /// </summary>
    private const string SaveAsRequired =
        "save_as is required: name the .png file to save the picture to, relative to the workspace root, "
        + "for example 'diagrams/flow.png'. A description of the picture is returned instead of the picture.";

    /// <summary>
    ///     The refusal used when a permitted destination could not be written.
    /// </summary>
    private const string SaveFailed = "The picture was drawn but could not be saved.";

    /// <summary>
    ///     The description of the <c>format</c> parameter.
    /// </summary>
    private const string FormatDescription =
        "The diagram language of the source, for example 'mermaid'. The tool description lists the "
        + "languages offered.";

    /// <summary>
    ///     The description of the <c>source</c> parameter.
    /// </summary>
    private const string SourceDescription =
        "The complete diagram source text. Relative file references in it resolve against the workspace root.";

    /// <summary>
    ///     The description of the <c>width</c> parameter.
    /// </summary>
    private const string WidthDescription =
        "Optional width of the picture in pixels; the height follows the aspect ratio. Larger values "
        + "are reduced to the largest size the tool draws.";

    /// <summary>
    ///     Creates the <c>diagram_render</c> tool for one preview mode.
    /// </summary>
    /// <remarks>
    ///     Internal because a pack is the unit of attachment: an application obtains this tool by
    ///     attaching <see cref="DiagramPack"/>, which supplies the renderer whose gate every drawing
    ///     of the pack shares, the offered languages and the filtered help catalog a failure points
    ///     into.
    /// </remarks>
    /// <param name="policy">The access policy governing every render. Must not be null.</param>
    /// <param name="renderer">The pack's renderer. Must not be null.</param>
    /// <param name="preview">How the model is shown the picture.</param>
    /// <param name="formats">The canonical names of the offered languages. Must not be null or empty.</param>
    /// <param name="catalog">The pack's filtered help catalog. Must not be null.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="formats"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="preview"/> is undefined.</exception>
    internal static AIFunction Create(
        PathPolicy policy,
        VisualRenderer renderer,
        DiagramPreview preview,
        IReadOnlyList<string> formats,
        HelpCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(catalog);
        if (formats.Count == 0)
        {
            throw new ArgumentException("At least one diagram format is required.", nameof(formats));
        }

        var context = new RenderContext(policy, renderer, preview, formats, catalog);
        var languages = string.Join(", ", formats);

        return preview switch
        {
            DiagramPreview.Image => GuardedToolFactory.Create(
                (
                    [Description(FormatDescription)] string? format = null,
                    [Description(SourceDescription)] string? source = null,
                    [Description(
                        "Optional path of a .png file to save the picture to as well, relative to the "
                        + "workspace root, for example 'diagrams/flow.png'. An existing file is replaced.")]
                    string? save_as = null,
                    [Description(WidthDescription)] int? width = null,
                    CancellationToken cancellationToken = default) =>
                    RenderAsync(context, format, source, save_as, width, cancellationToken),
                ToolName,
                "Draws diagram source text as a PNG picture and returns the picture so you can check what "
                + "you drew. Languages: " + languages + ". A source that cannot be drawn is reported "
                + "with the line, the column, the offending line and the help topic to read; fix it and "
                + "draw again. Optionally saves the picture to a .png file the agent may write."),

            DiagramPreview.Text => GuardedToolFactory.Create(
                (
                    [Description(FormatDescription)] string? format = null,
                    [Description(SourceDescription)] string? source = null,
                    [Description(
                        "Required path of the .png file to save the picture to, relative to the workspace "
                        + "root, for example 'diagrams/flow.png'. An existing file is replaced.")]
                    string? save_as = null,
                    [Description(WidthDescription)] int? width = null,
                    CancellationToken cancellationToken = default) =>
                    RenderAsync(context, format, source, save_as, width, cancellationToken),
                ToolName,
                "Draws diagram source text, saves the picture to a .png file the agent may write, and "
                + "returns a description of it in words: its size, the nodes, edges, labels and marks it "
                + "contains, and its dominant colors. Languages: " + languages + ". A source that cannot "
                + "be drawn is reported with the line, the column, the offending line and the help topic "
                + "to read; fix it and draw again."),

            _ => throw new ArgumentOutOfRangeException(nameof(preview), preview, "The preview mode is not defined.")
        };
    }

    /// <summary>
    ///     Draws one source, refusing rather than throwing whenever the request cannot be honored.
    /// </summary>
    /// <remarks>
    ///     The order of the checks is the contract: the request's own shape, then the size of the
    ///     source, then the destination, all before anything is drawn; then the render, the ceiling
    ///     on the picture, and the save.
    /// </remarks>
    /// <param name="context">What the tool was built with.</param>
    /// <param name="format">The language the model named, or null.</param>
    /// <param name="source">The source the model supplied, or null.</param>
    /// <param name="saveAs">The destination the model named, or null.</param>
    /// <param name="width">The width the model asked for, or null.</param>
    /// <param name="cancellationToken">A token that cancels the render.</param>
    /// <returns>The picture or its description, a shaped failure, or a refusal.</returns>
    private static async Task<object> RenderAsync(
        RenderContext context,
        string? format,
        string? source,
        string? saveAs,
        int? width,
        CancellationToken cancellationToken)
    {
        var policy = context.Policy;

        // Request-shape mistakes the model made and can correct.
        var canonical = DiagramFormats.Canonical(format);
        if (canonical is null || !context.Formats.Contains(canonical, StringComparer.Ordinal))
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "The format must be one of: " + string.Join(", ", context.Formats) + ".");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, SourceRequired);
        }

        if (width is < 1)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, WidthInvalid);
        }

        // The source is bounded like any text the agent reads.
        var bytes = Encoding.UTF8.GetBytes(source);
        var ceiling = Math.Min(policy.Limits.MaxReadBytes, VisualRenderer.MaxContentBytes);
        if (bytes.Length > ceiling)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The source exceeds the " + ceiling.ToString(CultureInfo.InvariantCulture)
                + "-byte limit for diagram source.");
        }

        // The destination is decided before anything is drawn, so a refused save costs no render.
        RenderDestination.Destination? destination = null;
        if (string.IsNullOrWhiteSpace(saveAs))
        {
            if (context.Preview == DiagramPreview.Text)
            {
                return ToolResult.Denied(DenialReason.InvalidRequest, SaveAsRequired);
            }
        }
        else if (!RenderDestination.TryResolve(policy, saveAs, out destination, out var denial))
        {
            return denial;
        }

        // Inline source has no folder of its own: its references resolve against the working
        // directory when it may be read, and otherwise fail closed.
        var fileName = DiagramFormats.FileName(canonical);
        var outcome = await context.Renderer.RenderAsync(
                new RenderRequest
                {
                    Content = bytes,
                    FileName = fileName,
                    Folder = VisualAccess.InlineBaseFolder(policy),
                    Access = VisualAccess.ToFileAccess(policy),
                    Limits = policy.Limits,
                    Width = width,
                    Describe = context.Preview == DiagramPreview.Text
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Failure is { } failure)
        {
            var help = new HelpReference(
                DiagramHelpTool.ToolName,
                DiagramFormats.HelpTag(canonical),
                topic => context.Catalog.Find(topic) is not null);
            return ToolResult.Text(
                RenderErrorShaper.Shape(failure, new ShapeContext(policy, null, source, fileName, help)));
        }

        // The picture is content the tool returns or writes, so the binary ceiling applies to it.
        var png = outcome.Png!;
        if (png.Length > policy.Limits.MaxBinaryBytes)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The rendered picture exceeds the "
                + policy.Limits.MaxBinaryBytes.ToString(CultureInfo.InvariantCulture)
                + "-byte binary limit. Request a smaller width.");
        }

        if (destination is not null
            && !await RenderDestination.TryWriteAsync(png, destination, cancellationToken).ConfigureAwait(false))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, SaveFailed);
        }

        return context.Preview == DiagramPreview.Image
            ? ToolResult.Image(png, ImageMediaTypes.Png, Caption(canonical, outcome, destination, policy))
            : ToolResult.Structured(Description(canonical, outcome, destination!, policy));
    }

    /// <summary>
    ///     Composes the caption of a picture returned in image preview.
    /// </summary>
    /// <param name="format">The language drawn.</param>
    /// <param name="outcome">The successful render.</param>
    /// <param name="destination">Where the picture was saved, or null.</param>
    /// <param name="policy">The access policy, whose folders are scrubbed from the notes.</param>
    /// <returns>The caption.</returns>
    private static string Caption(
        string format,
        RenderOutcome outcome,
        RenderDestination.Destination? destination,
        PathPolicy policy)
    {
        var caption = "Rendered " + format + " as PNG, "
            + outcome.Width.ToString(CultureInfo.InvariantCulture) + "x"
            + outcome.Height.ToString(CultureInfo.InvariantCulture) + " pixels"
            + (destination is null ? "." : ", saved to " + destination.Reported + ".")
            + (outcome.Notes.Count > 0 ? " Notes: " + string.Join("; ", outcome.Notes) : string.Empty);

        return RenderErrorShaper.Scrub(caption, policy, null);
    }

    /// <summary>
    ///     Composes the description of a picture returned in text preview.
    /// </summary>
    /// <param name="format">The language drawn.</param>
    /// <param name="outcome">The successful render, carrying its features and colors.</param>
    /// <param name="destination">Where the picture was saved.</param>
    /// <param name="policy">The access policy, whose folders are scrubbed from the notes.</param>
    /// <returns>The structured description.</returns>
    private static object Description(
        string format,
        RenderOutcome outcome,
        RenderDestination.Destination destination,
        PathPolicy policy)
    {
        return new
        {
            savedTo = destination.Reported,
            format,
            width = outcome.Width,
            height = outcome.Height,
            notes = outcome.Notes.Select(note => RenderErrorShaper.Scrub(note, policy, null)).ToArray(),
            features = outcome.Features.ToArray(),
            featuresTruncated = outcome.FeaturesTruncated,
            dominantColors = outcome.DominantColors.ToArray()
        };
    }

    /// <summary>
    ///     What a constructed tool captured: the policy, the pack's renderer, the preview mode, the
    ///     offered languages and the filtered help catalog.
    /// </summary>
    /// <param name="Policy">The access policy governing every render.</param>
    /// <param name="Renderer">The pack's renderer.</param>
    /// <param name="Preview">How the model is shown the picture.</param>
    /// <param name="Formats">The offered languages.</param>
    /// <param name="Catalog">The pack's filtered help catalog.</param>
    private sealed record RenderContext(
        PathPolicy Policy,
        VisualRenderer Renderer,
        DiagramPreview Preview,
        IReadOnlyList<string> Formats,
        HelpCatalog Catalog);
}
