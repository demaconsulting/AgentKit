namespace DemaConsulting.AgentKit.Tools.Memory;

/// <summary>
///     The controls an application author sets over the memory family: how similar a new
///     descriptor must be to an existing one before it is treated as a near-duplicate, and how
///     many memories a recall returns.
/// </summary>
/// <remarks>
///     <para>
///     <b>Every value here is the author's decision, not this library's.</b> AgentKit guarantees
///     that every file call compares the new descriptor against the memories the store holds when
///     that call runs, and that a recall returns at most the
///     configured number of memories; it takes no view on what the right threshold is for a given
///     corpus, because the right threshold depends on the embedding model, the subject matter and
///     how much the author would rather re-read a duplicate than miss a conflict.
///     </para>
///     <para>
///     <see cref="DefaultNearDuplicateThreshold"/> is 0.88. It is a measured starting point rather
///     than a constant of nature: over a seventeen-document technical corpus, a genuine
///     contradiction between two statements of the same fact — one saying 12 psi and one saying 18
///     psi — scored 0.965 cosine against each other, while unrelated statements from the same
///     corpus sat well below 0.88. A threshold in the high eighties therefore catches restatements
///     and contradictions of one fact without treating two different facts about one subject as
///     the same memory. An author whose corpus is narrower should expect to raise it.
///     </para>
///     <para>
///     <see cref="DefaultRecallCount"/> is 5. Recall returns whole memories — a descriptor plus its
///     never-embedded detail payload — so each match costs real context, and a top-k large enough to
///     "just include everything" spends the budget the answer itself needs.
///     </para>
///     <para>
///     <b>These values are provisional.</b> They are public API defaults and they appear in
///     requirement text, so they are to be confirmed by the repository owner before the first
///     tagged release.
///     </para>
///     <para>
///     Instances are immutable after construction and are safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     An author raising the near-duplicate threshold and narrowing recall. Each parameter is
///     optional, so one control is replaced without restating the other.
///     </para>
///     <code>
///     // Stricter duplicate detection and a tighter recall budget than the defaults.
///     var options = new MemoryOptions(nearDuplicateThreshold: 0.93, recallCount: 3);
///     </code>
/// </example>
public sealed class MemoryOptions
{
    /// <summary>
    ///     The default cosine similarity at or above which a new descriptor is treated as a
    ///     near-duplicate of one already stored.
    /// </summary>
    /// <remarks>
    ///     See the type-level remarks for the measurement this value comes from.
    /// </remarks>
    public const double DefaultNearDuplicateThreshold = 0.88;

    /// <summary>
    ///     The default number of memories a recall returns.
    /// </summary>
    public const int DefaultRecallCount = 5;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MemoryOptions"/> class.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Both parameters are optional and default to the corresponding published constant, so an
    ///     author replaces one control — <c>new MemoryOptions(recallCount: 3)</c> — without
    ///     restating the other and without a builder existing solely to make that convenient.
    ///     </para>
    ///     <para>
    ///     <b>The extremes are permitted and meaningful.</b> A threshold of 1.0 admits every
    ///     descriptor that is not a numerically exact match, and a threshold of 0.0 treats the
    ///     nearest stored memory as a duplicate of anything; both are legitimate author
    ///     configurations rather than mistakes, and neither is second-guessed. A
    ///     <paramref name="recallCount"/> of zero is the expressible way to attach the family and
    ///     have recall return nothing, exactly as a zero ceiling disables an operation in
    ///     <c>ToolLimits</c>. Values outside those ranges have no meaning and are rejected.
    ///     </para>
    /// </remarks>
    /// <param name="nearDuplicateThreshold">
    ///     The cosine similarity at or above which a new descriptor is reported as a near-duplicate
    ///     rather than stored. Must be a number in the inclusive range 0.0 to 1.0.
    /// </param>
    /// <param name="recallCount">
    ///     The number of memories a recall returns. Must not be negative; zero returns none.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="nearDuplicateThreshold"/> is not a number in the inclusive
    ///     range 0.0 to 1.0, or when <paramref name="recallCount"/> is negative.
    /// </exception>
    public MemoryOptions(
        double nearDuplicateThreshold = DefaultNearDuplicateThreshold,
        int recallCount = DefaultRecallCount)
    {
        // Validate before any assignment so a rejected instance never exists even briefly. A NaN
        // threshold compares false against everything, which would silently disable duplicate
        // detection rather than announcing that it had been misconfigured.
        if (double.IsNaN(nearDuplicateThreshold)
            || nearDuplicateThreshold < 0.0
            || nearDuplicateThreshold > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nearDuplicateThreshold),
                nearDuplicateThreshold,
                "The near-duplicate threshold must be a number in the inclusive range 0.0 to 1.0.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(recallCount);

        NearDuplicateThreshold = nearDuplicateThreshold;
        RecallCount = recallCount;
    }

    /// <summary>
    ///     Gets the controls an author receives when they configure nothing.
    /// </summary>
    /// <remarks>
    ///     A single shared instance rather than a factory method, because the type is immutable and
    ///     sharing it is therefore free of risk.
    /// </remarks>
    public static MemoryOptions Default { get; } = new();

    /// <summary>
    ///     Gets the cosine similarity at or above which a new descriptor is reported as a
    ///     near-duplicate rather than stored.
    /// </summary>
    /// <remarks>
    ///     The comparison is inclusive: a candidate scoring exactly this value is a near-duplicate.
    /// </remarks>
    public double NearDuplicateThreshold { get; }

    /// <summary>
    ///     Gets the number of memories a recall returns.
    /// </summary>
    /// <remarks>
    ///     A ceiling rather than a quota: a recall over a store holding fewer memories returns
    ///     however many it holds.
    /// </remarks>
    public int RecallCount { get; }
}
