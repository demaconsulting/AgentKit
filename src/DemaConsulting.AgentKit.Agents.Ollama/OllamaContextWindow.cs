using System.Text.Json;
using OllamaSharp;
using OllamaSharp.Models;

namespace DemaConsulting.AgentKit.Agents.Ollama;

/// <summary>
///     Where a reported Ollama context window came from.
/// </summary>
/// <remarks>
///     Published alongside the figure rather than kept private, because the window is the one number
///     the whole compaction arrangement turns on and the sources are not interchangeable: a loaded
///     model's length is what the server will enforce, while a published maximum is only what the
///     model could be loaded with. A caller that cannot tell them apart cannot warn its user, so
///     every member here is a distinct claim about how much the number should be trusted.
/// </remarks>
public enum OllamaContextWindowSource
{
    /// <summary>
    ///     The application stated it, which settles the question outright.
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
}

/// <summary>
///     The context window a conversation on an Ollama model should be accounted against, and where
///     the figure came from.
/// </summary>
/// <remarks>
///     <para>
///     <b>This exists because an <c>IChatClient</c> publishes no context window, and AgentKit
///     refuses to guess one.</b> A compacting session is told the window once, where the
///     application configures its provider, and answers with it thereafter — so the application has
///     to obtain it. For Ollama it can, but only through Ollama's own APIs, which is why this ships
///     apart from the adapter that serves every <c>IChatClient</c> provider alike.
///     </para>
///     <para>
///     <b>Two figures exist and they are not the same figure.</b> A model publishes the context
///     length it was trained for, and Ollama loads it with a context length of its own choosing,
///     which is smaller by default. The loaded figure is the one the server enforces, so it is
///     preferred; the published figure is used only when nothing is loaded, and
///     <see cref="Source"/> says so rather than presenting a maximum as though it were the limit in
///     force.
///     </para>
///     <para>
///     Reading the server and choosing among what it reported are deliberately separate:
///     <see cref="ReadAsync"/> performs the I/O, and <see cref="Select"/> is a pure function holding
///     the whole precedence, so that precedence can be exercised without a server.
///     </para>
///     <para>
///     Instances are immutable and safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     // Ask the server what window it will enforce for the model this conversation runs on.
///     var client = new OllamaApiClient(new Uri("http://localhost:11434"), "qwen3:8b");
///     var window = await OllamaContextWindow.ReadAsync(
///         client,
///         "qwen3:8b",
///         stated: null,
///         CancellationToken.None);
///
///     // Hand window.Tokens to the provider-session factory the compacting session runs on, and
///     // report window.Source so a published maximum is never shown as the limit in force.
///     Console.WriteLine($"{window.Tokens} tokens, from {window.Source}");
///     </code>
/// </example>
/// <param name="Tokens">The window in tokens. Always positive.</param>
/// <param name="Source">Where the figure was obtained.</param>
public sealed record OllamaContextWindow(int Tokens, OllamaContextWindowSource Source)
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
    ///     rather than a failed run, because an application that could not start because it could
    ///     not read an optional number would be worse than one that says which number it assumed.
    ///     Performs network I/O; cancellation is propagated rather than swallowed.
    /// </remarks>
    /// <param name="client">The Ollama client to ask. Must not be <see langword="null"/>.</param>
    /// <param name="model">The model the conversation runs on. Must not be <see langword="null"/>.</param>
    /// <param name="stated">
    ///     The window the application stated, or <see langword="null"/> when none was. A value of
    ///     zero or less is treated as unstated.
    /// </param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The window to account against, and where it came from. Never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="client"/> or <paramref name="model"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     <paramref name="cancellationToken"/> was canceled during a query.
    /// </exception>
    public static async Task<OllamaContextWindow> ReadAsync(
        IOllamaApiClient client,
        string model,
        int? stated,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(model);

        // A stated window settles it, and asking the server anyway would only invite a reader to
        // wonder which answer won.
        if (stated is > 0)
        {
            return Select(stated, running: null, published: null, model);
        }

        var running = await TryReadAsync(
            () => client.ListRunningModelsAsync(cancellationToken),
            cancellationToken);

        var published = await TryReadAsync(
            () => client.ShowModelAsync(new ShowModelRequest { Model = model }, cancellationToken),
            cancellationToken);

        return Select(stated, running, published, model);
    }

    /// <summary>
    ///     Chooses the window from what was stated and what the server reported.
    /// </summary>
    /// <remarks>
    ///     The whole precedence in one pure function, so it can be exercised without a server: a
    ///     stated window, then the loaded model's enforced length, then the model's published
    ///     maximum, then Ollama's own default. Contacts nothing and is safe for concurrent use.
    /// </remarks>
    /// <param name="stated">
    ///     The stated window, or <see langword="null"/>. A value of zero or less is treated as
    ///     unstated.
    /// </param>
    /// <param name="running">The models the server reports as loaded, or <see langword="null"/>.</param>
    /// <param name="published">The model's published metadata, or <see langword="null"/>.</param>
    /// <param name="model">
    ///     The model name to match a loaded model against, tagged or bare. Must not be
    ///     <see langword="null"/>.
    /// </param>
    /// <returns>The chosen window and its source. Never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public static OllamaContextWindow Select(
        int? stated,
        IEnumerable<RunningModel>? running,
        ShowModelResponse? published,
        string model)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);

        if (stated is > 0)
        {
            return new OllamaContextWindow(stated.Value, OllamaContextWindowSource.Stated);
        }

        var loaded = FromLoadedModel(running, model);
        if (loaded > 0)
        {
            return new OllamaContextWindow(loaded, OllamaContextWindowSource.LoadedModel);
        }

        var maximum = FromPublishedModel(published);
        if (maximum > 0)
        {
            return new OllamaContextWindow(maximum, OllamaContextWindowSource.PublishedModel);
        }

        return new OllamaContextWindow(AssumedTokens, OllamaContextWindowSource.Assumed);
    }

    /// <summary>
    ///     Finds the context length the named model is currently loaded with.
    /// </summary>
    /// <remarks>
    ///     Ollama names a loaded model with its tag — <c>qwen3.5:9b</c> — while a caller may omit
    ///     the tag, which the server resolves to <c>latest</c>. Both forms are compared so a
    ///     conversation started with a bare model name still finds its own loaded model.
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
    ///     The value is whatever the metadata carried, so it is converted rather than cast; an
    ///     unreadable value yields zero and the next source down is used.
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
    ///     Ollama's model metadata arrives as JSON extension data, so every value in it is a
    ///     <see cref="JsonElement"/> whatever the server put there. Only a number that fits a token
    ///     count is taken: text, a fraction, or a figure beyond the range all yield zero, so the
    ///     next source down is used rather than a session being sized by something that was never a
    ///     context length.
    /// </remarks>
    /// <param name="value">The metadata value, which may be <see langword="null"/>.</param>
    /// <returns>The token count, or zero when the value is not a usable number.</returns>
    private static int AsTokenCount(object? value) =>
        value is JsonElement { ValueKind: JsonValueKind.Number } element
        && element.TryGetInt32(out var tokens)
            ? tokens
            : 0;

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
    ///     Both queries here are conveniences: the conversation proceeds without either, on a window
    ///     from a lower-precedence source. The caller's cancellation is deliberately not swallowed,
    ///     because a canceled call must stop rather than quietly continue with an assumed window.
    ///     <para>
    ///     That question is asked of the token, not of the exception type. <c>HttpClient</c> reports
    ///     its own request timeout by throwing <c>TaskCanceledException</c>, which derives from
    ///     <see cref="OperationCanceledException"/> and is indistinguishable from a caller's
    ///     cancellation by type alone. A server that accepts the connection and then never answers is
    ///     exactly the unresponsive server this method exists to tolerate, so it costs a rung rather
    ///     than failing the run - while a caller who really did cancel still stops.
    ///     </para>
    /// </remarks>
    /// <typeparam name="T">The query's result type.</typeparam>
    /// <param name="query">The query to run.</param>
    /// <param name="cancellationToken">The caller's token, which alone distinguishes the two.</param>
    /// <returns>The result, or <see langword="null"/> when the server did not answer.</returns>
    private static async Task<T?> TryReadAsync<T>(Func<Task<T>> query, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await query();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
