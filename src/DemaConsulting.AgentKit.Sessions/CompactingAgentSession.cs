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
    ///     The live provider session and everything that belongs to it, replaced whole at every
    ///     rotation.
    /// </summary>
    /// <remarks>
    ///     The reference is to a <see cref="LiveProviderSession"/> rather than to an
    ///     <see cref="IProviderSession"/> because the fold a provider charges without reporting, and
    ///     whether that provider has been released, are properties of <em>that</em> provider session
    ///     and not of this object. Holding them here as separate fields is what let a fold measured
    ///     against the first provider session go on validating replacements it was never measured
    ///     from; adopting a replacement now replaces them together, because they are one value.
    /// </remarks>
    private LiveProviderSession _live;

    /// <summary>
    ///     Whether this session has been disposed, and so refuses further turns.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="LiveProviderSession.Released"/> because the two answer different
    ///     questions: whether this session may still be used, and whether anything is still held on
    ///     the provider's side. A release that failed leaves the second false, which is what allows
    ///     it to be retried.
    /// </remarks>
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CompactingAgentSession"/> class.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Private because creating the first provider session is asynchronous and a constructor
    ///     cannot await; <see cref="CreateAsync"/> is the entry point.
    ///     </para>
    ///     <para>
    ///     <b>This constructor is a window in which <paramref name="provider"/> is owned by nothing,
    ///     and it can throw.</b> <see cref="ReadUsage"/> evaluates adapter code, and the figure it
    ///     builds is validated rather than clamped, so an adapter whose split is arithmetically
    ///     impossible throws out of here — a condition
    ///     <see cref="ContextUsage(int, int, int, ContextUsageOrigin)"/> designs for. Nothing inside
    ///     a constructor can release the argument it was handed, so the window is closed by the
    ///     caller: <see cref="CreateAsync"/> releases the provider itself when this throws. Keeping
    ///     the window narrow is not enough, because the throw is by design rather than by accident.
    ///     </para>
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

        Layout = ContextLayout.Create(options.Compaction, options.SystemTokens, options.ToolDeclarationTokens);

        var usage = ReadUsage(provider, options, Layout);

        // Seeded with nothing, so nothing it reports as conversation is conversation: the whole of
        // that figure is fold. See LiveProviderSession.Adopt.
        _live = LiveProviderSession.Adopt(provider, usage, seededConversationTokens: 0);
        Usage = usage;
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
    ///     <para>
    ///     The first provider session is seeded with no history, because there is none yet — the
    ///     instructions and tools it carries are the same ones every later rotation will carry.
    ///     </para>
    ///     <para>
    ///     <b>Nothing this method creates is left unowned.</b> Between the factory returning a
    ///     provider session and this session taking ownership of it there is a window in which the
    ///     provider session belongs to no one, and that window can throw: constructing this session
    ///     reads the provider's usage, which is adapter code, and an adapter whose split is
    ///     arithmetically impossible is refused rather than clamped. Left alone, the instance would
    ///     be discarded, the exception would carry no handle, and a provider holding the
    ///     conversation server-side would keep it forever with nothing anywhere able to name it. So
    ///     the construction is guarded: the provider session is released, and the release is what
    ///     decides how the failure is reported.
    ///     </para>
    ///     <para>
    ///     <b>A released provider session means the original failure is reported unchanged.</b> It
    ///     is the adapter's defect, the stack still points at the adapter that wrote it — which is
    ///     the whole reason <see cref="ContextUsage"/> refuses rather than clamps — and there is
    ///     nothing left for a caller to clean up, so there is nothing a wrapper could add. When the
    ///     release itself fails there is something held and no other handle to it, and the only way
    ///     to hand it back is an <see cref="AgentSessionCreationException"/> carrying it in
    ///     <see cref="AgentSessionCreationException.RetainedProviderSession"/>, with the adapter's
    ///     own failure as the inner exception.
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
    ///     <paramref name="providerSessionFactory"/> returned <see langword="null"/>, or the created
    ///     provider session reports a context window this session could not converge in — the
    ///     second reported as an <see cref="AgentSessionCreationException"/>, which carries the
    ///     provider session when its release failed.
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

        // Construction is the window in which the provider session is owned by nothing, and it is a
        // window that throws by design rather than by accident: the constructor reads the provider's
        // usage, and an adapter reporting a conversation larger than its own total is refused where
        // it wrote it. Guarded here rather than narrowed, because narrowing it would not close it.
        CompactingAgentSession session;
        try
        {
            session = new CompactingAgentSession(options, providerSessionFactory, provider);
        }
        catch (Exception failure)
        {
            // Released rather than abandoned. Nothing else holds this provider session: the instance
            // that would have owned it does not exist, and the caller was never given one.
            if (await TryReleaseUnownedAsync(provider).ConfigureAwait(false))
            {
                // Nothing is held, so there is nothing to hand back and no reason to disturb the
                // failure the adapter reported. Rethrown rather than wrapped, so it still surfaces
                // where the adapter wrote it.
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

        // A provider that reports its window does so from the moment it exists, so a window the
        // session could not converge in is knowable before the first turn is ever spent against it.
        // Refused here for the same reason AgentSessionOptions refuses the equivalent configured
        // window at construction: a rotated context would never land below the rotation threshold.
        //
        // The caller receives no session on this path, so it is told so: the failure carries the
        // provider session itself whenever the release attempt failed, which is the one state in
        // which something is still held and nothing is left to retry it with.
        await session.EnsureReportedWindowConvergesAsync(callerHoldsSession: false).ConfigureAwait(false);
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
    ///     The live provider session returned <see langword="null"/> for a turn, the provider
    ///     session factory returned <see langword="null"/> during a rotation, or the live provider —
    ///     or a replacement a rotation adopted — reports a context window this session could not
    ///     converge in.
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
        //
        // A null result is refused rather than dereferenced. The interface is annotated as
        // returning a turn, but an adapter compiled without nullable analysis, or one whose own
        // transport returned nothing, can still produce one - and by then the provider may already
        // have accepted the message, so the failure has to name what happened rather than surface
        // as a NullReferenceException from the middle of this method while the transcript still
        // says the turn never occurred. The factory, the summarizer and the in-memory responder all
        // convert a null result into this same refusal.
        var turn = await _live.Session.SendAsync(message, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The provider session returned null; it must return a turn.");

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

        Usage = ReadUsage(_live.Session, _options, Layout);

        // A provider that could not say anything when this provider session was created gets its
        // fold measured here instead, at the first turn it does speak on. IContextUsageReporter
        // permits exactly that silence, so a session whose provider takes it must not be credited
        // an overhead allowance of zero for the rest of that provider session's life - which is
        // what the convergence check below would then compare a reported window against. Does
        // nothing for a provider session already measured, including one measured at exactly zero.
        _live.CreditFirstReport(Usage, Layout.ConversationTokens);

        // A reported window this session could not converge in makes rotation incapable of settling,
        // so it is refused here rather than allowed to thrash. A provider may only begin reporting -
        // or report a smaller window - after a turn, so the check belongs on every turn and not
        // merely at creation.
        await EnsureReportedWindowConvergesAsync(callerHoldsSession: true).ConfigureAwait(false);

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

        // The release flag belongs to the provider session, not to this object, so a rotation that
        // replaced the provider replaced the flag with it. A session released and then rotated
        // cannot therefore be read as leaving its replacement released.
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
    ///     <para>
    ///     <b>A replacement that cannot be adopted is released rather than orphaned.</b> Reading a
    ///     replacement's usage is adapter code and may throw by design, and it happens while the
    ///     replacement is owned by nothing: the factory has handed it over and this session has not
    ///     yet taken it. The failure leaves this session coherent against the provider it already
    ///     had, which is the correct outcome, but the replacement would be lost with no handle
    ///     anywhere — for a provider holding history server-side, a remote session never discarded.
    ///     It is therefore released before the failure travels on.
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

        // Built once and measured, because the fold credited to the replacement is what it reports
        // over and above this library's count of the very content it is being handed. See
        // LiveProviderSession.Adopt.
        var seedHistory = outcome.Layout.BuildSeed();
        var seededConversationTokens = 0L;
        foreach (var entry in seedHistory)
        {
            seededConversationTokens += entry.EstimatedTokens;
        }

        var seed = new ProviderSessionSeed(
            _options.Instructions,
            _options.Tools,
            seedHistory);

        var replacement = await _factory.CreateAsync(seed, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The provider session factory returned null.");

        // Read once, and read before the adoption, so the usage the session publishes and the fold
        // the replacement is credited are the same reading of the same session.
        //
        // Guarded, because between the factory returning the replacement and the adoption below it
        // is owned by nothing, and reading its usage runs adapter code that is entitled to throw: an
        // adapter reporting a conversation larger than its own total is refused where it wrote it
        // rather than clamped. Without the guard the replacement is orphaned - this session stays
        // coherent against the provider it already had, which is correct, but the replacement is
        // never released and no handle to it escapes.
        ContextUsage replacementUsage;
        LiveProviderSession adopted;
        try
        {
            replacementUsage = ReadUsage(replacement, _options, outcome.Layout);
            adopted = LiveProviderSession.Adopt(replacement, replacementUsage, seededConversationTokens);
        }
        catch (Exception)
        {
            // Released, and the outcome deliberately discarded: the caller still holds a working
            // session against the provider this rotation did not replace, and the failure it needs
            // is the one that stopped the rotation, not an adapter's disposal failure on top of it.
            // Nothing is handed back because there is nowhere to hand it - unlike creation, this
            // caller holds a session, and that session is not the one that failed.
            await TryReleaseUnownedAsync(replacement).ConfigureAwait(false);
            throw;
        }

        // The whole state transition happens here, as one block containing no await: the provider
        // reference, the layout and the counters describe the same session at every point an
        // exception could be observed. Doing it before disposal - rather than around it, as it was
        // - is what makes that true, because the await on disposal was the one place this object
        // could be left pointing at the replacement while its layout and counters still described
        // the session it replaced, and the next turn would then append to, and possibly rotate, the
        // wrong transcript.
        //
        // The fold and the release flag travel inside the adopted value, so nothing measured
        // against the session being replaced survives the swap.
        var previous = _live;
        _live = adopted;
        Layout = outcome.Layout;
        RotationCount++;
        ConsolidationCount += outcome.ConsolidationCount;
        Usage = replacementUsage;

        // The previous session is finished with only once its replacement exists and has been
        // adopted. For a provider holding history server-side this is what actually discards it.
        //
        // A failure to release it is deliberately not allowed to surface as a rotation failure. The
        // rotation has already succeeded: the context was consolidated, the replacement was created
        // and this session is coherent against it. Throwing it on would report the opposite to the
        // caller and leave it holding a session it would reasonably believe to be broken. The cost
        // of an adapter that cannot dispose is a provider-side session that outlives its use - the
        // adapter's own defect, which discarding a good session on top of it does not repair.
        await previous.TryReleaseAsync().ConfigureAwait(false);

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
        await EnsureReportedWindowConvergesAsync(callerHoldsSession: true).ConfigureAwait(false);

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
    ///     against that same 350, rotates on every following turn. So the live provider's own
    ///     <see cref="LiveProviderSession.UnreportedOverheadTokens"/> is added to the bound the
    ///     threshold must exceed,
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
    ///     fold is exactly separable; an adapter that begins reporting later has it measured at the
    ///     first turn it speaks on instead, against this library's estimate of the conversation by
    ///     then. That is approximate in the safe direction — see
    ///     <see cref="LiveProviderSession.CreditFirstReport"/> — rather than credited as zero, which
    ///     is what it used to be.
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
    ///     <b>The fold belongs to the provider session it was measured from.</b> It is carried on
    ///     <see cref="LiveProviderSession"/> rather than on this session, so adopting a replacement
    ///     replaces it. Measuring it once and reusing it was wrong in both directions: a totals-only
    ///     session replaced by one reporting a split had a fold credited that the replacement does
    ///     not charge, and a convergent replacement was refused; a split-reporting session replaced
    ///     by a totals-only one had no fold credited at all, and a replacement whose hidden overhead
    ///     keeps it above its own threshold was accepted, to rotate on every turn thereafter. A
    ///     provider session that reports nothing at the moment it is adopted has the measurement
    ///     deferred to the first turn it reports on rather than settled at zero; see
    ///     <see cref="LiveProviderSession.CreditFirstReport"/>.
    ///     </para>
    ///     <para>
    ///     The live provider is released before the exception is thrown, because the session is
    ///     being abandoned mid-life. A failure to release is swallowed rather than allowed to
    ///     replace the configuration error, which is the one the caller can act on; the release flag
    ///     is set only on success, and it lives on the provider, so an explicit
    ///     <see cref="DisposeAsync"/> still retries it. The message states the release's actual
    ///     outcome rather than claiming one, and says what to do about a failed release — which
    ///     differs by path. A caller that holds this session disposes it again. A caller on the
    ///     <see cref="CreateAsync"/> path receives no session at all, so the failure is an
    ///     <see cref="AgentSessionCreationException"/> carrying the unreleased provider session
    ///     itself: the retryable state is reachable rather than merely recorded.
    ///     </para>
    /// </remarks>
    /// <param name="callerHoldsSession">
    ///     Whether the caller holds this session and can therefore retry the release by disposing
    ///     it. False only on the <see cref="CreateAsync"/> path, where the instance is discarded.
    /// </param>
    /// <returns>A task that completes when the window has been accepted.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The provider reports a context window this session could not converge in. An
    ///     <see cref="AgentSessionCreationException"/> when the caller holds no session.
    /// </exception>
    private async Task EnsureReportedWindowConvergesAsync(bool callerHoldsSession)
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
        // The bound carries the live provider's own unreported overhead allowance as well as the
        // tier budgets, because the figure a rotated context will be compared against carries it
        // too. That term is the provider's own tokens, measured when that provider session was
        // created; see the remarks above for why it is added to the bound rather than removed from
        // the window, and LiveProviderSession.Adopt for how each provider session gets its own.
        var allowance = _live.UnreportedOverheadTokens;
        var minimumEffective = AgentSessionOptions.MinimumEffectiveWindowTokens(
            _options.Compaction, allowance);
        var effective = Usage.WindowTokens - Usage.OverheadTokens;
        if (Usage.Origin != ContextUsageOrigin.Provider
            || AgentSessionOptions.ConvergesAt(effective, _options.Compaction, allowance))
        {
            return;
        }

        _disposed = true;

        // Attempted unconditionally, and the outcome is used rather than assumed. Every entry point
        // holds a provider session that has not been released: CreateAsync holds a freshly created
        // one, SendAsync has already thrown ObjectDisposedException if this session was disposed,
        // and RotateAsync has just adopted a replacement and released only the session it
        // superseded. A failure to release is swallowed rather than allowed to replace the
        // configuration error, which is the one the caller can act on; the release flag stays false
        // inside the live provider, which is what keeps the release retryable.
        var released = await _live.TryReleaseAsync().ConfigureAwait(false);

        // What the caller is expected to do about the provider session, stated rather than left to
        // be inferred - and it differs by path, which is exactly the combination that was missed. A
        // caller holding this session retries the release by disposing it again. A caller that
        // never received one has no such handle, so the failure carries the provider session itself
        // and says to dispose that.
        var retry = callerHoldsSession
            ? "disposing this session again retries the release."
            : "dispose the provider session this failure carries "
                + $"({nameof(AgentSessionCreationException.RetainedProviderSession)}) to retry the "
                + "release.";
        var outcome = released
            ? "The live provider session was released."
            : "Release of the live provider session was attempted and failed, so the provider still "
                + "holds it; " + retry;

        var message =
            $"The provider reports a context window of {Usage.WindowTokens} tokens, leaving "
            + $"{effective} tokens once the {Usage.OverheadTokens} tokens of overhead it reports "
            + "outside the conversation are paid for. A rotated context occupies up to "
            + $"{_options.Compaction.TotalTierBudgetTokens} tokens of tier budgets and "
            + $"{ContextLayout.SeedFramingTokens(_options.Compaction)} tokens of framing for their "
            + $"seeded records, and is counted as carrying a further {allowance} "
            + "tokens of overhead this provider charges without reporting separately. All of that "
            + "must land below the rotation threshold, which requires at least "
            + $"{minimumEffective} tokens. The session is abandoned rather than left to rotate on "
            + $"every turn without ever getting under its own threshold. {outcome}";

        if (callerHoldsSession)
        {
            throw new InvalidOperationException(message);
        }

        throw new AgentSessionCreationException(message)
        {
            RetainedProviderSession = released ? null : _live.Session,
        };
    }

    /// <summary>
    ///     Releases a provider session nothing has taken ownership of, reporting whether the release
    ///     succeeded and never throwing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>For the two windows in which a provider session belongs to no one.</b> A provider
    ///     session is created by a factory and owned by a <see cref="LiveProviderSession"/>, and
    ///     between those two moments its usage is read — adapter code, which is entitled to throw,
    ///     and which this library deliberately lets throw rather than clamping what it returns. A
    ///     failure there would otherwise discard the only reference to a session the provider still
    ///     holds.
    ///     </para>
    ///     <para>
    ///     Distinct from <see cref="LiveProviderSession.TryReleaseAsync"/> because there is no live
    ///     provider session to ask: nothing was adopted, so there is no release flag to maintain and
    ///     nothing that could already have been released. The disposal failure is swallowed for the
    ///     same reason it is there — the caller is reporting a different failure, and this one must
    ///     not replace it — and reported as a return value so the caller can say which of the two
    ///     states it is in.
    ///     </para>
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

    /// <summary>
    ///     One live provider session together with everything that belongs to that session rather
    ///     than to the <see cref="CompactingAgentSession"/> holding it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>This type exists to make a class of defect unreachable rather than guarded.</b> The
    ///     fold a provider charges without reporting, and whether that provider has been released,
    ///     are facts about one provider session. Held as fields beside the provider reference they
    ///     could be — and repeatedly were — left describing a session that had already been replaced.
    ///     Held here they are replaced with the provider, in the same assignment, so a combination
    ///     in which they disagree cannot be written.
    ///     </para>
    ///     <para>
    ///     Immutable apart from <see cref="Released"/>, which is the one thing about a live provider
    ///     session that legitimately changes during its life.
    ///     </para>
    /// </remarks>
    private sealed class LiveProviderSession
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="LiveProviderSession"/> class.
        /// </summary>
        /// <remarks>
        ///     Private because the fold is a measurement rather than an argument; see
        ///     <see cref="Adopt"/>.
        /// </remarks>
        /// <param name="session">The provider session this value describes.</param>
        /// <param name="unreportedOverheadTokens">The overhead it charges without reporting.</param>
        /// <param name="foldMeasured">Whether that overhead came from a provider's own figure.</param>
        private LiveProviderSession(
            IProviderSession session,
            int unreportedOverheadTokens,
            bool foldMeasured)
        {
            Session = session;
            UnreportedOverheadTokens = unreportedOverheadTokens;
            FoldMeasured = foldMeasured;
        }

        /// <summary>
        ///     Gets the provider session itself.
        /// </summary>
        public IProviderSession Session { get; }

        /// <summary>
        ///     Gets the fixed overhead this provider session charges for without breaking it out of
        ///     its conversation figure, in the provider's own tokens.
        /// </summary>
        /// <remarks>
        ///     Zero for a provider that reports the split, and zero for an estimated figure, whose
        ///     split this library makes itself. So it changes nothing for either adapter shipped
        ///     today and exists entirely for the third:
        ///     <see cref="ContextUsage.FromProvider(int, int, int?)"/> is public and documented to
        ///     accept totals alone, and the session that receives them has to know what it is not
        ///     being told.
        ///     <para>
        ///     Measured once per provider session, at the first instant that provider session
        ///     produces a figure of its own — creation where it reports from creation, and the
        ///     first turn it speaks on where it does not. See <see cref="Adopt"/> and
        ///     <see cref="CreditFirstReport"/>.
        ///     </para>
        /// </remarks>
        public int UnreportedOverheadTokens { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the fold has been measured from a provider's own
        ///     figure.
        /// </summary>
        /// <remarks>
        ///     False for a provider session that reported nothing when it was adopted, which is the
        ///     one state in which <see cref="UnreportedOverheadTokens"/> is a placeholder rather
        ///     than a measurement. It is a distinct flag rather than a zero test because a genuine
        ///     fold of zero — every split-reporting adapter — must not be re-measured on every turn
        ///     against a conversation that has grown since.
        /// </remarks>
        public bool FoldMeasured { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether this provider session has actually been released.
        /// </summary>
        /// <remarks>
        ///     Set only once the provider's own disposal has completed, which is what keeps a failed
        ///     release retryable rather than turning a transient provider failure into a permanent
        ///     leak.
        /// </remarks>
        public bool Released { get; private set; }

        /// <summary>
        ///     Measures a freshly created provider session's fold and pairs it with the session.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     <b>Measured at the one moment it is separable, and measured per provider session.</b>
        ///     Whatever a provider-reported figure attributes to the conversation at the instant a
        ///     session is created, over and above this library's own count of the content that
        ///     session was handed, is not conversation: it is the system prompt, the tool
        ///     declarations and whatever framing of its own the provider charges for, counted in the
        ///     provider's tokens and folded into a figure it did not split.
        ///     </para>
        ///     <para>
        ///     <b>For the first provider session of a conversation the subtrahend is zero</b>,
        ///     because it is seeded with no history at all, and the whole of what it calls
        ///     conversation is fold. That is the case this measurement began as. A replacement is
        ///     seeded with a rotated context, so its own count of that context has to come off
        ///     before what is left can be called fold — and it is that subtraction, rather than a
        ///     figure carried over from the session being replaced, which makes the measurement
        ///     travel with the provider it describes. A fold measured once and reused was wrong in
        ///     both directions: it refused a convergent split-reporting replacement of a totals-only
        ///     session, and it accepted a totals-only replacement of a split-reporting session whose
        ///     hidden overhead keeps every rotated context above the threshold.
        ///     </para>
        ///     <para>
        ///     <b>The subtraction is approximate, and errs toward crediting too much.</b> The
        ///     minuend is the provider's own count and the subtrahend is this library's estimate of
        ///     the same entries, so a provider counting framing this library never sees has that
        ///     difference credited as fold as well. That is the safe direction: the allowance only
        ///     ever raises the bound the rotation threshold must exceed, so an over-credit refuses a
        ///     window that was marginal rather than accepting one that thrashes. It is zero for both
        ///     adapters shipped today — one reports the split and the other is estimated — and the
        ///     in-memory session, which counts the seeded entries with this very estimator, is
        ///     credited exactly nothing.
        ///     </para>
        ///     <para>
        ///     <b>A provider that says nothing here is not measured here.</b>
        ///     <see cref="IContextUsageReporter"/> permits <see cref="IContextUsageReporter.CurrentUsage"/>
        ///     to be <see langword="null"/> at creation, in which case the usage read at this
        ///     instant is this library's own estimate and carries no fold to find. The measurement
        ///     is deferred rather than settled at zero, and <see cref="CreditFirstReport"/> takes it
        ///     at the first turn the provider does speak on. Settling it at zero here was the
        ///     defect: a totals-only provider that begins reporting after its first turn folds real
        ///     overhead into every figure it publishes while being credited none of it, so a window
        ///     it cannot converge in is accepted and every replacement rotates again.
        ///     </para>
        /// </remarks>
        /// <param name="session">The provider session just created.</param>
        /// <param name="usageAtCreation">What it reported, or this library's estimate, at that instant.</param>
        /// <param name="seededConversationTokens">
        ///     This library's estimate of the history it was seeded with: zero for a session seeded
        ///     with none.
        /// </param>
        /// <returns>The provider session paired with its own fold.</returns>
        public static LiveProviderSession Adopt(
            IProviderSession session,
            ContextUsage usageAtCreation,
            long seededConversationTokens)
        {
            // Only a provider's own figure can carry a fold. An estimate publishes the split this
            // library made itself, so there is nothing it failed to break out - and it is also what
            // a reporter that cannot yet say anything falls back to, which is why the measurement
            // is recorded as not taken rather than taken and found to be zero.
            if (usageAtCreation.Origin != ContextUsageOrigin.Provider)
            {
                return new LiveProviderSession(session, 0, foldMeasured: false);
            }

            return new LiveProviderSession(
                session,
                Fold(usageAtCreation.ConversationTokens, seededConversationTokens),
                foldMeasured: true);
        }

        /// <summary>
        ///     Measures the fold from the first figure a provider session reports, for a provider
        ///     session that reported nothing when it was adopted.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     <b>Refreshing the fold, rather than refusing the transition, is the deliberate
        ///     choice.</b> <see cref="IContextUsageReporter"/> states plainly that an
        ///     implementation which sometimes knows returns <see langword="null"/> until it does,
        ///     so a provider that begins reporting after its first turn is doing exactly what the
        ///     contract invites. Refusing the transition would abandon a session over conformant
        ///     adapter behavior, and would abandon it on the first turn — after a real message had
        ///     been spent — which is the worst moment to discover a configuration this library
        ///     could simply have measured. So the transition is accepted and the measurement is
        ///     taken at the first instant it is available, which is what the fold was always
        ///     defined as.
        ///     </para>
        ///     <para>
        ///     <b>Once, and only for a fold that was never measured.</b> A provider session that
        ///     reported at creation has a fold measured against the content it was actually handed;
        ///     re-measuring it on every turn would subtract this library's estimate of a growing
        ///     conversation from the provider's count of it and credit the accumulating difference
        ///     between two tokenizers as though it were fixed overhead. The flag distinguishes an
        ///     unmeasured fold from a measured zero, which no reading of the figure itself could.
        ///     </para>
        ///     <para>
        ///     <b>The subtrahend is this library's estimate of the conversation at the same
        ///     instant</b>, which at creation is the seeded history and here is that history plus
        ///     the turns taken since. It is the same measurement
        ///     <see cref="Adopt"/> makes, taken later: less exact, because a turn's worth of
        ///     estimation error is inside it rather than an empty conversation, and in the same
        ///     direction — the allowance only ever raises the bound the rotation threshold must
        ///     exceed, so an over-credit refuses a marginal window rather than accepting one that
        ///     thrashes.
        ///     </para>
        /// </remarks>
        /// <param name="usage">The usage figure this turn produced, and where it came from.</param>
        /// <param name="conversationTokens">
        ///     This library's estimate of the conversation this provider session now holds.
        /// </param>
        public void CreditFirstReport(ContextUsage usage, long conversationTokens)
        {
            if (FoldMeasured || usage.Origin != ContextUsageOrigin.Provider)
            {
                return;
            }

            UnreportedOverheadTokens = Fold(usage.ConversationTokens, conversationTokens);
            FoldMeasured = true;
        }

        /// <summary>
        ///     Releases the provider session, propagating whatever failure it reports.
        /// </summary>
        /// <remarks>
        ///     Does nothing once the release has succeeded, so disposing twice is permitted. A
        ///     release that failed leaves <see cref="Released"/> false, so a later call tries again
        ///     rather than returning as though it had happened.
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
        ///     For the two paths that must not let an adapter's disposal failure replace the failure
        ///     they are reporting: a rotation that has already succeeded, and a session being
        ///     abandoned over a window it could not converge in. Returning the outcome rather than
        ///     discarding it is what lets those paths state what actually happened instead of
        ///     claiming an attempt.
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
                // Intentionally swallowed; the caller reports a different failure and says what to
                // do about this one. Released stays false, so the release remains retryable.
            }

            return Released;
        }

        /// <summary>
        ///     Computes the fold: what a provider-reported conversation figure carries over and
        ///     above this library's own count of the same material.
        /// </summary>
        /// <remarks>
        ///     Floored at zero, because a provider counting less than this library estimates has
        ///     folded nothing in — it has simply counted differently, and crediting a negative
        ///     allowance would lower the bound the rotation threshold must exceed and admit a
        ///     window that thrashes. Widened before the subtraction, so a reported figure at the
        ///     top of a token count and an estimate of zero cannot wrap.
        /// </remarks>
        /// <param name="reportedConversationTokens">What the provider calls conversation.</param>
        /// <param name="conversationTokens">This library's estimate of the same conversation.</param>
        /// <returns>The overhead the provider charges without breaking it out.</returns>
        private static int Fold(int reportedConversationTokens, long conversationTokens) =>
            (int)Math.Max(0L, reportedConversationTokens - conversationTokens);
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
///     was released and there is nothing left to clean up. Then treat the failure itself: a message
///     naming a provider window the configured compaction policy cannot converge in is a
///     configuration defect, and fixing the sizing is the repair; an inner exception means something
///     else stopped the creation and is the failure to act on.
///     </para>
///     <para>
///     <b>Not every failed creation is reported as this.</b> A creation stopped by someone else's
///     exception — an adapter's, typically — rethrows that exception unchanged once the provider
///     session it created has been released, because nothing is then held and a wrapper would only
///     move the failure away from where it was written. This type appears when the release itself
///     failed and the handle has nowhere else to go.
///     </para>
///     <para>
///     Derives from <see cref="InvalidOperationException"/> because that is what this condition has
///     always been reported as, and what every other road to it still reports.
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
    ///     <b>Settable at construction rather than taken as a constructor parameter, because a
    ///     constructor parameter made the ordinary construction ambiguous.</b> This type needs the
    ///     standard exception constructors, and a <c>(string, IAsyncDisposable?)</c> overload beside
    ///     <c>(string, Exception)</c> converts to neither, so <c>new
    ///     AgentSessionCreationException(message, null)</c> — the natural way to say that nothing is
    ///     retained — did not compile. The retained session is also orthogonal to the message and
    ///     the inner exception rather than an alternative to them: the creation path that carries a
    ///     session carries an adapter's failure with it. An initializer composes with all three
    ///     constructors, where an overload per combination or a static factory per combination
    ///     multiplies them.
    ///     </para>
    /// </remarks>
    public IAsyncDisposable? RetainedProviderSession { get; init; }
}
