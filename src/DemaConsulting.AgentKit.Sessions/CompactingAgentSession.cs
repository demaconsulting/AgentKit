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
    ///     Fixed overhead the first provider session folded into its own conversation count instead
    ///     of breaking out, measured at the one moment it is directly observable.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Measured, not assumed.</b> The first provider session is seeded with no history at
    ///     all, so whatever a provider-reported figure attributes to the conversation at that
    ///     instant is not conversation: it is the system prompt, the tool declarations and whatever
    ///     framing of its own the provider charges for, counted in the provider's tokens and folded
    ///     into a figure it did not split. That is the only moment the fold is visible, and it is
    ///     visible exactly.
    ///     </para>
    ///     <para>
    ///     Zero for a provider that reports the split — its empty conversation is zero — and zero
    ///     for an estimated figure, whose split this library makes itself. So this changes nothing
    ///     for either adapter shipped today and exists entirely for the third:
    ///     <see cref="ContextUsage.FromProvider(int, int, int?)"/> is public and documented to
    ///     accept totals alone, and the session that receives them has to know what it is not being
    ///     told. See <see cref="EnsureReportedWindowConvergesAsync"/>.
    ///     </para>
    /// </remarks>
    private readonly int _unreportedOverheadTokens;

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

        // The layout above carries no history, and the provider session was seeded with none, so
        // this reading is taken against an empty conversation. Anything a provider-reported figure
        // calls conversation here is therefore fixed overhead it did not break out, measured in its
        // own tokens. See the field's remarks.
        _unreportedOverheadTokens =
            Usage.Origin == ContextUsageOrigin.Provider ? Usage.ConversationTokens : 0;
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
    ///     provider session reports a context window this session could not converge in.
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

        // A provider that reports its window does so from the moment it exists, so a window the
        // session could not converge in is knowable before the first turn is ever spent against it.
        // Refused here for the same reason AgentSessionOptions refuses the equivalent configured
        // window at construction: a rotated context would never land below the rotation threshold.
        await session.EnsureReportedWindowConvergesAsync().ConfigureAwait(false);
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
    ///     A provider that reports a context window this session could not converge in abandons the
    ///     session: the live provider is released and an
    ///     <see cref="InvalidOperationException"/> is thrown. That applies to a replacement adopted
    ///     by a rotation as much as to the session the turn was taken against, so a rotation into an
    ///     unusable session is reported as this turn's failure rather than the next turn's. See
    ///     <see cref="EnsureReportedWindowConvergesAsync"/> for why that is preferred to adapting.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     The provider session factory returned <see langword="null"/> during a rotation, or the
    ///     live provider — or a replacement a rotation adopted — reports a context window this
    ///     session could not converge in.
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
        //
        // Recorded in one append rather than two. A transcript is immutable and copies its whole
        // backing array on every append, so appending the message and then the turn's entries
        // copied a filling window twice per turn: a long conversation - the entire case this
        // package exists for - paid quadratic copying with double the constant. Collecting both
        // halves into one sequence leaves the observable order identical and halves the copying,
        // and it keeps the property a deliberate earlier change established: nothing at all is
        // appended until SendAsync has returned, so a provider that refused the turn leaves behind
        // no ghost message.
        Layout = Layout.WithTranscript(
            Layout.Transcript.Append([TranscriptEntry.User(message), .. turn.Entries]));

        Usage = ReadUsage(_provider, _options, Layout);

        // A reported window this session could not converge in makes rotation incapable of settling,
        // so it is refused here rather than allowed to thrash. A provider may only begin reporting -
        // or report a smaller window - after a turn, so the check belongs on every turn and not
        // merely at creation.
        await EnsureReportedWindowConvergesAsync().ConfigureAwait(false);

        // Compare the conversation against the threshold. Both figures come from the same usage
        // reading, so both are in the same currency: a provider that reported its own conversation
        // count is measured entirely in that provider's tokens, and an estimate is measured
        // entirely in ours. Nothing is subtracted here, because there is no figure left to subtract
        // - the usage already carries the split.
        if (Usage.ConversationTokens < RotationThreshold(Usage, _options))
        {
            return new AgentSessionResponse(turn.ResponseText, Usage, rotationOccurred: false);
        }

        var rotation = await RotateAsync(cancellationToken).ConfigureAwait(false);
        return new AgentSessionResponse(
            turn.ResponseText, Usage, rotation.Occurred, rotation.Saturations);
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
    ///     <para>
    ///     The order is deliberate: consolidate first, create the replacement second, adopt it and
    ///     update this session's state third, dispose the old session last. A summarizer failure
    ///     therefore leaves the session exactly as it was, still able to answer, rather than leaving
    ///     it with no provider session at all. The adoption carries no await, so the live provider,
    ///     the layout and the counters can never be observed describing different sessions; and a
    ///     provider that fails to dispose does not fail the rotation, because by then the rotation
    ///     has already succeeded.
    ///     </para>
    ///     <para>
    ///     <b>A rotation that consolidated nothing is abandoned here rather than carried out.</b>
    ///     The engine returns the layout unchanged when the transcript already fits tier zero, and
    ///     for the engine — a pure function over a layout — that genuinely costs nothing. It is not
    ///     free at this level: carrying it out would create a replacement provider session, dispose
    ///     the live one, increment <see cref="RotationCount"/> and report a rotation to the caller,
    ///     all to arrive at exactly the context the session already had. Repeated every turn, that
    ///     is a provider session per turn spent to achieve nothing, and it is invisible, because no
    ///     consolidation ran and so nothing saturated. The convergence invariant asserted by
    ///     <see cref="AgentSessionOptions"/> makes this unreachable for a layout sitting within its
    ///     tier budgets — the threshold it guarantees exceeds the coarse tiers and their framing by
    ///     more than tier zero's budget, so anything able to cross the threshold must overflow tier
    ///     zero. It stays reachable for a layout whose tier is over budget, which is precisely the
    ///     saturated case, so the guard is kept rather than argued away.
    ///     </para>
    ///     <para>
    ///     <b>The currency that crossed the threshold is passed to the engine, because only this
    ///     turn knows it.</b> The threshold comparison above is made in whichever currency the usage
    ///     figure carries, while the engine's split is measured in estimated tokens throughout. When
    ///     a provider reported the crossing the two disagree by construction, and the engine is told
    ///     so through <see cref="ContextUsage.Origin"/>: a split that finds tier zero has room then
    ///     consolidates the whole verbatim history rather than abandoning a rotation the provider's
    ///     own count asked for. Without that, a provider counting more than this library estimates
    ///     crossed the threshold, consolidated nothing, seeded no replacement, and ran on into its
    ///     own compactor — the one outcome this package exists to prevent — with the trigger and the
    ///     split each behaving exactly as documented in its own currency.
    ///     </para>
    ///     <para>
    ///     <b>The replacement is validated before the rotation is reported as successful.</b> A
    ///     factory may return a session reporting a different context window from the one it
    ///     replaced, and a replacement this session could not converge in is unusable — so it is
    ///     held to the same rule the live session is held to on every turn, and the session is
    ///     abandoned here rather than at the next turn, after the caller has been told the rotation
    ///     succeeded. See <see cref="EnsureReportedWindowConvergesAsync"/>.
    ///     </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the rotation.</param>
    /// <returns>Whether a rotation was actually carried out, and any saturation it reported.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The provider session factory returned null, or the replacement it produced reports a
    ///     context window this session could not converge in.
    /// </exception>
    private async Task<(bool Occurred, IReadOnlyList<SaturationSignal> Saturations)> RotateAsync(
        CancellationToken cancellationToken)
    {
        var outcome = await RotationEngine
            .RotateAsync(Layout, _options.Summarizer, Usage.Origin, cancellationToken)
            .ConfigureAwait(false);

        // Nothing aged out, so there is no new context to seed a replacement from. Report the turn
        // as the ordinary turn it was; see the remarks above for why this is not a rotation.
        if (outcome.ConsolidationCount == 0)
        {
            return (false, outcome.Saturations);
        }

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

        // The replacement is held to the same window rule the session it replaced was held to. A
        // factory is under no obligation to return a session like the one before it - a routed
        // deployment, a changed model or a downgraded tier all report a smaller window - and the
        // usage read during the adoption above establishes what that window is without asking
        // whether the session could converge in it. Checked here rather than left to the next turn,
        // because by then the caller has been told this rotation succeeded and has sent another
        // message to a session that cannot settle.
        //
        // Placed after the superseded session has been released, so abandoning the rotation over an
        // unusable replacement does not also leak the session it replaced. The check releases the
        // replacement itself and marks this session disposed before it throws; that release can fail
        // like any other, which is why the message it throws claims only that release was attempted.
        await EnsureReportedWindowConvergesAsync().ConfigureAwait(false);

        return (true, outcome.Saturations);
    }

    /// <summary>
    ///     Abandons this session when the live provider reports a context window too small for this
    ///     session to converge.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The invariant being asserted: a rotated context must land strictly below the rotation
    ///     threshold.</b> A rotation leaves the conversation holding at most the policy's tier
    ///     budgets plus the framing their seeded records carry. If the threshold derived from the
    ///     reported window is at or below that figure, the very layout a rotation produces is
    ///     already over the threshold, so the next turn rotates again, and so does the one after —
    ///     a summarizer call and a provider session spent per turn, forever, with no saturation
    ///     signal raised because each individual consolidation reduces perfectly normally.
    ///     </para>
    ///     <para>
    ///     <b>Overhead a provider does not break out is part of that figure, and is credited
    ///     here.</b> <see cref="ContextUsage.FromProvider(int, int, int?)"/> lets an adapter report
    ///     totals without a split, in which case <see cref="ContextUsage.OverheadTokens"/> is zero
    ///     and the reported conversation is the whole of the usage. For the rotation
    ///     <em>trigger</em> that default is safe, because it compares an inflated conversation
    ///     against a threshold taken from the whole window and so fires early. For this check it is
    ///     not, and the same default being safe in one place and unsafe in the other is exactly how
    ///     this was missed: crediting no overhead here makes the window look larger than it is,
    ///     while the figure a rotated context will actually report still carries the fold. A window
    ///     of 500 tokens with a rotated bound of 311 passes a threshold of 350 while the provider
    ///     quietly charges 100 tokens it never broke out — and the replacement, reported at 411
    ///     against that same 350, rotates on every following turn. So
    ///     <see cref="_unreportedOverheadTokens"/> is added to the bound the threshold must exceed,
    ///     which is where it belongs: it is on the side of the comparison the reported conversation
    ///     figure is on. The trigger is deliberately left alone, and the two are then exactly
    ///     consistent — a rotated context reports at most the bound plus the fold, and this check
    ///     has established that the threshold exceeds it.
    ///     </para>
    ///     <para>
    ///     <b>What an adapter that genuinely cannot split its counts should do: nothing.</b> It
    ///     keeps reporting totals alone. Requiring a split, or making the adapter invent one, was
    ///     rejected: an invented split is indistinguishable from a measured one at the point it is
    ///     consumed, which is the very thing <see cref="ContextUsage"/> exists to prevent, and
    ///     refusing an unsplit provider outright would send an otherwise perfectly good adapter down
    ///     the estimating path, where this library's character ratio decides when a real provider
    ///     rotates. Measuring the fold against the empty conversation the session starts from costs
    ///     the adapter nothing, is exact rather than estimated, and is in the provider's own tokens.
    ///     The one thing such an adapter must do is report from the moment the session exists rather
    ///     than only after the first turn, because the empty conversation is the only moment the
    ///     fold is separable; an adapter that begins reporting later is credited only what it breaks
    ///     out, as it was before.
    ///     </para>
    ///     <para>
    ///     <b>Failing rather than adapting, deliberately.</b> Requiring merely that the window
    ///     <em>hold</em> the construction bound is the necessary condition, not the sufficient one,
    ///     and asserting it was the defect: it admitted every window between the bound and the
    ///     bound divided by the rotation fraction, which are exactly the windows that thrash.
    ///     <see cref="AgentSessionOptions.MinimumEffectiveWindowTokens"/> computes the sufficient
    ///     condition and is shared with the configured-window guard, so the two can never diverge.
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
    ///     a session could not converge in is refused — and differs only in when the figure becomes
    ///     knowable.
    ///     </para>
    ///     <para>
    ///     The live provider is released before the exception is thrown, because the session is
    ///     being abandoned mid-life and a caller that receives this exception has no session to
    ///     dispose. A failure to release is swallowed rather than allowed to replace the
    ///     configuration error, which is the one the caller can act on; the release flag is set only
    ///     on success, so an explicit <see cref="DisposeAsync"/> still retries it. The message
    ///     therefore says the release was <em>attempted</em> rather than achieved: it is thrown on
    ///     the path where the attempt may have failed, and on the <see cref="CreateAsync"/> path the
    ///     caller is handed no session to retry it with, so a claim that the provider had been
    ///     released would be untrue in exactly the case it was reporting.
    ///     </para>
    /// </remarks>
    /// <returns>A task that completes when the window has been accepted.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The provider reports a context window this session could not converge in.
    /// </exception>
    private async Task EnsureReportedWindowConvergesAsync()
    {
        // Only a reported window is checked. An estimate carries the configured window, which
        // AgentSessionOptions already refused if the session could not converge in it.
        //
        // The overhead removed is the provider's own - the reported total less the reported
        // conversation - not this library's estimate of it, so the effective window is in the same
        // currency as the window it came from. The tier budgets it is then compared against are
        // still this library's estimated tokens, because that is the currency a host configures
        // them in and the currency the layout enforces them in; this check therefore remains a
        // comparison between an estimated bound and a reported window, and is honest about being
        // approximate. What it no longer does is corrupt the reported window itself.
        //
        // The bound carries the unreported overhead allowance as well as the tier budgets, because
        // the figure a rotated context will be compared against carries it too. That term is the
        // provider's own tokens, measured against an empty conversation; see the remarks above for
        // why it is added to the bound rather than removed from the window.
        var minimumEffective = AgentSessionOptions.MinimumEffectiveWindowTokens(
            _options.Compaction, _unreportedOverheadTokens);
        var effective = Usage.WindowTokens - Usage.OverheadTokens;
        if (Usage.Origin != ContextUsageOrigin.Provider
            || AgentSessionOptions.ConvergesAt(effective, _options.Compaction, _unreportedOverheadTokens))
        {
            return;
        }

        _disposed = true;

        // Released unconditionally. Every entry point holds a provider session that has not been
        // released: CreateAsync holds a freshly created one, SendAsync has already thrown
        // ObjectDisposedException if this session was disposed, and RotateAsync has just adopted a
        // replacement and disposed only the session it superseded - so the release flag is false at
        // every entry and a guard on it only asserted something already known.
        try
        {
            await _provider.DisposeAsync().ConfigureAwait(false);
            _released = true;
        }
        catch (Exception)
        {
            // Intentionally swallowed. The configuration defect below is the failure the caller
            // can act on, and replacing it with an adapter's disposal failure would hide it.
            // The release flag stays false, so disposing this session again retries the release -
            // which is why the message below claims only that the release was attempted.
        }

        throw new InvalidOperationException(
            $"The provider reports a context window of {Usage.WindowTokens} tokens, leaving "
            + $"{effective} tokens once the {Usage.OverheadTokens} tokens of overhead it reports "
            + "outside the conversation are paid for. A rotated context occupies up to "
            + $"{_options.Compaction.TotalTierBudgetTokens} tokens of tier budgets and "
            + $"{ContextLayout.SeedFramingTokens(_options.Compaction)} tokens of framing for their "
            + $"seeded records, and is counted as carrying a further {_unreportedOverheadTokens} "
            + "tokens of overhead this provider folds into its conversation figure rather than "
            + "reporting separately. All of that must land below the rotation threshold, which "
            + $"requires at least {minimumEffective} tokens. The session is abandoned rather than "
            + "left to rotate on every turn without ever getting under its own threshold; release "
            + "of the provider session was attempted, and if that attempt failed the provider still "
            + "holds it and the release needs retrying.");
    }

    /// <summary>
    ///     Reads usage from the provider when it reports any, and estimates it otherwise.
    /// </summary>
    /// <remarks>
    ///     Static because it depends on nothing but its arguments, which makes the preference rule —
    ///     provider figures over our own — a single visible decision rather than something scattered
    ///     across the call sites that need a usage figure.
    ///     <para>
    ///     The estimated figure carries the layout's own conversation total alongside its total, so
    ///     the split it publishes is estimated on both sides, exactly as the provider's is reported
    ///     on both sides. Nothing downstream ever has to mix the two.
    ///     </para>
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

        return ContextUsage.FromEstimate(
            layout.TotalEstimatedTokens, options.ProviderWindowTokens, layout.ConversationTokens);
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
    ///     <b>The overhead removed from a reported window is the provider's own.</b> It comes from
    ///     <see cref="ContextUsage.OverheadTokens"/>, which is the reported total less the reported
    ///     conversation — both counted by the provider's tokenizer — so the window, the overhead and
    ///     the conversation the threshold is compared against are all in one currency.
    ///     <see cref="AgentSessionOptions.FixedOverheadTokens"/> is this library's character-ratio
    ///     estimate and is deliberately not used here: subtracting an estimate from a measurement
    ///     would put the threshold in no currency at all, and tool declarations — nested JSON
    ///     schemas — are exactly the material that ratio serves worst. A provider that reports no
    ///     split is credited no overhead at all, which rotates earlier rather than later and so
    ///     errs on the side the guarantee needs. That is true <em>here</em> and is not true of the
    ///     convergence check, which is why
    ///     <see cref="EnsureReportedWindowConvergesAsync"/> credits the fold it measures instead of
    ///     inheriting this default.
    ///     </para>
    ///     <para>
    ///     When the figure is the library's own estimate it was taken against the configured window,
    ///     so the threshold the options already computed is the matching one and is used unchanged.
    ///     </para>
    ///     <para>
    ///     Both paths call <see cref="AgentSessionOptions.RotationThresholdFor"/>, so they are
    ///     literally the same arithmetic and differ only in which window they measure. The
    ///     subtraction always leaves something to take a fraction of, because
    ///     <see cref="EnsureReportedWindowConvergesAsync"/> has already refused any reported window
    ///     this session could not converge in, and convergence requires an effective window larger
    ///     than every tier budget put together. The floor guards only against a rotation fraction
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

        // The subtraction always leaves a positive budget: a reported window this session could not
        // converge in has already been refused, and convergence requires an effective window larger
        // than every tier budget put together. The floor of one token inside RotationThresholdFor
        // guards only against a rotation fraction small enough to truncate away, exactly as the
        // configured threshold does.
        return AgentSessionOptions.RotationThresholdFor(
            usage.WindowTokens - usage.OverheadTokens, options.Compaction);
    }
}
