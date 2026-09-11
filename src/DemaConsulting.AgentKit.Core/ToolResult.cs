using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Why a tool refused to perform the operation it was asked to perform.
/// </summary>
/// <remarks>
///     There is deliberately no zero member. <c>default(DenialReason)</c> is therefore not a
///     valid reason and <see cref="ToolResult.Denied"/> rejects it: a refusal with an
///     unspecified reason is a bug, and the absence of a zero member makes that bug
///     unrepresentable rather than merely discouraged.
/// </remarks>
public enum DenialReason
{
    /// <summary>
    ///     The requested path lies outside the location the host permitted.
    /// </summary>
    PathNotPermitted = 1,

    /// <summary>
    ///     The requested resource exceeds a ceiling the host configured.
    /// </summary>
    ResourceTooLarge = 2,

    /// <summary>
    ///     The resource's media type is not one this tool can handle.
    /// </summary>
    UnsupportedMediaType = 3,

    /// <summary>
    ///     The requested target does not exist.
    /// </summary>
    TargetNotFound = 4,

    /// <summary>
    ///     The host does not provide the capability this operation requires.
    /// </summary>
    HostCapabilityUnavailable = 5,

    /// <summary>
    ///     The request itself is malformed or self-contradictory.
    /// </summary>
    InvalidRequest = 6
}

/// <summary>
///     Constructs the results a guarded tool returns to the model.
/// </summary>
/// <remarks>
///     <para>
///     <b>Every member returns <see cref="object"/>, and that is not laziness.</b> A tool
///     returns a union — a refusal, or text, or content — and <see cref="object"/> is the only
///     type that expresses it. That is also precisely the declared return type the underlying
///     function factory would serialize to JSON, which is why
///     <see cref="GuardedToolFactory"/> exists and why it is the only supported way to build a
///     tool from these results. A tool whose result is constructed here but that is not built
///     through that factory will have its result flattened into JSON before the provider ever
///     sees it.
///     </para>
///     <para>
///     <b>A refusal is a return value, not an exception.</b> An exception raised while a model
///     is calling a tool ends the agent's turn and strands it with no way forward, whereas a
///     returned refusal lets the model read the reason and choose a permitted alternative. Only
///     programming errors — a null text, an invalid media type, an undefined reason — are
///     reported as exceptions. This is the same dividing line <see cref="PathPolicy"/> draws.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
public static class ToolResult
{
    /// <summary>
    ///     The fixed prefix introducing a refusal.
    /// </summary>
    private const string DenialPrefix = "Denied";

    /// <summary>
    ///     The fixed opening of the sentence directing the model to a more appropriate tool.
    /// </summary>
    private const string RedirectPrefix = " Use the '";

    /// <summary>
    ///     The fixed close of the sentence directing the model to a more appropriate tool.
    /// </summary>
    private const string RedirectSuffix = "' tool instead.";

    /// <summary>
    ///     Constructs a text result.
    /// </summary>
    /// <remarks>
    ///     Text is returned as the plain string itself rather than wrapped in a content object,
    ///     because that is what every runtime already knows how to present to a model, and
    ///     wrapping it would add a layer nothing benefits from.
    /// </remarks>
    /// <param name="text">
    ///     The text to return. Must be non-null. An empty string is permitted — an empty file
    ///     is a legitimate read result, and reporting it as a refusal would be a lie.
    /// </param>
    /// <returns>The supplied text.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="text"/> is <see langword="null"/>.
    /// </exception>
    public static object Text(string text)
    {
        // A missing text is a programming error in the tool; an empty one is ordinary data.
        ArgumentNullException.ThrowIfNull(text);

        return text;
    }

    /// <summary>
    ///     Constructs a binary content result, optionally preceded by a caption.
    /// </summary>
    /// <remarks>
    ///     A caption is returned as a separate text part ahead of the content rather than being
    ///     folded into the content itself, because the model needs to be told what it is being
    ///     handed before it is handed it, and because the content part must remain a pure
    ///     attachment the provider can recognize.
    /// </remarks>
    /// <param name="data">The content bytes.</param>
    /// <param name="mediaType">
    ///     The media type of the content, for example <c>application/pdf</c>. Must be non-null
    ///     and non-empty.
    /// </param>
    /// <param name="caption">
    ///     An optional caption describing the content. When null or empty, the content is
    ///     returned on its own.
    /// </param>
    /// <returns>
    ///     A <see cref="DataContent"/> when no caption is supplied; otherwise a
    ///     <see cref="List{T}"/> of <see cref="AIContent"/> holding the caption followed by the
    ///     content.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="mediaType"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="mediaType"/> is an empty string.
    /// </exception>
    public static object Binary(ReadOnlyMemory<byte> data, string mediaType, string? caption = null)
    {
        // Content with no media type cannot be interpreted by a provider, so an absent one is a
        // programming error rather than something to guess at.
        ArgumentException.ThrowIfNullOrEmpty(mediaType);

        var content = new DataContent(data, mediaType);

        // Without a caption the content stands alone; the single-part shape keeps the common
        // case free of an enclosing list nothing would read.
        if (string.IsNullOrEmpty(caption))
        {
            return content;
        }

        // Caption first, content second: the order is the contract, because the model reads the
        // parts in order and must know what the attachment is before it reaches it.
        return new List<AIContent> { new TextContent(caption), content };
    }

    /// <summary>
    ///     Constructs an image result, optionally preceded by a caption.
    /// </summary>
    /// <remarks>
    ///     Delegates to <see cref="Binary"/> once the media type has been confirmed to denote
    ///     an image. The extra check is not ceremony: a caption attached to bytes that are not
    ///     an image would tell the model it is looking at a picture when it is not, and the
    ///     model has no way to discover otherwise.
    /// </remarks>
    /// <param name="data">The image bytes.</param>
    /// <param name="mediaType">
    ///     The media type of the image, for example <c>image/png</c>. Must be non-null,
    ///     non-empty, and must denote an image.
    /// </param>
    /// <param name="caption">
    ///     An optional caption describing the image. When null or empty, the image is returned
    ///     on its own.
    /// </param>
    /// <returns>
    ///     A <see cref="DataContent"/> when no caption is supplied; otherwise a
    ///     <see cref="List{T}"/> of <see cref="AIContent"/> holding the caption followed by the
    ///     image.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="mediaType"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="mediaType"/> is an empty string or does not denote an
    ///     image.
    /// </exception>
    public static object Image(ReadOnlyMemory<byte> data, string mediaType, string? caption = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(mediaType);

        // Refuse to describe non-image bytes as an image; the model cannot detect the mistake.
        if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The media type must denote an image, for example 'image/png'.",
                nameof(mediaType));
        }

        return Binary(data, mediaType, caption);
    }

    /// <summary>
    ///     Constructs a refusal result naming its reason and, where useful, a better tool.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The library contributes no host detail of its own.</b> The composed refusal
    ///     consists of a fixed prefix, the name of the supplied <see cref="DenialReason"/>, the
    ///     <paramref name="message"/> the caller supplied, and — when a redirect is given — a
    ///     fixed sentence naming it. Nothing about the host is added here. The refusal text is
    ///     handed to a model and the resulting transcript leaves this process, so the only way
    ///     host layout can reach it is through <paramref name="message"/>; callers keep that
    ///     discipline by composing messages from constants, as <see cref="PathPolicy"/> does.
    ///     </para>
    ///     <para>
    ///     The redirect exists because an agent told only "no" will retry the same tool, while
    ///     an agent told which tool to use instead makes progress.
    ///     </para>
    /// </remarks>
    /// <param name="reason">
    ///     The reason for the refusal. Must be a defined <see cref="DenialReason"/> member;
    ///     <c>default</c> is not one.
    /// </param>
    /// <param name="message">
    ///     The explanation handed to the model. Must be non-null and non-empty, and must carry
    ///     no host detail.
    /// </param>
    /// <param name="redirectToolName">
    ///     The name of a tool better suited to the request, or <see langword="null"/> when
    ///     there is none. When supplied, it must be a valid tool name.
    /// </param>
    /// <returns>The composed refusal text.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="reason"/> is not a defined member.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="message"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="message"/> is empty, or when
    ///     <paramref name="redirectToolName"/> is not a valid tool name.
    /// </exception>
    public static object Denied(DenialReason reason, string message, string? redirectToolName = null)
    {
        // An unspecified reason is a bug in the tool, not a refusal a model should be shown.
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "The denial reason must be a defined DenialReason member.");
        }

        // A reason with no explanation leaves the model nothing to act on.
        ArgumentException.ThrowIfNullOrEmpty(message);

        // Compose the fixed part. The reason contributes only its member name, and the member
        // names are invariant identifiers, so no culture-sensitive formatting is involved.
        var composed = DenialPrefix + " (" + reason.ToString() + "): " + message;

        // A redirect to a name no tool could legally carry is caught here rather than being
        // handed to the model, which would then ask for a tool that cannot exist.
        if (redirectToolName is not null)
        {
            ToolName.Validate(redirectToolName);
            composed += RedirectPrefix + redirectToolName + RedirectSuffix;
        }

        return composed;
    }
}
