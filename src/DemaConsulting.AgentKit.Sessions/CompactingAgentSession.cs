namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     The session that keeps its own transcript, watches the window, and rotates into a fresh
///     provider session when the context fills.
/// </summary>
/// <remarks>
///     <para>
///     <b>This is where the parts meet.</b> The options say what the agent is and when to compact;
///     the layout accounts for the context; the rotation engine ages it; the provider-session
///     factory produces the replacement. This class owns the sequencing and nothing else, which is
///     what keeps every other part independently testable.
///     </para>
///     <para>
///     <b>Rotation is a replacement, not an edit.</b> When the conversation crosses the rotation
///     threshold, the engine consolidates older history, a new provider session is created and
///     adopted seeded from the preserved content, and only then is the session it replaced
///     disposed. That is the only reduction both provider shapes support — one re-sends history
///     each turn, the other holds it server-side — and it is why the behavior is identical on
///     either.
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
///             if (response.IsSaturated)
///             {
///                 // No redundancy left to remove: further rotations will not buy room.
///                 Console.WriteLine($"Context saturated after {session.RotationCount} rotations.");
///                 break;
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
    ///     The live provider session, replaced at every rotation.
    /// </summary>
    private IProviderSession _provider;

    /// <summary>
    ///     Whether this session has been disposed, and so refuses further turns.
    /// </summary>
    private bool _disposed;

    /// <summary>
    ///     Whether the live provider session has actually been released.
    /// </summary>
    /// <remarks>
    ///     Tracked separately from <see cref="_disposed"/> because the two answer different
    ///     questions: whether this session may still be used, and whether anything is still held on
    ///     the provider's side. A release that failed leaves the second false, which is what allows
    ///     it to be retried.
    /// </remarks>
    private bool _released;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CompactingAgentSession"/> class.
    /// </summary>
    /// <remarks>
    ///     Private because creating the first provider session is asynchronous and a constructor
    ///     cannot await; <see cref="CreateAsync"/> is the entry point.
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
        _provider = provider;

        Layout = ContextLayout.Create(options.Compaction, options.SystemTokens, options.ToolDeclarationTokens);
        Usage = ReadUsage(provider, options, Layout);
    }

    /// <summary>
    ///     Gets the engine's own account of the context: the fixed overhead, the coarse tiers, and
    ///     the verbatim recent history.
    /// </summary>
    /// <remarks>
    ///     Exposed because it is the whole state a rotation acts on, and because an application
    ///     reasoning about its own tier budgets needs to see what is actually in them. Replaced
    ///     wholesale on every turn; the instance read here is a snapshot.
    /// </remarks>
    public ContextLayout Layout { get; private set; }

    /// <inheritdoc/>
    public ContextUsage Usage { get; private set; }

    /// <inheritdoc/>
    public int RotationCount { get; private set; }

    /// <summary>
    ///     Gets the total consolidations every rotation of this session has performed.
    /// </summary>
    /// <remarks>
    ///     Summarizer calls are the dominant cost of this arrangement, and the tiered scheme's
    ///     advantage over a flat rolling summary is partly that it makes fewer of them. Exposing the
    ///     running total lets an application see that cost rather than infer it.
    /// </remarks>
    public int ConsolidationCount { get; private set; }

    /// <summary>
    ///     Creates a session and its first provider session.
    /// </summary>
    /// <remarks>
    ///     The first provider session is seeded with no history, because there is none yet — the
    ///     instructions and tools it carries are the same ones every later rotation will carry.
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
    ///     <paramref name="providerSessionFactory"/> returned <see langword="null"/>, or the created
    ///     provider session reports a context window too small to hold the configured construction
    ///     bound.
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

        var session = new CompactingAgentSession(options, providerSessionFactory, provider);

        // A provider that reports its window does so from the moment it exists, so a window that
        // cannot hold the bound is knowable before the first turn is ever spent against it. Refused
        // here for the same reason AgentSessionOptions refuses the equivalent configured window at
        // construction: the session could never rotate back within its own bound.
        await session.EnsureReportedWindowHoldsBoundAsync().ConfigureAwait(false);
        return session;
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
    ///     <para>
    ///     A provider that reports a context window too small to hold this session's construction
    ///     bound abandons the session: the live provider is released and an
    ///     <see cref="InvalidOperationException"/> is thrown. See
    ///     <see cref="EnsureReportedWindowHoldsBoundAsync"/> for why that is preferred to adapting.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     The provider session factory returned <see langword="null"/> during a rotation, or the
    ///     live provider reports a context window too small to hold the construction bound.
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

        // Take the turn against the live session. The answer belongs to this session; any rotation
        // below prepares the replacement for the turn after.
        //
        // Nothing is recorded until the provider has accepted the message. A provider is entitled
        // to honor cancellation or fail before it takes the turn - the in-memory provider does
        // exactly that for a token that was already canceled - and a message recorded ahead of that
        // would be a turn no provider ever saw, which a later rotation would nonetheless
        // consolidate and seed into the replacement session.
        var turn = await _provider.SendAsync(message, cancellationToken).ConfigureAwait(false);

        // The turn's entries end with the answer, whether the adapter supplied entries or not, so
        // recording them records the whole turn - message, any tool work, and what the agent
        // concluded. That last part is what a later turn, seeded after a rotation from this very
        // transcript, needs in order to see what it already decided.
        Layout = Layout.WithTranscript(
            Layout.Transcript.Append(TranscriptEntry.User(message)).Append(turn.Entries));

        Usage = ReadUsage(_provider, _options, Layout);

        // A reported window too small to hold the construction bound makes rotation incapable of
        // making progress, so it is refused here rather than allowed to thrash. A provider may only
        // begin reporting - or report a smaller window - after a turn, so the check belongs on every
        // turn and not merely at creation.
        await EnsureReportedWindowHoldsBoundAsync().ConfigureAwait(false);

        // Compare conversation tokens - usage with the fixed overhead removed - against the
        // threshold, so the comparison means the same thing whether the figure came from the
        // provider or from our own estimate. The threshold is derived from the same window the
        // usage figure was measured against, for the same reason.
        var conversationTokens = Math.Max(0, Usage.UsedTokens - _options.FixedOverheadTokens);
        if (conversationTokens < RotationThreshold(Usage, _options))
        {
            return new AgentSessionResponse(turn.ResponseText, Usage, rotationOccurred: false);
        }

        var saturations = await RotateAsync(cancellationToken).ConfigureAwait(false);
        return new AgentSessionResponse(turn.ResponseText, Usage, rotationOccurred: true, saturations);
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
    ///     which swallows the same failure because by then it has already succeeded and the session
    ///     is coherent against its replacement.
    ///     </para>
    ///     <para>
    ///     Because the failure propagates, disposal stays retryable: this session is marked disposed
    ///     from the first call, so it refuses further turns either way, but the provider is
    ///     considered released only once its own disposal has completed. A later call therefore
    ///     tries again rather than returning as though the release had happened, so a transient
    ///     provider failure does not become a permanent leak. Once the release has succeeded,
    ///     disposing again is permitted and does nothing.
    ///     </para>
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        // Marked before the release is attempted, and regardless of how it ends. The caller has
        // given this session up; a turn taken after that would go to a provider session this object
        // is in the middle of releasing.
        _disposed = true;

        if (_released)
        {
            return;
        }

        await _provider.DisposeAsync().ConfigureAwait(false);
        _released = true;
    }

    /// <summary>
    ///     Ages the context by one rotation and replaces the live provider session with one seeded
    ///     from the result.
    /// </summary>
    /// <remarks>
    ///     The order is deliberate: consolidate first, create the replacement second, adopt it and
    ///     update this session's state third, dispose the old session last. A summarizer failure
    ///     therefore leaves the session exactly as it was, still able to answer, rather than leaving
    ///     it with no provider session at all. The adoption carries no await, so the live provider,
    ///     the layout and the counters can never be observed describing different sessions; and a
    ///     provider that fails to dispose does not fail the rotation, because by then the rotation
    ///     has already succeeded.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the rotation.</param>
    /// <returns>Any saturation the rotation reported.</returns>
    /// <exception cref="InvalidOperationException">The provider session factory returned null.</exception>
    private async Task<IReadOnlyList<SaturationSignal>> RotateAsync(CancellationToken cancellationToken)
    {
        var outcome = await RotationEngine
            .RotateAsync(Layout, _options.Summarizer, cancellationToken)
            .ConfigureAwait(false);

        var seed = new ProviderSessionSeed(
            _options.Instructions,
            _options.Tools,
            outcome.Layout.BuildSeed());

        var replacement = await _factory.CreateAsync(seed, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The provider session factory returned null.");

        // The whole state transition happens here, as one block containing no await: the provider
        // reference, the layout and the counters describe the same session at every point an
        // exception could be observed. Doing it before disposal - rather than around it, as it was
        // - is what makes that true, because the await on disposal was the one place this object
        // could be left pointing at the replacement while its layout and counters still described
        // the session it replaced, and the next turn would then append to, and possibly rotate, the
        // wrong transcript.
        var previous = _provider;
        _provider = replacement;
        Layout = outcome.Layout;
        RotationCount++;
        ConsolidationCount += outcome.ConsolidationCount;
        Usage = ReadUsage(replacement, _options, Layout);

        // The previous session is finished with only once its replacement exists and has been
        // adopted. For a provider holding history server-side this is what actually discards it.
        //
        // A failure to release it is deliberately not allowed to surface as a rotation failure. The
        // rotation has already succeeded: the context was consolidated, the replacement was created
        // and this session is coherent against it. Throwing it on would report the opposite to the
        // caller and leave it holding a session it would reasonably believe to be broken. The cost
        // of an adapter that cannot dispose is a provider-side session that outlives its use - the
        // adapter's own defect, which discarding a good session on top of it does not repair.
        try
        {
            await previous.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Intentionally swallowed; see above. Nothing is caught that the caller could act on:
            // the session being disposed is one this object has already given up all reference to.
        }

        return outcome.Saturations;
    }

    /// <summary>
    ///     Abandons this session when the live provider reports a context window too small to hold
    ///     the layout's construction bound.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Failing rather than adapting, deliberately.</b> The bound is the most this layout can
    ///     ever occupy: the fixed overhead, every tier budget, and the framing each seeded tier
    ///     record carries. A window below it cannot hold a freshly rotated context, so rotation
    ///     cannot make progress. With a reported window below the bound the threshold derived from
    ///     it is crossed on nearly every turn while <see cref="RotateAsync"/> still splits tier zero
    ///     at the policy's budget, so each rotation hands back a layout that is still over the
    ///     reported window and re-seeds it — a thrash loop that spends a summarizer call and a
    ///     provider session per turn and never converges.
    ///     </para>
    ///     <para>
    ///     Adapting was considered and rejected. Shrinking the tier budgets to fit would silently
    ///     discard the compaction policy the host configured and would have to re-consolidate
    ///     records already written against larger budgets, which is the one thing the layout is
    ///     built to make impossible; ignoring the reported window would reinstate the defect the
    ///     reported-window override exists to remove, letting the session run past the provider's
    ///     own compactor. Neither is a behavior an application could reason about, and both hide a
    ///     host configuration defect that the host alone can repair. The configured window is
    ///     already refused for exactly this condition, by
    ///     <see cref="AgentSessionOptions"/> at construction; failing here keeps one rule — a window
    ///     that cannot hold the bound is refused — and differs only in when the figure becomes
    ///     knowable.
    ///     </para>
    ///     <para>
    ///     The live provider is released before the exception is thrown, because the session is
    ///     being abandoned mid-life and a caller that receives this exception has no session to
    ///     dispose. A failure to release is swallowed rather than allowed to replace the
    ///     configuration error, which is the one the caller can act on; the release flag is set only
    ///     on success, so an explicit <see cref="DisposeAsync"/> still retries it.
    ///     </para>
    /// </remarks>
    /// <returns>A task that completes when the window has been accepted.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The provider reports a context window smaller than the construction bound.
    /// </exception>
    private async Task EnsureReportedWindowHoldsBoundAsync()
    {
        // Only a reported window is checked. An estimate carries the configured window, which
        // AgentSessionOptions already refused if it could not hold the bound.
        var bound = Layout.MaximumBoundTokens;
        if (Usage.Origin != ContextUsageOrigin.Provider || Usage.WindowTokens >= bound)
        {
            return;
        }

        _disposed = true;
        if (!_released)
        {
            try
            {
                await _provider.DisposeAsync().ConfigureAwait(false);
                _released = true;
            }
            catch (Exception)
            {
                // Intentionally swallowed. The configuration defect below is the failure the caller
                // can act on, and replacing it with an adapter's disposal failure would hide it.
                // The release flag stays false, so disposing this session again retries the release.
            }
        }

        throw new InvalidOperationException(
            $"The provider reports a context window of {Usage.WindowTokens} tokens, but this "
            + $"session's construction bound is {bound} tokens: the fixed overhead of "
            + $"{_options.FixedOverheadTokens}, the policy's {_options.Compaction.TotalTierBudgetTokens} "
            + "tokens of tier budgets, and the framing their seeded records carry. A rotation could "
            + "never bring the context back within the reported window, so the session has been "
            + "released rather than left to rotate on every turn without making progress.");
    }

    /// <summary>
    ///     Reads usage from the provider when it reports any, and estimates it otherwise.
    /// </summary>
    /// <remarks>
    ///     Static because it depends on nothing but its arguments, which makes the preference rule —
    ///     provider figures over our own — a single visible decision rather than something scattered
    ///     across the call sites that need a usage figure.
    /// </remarks>
    /// <param name="provider">The live provider session.</param>
    /// <param name="options">The configured window, used when estimating.</param>
    /// <param name="layout">The engine's own account of the context, used when estimating.</param>
    /// <returns>The usage figure, marked with where it came from.</returns>
    private static ContextUsage ReadUsage(
        IProviderSession provider,
        AgentSessionOptions options,
        ContextLayout layout)
    {
        // A provider's own figures count framing this library never sees, so prefer them whenever
        // they are offered.
        if (provider is IContextUsageReporter reporter && reporter.CurrentUsage is { } reported)
        {
            return reported;
        }

        return ContextUsage.FromEstimate(layout.TotalEstimatedTokens, options.ProviderWindowTokens);
    }

    /// <summary>
    ///     Derives the conversation size at which this turn should rotate, from the same window the
    ///     usage figure was measured against.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>A provider that reports its own window overrides the configured one.</b> The whole
    ///     point of rotating at a fraction of the window is that the provider's own compactor never
    ///     fires, and that guarantee is about the window the provider actually has. A host that
    ///     configures 128,000 tokens against a provider reporting 32,000 would otherwise be allowed
    ///     four times past the provider's own threshold — precisely the failure this package exists
    ///     to prevent — and the reverse mismatch would rotate long before it needed to, spending
    ///     summarizer tokens and prompt cache for nothing.
    ///     </para>
    ///     <para>
    ///     When the figure is the library's own estimate it was taken against the configured window,
    ///     so the threshold the options already computed is the matching one and is used unchanged.
    ///     </para>
    ///     <para>
    ///     The fixed overhead is subtracted first and the result floored at one token, exactly as
    ///     <see cref="AgentSessionOptions.RotationThresholdTokens"/> does, so the two paths differ
    ///     only in which window they measure. The subtraction always leaves something to take a
    ///     fraction of, because <see cref="EnsureReportedWindowHoldsBoundAsync"/> has already
    ///     refused any reported window below the construction bound, and that bound exceeds the
    ///     fixed overhead by every tier budget. The floor guards only against a rotation fraction
    ///     small enough to truncate to zero.
    ///     </para>
    /// </remarks>
    /// <param name="usage">The usage figure this turn produced, and where it came from.</param>
    /// <param name="options">What the application configured.</param>
    /// <returns>The conversation tokens at which the session rotates.</returns>
    private static int RotationThreshold(ContextUsage usage, AgentSessionOptions options)
    {
        // An estimate was measured against the configured window, so the configured threshold is
        // already the matching one.
        if (usage.Origin != ContextUsageOrigin.Provider)
        {
            return options.RotationThresholdTokens;
        }

        // The subtraction always leaves a positive budget: a reported window below the construction
        // bound has already been refused, and the bound exceeds the fixed overhead by every tier
        // budget. The floor of one token guards only against a rotation fraction small enough to
        // truncate away, exactly as the configured threshold does.
        var effective = usage.WindowTokens - options.FixedOverheadTokens;
        return Math.Max(1, (int)(effective * options.Compaction.RotationThreshold));
    }
}
