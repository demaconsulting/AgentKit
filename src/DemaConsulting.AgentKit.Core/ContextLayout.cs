using System.Collections.ObjectModel;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     One slot: a single consolidated record of a span of older history.
/// </summary>
/// <remarks>
///     <para>
///     A slot is deliberately a single string rather than a structured record. What a consolidation
///     produces is prose written for the agent to rely on later, and imposing a structure on it
///     here would either constrain the summarizer or require this library to parse a model's
///     output. The slot's job is to hold that prose.
///     </para>
///     <para>
///     Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
internal sealed class Slot
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Slot"/> class.
    /// </summary>
    /// <param name="content">The consolidated record. Must not be <see langword="null"/> or blank.</param>
    /// <exception cref="ArgumentException"><paramref name="content"/> is <see langword="null"/> or blank.</exception>
    public Slot(string content)
    {
        // A blank slot holds nothing and would spend framing to say nothing. The engine normalizes
        // a blank summarizer answer to empty and never builds a slot from one, so a blank arriving
        // here is a defect rather than history.
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("A slot must hold a non-blank record.", nameof(content));
        }

        Content = content;
    }

    /// <summary>
    ///     Gets the consolidated record.
    /// </summary>
    public string Content { get; }
}

/// <summary>
///     One tier: an ordered list of consolidated slots, oldest first, which the rotation engine
///     keeps to at most <see cref="ContextLayout.SlotsPerTier"/> slots.
/// </summary>
/// <remarks>
///     <para>
///     A tier holds a fixed number of consolidated slots. When it is full and another slot arrives,
///     what happens depends on which tier it is — a coarser tier consolidates its slots as peers and
///     empties, while the coarsest tier drops its oldest slot as a ring — but those rules belong to
///     the rotation engine. The tier itself only knows how to hold slots, say how many it holds, and
///     return a copy of itself without its oldest.
///     </para>
///     <para>
///     Instances are immutable: every mutator returns a new tier.
///     </para>
/// </remarks>
internal sealed class Tier
{
    /// <summary>
    ///     The slots, oldest first.
    /// </summary>
    private readonly Slot[] _slots;

    /// <summary>
    ///     The read-only view handed out by <see cref="Slots"/>.
    /// </summary>
    private readonly ReadOnlyCollection<Slot> _slotsView;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Tier"/> class.
    /// </summary>
    /// <param name="slots">The slots, oldest first. Ownership passes to this instance.</param>
    private Tier(Slot[] slots)
    {
        _slots = slots;
        _slotsView = Array.AsReadOnly(slots);
    }

    /// <summary>
    ///     Gets the tier holding no slots.
    /// </summary>
    public static Tier Empty { get; } = new([]);

    /// <summary>
    ///     Gets the slots, oldest first.
    /// </summary>
    public IReadOnlyList<Slot> Slots => _slotsView;

    /// <summary>
    ///     Gets the number of slots the tier holds.
    /// </summary>
    public int Count => _slots.Length;

    /// <summary>
    ///     Gets a value indicating whether the tier holds no slots.
    /// </summary>
    public bool IsEmpty => _slots.Length == 0;

    /// <summary>
    ///     Returns a tier with one more slot at the end.
    /// </summary>
    /// <param name="slot">The slot to append. Must not be <see langword="null"/>.</param>
    /// <returns>A new tier; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="slot"/> is <see langword="null"/>.</exception>
    public Tier Append(Slot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        return new Tier([.. _slots, slot]);
    }

    /// <summary>
    ///     Returns a tier with its oldest slot removed.
    /// </summary>
    /// <returns>A new tier; this one is unchanged.</returns>
    /// <exception cref="InvalidOperationException">The tier is empty.</exception>
    public Tier DropOldest()
    {
        if (_slots.Length == 0)
        {
            throw new InvalidOperationException("An empty tier has no slot to drop.");
        }

        return new Tier(_slots[1..]);
    }
}

/// <summary>
///     The whole of a session's context as this library accounts for it: the coarse tiers of
///     consolidated slots, and the verbatim recent turns.
/// </summary>
/// <remarks>
///     <para>
///     <b>A round-robin structure configured in counts.</b> The context holds
///     <see cref="TierCount"/> coarse tiers, each a ring of at most <see cref="SlotsPerTier"/>
///     consolidated slots, and behind them a verbatim tail of whole turns. Resolution decays with
///     age by construction: recent turns are word for word, older history is a slot in tier one,
///     older still is a slot in a coarser tier. Nothing is weighed against a token budget.
///     </para>
///     <para>
///     <b>Laid out most-stable-first.</b> <see cref="BuildSeed"/> emits the coarsest tier first,
///     then each finer tier, then the verbatim tail, so the further back in the seed's prefix the
///     more stable the content — the shape a provider's prompt cache rewards.
///     </para>
///     <para>
///     Instances are immutable: every mutator returns a new layout. That is what makes the rotation
///     engine a pure function and lets a test compare a before and after. Instances are safe for
///     concurrent use.
///     </para>
/// </remarks>
internal sealed class ContextLayout
{
    /// <summary>
    ///     The number of slots one tier holds. An internal constant, not a setting: it is the value
    ///     the recall measurement was taken at, and an application has no basis for choosing it.
    /// </summary>
    public const int SlotsPerTier = 4;

    /// <summary>
    ///     The number of coarse tiers. An internal constant, not a setting, for the same reason as
    ///     <see cref="SlotsPerTier"/>.
    /// </summary>
    public const int TierCount = 3;

    /// <summary>
    ///     The fraction of the effective window at which a session rotates. An internal constant.
    /// </summary>
    /// <remarks>
    ///     Rotating at 70 percent leaves 30 percent of the effective window as headroom, which
    ///     absorbs one turn's overshoot on a provider that can only report its usage after a turn,
    ///     and keeps the session clear of another compactor's floor.
    /// </remarks>
    public const double RotationThreshold = 0.70;


    /// <summary>
    ///     The text preceding a tier's own index in a seeded record's label.
    /// </summary>
    private const string RecordLabelPrefix = "Consolidated record of earlier work (detail level ";

    /// <summary>
    ///     The text following a tier's own index in a seeded record's label.
    /// </summary>
    private const string RecordLabelSuffix = ", coarser levels cover older material):\n";

    /// <summary>
    ///     The coarse tiers, tier one first (finest coarse) and the coarsest last.
    /// </summary>
    private readonly Tier[] _tiers;

    /// <summary>
    ///     The read-only view handed out by <see cref="Tiers"/>.
    /// </summary>
    private readonly ReadOnlyCollection<Tier> _tiersView;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ContextLayout"/> class.
    /// </summary>
    /// <param name="tail">The verbatim recent turns.</param>
    /// <param name="tiers">The coarse tiers, tier one first. Ownership passes to this instance.</param>
    private ContextLayout(SessionTranscript tail, Tier[] tiers)
    {
        Tail = tail;
        _tiers = tiers;
        _tiersView = Array.AsReadOnly(tiers);
    }

    /// <summary>
    ///     Gets the verbatim recent turns.
    /// </summary>
    public SessionTranscript Tail { get; }

    /// <summary>
    ///     Gets the coarse tiers, tier one first and the coarsest last.
    /// </summary>
    public IReadOnlyList<Tier> Tiers => _tiersView;

    /// <summary>
    ///     Creates an empty layout.
    /// </summary>
    /// <returns>A layout holding no history and no slots.</returns>
    public static ContextLayout Create()
    {
        var tiers = new Tier[TierCount];
        Array.Fill(tiers, Tier.Empty);

        return new ContextLayout(SessionTranscript.Empty, tiers);
    }

    /// <summary>
    ///     Returns a layout with a different verbatim tail.
    /// </summary>
    /// <param name="tail">The new verbatim tail. Must not be <see langword="null"/>.</param>
    /// <returns>A new layout; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tail"/> is <see langword="null"/>.</exception>
    public ContextLayout WithTail(SessionTranscript tail)
    {
        ArgumentNullException.ThrowIfNull(tail);

        return new ContextLayout(tail, _tiers);
    }

    /// <summary>
    ///     Returns a layout with different coarse tiers and a different verbatim tail.
    /// </summary>
    /// <remarks>
    ///     Both are replaced together because a rotation changes both at once.
    /// </remarks>
    /// <param name="tail">The new verbatim tail. Must not be <see langword="null"/>.</param>
    /// <param name="tiers">
    ///     The new coarse tiers, tier one first. Must not be <see langword="null"/>, must contain no
    ///     <see langword="null"/> entry, and must hold exactly <see cref="TierCount"/> tiers.
    /// </param>
    /// <returns>A new layout; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tail"/> or <paramref name="tiers"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tiers"/> holds the wrong number of tiers or contains a
    ///     <see langword="null"/> entry.
    /// </exception>
    public static ContextLayout WithTiers(SessionTranscript tail, IReadOnlyList<Tier> tiers)
    {
        ArgumentNullException.ThrowIfNull(tail);
        ArgumentNullException.ThrowIfNull(tiers);

        if (tiers.Count != TierCount)
        {
            throw new ArgumentException(
                $"Expected {TierCount} tiers, but {tiers.Count} were supplied.",
                nameof(tiers));
        }

        var copied = new Tier[TierCount];
        for (var index = 0; index < TierCount; index++)
        {
            copied[index] = tiers[index]
                ?? throw new ArgumentException("A tier in the list is null.", nameof(tiers));
        }

        return new ContextLayout(tail, copied);
    }

    /// <summary>
    ///     Builds the history a fresh provider session is seeded with, most stable first.
    /// </summary>
    /// <remarks>
    ///     Emits the tiers from coarsest to finest, each slot oldest first, then the verbatim tail
    ///     in its own order. Each slot is labeled with the detail level it belongs to, because a
    ///     model handed several records with no ordering cue cannot tell which supersedes which. The
    ///     system prompt and tool declarations are not emitted here — they are supplied to a
    ///     provider through its own configuration.
    /// </remarks>
    /// <returns>The seed history, most stable first. Never <see langword="null"/>.</returns>
    public IReadOnlyList<TranscriptEntry> BuildSeed()
    {
        var seed = new List<TranscriptEntry>();

        // Coarsest first: the oldest, least detailed material sits furthest from the live turn.
        for (var tierIndex = TierCount - 1; tierIndex >= 0; tierIndex--)
        {
            var tier = _tiers[tierIndex];
            foreach (var slot in tier.Slots)
            {
                seed.Add(TranscriptEntry.ContextRecord(RecordLabel(tierIndex + 1) + slot.Content));
            }
        }

        // Then the verbatim recent history, in the order it happened.
        seed.AddRange(Tail.Entries);

        return Array.AsReadOnly(seed.ToArray());
    }

    /// <summary>
    ///     Builds the label a slot's record carries when it is seeded.
    /// </summary>
    /// <param name="tierIndex">The one-based tier the slot belongs to.</param>
    /// <returns>The label, ending in the newline that separates it from the record.</returns>
    private static string RecordLabel(int tierIndex) =>
        $"{RecordLabelPrefix}{tierIndex}{RecordLabelSuffix}";
}
