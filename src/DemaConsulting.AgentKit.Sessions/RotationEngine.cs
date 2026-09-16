namespace DemaConsulting.AgentKit.Sessions;

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
    /// <param name="level">The compaction level the rotation settled at.</param>
    /// <param name="materialDropped">
    ///     Whether the rotation dropped a slot or a verbatim turn outright because a fully
    ///     consolidated context at the highest level still did not fit.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="consolidationCount"/> is negative, or <paramref name="level"/> is not a
    ///     defined <see cref="CompactionLevel"/> member.
    /// </exception>
    public RotationOutcome(
        ContextLayout layout,
        int consolidationCount,
        CompactionLevel level,
        bool materialDropped)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentOutOfRangeException.ThrowIfNegative(consolidationCount);

        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "The compaction level must be a defined CompactionLevel member.");
        }

        Layout = layout;
        ConsolidationCount = consolidationCount;
        Level = level;
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
    ///     Counts every summarizer call, including the extra ones an escalation makes when it
    ///     rebuilds the context at a terser level. Summarizer calls are the dominant cost of this
    ///     arrangement, so the true count is reported rather than the count at the settled level
    ///     alone.
    /// </remarks>
    public int ConsolidationCount { get; }

    /// <summary>
    ///     Gets the compaction level the rotation settled at.
    /// </summary>
    /// <remarks>
    ///     A rotation escalates the level — shortening the verbatim tail and asking the summarizer
    ///     to be terser — until the built seed fits or the highest level is reached. This is the
    ///     level it stopped at, which the session reports and starts its next rotation from.
    /// </remarks>
    public CompactionLevel Level { get; }

    /// <summary>
    ///     Gets a value indicating whether the rotation dropped material outright to make the
    ///     context fit.
    /// </summary>
    /// <remarks>
    ///     True when the drop-until-it-fits rule had to discard a consolidated slot or a verbatim
    ///     turn because a fully consolidated context at the highest level still exceeded the
    ///     rotation threshold. It is the honest signal that compaction bought nothing.
    /// </remarks>
    public bool MaterialDropped { get; }
}

/// <summary>
///     Ages a session's context by one rotation: a deterministic function from the current layout
///     and an injected summarizer to the next layout that fits.
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
///     <b>The five rules.</b> Rule 1 appends whole turns to the verbatim tail (done by the session,
///     not here). Rule 2 consolidates everything older than the level-adjusted tail into one slot
///     and appends it to tier one. Rule 3 consolidates a full coarse tier's slots as peers into one
///     slot of the next tier and clears it. Rule 4 makes the coarsest tier a ring, dropping its
///     oldest slot when it is full. Rule 5 measures the built seed and, while it still exceeds the
///     rotation threshold, escalates the compaction level — shortening the tail and asking for a
///     terser summary — and, once the highest level cannot make it fit, drops the oldest slot of the
///     coarsest non-empty tier, then the oldest verbatim turn, until it fits. Rule 5 terminates
///     unconditionally: the level ladder is finite, slots are finite, the tail is finite, and the
///     drop loop bottoms out at the newest turn alone.
///     </para>
///     <para>
///     <b>Escalate until it fits, and drop until it fits.</b> Escalation is tried first because it
///     preserves more history — a terser summary keeps everything, just more briefly — and dropping
///     is the last resort, taken only when the tersest structure still does not fit and reported as
///     material dropped.
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
    /// <param name="level">The compaction level the session is at.</param>
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
    /// <param name="level">The level to escalate from.</param>
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
    /// <param name="level">The level to relax from.</param>
    /// <returns>The relaxed level.</returns>
    public static CompactionLevel Relax(CompactionLevel level) => level switch
    {
        CompactionLevel.High => CompactionLevel.Medium,
        CompactionLevel.Medium => CompactionLevel.Low,
        _ => CompactionLevel.Low,
    };

    /// <summary>
    ///     Performs one rotation, returning the aged layout that fits.
    /// </summary>
    /// <param name="layout">The layout to rotate. Must not be <see langword="null"/>.</param>
    /// <param name="summarizer">
    ///     The out-of-session summarizer performing each consolidation. Must not be
    ///     <see langword="null"/>, and must not return <see langword="null"/>.
    /// </param>
    /// <param name="startLevel">The compaction level to begin the rotation at.</param>
    /// <param name="verbatimTurns">The configured maximum verbatim tail length.</param>
    /// <param name="rotationThresholdTokens">
    ///     The estimated conversation size the built seed must land at or below, in this library's
    ///     own tokens. Must be positive.
    /// </param>
    /// <param name="cancellationToken">Cancels the rotation.</param>
    /// <returns>The aged layout, the consolidation count, the settled level, and whether material was dropped.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="layout"/> or <paramref name="summarizer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="startLevel"/> is not a defined <see cref="CompactionLevel"/> member, or
    ///     <paramref name="rotationThresholdTokens"/> is not positive.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="summarizer"/> returned <see langword="null"/> from a consolidation.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static async Task<RotationOutcome> RotateAsync(
        ContextLayout layout,
        ISummarizer summarizer,
        CompactionLevel startLevel,
        int verbatimTurns,
        int rotationThresholdTokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(summarizer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rotationThresholdTokens);

        if (!Enum.IsDefined(startLevel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(startLevel),
                startLevel,
                "The compaction level must be a defined CompactionLevel member.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var level = startLevel;
        var consolidationTotal = 0;

        // Escalate until the built seed fits. Each attempt rebuilds from the original layout at the
        // current level, so the terser tail is applied cleanly rather than layered on the last try.
        while (true)
        {
            var (candidate, consolidations) =
                await BuildAtLevelAsync(layout, summarizer, level, verbatimTurns, cancellationToken)
                    .ConfigureAwait(false);
            consolidationTotal += consolidations;

            if (candidate.EstimatedConversationTokens <= rotationThresholdTokens)
            {
                return new RotationOutcome(candidate, consolidationTotal, level, materialDropped: false);
            }

            if (level != CompactionLevel.High)
            {
                level = Escalate(level);
                continue;
            }

            // At the highest level and the tersest structure still does not fit: drop until it does.
            var (dropped, droppedLayout) = DropUntilFits(candidate, rotationThresholdTokens);
            return new RotationOutcome(droppedLayout, consolidationTotal, level, dropped);
        }
    }

    /// <summary>
    ///     Builds the layout produced by rotating at one compaction level: rule 2 into tier one,
    ///     cascading through rules 3 and 4.
    /// </summary>
    /// <param name="layout">The layout to rotate, always the original so escalation is idempotent.</param>
    /// <param name="summarizer">The out-of-session summarizer.</param>
    /// <param name="level">The compaction level to build at.</param>
    /// <param name="verbatimTurns">The configured maximum verbatim tail length.</param>
    /// <param name="cancellationToken">Cancels the consolidation.</param>
    /// <returns>The rotated layout at this level, and how many consolidations it cost.</returns>
    private static async Task<(ContextLayout Layout, int Consolidations)> BuildAtLevelAsync(
        ContextLayout layout,
        ISummarizer summarizer,
        CompactionLevel level,
        int verbatimTurns,
        CancellationToken cancellationToken)
    {
        var state = new RotationState(summarizer, level, layout.Tiers);

        var keep = VerbatimTurnsFor(level, verbatimTurns);
        var (older, retained) = layout.Tail.SplitAtTail(keep);
        if (older.Count > 0)
        {
            var items = older.Select(entry => entry.ToTranscriptLine()).ToList();
            var slot = await state.ConsolidateItemsAsync(items, tierIndex: 1, cancellationToken)
                .ConfigureAwait(false);
            if (slot is not null)
            {
                await state.AppendSlotAsync(slot, tier: 0, cancellationToken).ConfigureAwait(false);
            }
        }

        return (state.BuildLayout(layout, retained), state.ConsolidationCount);
    }

    /// <summary>
    ///     Drops the oldest slot of the coarsest non-empty tier, then the oldest verbatim turn,
    ///     until the built seed fits or the newest turn stands alone.
    /// </summary>
    /// <remarks>
    ///     Pure arithmetic over the layout: no summarizer call is made, because a drop discards
    ///     material rather than reducing it. Terminates because slots and turns are finite and the
    ///     loop bottoms out at the newest turn alone.
    /// </remarks>
    /// <param name="layout">The layout whose seed still does not fit.</param>
    /// <param name="rotationThresholdTokens">The estimated conversation size the seed must fit within.</param>
    /// <returns>Whether anything was dropped, and the layout that fits or holds only its newest turn.</returns>
    private static (bool Dropped, ContextLayout Layout) DropUntilFits(
        ContextLayout layout,
        int rotationThresholdTokens)
    {
        var dropped = false;

        while (layout.EstimatedConversationTokens > rotationThresholdTokens)
        {
            var coarsest = -1;
            for (var index = ContextLayout.TierCount - 1; index >= 0; index--)
            {
                if (layout.Tiers[index].Count > 0)
                {
                    coarsest = index;
                    break;
                }
            }

            if (coarsest >= 0)
            {
                var tiers = layout.Tiers.ToArray();
                tiers[coarsest] = tiers[coarsest].DropOldest();
                layout = layout.WithTiers(layout.Tail, tiers);
                dropped = true;
                continue;
            }

            if (layout.Tail.TurnCount > 1)
            {
                layout = layout.WithTail(layout.Tail.DropOldestTurn());
                dropped = true;
                continue;
            }

            // Every slot is gone and the newest turn stands alone; there is nothing left to drop.
            break;
        }

        return (dropped, layout);
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
        /// <param name="level">The compaction level.</param>
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
                slots.Add(slot);
                return;
            }

            // Rule 3: consolidate this tier's full complement of slots as peers into one slot of the
            // next tier, clear this tier, and place the arriving slot in the now-empty tier.
            var items = slots.Select(existing => existing.Content).ToList();
            var consolidated = await ConsolidateItemsAsync(items, tier + 2, cancellationToken)
                .ConfigureAwait(false);
            slots.Clear();
            if (consolidated is not null)
            {
                await AppendSlotAsync(consolidated, tier + 1, cancellationToken).ConfigureAwait(false);
            }

            _tiers[tier].Add(slot);
        }

        /// <summary>
        ///     Consolidates a list of pieces into one slot, chunking the material when it is too
        ///     large for a single summarizer call.
        /// </summary>
        /// <param name="items">The rendered pieces to consolidate, oldest first.</param>
        /// <param name="tierIndex">The one-based tier the result belongs to, for the request.</param>
        /// <param name="cancellationToken">Cancels the consolidation.</param>
        /// <returns>
        ///     The consolidated slot, or <see langword="null"/> when there is nothing to consolidate
        ///     or the summarizer returned only blank records.
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

            // Small enough to consolidate in one call, or a single piece that cannot be split
            // further: consolidate it directly.
            var material = string.Join("\n", items);
            if (items.Count == 1
                || TokenEstimator.EstimateTokens(material) <= ContextLayout.MaxSummarizerInputTokens)
            {
                var content = await ConsolidateAsync(material, tierIndex, cancellationToken).ConfigureAwait(false);
                return content.Length == 0 ? null : new Slot(content);
            }

            // Chunk the pieces so each group fits the summarizer, and consolidate each group.
            var groups = ChunkBySize(items);
            if (groups.Count <= 1)
            {
                var content = await ConsolidateAsync(material, tierIndex, cancellationToken).ConfigureAwait(false);
                return content.Length == 0 ? null : new Slot(content);
            }

            var records = new List<string>();
            foreach (var group in groups)
            {
                var record = await ConsolidateAsync(string.Join("\n", group), tierIndex, cancellationToken)
                    .ConfigureAwait(false);
                if (record.Length > 0)
                {
                    records.Add(record);
                }
            }

            if (records.Count == 0)
            {
                return null;
            }

            if (records.Count == 1)
            {
                return new Slot(records[0]);
            }

            // Consolidate the chunk records into one. Grouping strictly reduced the piece count, so
            // recursing terminates; if it somehow did not, consolidate the records in one final call
            // rather than recursing without progress.
            if (records.Count >= items.Count)
            {
                var content = await ConsolidateAsync(string.Join("\n", records), tierIndex, cancellationToken)
                    .ConfigureAwait(false);
                return content.Length == 0 ? null : new Slot(content);
            }

            return await ConsolidateItemsAsync(records, tierIndex, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Builds a layout from the current working tiers and a verbatim tail.
        /// </summary>
        /// <param name="template">The layout whose fixed overhead is carried forward.</param>
        /// <param name="tail">The verbatim tail.</param>
        /// <returns>A layout with the working tiers and the supplied tail.</returns>
        public ContextLayout BuildLayout(ContextLayout template, SessionTranscript tail)
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

            return template.WithTiers(tail, tiers);
        }

        /// <summary>
        ///     Groups pieces so each group's rendered size fits the summarizer's input bound.
        /// </summary>
        /// <remarks>
        ///     A piece that on its own exceeds the bound becomes its own group rather than being
        ///     split mid-piece; the recursion in <see cref="ConsolidateItemsAsync"/> reduces it
        ///     afterwards.
        /// </remarks>
        /// <param name="items">The pieces to group, oldest first.</param>
        /// <returns>The groups, each a list of pieces.</returns>
        private static List<List<string>> ChunkBySize(IReadOnlyList<string> items)
        {
            var groups = new List<List<string>>();
            var current = new List<string>();
            var currentTokens = 0;

            foreach (var item in items)
            {
                var itemTokens = TokenEstimator.EstimateTokens(item);
                if (current.Count > 0 && currentTokens + itemTokens > ContextLayout.MaxSummarizerInputTokens)
                {
                    groups.Add(current);
                    current = [];
                    currentTokens = 0;
                }

                current.Add(item);
                currentTokens += itemTokens;
            }

            if (current.Count > 0)
            {
                groups.Add(current);
            }

            return groups;
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
