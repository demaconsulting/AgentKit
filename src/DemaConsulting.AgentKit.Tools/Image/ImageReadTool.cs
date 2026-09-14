using System.ComponentModel;
using System.Globalization;
using System.Security;
using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Image;

/// <summary>
///     The <c>image_read</c> tool: returns the visual content of one file the access policy
///     permits the agent to read, as a caption followed by the content itself.
/// </summary>
/// <remarks>
///     <para>
///     <b>The tool takes its policy at construction, and there is no other way to build it.</b>
///     <see cref="Create"/> is the only factory, it requires a <see cref="PathPolicy"/>, and it
///     builds the tool through <see cref="GuardedToolFactory"/>. An unguarded image tool, or one
///     governed by no policy, is therefore unrepresentable rather than merely discouraged.
///     </para>
///     <para>
///     <b>The delegate is declared <c>Task&lt;object&gt;</c> deliberately, and here it carries
///     the whole point of the family.</b> A successful read returns a caption plus binary
///     content — a <see cref="List{T}"/> of <see cref="AIContent"/> — which the underlying
///     function factory would serialize into a <c>JsonElement</c> unless the guarded factory's
///     result passthrough is applied. Serialized, the provider never receives the image, and the
///     model — told a tool returned one — reports that it can see the image and then fabricates a
///     description of content it never received. There is no error to notice. The guarded factory
///     is what makes the content survive to the caller, which is why this declaration must not be
///     "tidied up" into a strongly-typed one; see the remarks on <see cref="GuardedToolFactory"/>.
///     </para>
///     <para>
///     <b>A path the model supplies is read relative to the workspace root.</b> The access policy
///     holds the workspace a bare name is resolved against, and an absolute path remains
///     expressible and remains subject to the same containment decision. The path parameter
///     carries a default, so an omitted argument becomes a refusal stating what to supply rather
///     than a framework error the model cannot interpret.
///     </para>
///     <para>
///     <b>An oversized file is a denial naming the ceiling, never a truncation.</b> Returning
///     part of an image would hand the model corrupt content it has no way to detect. A denial
///     that names the ceiling lets the model narrow its request instead.
///     </para>
///     <para>
///     Every refusal is returned rather than thrown. A refusal this tool composes itself — an
///     unsupported media type, or an oversized image naming the ceiling — interpolates only an
///     integer or the resolved media type. A refusal the access policy produces, by contrast, states
///     what was requested, how a relative request was interpreted, and which locations are permitted,
///     so a confined model is told where it may look instead of being left to guess. Each refusal
///     states a fact and stops: it does not prescribe a course of action, because a denial that
///     suggested one was measured pushing a model into a destructive workaround the user had
///     explicitly forbidden. Naming a sibling tool remains permissible only where doing so states
///     what the file <em>is</em> rather than offering a way around the refusal — see
///     <see cref="ImageMediaTypes"/>.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads;
///     a constructed tool captures only the immutable policy it was given.
///     </para>
/// </remarks>
public static class ImageReadTool
{
    /// <summary>
    ///     The name this tool is published under.
    /// </summary>
    /// <remarks>
    ///     Published as a constant on the unit that defines the tool so that a caller naming it
    ///     — most importantly a sibling tool redirecting a model to it — cannot drift from the
    ///     name actually registered.
    /// </remarks>
    public const string ToolName = "image_read";

    /// <summary>
    ///     The description the model reads when choosing this tool.
    /// </summary>
    private const string ToolDescription =
        "Reads the visual content of an image or PDF file the agent is permitted to read. Paths "
        + "are relative to the workspace root. Returns the content with a caption, or a denial "
        + "explaining why the request was refused.";

    /// <summary>
    ///     The refusal used when the request carries no path at all.
    /// </summary>
    private const string PathRequired =
        "A file path is required. Supply a path relative to the workspace root, "
        + "for example 'diagram.png'.";

    /// <summary>
    ///     The refusal used when the request names a directory rather than a file.
    /// </summary>
    /// <remarks>
    ///     The refusal states the fact and stops. A denial that prescribes another course of action
    ///     was measured pushing a model into a workaround the user had forbidden, so a denial in
    ///     this library says what is so and leaves the choice of what to do next to the model.
    /// </remarks>
    private const string PathIsDirectory =
        "The requested path is a directory, not a file.";

    /// <summary>
    ///     The refusal used when the requested file does not exist.
    /// </summary>
    /// <remarks>
    ///     States the fact and stops, for the same reason as <see cref="PathIsDirectory"/>.
    /// </remarks>
    private const string FileNotFound =
        "The requested file does not exist.";

    /// <summary>
    ///     The refusal used when the file exists and is permitted but cannot be read.
    /// </summary>
    private const string FileUnreadable = "The requested file could not be read.";

    /// <summary>
    ///     The fixed opening of the caption naming what the model is being handed.
    /// </summary>
    private const string CaptionPrefix = "File content of media type ";

    /// <summary>
    ///     Creates the <c>image_read</c> tool governed by an access policy.
    /// </summary>
    /// <remarks>
    ///     Internal rather than public because a pack is the unit of attachment: an application
    ///     obtains this tool by attaching <see cref="ImagePack"/>, which is the only place the
    ///     <c>image</c> family prefix is claimed. The policy is captured by the returned tool's
    ///     delegate, so the tool cannot later be pointed at a different policy.
    /// </remarks>
    /// <param name="policy">The access policy governing every read this tool performs.</param>
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
        // Here the guarded delivery is what keeps the image content from being serialized to
        // JSON. The path carries a default so that an omitted argument becomes a refusal this
        // tool composes, rather than a framework error raised before the body is reached.
        var read = (
                [Description(
                    "The path of the file to read, relative to the workspace root, "
                    + "for example 'diagram.png'.")]
                string? path = null,
                CancellationToken cancellationToken = default) =>
            ReadAsync(policy, path, cancellationToken);

        return GuardedToolFactory.Create(read, ToolName, ToolDescription);
    }

    /// <summary>
    ///     Reads one file, refusing rather than throwing whenever the request cannot be honored.
    /// </summary>
    /// <remarks>
    ///     The order of the checks is the contract: the policy decision comes before anything is
    ///     learned about the file, so a refused path never reveals whether it exists. The
    ///     ceiling is then checked before any content is returned, so a model is refused rather
    ///     than handed a partial image.
    /// </remarks>
    /// <param name="policy">The access policy governing the read.</param>
    /// <param name="path">The path the model requested, or null when it supplied none.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The file's content, or a refusal naming its reason.</returns>
    private static async Task<object> ReadAsync(
        PathPolicy policy,
        string? path,
        CancellationToken cancellationToken)
    {
        // A malformed request is refused rather than thrown: the model supplied it, so the model
        // is the one that must be told how to correct it.
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathRequired);
        }

        // The single read decision. Resolution and containment both happen inside the policy, so a
        // path outside the permitted location, or one a deny pattern excludes, is refused here
        // without this tool making any path judgment of its own.
        if (!policy.TryResolveRead(path, out var realPath, out var denialMessage))
        {
            return ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage);
        }

        // A directory has no visual content to return, and this family has no listing tool to
        // redirect to, so the refusal simply names the mistake.
        if (Directory.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.InvalidRequest, PathIsDirectory);
        }

        // The type is decided from the extension before the file system is consulted for size or
        // content. An unsupported type is refused — stating what the file is, which for an .svg
        // means naming the text reader — rather
        // than a file being read only to be discarded.
        if (!ImageMediaTypes.TryResolveMediaType(realPath, out var mediaType))
        {
            return ImageMediaTypes.DenyUnsupportedType(realPath);
        }

        // A missing file is refused as such; there is no listing tool in this family to name.
        // System.IO.File is qualified because the sibling File tool family occupies the unqualified
        // 'File' name within this assembly.
        if (!System.IO.File.Exists(realPath))
        {
            return ToolResult.Denied(DenialReason.TargetNotFound, FileNotFound);
        }

        return await ReadPermittedFileAsync(policy, realPath, mediaType, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads a file already known to exist, to be permitted, and to carry a supported type,
    ///     observing the policy's binary ceiling.
    /// </summary>
    /// <remarks>
    ///     The binary ceiling is the single budget that applies to content this tool returns:
    ///     unlike text, an image is not charged against the context window by the same route, so
    ///     a byte ceiling is the right control. Size is judged before the file is opened, so an
    ///     oversized file is never read into memory merely to discover it was oversized.
    /// </remarks>
    /// <param name="policy">The access policy whose ceiling applies.</param>
    /// <param name="realPath">The real location of the permitted file.</param>
    /// <param name="mediaType">The media type the file's extension resolved to.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The file's content, or a refusal naming the ceiling it exceeded.</returns>
    private static async Task<object> ReadPermittedFileAsync(
        PathPolicy policy,
        string realPath,
        string mediaType,
        CancellationToken cancellationToken)
    {
        try
        {
            // Size is judged before the file is opened, so an oversized file is never read into
            // memory merely to discover it was oversized.
            var length = new FileInfo(realPath).Length;
            if (length > policy.Limits.MaxBinaryBytes)
            {
                return ToolResult.Denied(
                    DenialReason.ResourceTooLarge,
                    "The file exceeds the "
                    + policy.Limits.MaxBinaryBytes.ToString(CultureInfo.InvariantCulture)
                    + "-byte binary limit.");
            }

            var data = await System.IO.File.ReadAllBytesAsync(realPath, cancellationToken)
                .ConfigureAwait(false);
            var caption = CaptionPrefix + mediaType + ".";

            // A PDF is not an image/ media type, so it is returned through Binary, whose guard
            // accepts any media type; the image types are returned through Image, whose guard
            // insists on the image/ prefix. Both produce the identical caption-plus-content
            // shape the guarded factory must deliver unserialized.
            return IsImageMediaType(mediaType)
                ? ToolResult.Image(data, mediaType, caption)
                : ToolResult.Binary(data, mediaType, caption);
        }
        catch (Exception exception) when (IsAccessFailure(exception))
        {
            // The same explicit classification the policy uses: a file system failure becomes a
            // refusal the model can act on, while a genuine defect still surfaces.
            return ToolResult.Denied(DenialReason.InvalidRequest, FileUnreadable);
        }
    }

    /// <summary>
    ///     Determines whether a media type denotes an image rather than another kind of content.
    /// </summary>
    /// <remarks>
    ///     The dispatch between <see cref="ToolResult.Image"/> and <see cref="ToolResult.Binary"/>
    ///     is made on the <c>image/</c> prefix rather than on the specific type, so a type added
    ///     to <see cref="ImageMediaTypes"/> later is routed correctly without this method
    ///     changing — and it is the same prefix <see cref="ToolResult.Image"/> itself guards on.
    /// </remarks>
    /// <param name="mediaType">The resolved media type.</param>
    /// <returns>
    ///     <see langword="true"/> when the media type denotes an image; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static bool IsImageMediaType(string mediaType)
    {
        return mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
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
}
