namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     The one control an application configures about compaction: how many recent turns are kept
///     word for word.
/// </summary>
/// <remarks>
///     <para>
///     <b>Compaction is configured in counts, not tokens.</b> A session's context is a round-robin
///     structure — a fixed number of recent turns held verbatim, and behind them a fixed number of
///     tiers, each a ring of a fixed number of consolidated slots. The counts that give it its
///     shape are internal constants, because an application has no basis for choosing them and no
///     symptom telling it a choice was wrong. The only number an author can reason about is how
///     much recent history to keep untouched, and that is what this type carries.
///     </para>
///     <para>
///     <b><see cref="VerbatimTurns"/> is a maximum, not a quota.</b> The tail holds at most that
///     many turns, and holds fewer when the context is under pressure: the session shortens the
///     tail as it escalates its compaction level, a rotation always ages out at least one turn
///     however short the tail already is, and where consolidation is failing outright the oldest
///     verbatim turn is discarded to stop the context growing. Asking for a number of turns is
///     therefore a ceiling the session respects, never a floor it guarantees.
///     </para>
///     <para>
///     <b>Instances are immutable after construction and safe for concurrent use.</b> An
///     application that configures nothing receives <see cref="Default"/>.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     A host that wants a shorter verbatim tail — because its turns are large, or because it would
///     rather rotate sooner and keep more consolidated history — configures the one number.
///     </para>
///     <code>
///     var policy = new CompactionPolicy(verbatimTurns: 8);
///     </code>
/// </example>
public sealed class CompactionPolicy
{
    /// <summary>
    ///     The verbatim tail length an application receives when it configures none.
    /// </summary>
    /// <remarks>
    ///     Twenty recent turns held word for word. It is a maximum rather than a quota: a session
    ///     under pressure keeps fewer. The figure is a starting point suited to an ordinary
    ///     conversational agent; a host whose turns are unusually large or small can replace it.
    /// </remarks>
    public const int DefaultVerbatimTurns = 20;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CompactionPolicy"/> class.
    /// </summary>
    /// <remarks>
    ///     The one parameter is optional and defaults to <see cref="DefaultVerbatimTurns"/>.
    ///     Validation happens before any assignment, so a rejected policy never exists even
    ///     briefly; a rejected value is a defect in the host's configuration code rather than
    ///     something a model can provoke.
    /// </remarks>
    /// <param name="verbatimTurns">
    ///     The most recent turns to keep word for word. Must be positive. Defaults to
    ///     <see cref="DefaultVerbatimTurns"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="verbatimTurns"/> is not positive.</exception>
    public CompactionPolicy(int verbatimTurns = DefaultVerbatimTurns)
    {
        // A tail of zero turns would keep no recent history verbatim and would rotate everything
        // the moment it arrived, which is not a session an application could reason about.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(verbatimTurns);

        VerbatimTurns = verbatimTurns;
    }

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
    ///     Gets the most recent turns kept word for word.
    /// </summary>
    /// <remarks>
    ///     A maximum, not a quota: the tail holds at most this many turns and holds fewer when the
    ///     context is under pressure. See the type remarks.
    /// </remarks>
    public int VerbatimTurns { get; }
}
