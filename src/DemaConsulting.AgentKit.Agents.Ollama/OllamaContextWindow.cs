using OllamaSharp;
using OllamaSharp.Models;

namespace DemaConsulting.AgentKit.Agents.Ollama;

/// <summary>
///     Where a reported Ollama context window came from.
/// </summary>
/// <remarks>
///     Published alongside the figure rather than kept private, because the window is the one number
///     the whole compaction arrangement turns on and the sources are not interchangeable: a length
///     read from the loaded instance is what the server will enforce, while an assumed one is only
///     the conservative guess made when nothing could be read. A caller that cannot tell them apart
///     cannot warn its user, so every member here is a distinct claim about how much the number
///     should be trusted.
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
///     <b>Only the running instance can answer this.</b> A model file publishes the context length
///     it could be loaded with, but the server decides at load time what the instance will actually
///     use, and that decision is the one enforced. The two figures are routinely far apart, in the
///     direction that loses history, so the published maximum is never consulted: either the
///     application stated a size and asked the server to run at it, or the loaded instance is asked
///     what it is running, or nothing is known and the conservative default is named as assumed.
///     </para>
///     <para>
///     <b>Whichever rung answers, the figure is asked of the server rather than merely believed.</b>
///     The application composes its chat clients with <see cref="OllamaContextSizingChatClient"/>
///     using the window this type returned, so every request asks Ollama to run at that length. A
///     stated size additionally wins outright here, because the application has already made it
///     true and the server need not be asked a question it has been told the answer to. See that
///     type's remarks for why a discovered window needs asking for just as much as a stated one,
///     and why a summarizer does too.
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
///     // Ask the server what window the instance running this model is using.
///     var client = new OllamaApiClient(new Uri("http://localhost:11434"), "qwen3:8b");
///     var window = await OllamaContextWindow.ReadAsync(
///         client,
///         "qwen3:8b",
///         stated: null,
///         CancellationToken.None);
///
///     // Hand window.Tokens to the provider-session factory the compacting session runs on, and
///     // report window.Source so an assumed default is never shown as a measured limit.
///     Console.WriteLine($"{window.Tokens} tokens, from {window.Source}");
///
///     // Then ask the server for that same figure on every request, so it stays true - a window
///     // that was only read is no more durable than one that was only claimed.
///     IChatClient sized = new OllamaContextSizingChatClient(client, window.Tokens);
///     </code>
/// </example>
/// <param name="Tokens">The window in tokens. Always positive.</param>
/// <param name="Source">Where the figure was obtained.</param>
/// <exception cref="ArgumentOutOfRangeException">
///     <paramref name="Tokens"/> is zero or less, which no window could be.
/// </exception>
public sealed record OllamaContextWindow(int Tokens, OllamaContextWindowSource Source)
{
    /// <summary>
    ///     The window in tokens. Always positive.
    /// </summary>
    /// <remarks>
    ///     Enforced at construction rather than only along the paths <see cref="Select"/> takes, so
    ///     the promise holds for every instance that can exist rather than only for the ones this
    ///     type produced. Without it a caller could construct an instance contradicting this very
    ///     sentence, leaving the invariant to whichever consumer happened to re-check it.
    /// </remarks>
    public int Tokens { get; } = Tokens > 0
        ? Tokens
        : throw new ArgumentOutOfRangeException(
            nameof(Tokens),
            Tokens,
            "A context window must be a positive number of tokens.");

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
    ///     Reads the window from an Ollama server, preferring what a stated option says and then
    ///     what the server reports about the instance it is running.
    /// </summary>
    /// <remarks>
    ///     The query is not required to succeed. A server that refuses it — an older build, a proxy,
    ///     one that never answers — yields the assumed default rather than a failed run, because an
    ///     application that could not start because it could not read an optional number would be
    ///     worse than one that says which number it assumed. Performs network I/O; cancellation is
    ///     propagated rather than swallowed.
    /// </remarks>
    /// <param name="client">The Ollama client to ask. Must not be <see langword="null"/>.</param>
    /// <param name="model">
    ///     The model the conversation runs on. Must not be <see langword="null"/> or empty.
    /// </param>
    /// <param name="stated">
    ///     The window the application stated, or <see langword="null"/> when none was. A value of
    ///     zero or less is treated as unstated.
    /// </param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The window to account against, and where it came from. Never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="client"/> or <paramref name="model"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="model"/> is empty.</exception>
    /// <exception cref="OperationCanceledException">
    ///     <paramref name="cancellationToken"/> was canceled during the query.
    /// </exception>
    public static async Task<OllamaContextWindow> ReadAsync(
        IOllamaApiClient client,
        string model,
        int? stated,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(model);

        // A stated window settles it. Asking the server anyway would invite a reader to wonder
        // which answer won, and would make discovery load a model as a side effect of asking.
        if (stated is > 0)
        {
            return Select(stated, running: null, model);
        }

        var running = await TryReadAsync(
            () => client.ListRunningModelsAsync(cancellationToken),
            cancellationToken);

        return Select(stated, running, model);
    }

    /// <summary>
    ///     Chooses the window from what was stated and what the server reported.
    /// </summary>
    /// <remarks>
    ///     The whole precedence in one pure function, so it can be exercised without a server: a
    ///     stated window, then the length the loaded instance is running, then Ollama's own default.
    ///     Contacts nothing and is safe for concurrent use.
    /// </remarks>
    /// <param name="stated">
    ///     The stated window, or <see langword="null"/>. A value of zero or less is treated as
    ///     unstated.
    /// </param>
    /// <param name="running">The models the server reports as loaded, or <see langword="null"/>.</param>
    /// <param name="model">
    ///     The model name to match a loaded model against, tagged or bare. Must not be
    ///     <see langword="null"/> or empty.
    /// </param>
    /// <returns>The chosen window and its source. Never <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="model"/> is empty.</exception>
    public static OllamaContextWindow Select(
        int? stated,
        IEnumerable<RunningModel>? running,
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
    ///     The query is a convenience: the conversation proceeds without it, on the assumed default.
    ///     The caller's cancellation is deliberately not swallowed, because a canceled call must
    ///     stop rather than quietly continue with an assumed window.
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
