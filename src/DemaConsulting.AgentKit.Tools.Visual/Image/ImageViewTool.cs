using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Visual.Rendering;
using DemaConsulting.CanvasNet.Viewer;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Visual.Image;

/// <summary>
///     The <c>image_view</c> tool: draws one document, drawing or diagram file the access policy
///     permits the agent to read — an SVG, a PDF page, a slide, a VSDX page, a Mermaid, PlantUML,
///     Vega-Lite or OpenSCAD source, or a raster image — as a PNG picture the model can look at.
/// </summary>
/// <remarks>
///     <para>
///     <b>A separate tool from <c>image_read</c>, deliberately.</b> <c>image_read</c> hands the
///     provider a file's bytes unchanged, which is right for the formats a provider renders itself
///     and keeps it a byte-exact pass-through. This tool draws a file a provider cannot render and
///     returns the drawing instead, so the two never disagree about what "reading" a file means.
///     </para>
///     <para>
///     <b>The input is judged by the policy before anything is learned about it.</b> The
///     rendering library never judges the file it is asked to draw, only the files that file
///     references, so this tool calls <see cref="PathPolicy.TryResolveRead"/> on the input itself
///     and returns the policy's refusal unchanged. It then reads the file once, inside the
///     binary ceiling, and draws those same bytes — the library is never given the input's path,
///     so the bytes judged and the bytes drawn are the same, and no message the library composes
///     can name the input's absolute location.
///     </para>
///     <para>
///     <b>Every file the input references is confined.</b> Pictures, includes and data files are
///     resolved against the input's own folder and judged by the file-access policy
///     <see cref="VisualAccess"/> maps from the access policy; a reference outside the grants is
///     refused, and a picture whose embedded image was refused is reported as a failure rather
///     than shown with a hole. Nothing is ever fetched from the network.
///     </para>
///     <para>
///     <b>A failed render is a result the model can act on, not a denial.</b> A file the model
///     wrote and can fix — a syntax error, an unsupported construct — is reported as text naming
///     the code, the line and column, the offending line with a caret, and a hint; see
///     <see cref="RenderErrorShaper"/>. Path, permission and size problems remain denials, as in
///     every other tool.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads; a
///     constructed tool captures only the immutable policy and the pack's renderer, which is
///     itself safe for concurrent use.
///     </para>
/// </remarks>
public static class ImageViewTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     Published as a constant so that a sibling refusal naming it — <c>image_read</c>'s
    ///     refusal of an <c>.svg</c> — cannot drift from the name actually registered.
    /// </remarks>
    public const string ToolName = "image_view";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Draws a document, drawing or diagram file the agent is permitted to read as a PNG picture "
        + "to look at: SVG, PDF (one page), PPTX (one slide), VSDX (one page), Mermaid (.mmd), "
        + "PlantUML (.puml), Vega-Lite (.vl.json), OpenSCAD (.scad) or a raster image. Paths are "
        + "relative to the workspace root. Files the document references must lie inside the "
        + "locations the agent may read. Returns the picture with a caption, a report of why it "
        + "could not be drawn, or a denial.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    private const string PathRequired =
        "A file path is required. Supply a path relative to the workspace root, for example "
        + "'diagram.svg'.";

    /// <summary>
    ///     The refusal used when the requested page is below 1.
    /// </summary>
    private const string PageInvalid = "The page must be 1 or greater.";

    /// <summary>
    ///     The refusal used when the requested width is below 1.
    /// </summary>
    private const string WidthInvalid = "The width must be 1 pixel or greater.";

    /// <summary>
    ///     The refusal used when the request names a directory.
    /// </summary>
    private const string PathIsDirectory = "The requested path is a directory, not a file.";

    /// <summary>
    ///     The refusal used when the requested file does not exist.
    /// </summary>
    private const string FileNotFound = "The requested file does not exist.";

    /// <summary>
    ///     The refusal used when the file exists and is permitted but cannot be read.
    /// </summary>
    private const string FileUnreadable = "The requested file could not be read.";

    /// <summary>
    ///     Creates the <c>image_view</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal because a pack is the unit of attachment: an application obtains this tool by
    ///     attaching <see cref="ImagePack"/>, which supplies the renderer whose gate and options
    ///     every render of the pack shares.
    /// </remarks>
    /// <param name="policy">The access policy governing every view. Must not be null.</param>
    /// <param name="renderer">The pack's renderer. Must not be null.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    internal static AIFunction Create(PathPolicy policy, VisualRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(renderer);

        // Declared to return Task<object> so the picture reaches the caller unserialized; every
        // parameter carries a default so an omitted argument becomes a refusal this tool composes.
        var view = (
                [Description(
                    "The path of the file to draw, relative to the workspace root, for example "
                    + "'docs/architecture.svg'.")]
                string? path = null,
                [Description("The 1-based page, slide or sheet of a document to draw. Defaults to 1.")]
                int page = 1,
                [Description(
                    "Optional width of the picture in pixels; the height follows the aspect ratio. "
                    + "Larger values are reduced to the largest size the tool draws.")]
                int? width = null,
                CancellationToken cancellationToken = default) =>
            ViewAsync(policy, renderer, path, page, width, cancellationToken);

        return GuardedToolFactory.Create(view, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Determines whether the rendering library can draw a file, by its name.
    /// </summary>
    /// <remarks>
    ///     Matched on the end of the name rather than on <see cref="Path.GetExtension(string)"/>,
    ///     because one supported extension, <c>.vl.json</c>, has two parts.
    /// </remarks>
    /// <param name="path">The path whose name decides. Must not be null.</param>
    /// <returns>True when the library draws files of this name.</returns>
    internal static bool IsViewable(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var name = Path.GetFileName(path);
        return CanvasNetViewer.Extensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Draws one file, refusing rather than throwing whenever the request cannot be honored.
    /// </summary>
    /// <remarks>
    ///     The order of the checks is the contract: the request's own shape, then the policy
    ///     decision before anything is learned about the file, then the file's kind, existence and
    ///     size, then the render.
    /// </remarks>
    /// <param name="policy">The access policy governing the view.</param>
    /// <param name="renderer">The pack's renderer.</param>
    /// <param name="path">The path the model requested, or null.</param>
    /// <param name="page">The page the model requested.</param>
    /// <param name="width">The width the model requested, or null.</param>
    /// <param name="cancellationToken">A token that cancels the view.</param>
    /// <returns>The picture, a shaped failure, or a refusal.</returns>
    private static async Task<object> ViewAsync(
        PathPolicy policy,
        VisualRenderer renderer,
        string? path,
        int page,
        int? width,
        CancellationToken cancellationToken)
    {
        // Request-shape mistakes the model made and can correct.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        if (page < 1)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PageInvalid);
        }

        if (width is < 1)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, WidthInvalid);
        }

        // The single read decision, taken before anything is learned about the file. The library
        // never judges its input, so this is the input's only judgment.
        if (!policy.TryResolveRead(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsDirectory);
        }

        // The type is decided from the name before the file is opened.
        if (!IsViewable(realPath))
        {
            return ImageMediaTypes.DenyUnsupportedType(realPath);
        }

        if (!File.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.TargetNotFound, FileNotFound);
        }

        var content = await ReadAsync(policy, realPath, cancellationToken).ConfigureAwait(false);
        if (content.Denial is not null)
        {
            return content.Denial;
        }

        // References resolve against the input's own folder and are judged by the mapped policy.
        var folder = Path.GetDirectoryName(realPath);
        var fileName = Path.GetFileName(realPath);
        var outcome = await renderer.RenderAsync(
                new RenderRequest
                {
                    Content = content.Bytes!,
                    FileName = fileName,
                    Folder = folder,
                    Access = VisualAccess.ToFileAccess(policy),
                    Limits = policy.Limits,
                    Width = width,
                    Page = page
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (outcome.Failure is { } failure)
        {
            var sourceText = SourceText(content.Bytes!, fileName, policy.Limits.MaxReadBytes);
            return ToolResult.Text(
                RenderErrorShaper.Shape(failure, new ShapeContext(policy, folder, sourceText, fileName, null)));
        }

        // The picture is a result the model receives, so the binary ceiling applies to it too.
        var png = outcome.Png!;
        if (png.Length > policy.Limits.MaxBinaryBytes)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The rendered picture exceeds the "
                + policy.Limits.MaxBinaryBytes.ToString(CultureInfo.InvariantCulture)
                + "-byte binary limit. Request a smaller width.");
        }

        return ToolResult.Image(png, ImageMediaTypes.Png, Caption(outcome, policy, folder));
    }

    /// <summary>
    ///     Reads a permitted file once, observing the binary ceiling.
    /// </summary>
    /// <remarks>
    ///     The size is read from the open handle, so the ceiling is bound to the very bytes that
    ///     are drawn. The ceiling is the lower of the policy's binary limit and the largest content
    ///     the library accepts, so a file the library would refuse is refused here naming the
    ///     limit, rather than reported as corrupt.
    /// </remarks>
    /// <param name="policy">The access policy whose ceiling applies.</param>
    /// <param name="realPath">The permitted file.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The bytes, or the refusal.</returns>
    private static async Task<(byte[]? Bytes, object? Denial)> ReadAsync(
        PathPolicy policy,
        string realPath,
        CancellationToken cancellationToken)
    {
        var ceiling = Math.Min(policy.Limits.MaxBinaryBytes, VisualRenderer.MaxContentBytes);
        try
        {
            await using var stream = new FileStream(
                realPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            var length = stream.Length;
            if (length > ceiling)
            {
                return (null, ToolResult.Denied(
                    DenialReason.ResourceTooLarge,
                    "The file exceeds the "
                    + ceiling.ToString(CultureInfo.InvariantCulture)
                    + "-byte limit for drawing a file."));
            }

            // Bounded by the int ceiling above, so the narrowing is exact.
            var data = new byte[(int)length];
            await stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
            return (data, null);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException)
        {
            return (null, ToolResult.Denied(DenialReason.InvalidRequest, FileUnreadable));
        }
    }

    /// <summary>
    ///     Decodes a text source so that a failure can quote the line it points at.
    /// </summary>
    /// <remarks>
    ///     Only a raster-free, text-sized file is decoded: a raster image or a document has no
    ///     line to quote, and a file larger than the policy's read ceiling is not worth quoting
    ///     from.
    /// </remarks>
    /// <param name="bytes">The file's bytes.</param>
    /// <param name="fileName">The file's name.</param>
    /// <param name="maxReadBytes">The policy's read ceiling.</param>
    /// <returns>The text, or null.</returns>
    private static string? SourceText(byte[] bytes, string fileName, int maxReadBytes)
    {
        string[] textExtensions = [".svg", ".mmd", ".mermaid", ".puml", ".pu", ".plantuml", ".vl", ".vl.json", ".scad"];
        if (bytes.Length > maxReadBytes
            || !textExtensions.Any(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    ///     Composes the caption naming what the model is being shown.
    /// </summary>
    /// <param name="outcome">The successful render.</param>
    /// <param name="policy">The access policy, whose folders are scrubbed from the notes.</param>
    /// <param name="folder">The input's folder.</param>
    /// <returns>The caption.</returns>
    private static string Caption(RenderOutcome outcome, PathPolicy policy, string? folder)
    {
        var caption = "Rendered " + outcome.Format
            + (outcome.PageCount > 1
                ? " page " + outcome.Page.ToString(CultureInfo.InvariantCulture)
                  + " of " + outcome.PageCount.ToString(CultureInfo.InvariantCulture)
                : string.Empty)
            + " as PNG, "
            + outcome.Width.ToString(CultureInfo.InvariantCulture) + "x"
            + outcome.Height.ToString(CultureInfo.InvariantCulture) + " pixels."
            + (outcome.Notes.Count > 0 ? " Notes: " + string.Join("; ", outcome.Notes) : string.Empty);

        return RenderErrorShaper.Scrub(caption, policy, folder);
    }
}
