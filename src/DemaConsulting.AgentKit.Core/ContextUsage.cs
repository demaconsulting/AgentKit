namespace DemaConsulting.AgentKit.Core;
/// <summary>
///     How much of a context window a session is currently occupying.
/// </summary>
/// <remarks>
///     <para>
///     <b>The provider session answers for its own window.</b> Every provider this library reaches
///     publishes what it has counted and what it will hold, so both figures arrive together from one
///     place. Reducing them to one shape is what lets the compaction engine ask a single question -
///     how full, out of how much - and believe the answer without checking where it came from.
///     </para>
///     <para>
///     <b>The conversation is carried, not derived by the consumer.</b> Every rotation decision is
///     made against the conversation alone — the window less what the system prompt and the tool
///     declarations occupy — so that figure is part of this shape rather than something a caller
///     works out afterwards. It has to be, because the only honest way to work it out is to ask
///     whoever produced the totals: a provider that reports a conversation count has counted it
///     with the same tokenizer it counted everything else with, and subtracting this library's
///     character-ratio estimate from a provider's measurement instead would produce a figure in
///     neither currency. <see cref="OverheadTokens"/> is therefore the derived value, and it is
///     derived in whatever currency the figures arrived in.
///     </para>
///     <para>
///     Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
public sealed class ContextUsage
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ContextUsage"/> class.
    /// </summary>
    /// <remarks>
    ///     Validation happens before any assignment so an unusable figure never exists even
    ///     briefly. Usage is permitted to exceed the window: a provider may report that, and
    ///     clamping it would hide exactly the condition an application most needs to see.
    /// </remarks>
    /// <param name="usedTokens">The tokens currently occupied. Must not be negative.</param>
    /// <param name="windowTokens">The size of the window they are occupied from. Must be positive.</param>
    /// <param name="conversationTokens">
    ///     The part of <paramref name="usedTokens"/> the conversation occupies. Must not be negative
    ///     and must not exceed <paramref name="usedTokens"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="usedTokens"/> is negative, <paramref name="windowTokens"/> is not
    ///     positive, or <paramref name="conversationTokens"/> is negative or exceeds
    ///     <paramref name="usedTokens"/>.
    /// </exception>
    public ContextUsage(int usedTokens, int windowTokens, int conversationTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(usedTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(conversationTokens);

        // A conversation larger than everything occupied is not a condition any accounting can
        // produce, unlike usage beyond the window, which a provider genuinely reports. It would
        // make OverheadTokens negative, which would then enlarge the effective window the rotation
        // threshold is taken from - the session would rotate later than the provider's own window
        // allows, which is the one failure this package exists to prevent. Refused here rather than
        // clamped, so an adapter's arithmetic defect surfaces where the adapter wrote it.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(conversationTokens, usedTokens);

        UsedTokens = usedTokens;
        WindowTokens = windowTokens;
        ConversationTokens = conversationTokens;
    }

    /// <summary>
    ///     Gets the tokens currently occupied in the window.
    /// </summary>
    /// <remarks>
    ///     Counts everything the provider holds: the system prompt, the tool declarations, and the
    ///     conversation. Compare <see cref="ConversationTokens"/> — not this figure — against a
    ///     rotation threshold expressed as a fraction of the effective window.
    /// </remarks>
    public int UsedTokens { get; }

    /// <summary>
    ///     Gets the size of the context window the tokens are occupied from.
    /// </summary>
    public int WindowTokens { get; }

    /// <summary>
    ///     Gets the part of <see cref="UsedTokens"/> the conversation occupies.
    /// </summary>
    /// <remarks>
    ///     Never negative and never greater than <see cref="UsedTokens"/>. This is the figure a
    ///     rotation threshold is compared against, because the threshold is a fraction of the window
    ///     left once the fixed overhead is paid for. It is carried rather than computed by the
    ///     consumer so that it is always in the same currency as the totals beside it: a provider
    ///     that reports a conversation count counted it with the tokenizer it counted everything
    ///     else with, and the totals beside it came from the same reading.
    /// </remarks>
    public int ConversationTokens { get; }

    /// <summary>
    ///     Gets the part of <see cref="UsedTokens"/> the conversation does not account for: the
    ///     system prompt and the tool declarations, present on every turn and never consolidated.
    /// </summary>
    /// <remarks>
    ///     Never negative, because the conversation is refused if it exceeds the total. Subtract
    ///     this from <see cref="WindowTokens"/> to obtain the window left for conversation, which is
    ///     the window a rotation threshold is a fraction of. Both terms of that subtraction come
    ///     from this one figure, so it is performed in one currency whichever road the figure came
    ///     down.
    /// </remarks>
    public int OverheadTokens => UsedTokens - ConversationTokens;

    /// <summary>
    ///     Creates a usage figure a provider reported itself.
    /// </summary>
    /// <remarks>
    ///     <b>An adapter that knows the split should say so.</b> A provider reporting a conversation
    ///     count alongside its totals — GitHub Copilot reports both, and the system and tool
    ///     declaration counts that make up the difference — passes it, and every threshold
    ///     comparison downstream is then made entirely in that provider's own tokens. An adapter
    ///     that receives only totals omits it, and the whole of the usage is treated as
    ///     conversation. That is not an invented number: it credits the session with no overhead
    ///     allowance at all, which rotates strictly earlier than a correct split would and so cannot
    ///     let the session run past the provider's own compactor. Estimating the split here instead
    ///     would mix this library's character ratio into a provider's measurement, which is the one
    ///     thing this shape exists to prevent.
    /// </remarks>
    /// <param name="usedTokens">The tokens the provider says are occupied. Must not be negative.</param>
    /// <param name="windowTokens">The limit the provider reports. Must be positive.</param>
    /// <param name="conversationTokens">
    ///     The part of <paramref name="usedTokens"/> the provider attributes to the conversation, or
    ///     <see langword="null"/> when the provider reports no split. Must not be negative and must
    ///     not exceed <paramref name="usedTokens"/>.
    /// </param>
    /// <returns>A usage figure built from the provider's own counts.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="usedTokens"/> is negative, <paramref name="windowTokens"/> is not
    ///     positive, or <paramref name="conversationTokens"/> is negative or exceeds
    ///     <paramref name="usedTokens"/>.
    /// </exception>
    public static ContextUsage FromProvider(int usedTokens, int windowTokens, int? conversationTokens = null) =>
        new(usedTokens, windowTokens, conversationTokens ?? usedTokens);

}
