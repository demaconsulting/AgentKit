namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     Where a context usage figure came from, which decides how much to trust it.
/// </summary>
public enum ContextUsageOrigin
{
    /// <summary>
    ///     The provider reported the figure itself.
    /// </summary>
    Provider,

    /// <summary>
    ///     The figure was estimated from the engine's own transcript because the provider reports
    ///     nothing.
    /// </summary>
    Estimated,
}

/// <summary>
///     How much of a context window a session is currently occupying, and how that was determined.
/// </summary>
/// <remarks>
///     <para>
///     <b>This type exists because providers disagree about what they will tell us.</b> A GitHub
///     Copilot session reports its current token count and its token limit after every turn. A
///     provider reached through an <c>IChatClient</c> reports neither, and the window size has to
///     be configured by the application instead. The compaction engine cannot have two behaviors,
///     so both shapes are reduced to this one figure, with <see cref="Origin"/> recording which
///     road it came down.
///     </para>
///     <para>
///     <b>The origin is not decoration.</b> A provider-reported figure counts what the provider
///     actually charged, including framing this library never sees. An estimated figure is derived
///     from a character ratio and is only as good as that ratio. Surfacing the difference lets an
///     application log it, and lets a test assert that a provider's own numbers were preferred when
///     they were available.
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
    /// <param name="origin">
    ///     Whether the figures were reported by the provider or estimated. Must be a defined
    ///     <see cref="ContextUsageOrigin"/> member.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="usedTokens"/> is negative, <paramref name="windowTokens"/> is not
    ///     positive, <paramref name="conversationTokens"/> is negative or exceeds
    ///     <paramref name="usedTokens"/>, or <paramref name="origin"/> is not a defined
    ///     <see cref="ContextUsageOrigin"/> member.
    /// </exception>
    public ContextUsage(
        int usedTokens,
        int windowTokens,
        int conversationTokens,
        ContextUsageOrigin origin)
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

        // An undefined origin is a defect in the caller, not a measurement. It would be accepted
        // and then read as "not Provider" by everything that asks - the session's rotation
        // threshold would take the configured-window path, and the reported-window bound check
        // would be skipped entirely - so a cast integer would silently select a materially
        // different code path. Refused here, following TranscriptEntry's precedent.
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(
                nameof(origin),
                origin,
                "The usage origin must be a defined ContextUsageOrigin member.");
        }

        UsedTokens = usedTokens;
        WindowTokens = windowTokens;
        ConversationTokens = conversationTokens;
        Origin = origin;
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
    ///     else with, and a figure this library estimated was estimated throughout.
    /// </remarks>
    public int ConversationTokens { get; }

    /// <summary>
    ///     Gets whether the figures were reported by the provider or estimated by this library.
    /// </summary>
    public ContextUsageOrigin Origin { get; }

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
    ///     Gets the tokens still available in the window, never negative.
    /// </summary>
    /// <remarks>
    ///     Floored at zero because a window that is over-full has no negative amount of room; the
    ///     over-full condition is visible in <see cref="UsedTokens"/> against
    ///     <see cref="WindowTokens"/> for a caller that needs it.
    /// </remarks>
    public int FreeTokens => Math.Max(0, WindowTokens - UsedTokens);

    /// <summary>
    ///     Gets the fraction of the window currently occupied.
    /// </summary>
    /// <remarks>
    ///     May exceed one when a provider reports usage beyond its own limit. Not clamped, for the
    ///     same reason <see cref="UsedTokens"/> is not.
    /// </remarks>
    public double UsedFraction => (double)UsedTokens / WindowTokens;

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
    ///     <para>
    ///     <b>Read the previous paragraph as being about the rotation trigger alone.</b> Rotating
    ///     earlier is the safe direction for the trigger and is not the safe direction everywhere: a
    ///     session also has to decide whether it can converge in the window at all, and crediting no
    ///     overhead makes that window look larger than it is. <see cref="CompactingAgentSession"/>
    ///     therefore measures what an unsplit figure folds in — against the empty conversation its
    ///     first provider session starts from, where the fold is exactly visible — rather than
    ///     taking this default at face value. An adapter reporting totals alone should prefer to
    ///     report them from the moment the session exists, so that measurement can be taken
    ///     exactly; one that begins reporting later has it taken at its first report instead,
    ///     approximately and in the safe direction.
    ///     </para>
    /// </remarks>
    /// <param name="usedTokens">The tokens the provider says are occupied. Must not be negative.</param>
    /// <param name="windowTokens">The limit the provider reports. Must be positive.</param>
    /// <param name="conversationTokens">
    ///     The part of <paramref name="usedTokens"/> the provider attributes to the conversation, or
    ///     <see langword="null"/> when the provider reports no split. Must not be negative and must
    ///     not exceed <paramref name="usedTokens"/>.
    /// </param>
    /// <returns>A usage figure marked as provider-reported.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="usedTokens"/> is negative, <paramref name="windowTokens"/> is not
    ///     positive, or <paramref name="conversationTokens"/> is negative or exceeds
    ///     <paramref name="usedTokens"/>.
    /// </exception>
    public static ContextUsage FromProvider(int usedTokens, int windowTokens, int? conversationTokens = null) =>
        new(usedTokens, windowTokens, conversationTokens ?? usedTokens, ContextUsageOrigin.Provider);

    /// <summary>
    ///     Creates a usage figure this library derived from its own transcript.
    /// </summary>
    /// <param name="usedTokens">The estimated tokens occupied. Must not be negative.</param>
    /// <param name="windowTokens">The window size the application configured. Must be positive.</param>
    /// <param name="conversationTokens">
    ///     The part of <paramref name="usedTokens"/> the conversation occupies, or
    ///     <see langword="null"/> to treat the whole of it as conversation. Must not be negative and
    ///     must not exceed <paramref name="usedTokens"/>.
    /// </param>
    /// <returns>A usage figure marked as estimated.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="usedTokens"/> is negative, <paramref name="windowTokens"/> is not
    ///     positive, or <paramref name="conversationTokens"/> is negative or exceeds
    ///     <paramref name="usedTokens"/>.
    /// </exception>
    public static ContextUsage FromEstimate(int usedTokens, int windowTokens, int? conversationTokens = null) =>
        new(usedTokens, windowTokens, conversationTokens ?? usedTokens, ContextUsageOrigin.Estimated);
}

/// <summary>
///     Implemented by a provider session that can report its own context usage.
/// </summary>
/// <remarks>
///     <para>
///     <b>Deliberately separate from <see cref="IProviderSession"/>, and deliberately optional.</b>
///     Requiring every provider session to report usage would force an adapter for a provider that
///     reports nothing to invent a number, and an invented number is indistinguishable from a real
///     one at the point it is consumed. Keeping the capability in its own interface lets an adapter
///     say "I do not know" by simply not implementing it, and lets the session engine ask once —
///     with a type test — and fall back to its own estimate when the answer is no.
///     </para>
///     <para>
///     A provider that <em>sometimes</em> knows implements this and returns <see langword="null"/>
///     from <see cref="CurrentUsage"/> until it does.
///     </para>
/// </remarks>
public interface IContextUsageReporter
{
    /// <summary>
    ///     Gets the provider's own account of how much of its window is occupied, or
    ///     <see langword="null"/> when it cannot say.
    /// </summary>
    /// <remarks>
    ///     Read after every turn. An implementation must not contact the provider to answer: this
    ///     reports what the last exchange already revealed, so that reading it is free and cannot
    ///     fail.
    ///     <para>
    ///     An implementation whose provider distinguishes
    ///     the conversation from the system prompt and the tool declarations should pass that split
    ///     to <see cref="ContextUsage.FromProvider"/> rather than leave it to be inferred, because
    ///     it is the only figure in the provider's own tokens this library could otherwise only
    ///     guess at. An implementation that cannot should prefer to report from the moment the
    ///     session exists rather than only once it has answered something: the empty conversation a
    ///     session starts from is where <see cref="CompactingAgentSession"/> measures what an
    ///     unsplit figure folds in exactly, and an implementation that says nothing until after a
    ///     turn has that measured approximately instead.
    ///     </para>
    ///     <para>
    ///     <b>Reporting late costs something, and the cost is now bounded rather than
    ///     unbounded.</b> The fold is measured once per provider session, at the first instant that
    ///     provider session produces a figure of its own. For an implementation reporting from
    ///     creation that instant is creation, where the conversation is exactly what the session
    ///     was seeded with and the measurement is exact. For an implementation that returns
    ///     <see langword="null"/> at creation it is the first turn the implementation does report
    ///     on, and the measurement is taken against this library's own estimate of the conversation
    ///     by then — so a turn's worth of estimating error sits inside it. That error is credited
    ///     as fold, which only ever raises the bound the rotation threshold must exceed, so it
    ///     refuses a marginal window rather than accepting one that cannot settle.
    ///     </para>
    ///     <para>
    ///     What it is no longer is unmeasured. An implementation silent at creation used to be
    ///     credited a fold of zero for that whole provider session, so a provider charging real
    ///     overhead it never breaks out was treated as charging none:
    ///     <see cref="CompactingAgentSession"/> could accept a window it cannot in fact converge in
    ///     and rotate on every turn without raising a saturation signal. Reporting from creation,
    ///     or reporting the split, is still what an implementation should do — it is the difference
    ///     between an exact measurement and an approximate one — but neither is required for the
    ///     fold to be measured at all.
    ///     </para>
    /// </remarks>
    ContextUsage? CurrentUsage { get; }
}
