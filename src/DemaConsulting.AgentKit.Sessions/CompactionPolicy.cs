namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     The controls governing when a session rotates and how much of each tier survives.
/// </summary>
/// <remarks>
///     <para>
///     <b>Context is laid out most-stable-first.</b> A session's context reads
///     <c>[system][tool declarations][tier N coarse] ... [tier 1][tier 0 verbatim]</c>. Tier zero
///     holds the most recent turns exactly as they happened; each higher tier holds a
///     progressively coarser record of older history. This policy carries one token budget per
///     tier, and those budgets are what makes the whole arrangement bounded: the context can never
///     exceed the system prompt, plus the tool declarations, plus the sum of the tier budgets.
///     </para>
///     <para>
///     <b>Why tiers rather than one rolling summary.</b> A single rolling summary is a downward
///     ratchet — each pass re-summarizes the previous summary, so detail is lost repeatedly rather
///     than once. The measurement that settled this, over 50 rotations with a deliberately
///     compressed window (n = 50 rotations, one run per arrangement, recorded in the compaction
///     spike that preceded this package): a flat rolling summary recalled 100 percent of probes
///     from the most recent rotations but nothing at all beyond 15 rotations ago, finishing at
///     5 of 23 probes overall, with its final context collapsed to 363 tokens. The tiered
///     arrangement held between 50 and 100 percent recall all the way out to 50 rotations,
///     finished at 13 of 17 probes, retained 3,158 tokens of context, and spent 17 percent fewer
///     summarizer tokens (381,440 against 461,173) because it consolidates only the tiers that
///     actually overflowed. The cost is real and is stated plainly: tier content occupies window
///     space, so the tiered arrangement completed roughly 27 percent fewer turns of work per
///     rotation.
///     </para>
///     <para>
///     <b>Instances are immutable after construction and safe for concurrent use.</b> An
///     application that configures nothing receives <see cref="Default"/>.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     A host that wants a deeper hierarchy, or that rotates earlier because its provider's
///     reported usage runs ahead of the estimate, replaces only what it cares about.
///     </para>
///     <code>
///     // Five tiers, rotating at 60% of the effective window instead of 70%.
///     var policy = new CompactionPolicy(
///         tierBudgetTokens: [2000, 1200, 900, 700, 500],
///         rotationThreshold: 0.60);
///
///     // The whole arrangement is bounded by construction: this is the most conversation
///     // context the session can ever hold.
///     var bound = policy.TotalTierBudgetTokens;
///     </code>
/// </example>
public sealed class CompactionPolicy
{
    /// <summary>
    ///     The default fraction of the effective window at which a session rotates.
    /// </summary>
    /// <remarks>
    ///     Rotation is not free — it invalidates the provider's prompt cache and spends summarizer
    ///     tokens — so it should be infrequent, and it must never be late. Firing at 70 percent
    ///     leaves 30 percent of the effective window as headroom, which covers both the error in a
    ///     character-ratio token estimate and the turn in flight when the threshold is crossed.
    ///     Because the surviving tiers are sized to a few thousand tokens, a rotation lands the
    ///     session near 40 percent of the window rather than just under the threshold, so the
    ///     hysteresis is generous and rotation does not re-trigger immediately.
    /// </remarks>
    public const double DefaultRotationThreshold = 0.70;

    /// <summary>
    ///     The default output-to-input ratio at or above which a consolidation is treated as
    ///     saturated.
    /// </summary>
    /// <remarks>
    ///     Consolidation works by removing redundancy. When a consolidation returns output nearly
    ///     as large as the material it was given, there is no redundancy left to remove, and
    ///     rotating again will spend summarizer tokens for no reduction. Ninety percent is
    ///     deliberately close to one: a genuine consolidation reduces its input substantially, so
    ///     a result this close to its input is not a near miss but a signal that the material has
    ///     been compressed as far as it will go. The engine surfaces the signal rather than acting
    ///     on it, because what to do about an agent that has saturated its context is the
    ///     application's decision, not this library's.
    /// </remarks>
    public const double DefaultSaturationRatio = 0.90;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CompactionPolicy"/> class.
    /// </summary>
    /// <remarks>
    ///     Every parameter is optional and defaults to the corresponding published value, so an
    ///     application replaces one control — <c>new CompactionPolicy(rotationThreshold: 0.6)</c> —
    ///     without restating the others. Validation happens before any assignment so a rejected
    ///     policy never exists even briefly; every rejected condition is a defect in the host's
    ///     configuration code rather than something a model can provoke.
    /// </remarks>
    /// <param name="tierBudgetTokens">
    ///     The token budget for each tier, tier zero first. Must hold at least two budgets, every
    ///     budget must be positive, and no budget may be larger than the one before it.
    ///     <see langword="null"/> selects <see cref="DefaultTierBudgetTokens"/>.
    /// </param>
    /// <param name="rotationThreshold">
    ///     The fraction of the effective window at which a session rotates. Must be greater than
    ///     zero and at most one.
    /// </param>
    /// <param name="saturationRatio">
    ///     The output-to-input ratio at or above which a consolidation is reported as saturated.
    ///     Must be greater than zero and at most one.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tierBudgetTokens"/> holds fewer than two budgets, or a budget is larger
    ///     than the one before it.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     A tier budget is not positive, or <paramref name="rotationThreshold"/> or
    ///     <paramref name="saturationRatio"/> lies outside the range greater than zero and at most
    ///     one.
    /// </exception>
    public CompactionPolicy(
        IReadOnlyList<int>? tierBudgetTokens = null,
        double rotationThreshold = DefaultRotationThreshold,
        double saturationRatio = DefaultSaturationRatio)
    {
        var budgets = tierBudgetTokens ?? DefaultTierBudgetTokens;

        // At least two tiers, because one tier is not a hierarchy: with a single verbatim tier
        // there is nowhere for overflowing history to age into, and the arrangement degenerates
        // to dropping the oldest turns outright.
        if (budgets.Count < 2)
        {
            throw new ArgumentException(
                "At least two tier budgets are required: a verbatim tier and somewhere for it to age into.",
                nameof(tierBudgetTokens));
        }

        // A non-positive budget would make a tier unable to hold anything, and a budget larger
        // than the tier it ages from would mean the coarser record is allowed more room than the
        // finer one it replaces - which is the opposite of what consolidation is for.
        for (var index = 0; index < budgets.Count; index++)
        {
            if (budgets[index] <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(tierBudgetTokens),
                    budgets[index],
                    $"Tier {index} is budgeted {budgets[index]} tokens; every tier budget must be positive.");
            }

            if (index > 0 && budgets[index] > budgets[index - 1])
            {
                throw new ArgumentException(
                    $"Tier {index} is budgeted {budgets[index]} tokens, more than tier {index - 1}'s "
                    + $"{budgets[index - 1]}; a coarser tier must not be larger than the tier it ages from.",
                    nameof(tierBudgetTokens));
            }
        }

        // A threshold at or below zero would rotate on every turn; above one it could never fire.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rotationThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rotationThreshold, 1.0);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(saturationRatio);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(saturationRatio, 1.0);

        TierBudgetTokens = [.. budgets];
        RotationThreshold = rotationThreshold;
        SaturationRatio = saturationRatio;
        TotalTierBudgetTokens = TierBudgetTokens.Sum();
    }

    /// <summary>
    ///     Gets the tier budgets an application receives when it configures none.
    /// </summary>
    /// <remarks>
    ///     Four tiers of 2,000, 1,200, 900 and 700 tokens, totaling 4,800 tokens of bounded
    ///     conversation context. The shape is what matters more than the exact numbers: tier zero
    ///     is the largest because verbatim recent history is the most useful thing in the window,
    ///     and each older tier is smaller because a coarser record of older material should cost
    ///     less. These are the starting values the compaction spike that preceded this package ran
    ///     with (n = 50 rotations, one run, recorded in that spike); they are published as
    ///     constants precisely so an application with a different window or a different task shape
    ///     can replace them.
    /// </remarks>
    public static IReadOnlyList<int> DefaultTierBudgetTokens { get; } = [2000, 1200, 900, 700];

    /// <summary>
    ///     Gets the policy an application receives when it configures nothing.
    /// </summary>
    /// <remarks>
    ///     A single shared instance rather than a factory method, because the type is immutable and
    ///     sharing it is therefore free of risk. Callers may compare against it by reference to
    ///     establish that no host configuration was applied.
    /// </remarks>
    public static CompactionPolicy Default { get; } = new();

    /// <summary>
    ///     Gets the token budget for each tier, tier zero first.
    /// </summary>
    /// <remarks>
    ///     Tier zero's budget bounds the verbatim history; every higher budget bounds one
    ///     consolidated record. The list is a defensive copy taken at construction, so a caller
    ///     that mutates the list it supplied cannot change this policy afterwards.
    /// </remarks>
    public IReadOnlyList<int> TierBudgetTokens { get; }

    /// <summary>
    ///     Gets the number of tiers, counting the verbatim tier zero.
    /// </summary>
    public int TierCount => TierBudgetTokens.Count;

    /// <summary>
    ///     Gets the fraction of the effective window at which a session rotates.
    /// </summary>
    public double RotationThreshold { get; }

    /// <summary>
    ///     Gets the output-to-input ratio at or above which a consolidation is reported as saturated.
    /// </summary>
    public double SaturationRatio { get; }

    /// <summary>
    ///     Gets the most conversation context this policy can ever hold, in tokens.
    /// </summary>
    /// <remarks>
    ///     The sum of every tier budget. Together with the system prompt and the tool declarations
    ///     this is the bound the whole arrangement is constructed to respect, and
    ///     <see cref="AgentSessionOptions"/> refuses a configuration whose effective window cannot
    ///     accommodate it. The bound is a post-rotation property: between rotations tier zero grows
    ///     past its budget, which is precisely the headroom the rotation threshold reserves.
    /// </remarks>
    public int TotalTierBudgetTokens { get; }
}
