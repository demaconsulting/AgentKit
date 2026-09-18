using System.Text.Json;
using OllamaSharp;
using OllamaSharp.Models;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant;

/// <summary>
///     Where the context window a compacting session is accounted against came from.
/// </summary>
/// <remarks>
///     Reported in the startup banner rather than kept private, because the window is the one
///     number the whole compaction arrangement turns on: a session told a window twice the truth
///     will not rotate until the provider has already truncated, and a reader cannot tell the two
///     apart from the outside. Naming the source makes the difference visible before a run starts.
/// </remarks>
public enum ContextWindowSource
{
    /// <summary>
    ///     The application stated it on the command line, which settles the question outright.
    /// </summary>
    Stated,

    /// <summary>
    ///     Read from the loaded model, which is the window the server will actually enforce.
    /// </summary>
    LoadedModel,

    /// <summary>
    ///     Read from the model's published metadata: its maximum, which the server may have loaded
    ///     it below.
    /// </summary>
    PublishedModel,

    /// <summary>
    ///     Nothing could be read, so Ollama's own default context length is assumed.
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
            $"{Tokens} tokens (stated with --context-window)",
        ContextWindowSource.LoadedModel =>
            $"{Tokens} tokens (read from the loaded model, so this is what the server enforces)",
        ContextWindowSource.PublishedModel =>
            $"{Tokens} tokens (the model's published maximum; the server may have loaded it "
            + "smaller, so pass --context-window if it did)",
        ContextWindowSource.Ceiling =>
            $"at most {Tokens} tokens (a --context-window ceiling; the runtime reports its own "
            + "window every turn and the lower of the two governs)",
        _ =>
            $"{Tokens} tokens (assumed: Ollama reported nothing, and this is its own default)",
    };
}

/// <summary>
///     Reads the context window a compacting session should be accounted against from an Ollama
///     server.
/// </summary>
/// <remarks>
///     <para>
///     <b>This exists because an <c>IChatClient</c> publishes no context window, and AgentKit
///     refuses to guess one.</b> <c>ChatClientProviderSessionFactory</c> is told the window once,
///     where the application configures its provider, and answers with it thereafter. So the
///     application has to obtain it — and for Ollama it can, which is why this sample reads it
///     rather than hard-coding a number and hoping.
///     </para>
///     <para>
///     <b>Two figures exist and they are not the same figure.</b> A model publishes the context
///     length it was trained for, and Ollama loads it with a context length of its own choosing,
///     which is smaller by default. The loaded figure is the one the server enforces, so it is
///     preferred; the published figure is used only when nothing is loaded, and the banner says so
///     rather than presenting a maximum as though it were the limit in force.
///     </para>
///     <para>
///     The selection is a pure function of what the server reported, kept separate from the reading
///     of it, so the precedence can be tested without a server.
///     </para>
/// </remarks>
public static class OllamaContextWindow
{
    /// <summary>
    ///     The context length Ollama loads a model with when nothing configures otherwise.
    /// </summary>
    /// <remarks>
    ///     Used only when the server answered nothing useful. It is deliberately the conservative
    ///     figure: assuming less than the truth makes a session rotate earlier than it needed to,
    ///     which costs summarizer calls, while assuming more makes it rotate after the provider has
    ///     already discarded the beginning of the conversation, which loses history silently.
    /// </remarks>
    public const int AssumedTokens = 4096;

    /// <summary>
    ///     The metadata key, beneath the architecture name, carrying a model's published context
    ///     length.
    /// </summary>
    /// <remarks>
    ///     Ollama reports model metadata keyed by architecture — <c>llama.context_length</c>,
    ///     <c>qwen3.context_length</c> and so on — so the architecture the same response names is
    ///     what makes the key addressable without knowing the model.
    /// </remarks>
    private const string ContextLengthKeySuffix = ".context_length";

    /// <summary>
    ///     Reads the window from an Ollama server, preferring what a stated option says and then
    ///     what the server reports.
    /// </summary>
    /// <remarks>
    ///     Neither query is required to succeed. A server that refuses either — an older build, a
    ///     proxy, a model that has never been loaded — yields a window from the next source down
    ///     rather than a failed run, because a sample that cannot start because it could not read
    ///     an optional number would be worse than one that says which number it assumed.
    /// </remarks>
    /// <param name="client">The Ollama client to ask. Must not be <see langword="null"/>.</param>
    /// <param name="model">The model the conversation runs on. Must not be <see langword="null"/>.</param>
    /// <param name="stated">
    ///     The window stated on the command line, or <see langword="null"/> when none was.
    /// </param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The window to account against, and where it came from.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="client"/> or <paramref name="model"/> is <see langword="null"/>.
    /// </exception>
    public static async Task<ContextWindow> ReadAsync(
        IOllamaApiClient client,
        string model,
        int? stated,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(model);

        // A stated window settles it, and asking the server anyway would only invite a reader to
        // wonder which answer won.
        if (stated is > 0)
        {
            return Select(stated, running: null, published: null, model);
        }

        var running = await TryReadAsync(
            () => client.ListRunningModelsAsync(cancellationToken));

        var published = await TryReadAsync(
            () => client.ShowModelAsync(new ShowModelRequest { Model = model }, cancellationToken));

        return Select(stated, running, published, model);
    }

    /// <summary>
    ///     Chooses the window from what was stated and what the server reported.
    /// </summary>
    /// <remarks>
    ///     The whole precedence in one pure function, so it can be exercised without a server: a
    ///     stated window, then the loaded model's enforced length, then the model's published
    ///     maximum, then Ollama's own default.
    /// </remarks>
    /// <param name="stated">The stated window, or <see langword="null"/>.</param>
    /// <param name="running">The models the server reports as loaded, or <see langword="null"/>.</param>
    /// <param name="published">The model's published metadata, or <see langword="null"/>.</param>
    /// <param name="model">The model name to match a loaded model against.</param>
    /// <returns>The chosen window and its source.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public static ContextWindow Select(
        int? stated,
        IEnumerable<RunningModel>? running,
        ShowModelResponse? published,
        string model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (stated is > 0)
        {
            return new ContextWindow(stated.Value, ContextWindowSource.Stated);
        }

        var loaded = FromLoadedModel(running, model);
        if (loaded > 0)
        {
            return new ContextWindow(loaded, ContextWindowSource.LoadedModel);
        }

        var maximum = FromPublishedModel(published);
        if (maximum > 0)
        {
            return new ContextWindow(maximum, ContextWindowSource.PublishedModel);
        }

        return new ContextWindow(AssumedTokens, ContextWindowSource.Assumed);
    }

    /// <summary>
    ///     Finds the context length the named model is currently loaded with.
    /// </summary>
    /// <remarks>
    ///     Ollama names a loaded model with its tag — <c>qwen3.5:9b</c> — while a command line may
    ///     omit the tag, which the server resolves to <c>latest</c>. Both forms are compared so a
    ///     run started with a bare model name still finds its own loaded model.
    /// </remarks>
    /// <param name="running">The loaded models, or <see langword="null"/> when none were reported.</param>
    /// <param name="model">The model name the conversation runs on.</param>
    /// <returns>The loaded context length, or zero when the model is not loaded.</returns>
    private static int FromLoadedModel(IEnumerable<RunningModel>? running, string model)
    {
        if (running is null)
        {
            return 0;
        }

        var wanted = Tagged(model);

        var match = running.FirstOrDefault(
            loaded => Matches(loaded.Name) || Matches(loaded.ModelName));

        return match?.ContextLength ?? 0;

        bool Matches(string? name) =>
            name is not null && string.Equals(Tagged(name), wanted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Reads the published context length from a model's metadata.
    /// </summary>
    /// <remarks>
    ///     The value arrives as whatever the JSON carried, so every plausible numeric shape is
    ///     accepted rather than one being assumed; an unreadable value yields zero and the next
    ///     source down is used.
    /// </remarks>
    /// <param name="published">The model metadata, or <see langword="null"/>.</param>
    /// <returns>The published context length, or zero when it could not be read.</returns>
    private static int FromPublishedModel(ShowModelResponse? published)
    {
        var architecture = published?.Info?.Architecture;
        var extra = published?.Info?.ExtraInfo;

        if (string.IsNullOrEmpty(architecture) || extra is null)
        {
            return 0;
        }

        if (!extra.TryGetValue(architecture + ContextLengthKeySuffix, out var value))
        {
            return 0;
        }

        return AsTokenCount(value);
    }

    /// <summary>
    ///     Converts a metadata value to a token count.
    /// </summary>
    /// <remarks>
    ///     A JSON number deserialized into <see cref="object"/> may arrive as a
    ///     <see cref="JsonElement"/>, as an integer, or as text, depending on how the response was
    ///     materialized. Converting rather than casting is what keeps this from being a silent zero
    ///     whenever a serializer changes its mind.
    /// </remarks>
    /// <param name="value">The metadata value, which may be <see langword="null"/>.</param>
    /// <returns>The token count, or zero when the value is not a usable number.</returns>
    private static int AsTokenCount(object? value)
    {
        switch (value)
        {
            case null:
                return 0;

            case JsonElement element:
                return element.ValueKind == JsonValueKind.Number
                       && element.TryGetInt32(out var fromJson)
                    ? fromJson
                    : 0;

            case int integer:
                return integer;

            case long wide:
                return wide is > 0 and <= int.MaxValue ? (int)wide : 0;

            case double real:
                return real is > 0 and <= int.MaxValue ? (int)real : 0;

            case string text:
                return int.TryParse(text, out var parsed) ? parsed : 0;

            default:
                return 0;
        }
    }

    /// <summary>
    ///     Returns a model name with an explicit tag, so two spellings of one model compare equal.
    /// </summary>
    /// <param name="model">The model name, tagged or bare.</param>
    /// <returns>The name with a tag.</returns>
    private static string Tagged(string model) =>
        model.Contains(':', StringComparison.Ordinal) ? model : model + ":latest";

    /// <summary>
    ///     Runs an optional server query, treating any failure as "the server did not say".
    /// </summary>
    /// <remarks>
    ///     Both queries here are conveniences: the run proceeds without either, on a window from a
    ///     lower-precedence source. Cancellation is deliberately not swallowed, because a canceled
    ///     run must stop rather than quietly continue with an assumed window.    /// </remarks>
    /// <typeparam name="T">The query's result type.</typeparam>
    /// <param name="query">The query to run.</param>
    /// <returns>The result, or <see langword="null"/> when the server did not answer.</returns>
    private static async Task<T?> TryReadAsync<T>(Func<Task<T>> query)
        where T : class
    {
        try
        {
            return await query();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Every other failure is the same fact from this method's point of view: the server did
            // not report a window. Which way it failed changes nothing the caller can act on, and
            // an older Ollama, a proxy, and a model that was never pulled all arrive here.
            return null;
        }
    }
}
