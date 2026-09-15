using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     Everything a fresh provider session needs to resume where the previous one left off.
/// </summary>
/// <remarks>
///     <para>
///     A seed is the whole interface between the compaction engine and a provider adapter. The
///     engine never touches a provider's API; it produces one of these, and an adapter turns it
///     into whatever its provider requires. That is what keeps the engine provider-agnostic, and
///     what lets it be tested against a fake.
///     </para>
///     <para>
///     The instructions and tools are carried separately from the history because providers accept
///     them separately — as configuration rather than as messages — which is also why they are
///     accounted for as fixed overhead rather than as conversation.
///     </para>
///     <para>
///     Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
public sealed class ProviderSessionSeed
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ProviderSessionSeed"/> class.
    /// </summary>
    /// <param name="instructions">The system instructions, or <see langword="null"/> when there are none.</param>
    /// <param name="tools">
    ///     The tools the session may call. Must not be <see langword="null"/>; may be empty. Must
    ///     contain no <see langword="null"/> entry.
    /// </param>
    /// <param name="history">
    ///     The history to seed, most stable first, as produced by
    ///     <see cref="ContextLayout.BuildSeed"/>. Must not be <see langword="null"/>; may be empty
    ///     for a session that has not started.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tools"/> or <paramref name="history"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> or <paramref name="history"/> contains a <see langword="null"/> entry.
    /// </exception>
    public ProviderSessionSeed(
        string? instructions,
        IReadOnlyList<AIFunction> tools,
        IReadOnlyList<TranscriptEntry> history)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(history);

        // Reject null entries here rather than leaving an adapter to discover them mid-construction,
        // where the failure would be attributed to the provider rather than to the composition.
        if (tools.Any(tool => tool is null))
        {
            throw new ArgumentException("A tool in the list is null.", nameof(tools));
        }

        if (history.Any(entry => entry is null))
        {
            throw new ArgumentException("An entry in the history is null.", nameof(history));
        }

        Instructions = instructions;

        // Copy both lists into storage this seed owns, exposed only as read-only views. A seed is
        // an immutable snapshot that an adapter may hold across a rotation: retaining the caller's
        // lists would let it observe a later mutation and start a session from something other than
        // what was validated here.
        Tools = Array.AsReadOnly<AIFunction>([.. tools]);
        History = Array.AsReadOnly<TranscriptEntry>([.. history]);
    }

    /// <summary>
    ///     Gets the system instructions, or <see langword="null"/> when there are none.
    /// </summary>
    public string? Instructions { get; }

    /// <summary>
    ///     Gets the tools the session may call.
    /// </summary>
    /// <remarks>
    ///     Identical across every rotation of one logical session: rotation replaces history, never
    ///     capability.
    /// </remarks>
    public IReadOnlyList<AIFunction> Tools { get; }

    /// <summary>
    ///     Gets the history to seed, most stable first.
    /// </summary>
    /// <remarks>
    ///     Holds consolidated tier records ahead of verbatim recent turns. Empty for the first
    ///     session of a conversation.
    /// </remarks>
    public IReadOnlyList<TranscriptEntry> History { get; }
}

/// <summary>
///     What one turn against a provider produced.
/// </summary>
/// <remarks>
///     <para>
///     The response text and the history entries are separate because they answer different
///     questions. The text is what the application shows or acts on; the entries are what the
///     engine records, and for a tool-using turn there are several of them — an assistant message,
///     then call and result pairs — none of which is the answer.
///     </para>
///     <para>
///     Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
public sealed class ProviderTurn
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ProviderTurn"/> class.
    /// </summary>
    /// <remarks>
    ///     When no entries are supplied, the turn is recorded as a single assistant message
    ///     carrying <paramref name="responseText"/>. That is the correct record for a provider that
    ///     called no tools, and it spares a simple adapter from restating its own answer.
    /// </remarks>
    /// <param name="responseText">
    ///     The provider's answer. Must not be <see langword="null"/>; may be empty.
    /// </param>
    /// <param name="entries">
    ///     The history entries the turn produced, in order. <see langword="null"/> or empty records
    ///     the turn as one assistant message. Must contain no <see langword="null"/> entry.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="responseText"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="entries"/> contains a <see langword="null"/> entry.</exception>
    public ProviderTurn(string responseText, IReadOnlyList<TranscriptEntry>? entries = null)
    {
        ArgumentNullException.ThrowIfNull(responseText);

        if (entries is not null && entries.Any(entry => entry is null))
        {
            throw new ArgumentException("An entry in the list is null.", nameof(entries));
        }

        ResponseText = responseText;

        // Copy into storage this turn owns, exposed only as a read-only view, for the same reason
        // the seed does: the entries are what the engine records as history, and a turn is
        // documented as immutable.
        Entries = entries is null || entries.Count == 0
            ? Array.AsReadOnly<TranscriptEntry>([TranscriptEntry.Assistant(responseText)])
            : Array.AsReadOnly<TranscriptEntry>([.. entries]);
    }

    /// <summary>
    ///     Gets the provider's answer.
    /// </summary>
    public string ResponseText { get; }

    /// <summary>
    ///     Gets the history entries the turn produced, in order. Never empty.
    /// </summary>
    public IReadOnlyList<TranscriptEntry> Entries { get; }
}

/// <summary>
///     One live conversation with a provider, from creation to disposal.
/// </summary>
/// <remarks>
///     <para>
///     <b>Deliberately minimal.</b> The interface says nothing about streaming, tool invocation,
///     retries, or provider configuration, because the compaction engine needs none of that: it
///     sends a message, records what came back, and eventually disposes the session so a fresh one
///     can replace it. Everything an adapter has to do beyond that is the adapter's business.
///     </para>
///     <para>
///     <b>A session is disposable because rotation disposes it.</b> Disposal is the mechanism that
///     works for both provider shapes — one holding history server-side, the other resending it —
///     and it is the only way to discard server-side history that the engine can rely on.
///     </para>
///     <para>
///     An implementation that can report its own context usage also implements
///     <see cref="IContextUsageReporter"/>; the engine tests for it and estimates when it is absent.
///     </para>
///     <para>
///     Implementations need not be safe for concurrent use: one session serves one conversation,
///     and turns within a conversation are sequential by nature.
///     </para>
/// </remarks>
public interface IProviderSession : IAsyncDisposable
{
    /// <summary>
    ///     Sends one message and returns what the provider produced.
    /// </summary>
    /// <remarks>
    ///     An implementation may call tools before answering; the entries it returns should record
    ///     those calls and their results so the engine's transcript matches what the provider
    ///     actually holds. A session that has been disposed rejects the call rather than
    ///     reconnecting.
    /// </remarks>
    /// <param name="message">The message to send. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the turn.</param>
    /// <returns>The provider's answer and the history entries the turn produced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The session has been disposed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>
///     Creates provider sessions from a seed, once at the start of a conversation and again at
///     every rotation.
/// </summary>
/// <remarks>
///     <para>
///     The factory is separate from the session because rotation needs to create a <em>replacement</em>
///     while holding no reference to a provider's API itself. An application supplies one factory
///     for the provider it chose; the engine calls it whenever it needs a session, and never
///     otherwise.
///     </para>
///     <para>
///     Implementations must be safe for concurrent use, because an application may run more than
///     one session against the same factory.
///     </para>
/// </remarks>
public interface IProviderSessionFactory
{
    /// <summary>
    ///     Creates a provider session carrying the seeded instructions, tools and history.
    /// </summary>
    /// <remarks>
    ///     The returned session must already hold the seeded history, so that the first message sent
    ///     to it continues the conversation rather than starting one. Ownership of the returned
    ///     session passes to the caller, which disposes it at the next rotation.
    /// </remarks>
    /// <param name="seed">What the new session must start from. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the creation.</param>
    /// <returns>A session ready to receive its next message.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="seed"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<IProviderSession> CreateAsync(ProviderSessionSeed seed, CancellationToken cancellationToken = default);
}
