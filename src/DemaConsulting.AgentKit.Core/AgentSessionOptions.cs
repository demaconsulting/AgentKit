using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Everything an application configures about one session: what the agent is told, what it may
///     call, how big the provider's window is, how much recent history to keep verbatim, and who
///     does the compacting.
/// </summary>
/// <remarks>
///     <para>
///     <b>What an application configures, and nothing more.</b> The summarizer that performs each
///     consolidation, the instructions and tools every session is seeded with, and how much recent
///     history to keep word for word. The provider's context window is deliberately absent: it is a
///     fact about the provider, so the provider session answers for it.
///     </para>
///     <para>
///     <b>Nothing here predicts whether the arrangement will settle.</b> The round-robin structure
///     writes terser records under pressure and bins its oldest card when that is not enough, and
///     reports how hard it is working - rather than refusing a configuration in advance on a
///     calculation it has no way to make.
///     </para>
///     <para>
///     Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Configuring a session with a small tool set and a shorter verbatim tail.
///     </para>
///     <code>
///     public AgentSessionOptions Configure(ISummarizer summarizer, IReadOnlyList&lt;AIFunction&gt; tools)
///     {
///         return new AgentSessionOptions(
///             summarizer,
///             instructions: "You are a research assistant confined to the permitted locations.",
///             tools: tools,
///             verbatimTurns: 12);
///     }
///     </code>
/// </example>
public sealed class AgentSessionOptions
{
    /// <summary>
    ///     The most recent turns kept word for word when an application configures no number.
    /// </summary>
    /// <remarks>
    ///     Twenty turns is enough that an agent reliably remembers the exchange it is in the middle
    ///     of, and short enough that the summarized tiers carry the weight of a long conversation
    ///     rather than the tail carrying it.
    /// </remarks>
    public const int DefaultVerbatimTurns = 20;
    /// <summary>
    ///     Initializes a new instance of the <see cref="AgentSessionOptions"/> class.
    /// </summary>
    /// <remarks>
    ///     Only the summarizer is required, because compaction cannot happen without one and
    ///     defaulting it would give an application a session that silently never compacts.
    ///     Everything else defaults.
    ///     <para>
    ///     The provider's context window is deliberately not configured here. It is a fact about the
    ///     provider, so it belongs where the provider is constructed: an adapter reads it from a
    ///     native API where one exists, or is told it once, and answers for it thereafter. Carrying
    ///     a second copy in these options invited the two to disagree, and the session then had to
    ///     decide which to believe.
    ///     </para>
    /// </remarks>
    /// <param name="summarizer">
    ///     The out-of-session summarizer performing each consolidation. Must not be
    ///     <see langword="null"/>.
    /// </param>
    /// <param name="instructions">The system instructions, or <see langword="null"/> for none.</param>
    /// <param name="tools">
    ///     The tools the agent may call. <see langword="null"/> means none. Must contain no
    ///     <see langword="null"/> entry.
    /// </param>
    /// <param name="verbatimTurns">
    ///     The most recent turns to keep word for word. Must be positive.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="summarizer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> contains a <see langword="null"/> entry, or the tool declarations
    ///     are too large for a token count.
    /// </exception>
    public AgentSessionOptions(
        ISummarizer summarizer,
        string? instructions = null,
        IReadOnlyList<AIFunction>? tools = null,
        int verbatimTurns = DefaultVerbatimTurns)
    {
        ArgumentNullException.ThrowIfNull(summarizer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(verbatimTurns);

        var toolList = tools ?? [];

        if (toolList.Any(tool => tool is null))
        {
            throw new ArgumentException("A tool declaration must not be null.", nameof(tools));
        }

        Summarizer = summarizer;
        Instructions = instructions;

        // Copy the caller's tools into storage these options own, exposed only as a read-only view,
        // so a caller cannot change what every future rotation seeds after the fact.
        Tools = Array.AsReadOnly<AIFunction>([.. toolList]);
        VerbatimTurns = verbatimTurns;
    }

    /// <summary>
    ///     Computes the conversation size at which a session rotates, for one effective window.
    /// </summary>
    /// <remarks>
    ///     Shared by these options and by <see cref="CompactingAgentSession"/>, which derives the
    ///     same figure from a provider-reported window. Published as one function so the two can
    ///     never drift. Never below one token, so a rotation fraction too small to survive
    ///     truncation still means "rotate on every turn" rather than "rotate a conversation holding
    ///     nothing".
    /// </remarks>
    /// <param name="effectiveWindowTokens">The window left once the fixed overhead is paid for.</param>
    /// <returns>The conversation tokens at which the session rotates, never below one.</returns>
    internal static int RotationThresholdFor(int effectiveWindowTokens) =>
        Math.Max(1, (int)(effectiveWindowTokens * ContextLayout.RotationThreshold));

    /// <summary>
    ///     Gets the out-of-session summarizer performing each consolidation.
    /// </summary>
    public ISummarizer Summarizer { get; }

    /// <summary>
    ///     Gets the system instructions, or <see langword="null"/> when there are none.
    /// </summary>
    public string? Instructions { get; }

    /// <summary>
    ///     Gets the tools the agent may call.
    /// </summary>
    /// <remarks>
    ///     Carried across every rotation unchanged: rotation replaces history, never capability.
    /// </remarks>
    public IReadOnlyList<AIFunction> Tools { get; }

    /// <summary>
    ///     Gets the most recent turns kept word for word.
    /// </summary>
    /// <remarks>
    ///     <b>A maximum, not a quota.</b> The tail holds at most this many turns, and holds fewer
    ///     when the context is under pressure: the session keeps a shorter tail as it compacts more
    ///     aggressively, a rotation always ages out at least one turn however short the tail already
    ///     is, and where consolidation is failing outright the oldest turn is discarded to stop the
    ///     context growing. Asking for a number of turns is a ceiling the session respects, never a
    ///     floor it guarantees.
    /// </remarks>
    public int VerbatimTurns { get; }
}
