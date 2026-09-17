namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     The session that keeps its own transcript, watches the window, and rotates into a fresh
///     provider session when the context fills.
/// </summary>
/// <remarks>
///     <para>
///     <b>This is where the parts meet.</b> The options say what the agent is and how much recent
///     history to keep; the layout accounts for the context as a round-robin structure; the
///     rotation engine ages it; the provider-session factory produces the replacement. This class
///     owns the sequencing and nothing else, which is what keeps every other part independently
///     testable.
///     </para>
///     <para>
///     <b>Rotation is a replacement, not an edit.</b> When occupancy crosses the rotation threshold,
///     the engine consolidates older history into slots, a new provider session is created and
///     adopted seeded from the preserved content, and only then is the session it replaced disposed.
///     That is the only reduction both provider shapes support — one re-sends history each turn, the
///     other holds it server-side — and it is why the behavior is identical on either.
///     </para>
///     <para>
///     <b>The transcript is kept here, out of session.</b> Consolidation is a separate stateless
///     call that receives the material as input, never a request to the live session to summarize
///     itself: doing that spends the session's own context on the summary and provokes the
///     provider's built-in compactor.
///     </para>
///     <para>
///     Instances are not safe for concurrent use, consistent with <see cref="IAgentSession"/>.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Running a long conversation that compacts itself. The application supplies a summarizer and
///     a provider-session factory; everything else is handled.
///     </para>
///     <code>
///     public async Task RunAsync(ISummarizer summarizer, IProviderSessionFactory provider)
///     {
///         string[] questions = ["What changed?", "Why?", "What is left?"];
///
///         var options = new AgentSessionOptions(summarizer, instructions: "You are a helpful assistant.");
///         await using var session = await CompactingAgentSession.CreateAsync(options, provider);
///
///         foreach (var question in questions)
///         {
///             var response = await session.SendAsync(question);
///             Console.WriteLine(response.Text);
///
///             if (response.MaterialDropped)
///             {
///                 // Compacting bought nothing: history had to be discarded to make room.
///                 Console.WriteLine($"Dropped history at level {response.Level} after {session.RotationCount} rotations.");
///             }
///         }
///     }
///     </code>
/// </example>
public sealed class CompactingAgentSession : IAgentSession
{
    /// <summary>
    ///     What the application configured about this session.
    /// </summary>
    private readonly AgentSessionOptions _options;

    /// <summary>
    ///     Produces the replacement provider session at every rotation.
    /// </summary>
    private readonly IProviderSessionFactory _factory;

    /// <summary>
    ///     The live provider session and whether it has been released, replaced whole at every
    ///     rotation.
    /// </summary>
    private LiveProviderSession _live;

    /// <summary>
    ///     Whether this session has been disposed, and so refuses further turns.
    /// </summary>
    private bool _disposed;

    /// <summary>
    ///     The turns taken since the last rotation, or since the session was created before the
    ///     first rotation.
    /// </summary>
    /// <remarks>
    ///     The hysteresis clock. A rotation that follows within <see cref="K"/> turns of the last
    ///     escalates the compaction level; one that follows only after <see cref="M"/> turns relaxes
    ///     it.
    /// </remarks>
    private int _turnsSinceRotation;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CompactingAgentSession"/> class.
    /// </summary>
    /// <remarks>
    ///     Private because creating the first provider session is asynchronous and a constructor
    ///     cannot await; <see cref="CreateAsync"/> is the entry point. Reading the provider's usage
    ///     evaluates adapter code and can throw by design, so the constructor is a window in which
    ///     the provider session is owned by nothing — a window <see cref="CreateAsync"/> guards.
    /// </remarks>
    /// <param name="options">What the application configured.</param>
    /// <param name="factory">Produces the replacement provider session at every rotation.</param>
    /// <param name="provider">The first live provider session.</param>
    private CompactingAgentSession(
        AgentSessionOptions options,
        IProviderSessionFactory factory,
        IProviderSession provider)
    {
        _options = options;
        _factory = factory;

        Layout = ContextLayout.Create(options.SystemTokens, options.ToolDeclarationTokens);
        _live = LiveProviderSession.Adopt(provider);
        Usage = ReadUsage(provider);
    }

    /// <summary>
    ///     Gets the engine's own account of the context: the fixed overhead, the coarse tiers of
    ///     consolidated slots, and the verbatim recent turns.
    /// </summary>
    /// <remarks>
    ///     Replaced wholesale on every turn; the instance read here is a snapshot.
    /// </remarks>
    internal ContextLayout Layout { get; private set; }

    /// <inheritdoc/>
    public ContextUsage Usage { get; private set; }

    /// <inheritdoc/>
    public int RotationCount { get; private set; }

    /// <inheritdoc/>
    public CompactionLevel Level { get; private set; } = CompactionLevel.Low;

    /// <summary>
    ///     Gets the total consolidations every rotation of this session has performed.
    /// </summary>
    /// <remarks>
    ///     Summarizer calls are the dominant cost of this arrangement. Exposing the running total
    ///     lets an application see that cost rather than infer it.
    /// </remarks>
    public int ConsolidationCount { get; private set; }

    /// <summary>
    ///     Gets the turns that must pass within which a second rotation escalates the compaction
    ///     level.
    /// </summary>
    /// <remarks>
    ///     Derived from <see cref="AgentSessionOptions.VerbatimTurns"/>, not a setting: an
    ///     application cannot tune it better than this library can.
    /// </remarks>
    private int K => _options.VerbatimTurns;

    /// <summary>
    ///     Gets the turns that must pass without a rotation before the compaction level relaxes.
    /// </summary>
    /// <remarks>
    ///     Twice <see cref="K"/>, so the escalate and relax predicates can never both hold.
    /// </remarks>
    private int M => 2 * _options.VerbatimTurns;

    /// <summary>
    ///     Creates a session and its first provider session.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The first provider session is seeded with no history, because there is none yet — the
    ///     instructions and tools it carries are the same ones every later rotation will carry.
    ///     </para>
    ///     <para>
    ///     <b>Nothing this method creates is left unowned.</b> Between the factory returning a
    ///     provider session and this session taking ownership of it there is a window in which the
    ///     provider session belongs to no one, and that window can throw: constructing this session
    ///     reads the provider's usage, which is adapter code. So the construction is guarded — the
    ///     provider session is released, and the release decides how the failure is reported. When
    ///     the release succeeds the original failure is rethrown unchanged; when it fails the
    ///     provider still holds a session, and an <see cref="AgentSessionCreationException"/> carries
    ///     it in <see cref="AgentSessionCreationException.RetainedProviderSession"/>.
    ///     </para>
    /// </remarks>
    /// <param name="options">What the application configured. Must not be <see langword="null"/>.</param>
    /// <param name="providerSessionFactory">
    ///     Produces provider sessions, now and at every rotation. Must not be <see langword="null"/>,
    ///     and must not return <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the creation.</param>
    /// <returns>A session ready to receive its first message.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="options"/> or <paramref name="providerSessionFactory"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="providerSessionFactory"/> returned <see langword="null"/>.
    /// </exception>
    /// <exception cref="AgentSessionCreationException">
    ///     The created provider session could not be adopted and could not then be released, so the
    ///     failure carries it in
    ///     <see cref="AgentSessionCreationException.RetainedProviderSession"/> for the caller to
    ///     dispose. Whatever prevented the adoption is the inner exception.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<CompactingAgentSession> CreateAsync(
        AgentSessionOptions options,
        IProviderSessionFactory providerSessionFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(providerSessionFactory);

        var seed = new ProviderSessionSeed(options.Instructions, options.Tools, []);
        var provider = await providerSessionFactory.CreateAsync(seed, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The provider session factory returned null.");

        // Construction is the window in which the provider session is owned by nothing, and reading
        // its usage can throw by design: an adapter reporting a conversation larger than its own
        // total is refused where it wrote it. Guarded here so nothing is orphaned.
        try
        {
            return new CompactingAgentSession(options, providerSessionFactory, provider);
        }
        catch (Exception failure)
        {
            // Released rather than abandoned. Nothing else holds this provider session: the instance
            // that would have owned it does not exist, and the caller was never given one.
            if (await TryReleaseUnownedAsync(provider).ConfigureAwait(false))
            {
                // Nothing is held, so there is nothing to hand back. Rethrown rather than wrapped,
                // so it still surfaces where the adapter wrote it.
                throw;
            }

            // The release failed, so the provider still holds a session and this is the only handle
            // to it in existence. Carried on the failure, because the alternative is to drop it.
            throw new AgentSessionCreationException(
                "The session could not be created, and the provider session it had already created "
                + "could not be released, so the provider still holds it; dispose the provider "
                + $"session this failure carries ({nameof(AgentSessionCreationException.RetainedProviderSession)}) "
                + "to retry the release. See the inner exception for why the session could not be "
                + "created.",
                failure)
            {
                RetainedProviderSession = provider,
            };
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     A turn is recorded only once the provider has accepted it. A provider that cancels or
    ///     fails before taking the turn leaves this session exactly as it was, with no record of a
    ///     message no provider ever saw.
    ///     <para>
    ///     Where the turn triggers a rotation, a failure to dispose the superseded provider session
    ///     is not reported: the rotation itself succeeded and this session is coherent against its
    ///     replacement, so the turn is answered normally.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     The live provider session returned <see langword="null"/> for a turn, or the provider
    ///     session factory returned <see langword="null"/> during a rotation.
    /// </exception>
    public async Task<AgentSessionResponse> SendAsync(
        string message,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A session message must not be blank.", nameof(message));
        }

        // Take the turn against the live session. Nothing is recorded until the provider has
        // accepted the message, so a provider that refuses the turn leaves behind no ghost message.
        var turn = await _live.Session.SendAsync(message, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The provider session returned null; it must return a turn.");

        // Rule 1: append the whole turn — the message, any tool work, and the answer — as one turn.
        Layout = Layout.WithTail(Layout.Tail.AppendTurn([TranscriptEntry.User(message), .. turn.Entries]));
        _turnsSinceRotation++;

        Usage = ReadUsage(_live.Session);

        // Rule 2: rotate when occupancy reaches the rotation threshold. Both figures come from the
        // same usage reading, so both are in the same currency: a provider that reported its own
        // conversation count is measured entirely in that provider's tokens, and an estimate is
        // measured entirely in ours. Nothing is subtracted here.
        if (Usage.ConversationTokens < RotationThreshold(Usage))
        {
            return new AgentSessionResponse(
                turn.ResponseText, Usage, rotationOccurred: false, Level, materialDropped: false);
        }

        var (occurred, dropped) = await RotateAsync(cancellationToken).ConfigureAwait(false);
        return new AgentSessionResponse(turn.ResponseText, Usage, occurred, Level, dropped);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     <para>
    ///     Disposes whichever provider session is currently live. Sessions replaced by earlier
    ///     rotations were already disposed at the moment they were replaced, so nothing is left
    ///     holding server-side state.
    ///     </para>
    ///     <para>
    ///     A failure to release propagates, because a caller that asked for the session to be
    ///     released is entitled to learn that it was not — the deliberate opposite of a rotation,
    ///     which swallows the same failure because by then it has already succeeded. Because the
    ///     failure propagates, disposal stays retryable: this session is marked disposed from the
    ///     first call, but the provider is considered released only once its own disposal has
    ///     completed, so a later call tries again rather than returning as though the release had
    ///     happened.
    ///     </para>
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _live.ReleaseAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Ages the context by one rotation and replaces the live provider session with one seeded
    ///     from the result.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The order is deliberate: consolidate first, create the replacement second, adopt it and
    ///     update this session's state third, dispose the old session last. A summarizer failure
    ///     therefore leaves the session exactly as it was, still able to answer. The adoption carries
    ///     no await, so the live provider, the layout and the counters can never be observed
    ///     describing different sessions; and a provider that fails to dispose does not fail the
    ///     rotation, because by then it has already succeeded.
    ///     </para>
    ///     <para>
    ///     <b>The compaction level is chosen here, from the hysteresis clock.</b> A rotation that
    ///     follows within <see cref="K"/> turns of the last starts one level terser, so a session
    ///     under repeated pressure escalates. The engine may escalate further while building the
    ///     seed — a seed that does not fit is rebuilt at a terser level until it does — and the level
    ///     it settles at is what this session reports and starts its next rotation from. A rotation
    ///     that follows only after <see cref="M"/> quiet turns relaxes one level.
    ///     </para>
    ///     <para>
    ///     <b>A rotation that consolidated nothing and dropped nothing is not carried out.</b> When
    ///     the tail already holds everything and the seed fits, there is no new context to seed a
    ///     replacement from, so creating one would spend a provider session to arrive at exactly the
    ///     context the session already had. The level may still have changed, and that change is
    ///     recorded, but no replacement is created and the turn is reported as one that did not
    ///     rotate.
    ///     </para>
    ///     <para>
    ///     <b>A replacement that cannot be adopted is released rather than orphaned.</b> Reading a
    ///     replacement's usage is adapter code and may throw by design, and it happens while the
    ///     replacement is owned by nothing. The failure leaves this session coherent against the
    ///     provider it already had, so the replacement is released before the failure travels on.
    ///     </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the rotation.</param>
    /// <returns>Whether a rotation was carried out, and whether it dropped material.</returns>
    /// <exception cref="InvalidOperationException">The provider session factory returned null.</exception>
    private async Task<(bool Occurred, bool MaterialDropped)> RotateAsync(CancellationToken cancellationToken)
    {
        var originalLevel = Level;
        var sinceRotation = _turnsSinceRotation;
        var hadPriorRotation = RotationCount > 0;

        // Pressure is measured in turns, not tokens: the question is how quickly the window filled
        // again after the last rotation, and both figures are counts this session already keeps.
        var underPressure = hadPriorRotation && sinceRotation <= K;
        var quiet = hadPriorRotation && sinceRotation >= M;

        // The response to pressure is one step terser, and at the tersest level there is nowhere
        // further to go - so the oldest card goes in the bin. That is the ring in rule four, brought
        // forward because the context is filling faster than the ring's own schedule empties it. It
        // is a count, not a measurement: nothing here judges a context it has not sent.
        var level = originalLevel;
        var layout = Layout;
        var droppedForPressure = false;

        if (underPressure)
        {
            if (level == CompactionLevel.High)
            {
                var (dropped, reduced) = DropOldest(layout);
                layout = reduced;
                droppedForPressure = dropped;
            }
            else
            {
                level = RotationEngine.Escalate(level);
            }
        }
        else if (quiet)
        {
            level = RotationEngine.Relax(level);
        }

        var outcome = await RotationEngine
            .RotateAsync(layout, _options.Summarizer, level, _options.VerbatimTurns, cancellationToken)
            .ConfigureAwait(false);

        var settledLevel = outcome.Level;
        var materialDropped = outcome.MaterialDropped || droppedForPressure;

        // Nothing aged out and nothing was dropped: there is no new context to seed a replacement
        // from. Record the level change and report the turn as the ordinary turn it was.
        if (outcome.ConsolidationCount == 0 && !materialDropped)
        {
            Level = settledLevel;
            return (false, false);
        }

        var seed = new ProviderSessionSeed(_options.Instructions, _options.Tools, outcome.Layout.BuildSeed());

        var replacement = await _factory.CreateAsync(seed, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The provider session factory returned null.");

        // Guarded, because between the factory returning the replacement and the adoption below it
        // is owned by nothing, and reading its usage runs adapter code that is entitled to throw.
        ContextUsage replacementUsage;
        LiveProviderSession adopted;
        try
        {
            replacementUsage = ReadUsage(replacement);
            adopted = LiveProviderSession.Adopt(replacement);
        }
        catch (Exception)
        {
            // Released, and the outcome discarded: the caller still holds a working session against
            // the provider this rotation did not replace.
            await TryReleaseUnownedAsync(replacement).ConfigureAwait(false);
            throw;
        }

        // The whole state transition happens here, as one block containing no await, so the provider
        // reference, the layout and the counters describe the same session at every point an
        // exception could be observed.
        var previous = _live;
        _live = adopted;
        Layout = outcome.Layout;
        RotationCount++;
        ConsolidationCount += outcome.ConsolidationCount;
        Usage = replacementUsage;
        Level = settledLevel;
        _turnsSinceRotation = 0;

        // The previous session is finished with only once its replacement exists and has been
        // adopted. A failure to release it is deliberately not allowed to surface as a rotation
        // failure: the rotation has already succeeded.
        await previous.TryReleaseAsync().ConfigureAwait(false);

        return (true, materialDropped);
    }

    /// <summary>
    ///     Drops the oldest thing the context holds: the oldest slot of the coarsest tier that has
    ///     one, or the oldest verbatim turn when no tier does.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The ring in rule four, brought forward. Normally the coarsest tier sheds its oldest slot
    ///     only when a new one arrives, which is roughly once every sixteen rotations; under
    ///     sustained pressure the context is filling faster than that schedule empties it, so the
    ///     same move is made on demand. The coarsest tier is chosen because its slot covers the
    ///     oldest and least detailed span of the conversation - the part the agent will miss least.
    ///     </para>
    ///     <para>
    ///     <b>Falling through to a verbatim turn is what keeps this a bound rather than a
    ///     preference.</b> Every tier is empty exactly when no consolidation has ever succeeded,
    ///     which is the same condition under which a rotation cannot shrink anything: a summarizer
    ///     that answers blank leaves the material where it is, by design, so without this the tail
    ///     would gain a turn every turn and shed nothing, forever, while each rotation reported
    ///     success and spent a fresh provider session. Binning the oldest page when there are no
    ///     cards left is the only move arithmetic leaves, and it is still a count.
    ///     </para>
    ///     <para>
    ///     The newest turn is never dropped: a session must be able to answer the message it was
    ///     just given.
    ///     </para>
    /// </remarks>
    /// <param name="layout">The layout to reduce.</param>
    /// <returns>Whether anything was dropped, and the layout without it.</returns>
    private static (bool Dropped, ContextLayout Layout) DropOldest(ContextLayout layout)
    {
        for (var index = ContextLayout.TierCount - 1; index >= 0; index--)
        {
            if (layout.Tiers[index].Count == 0)
            {
                continue;
            }

            var tiers = layout.Tiers.ToArray();
            tiers[index] = tiers[index].DropOldest();
            return (true, layout.WithTiers(layout.Tail, tiers));
        }

        if (layout.Tail.TurnCount > 1)
        {
            return (true, layout.WithTail(layout.Tail.DropOldestTurn()));
        }

        return (false, layout);
    }

    /// <summary>
    ///     Releases a provider session nothing has taken ownership of, reporting whether the release
    ///     succeeded and never throwing.
    /// </summary>
    /// <remarks>
    ///     For the two windows in which a provider session belongs to no one — creation and the
    ///     moment a rotation reads a replacement's usage before adopting it. A failure there would
    ///     otherwise discard the only reference to a session the provider still holds. The disposal
    ///     failure is swallowed and reported as a return value so the caller can say which of the
    ///     two states it is in.
    /// </remarks>
    /// <param name="session">The provider session no one owns.</param>
    /// <returns><see langword="true"/> when the provider session was released.</returns>
    private static async ValueTask<bool> TryReleaseUnownedAsync(IProviderSession session)
    {
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            // Intentionally swallowed; the caller reports the failure that brought it here and says
            // what to do about this one.
            return false;
        }
    }

    /// <summary>
    ///     Reads the provider session's account of how full it is.
    /// </summary>
    /// <remarks>
    ///     A single line kept as a named method because it marks the one place a token figure enters
    ///     the engine. There is nothing to choose between and nothing to reconcile: the adapter
    ///     answers for its own provider, and the engine believes it.
    /// </remarks>
    /// <param name="provider">The live provider session.</param>
    /// <returns>The provider session's usage figure.</returns>
    private static ContextUsage ReadUsage(IProviderSession provider) => provider.CurrentUsage;

    /// <summary>
    ///     Derives the conversation size at which this turn should rotate, from the window the
    ///     provider reported.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     One path, one source. The provider session answers how full it is and out of how much,
    ///     and both figures come back together from the same place, so the window, the overhead and
    ///     the conversation compared against the threshold are all counted the same way. There is no
    ///     second window to reconcile and nothing to decide between.
    ///     </para>
    ///     <para>
    ///     Where an adapter's provider does not publish these figures, the adapter supplies them
    ///     itself - reading them from a native API, taking a window given to it at construction, or
    ///     estimating - and owns that choice. The engine asks one question and believes the answer,
    ///     which is what keeps this comparison in a single currency.
    ///     </para>
    /// </remarks>
    /// <param name="usage">The usage figure this turn produced.</param>
    /// <returns>The conversation tokens at which the session rotates.</returns>
    private static int RotationThreshold(ContextUsage usage) =>
        AgentSessionOptions.RotationThresholdFor(usage.WindowTokens - usage.OverheadTokens);

    /// <summary>
    ///     One live provider session together with whether it has been released, held as one value so
    ///     the two can never describe different sessions.
    /// </summary>
    /// <remarks>
    ///     Held here rather than as fields beside a provider reference so that a rotation replaces
    ///     the session and its release flag in the same assignment, which is what keeps the
    ///     disposal-and-retry behavior sound across every failure path.
    /// </remarks>
    private sealed class LiveProviderSession
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="LiveProviderSession"/> class.
        /// </summary>
        /// <param name="session">The provider session this value describes.</param>
        private LiveProviderSession(IProviderSession session) => Session = session;

        /// <summary>
        ///     Gets the provider session itself.
        /// </summary>
        public IProviderSession Session { get; }

        /// <summary>
        ///     Gets a value indicating whether this provider session has actually been released.
        /// </summary>
        /// <remarks>
        ///     Set only once the provider's own disposal has completed, which keeps a failed release
        ///     retryable rather than turning a transient provider failure into a permanent leak.
        /// </remarks>
        public bool Released { get; private set; }

        /// <summary>
        ///     Pairs a provider session with a fresh, unreleased state.
        /// </summary>
        /// <param name="session">The provider session to adopt.</param>
        /// <returns>The adopted live provider session.</returns>
        public static LiveProviderSession Adopt(IProviderSession session) => new(session);

        /// <summary>
        ///     Releases the provider session, propagating whatever failure it reports.
        /// </summary>
        /// <remarks>
        ///     Does nothing once the release has succeeded, so disposing twice is permitted. A
        ///     release that failed leaves <see cref="Released"/> false, so a later call tries again.
        /// </remarks>
        /// <returns>A task that completes when the provider session has been released.</returns>
        public async ValueTask ReleaseAsync()
        {
            if (Released)
            {
                return;
            }

            await Session.DisposeAsync().ConfigureAwait(false);
            Released = true;
        }

        /// <summary>
        ///     Attempts the release and reports whether the provider session is now released.
        /// </summary>
        /// <remarks>
        ///     For the path that must not let an adapter's disposal failure replace the failure it is
        ///     reporting: a rotation that has already succeeded.
        /// </remarks>
        /// <returns><see langword="true"/> when the provider session has been released.</returns>
        public async ValueTask<bool> TryReleaseAsync()
        {
            try
            {
                await ReleaseAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Intentionally swallowed; the caller reports a different failure. Released stays
                // false, so the release remains retryable.
            }

            return Released;
        }
    }
}

/// <summary>
///     Reports that <see cref="CompactingAgentSession.CreateAsync"/> could not hand back the session
///     it created, and carries the provider session when that session could not be released.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why a session creation needs its own failure.</b> Creation is the one path that abandons a
///     session the caller never receives. Everywhere else a caller holding the session can retry a
///     failed release by disposing it again; here the only handle to a provider-side session the
///     provider still holds would be discarded with the instance. The failure therefore carries that
///     handle, so the retryable state is reachable rather than merely recorded.
///     </para>
///     <para>
///     <b>What a caller is expected to do.</b> If <see cref="RetainedProviderSession"/> is not
///     <see langword="null"/>, the provider still holds a session: dispose it, and expect that
///     disposal to be able to fail again. A <see langword="null"/> value means the provider session
///     was released and there is nothing left to clean up; the inner exception is the failure to act
///     on.
///     </para>
///     <para>
///     Derives from <see cref="InvalidOperationException"/> because that is what this condition has
///     always been reported as.
///     </para>
/// </remarks>
public sealed class AgentSessionCreationException : InvalidOperationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AgentSessionCreationException"/> class.
    /// </summary>
    public AgentSessionCreationException()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AgentSessionCreationException"/> class with
    ///     a message.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public AgentSessionCreationException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AgentSessionCreationException"/> class with
    ///     a message and the failure that caused it.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The failure that caused it.</param>
    public AgentSessionCreationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     Gets the provider session the abandoned creation still holds, or <see langword="null"/>
    ///     when it was released.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Non-null only when the provider's own release failed. Disposing it retries that release;
    ///     the retry may fail in turn, which is the adapter's defect and not something this library
    ///     can repair on its behalf.
    ///     </para>
    ///     <para>
    ///     Settable at construction rather than taken as a constructor parameter, because a
    ///     <c>(string, IAsyncDisposable?)</c> overload beside <c>(string, Exception)</c> would make
    ///     <c>new AgentSessionCreationException(message, null)</c> ambiguous. An initializer composes
    ///     with all three constructors.
    ///     </para>
    /// </remarks>
    public IAsyncDisposable? RetainedProviderSession { get; init; }
}
