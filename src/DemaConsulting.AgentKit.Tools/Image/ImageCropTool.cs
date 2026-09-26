using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.CanvasNet.Canvas;
using DemaConsulting.CanvasNet.Codecs;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Image;

/// <summary>
///     The <c>image_crop</c> tool: returns a rectangular region of one image the access policy
///     permits the agent to read, as a caption followed by the region itself, or writes that
///     region as a new PNG file where a read-write grant permits it.
/// </summary>
/// <remarks>
///     <para>
///     <b>This tool exists because a model that can see an image still cannot examine part of one
///     closely.</b> A detail occupying a hundredth of a large picture is, at the resolution a
///     provider receives, effectively invisible. Handing the model the region it named — at full
///     resolution, as image content — is what turns "I think that says something" into reading
///     it. The region is named in pixels from the top-left corner, in the coordinate space
///     <see cref="ImageReadTool"/> reports, which is why the two ship as one capability rather
///     than two: a region request the model cannot aim is a region request it will aim wrongly.
///     </para>
///     <para>
///     <b>A region that is not wholly inside the image is refused, never clamped.</b> Clamping
///     would return a different region from the one asked about while reporting success, and the
///     model has no way to detect the substitution: it would then describe, confidently, a part
///     of the picture it never received. The refusal names the image's real dimensions, which is
///     what turns it from a dead end into the fact the model was missing.
///     </para>
///     <para>
///     <b>A region is either returned or written, and the request says which.</b> With no
///     destination named the region comes back inline as image content to examine, adding no
///     write decision to a request that did not ask for one. With a destination named the region
///     is written there as a new PNG file and the result is a text confirmation naming what was
///     written. The second outcome exists because a region that can only be looked at cannot
///     become a figure: an agent preparing a document needs the region it identified to exist as
///     a file it can point at, and returning the bytes inline leaves it with nothing to
///     reference. Naming the outcome in the request rather than inferring it means the model
///     states which of the two it wants, so neither is ever delivered by surprise, and the two
///     are distinguishable in the result's own shape — a <see cref="List{T}"/> of
///     <see cref="AIContent"/> against a string.
///     </para>
///     <para>
///     <b>The destination is resolved through <see cref="PathPolicy.TryResolveWrite"/>,
///     independently of the read decision that admitted the source.</b> This is
///     <see cref="TextFile.TextFileCreateTool"/>'s reasoning applied here: a path an agent may
///     read is refused for creation unless a read-write grant permits it too, which is what
///     keeps a read-wide, write-narrow configuration meaningful. Deriving the write from the
///     read would silently convert every readable location into a writable one, so one
///     <c>image_crop</c> call exercises both of the grants an application configured rather than
///     one of them twice. Making the destination a parameter rather than a second tool is what
///     keeps one denial taxonomy: the coordinate convention, the out-of-bounds refusal naming
///     real dimensions, the decode budget and the media-type map are stated once and apply
///     whichever outcome was asked for. An existing file is never replaced, and no directory is
///     ever created.
///     </para>
///     <para>
///     <b>The destination must be named as a <c>.png</c> file, and the binary ceiling does not
///     apply to one.</b> A region is always encoded as PNG, so a destination named for another
///     format would hold PNG bytes under a name that says otherwise — a file every later reader
///     would be entitled to misread. <see cref="ToolLimits.MaxBinaryBytes"/> governs what this
///     family hands a <em>provider</em>; a file on disk is handed to no provider and charged to
///     no context window, so it is measured against the decode ceiling the image was admitted
///     under and the extent of the region asked for, not against a provider-attachment ceiling
///     that does not describe it.
///     </para>
///     <para>
///     <b>The decode budget is decided from the header, before any pixel data is allocated.</b>
///     A compressed image well inside the binary ceiling can declare far more pixels than its
///     size suggests, and decoding costs four bytes per pixel whatever the file's own encoding
///     was. This tool therefore reads what the file declares, compares it against both the
///     largest extent it can decode on one axis and the host's pixel ceiling, and refuses before
///     allocating anything — naming both bounds, because a model needs both to form a request
///     that would be accepted.
///     </para>
///     <para>
///     <b>The tool takes its policy at construction, and there is no other way to build it.</b>
///     <see cref="Create"/> is the only factory, it requires a <see cref="PathPolicy"/>, and it
///     builds the tool through <see cref="GuardedToolFactory"/>, on exactly the reasoning
///     recorded for <see cref="ImageReadTool"/>.
///     </para>
///     <para>
///     <b>The delegate is declared <c>Task&lt;object&gt;</c> deliberately.</b> A crop with no
///     destination returns a caption plus image content — a <see cref="List{T}"/> of
///     <see cref="AIContent"/> — which the underlying function factory would serialize into a
///     <c>JsonElement</c> unless the guarded factory's result passthrough is applied. Serialized,
///     the provider never receives the region, and the model — told a tool returned one — reports
///     that it can see it and then fabricates a description. There is no error to notice. A crop
///     with a destination returns a string, for which the passthrough is immaterial; the one
///     declared return type carries both outcomes. The result is constructed through
///     <see cref="ToolResult.Image"/> or <see cref="ToolResult.Text"/> and never through
///     <see cref="ToolResult.Structured"/>, which would be serialized to JSON by design.
///     </para>
///     <para>
///     Every refusal is returned rather than thrown, and every refusal this tool composes itself
///     interpolates only integers and the resolved media type — no absolute path, no permitted
///     location, no directory separator. Only the access policy's own refusal discloses host
///     paths, and it does so deliberately and unchanged. <b>That rule governs refusals, not
///     results</b>: the confirmation for a written region names the destination, in the dialect
///     <see cref="PathPolicy.EmitRelative"/> decides, exactly as the text file family's reader
///     names the file it read. A name the model is handed is a name it can hand forward, and
///     withholding it would leave the model unable to reference the file it just produced.
///     <b>Neither the decoding library's own exception text nor the operating system's ever
///     reaches a model</b>: both are developer-facing, and may echo values read out of the file
///     or details of the host's own layout.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class ImageCropTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     Published as a constant on the unit that defines the tool so that a caller naming it
    ///     cannot drift from the name actually registered.
    /// </remarks>
    public const string ToolName = "image_crop";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    /// <remarks>
    ///     Names <see cref="ImageReadTool"/> as the source of the coordinate space, because that
    ///     is what makes the two tools compose and a description is not a denial — the rule that
    ///     restricts a refusal from naming a sibling tool governs refusals, not the text a model
    ///     reads before it has been refused anything. It carries no exhaustive statement of which
    ///     format variants decode: a rare exception stated here would make a model avoid a
    ///     capability that works on essentially every file it will meet, and the exceptions are
    ///     stated where they are actionable, in the refusals themselves.
    /// </remarks>
    private const string ToolDescription =
        "Returns a rectangular region of a PNG or JPEG file the agent is permitted to read. The "
        + "region is given in pixels from the top-left corner of the image, whose pixel "
        + "dimensions image_read reports. Paths are relative to the workspace root. With no "
        + "destination the region comes back as image content to look at. Give a destination to "
        + "write the region instead as a new .png file the agent is permitted to write, and "
        + "receive a confirmation rather than the image. Returns the region, a confirmation, or "
        + "a denial explaining why the request was refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    /// <remarks>
    ///     Worded identically to <see cref="ImageReadTool"/>'s, so the family answers the same
    ///     mistake the same way whichever tool the model reached for.
    /// </remarks>
    private const string PathRequired =
        "A file path is required. Supply a path relative to the workspace root, "
        + "for example 'diagram.png'.";

    /// <summary>
    ///     The refusal used when the request omits any part of the region.
    /// </summary>
    /// <remarks>
    ///     Converts what would otherwise be a framework binding error, raised before this tool is
    ///     reached, into something the model can act on. Every region parameter carries a default
    ///     precisely so this refusal is reachable.
    /// </remarks>
    private const string RegionRequired =
        "A crop region is required. Supply x, y, width and height in pixels.";

    /// <summary>
    ///     The refusal used when the region's origin is negative.
    /// </summary>
    /// <remarks>
    ///     States the coordinate convention, which is the one thing about it a model cannot
    ///     observe from the image itself and the most likely reason it produced a negative value.
    /// </remarks>
    private const string OriginMustNotBeNegative =
        "The region's origin is measured in pixels from the top-left corner of the image, so x "
        + "and y must not be negative.";

    /// <summary>
    ///     The refusal used when the region has no extent.
    /// </summary>
    /// <remarks>
    ///     An empty region is a self-contradictory request rather than a resource problem, which
    ///     is why the reason is <see cref="DenialReason.InvalidRequest"/> and not
    ///     <see cref="DenialReason.ResourceTooLarge"/>.
    /// </remarks>
    private const string ExtentMustBePositive =
        "The region's width and height are measured in pixels and must both be greater than "
        + "zero.";

    /// <summary>
    ///     The refusal used when the request names a directory rather than a file.
    /// </summary>
    /// <remarks>
    ///     States the fact and stops, for the same reason <see cref="ImageReadTool"/>'s does.
    /// </remarks>
    private const string PathIsDirectory =
        "The requested path is a directory, not a file.";

    /// <summary>
    ///     The refusal used when the requested file does not exist.
    /// </summary>
    private const string FileNotFound =
        "The requested file does not exist.";

    /// <summary>
    ///     The refusal used when the file exists and is permitted but cannot be read.
    /// </summary>
    private const string FileUnreadable = "The requested file could not be read.";

    /// <summary>
    ///     The refusal used when the destination does not name a <c>.png</c> file.
    /// </summary>
    /// <remarks>
    ///     A region is always encoded as PNG, so a destination named for another format would
    ///     hold PNG bytes under a name that says otherwise. The reason is
    ///     <see cref="DenialReason.InvalidRequest"/> rather than
    ///     <see cref="DenialReason.UnsupportedMediaType"/> because no file exists yet whose type
    ///     could be unsupported: the contradiction is in the request. A blank or whitespace-only
    ///     destination has no extension and so lands here too, which is deliberate — it is a
    ///     truthful and actionable answer, and it means a destination the model did not really
    ///     intend is never silently read as "no destination".
    /// </remarks>
    private const string DestinationMustBePng =
        "The destination must name a .png file, because a region is always encoded as PNG. "
        + "Supply a destination path whose file name ends in '.png'.";

    /// <summary>
    ///     The refusal used when the destination names a directory rather than a file.
    /// </summary>
    /// <remarks>
    ///     Worded as <see cref="TextFile.TextFileCreateTool"/>'s is, so an agent meets one rule
    ///     for creating a file rather than one rule per family.
    /// </remarks>
    private const string DestinationIsDirectory =
        "The destination path is a directory, not a file. Name the .png file to create within it.";

    /// <summary>
    ///     The refusal used when a file already exists at the destination.
    /// </summary>
    /// <remarks>
    ///     The same guarantee <see cref="TextFile.TextFileCreateTool"/> makes, for the same
    ///     reason: a figure silently replaced is a document that now points at a different
    ///     picture with nothing reporting an error. It also protects the source, because a
    ///     destination equal to the image being cropped names a file already proven to exist.
    /// </remarks>
    private const string DestinationExists =
        "A file already exists at the destination path. This tool writes a new file and does not "
        + "replace an existing one. Name a destination that does not exist yet.";

    /// <summary>
    ///     The refusal used when the destination's parent directory does not exist.
    /// </summary>
    /// <remarks>
    ///     A missing parent is refused rather than materialized, because silently creating a tree
    ///     is a side effect the operator never asked for and, on a mistyped path, would scatter
    ///     directories the agent then believes are real.
    /// </remarks>
    private const string DestinationParentMissing =
        "The parent directory of the destination path does not exist. This tool creates no "
        + "directory, so name a destination inside a directory that already exists.";

    /// <summary>
    ///     The refusal used when a permitted destination cannot be written.
    /// </summary>
    /// <remarks>
    ///     Deliberately carries no remedy, on the same line <see cref="FileUnreadable"/> draws:
    ///     the tool does not know why the operating system refused, and a guess would be worse
    ///     than silence. The underlying failure's own message is never surfaced.
    /// </remarks>
    private const string DestinationUnwritable = "The cropped region could not be written.";

    /// <summary>
    ///     Creates the <c>image_crop</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="ImagePack"/>, which is the only place the
    ///     <c>image</c> family prefix is claimed. The policy is captured by the returned tool's
    ///     delegate, so the tool cannot later be pointed at a different policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every read and every write this tool
    ///     performs.</param>
    /// <returns>The constructed tool, carrying <see cref="ToolName"/> and a description.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    internal static AIFunction Create(PathPolicy policy)
    {
        // A missing policy is a programming error in the composing application, not something a
        // model supplied, so it is surfaced rather than converted into a denial.
        ArgumentNullException.ThrowIfNull(policy);

        // Declared to return Task<object> on purpose; see the type remarks before changing this.
        // Every parameter carries a default so that an omitted argument becomes a refusal this
        // tool composes, rather than a framework error raised before the body is reached.
        var crop = (
                [Description(
                    "The path of the image to take a region of, relative to the workspace root, "
                    + "for example 'diagram.png'.")]
                string? path = null,
                [Description("The region's left edge, in pixels from the image's left edge.")]
                int? x = null,
                [Description("The region's top edge, in pixels from the image's top edge.")]
                int? y = null,
                [Description("The region's width, in pixels.")]
                int? width = null,
                [Description("The region's height, in pixels.")]
                int? height = null,
                [Description(
                    "Optional. The path of a new .png file to write the region to, relative to "
                    + "the workspace root. Omit it to receive the region as image content "
                    + "instead. An existing file is never replaced.")]
                string? destination = null,
                CancellationToken cancellationToken = default) =>
            CropAsync(policy, path, x, y, width, height, destination, cancellationToken);

        return GuardedToolFactory.Create(crop, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Extracts one region, refusing rather than throwing whenever the request cannot be
    ///     honored.
    /// </summary>
    /// <remarks>
    ///     <b>The order of the checks is the contract, and each step refuses before the next
    ///     allocates anything.</b> The request's own shape is judged first, because a
    ///     self-contradictory region — or a destination that cannot hold PNG bytes under its own
    ///     name — is the model's mistake and needs no file to diagnose. The
    ///     policy decision comes before anything is learned about the file, so a refused path
    ///     never reveals whether it exists. The file's size is judged from the handle it was
    ///     opened on and before any of its content is read, its declared dimensions before it is
    ///     decoded, and the region against those dimensions before a single pixel is allocated.
    ///     <para>
    ///     <b>The destination's file-system checks sit after the source's and before the first
    ///     byte is read.</b> After, because the source is the subject of the request: when both
    ///     are wrong the refusal should be about the thing the model asked to look at, and
    ///     nothing expensive has happened either way. Before the read, because a mistyped
    ///     destination must not cost a file read and a decode. The source having been proven to
    ///     exist by then is also what makes a destination equal to the source fall into the
    ///     already-exists refusal, so an image can never be consumed by its own region.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy governing the read and any write.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="x">The region's left edge, or null when the model supplied none.</param>
    /// <param name="y">The region's top edge, or null when the model supplied none.</param>
    /// <param name="width">The region's width, or null when the model supplied none.</param>
    /// <param name="height">The region's height, or null when the model supplied none.</param>
    /// <param name="destination">
    ///     The path to write the region to, or null when the model named none and wants the
    ///     region inline.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The region's content, a confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> CropAsync(
        PathPolicy policy,
        string? path,
        int? x,
        int? y,
        int? width,
        int? height,
        string? destination,
        CancellationToken cancellationToken)
    {
        // A malformed request is refused rather than thrown: the model supplied it, so the model
        // is the one that must be told how to correct it.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        // An omitted region reaches here as a null rather than as a framework binding error,
        // which is the whole reason every region parameter carries a default.
        if (x is null || y is null || width is null || height is null)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, RegionRequired);
        }

        // A negative origin or an empty extent is a contradiction in the request itself and needs
        // no file to diagnose, so it is refused before the file system is consulted at all.
        if (x < 0 || y < 0)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, OriginMustNotBeNegative);
        }

        if (width <= 0 || height <= 0)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, ExtentMustBePositive);
        }

        // A destination that cannot truthfully hold PNG bytes under its own name is a
        // contradiction in the request rather than a problem with any file, so it is judged here
        // with the other request-shape checks and needs no file system to diagnose. The family's
        // own extension map answers it, so "what is this file" keeps one answer and a
        // capitalized extension names the same content.
        if (destination is not null
            && (!ImageMediaTypes.TryResolveMediaType(destination, out var destinationType)
                || !string.Equals(destinationType, ImageMediaTypes.Png, StringComparison.Ordinal)))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, DestinationMustBePng);
        }

        // The single read decision. Resolution and containment both happen inside the policy, so
        // a path outside the permitted location is refused here without this tool making any path
        // judgment of its own.
        if (!policy.TryResolveRead(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // A directory has no image content to take a region of.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsDirectory);
        }

        // The type is decided from the extension before the file system is consulted for size or
        // content, matching the read tool's documented order. A type the family reads but cannot
        // decode, and a type it cannot read at all, are both answered by the one map that owns
        // the question.
        if (!ImageMediaTypes.TryResolveCroppableMediaType(realPath, out var mediaType))
        {
            return ImageMediaTypes.DenyNonCroppableType(realPath);
        }

        // A missing file is refused as such. System.IO.File is qualified because the sibling File
        // tool family occupies the unqualified 'File' name within this assembly.
        if (!System.IO.File.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.TargetNotFound, FileNotFound);
        }

        // The destination's own governance decision, taken only when one was named, and taken
        // independently of the read that admitted the source.
        Destination? target = null;
        if (destination is not null
            && !TryResolveDestination(policy, destination, out target, out var destinationDenial))
        {
            return destinationDenial;
        }

        return await CropPermittedFileAsync(
                policy,
                realPath,
                mediaType,
                new Region(x.Value, y.Value, width.Value, height.Value),
                target,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Resolves a named destination through the policy's write decision and confirms the
    ///     file system can accept a new file there.
    /// </summary>
    /// <remarks>
    ///     <b>The write decision is taken alone.</b> A path a read-only grant permits is not
    ///     thereby writable, which is what keeps a read-wide, write-narrow configuration
    ///     meaningful; the policy's own refusal is returned unchanged, because it enumerates
    ///     every permitted location with its access level and that is what lets a confined agent
    ///     recover to one it may actually use. The remaining checks are the text file family's,
    ///     in its order and its voice, so an agent meets one rule for creating a file rather than
    ///     one rule per family.
    /// </remarks>
    /// <param name="policy">The access policy governing the write.</param>
    /// <param name="destination">The destination the model named; never null here.</param>
    /// <param name="resolved">
    ///     On success, the permitted destination together with the form the confirmation reports
    ///     it in; otherwise <see langword="null"/>.
    /// </param>
    /// <param name="denial">
    ///     On refusal, the composed refusal naming its reason; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> when a new file may be written at the destination; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static bool TryResolveDestination(
        PathPolicy policy,
        string destination,
        [NotNullWhen(true)] out Destination? resolved,
        [NotNullWhen(false)] out object? denial)
    {
        resolved = null;

        // The write decision alone, independent of the read that admitted the source.
        if (!policy.TryResolveWrite(destination, out var realDestination, out var denialMessage))
        {
            denial = ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
            return false;
        }

        // Writing a file over a directory is not a meaningful operation.
        if (Directory.Exists(realDestination))
        {
            denial = ToolResult.Denied(DenialReason.InvalidRequest, DestinationIsDirectory);
            return false;
        }

        // The defining guarantee: an existing file is never replaced. Because the source has
        // already been proven to exist by the time this runs, a request naming the source as its
        // own destination is refused here too, so an image can never be consumed by its own
        // region.
        if (System.IO.File.Exists(realDestination))
        {
            denial = ToolResult.Denied(DenialReason.InvalidRequest, DestinationExists);
            return false;
        }

        // A missing parent is refused, never created.
        var parent = Path.GetDirectoryName(realDestination);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            denial = ToolResult.Denied(DenialReason.TargetNotFound, DestinationParentMissing);
            return false;
        }

        // The confirmation reports the destination in the policy's own dialect: relative when
        // the anchor is granted and the result lies within it, absolute otherwise — which for a
        // session folder outside the workspace is the only truthful answer. Computed once, here,
        // so the write and the confirmation cannot disagree about what was written.
        resolved = new Destination(
            realDestination,
            policy.EmitRelative(realDestination, destination)
                ? Path.GetRelativePath(policy.WorkingDirectory, realDestination)
                : realDestination);
        denial = null;
        return true;
    }

    /// <summary>
    ///     Extracts a region from a file already known to exist, to be permitted, and to carry a
    ///     type a region can be taken from.
    /// </summary>
    /// <remarks>
    ///     The file is opened once and its size is read from that open handle, so the ceiling is
    ///     bound to the very bytes the read then takes: an oversized file is still never read
    ///     into memory merely to discover it was oversized, and a file that grows or is replaced
    ///     after the size was taken cannot enlarge what is loaded. An
    ///     encoded result is judged against the same ceiling before it is returned <em>inline</em>,
    ///     because a large region of a compressed source re-encoded losslessly can genuinely
    ///     exceed a ceiling the source file sat well inside. A result written to a file is not
    ///     measured against it: that ceiling governs what this family hands a provider, and a
    ///     file on disk is handed to none.
    /// </remarks>
    /// <param name="policy">The access policy whose ceilings apply.</param>
    /// <param name="realPath">The real location of the permitted file.</param>
    /// <param name="mediaType">The media type the file's extension resolved to.</param>
    /// <param name="region">The region the model asked for.</param>
    /// <param name="destination">
    ///     The permitted destination to write the region to, or null when the region is wanted
    ///     inline.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The region's content, a confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> CropPermittedFileAsync(
        PathPolicy policy,
        string realPath,
        string mediaType,
        Region region,
        Destination? destination,
        CancellationToken cancellationToken)
    {
        byte[] data;

        try
        {
            // The file is opened once, and the size the ceiling is judged against is read from
            // that same open handle rather than from a separate directory lookup. Judging a size
            // and then reopening the path to read it would leave a window in which the file could
            // grow or be replaced, and the larger content would be loaded despite the ceiling.
            await using var stream = new FileStream(
                realPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            // Nothing is allocated before the ceiling has passed, so an oversized file is still
            // never read into memory merely to discover it was oversized.
            var length = stream.Length;
            if (length > policy.Limits.MaxBinaryBytes)
            {
                return ToolResult.Denied(
                    DenialReason.ResourceTooLarge,
                    "The file exceeds the "
                    + Number(policy.Limits.MaxBinaryBytes)
                    + "-byte binary limit.");
            }

            // Exactly the count that was validated is read, so a file growing under the read
            // cannot enlarge what is loaded. A file that SHRINKS instead ends the read early,
            // which surfaces as an EndOfStreamException — an IOException, and therefore already
            // an access failure by the classification below.
            data = new byte[length];
            await stream.ReadExactlyAsync(data, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            // The same explicit classification the read tool uses: a file system failure becomes
            // a refusal the model can act on, while a genuine defect still surfaces.
            return ToolResult.Denied(DenialReason.InvalidRequest, FileUnreadable);
        }

        return await CropBytesAsync(policy, data, mediaType, region, destination, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Triages the file's declared dimensions and the requested region, then extracts and
    ///     encodes the region.
    /// </summary>
    /// <remarks>
    ///     <b>Every refusal here is decided before the step that would have made it expensive.</b>
    ///     The header is read first, so a file whose bytes are not of the type its extension
    ///     claims is refused without any size being stated — because none was read. A file the
    ///     header reader reports as one the decoder will not decode is refused next, before any
    ///     budget arithmetic, since no budget could make it decodable. The declared
    ///     size is then checked against the decode budget, so a file declaring more pixels than
    ///     the host sanctioned is refused before a pixel buffer exists. The region is checked
    ///     against the declared size, so a request that could never have been satisfied is
    ///     refused before the decode that would have satisfied nothing. Only then is anything
    ///     decoded.
    /// </remarks>
    /// <param name="policy">The access policy whose ceilings apply.</param>
    /// <param name="data">The file's bytes, as already read.</param>
    /// <param name="mediaType">The media type the file's extension resolved to.</param>
    /// <param name="region">The region the model asked for.</param>
    /// <param name="destination">
    ///     The permitted destination to write the region to, or null when the region is wanted
    ///     inline.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>The region's content, a confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> CropBytesAsync(
        PathPolicy policy,
        byte[] data,
        string mediaType,
        Region region,
        Destination? destination,
        CancellationToken cancellationToken)
    {
        // Nothing can be said about a file whose header will not read: it is not content of the
        // type its extension claimed, or it is truncated before its header ends, or it is empty.
        // The refusal states no size, because none was read.
        if (!ImageProbe.TryReadSize(
                data,
                mediaType,
                out var imageWidth,
                out var imageHeight,
                out var canDecode))
        {
            return ToolResult.Denied(
                DenialReason.UnsupportedMediaType,
                "The file is not readable as " + mediaType + " content.");
        }

        // A well-formed file the decoder will not decode is refused in those terms, from the
        // header report rather than from any format knowledge of this library's own. The refusal
        // hands over the declared size directly rather than sending the model to another tool
        // for it, and names no feature, because the report names none.
        if (!canDecode)
        {
            return ToolResult.Denied(
                DenialReason.UnsupportedMediaType,
                "This image is well formed but uses a feature this tool does not decode. The "
                + "image declares " + Size(imageWidth, imageHeight) + " pixels.");
        }

        // The decode budget, decided from the declared dimensions alone. Both bounds are named
        // because a model needs both to form a request that would be accepted: one bounds each
        // axis and the other bounds their product, and neither implies the other. The comparison
        // is done in long arithmetic because the product of two in-range dimensions overflows an
        // int, and the per-axis bound is read from the decoder rather than restated here so the
        // two cannot drift apart.
        if (imageWidth > Surface.MaxDimension
            || imageHeight > Surface.MaxDimension
            || ((long)imageWidth * imageHeight) > policy.Limits.MaxImagePixels)
        {
            return ToolResult.Denied(
                DenialReason.ResourceTooLarge,
                "The image declares " + Size(imageWidth, imageHeight)
                + " pixels. This tool decodes images up to " + Number(Surface.MaxDimension)
                + " pixels on a side and " + Number(policy.Limits.MaxImagePixels)
                + " pixels in total.");
        }

        // The region must lie wholly inside the image. It is REFUSED, never clamped: clamping
        // would return a different region from the one asked about while reporting success, and
        // the model would then describe, confidently, a part of the picture it never received.
        // Naming the image's real dimensions is what turns the refusal into the fact the model
        // was missing. Long arithmetic again, because origin plus extent can overflow an int.
        if ((long)region.X + region.Width > imageWidth
            || (long)region.Y + region.Height > imageHeight)
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "The requested region " + region.Describe()
                + " lies outside the image, which is " + Size(imageWidth, imageHeight)
                + " pixels.");
        }

        return await EncodeRegionAsync(
                policy, data, mediaType, region, imageWidth, imageHeight, destination, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Decodes the image, copies the region out of it and encodes the result.
    /// </summary>
    /// <remarks>
    ///     This is the first step that allocates pixel data, which is why everything decidable
    ///     from the header was decided before it. A decode failure here is a file whose header
    ///     read cleanly and whose body did not, so the refusal states the size — which is exactly
    ///     what distinguishes it from the refusal for a file nothing could be learned about.
    ///     <para>
    ///     <b>The region is encoded as PNG whatever the source was.</b> The encoding is lossless,
    ///     so the model receives the source's pixels rather than a re-compressed approximation of
    ///     them in the very region it asked to examine closely, and it is a format every vision
    ///     provider accepts.
    ///     </para>
    ///     <para>
    ///     A failure inside the encoder, on a pixel buffer this tool constructed, is a defect
    ///     rather than anything a model can provoke, and is allowed to propagate — the same
    ///     dividing line <see cref="IsAccessFailure"/> draws. The encoder therefore sits outside
    ///     the decode's <c>catch</c>: widening that clause to span it would silently report a
    ///     defect as content that could not be decoded.
    ///     </para>
    ///     <para>
    ///     <b>Every pixel buffer this method creates is released.</b> The decoded image is
    ///     disposed as soon as the region has been copied out of it, because the copy is
    ///     independent of its source; the region itself is disposed once it has been encoded,
    ///     which is why the encode precedes the disposal rather than following it. The write,
    ///     when one was asked for, happens inside that same block, which is harmless because the
    ///     encoded bytes are already detached from the surface by then.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy whose ceilings apply.</param>
    /// <param name="data">The file's bytes, as already read.</param>
    /// <param name="mediaType">The media type the file's extension resolved to.</param>
    /// <param name="region">The region the model asked for, already known to be inside the image.</param>
    /// <param name="imageWidth">The width the image's header declared.</param>
    /// <param name="imageHeight">The height the image's header declared.</param>
    /// <param name="destination">
    ///     The permitted destination to write the region to, or null when the region is wanted
    ///     inline.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>The region's content, a confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> EncodeRegionAsync(
        PathPolicy policy,
        byte[] data,
        string mediaType,
        Region region,
        int imageWidth,
        int imageHeight,
        Destination? destination,
        CancellationToken cancellationToken)
    {
        Surface cropped;

        try
        {
            using var source = new MemoryStream(data, writable: false);

            // The decoded image is released as soon as the region has been copied out of it:
            // the copy is independent of its source, so nothing the region needs outlives the
            // surface it came from.
            using var surface = string.Equals(mediaType, ImageMediaTypes.Png, StringComparison.Ordinal)
                ? PngCodec.Load(source)
                : JpegCodec.Load(source);

            cropped = surface.Crop(region.X, region.Y, region.Width, region.Height);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // The header read but the body did not. The size is stated because it was read, which
            // is what makes this refusal distinguishable from the unreadable-header one. The
            // decoder's own message is never surfaced: it is developer-facing and may echo values
            // read out of the file. This clause additionally covers the library's
            // well-formed-but-unsupported exception type, which is the backstop should the
            // header's feasibility report ever lag what the decoder actually accepts.
            return ToolResult.Denied(
                DenialReason.UnsupportedMediaType,
                "The image declares " + Size(imageWidth, imageHeight)
                + " pixels, but its pixel data could not be decoded.");
        }

        // The region is released once it has been encoded. The encoder runs outside the catch
        // above deliberately: a failure on a pixel buffer this tool constructed is a defect, not
        // a refusal.
        using (cropped)
        {
            using var encoded = new MemoryStream();
            PngCodec.Save(cropped, encoded, PngColorType.Rgba);
            var bytes = encoded.ToArray();

            if (destination is null)
            {
                // A large region of a compressed source, re-encoded losslessly, can genuinely
                // exceed a ceiling the source file sat well inside. The same ceiling governs
                // everything this family hands a provider, whatever produced the bytes.
                if (bytes.Length > policy.Limits.MaxBinaryBytes)
                {
                    return ToolResult.Denied(
                        DenialReason.ResourceTooLarge,
                        "The cropped image is " + Number(bytes.Length)
                        + " bytes, which exceeds the " + Number(policy.Limits.MaxBinaryBytes)
                        + "-byte binary limit.");
                }

                // The caption restates the region AND the source's dimensions, so a follow-up
                // region can be aimed without reading the whole image again. ToolResult.Image,
                // never ToolResult.Structured: the latter is serialized to JSON by design, which
                // would destroy the content the guarded factory exists to deliver intact.
                return ToolResult.Image(
                    bytes,
                    ImageMediaTypes.Png,
                    "Cropped region " + region.Describe()
                    + " of a " + Size(imageWidth, imageHeight) + " " + mediaType
                    + " image, returned as " + ImageMediaTypes.Png + ".");
            }

            // The binary ceiling is deliberately not applied here: it bounds what this family
            // hands a provider, and a file on disk is handed to none. What bounds a written
            // region is the decode ceiling the image was admitted under and the extent of the
            // region asked for, both decided before any pixel data existed.
            return await WriteRegionAsync(
                    bytes, destination, region, imageWidth, imageHeight, mediaType, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Writes an encoded region to a destination the policy has already permitted and the
    ///     file system has already been confirmed able to accept.
    /// </summary>
    /// <remarks>
    ///     <b><see cref="FileMode.CreateNew"/> is what makes "does not replace an existing one" a
    ///     guarantee rather than a check with a window after it.</b> The existence refusal taken
    ///     earlier is what <em>teaches</em> — without it the model would receive only "could not
    ///     be written" and have nothing to correct — and this is what <em>enforces</em>: a file
    ///     appearing between the two is refused rather than silently destroyed. Neither is
    ///     redundant.
    ///     <para>
    ///     The confirmation names the destination, the region and the source's dimensions: the
    ///     destination so the model can reference the file it just produced, and the region and
    ///     dimensions so a second, adjacent figure can be aimed without reading the image again.
    ///     It carries no byte count, because a byte count is not something a model can act on and
    ///     stating one would imply a ceiling that deliberately does not exist. <b>No exception's
    ///     own message is ever surfaced</b>, on the same line <see cref="IsAccessFailure"/> draws
    ///     everywhere else in this unit.
    ///     </para>
    /// </remarks>
    /// <param name="bytes">The encoded region to write.</param>
    /// <param name="destination">The permitted destination and the form it is reported in.</param>
    /// <param name="region">The region the model asked for.</param>
    /// <param name="imageWidth">The width the image's header declared.</param>
    /// <param name="imageHeight">The height the image's header declared.</param>
    /// <param name="mediaType">The media type the source file's extension resolved to.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>A confirmation naming what was written, or a refusal naming its reason.</returns>
    private static async Task<object> WriteRegionAsync(
        byte[] bytes,
        Destination destination,
        Region region,
        int imageWidth,
        int imageHeight,
        string mediaType,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                destination.RealPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);

            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            // A permitted destination the operating system nonetheless refused. The reason is
            // not known here, and a guess would be worse than silence.
            return ToolResult.Denied(DenialReason.InvalidRequest, DestinationUnwritable);
        }

        return ToolResult.Text(
            "Wrote the cropped region " + region.Describe()
            + " of a " + Size(imageWidth, imageHeight) + " " + mediaType
            + " image to " + destination.Reported + ".");
    }

    /// <summary>
    ///     Formats an integer for a message a model reads.
    /// </summary>
    /// <remarks>
    ///     The invariant culture is used so a refusal reads identically on every host, rather
    ///     than acquiring thousands separators a model would then have to interpret.
    /// </remarks>
    /// <param name="value">The value to format.</param>
    /// <returns>The formatted value.</returns>
    private static string Number(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Formats a pair of dimensions the way every message in this family states them.
    /// </summary>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    /// <returns>The formatted dimensions.</returns>
    private static string Size(int width, int height)
    {
        return Number(width) + "x" + Number(height);
    }

    /// <summary>
    ///     Determines whether an exception represents a failure to reach or read a path rather
    ///     than a defect that should be allowed to propagate.
    /// </summary>
    /// <remarks>
    ///     Enumerated explicitly rather than catching everything, so that a null reference or an
    ///     out-of-memory condition still fails loudly during development instead of being
    ///     reported to a model as an unreadable file.
    /// </remarks>
    /// <param name="exception">The exception to classify.</param>
    /// <returns>
    ///     <see langword="true"/> when the exception means the file could not be reached or
    ///     read; otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsAccessFailure(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or SecurityException;
    }

    /// <summary>
    ///     The rectangular region a request named, once every part of it is known to be present
    ///     and individually sane.
    /// </summary>
    /// <remarks>
    ///     Carried as one value rather than four loose integers so that the triage steps and the
    ///     messages cannot disagree about which number is which, and so that no method has to
    ///     take four same-typed parameters a caller could transpose.
    /// </remarks>
    /// <param name="X">The region's left edge, in pixels from the image's left edge.</param>
    /// <param name="Y">The region's top edge, in pixels from the image's top edge.</param>
    /// <param name="Width">The region's width, in pixels; always greater than zero.</param>
    /// <param name="Height">The region's height, in pixels; always greater than zero.</param>
    private sealed record Region(int X, int Y, int Width, int Height)
    {
        /// <summary>
        ///     Describes the region the way every message naming it states it.
        /// </summary>
        /// <remarks>
        ///     One form, used by both the success caption and the out-of-bounds refusal, so a
        ///     model reads the same shape whichever it receives.
        /// </remarks>
        /// <returns>The formatted region.</returns>
        public string Describe()
        {
            return Number(X) + "," + Number(Y) + " " + Size(Width, Height);
        }
    }

    /// <summary>
    ///     A destination the policy has permitted for writing, carried together with the form the
    ///     confirmation reports it in.
    /// </summary>
    /// <remarks>
    ///     Carried as one value for the same reason <see cref="Region"/> is: so that no method
    ///     has to take two same-typed string parameters a caller could transpose, and so the
    ///     write and the confirmation cannot disagree about which path was meant. The reported
    ///     form is computed once, at resolution, through
    ///     <see cref="PathPolicy.EmitRelative"/> — the dialect every other tool in the library
    ///     reports a path in.
    /// </remarks>
    /// <param name="RealPath">The real location the policy permitted the write to.</param>
    /// <param name="Reported">
    ///     The destination as the confirmation names it: relative to the working directory when
    ///     the policy's dialect calls for that, and absolute otherwise.
    /// </param>
    private sealed record Destination(string RealPath, string Reported);
}
