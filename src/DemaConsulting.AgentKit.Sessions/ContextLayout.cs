namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     One coarse tier: a consolidated record of older history, and the budget it must fit within.
/// </summary>
/// <remarks>
///     <para>
///     A tier is deliberately a single string rather than a structured record. What a consolidation
///     produces is prose written for the agent to rely on later, and imposing a structure on it
///     here would either constrain the summarizer or require this library to parse a model's
///     output — both of which would make the arrangement brittle for no gain. The tier's job is to
///     hold that prose and to know whether it still fits.
///     </para>
///     <para>
///     Tier zero is not represented by this type: it holds verbatim history and is a
///     <see cref="SessionTranscript"/>. Instances of this type carry index one and above.
///     </para>
///     <para>
///     Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
public sealed class ContextTier
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ContextTier"/> class.
    /// </summary>
    /// <param name="index">
    ///     The tier's position, counting the verbatim tier as zero. Must be one or greater.
    /// </param>
    /// <param name="budgetTokens">The tokens this tier may occupy. Must be positive.</param>
    /// <param name="content">
    ///     The consolidated record. Must not be <see langword="null"/>; empty means the tier holds
    ///     nothing yet.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="index"/> is less than one, or <paramref name="budgetTokens"/> is not positive.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    public ContextTier(int index, int budgetTokens, string content)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budgetTokens);
        ArgumentNullException.ThrowIfNull(content);

        Index = index;
        BudgetTokens = budgetTokens;
        Content = content;
        EstimatedTokens = TokenEstimator.EstimateTokens(content);
    }

    /// <summary>
    ///     Gets the tier's position, counting the verbatim tier as zero.
    /// </summary>
    public int Index { get; }

    /// <summary>
    ///     Gets the tokens this tier may occupy.
    /// </summary>
    public int BudgetTokens { get; }

    /// <summary>
    ///     Gets the consolidated record, or an empty string when the tier holds nothing yet.
    /// </summary>
    public string Content { get; }

    /// <summary>
    ///     Gets the estimated tokens the record occupies.
    /// </summary>
    public int EstimatedTokens { get; }

    /// <summary>
    ///     Gets a value indicating whether the tier holds no record yet.
    /// </summary>
    /// <remarks>
    ///     An empty tier is the signal that a consolidation into it is a first recording rather than
    ///     an extension, which is what <see cref="ConsolidationRequest.IsDegradation"/> reports to
    ///     the summarizer.
    /// </remarks>
    public bool IsEmpty => Content.Length == 0;

    /// <summary>
    ///     Gets a value indicating whether the record fits the tier's budget.
    /// </summary>
    public bool IsWithinBudget => EstimatedTokens <= BudgetTokens;

    /// <summary>
    ///     Returns a tier with the same index and budget carrying a different record.
    /// </summary>
    /// <param name="content">The new record. Must not be <see langword="null"/>.</param>
    /// <returns>A new tier; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    public ContextTier WithContent(string content) => new(Index, BudgetTokens, content);

    /// <summary>
    ///     Creates a tier holding no record yet.
    /// </summary>
    /// <param name="index">The tier's position. Must be one or greater.</param>
    /// <param name="budgetTokens">The tokens the tier may occupy. Must be positive.</param>
    /// <returns>An empty tier.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="index"/> is less than one, or <paramref name="budgetTokens"/> is not positive.
    /// </exception>
    public static ContextTier Empty(int index, int budgetTokens) => new(index, budgetTokens, string.Empty);
}

/// <summary>
///     The whole of a session's context as this library accounts for it: fixed overhead, the coarse
///     tiers, and the verbatim recent history.
/// </summary>
/// <remarks>
///     <para>
///     <b>Laid out most-stable-first.</b> The arrangement is
///     <c>[system][tool declarations][tier N coarse] ... [tier 1][tier 0 verbatim]</c>, and
///     <see cref="BuildSeed"/> emits it in exactly that order. Stability decreases from left to
///     right, which is what allows a provider's prompt cache to match the longest possible prefix
///     between turns.
///     </para>
///     <para>
///     <b>Bounded by construction.</b> The most this layout can ever hold is the system prompt,
///     plus the tool declarations, plus the sum of the tier budgets — published as
///     <see cref="MaximumBoundTokens"/>. <see cref="IsWithinBound"/> asserts it. The bound is a
///     <em>post-rotation</em> property and is documented as one: between rotations the context is
///     strictly append-only, so tier zero grows past its budget until the next rotation batches
///     everything back inside the bound. That growth is exactly what the rotation threshold's
///     headroom is reserved for.
///     </para>
///     <para>
///     Instances are immutable: every mutator returns a new layout. That is what makes the rotation
///     engine a pure function and lets a test compare a before and after. Instances are safe for
///     concurrent use.
///     </para>
/// </remarks>
public sealed class ContextLayout
{
    /// <summary>
    ///     The coarse tiers, tier one first.
    /// </summary>
    private readonly ContextTier[] _coarseTiers;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ContextLayout"/> class.
    /// </summary>
    /// <remarks>
    ///     Private because a layout is only ever produced by <see cref="Create"/> or by one of the
    ///     <c>With</c> methods, both of which guarantee the tier array matches the policy. Taking
    ///     ownership of the array avoids copying it on every rotation.
    /// </remarks>
    /// <param name="policy">The policy whose budgets this layout observes.</param>
    /// <param name="systemTokens">The tokens the system prompt occupies.</param>
    /// <param name="toolDeclarationTokens">The tokens the tool declarations occupy.</param>
    /// <param name="transcript">The verbatim tier-zero history.</param>
    /// <param name="coarseTiers">The coarse tiers, tier one first. Ownership passes to this instance.</param>
    private ContextLayout(
        CompactionPolicy policy,
        int systemTokens,
        int toolDeclarationTokens,
        SessionTranscript transcript,
        ContextTier[] coarseTiers)
    {
        Policy = policy;
        SystemTokens = systemTokens;
        ToolDeclarationTokens = toolDeclarationTokens;
        Transcript = transcript;
        _coarseTiers = coarseTiers;
    }

    /// <summary>
    ///     Gets the policy whose budgets this layout observes.
    /// </summary>
    public CompactionPolicy Policy { get; }

    /// <summary>
    ///     Gets the tokens the system prompt occupies.
    /// </summary>
    /// <remarks>
    ///     Fixed overhead: present on every turn and never consolidated, so it is subtracted from
    ///     the provider window before any rotation percentage is applied.
    /// </remarks>
    public int SystemTokens { get; }

    /// <summary>
    ///     Gets the tokens the tool declarations occupy.
    /// </summary>
    /// <remarks>
    ///     Fixed overhead for the same reason as <see cref="SystemTokens"/>, and materially larger:
    ///     declarations for a realistic tool set run to thousands of tokens.
    /// </remarks>
    public int ToolDeclarationTokens { get; }

    /// <summary>
    ///     Gets the verbatim tier-zero history.
    /// </summary>
    public SessionTranscript Transcript { get; }

    /// <summary>
    ///     Gets the coarse tiers, tier one first.
    /// </summary>
    /// <remarks>
    ///     Holds one fewer element than <see cref="CompactionPolicy.TierCount"/>, because tier zero
    ///     is the transcript rather than a consolidated record. Element zero is tier one.
    /// </remarks>
    public IReadOnlyList<ContextTier> CoarseTiers => _coarseTiers;

    /// <summary>
    ///     Gets the tokens the conversation occupies: the coarse tiers plus the verbatim history.
    /// </summary>
    /// <remarks>
    ///     Excludes the fixed overhead, so this is the figure the rotation threshold — expressed as
    ///     a fraction of the effective window — is compared against.
    /// </remarks>
    public int ConversationTokens
    {
        get
        {
            var total = Transcript.EstimatedTokens;
            foreach (var tier in _coarseTiers)
            {
                total += tier.EstimatedTokens;
            }

            return total;
        }
    }

    /// <summary>
    ///     Gets the tokens the whole context occupies, fixed overhead included.
    /// </summary>
    public int TotalEstimatedTokens => SystemTokens + ToolDeclarationTokens + ConversationTokens;

    /// <summary>
    ///     Gets the most this layout can ever occupy: fixed overhead plus every tier budget.
    /// </summary>
    /// <remarks>
    ///     This is the bound the whole arrangement exists to respect. It is a property of the
    ///     configuration alone — it does not depend on what the session has done — which is what
    ///     makes it something an application can reason about before starting.
    /// </remarks>
    public int MaximumBoundTokens => SystemTokens + ToolDeclarationTokens + Policy.TotalTierBudgetTokens;

    /// <summary>
    ///     Gets a value indicating whether the context currently sits within its construction bound.
    /// </summary>
    /// <remarks>
    ///     True immediately after a rotation. False in the ordinary course of a session between
    ///     rotations, because tier zero is append-only and grows past its budget until the next
    ///     rotation. A caller checking this outside a post-rotation assertion is asking the wrong
    ///     question; see the class remarks.
    /// </remarks>
    public bool IsWithinBound => TotalEstimatedTokens <= MaximumBoundTokens;

    /// <summary>
    ///     Creates an empty layout for a policy and a measured fixed overhead.
    /// </summary>
    /// <remarks>
    ///     Allocates one empty coarse tier per non-verbatim budget, so the tier array matches the
    ///     policy from the outset and no code path has to grow it later.
    /// </remarks>
    /// <param name="policy">The policy whose budgets the layout observes. Must not be <see langword="null"/>.</param>
    /// <param name="systemTokens">The tokens the system prompt occupies. Must not be negative.</param>
    /// <param name="toolDeclarationTokens">
    ///     The tokens the tool declarations occupy. Must not be negative.
    /// </param>
    /// <returns>A layout holding no history and no records.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="systemTokens"/> or <paramref name="toolDeclarationTokens"/> is negative.
    /// </exception>
    public static ContextLayout Create(CompactionPolicy policy, int systemTokens, int toolDeclarationTokens)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegative(systemTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(toolDeclarationTokens);

        // One coarse tier per budget above tier zero; tier zero is the transcript.
        var tiers = new ContextTier[policy.TierCount - 1];
        for (var index = 0; index < tiers.Length; index++)
        {
            tiers[index] = ContextTier.Empty(index + 1, policy.TierBudgetTokens[index + 1]);
        }

        return new ContextLayout(policy, systemTokens, toolDeclarationTokens, SessionTranscript.Empty, tiers);
    }

    /// <summary>
    ///     Returns a layout with a different verbatim history.
    /// </summary>
    /// <param name="transcript">The new tier-zero history. Must not be <see langword="null"/>.</param>
    /// <returns>A new layout; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="transcript"/> is <see langword="null"/>.</exception>
    public ContextLayout WithTranscript(SessionTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        return new ContextLayout(Policy, SystemTokens, ToolDeclarationTokens, transcript, _coarseTiers);
    }

    /// <summary>
    ///     Returns a layout with different coarse tiers and a different verbatim history.
    /// </summary>
    /// <remarks>
    ///     Both are replaced together because a rotation changes both at once, and applying them
    ///     separately would produce an intermediate layout that never actually exists.
    /// </remarks>
    /// <param name="transcript">The new tier-zero history. Must not be <see langword="null"/>.</param>
    /// <param name="coarseTiers">
    ///     The new coarse tiers, tier one first. Must not be <see langword="null"/>, must contain no
    ///     <see langword="null"/> entry, and must hold exactly one fewer element than the policy's
    ///     tier count.
    /// </param>
    /// <returns>A new layout; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="transcript"/> or <paramref name="coarseTiers"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="coarseTiers"/> holds the wrong number of tiers, or contains a
    ///     <see langword="null"/> entry.
    /// </exception>
    public ContextLayout WithTiers(SessionTranscript transcript, IReadOnlyList<ContextTier> coarseTiers)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(coarseTiers);

        if (coarseTiers.Count != Policy.TierCount - 1)
        {
            throw new ArgumentException(
                $"Expected {Policy.TierCount - 1} coarse tiers for a {Policy.TierCount}-tier policy, "
                + $"but {coarseTiers.Count} were supplied.",
                nameof(coarseTiers));
        }

        var copied = new ContextTier[coarseTiers.Count];
        for (var index = 0; index < coarseTiers.Count; index++)
        {
            copied[index] = coarseTiers[index]
                ?? throw new ArgumentException("A tier in the list is null.", nameof(coarseTiers));
        }

        return new ContextLayout(Policy, SystemTokens, ToolDeclarationTokens, transcript, copied);
    }

    /// <summary>
    ///     Builds the history a fresh provider session is seeded with, most stable first.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Emits the coarse tiers from coarsest to finest, then the verbatim history in its own
    ///     order. Each tier record is labeled with how old the material it covers is, because a
    ///     model handed several records with no ordering cue cannot tell which supersedes which.
    ///     Empty tiers are skipped: seeding an empty record would spend framing tokens to say
    ///     nothing.
    ///     </para>
    ///     <para>
    ///     The system prompt and the tool declarations are <em>not</em> emitted here. They are
    ///     supplied to a provider through its own configuration rather than as history, which is
    ///     why they are accounted for as fixed overhead and not as entries.
    ///     </para>
    /// </remarks>
    /// <returns>The seed history, most stable first. Never <see langword="null"/>.</returns>
    public IReadOnlyList<TranscriptEntry> BuildSeed()
    {
        var seed = new List<TranscriptEntry>(_coarseTiers.Length + Transcript.Entries.Count);

        // Coarsest first: the oldest, least detailed material sits furthest from the live turn.
        for (var index = _coarseTiers.Length - 1; index >= 0; index--)
        {
            var tier = _coarseTiers[index];
            if (tier.IsEmpty)
            {
                continue;
            }

            seed.Add(TranscriptEntry.ContextRecord(
                $"Consolidated record of earlier work (detail level {tier.Index}, coarser levels cover older material):\n{tier.Content}"));
        }

        // Then the verbatim recent history, in the order it happened.
        seed.AddRange(Transcript.Entries);
        return seed;
    }
}
