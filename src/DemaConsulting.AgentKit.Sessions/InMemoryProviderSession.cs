using System.Collections.ObjectModel;

namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     A provider session that contacts nothing: it holds its seeded history in memory, answers
///     from a supplied responder, and accounts for its own context usage.
/// </summary>
/// <remarks>
///     <para>
///     <b>Shipped rather than confined to this library's tests, deliberately.</b> The compaction
///     engine's whole promise is that a long-running agent keeps the detail that matters, and that
///     promise is only believable if it can be exercised end to end without a live model. An
///     application author writing their own summarizer, choosing a verbatim tail length, or acting
///     on the reported compaction level needs the same ability. Keeping the fake in the package
///     makes that a supported activity instead of something each consumer reimplements.
///     </para>
///     <para>
///     <b>It answers for its own window, as every adapter does.</b> A provider session is the one
///     place that can say how full it is and out of how much, so this one computes both from its
///     seeded history and the window it was given, and reports them as a provider's own figures.
///     That is the shape a real adapter has, which is what makes exercising the engine against this
///     session meaningful.
///     </para>
///     <para>
///     Instances are not safe for concurrent use, consistent with <see cref="IProviderSession"/>.
///     </para>
/// </remarks>
public sealed class InMemoryProviderSession : IProviderSession
{
    /// <summary>
    ///     The answer for a given message.
    /// </summary>
    private readonly Func<string, ProviderTurn> _responder;

    /// <summary>
    ///     The history, seeded plus everything since.
    /// </summary>
    private readonly List<TranscriptEntry> _history;

    /// <summary>
    ///     The read-only view handed out by <see cref="History"/>.
    /// </summary>
    private readonly ReadOnlyCollection<TranscriptEntry> _historyView;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryProviderSession"/> class.
    /// </summary>
    /// <param name="seed">What the session starts from. Must not be <see langword="null"/>.</param>
    /// <param name="responder">
    ///     Produces the turn for a given message. Must not be <see langword="null"/>, and must not
    ///     return <see langword="null"/>.
    /// </param>
    /// <param name="windowTokens">
    ///     The context window this session pretends to have. Must be positive.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="seed"/> or <paramref name="responder"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="windowTokens"/> is not positive.</exception>
    public InMemoryProviderSession(
        ProviderSessionSeed seed,
        Func<string, ProviderTurn> responder,
        int windowTokens)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(responder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowTokens);

        Seed = seed;
        _responder = responder;
        WindowTokens = windowTokens;
        _history = [.. seed.History];
        _historyView = _history.AsReadOnly();

        // The fixed overhead a real provider would charge for instructions and declarations is
        // charged here too, so a test exercising the rotation threshold sees the same arithmetic
        // the engine performs against a real provider.
        //
        // Summed wide and saturated rather than wrapped: an instruction estimate fits a token count
        // and a declaration estimate fits one, but the two need not fit one together, and a
        // negative overhead would report a usage figure smaller than the conversation it contains -
        // which ContextUsage refuses outright, out of a property a test only reads.
        var fixedOverhead = (long)TokenEstimator.EstimateTokens(seed.Instructions)
            + TokenEstimator.EstimateToolDeclarationTokens(seed.Tools);
        FixedOverheadTokens = (int)Math.Min(fixedOverhead, int.MaxValue);
    }

    /// <summary>
    ///     Gets what this session was seeded with.
    /// </summary>
    /// <remarks>
    ///     Exposed so a test can assert what a rotation actually carried forward — which tier
    ///     records survived, and which verbatim turns — without reaching into the engine.
    /// </remarks>
    public ProviderSessionSeed Seed { get; }

    /// <summary>
    ///     Gets the context window this session pretends to have.
    /// </summary>
    public int WindowTokens { get; }

    /// <summary>
    ///     Gets the tokens the instructions and tool declarations occupy on every turn.
    /// </summary>
    public int FixedOverheadTokens { get; }

    /// <summary>
    ///     Gets the history, seeded entries first and everything since after them.
    /// </summary>
    /// <remarks>
    ///     A live read-only view rather than the backing list: the history grows as the session
    ///     takes turns, and a caller holding this list should see that, but must not be able to cast
    ///     it back and edit a conversation the session believes it holds.
    /// </remarks>
    public IReadOnlyList<TranscriptEntry> History => _historyView;

    /// <summary>
    ///     Gets the number of turns this session has answered.
    /// </summary>
    public int TurnCount { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether this session has been disposed.
    /// </summary>
    /// <remarks>
    ///     Exposed so a test can assert that rotation disposed the previous session rather than
    ///     leaking it — a leak that against a real provider would hold a server-side conversation
    ///     open and keep being billed for.
    /// </remarks>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    ///     Computed from this session's own history and its fixed overhead, and marked as
    ///     provider-reported because that is what it stands in for. The conversation is reported
    ///     separately, as a provider that distinguishes the two does, so the shape a real reporting
    ///     adapter uses is the shape exercised here.
    /// </remarks>
    public ContextUsage CurrentUsage
    {
        get
        {
            // Accumulated wide and saturated where it is narrowed, for the reason the transcript's
            // own total is: entries carrying the longest strings that can exist sum past a token
            // count, and a wrapped negative conversation would be refused by ContextUsage from
            // inside a property. Saturating keeps the two figures ordered - the total is never
            // below the conversation it contains - which is the one relation ContextUsage requires.
            var conversation = 0L;
            foreach (var entry in _history)
            {
                conversation += entry.EstimatedTokens;
            }

            var used = FixedOverheadTokens + conversation;

            return ContextUsage.FromProvider(
                (int)Math.Min(used, int.MaxValue),
                WindowTokens,
                (int)Math.Min(conversation, int.MaxValue));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     The responder runs before anything is recorded. A responder that throws, or returns
    ///     <see langword="null"/>, therefore leaves this session exactly as it was rather than
    ///     holding a user message no turn ever answered — the same rule
    ///     <see cref="CompactingAgentSession"/> applies to its own transcript. A fake whose history
    ///     diverged from the engine's transcript under failure would make the engine's own guarantee
    ///     untestable.
    ///     <para>
    ///     Cancellation is honored on both sides of the responder, for the same reason. A token
    ///     canceled before the call refuses the turn outright; a token canceled <em>while</em> the
    ///     responder ran refuses it too, so a canceled turn leaves no history whichever moment the
    ///     cancellation arrived in. Checking only beforehand recorded the message and the answer of
    ///     a turn the caller had been told was canceled, which is precisely the contract this
    ///     session is here to model faithfully.
    ///     </para>
    /// </remarks>
    public Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        // Answer first, record second. Recording the incoming message ahead of the responder would
        // leave a ghost message behind whenever the responder failed, and a real provider that
        // rejects a turn holds nothing either.
        var turn = _responder(message)
            ?? throw new InvalidOperationException("The responder returned null; it must return a turn.");

        // Checked again, on the far side of the responder. The check above refuses a turn the
        // caller had already given up on; this one refuses a turn whose responder completed after
        // the caller gave up while it was running - a responder is free to cancel the token itself,
        // and an adapter for a real provider awaits a call that a cancellation can overtake. The
        // documented contract is that a canceled turn leaves this session exactly as it was, and
        // recording here would break it in the one direction that matters: the history would hold a
        // message and an answer for a turn the caller was told had been canceled.
        cancellationToken.ThrowIfCancellationRequested();

        // Both halves of the turn are appended together, so the history a caller can observe never
        // holds a message without the turn that answered it.
        _history.Add(TranscriptEntry.User(message));
        _history.AddRange(turn.Entries);
        TurnCount++;
        return Task.FromResult(turn);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Marks the session disposed and drops its history, which is what makes a leaked session
    ///     detectable in a test. Disposing twice is permitted and does nothing the second time.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        _history.Clear();
        return default;
    }
}

/// <summary>
///     Creates <see cref="InMemoryProviderSession"/> instances and remembers every one it made.
/// </summary>
/// <remarks>
///     <para>
///     The remembering is the point. Rotation creates a replacement session and disposes the
///     previous one, and both halves of that have to be observable for the behavior to be
///     verifiable at all: a test asserts how many sessions were created, that the earlier ones were
///     disposed, and what the newest one was seeded with.
///     </para>
///     <para>
///     Instances are safe for concurrent use, as <see cref="IProviderSessionFactory"/> requires:
///     an application may run several sessions against one factory, and their rotations can create
///     replacements at the same moment. Creation is serialized and <see cref="Sessions"/> hands
///     back a snapshot, so a concurrent rotation can neither corrupt the record nor be seen halfway
///     through adding to it. The sessions it produces remain single-conversation objects and are
///     not themselves safe for concurrent use.
///     </para>
/// </remarks>
public sealed class InMemoryProviderSessionFactory : IProviderSessionFactory
{
    /// <summary>
    ///     Guards the record of created sessions.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    ///     The context window the created sessions pretend to have when none is given.
    /// </summary>
    /// <remarks>
    ///     A round number large enough that a test which is not about the window does not
    ///     accidentally rotate, and small enough that one which is can reach it cheaply. It stands
    ///     for nothing in particular: a real adapter reads its window from its provider or is told
    ///     it, and this session is the stand-in for one.
    /// </remarks>
    public const int DefaultWindowTokens = 128_000;

    /// <summary>
    ///     Produces the turn for a given message.
    /// </summary>
    private readonly Func<string, ProviderTurn> _responder;

    /// <summary>
    ///     The sessions created so far, oldest first.
    /// </summary>
    private readonly List<InMemoryProviderSession> _sessions = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="InMemoryProviderSessionFactory"/> class.
    /// </summary>
    /// <param name="responder">
    ///     Produces the turn for a given message. <see langword="null"/> selects a responder that
    ///     echoes the message back as an assistant answer, which is enough to exercise the
    ///     lifecycle when what the model says does not matter.
    /// </param>
    /// <param name="windowTokens">
    ///     The context window the sessions pretend to have. Must be positive.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="windowTokens"/> is not positive.</exception>
    public InMemoryProviderSessionFactory(
        Func<string, ProviderTurn>? responder = null,
        int windowTokens = DefaultWindowTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowTokens);

        _responder = responder ?? (message => new ProviderTurn($"Acknowledged: {message}"));
        WindowTokens = windowTokens;
    }

    /// <summary>
    ///     Gets the context window the created sessions pretend to have.
    /// </summary>
    public int WindowTokens { get; }

    /// <summary>
    ///     Gets every session created so far, oldest first.
    /// </summary>
    /// <remarks>
    ///     One entry per rotation plus one for the original session, so the count is the rotation
    ///     count plus one for a conversation that ran to completion. Each read returns a snapshot
    ///     taken under the factory's lock, so a concurrent creation can neither be observed halfway
    ///     through nor invalidate a list a caller is walking.
    /// </remarks>
    public IReadOnlyList<InMemoryProviderSession> Sessions
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly<InMemoryProviderSession>([.. _sessions]);
            }
        }
    }

    /// <inheritdoc/>
    public Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        var session = new InMemoryProviderSession(seed, _responder, WindowTokens);

        // Serialize the record. An unsynchronized List<T>.Add from two rotations at once can lose a
        // session or leave the list internally inconsistent, and this factory is the one an
        // application is invited to run several sessions against.
        lock (_gate)
        {
            _sessions.Add(session);
        }

        return Task.FromResult<IProviderSession>(session);
    }
}
