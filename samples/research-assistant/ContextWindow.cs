using DemaConsulting.AgentKit.Agents.Ollama;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant;

/// <summary>
///     Where the context window a compacting session is accounted against came from.
/// </summary>
/// <remarks>
///     Reported in the startup banner rather than kept private, because the window is the one
///     number the whole compaction arrangement turns on: a session told a window twice the truth
///     will not rotate until the provider has already truncated, and a reader cannot tell the two
///     apart from the outside. Naming the source makes the difference visible before a run starts.
///     <para>
///     This is the sample's own banner vocabulary, covering both providers it supports, rather than
///     the shipped Ollama package's discovery vocabulary. It carries one member the package can
///     never produce — <see cref="Ceiling"/>, which belongs to the Copilot path — so keeping it here
///     is what lets the package publish only what Ollama discovery can actually report, and what
///     keeps the Copilot path from ever naming an Ollama type: the conversion lives on
///     <c>ContextWindow.From</c>, which that path never calls.
///     </para>
/// </remarks>
public enum ContextWindowSource
{
    /// <summary>
    ///     The application stated it on the command line and asked the server to run at it, which
    ///     settles the question outright.
    /// </summary>
    Stated,

    /// <summary>
    ///     Read from the loaded model, which is the window the server will actually enforce.
    /// </summary>
    LoadedModel,

    /// <summary>
    ///     Nothing could be read about the instance, so Ollama's own default context length is
    ///     assumed.
    /// </summary>
    Assumed,

    /// <summary>
    ///     Stated on the command line as an upper bound only, on a provider that answers for its own
    ///     window with every turn.
    /// </summary>
    /// <remarks>
    ///     Distinct from <see cref="Stated"/> because it is not a claim about the window the session
    ///     will actually account against. The runtime's own limit governs wherever it is lower, and
    ///     that figure is not known until the first turn reports it — so a banner that printed this
    ///     number as the window would be stating something that may never be true.
    /// </remarks>
    Ceiling,
}

/// <summary>
///     The context window one run accounts its conversation against, and where the figure came
///     from.
/// </summary>
/// <param name="Tokens">The window in tokens. Always positive.</param>
/// <param name="Source">Where the figure was obtained.</param>
public sealed record ContextWindow(int Tokens, ContextWindowSource Source)
{
    /// <summary>
    ///     Describes the window and its provenance in one line for the startup banner.
    /// </summary>
    /// <returns>A sentence naming the number and how it was arrived at.</returns>
    public string Describe() => Source switch
    {
        ContextWindowSource.Stated =>
            $"{Tokens} tokens (stated with --context-window, and asked of the server on every "
            + "request)",
        ContextWindowSource.LoadedModel =>
            $"{Tokens} tokens (read from the loaded model, and asked of the server on every request "
            + "so the instance stays at it)",
        ContextWindowSource.Ceiling =>
            $"at most {Tokens} tokens (a --context-window ceiling; the runtime reports its own "
            + "window every turn and the lower of the two governs)",
        _ =>
            $"{Tokens} tokens (assumed: no model was loaded to ask, so this is Ollama's own default "
            + "asked for on every request; pass --context-window to run at a size of your choosing)",
    };

    /// <summary>
    ///     Converts what the Ollama package discovered into the banner's own vocabulary.
    /// </summary>
    /// <remarks>
    ///     The shipped package reports only what Ollama discovery can produce, so each of its three
    ///     sources has exactly one counterpart here; the banner's fourth,
    ///     <see cref="ContextWindowSource.Ceiling"/>, is produced by the Copilot path alone and
    ///     never arrives through this method. That is what lets the Ollama composition ask the
    ///     server for whatever window this method returns without inspecting its source: a ceiling
    ///     bounds another provider's window rather than naming one, and it cannot reach here.
    /// </remarks>
    /// <param name="discovered">The window the package read. Must not be <see langword="null"/>.</param>
    /// <returns>The same figure, sourced in the banner's terms.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="discovered"/> is <see langword="null"/>.</exception>
    public static ContextWindow From(OllamaContextWindow discovered)
    {
        ArgumentNullException.ThrowIfNull(discovered);

        return new ContextWindow(
            discovered.Tokens,
            discovered.Source switch
            {
                OllamaContextWindowSource.Stated => ContextWindowSource.Stated,
                OllamaContextWindowSource.LoadedModel => ContextWindowSource.LoadedModel,
                _ => ContextWindowSource.Assumed,
            });
    }
}
