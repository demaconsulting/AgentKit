using System.ComponentModel;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.CanvasNet.Canvas;
using DemaConsulting.CanvasNet.Codecs;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Image;

/// <summary>
///     The <c>image_auto_crop</c> tool: trims one image the access policy permits the agent to
///     read down to the region its content occupies, expanded by a padding margin, returned as a
///     caption followed by the region itself, or written as a new PNG file where a read-write
///     grant permits it.
/// </summary>
/// <remarks>
///     <para>
///     <b>This tool exists because the region a model wants is usually the region it cannot
///     name.</b> A screenshot, a slide export or a rendered chart arrives surrounded by margin
///     that carries no information and costs the same resolution budget as the content does. A
///     model can see that the picture is mostly empty; it cannot measure where the emptiness
///     stops, because measuring is exactly what it is unable to do by looking. <c>image_crop</c>
///     answers "give me this rectangle"; this tool answers "give me the part that matters", which
///     is the question a model can actually ask.
///     </para>
///     <para>
///     <b>Automatic trimming was rejected for this library once, and this version is definable
///     only because every step of it is a pure function of the file's bytes.</b> The earlier
///     proposal would have chosen a threshold from the image, so the same file could trim
///     differently on two runs and a model had no way to tell which answer it got. Here the
///     background is the modal exact color of the image's own one-pixel border, taken in a fixed
///     scan order with ties resolved to the earliest position; the tolerance is a fixed integer
///     constant; the classification is integer arithmetic with no floating point anywhere; and
///     the scan visits every pixel with no early exit. <b>Given the same bytes and the same
///     padding, the region is identical on every host, every target framework and every run.</b>
///     </para>
///     <para>
///     <b>The background is sampled, never assumed.</b> Nothing in the computation mentions
///     white. A dark-themed screenshot, a colored slide and a transparent export each have their
///     own margin, and each is read from the border of the image in hand. The mode is used rather
///     than a corner or a mean for two reasons a single sample and an average both fail: one
///     pixel is one sample, so a compression artifact or a single-pixel rule at that exact
///     coordinate picks a background matching nothing in the image; and a mean over a border that
///     includes even a few content pixels — the ordinary case of content running to an edge —
///     drifts to a color present nowhere in the image, so nothing matches it and the trim
///     degenerates to the whole picture. The mode is always a color the image genuinely contains
///     and tolerates a minority of content pixels on the border.
///     </para>
///     <para>
///     <b>The tolerance is fixed and is not a parameter.</b> See
///     <see cref="BackgroundTolerance"/>: a caller that cannot see the image cannot choose a
///     tolerance better than the default, and a knob a model must guess at is precisely the
///     unpredictability this tool was redesigned to remove. The padding <em>is</em> the caller's,
///     because a caller does know how much air it wants around a figure it is about to place.
///     </para>
///     <para>
///     <b>Padding that would run past an edge is clamped, never refused.</b> Content flush to an
///     edge is ordinary rather than a fault, and a padding larger than every margin yields the
///     whole image — a truthful and predictable answer. This is not the substitution
///     <c>image_crop</c> refuses: there the caller named a rectangle and would have received a
///     different one, whereas here the caller named no rectangle at all, so there is nothing to
///     substitute for.
///     </para>
///     <para>
///     <b>An image that is entirely background is refused, and nothing is written.</b> There is
///     no honest content box for such an image. Returning the whole picture would answer a
///     different question while reporting success — the substitution this family exists to
///     refuse — and returning an empty region is not a region at all. The refusal names the
///     image's dimensions and says that every pixel matched the sampled background, which is the
///     fact the model was missing.
///     </para>
///     <para>
///     <b>Misclassification can only ever enlarge the region, never lose content.</b> The
///     per-channel test is the stricter of the plausible measures on multi-channel drift, so a
///     marginal pixel is classified as content. Too small a tolerance leaves a halo and returns
///     nearly the whole image — visible and harmless; too large would eat faint content, which is
///     genuinely lossy. The measure errs toward the harmless failure by construction.
///     </para>
///     <para>
///     <b>A region is either returned or written, and the request says which.</b> With no
///     destination named the region comes back inline as image content, adding no write decision
///     to a request that did not ask for one, which is why this tool remains fully useful under a
///     policy that permits no writing anywhere. With a destination named the region is written
///     there as a new PNG file and the result is a text confirmation. The destination is resolved
///     through <see cref="PathPolicy.TryResolveWrite"/>, independently of the read decision that
///     admitted the source, and the whole destination taxonomy is
///     <see cref="ImageDestination"/>'s — the same five refusals, in the same order and the same
///     words, that <see cref="ImageCropTool"/> produces, because an agent should meet one rule
///     for creating a file rather than one rule per tool.
///     </para>
///     <para>
///     <b>The decode budget is decided from the header, before any pixel data is allocated</b>,
///     through <see cref="ImageAdmission"/>. A compressed image well inside the binary ceiling can
///     declare far more pixels than its size suggests, and decoding costs four bytes per pixel
///     whatever the file's own encoding was.
///     </para>
///     <para>
///     <b>The delegate is declared <c>Task&lt;object&gt;</c> deliberately</b>, on exactly the
///     reasoning recorded for <see cref="ImageCropTool"/>: a trim with no destination returns a
///     caption plus image content, which the underlying function factory would serialize into a
///     <c>JsonElement</c> unless the guarded factory's result passthrough is applied. The result
///     is constructed through <see cref="ToolResult.Image"/> or <see cref="ToolResult.Text"/> and
///     never through <see cref="ToolResult.Structured"/>, which would be serialized to JSON by
///     design.
///     </para>
///     <para>
///     Every refusal is returned rather than thrown, and every refusal this tool composes itself
///     interpolates only integers and the resolved media type — no absolute path, no permitted
///     location, no directory separator. Only the access policy's own refusal discloses host
///     paths, and it does so deliberately and unchanged. That rule governs refusals, not results:
///     the confirmation for a written region names the destination, in the dialect
///     <see cref="PathPolicy.EmitRelative"/> decides.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class ImageAutoCropTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     Published as a constant on the unit that defines the tool so that a caller naming it
    ///     cannot drift from the name actually registered.
    /// </remarks>
    public const string ToolName = "image_auto_crop";

    /// <summary>
    ///     How far a pixel may differ from the sampled background, per channel, and still count
    ///     as background.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Fixed, and deliberately absent from the tool's parameter list.</b> A caller cannot
    ///     see the image, so to choose a tolerance better than this one it would have to already
    ///     know the noise characteristics of the margin — which is precisely the thing it called
    ///     this tool in order not to have to know. A knob a model must guess at produces output
    ///     that varies with the guess, and that unpredictability is why automatic trimming was
    ///     rejected for this library the first time. Fixing it makes the result a pure function
    ///     of the file and the padding.
    ///     </para>
    ///     <para>
    ///     <b>Why 8 of 255, about 3.1%.</b> JPEG chroma ringing beside a hard edge on a flat
    ///     field sits within roughly two to six of the flat value at ordinary quality settings, so
    ///     8 absorbs it with margin. The outermost ring of an anti-aliased glyph or rule is
    ///     blended only a few percent toward the ink, which is the ring that decides whether a
    ///     halo survives. Genuine content clears 8 in at least one channel in every realistic
    ///     case: the faintest useful gridline in a rendered chart is at least 20 from white. The
    ///     value therefore sits an order of magnitude below any plausible faint-content
    ///     threshold, which is the side it must err on — too small merely leaves a halo, while
    ///     too large eats content.
    ///     </para>
    /// </remarks>
    private const int BackgroundTolerance = 8;

    /// <summary>
    ///     The padding applied when the caller names none.
    /// </summary>
    /// <remarks>
    ///     Large enough that a figure does not sit flush against its own crop, small enough that
    ///     it never dominates a trimmed margin.
    /// </remarks>
    private const int DefaultPadding = 8;

    /// <summary>
    ///     The largest padding this tool accepts.
    /// </summary>
    /// <remarks>
    ///     256 exceeds any margin a page, slide or diagram export leaves — typically 20 to 80
    ///     pixels — so no legitimate request meets it, and a value beyond it is a model error
    ///     worth reporting rather than silently answering with the whole image.
    /// </remarks>
    private const int MaxPadding = 256;

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     States the one thing that distinguishes this tool from <see cref="ImageCropTool"/> —
    ///     that the caller names no region — because a model given two region tools and no basis
    ///     for choosing will reach for the one it read about first. It states that the background
    ///     comes from the image's own border, because that is what tells a model the tool is
    ///     usable on a dark or colored export rather than only on a white one, and it states the
    ///     padding's default and bound, because those are the only values a caller has to
    ///     choose.
    ///     </para>
    ///     <para>
    ///     <b>The description is fixed and does not vary with the access policy</b>, on the
    ///     settled reasoning recorded for <see cref="ImageCropTool"/>: the <c>destination</c>
    ///     parameter's description is a <c>[Description]</c> attribute argument and therefore a
    ///     compile-time constant, so a policy-varying tool description would ship inside the same
    ///     declaration as a parameter description still offering the destination — one payload
    ///     contradicting itself.
    ///     </para>
    /// </remarks>
    private const string ToolDescription =
        "Trims a PNG or JPEG file the agent is permitted to read down to the region its content "
        + "occupies, and returns that region. The caller names no region: the tool finds it, "
        + "taking the background from the image's own border rather than assuming white, so a "
        + "dark, colored or transparent margin trims correctly. The region is expanded by a "
        + "padding margin, 8 pixels by default and at most 256, clamped to the image's edges. "
        + "Paths are relative to the workspace root. With no destination the trimmed region "
        + "comes back as image content to look at. Give a destination to write it instead as a "
        + "new .png file the agent is permitted to write, and receive a confirmation rather than "
        + "the image. Returns the region, a confirmation, or a denial explaining why the request "
        + "was refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    /// <remarks>
    ///     Worded identically to <see cref="ImageReadTool"/>'s and
    ///     <see cref="ImageCropTool"/>'s, so the family answers the same mistake the same way
    ///     whichever tool the model reached for.
    /// </remarks>
    private const string PathRequired =
        "A file path is required. Supply a path relative to the workspace root, "
        + "for example 'diagram.png'.";

    /// <summary>
    ///     The refusal used when the requested padding is negative.
    /// </summary>
    /// <remarks>
    ///     A negative padding would <em>shrink</em> the content box and lose content, which is
    ///     the one outcome this family never produces silently, so it is refused rather than
    ///     clamped to zero. The message states what the padding means, because that is the one
    ///     thing about it a model cannot observe from the picture.
    /// </remarks>
    private const string PaddingMustNotBeNegative =
        "The padding is measured in pixels added around the content, so it must not be negative. "
        + "Supply zero for the tight content region.";

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
    ///     The refusal used when a permitted destination cannot be written.
    /// </summary>
    /// <remarks>
    ///     Deliberately carries no remedy, on the same line
    ///     <see cref="ImageAdmission.FileUnreadable"/> draws: the tool does not know why the
    ///     operating system refused, and a guess would be worse than silence. It is composed here
    ///     rather than in <see cref="ImageDestination"/> because it names <em>what</em> could not
    ///     be written, which only the tool that produced it knows.
    /// </remarks>
    private const string DestinationUnwritable = "The trimmed region could not be written.";

    /// <summary>
    ///     Creates the <c>image_auto_crop</c> tool governed by an access policy.
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
        // tool composes, rather than a framework error raised before the body is reached. For
        // the padding an omitted argument is not a refusal at all — it genuinely means "apply
        // the default" — which is why it is nullable rather than defaulted to 8 directly: a
        // model that supplied a negative value must still be told, and a null and a zero must
        // stay distinguishable.
        var autoCrop = (
                [Description(
                    "The path of the image to trim, relative to the workspace root, "
                    + "for example 'diagram.png'.")]
                string? path = null,
                [Description(
                    "Optional. Pixels of the original background to keep around the content, "
                    + "0 to 256. Defaults to 8. Padding that would run past an edge is clamped "
                    + "to that edge.")]
                int? padding = null,
                [Description(
                    "Optional. The path of a new .png file to write the trimmed region to, "
                    + "relative to the workspace root. Omit it to receive the region as image "
                    + "content instead. An existing file is never replaced.")]
                string? destination = null,
                CancellationToken cancellationToken = default) =>
            AutoCropAsync(policy, path, padding, destination, cancellationToken);

        return GuardedToolFactory.Create(autoCrop, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Trims one image to its content, refusing rather than throwing whenever the request
    ///     cannot be honored.
    /// </summary>
    /// <remarks>
    ///     <b>The order of the checks is the contract, and each step refuses before the next
    ///     allocates anything.</b> It is <see cref="ImageCropTool"/>'s order, with the region's
    ///     shape replaced by the padding's, so a model that has learned one of these tools has
    ///     learned both. The request's own shape is judged first, because a padding outside its
    ///     stated range — or a destination that cannot hold PNG bytes under its own name — is the
    ///     model's mistake and needs no file to diagnose. The policy decision comes before
    ///     anything is learned about the file, so a refused path never reveals whether it exists.
    ///     The destination's file-system checks sit after the source's and before the first byte
    ///     is read: after, because the source is the subject of the request; before the read,
    ///     because a mistyped destination must not cost a file read and a decode. The source
    ///     having been proven to exist by then is what makes a destination equal to the source
    ///     fall into the already-exists refusal, so an image can never be consumed by its own
    ///     trimming.
    /// </remarks>
    /// <param name="policy">The access policy governing the read and any write.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="padding">
    ///     The padding the model asked for, or null when it supplied none and the default
    ///     applies.
    /// </param>
    /// <param name="destination">
    ///     The path to write the region to, or null when the model named none and wants the
    ///     region inline.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The region's content, a confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> AutoCropAsync(
        PathPolicy policy,
        string? path,
        int? padding,
        string? destination,
        CancellationToken cancellationToken)
    {
        // A malformed request is refused rather than thrown: the model supplied it, so the model
        // is the one that must be told how to correct it.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        // An omitted padding is not a refusal — it means "apply the default" — but a padding
        // outside the stated range is a contradiction in the request and needs no file to
        // diagnose, so it is refused before the file system is consulted at all.
        if (padding < 0)
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PaddingMustNotBeNegative);
        }

        if (padding > MaxPadding)
        {
            return ToolResult.Denied(
                DenialReason.InvalidRequest,
                "The padding must be at most " + ImageAdmission.Number(MaxPadding)
                + " pixels. A larger margin than that is not something this tool trims to.");
        }

        // A destination that cannot truthfully hold PNG bytes under its own name is a
        // contradiction in the request rather than a problem with any file, so it is judged here
        // with the other request-shape checks and needs no file system to diagnose.
        if (destination is not null && !ImageDestination.NamesPng(destination))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, ImageDestination.MustBePng);
        }

        // The single read decision. Resolution and containment both happen inside the policy, so
        // a path outside the permitted location is refused here without this tool making any path
        // judgment of its own.
        if (!policy.TryResolveRead(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // A directory has no image content to trim.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsDirectory);
        }

        // The type is decided from the extension before the file system is consulted for size or
        // content, matching the family's documented order. Trimming means decoding, so the set is
        // the family's croppable set and the refusal is the family's own.
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
        ImageDestination.Destination? target = null;
        if (destination is not null
            && !ImageDestination.TryResolve(policy, destination, out target, out var destinationDenial))
        {
            return destinationDenial;
        }

        return await TrimPermittedFileAsync(
                policy,
                realPath,
                mediaType,
                padding ?? DefaultPadding,
                target,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Trims a file already known to exist, to be permitted, and to carry a type a region can
    ///     be taken from.
    /// </summary>
    /// <remarks>
    ///     The read and the triage are the family's shared admission steps, so this tool observes
    ///     exactly the ceilings <see cref="ImageCropTool"/> does and a later correction to either
    ///     reaches both. Everything decidable from the header is decided before the decode, which
    ///     is the first step that allocates pixel data.
    ///     <para>
    ///     <b>Every pixel buffer this method creates is released.</b> The decoded image is
    ///     disposed as soon as the region has been copied out of it, because the copy is
    ///     independent of its source; the region itself is disposed once it has been encoded.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy whose ceilings apply.</param>
    /// <param name="realPath">The real location of the permitted file.</param>
    /// <param name="mediaType">The media type the file's extension resolved to.</param>
    /// <param name="padding">The padding to expand the content region by, already in range.</param>
    /// <param name="destination">
    ///     The permitted destination to write the region to, or null when the region is wanted
    ///     inline.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The region's content, a confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> TrimPermittedFileAsync(
        PathPolicy policy,
        string realPath,
        string mediaType,
        int padding,
        ImageDestination.Destination? destination,
        CancellationToken cancellationToken)
    {
        // The shared admission step reads the file within the binary ceiling, returning either
        // the bytes or the refusal that stopped it.
        var read = await ImageAdmission
            .ReadWithinCeilingAsync(policy, realPath, cancellationToken)
            .ConfigureAwait(false);
        if (read is not byte[] data)
        {
            return read;
        }

        // The shared admission triage: an unreadable header, a well-formed file the decoder will
        // not decode, and a declaration beyond the host's decode budget are all refused here,
        // before a pixel buffer exists.
        if (!ImageAdmission.TryTriage(
                policy, data, mediaType, out var imageWidth, out var imageHeight, out var denial))
        {
            return denial;
        }

        // The shared decode step. A body that will not decode is refused here, with the size
        // stated because it was read.
        if (!ImageAdmission.TryDecode(
                data, mediaType, imageWidth, imageHeight, out var surface, out var decodeDenial))
        {
            return decodeDenial;
        }

        Surface trimmed;
        Region region;

        // The decoded image is released as soon as the region has been copied out of it: the
        // copy is independent of its source, so nothing the region needs outlives the surface it
        // came from.
        using (surface)
        {
            // The whole trimming decision, on pixels, in one synchronous block. A span over the
            // decoded buffer cannot live across an await, and there is no reason for it to.
            var background = SampleBackground(surface);
            if (!TryFindContent(surface, background, out var minX, out var minY, out var maxX, out var maxY))
            {
                // No content pixel anywhere. There is no honest region to return, and returning
                // the whole image would answer a different question while reporting success —
                // the substitution this family exists to refuse. Nothing is written, including
                // when a destination was named and had already passed its governance checks:
                // the write is the last step and is never reached.
                return ToolResult.Denied(
                    DenialReason.InvalidRequest,
                    "Every pixel of this " + ImageAdmission.Size(imageWidth, imageHeight)
                    + " " + mediaType + " image matches the background sampled from its own "
                    + "border, so there is no content region to trim to.");
            }

            // The content box, expanded by the padding and clamped to the image's own edges.
            // Clamping is never an error: content flush to an edge is ordinary, and a padding
            // larger than every margin yields the whole image, which is truthful. The arithmetic
            // cannot overflow, because the padding is at most 256 and the coordinates are
            // bounded by the decoder's own per-axis maximum.
            var left = Math.Max(0, minX - padding);
            var top = Math.Max(0, minY - padding);
            var right = Math.Min(surface.Width - 1, maxX + padding);
            var bottom = Math.Min(surface.Height - 1, maxY + padding);

            region = new Region(left, top, right - left + 1, bottom - top + 1);
            trimmed = surface.Crop(region.X, region.Y, region.Width, region.Height);
        }

        // Outside the block on purpose: the decoded source is already released, so the encode and
        // any write run holding only the region's own copy. Keeping the call inside would hold the
        // whole decoded image alive for the length of an I/O operation that cannot use it.
        return await EncodeRegionAsync(
                policy, trimmed, region, imageWidth, imageHeight, mediaType, padding,
                destination, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Encodes the trimmed region and either returns it inline or writes it to the
    ///     destination.
    /// </summary>
    /// <remarks>
    ///     <b>The region is encoded as PNG whatever the source was.</b> The encoding is lossless,
    ///     so the model receives the source's pixels rather than a re-compressed approximation of
    ///     them, and it is a format every vision provider accepts. The encoder runs outside the
    ///     decode's own <c>catch</c>, which is inside <see cref="ImageAdmission.TryDecode"/>: a
    ///     failure on a pixel buffer this tool constructed is a defect rather than anything a
    ///     model can provoke, and is allowed to propagate.
    ///     <para>
    ///     The inline byte ceiling is applied here and only here. A trimmed region of a
    ///     compressed source, re-encoded losslessly, can genuinely exceed a ceiling the source
    ///     file sat well inside. A region written to a file is not measured against it: that
    ///     ceiling bounds what this family hands a provider, and a file on disk is handed to
    ///     none.
    ///     </para>
    /// </remarks>
    /// <param name="policy">The access policy whose ceilings apply.</param>
    /// <param name="trimmed">The region copied out of the source; disposed here.</param>
    /// <param name="region">The region, as the messages state it.</param>
    /// <param name="imageWidth">The width the image's header declared.</param>
    /// <param name="imageHeight">The height the image's header declared.</param>
    /// <param name="mediaType">The media type the source file's extension resolved to.</param>
    /// <param name="padding">The padding that was applied.</param>
    /// <param name="destination">
    ///     The permitted destination to write the region to, or null when the region is wanted
    ///     inline.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns>The region's content, a confirmation, or a refusal naming its reason.</returns>
    private static async Task<object> EncodeRegionAsync(
        PathPolicy policy,
        Surface trimmed,
        Region region,
        int imageWidth,
        int imageHeight,
        string mediaType,
        int padding,
        ImageDestination.Destination? destination,
        CancellationToken cancellationToken)
    {
        byte[] bytes;

        // The region is released once it has been encoded, which is why the encode precedes the
        // disposal rather than following it.
        using (trimmed)
        {
            using var encoded = new MemoryStream();
            PngCodec.Save(trimmed, encoded, PngColorType.Rgba);
            bytes = encoded.ToArray();
        }

        if (destination is null)
        {
            // The same ceiling governs everything this family hands a provider, whatever
            // produced the bytes.
            if (bytes.Length > policy.Limits.MaxBinaryBytes)
            {
                return ToolResult.Denied(
                    DenialReason.ResourceTooLarge,
                    "The trimmed image is " + ImageAdmission.Number(bytes.Length)
                    + " bytes, which exceeds the "
                    + ImageAdmission.Number(policy.Limits.MaxBinaryBytes)
                    + "-byte binary limit.");
            }

            // The caption states the region the tool chose, the padding it applied and the
            // source's dimensions: the region and the dimensions because the model named neither
            // and has no other way to learn where in the picture it is now looking, and the
            // padding because it is the one input that moved the answer. ToolResult.Image, never
            // ToolResult.Structured: the latter is serialized to JSON by design, which would
            // destroy the content the guarded factory exists to deliver intact.
            return ToolResult.Image(
                bytes,
                ImageMediaTypes.Png,
                "Trimmed region " + region.Describe()
                + " of a " + ImageAdmission.Size(imageWidth, imageHeight) + " " + mediaType
                + " image, with " + ImageAdmission.Number(padding)
                + " pixels of padding, returned as " + ImageMediaTypes.Png + ".");
        }

        // A permitted destination the operating system nonetheless refused. The reason is not
        // known here, and a guess would be worse than silence.
        if (!await ImageDestination.TryWriteAsync(bytes, destination, cancellationToken)
                .ConfigureAwait(false))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, DestinationUnwritable);
        }

        return ToolResult.Text(
            "Wrote the trimmed region " + region.Describe()
            + " of a " + ImageAdmission.Size(imageWidth, imageHeight) + " " + mediaType
            + " image to " + destination.Reported + ".");
    }

    /// <summary>
    ///     Determines the image's background as the modal exact color of its one-pixel perimeter
    ///     ring.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The scan order is fixed and is part of the contract:</b> the top row left to right,
    ///     then the bottom row left to right, then the left column and then the right column,
    ///     each excluding the corners already visited. A value replaces the incumbent only on a
    ///     <em>strictly greater</em> count, so a tie is resolved to whichever value appeared
    ///     first in that order — which makes the answer a pure function of the bytes rather than
    ///     of a dictionary's enumeration order.
    ///     </para>
    ///     <para>
    ///     Colors are compared as packed exact values rather than bucketed, because bucketing
    ///     would introduce a second threshold on top of <see cref="BackgroundTolerance"/> and
    ///     make the result depend on where the bucket boundaries happened to fall. The counting
    ///     dictionary is bounded by the perimeter size, which is at most four times the decoder's
    ///     per-axis maximum.
    ///     </para>
    ///     <para>
    ///     Degenerate sizes are total rather than special-cased: an image one pixel high or one
    ///     pixel wide is entirely its own border, and a one-by-one image makes its single pixel
    ///     the background — so that image is entirely background and is refused by the caller.
    ///     </para>
    /// </remarks>
    /// <param name="surface">The decoded image to sample the border of.</param>
    /// <returns>The background color, as the classifier compares against it.</returns>
    private static Rgba32 SampleBackground(Surface surface)
    {
        var width = surface.Width;
        var height = surface.Height;
        var counts = new Dictionary<uint, int>();
        var best = 0u;
        var bestCount = 0;

        // The top row, left to right.
        var top = surface.GetRowSpan(0);
        for (var x = 0; x < width; x++)
        {
            Tally(top[x], counts, ref best, ref bestCount);
        }

        // The bottom row, left to right, unless it is the row already visited.
        if (height > 1)
        {
            var bottom = surface.GetRowSpan(height - 1);
            for (var x = 0; x < width; x++)
            {
                Tally(bottom[x], counts, ref best, ref bestCount);
            }
        }

        // The left column, top to bottom, excluding the corners already visited.
        for (var y = 1; y < height - 1; y++)
        {
            Tally(surface.GetRowSpan(y)[0], counts, ref best, ref bestCount);
        }

        // The right column, top to bottom, unless it is the column already visited.
        if (width > 1)
        {
            for (var y = 1; y < height - 1; y++)
            {
                Tally(surface.GetRowSpan(y)[width - 1], counts, ref best, ref bestCount);
            }
        }

        return Unpack(best);
    }

    /// <summary>
    ///     Counts one border pixel and promotes it to the incumbent background when it is now
    ///     strictly the most frequent value seen.
    /// </summary>
    /// <remarks>
    ///     The comparison is strictly greater rather than greater-or-equal, which is what makes
    ///     a tie resolve to the earliest position in the caller's fixed scan order.
    /// </remarks>
    /// <param name="pixel">The border pixel to count.</param>
    /// <param name="counts">The running tally of every exact color seen on the border.</param>
    /// <param name="best">The most frequent value so far, updated in place.</param>
    /// <param name="bestCount">The count that value reached, updated in place.</param>
    private static void Tally(Rgba32 pixel, Dictionary<uint, int> counts, ref uint best, ref int bestCount)
    {
        var packed = Pack(pixel);
        counts.TryGetValue(packed, out var count);
        count++;
        counts[packed] = count;

        if (count > bestCount)
        {
            best = packed;
            bestCount = count;
        }
    }

    /// <summary>
    ///     Finds the smallest rectangle containing every pixel that is not background.
    /// </summary>
    /// <remarks>
    ///     <b>Every pixel is visited and there is no early exit.</b> An early exit would make the
    ///     result depend on the traversal order, which it must not. The scan reads whole rows
    ///     through the decoded buffer, so it allocates nothing and costs one comparison per
    ///     channel per pixel.
    /// </remarks>
    /// <param name="surface">The decoded image to scan.</param>
    /// <param name="background">The background the border sampling established.</param>
    /// <param name="minX">On success, the leftmost content column; otherwise undefined.</param>
    /// <param name="minY">On success, the topmost content row; otherwise undefined.</param>
    /// <param name="maxX">On success, the rightmost content column; otherwise undefined.</param>
    /// <param name="maxY">On success, the bottommost content row; otherwise undefined.</param>
    /// <returns>
    ///     <see langword="true"/> when at least one pixel is content; <see langword="false"/>
    ///     when the image is entirely background.
    /// </returns>
    private static bool TryFindContent(
        Surface surface,
        Rgba32 background,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        minX = surface.Width;
        minY = surface.Height;
        maxX = -1;
        maxY = -1;
        var found = false;

        for (var y = 0; y < surface.Height; y++)
        {
            var row = surface.GetRowSpan(y);
            for (var x = 0; x < surface.Width; x++)
            {
                if (IsBackground(row[x], background))
                {
                    continue;
                }

                found = true;
                if (x < minX)
                {
                    minX = x;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }
        }

        return found;
    }

    /// <summary>
    ///     Determines whether one pixel counts as background.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The measure is the per-channel maximum difference, which is the same as requiring
    ///     every channel to be within the tolerance.</b> It is used rather than a Euclidean
    ///     distance for three reasons. Determinism: a Euclidean radius needs a square root or a
    ///     squared comparison, and floating point is not guaranteed bit-identical across runtimes
    ///     and architectures, whereas this is pure integer arithmetic and is exactly reproducible
    ///     everywhere. Plain meaning: it states in one sentence what a reviewer and a model both
    ///     reason in — no channel differs by more than the tolerance. And direction: for a given
    ///     tolerance this is the stricter of the two on multi-channel drift, so a marginal pixel
    ///     is classified as content, and misclassifying background as content only ever
    ///     <em>enlarges</em> the region while the opposite error would lose content.
    ///     </para>
    ///     <para>
    ///     <b>Alpha participates as a fourth channel under the same rule.</b> A fully transparent
    ///     margin around a rendered figure is the most common background in an export with
    ///     transparency, and its RGB is frequently arbitrary — encoders vary, and nothing
    ///     requires it to be zero. If alpha were ignored, a transparent margin whose RGB differed
    ///     from the sampled background would read as content and defeat the trim entirely, which
    ///     is the exact failure this tool exists to prevent. Including it also makes an opaque
    ///     pixel over a transparent background differ by the full range in one channel, so it is
    ///     correctly content.
    ///     </para>
    ///     <para>
    ///     <b>A stated consequence:</b> two pixels that are both fully transparent but carry
    ///     different RGB do not match under this rule. That is deliberate and is not
    ///     special-cased — the effect is to classify such a pixel as content, which enlarges the
    ///     region and never loses anything, and keeping the rule to one sentence is worth more
    ///     than the marginal tightening.
    ///     </para>
    /// </remarks>
    /// <param name="pixel">The pixel to classify.</param>
    /// <param name="background">The background the border sampling established.</param>
    /// <returns>
    ///     <see langword="true"/> when the pixel is within the tolerance of the background on
    ///     every channel; otherwise <see langword="false"/>.
    /// </returns>
    private static bool IsBackground(Rgba32 pixel, Rgba32 background)
    {
        return Math.Abs(pixel.R - background.R) <= BackgroundTolerance
            && Math.Abs(pixel.G - background.G) <= BackgroundTolerance
            && Math.Abs(pixel.B - background.B) <= BackgroundTolerance
            && Math.Abs(pixel.A - background.A) <= BackgroundTolerance;
    }

    /// <summary>
    ///     Packs a pixel into one integer so exact colors can be counted.
    /// </summary>
    /// <remarks>
    ///     The channel order is red, green, blue, alpha from the most significant byte down. Any
    ///     fixed order would do; naming it here is what stops <see cref="Unpack"/> and this
    ///     method drifting apart.
    /// </remarks>
    /// <param name="pixel">The pixel to pack.</param>
    /// <returns>The packed value.</returns>
    private static uint Pack(Rgba32 pixel)
    {
        return ((uint)pixel.R << 24) | ((uint)pixel.G << 16) | ((uint)pixel.B << 8) | pixel.A;
    }

    /// <summary>
    ///     Restores a pixel from the packed form <see cref="Pack"/> produced.
    /// </summary>
    /// <param name="packed">The packed value.</param>
    /// <returns>The pixel.</returns>
    private static Rgba32 Unpack(uint packed)
    {
        return new Rgba32(
            (byte)(packed >> 24),
            (byte)(packed >> 16),
            (byte)(packed >> 8),
            (byte)packed);
    }

    /// <summary>
    ///     The rectangular region this tool chose, once the content box has been found and
    ///     padded.
    /// </summary>
    /// <remarks>
    ///     Carried as one value rather than four loose integers so that the caption and the
    ///     confirmation cannot disagree about which number is which, and so that no method has to
    ///     take four same-typed parameters a caller could transpose. It states itself in the same
    ///     form <see cref="ImageCropTool"/>'s messages state a region, so a model reads one shape
    ///     whichever tool produced it.
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
        /// <returns>The formatted region.</returns>
        public string Describe()
        {
            return ImageAdmission.Number(X) + "," + ImageAdmission.Number(Y)
                + " " + ImageAdmission.Size(Width, Height);
        }
    }
}
