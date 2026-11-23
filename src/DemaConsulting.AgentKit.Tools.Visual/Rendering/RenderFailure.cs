namespace DemaConsulting.AgentKit.Tools.Visual.Rendering;

/// <summary>
///     Why a render failed, classified and located so that the failure can be told to a model in
///     a form it can act on.
/// </summary>
/// <remarks>
///     Produced by <see cref="RenderErrorShaper.Classify"/> from the exception a render threw, or
///     by the renderer itself for a timeout and for a picture it promotes to a failure. It holds
///     the library's own wording, cleaned of its framing; host paths are removed only when the
///     failure is shaped into the text the model receives, because only the caller knows which
///     folders are the host's. The record is immutable.
/// </remarks>
/// <param name="Code">
///     The stable failure code, one of the library's error codes or <c>render-timeout</c> or
///     <c>empty-render</c>.
/// </param>
/// <param name="Detail">A finer code the library gave, such as <c>mermaid-syntax-error</c>, or null.</param>
/// <param name="Message">The failure in the library's words, without its framing or location prefix.</param>
/// <param name="Hint">The library's suggestion, or null.</param>
/// <param name="File">The file name the location refers to, as the library reported it, or null.</param>
/// <param name="Line">The 1-based line the failure points at, or null.</param>
/// <param name="Column">The 1-based column the failure points at, or null.</param>
/// <param name="Pointer">The JSON pointer the failure points at, or null.</param>
/// <param name="Notes">Every note the render reported before it failed, already formatted.</param>
internal sealed record RenderFailure(
    string Code,
    string? Detail,
    string Message,
    string? Hint,
    string? File,
    int? Line,
    int? Column,
    string? Pointer,
    IReadOnlyList<string> Notes);
