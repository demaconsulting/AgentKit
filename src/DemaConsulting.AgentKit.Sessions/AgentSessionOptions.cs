using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     Everything an application configures about one session: what the agent is told, what it may
///     call, how big the provider's window is, how much recent history to keep verbatim, and who
///     does the compacting.
/// </summary>
/// <remarks>
///     <para>
///     <b>The fixed overhead is measured here, once, and it is an estimate.</b> The system prompt
///     and the tool declarations are present on every turn and are never consolidated, so they are
///     subtracted from the provider's window before the rotation percentage is applied. Applying a
///     percentage to the raw window instead would make the rotation point drift with how many tools
///     an application attached. The measurement is <see cref="TokenEstimator"/>'s character ratio,
///     which is a rule of thumb: it is applied only to the window this class was configured with,
///     never to a window or a usage figure a provider reported. A provider that reports its own
///     figures reports its own overhead with them, and <see cref="CompactingAgentSession"/> uses
///     that instead.
///     </para>
///     <para>
///     <b>What is validated here is the application's own arithmetic.</b> The summarizer is
///     required, because compaction cannot happen without one; the provider window must be positive;
///     and the fixed overhead must leave some room for conversation. These are defects in the host's
///     configuration code, surfaced where the host wrote them. What is <em>not</em> asserted is any
///     prediction about whether the arrangement will settle: the round-robin structure escalates its
///     compaction level and drops material until the context fits, and reports how hard it is
///     working, rather than refusing a window in advance in a currency it cannot measure.
///     </para>
///     <para>
///     Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Configuring a session for a provider with a 32,000-token window, a small tool set, and a
///     shorter verbatim tail.
///     </para>
///     <code>
///     public AgentSessionOptions Configure(ISummarizer summarizer, IReadOnlyList&lt;AIFunction&gt; tools)
///     {
///         return new AgentSessionOptions(
///             summarizer,
///             instructions: "You are a research assistant confined to the permitted locations.",
///             tools: tools,
///             compaction: new CompactionPolicy(verbatimTurns: 12));
///     }
///     </code>
/// </example>
public sealed class AgentSessionOptions
{
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
    /// <param name="compaction">
    ///     The compaction controls. <see langword="null"/> selects <see cref="CompactionPolicy.Default"/>.
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
        CompactionPolicy? compaction = null)
    {
        ArgumentNullException.ThrowIfNull(summarizer);

        var policy = compaction ?? CompactionPolicy.Default;
        var toolList = tools ?? [];

        // Measure the fixed overhead the seed carries. Estimating the declarations also validates
        // the tool list, because a null declaration cannot be estimated.
        var systemTokens = TokenEstimator.EstimateTokens(instructions);
        var toolTokens = TokenEstimator.EstimateToolDeclarationTokens(toolList);

        Summarizer = summarizer;
        Instructions = instructions;

        // Copy the caller's tools into storage these options own, exposed only as a read-only view.
        // The declarations are measured once, just above: a caller that could add or remove a tool
        // afterwards would change what every future rotation seeds without changing the measurement.
        Tools = Array.AsReadOnly<AIFunction>([.. toolList]);
        Compaction = policy;
        SystemTokens = systemTokens;
        ToolDeclarationTokens = toolTokens;
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
    ///     Gets the compaction controls.
    /// </summary>
    public CompactionPolicy Compaction { get; }

    /// <summary>
    ///     Gets the most recent turns kept word for word, a maximum rather than a quota.
    /// </summary>
    public int VerbatimTurns => Compaction.VerbatimTurns;

    /// <summary>
    ///     Gets the tokens the system instructions occupy.
    /// </summary>
    public int SystemTokens { get; }

    /// <summary>
    ///     Gets the tokens the tool declarations occupy.
    /// </summary>
    /// <remarks>
    ///     Estimated from each tool's name, description and schema. For a realistic tool set this
    ///     runs to thousands of tokens, which is why it is subtracted before percentages rather than
    ///     absorbed into the conversation.
    /// </remarks>
    public int ToolDeclarationTokens { get; }

    /// <summary>
    ///     Gets the fixed overhead present on every turn: the system prompt plus the tool declarations.
    /// </summary>
    /// <remarks>
    ///     An estimate, published so an application can see what its instructions and tool set cost
    ///     before a session starts. The engine does not compare it against anything a provider
    ///     reports: a provider reports its own overhead alongside its own totals, and mixing the two
    ///     would produce a number in neither currency.
    /// </remarks>
    public int FixedOverheadTokens => SystemTokens + ToolDeclarationTokens;
}
