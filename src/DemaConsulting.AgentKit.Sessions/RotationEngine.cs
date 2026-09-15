namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     Why a consolidation was reported as saturated.
/// </summary>
public enum SaturationReason
{
    /// <summary>
    ///     The consolidation returned output nearly as large as its input, so there is no
    ///     redundancy left in the material to remove.
    /// </summary>
    NoRedundancy,

    /// <summary>
    ///     The consolidated record still exceeds its tier's budget, and there was no coarser tier
    ///     left to age the older record into.
    /// </summary>
    TierOverBudget,
}

/// <summary>
///     A report that a rotation could not reduce what it was asked to reduce.
/// </summary>
/// <remarks>
///     <para>
///     <b>Surfaced rather than acted upon, deliberately.</b> An agent whose context has saturated
///     will keep triggering rotations that spend summarizer tokens and buy nothing. Detecting that
///     is this library's job; deciding what to do about it — warn, stop, split the task, start
///     fresh — depends on what the application is for, so the signal is handed back rather than
///     turned into a policy here. Without detection the failure is invisible: every rotation
///     appears to succeed.
///     </para>
///     <para>
///     Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
public sealed class SaturationSignal
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SaturationSignal"/> class.
    /// </summary>
    /// <param name="tierIndex">The tier whose consolidation saturated. Must be one or greater.</param>
    /// <param name="inputTokens">The estimated tokens handed to the consolidation. Must not be negative.</param>
    /// <param name="outputTokens">The estimated tokens it returned. Must not be negative.</param>
    /// <param name="reason">
    ///     Why the result was treated as saturated. Must be a defined
    ///     <see cref="SaturationReason"/> member.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="tierIndex"/> is less than one, a token count is negative, or
    ///     <paramref name="reason"/> is not a defined <see cref="SaturationReason"/> member.
    /// </exception>
    public SaturationSignal(int tierIndex, int inputTokens, int outputTokens, SaturationReason reason)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tierIndex, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);

        // A saturation signal is handed to the application to decide on - warn, stop, split the
        // task, start fresh - and an undefined reason gives it nothing to decide from while
        // matching no branch it could write. Refused for the same reason the other enum-carrying
        // constructors in this package refuse one, so the rule is the same everywhere.
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "The saturation reason must be a defined SaturationReason member.");
        }

        TierIndex = tierIndex;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        Reason = reason;
    }

    /// <summary>
    ///     Gets the tier whose consolidation saturated.
    /// </summary>
    public int TierIndex { get; }

    /// <summary>
    ///     Gets the estimated tokens handed to the consolidation.
    /// </summary>
    public int InputTokens { get; }

    /// <summary>
    ///     Gets the estimated tokens the consolidation returned.
    /// </summary>
    public int OutputTokens { get; }

    /// <summary>
    ///     Gets why the result was treated as saturated.
    /// </summary>
    public SaturationReason Reason { get; }
}

/// <summary>
///     What one rotation produced: the new layout, any saturation reported, and how much
///     summarizer work it cost.
/// </summary>
/// <remarks>
///     Instances are immutable after construction and safe for concurrent use.
/// </remarks>
public sealed class RotationOutcome
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RotationOutcome"/> class.
    /// </summary>
    /// <param name="layout">The layout after rotation. Must not be <see langword="null"/>.</param>
    /// <param name="saturations">
    ///     The saturation reports, if any. Must not be <see langword="null"/> and must contain no
    ///     <see langword="null"/> entry; an empty list means the rotation reduced what it was asked
    ///     to reduce.
    /// </param>
    /// <param name="consolidationCount">
    ///     How many consolidations the rotation performed. Must not be negative.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="layout"/> or <paramref name="saturations"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="saturations"/> contains a <see langword="null"/> entry.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="consolidationCount"/> is negative.</exception>
    public RotationOutcome(
        ContextLayout layout,
        IReadOnlyList<SaturationSignal> saturations,
        int consolidationCount)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(saturations);
        ArgumentOutOfRangeException.ThrowIfNegative(consolidationCount);

        // Reject a null signal before anything is copied, exactly as AgentSessionResponse does: an
        // outcome holding one would report IsSaturated true while the consumer that went to look at
        // the signal could not read it, which is worse than reporting nothing at all.
        if (saturations.Any(signal => signal is null))
        {
            throw new ArgumentException("A saturation signal in the list is null.", nameof(saturations));
        }

        Layout = layout;

        // Copy the signals into storage this outcome owns, exposed only as a read-only view: the
        // rotation hands over its own mutable working list, and an outcome documented as immutable
        // must not remain a window onto it.
        Saturations = Array.AsReadOnly<SaturationSignal>([.. saturations]);
        ConsolidationCount = consolidationCount;
    }

    /// <summary>
    ///     Gets the layout after rotation.
    /// </summary>
    /// <remarks>
    ///     This is what a fresh provider session is seeded from, through
    ///     <see cref="ContextLayout.BuildSeed"/>.
    /// </remarks>
    public ContextLayout Layout { get; }

    /// <summary>
    ///     Gets the saturation reports, empty when the rotation reduced normally.
    /// </summary>
    public IReadOnlyList<SaturationSignal> Saturations { get; }

    /// <summary>
    ///     Gets how many consolidations the rotation performed.
    /// </summary>
    /// <remarks>
    ///     Exposed because summarizer calls are the dominant cost of this arrangement, and because
    ///     a test asserting that only the overflowing tiers were consolidated needs to count them.
    /// </remarks>
    public int ConsolidationCount { get; }

    /// <summary>
    ///     Gets a value indicating whether any consolidation in this rotation saturated.
    /// </summary>
    public bool IsSaturated => Saturations.Count > 0;
}

/// <summary>
///     Ages a session's context by one rotation: a deterministic function from the current layout
///     and an injected summarizer to the next layout.
/// </summary>
/// <remarks>
///     <para>
///     <b>Rotation, not in-place reduction.</b> When the context fills, older history is
///     consolidated and a fresh provider session is created seeded with the preserved content,
///     after which the session it replaces is disposed. This is the only reduction mechanism both
///     provider shapes support: one re-sends the whole history on every turn and would accept an
///     edit, the other keeps history server-side and would not. Rotating is what makes the two
///     behave identically. This class performs the consolidation half alone: it is a pure function
///     over a layout and owns no provider session, so creating the replacement and disposing the
///     one it supersedes belong to <see cref="CompactingAgentSession"/>.
///     </para>
///     <para>
///     <b>Aging happens only here, and in one batch.</b> Between rotations the context is strictly
///     append-only — nothing already sent is rewritten — which is what preserves a provider's
///     prompt cache. At rotation every overflowing tier consolidates at once, cascading into
///     coarser tiers where it must. Batching costs nothing extra, because a rotation invalidates
///     the cache anyway.
///     </para>
///     <para>
///     <b>Deterministic and pure apart from the summarizer.</b> Every decision this class makes —
///     where the tier boundary falls, whether it snaps, which tiers overflow, whether a result
///     saturated — is arithmetic over the layout it was handed. Supply a deterministic fake
///     summarizer and the whole engine is a pure function, which is how it is tested without a
///     model.
///     </para>
///     <para>
///     This class is static, holds no state, and is safe for concurrent use.
///     </para>
/// </remarks>
public static class RotationEngine
{
    /// <summary>
    ///     Performs one rotation, returning the aged layout and any saturation it detected.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The algorithm, in order:
    ///     </para>
    ///     <para>
    ///     1. Split the verbatim history at tier zero's budget, newest first, snapping the boundary
    ///     so a tool call is never separated from its result. The retained suffix stays verbatim.
    ///     </para>
    ///     <para>
    ///     2. If nothing overflowed, the rotation is a re-seed: the layout is returned unchanged and
    ///     no summarizer call is made. That is the correct outcome for a session whose recent
    ///     history already fits, and it costs nothing.
    ///     </para>
    ///     <para>
    ///     3. Otherwise fold the overflow into tier one, cascading: a consolidation whose result
    ///     still exceeds its tier's budget first ages that tier's <em>previous</em> record down into
    ///     the next coarser tier — the deliberate degradation the design allows — and then records
    ///     the new material at this tier alone.
    ///     </para>
    ///     <para>
    ///     Recursion is bounded by the tier count, so the worst case is one cascade per tier.
    ///     </para>
    /// </remarks>
    /// <param name="layout">The layout to rotate. Must not be <see langword="null"/>.</param>
    /// <param name="summarizer">
    ///     The out-of-session summarizer performing each consolidation. Must not be
    ///     <see langword="null"/>, and must not return <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">
    ///     Cancels the rotation. Checked once after argument validation, so an already-canceled
    ///     rotation is refused even when the transcript fits and there is no work to do, and again
    ///     between consolidations.
    /// </param>
    /// <returns>The aged layout, the saturation reports, and the consolidation count.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="layout"/> or <paramref name="summarizer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="summarizer"/> returned <see langword="null"/> from a consolidation.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<RotationOutcome> RotateAsync(
        ContextLayout layout,
        ISummarizer summarizer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(summarizer);

        // Honor cancellation before any work is decided on, not merely between consolidations. The
        // no-work path below returns without ever reaching a consolidation, so a check placed only
        // there would hand a caller a successful rotation result for a rotation it had already
        // canceled, whenever the transcript happened to fit. Argument validation comes first,
        // because a malformed call is a defect in the caller and is worth reporting as such even on
        // a canceled token.
        cancellationToken.ThrowIfCancellationRequested();

        var policy = layout.Policy;

        // Step 1: decide what stays verbatim. The split snaps the boundary so a tool result never
        // survives without the call that produced it.
        var (retained, overflow) = layout.Transcript.SplitAtBudget(policy.TierBudgetTokens[0]);

        // Step 2: nothing aged out, so this rotation is purely a re-seed of the same content into a
        // fresh provider session. No summarizer call is made and no tier changes.
        if (overflow.Count == 0)
        {
            return new RotationOutcome(layout, [], 0);
        }

        // Step 3: fold the overflow into tier one, cascading into coarser tiers where required.
        var state = new RotationState(policy, summarizer, [.. layout.CoarseTiers]);
        await state.AgeAsync(1, SessionTranscript.Render(overflow), cancellationToken).ConfigureAwait(false);

        return new RotationOutcome(
            layout.WithTiers(retained, state.Tiers),
            state.Saturations,
            state.ConsolidationCount);
    }

    /// <summary>
    ///     The mutable working set of one rotation: the tiers being aged, the summarizer performing
    ///     the consolidations, and what the rotation has observed so far.
    /// </summary>
    /// <remarks>
    ///     Exists so the cascading recursion can carry its accumulating state without a long
    ///     parameter list or a closure per call. It is created inside
    ///     <see cref="RotateAsync"/> and never escapes it, so a rotation remains a pure function
    ///     from the caller's point of view even though this type is mutable.
    /// </remarks>
    /// <param name="policy">The policy whose budgets and saturation ratio govern the rotation.</param>
    /// <param name="summarizer">The out-of-session summarizer performing each consolidation.</param>
    /// <param name="tiers">The coarse tiers being aged, tier one first. Mutated in place.</param>
    private sealed class RotationState(CompactionPolicy policy, ISummarizer summarizer, ContextTier[] tiers)
    {
        /// <summary>
        ///     Gets the coarse tiers being aged, tier one first.
        /// </summary>
        public ContextTier[] Tiers { get; } = tiers;

        /// <summary>
        ///     Gets the saturation reports accumulated so far.
        /// </summary>
        public List<SaturationSignal> Saturations { get; } = [];

        /// <summary>
        ///     Gets the number of consolidations performed so far.
        /// </summary>
        public int ConsolidationCount { get; private set; }

        /// <summary>
        ///     Folds material into one tier, aging that tier's existing record into the next coarser
        ///     tier when the two cannot fit together.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     The overflow test is made <em>after</em> consolidating rather than before, because a
        ///     consolidation compresses: summing the previous record and the new material first
        ///     would cascade on material that would in fact have fitted once combined, degrading
        ///     detail that did not need to degrade.
        ///     </para>
        ///     <para>
        ///     When a cascade is required, the record that ages down is the <em>previous</em> one —
        ///     the older material — and the tier is then re-recorded holding the new material alone.
        ///     That ordering is what keeps the hierarchy monotonic in age: coarser always means
        ///     older.
        ///     </para>
        ///     <para>
        ///     A tier with no previous record cannot cascade, because there is nothing older to move
        ///     down; if its single consolidation still overflows, that is reported as
        ///     <see cref="SaturationReason.TierOverBudget"/> rather than papered over.
        ///     </para>
        ///     <para>
        ///     Both recordings a cascade performs — the merge that overflowed and the re-recording
        ///     of the new material alone — are tested for redundancy. A re-recording that returns
        ///     nearly as much as it was given has saturated whether or not it happened to fit the
        ///     tier, and checking only the merge let that rotation report an unqualified success.
        ///     </para>
        /// </remarks>
        /// <param name="tierIndex">The tier to fold into. One or greater, at most the coarsest tier.</param>
        /// <param name="material">The material to fold in, rendered as labeled text.</param>
        /// <param name="cancellationToken">Cancels the consolidation.</param>
        /// <returns>A task completing when the tier, and any coarser tier it cascaded into, is settled.</returns>
        /// <exception cref="InvalidOperationException">The summarizer returned <see langword="null"/>.</exception>
        public async Task AgeAsync(int tierIndex, string material, CancellationToken cancellationToken)
        {
            var slot = tierIndex - 1;
            var tier = Tiers[slot];
            var previous = tier.Content;
            var budget = policy.TierBudgetTokens[tierIndex];

            // Consolidate the previous record together with the new material. The previous record
            // is an input rather than context: this is the ratchet that keeps detail an earlier
            // consolidation decided to keep.
            var merged = await ConsolidateAsync(tierIndex, previous, material, budget, cancellationToken)
                .ConfigureAwait(false);
            var mergedTokens = TokenEstimator.EstimateTokens(merged);
            var inputTokens = TokenEstimator.EstimateTokens(previous) + TokenEstimator.EstimateTokens(material);

            // A result nearly as large as its input means the material holds no redundancy left to
            // remove; rotating again would spend summarizer tokens for no reduction.
            if (inputTokens > 0 && mergedTokens >= policy.SaturationRatio * inputTokens)
            {
                Saturations.Add(new SaturationSignal(
                    tierIndex, inputTokens, mergedTokens, SaturationReason.NoRedundancy));
            }

            // The common case: the combined record fits, and nothing needs to degrade.
            if (mergedTokens <= budget)
            {
                Tiers[slot] = tier.WithContent(merged);
                return;
            }

            // The combined record does not fit. Cascade only when there is an older record to move
            // down and somewhere coarser to move it to.
            var canCascade = previous.Length > 0 && tierIndex + 1 < policy.TierCount;
            if (!canCascade)
            {
                Tiers[slot] = tier.WithContent(merged);
                Saturations.Add(new SaturationSignal(
                    tierIndex, inputTokens, mergedTokens, SaturationReason.TierOverBudget));
                return;
            }

            // Age the older record one tier coarser - the deliberate degradation the design allows -
            // then re-record this tier holding only the new material.
            await AgeAsync(tierIndex + 1, previous, cancellationToken).ConfigureAwait(false);

            var alone = await ConsolidateAsync(tierIndex, string.Empty, material, budget, cancellationToken)
                .ConfigureAwait(false);
            Tiers[slot] = tier.WithContent(alone);

            var aloneTokens = TokenEstimator.EstimateTokens(alone);
            var aloneInputTokens = TokenEstimator.EstimateTokens(material);

            // The same redundancy test the merge above is given. A re-recording that returns nearly
            // as much as the material it was handed says the material holds nothing left to remove,
            // and that is just as true on the cascade path as on the common one. Omitting it here
            // let a rotation that had in fact saturated report a plain success, because a result at
            // or above the saturation ratio that still fits the tier passes the over-budget check
            // below and would then have been reported as nothing at all.
            if (aloneInputTokens > 0 && aloneTokens >= policy.SaturationRatio * aloneInputTokens)
            {
                Saturations.Add(new SaturationSignal(
                    tierIndex, aloneInputTokens, aloneTokens, SaturationReason.NoRedundancy));
            }

            if (aloneTokens > budget)
            {
                Saturations.Add(new SaturationSignal(
                    tierIndex,
                    aloneInputTokens,
                    aloneTokens,
                    SaturationReason.TierOverBudget));
            }
        }

        /// <summary>
        ///     Performs one consolidation and counts it.
        /// </summary>
        /// <remarks>
        ///     Centralizes the null check on the summarizer's result so every call site is protected
        ///     by it, and centralizes the count so the reported
        ///     <see cref="RotationOutcome.ConsolidationCount"/> cannot drift from what actually
        ///     happened.
        /// </remarks>
        /// <param name="tierIndex">The tier the result belongs to.</param>
        /// <param name="previousRecord">The record to carry forward, empty when there is none.</param>
        /// <param name="material">The material to fold in.</param>
        /// <param name="budgetTokens">The tier's budget, passed for the summarizer's information.</param>
        /// <param name="cancellationToken">Cancels the consolidation.</param>
        /// <returns>The consolidated record, never <see langword="null"/>.</returns>
        /// <exception cref="InvalidOperationException">The summarizer returned <see langword="null"/>.</exception>
        private async Task<string> ConsolidateAsync(
            int tierIndex,
            string previousRecord,
            string material,
            int budgetTokens,
            CancellationToken cancellationToken)
        {
            var request = new ConsolidationRequest(tierIndex, previousRecord, material, budgetTokens);
            ConsolidationCount++;

            var result = await summarizer.ConsolidateAsync(request, cancellationToken).ConfigureAwait(false);
            return result ?? throw new InvalidOperationException(
                $"The summarizer returned null for a tier {tierIndex} consolidation; "
                + "an implementation with nothing to say must return an empty string.");
        }
    }
}
