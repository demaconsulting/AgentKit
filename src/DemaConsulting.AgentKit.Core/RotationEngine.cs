namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     What one rotation produced: the new layout, how much summarizer work it cost, the compaction
///     level it settled at, and whether it had to drop material outright to make the context fit.
/// </summary>
/// <remarks>
///     Instances are immutable after construction and safe for concurrent use.
/// </remarks>
internal sealed class RotationOutcome
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RotationOutcome"/> class.
    /// </summary>
    /// <param name="layout">The layout after rotation. Must not be <see langword="null"/>.</param>
    /// <param name="consolidationCount">How many consolidations the rotation performed. Must not be negative.</param>
    /// <param name="materialDropped">
    ///     Whether the rotation dropped a slot or a verbatim turn outright because a fully
    ///     consolidated context at the highest level still did not fit.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="consolidationCount"/> is negative.
    /// </exception>
    public RotationOutcome(
        ContextLayout layout,
        int consolidationCount,
        bool materialDropped)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentOutOfRangeException.ThrowIfNegative(consolidationCount);

        Layout = layout;
        ConsolidationCount = consolidationCount;
        MaterialDropped = materialDropped;
    }

    /// <summary>
    ///     Gets the layout after rotation, which a fresh provider session is seeded from.
    /// </summary>
    public ContextLayout Layout { get; }

    /// <summary>
    ///     Gets how many consolidations the rotation performed.
    /// </summary>
    /// <remarks>
    ///     Summarizer calls are the dominant cost of this arrangement, so the count is reported
    ///     rather than inferred. A rotation makes one call for the material it ages into tier one,
    ///     plus one for each tier a cascade fills.
    /// </remarks>
    public int ConsolidationCount { get; }

    /// <summary>
    ///     Gets a value indicating whether the rotation discarded material outright.
    /// </summary>
    /// <remarks>
    ///     True when a full tier could not be consolidated and its oldest slot was displaced to make
    ///     room for the arriving one. It is the honest signal that compaction bought nothing, and is
    ///     deliberately distinct from a consolidation that simply could not be made: that leaves the
    ///     material where it is, which is not a loss.
    /// </remarks>
    public bool MaterialDropped { get; }
}

/// <summary>
///     Ages a session's context by one rotation: a deterministic function from the current layout
///     and an injected summarizer to the next layout.
/// </summary>
/// <remarks>
///     <para>
///     <b>Rotation, not in-place reduction.</b> When the context fills, older history is
///     consolidated and a fresh provider session is created seeded with the preserved content,
///     after which the session it replaces is disposed. This class performs the consolidation half
///     alone: it is a pure function over a layout and owns no provider session, so creating the
///     replacement and disposing the one it supersedes belong to
///     <see cref="CompactingAgentSession"/>.
///     </para>
///     <para>
///     <b>The rules, and which of them live here.</b> Rule 1 appends whole turns to the verbatim
///     tail, which the session does. Rule 2 consolidates everything older than the level-adjusted
///     tail into one slot and appends it to tier one. Rule 3 consolidates a full coarse tier's slots
///     as peers into one slot of the next tier and clears it. Rule 4 makes the coarsest tier a ring,
///     dropping its oldest slot when it is full. Rules 2 to 4 are here. Rule 5 - the response to a
///     context that keeps filling - belongs to the session, because it is decided from how quickly
///     the window refilled, which only the session knows.
///     </para>
///     <para>
///     <b>Nothing here measures whether the result will fit.</b> A rotation cannot: the context it
///     builds has not been sent, so the only figure available would be this library's own estimate,
///     and comparing that against a provider's real capacity is the mistake this design exists to
///     remove. A rotation moves material and reports what it did.
///     </para>
///     <para>
///     <b>A rotation always moves something.</b> The tail keeps at most what the level asks for and
///     at most one turn fewer than it holds, so a provider counting well above this library's
///     estimate - which reaches its threshold while the tail is still short - cannot produce a
///     rotation that consolidates nothing and leaves the session on a provider already full.
///     </para>
///     <para>
///     <b>A blank summarizer answer is normalized to empty where it is received.</b> An answer of
///     pure whitespace produces no slot rather than a slot that silently erases a span of history
///     while still costing framing.
///     </para>
///     <para>
///     This class is static, holds no state, and is safe for concurrent use.
///     </para>
/// </remarks>
internal static class RotationEngine
{
    /// <summary>
    ///     Returns the verbatim tail length for a compaction level.
    /// </summary>
    /// <remarks>
    ///     Low keeps the full allowance, medium half, and high a quarter, never below one turn.
    /// </remarks>
    /// <param name="level">The compaction level to rotate at.</param>
    /// <param name="verbatimTurns">The configured maximum verbatim tail length.</param>
    /// <returns>The number of newest turns to keep verbatim at this level, at least one.</returns>
    public static int VerbatimTurnsFor(CompactionLevel level, int verbatimTurns) => level switch
    {
        CompactionLevel.Medium => Math.Max(1, verbatimTurns / 2),
        CompactionLevel.High => Math.Max(1, verbatimTurns / 4),
        _ => verbatimTurns,
    };

    /// <summary>
    ///     Returns the next-terser compaction level, saturating at <see cref="CompactionLevel.High"/>.
    /// </summary>
    /// <returns>The escalated level.</returns>
    public static CompactionLevel Escalate(CompactionLevel level) => level switch
    {
        CompactionLevel.Low => CompactionLevel.Medium,
        CompactionLevel.Medium => CompactionLevel.High,
        _ => CompactionLevel.High,
    };

    /// <summary>
    ///     Returns the next-gentler compaction level, saturating at <see cref="CompactionLevel.Low"/>.
    /// </summary>
    /// <returns>The relaxed level.</returns>
    public static CompactionLevel Relax(CompactionLevel level) => level switch
    {
        CompactionLevel.High => CompactionLevel.Medium,
        CompactionLevel.Medium => CompactionLevel.Low,
        _ => CompactionLevel.Low,
    };

    /// <summary>
    ///     Performs one rotation: ages the turns older than the level's tail into tier one, and
    ///     cascades the tiers that fill as a result.
    /// </summary>
    /// <remarks>
    ///     Nothing here measures whether the result will fit. A rotation moves material and reports
    ///     what it did; deciding how hard to compact, and whether to discard a slot outright, belongs
    ///     to the session, which is the only place that knows how quickly the window filled.
    /// </remarks>
    /// <param name="layout">The layout to rotate. Must not be <see langword="null"/>.</param>
    /// <param name="summarizer">
    ///     The out-of-session summarizer performing each consolidation. Must not be
    ///     <see langword="null"/>, and must not return <see langword="null"/>.
    /// </param>
    /// <param name="level">The compaction level to rotate at.</param>
    /// <param name="verbatimTurns">The configured maximum verbatim tail length.</param>
    /// <param name="cancellationToken">Cancels the rotation.</param>
    /// <returns>The aged layout, the consolidation count, the level, and whether material was dropped.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="layout"/> or <paramref name="summarizer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="level"/> is not a defined <see cref="CompactionLevel"/> member.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="summarizer"/> returned <see langword="null"/> from a consolidation.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<RotationOutcome> RotateAsync(
        ContextLayout layout,
        ISummarizer summarizer,
        CompactionLevel level,
        int verbatimTurns,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(summarizer);

        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "The compaction level must be a defined CompactionLevel member.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var (rotated, consolidations, dropped) =
            await BuildAtLevelAsync(layout, summarizer, level, verbatimTurns, cancellationToken)
                .ConfigureAwait(false);

        return new RotationOutcome(rotated, consolidations, dropped);
    }

    /// <summary>
    ///     Builds the layout produced by rotating at one compaction level: rule 2 into tier one,
    ///     cascading through rules 3 and 4.
    /// </summary>
    /// <param name="layout">The layout to rotate, the layout to age.</param>
    /// <param name="summarizer">The out-of-session summarizer.</param>
    /// <param name="level">The compaction level to rotate at.</param>
    /// <param name="verbatimTurns">The configured maximum verbatim tail length.</param>
    /// <param name="cancellationToken">Cancels the consolidation.</param>
    /// <returns>The rotated layout at this level, and how many consolidations it cost.</returns>
    private static async Task<(ContextLayout Layout, int Consolidations, bool Dropped)> BuildAtLevelAsync(
        ContextLayout layout,
        ISummarizer summarizer,
        CompactionLevel level,
        int verbatimTurns,
        CancellationToken cancellationToken)
    {
        var state = new RotationState(summarizer, level, layout.Tiers);

        // A rotation must move something. The level sets how much of the tail to keep, but the tail
        // is a count of turns while the trigger is the provider's occupancy in tokens: a provider
        // counting well above this library's estimate reaches its threshold while the tail is still
        // shorter than its configured maximum. Keeping the level's figure blindly would then leave
        // nothing older to consolidate, produce a layout identical to the one handed in, and send
        // the session back to a provider it has already been told is full. So the tail keeps at most
        // what the level asks for and at most one turn fewer than it holds, whichever is smaller.
        var keep = Math.Min(
            VerbatimTurnsFor(level, verbatimTurns),
            layout.Tail.TurnCount - 1);

        if (keep < 0)
        {
            // One turn or none: there is nothing older than the newest turn to move.
            return (layout, 0, false);
        }

        var (older, retained) = layout.Tail.SplitAtTail(keep);
        if (older.Count == 0)
        {
            return (state.BuildLayout(retained), state.ConsolidationCount, state.MaterialDropped);
        }

        var slot = await state.ConsolidateTurnsAsync(older, tierIndex: 1, cancellationToken)
            .ConfigureAwait(false);

        // A blank answer produces no slot, and the material it was asked to consolidate must then
        // stay where it is. Retaining only the tail here would discard those turns while recording
        // nothing in their place - a silent loss, reported as an ordinary success, and committed to
        // the provider when the replacement session is seeded from the shortened layout.
        if (slot is null)
        {
            return (state.BuildLayout(layout.Tail), state.ConsolidationCount, false);
        }

        await state.AppendSlotAsync(slot, tier: 0, cancellationToken).ConfigureAwait(false);

        return (state.BuildLayout(retained), state.ConsolidationCount, state.MaterialDropped);
    }


    /// <summary>
    ///     The mutable working set of one rotation at one level: the tiers being aged and the
    ///     summarizer performing the consolidations.
    /// </summary>
    /// <remarks>
    ///     Created inside <see cref="BuildAtLevelAsync"/> and never escaping it, so a rotation
    ///     remains a pure function from the caller's point of view even though this type is mutable.
    /// </remarks>
    private sealed class RotationState
    {
        /// <summary>
        ///     The out-of-session summarizer performing each consolidation.
        /// </summary>
        private readonly ISummarizer _summarizer;

        /// <summary>
        ///     The compaction level, which chooses the aggressiveness clause.
        /// </summary>
        private readonly CompactionLevel _level;

        /// <summary>
        ///     The tiers being aged, tier one first, each a mutable list of slots oldest first.
        /// </summary>
        private readonly List<Slot>[] _tiers;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RotationState"/> class.
        /// </summary>
        /// <param name="summarizer">The out-of-session summarizer.</param>
        /// <param name="level">The compaction level each consolidation is asked for.</param>
        /// <param name="tiers">The current tiers to copy into mutable working lists.</param>
        public RotationState(ISummarizer summarizer, CompactionLevel level, IReadOnlyList<Tier> tiers)
        {
            _summarizer = summarizer;
            _level = level;
            _tiers = new List<Slot>[ContextLayout.TierCount];
            for (var index = 0; index < ContextLayout.TierCount; index++)
            {
                _tiers[index] = [.. tiers[index].Slots];
            }
        }

        /// <summary>
        ///     Gets the number of consolidations performed so far.
        /// </summary>
        public int ConsolidationCount { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether building this layout discarded material.
        /// </summary>
        /// <remarks>
        ///     Set when a full tier could not be consolidated because the summarizer returned a
        ///     blank record, so the arriving slot displaced the oldest rather than the tier being
        ///     cleared into a record that was never written. Reported through
        ///     <see cref="RotationOutcome.MaterialDropped"/> alongside the drop the session makes under pressure,
        ///     because a loss the session cannot see is the failure this signal exists to prevent.
        /// </remarks>
        public bool MaterialDropped { get; private set; }

        /// <summary>
        ///     Appends a slot to a tier, cascading through rules 3 and 4 when the tier is full.
        /// </summary>
        /// <param name="slot">The slot to append.</param>
        /// <param name="tier">The zero-based tier to append to.</param>
        /// <param name="cancellationToken">Cancels the consolidation a cascade may trigger.</param>
        /// <returns>A task completing when the tier, and any coarser tier it cascaded into, is settled.</returns>
        public async Task AppendSlotAsync(Slot slot, int tier, CancellationToken cancellationToken)
        {
            var slots = _tiers[tier];

            // Room to spare: append and stop.
            if (slots.Count < ContextLayout.SlotsPerTier)
            {
                slots.Add(slot);
                return;
            }

            // Rule 4: the coarsest tier is a ring — drop its oldest slot and append the new one.
            if (tier == ContextLayout.TierCount - 1)
            {
                slots.RemoveAt(0);
                MaterialDropped = true;
                slots.Add(slot);
                return;
            }

            // Rule 3: consolidate this tier's full complement of slots as peers into one slot of the
            // next tier, clear this tier, and place the arriving slot in the now-empty tier.
            var items = slots.Select(existing => existing.Content).ToList();
            var consolidated = await ConsolidateItemsAsync(items, tier + 2, cancellationToken)
                .ConfigureAwait(false);

            // A blank answer means the consolidation did not happen, so this tier cannot be cleared:
            // clearing it on the strength of a record that was never written would discard its whole
            // complement - up to a tier's worth of history - and append nothing in its place. The
            // arriving slot still needs a home and the tier is full, so it displaces the oldest, the
            // same bounded move the coarsest tier makes. One slot is lost rather than all of them,
            // and it is reported rather than silent.
            if (consolidated is null)
            {
                slots.RemoveAt(0);
                slots.Add(slot);
                MaterialDropped = true;
                return;
            }

            slots.Clear();
            await AppendSlotAsync(consolidated, tier + 1, cancellationToken).ConfigureAwait(false);

            _tiers[tier].Add(slot);
        }

        /// <summary>
        ///     Consolidates whole turns into one slot, keeping each turn indivisible.
        /// </summary>
        /// <remarks>
        ///     Each turn is rendered as a single piece, so the chunking below can place a turn in one
        ///     group or another but can never split one across two summarizer calls. That is what
        ///     makes turn-granular boundaries real: a tool result can never reach a summarizer in a
        ///     different call from the tool call it answers.
        /// </remarks>
        /// <param name="turns">The turns to consolidate, oldest first.</param>
        /// <param name="tierIndex">The one-based tier the result belongs to, for the request.</param>
        /// <param name="cancellationToken">Cancels the consolidation.</param>
        /// <returns>The consolidated slot, or <see langword="null"/> when the consolidation failed.</returns>
        public async Task<Slot?> ConsolidateTurnsAsync(
            IReadOnlyList<SessionTurn> turns,
            int tierIndex,
            CancellationToken cancellationToken)
        {
            if (turns.Count == 0)
            {
                return null;
            }

            var pieces = turns
                .Select(turn => string.Join("\n", turn.Entries.Select(entry => entry.ToTranscriptLine())))
                .ToList();

            return await ConsolidateItemsAsync(pieces, tierIndex, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Consolidates a list of indivisible pieces into one slot, chunking when the material is
        ///     too large for a single summarizer call.
        /// </summary>
        /// <remarks>
        ///     A piece is never split. Callers decide what a piece is: a whole turn for rule two, a
        ///     whole slot record for a rule three cascade.
        /// </remarks>
        /// <param name="items">The pieces to consolidate, oldest first.</param>
        /// <param name="tierIndex">The one-based tier the result belongs to, for the request.</param>
        /// <param name="cancellationToken">Cancels the consolidation.</param>
        /// <returns>
        ///     The consolidated slot, or <see langword="null"/> when there is nothing to consolidate
        ///     or the summarizer answered blank.
        /// </returns>
        public async Task<Slot?> ConsolidateItemsAsync(
            IReadOnlyList<string> items,
            int tierIndex,
            CancellationToken cancellationToken)
        {
            if (items.Count == 0)
            {
                return null;
            }

            // One call, whatever the material. A rotation fires at a fraction of the provider's own
            // window, so the material aged out is already bounded by it - and where an application
            // pairs a small summarizer with a large provider anyway, the call fails or comes back
            // empty, which is a case this engine already answers: the material stays verbatim, the
            // window keeps filling, and the session sheds its oldest card. Splitting the material to
            // pre-empt that was machinery guarding a door that is already locked.
            var content = await ConsolidateAsync(string.Join("\n", items), tierIndex, cancellationToken)
                .ConfigureAwait(false);

            return content.Length == 0 ? null : new Slot(content);
        }

        /// <summary>
        ///     Builds a layout from the current working tiers and a verbatim tail.
        /// </summary>
        /// <param name="tail">The verbatim tail.</param>
        /// <returns>A layout with the working tiers and the supplied tail.</returns>
        public ContextLayout BuildLayout(SessionTranscript tail)
        {
            var tiers = new Tier[ContextLayout.TierCount];
            for (var index = 0; index < ContextLayout.TierCount; index++)
            {
                var tier = Tier.Empty;
                foreach (var slot in _tiers[index])
                {
                    tier = tier.Append(slot);
                }

                tiers[index] = tier;
            }

            return ContextLayout.WithTiers(tail, tiers);
        }


        /// <summary>
        ///     Performs one consolidation, normalizes its result, and counts it.
        /// </summary>
        /// <remarks>
        ///     This is the one boundary a summarizer's answer crosses, so it is where a blank answer
        ///     becomes an empty string. Cancellation is honored before the call and again after it,
        ///     so a result produced across a cancellation is refused rather than accepted.
        /// </remarks>
        /// <param name="material">The material to consolidate. Must be non-blank.</param>
        /// <param name="tierIndex">The one-based tier the result belongs to.</param>
        /// <param name="cancellationToken">Cancels the consolidation.</param>
        /// <returns>The consolidated record, or an empty string for a blank answer.</returns>
        /// <exception cref="InvalidOperationException">The summarizer returned <see langword="null"/>.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
        private async Task<string> ConsolidateAsync(
            string material,
            int tierIndex,
            CancellationToken cancellationToken)
        {
            var request = new ConsolidationRequest(tierIndex, material, ConsolidationPrompt.InstructionFor(_level));

            cancellationToken.ThrowIfCancellationRequested();
            ConsolidationCount++;

            var result = await _summarizer.ConsolidateAsync(request, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (result is null)
            {
                throw new InvalidOperationException(
                    $"The summarizer returned null for a tier {tierIndex} consolidation; "
                    + "an implementation with nothing to say must return an empty string.");
            }

            // Blank becomes empty here and nowhere else, so no downstream consumer has to decide
            // what whitespace means.
            return string.IsNullOrWhiteSpace(result) ? string.Empty : result;
        }
    }
}
