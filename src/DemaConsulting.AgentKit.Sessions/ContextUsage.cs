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
    /// <param name="origin">
    ///     Whether the figures were reported by the provider or estimated. Must be a defined
    ///     <see cref="ContextUsageOrigin"/> member.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="usedTokens"/> is negative, <paramref name="windowTokens"/> is not
    ///     positive, or <paramref name="origin"/> is not a defined
    ///     <see cref="ContextUsageOrigin"/> member.
    /// </exception>
    public ContextUsage(int usedTokens, int windowTokens, ContextUsageOrigin origin)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(usedTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowTokens);

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
        Origin = origin;
    }

    /// <summary>
    ///     Gets the tokens currently occupied in the window.
    /// </summary>
    /// <remarks>
    ///     Counts everything the provider holds: the system prompt, the tool declarations, and the
    ///     conversation. Subtract the fixed overhead before comparing against a rotation threshold
    ///     expressed as a fraction of the effective window.
    /// </remarks>
    public int UsedTokens { get; }

    /// <summary>
    ///     Gets the size of the context window the tokens are occupied from.
    /// </summary>
    public int WindowTokens { get; }

    /// <summary>
    ///     Gets whether the figures were reported by the provider or estimated by this library.
    /// </summary>
    public ContextUsageOrigin Origin { get; }

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
    /// <param name="usedTokens">The tokens the provider says are occupied. Must not be negative.</param>
    /// <param name="windowTokens">The limit the provider reports. Must be positive.</param>
    /// <returns>A usage figure marked as provider-reported.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="usedTokens"/> is negative, or <paramref name="windowTokens"/> is not positive.
    /// </exception>
    public static ContextUsage FromProvider(int usedTokens, int windowTokens) =>
        new(usedTokens, windowTokens, ContextUsageOrigin.Provider);

    /// <summary>
    ///     Creates a usage figure this library derived from its own transcript.
    /// </summary>
    /// <param name="usedTokens">The estimated tokens occupied. Must not be negative.</param>
    /// <param name="windowTokens">The window size the application configured. Must be positive.</param>
    /// <returns>A usage figure marked as estimated.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="usedTokens"/> is negative, or <paramref name="windowTokens"/> is not positive.
    /// </exception>
    public static ContextUsage FromEstimate(int usedTokens, int windowTokens) =>
        new(usedTokens, windowTokens, ContextUsageOrigin.Estimated);
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
    /// </remarks>
    ContextUsage? CurrentUsage { get; }
}
