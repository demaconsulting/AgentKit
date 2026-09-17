namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     A provider session whose reported usage counts its history at a configurable multiple of this
///     library's own estimate, so a provider's count of a rotated seed can diverge from ours.
/// </summary>
/// <remarks>
///     <para>
///     <b>This fake exists to close a blind spot that hid four separate defects.</b> Every other
///     provider fake either ignores the rotated seed or counts it with this library's own estimator,
///     so none can produce a provider whose count of a rotated seed differs from ours — the exact
///     condition under which the old, prediction-in-mixed-currency design failed. This one applies a
///     tokenizer multiplier to the seed and everything since, so at a multiplier of two or three the
///     provider reports twice or three times what the estimator does, which is ordinary for JSON and
///     code.
///     </para>
///     <para>
///     Any test of the drop-until-it-fits rule that does not use this fake is not testing it.
///     </para>
/// </remarks>
internal sealed class DivergentTokenizerProviderSession : IProviderSession
{
    /// <summary>
    ///     The answer for a given message.
    /// </summary>
    private readonly Func<string, ProviderTurn> _responder;

    /// <summary>
    ///     The factor applied to this library's estimate to obtain the provider's own count.
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
    /// <param name="multiplier">The factor applied to the estimate to obtain the reported count.</param>
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
    ///     Reports the provider's own count as the multiplier applied to this library's estimate of
    ///     the whole history, with no overhead broken out — the totals-only shape a provider that
    ///     cannot split its counts uses.
    /// </remarks>
    public ContextUsage CurrentUsage
    {
        get
        {
            var estimate = 0;
            foreach (var entry in _history)
            {
                estimate += entry.EstimatedTokens;
            }

            var reported = (int)Math.Round(estimate * _multiplier);
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
/// <param name="multiplier">The factor the sessions apply to the estimate to obtain their count.</param>
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
