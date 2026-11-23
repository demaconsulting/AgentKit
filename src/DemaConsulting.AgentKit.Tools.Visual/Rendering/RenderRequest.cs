using DemaConsulting.AgentKit.Core;
using DemaConsulting.CanvasNet.Files;

namespace DemaConsulting.AgentKit.Tools.Visual.Rendering;

/// <summary>
///     One render a visual tool asks the renderer for: the bytes of a document or diagram, the
///     name its format is chosen by, and the confinement and limits it is drawn under.
/// </summary>
/// <remarks>
///     <para>
///     <b>The content is always bytes the tool already holds.</b> A file is read by the tool,
///     inside the policy's binary ceiling and after the policy permitted it, and the same bytes
///     are drawn; the library is never given the input's path. That keeps the bytes judged and
///     the bytes drawn identical, and keeps the input's absolute path out of every message the
///     library composes: the library names the content only by <see cref="FileName"/>.
///     </para>
///     <para>
///     The record is immutable and is therefore safe to hand to the thread that renders.
///     </para>
/// </remarks>
internal sealed record RenderRequest
{
    /// <summary>
    ///     Gets the bytes of the document or diagram to draw.
    /// </summary>
    public required byte[] Content { get; init; }

    /// <summary>
    ///     Gets the file name the content is drawn as; its extension chooses the format, and it is
    ///     the only name the library's messages use for the content.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    ///     Gets the folder relative references are resolved against, or <see langword="null"/>
    ///     when every relative reference must fail closed.
    /// </summary>
    public string? Folder { get; init; }

    /// <summary>
    ///     Gets the file-access policy every referenced file is judged by.
    /// </summary>
    public required FileAccessPolicy Access { get; init; }

    /// <summary>
    ///     Gets the policy limits the render observes: the pixel budget and the binary ceiling.
    /// </summary>
    public required ToolLimits Limits { get; init; }

    /// <summary>
    ///     Gets the width, in pixels, the caller asked for, or <see langword="null"/> for the
    ///     renderer's own sizing.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    ///     Gets the 1-based page, slide or sheet to draw. Every format without pages has only
    ///     page 1.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    ///     Gets a value indicating whether the outcome should describe the picture in words: its
    ///     features and its dominant colors.
    /// </summary>
    public bool Describe { get; init; }
}
