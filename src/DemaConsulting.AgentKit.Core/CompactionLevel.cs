namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     How aggressively a session is compacting, reported on every turn so an application can see
///     how much fidelity the context is being held at.
/// </summary>
/// <remarks>
///     <para>
///     <b>This is session state, not a setting.</b> It adapts: a session that keeps filling its
///     window shortly after a rotation escalates, and one that runs for a long stretch without
///     filling relaxes. An initial value would be a guess about something the session overwrites
///     within a few turns, so there is nothing to configure here.
///     </para>
///     <para>
///     <b>It is the fidelity signal.</b> A higher level means fewer recent turns are kept word for
///     word and the summarizer is told to be terser, which is how a session under pressure buys
///     room. Read alongside <see cref="AgentSessionResponse.MaterialDropped"/>: the level says how
///     hard the session is compacting, and dropped material says that compacting bought nothing and
///     history had to be discarded outright.
///     </para>
/// </remarks>
public enum CompactionLevel
{
    /// <summary>
    ///     The relaxed default: the full verbatim tail is kept and the summarizer is asked to
    ///     consolidate concisely.
    /// </summary>
    Low,

    /// <summary>
    ///     Half the verbatim tail is kept and the summarizer is asked to record only decisions,
    ///     facts and open threads.
    /// </summary>
    Medium,

    /// <summary>
    ///     A quarter of the verbatim tail is kept and the summarizer is asked for one or two lines
    ///     of essential facts only.
    /// </summary>
    High,
}
