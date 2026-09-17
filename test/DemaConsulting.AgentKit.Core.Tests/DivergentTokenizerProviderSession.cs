namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     A provider session whose reported usage counts its history at a configurable multiple of the
///     rate <see cref="InMemoryProviderSession"/> charges, so a test can run one conversation
///     against providers that count the same history differently.
/// </summary>
/// <remarks>
///     <para>
///     <b>The engine believes whatever a provider session reports, and this is how that is put under
///     load.</b> The engine performs no token arithmetic of its own: it asks how full the session is
///     and out of how much, and acts on the answer. A provider that charges two or three times as
///     much for the same history — ordinary for JSON and code against a tokenizer tuned for prose —
///     must therefore reach its threshold sooner and compact harder, with nothing but the adapter's
///     own count distinguishing the runs.
///     </para>
///     <para>
///     It reports totals only, with no split broken out, which is the shape an adapter uses when its
///     provider will not say how much of the window the system prompt and tool declarations occupy.
///     </para>
/// </remarks>
internal sealed class DivergentTokenizerProviderSession : IProviderSession
{
    /// <summary>
    ///     The answer for a given message.
    /// </summary>
    private readonly Func<string, ProviderTurn> _responder;

    /// <summary>
    ///     The factor applied to the baseline count to obtain this provider's own count.
    /// </summary>
    private readonly double _multiplier;

    /// <summary>
    ///     The history, seeded plus everything since.
    /// </summary>
    private readonly List<TranscriptEntry> _history;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DivergentTokenizerProviderSession"/> class.
    /// </summary>
    /// <param name="seed">What the session starts from.</param>
    /// <param name="responder">Produces the turn for a given message.</param>
    /// <param name="windowTokens">The window this session reports.</param>
    /// <param name="multiplier">The factor applied to the baseline count to obtain the reported count.</param>
    public DivergentTokenizerProviderSession(
        ProviderSessionSeed seed,
        Func<string, ProviderTurn> responder,
        int windowTokens,
        double multiplier)
    {
        Seed = seed;
        _responder = responder;
        WindowTokens = windowTokens;
        _multiplier = multiplier;
        _history = [.. seed.History];
    }

    /// <summary>
    ///     Gets what this session was seeded with.
    /// </summary>
    public ProviderSessionSeed Seed { get; }

    /// <summary>
    ///     Gets the window this session reports.
    /// </summary>
    public int WindowTokens { get; }

    /// <summary>
    ///     Gets the number of turns this session has answered.
    /// </summary>
    public int TurnCount { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether this session has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    ///     Reports the multiplier applied to the baseline count of the whole history, with no
    ///     overhead broken out — the totals-only shape a provider that cannot split its counts uses.
    /// </remarks>
    public ContextUsage CurrentUsage
    {
        get
        {
            var baseline = 0;
            foreach (var entry in _history)
            {
                baseline += SessionTestData.EntryTokens(entry);
            }

            var reported = (int)Math.Round(baseline * _multiplier);
            return ContextUsage.FromProvider(reported, WindowTokens, reported);
        }
    }

    /// <inheritdoc/>
    public Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var turn = _responder(message)
            ?? throw new InvalidOperationException("The responder returned null; it must return a turn.");

        cancellationToken.ThrowIfCancellationRequested();

        _history.Add(TranscriptEntry.User(message));
        _history.AddRange(turn.Entries);
        TurnCount++;
        return Task.FromResult(turn);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        _history.Clear();
        return default;
    }
}

/// <summary>
///     Creates <see cref="DivergentTokenizerProviderSession"/> instances at a fixed tokenizer
///     multiplier and remembers every one it made.
/// </summary>
/// <param name="multiplier">The factor the sessions apply to the baseline count to obtain their own.</param>
/// <param name="windowTokens">The window the sessions report.</param>
/// <param name="responder">
///     Produces the turn for a given message. <see langword="null"/> selects an acknowledging
///     responder.
/// </param>
internal sealed class DivergentTokenizerProviderSessionFactory(
    double multiplier,
    int windowTokens,
    Func<string, ProviderTurn>? responder = null) : IProviderSessionFactory
{
    /// <summary>
    ///     The sessions created so far, oldest first.
    /// </summary>
    private readonly List<DivergentTokenizerProviderSession> _sessions = [];

    /// <summary>
    ///     The responder every session answers with.
    /// </summary>
    private readonly Func<string, ProviderTurn> _responder =
        responder ?? (message => new ProviderTurn($"Acknowledged: {message}"));

    /// <summary>
    ///     Gets every session created so far, oldest first.
    /// </summary>
    public IReadOnlyList<DivergentTokenizerProviderSession> Sessions => _sessions;

    /// <inheritdoc/>
    public Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        var session = new DivergentTokenizerProviderSession(seed, _responder, windowTokens, multiplier);
        _sessions.Add(session);
        return Task.FromResult<IProviderSession>(session);
    }
}
