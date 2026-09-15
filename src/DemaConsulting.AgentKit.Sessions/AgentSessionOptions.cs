using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Sessions;

/// <summary>
///     Everything an application configures about one session: what the agent is told, what it may
///     call, how big the provider's window is, when to compact, and who does the compacting.
/// </summary>
/// <remarks>
///     <para>
///     <b>The fixed overhead is measured here, once.</b> The system prompt and the tool
///     declarations are present on every turn and are never consolidated, so they are subtracted
///     from the provider's window before the rotation percentage is applied. Applying a percentage
///     to the raw window instead would make the rotation point drift with how many tools an
///     application attached — attach more tools and the agent would get less conversation before
///     rotating, without anything in the configuration saying so.
///     </para>
///     <para>
///     <b>The bound is asserted at construction.</b> A configuration whose effective window cannot
///     hold the sum of the tier budgets — together with the framing each tier record is seeded with
///     — is refused outright, because such a session would rotate into a context that was already
///     over budget and would never converge. That check is the "bounded by construction" property
///     made real: if these options construct, the arrangement fits.
///     </para>
///     <para>
///     Instances are immutable after construction and safe for concurrent use.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Configuring a session for a provider with a 32,000-token window, a small tool set, and the
///     default four-tier compaction policy.
///     </para>
///     <code>
///     public AgentSessionOptions Configure(ISummarizer summarizer, IReadOnlyList&lt;AIFunction&gt; tools)
///     {
///         var options = new AgentSessionOptions(
///             summarizer,
///             instructions: "You are a research assistant confined to the permitted locations.",
///             tools: tools,
///             providerWindowTokens: 32_000);
///
///         // What is left for conversation once the prompt and declarations are paid for, and the
///         // point at which the session will rotate.
///         var effective = options.EffectiveWindowTokens;
///         var rotateAt = options.RotationThresholdTokens;
///         return options;
///     }
///     </code>
/// </example>
public sealed class AgentSessionOptions
{
    /// <summary>
    ///     The provider window assumed when an application configures none.
    /// </summary>
    /// <remarks>
    ///     A provider reached through an <c>IChatClient</c> reports no window size, so one has to be
    ///     assumed. 128,000 tokens is a common contemporary window and is offered as a starting
    ///     point rather than a claim about any particular model. An application that knows its
    ///     provider's window should say so: under-stating it wastes context, and over-stating it
    ///     rotates too late, which is the failure that loses work.
    /// </remarks>
    public const int DefaultProviderWindowTokens = 128_000;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AgentSessionOptions"/> class.
    /// </summary>
    /// <remarks>
    ///     Only the summarizer is required, because compaction cannot happen without one and
    ///     defaulting it would give an application a session that silently never compacts.
    ///     Everything else defaults. Validation happens before any assignment so a rejected
    ///     configuration never exists even briefly.
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
    /// <param name="providerWindowTokens">
    ///     The provider's context window in tokens. Must be positive.
    /// </param>
    /// <param name="compaction">
    ///     The compaction controls. <see langword="null"/> selects <see cref="CompactionPolicy.Default"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="summarizer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="providerWindowTokens"/> is not positive.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> contains a <see langword="null"/> entry, or the window left after
    ///     the system prompt and tool declarations cannot hold the policy's tier budgets and the
    ///     framing their seeded records carry.
    /// </exception>
    public AgentSessionOptions(
        ISummarizer summarizer,
        string? instructions = null,
        IReadOnlyList<AIFunction>? tools = null,
        int providerWindowTokens = DefaultProviderWindowTokens,
        CompactionPolicy? compaction = null)
    {
        ArgumentNullException.ThrowIfNull(summarizer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(providerWindowTokens);

        var policy = compaction ?? CompactionPolicy.Default;
        var toolList = tools ?? [];

        // Measure the fixed overhead first: everything below is arithmetic on what is left after it.
        // This also validates the tool list, because a null declaration cannot be estimated.
        var systemTokens = TokenEstimator.EstimateTokens(instructions);
        var toolTokens = TokenEstimator.EstimateToolDeclarationTokens(toolList);
        var effective = providerWindowTokens - systemTokens - toolTokens;

        // Refuse a configuration that cannot converge. Both conditions are host configuration
        // defects, surfaced where the application wrote them rather than as a session that rotates
        // forever without ever getting under its own budget.
        if (effective <= 0)
        {
            throw new ArgumentException(
                $"The system prompt ({systemTokens} tokens) and tool declarations ({toolTokens} tokens) "
                + $"leave no room in a {providerWindowTokens}-token window.",
                nameof(providerWindowTokens));
        }

        // The two figures below always sum within a token count: a policy is refused at construction
        // unless its budgets and the framing of its seeded records fit one together. This comparison
        // can therefore be made in plain token arithmetic, where once it could wrap - a policy of
        // [int.MaxValue - 1, 1] made the claimed bound negative and passed a check no positive
        // window could actually have satisfied.
        if (effective < policy.TotalTierBudgetTokens + ContextLayout.SeedFramingTokens(policy))
        {
            throw new ArgumentException(
                $"The effective window of {effective} tokens cannot hold the policy's "
                + $"{policy.TotalTierBudgetTokens} tokens of tier budgets and the "
                + $"{ContextLayout.SeedFramingTokens(policy)} tokens of framing their seeded records "
                + "carry; the session could never rotate back within its bound.",
                nameof(compaction));
        }

        Summarizer = summarizer;
        Instructions = instructions;

        // Copy the caller's tools into storage these options own, exposed only as a read-only view.
        // ToolDeclarationTokens is measured once, just above: a caller that could add or remove a
        // tool afterwards would change what every future rotation seeds without changing the
        // effective window or the threshold derived from it.
        Tools = Array.AsReadOnly<AIFunction>([.. toolList]);
        ProviderWindowTokens = providerWindowTokens;
        Compaction = policy;
        SystemTokens = systemTokens;
        ToolDeclarationTokens = toolTokens;
        EffectiveWindowTokens = effective;

        // Never below one token. A policy whose threshold is a small enough fraction of this window
        // truncates to zero, and a threshold of zero is satisfied by a conversation of no tokens at
        // all - so the session would be willing to rotate a context holding nothing, spending
        // summarizer work and a provider session on material that does not exist.
        RotationThresholdTokens = Math.Max(1, (int)(effective * policy.RotationThreshold));
    }

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
    ///     Carried across every rotation unchanged: rotation replaces history, never capability. The
    ///     list is a read-only view over a copy taken at construction, so the declarations measured
    ///     into <see cref="ToolDeclarationTokens"/> are the same ones every future seed carries.
    /// </remarks>
    public IReadOnlyList<AIFunction> Tools { get; }

    /// <summary>
    ///     Gets the provider's context window in tokens.
    /// </summary>
    public int ProviderWindowTokens { get; }

    /// <summary>
    ///     Gets the compaction controls.
    /// </summary>
    public CompactionPolicy Compaction { get; }

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
    ///     absorbed into the conversation budget.
    /// </remarks>
    public int ToolDeclarationTokens { get; }

    /// <summary>
    ///     Gets the fixed overhead present on every turn: the system prompt plus the tool declarations.
    /// </summary>
    public int FixedOverheadTokens => SystemTokens + ToolDeclarationTokens;

    /// <summary>
    ///     Gets the window left for conversation once the fixed overhead is paid for.
    /// </summary>
    /// <remarks>
    ///     Always positive: a configuration whose overhead consumed the whole window is refused at
    ///     construction.
    /// </remarks>
    public int EffectiveWindowTokens { get; }

    /// <summary>
    ///     Gets the conversation size at which the session rotates.
    /// </summary>
    /// <remarks>
    ///     The effective window multiplied by the policy's rotation threshold, truncated, and never
    ///     below one token. Compared against conversation tokens — that is, usage with the fixed
    ///     overhead subtracted — so the comparison means the same thing whether the figure came from
    ///     a provider or from an estimate. The floor of one token is what keeps a threshold too
    ///     small to survive truncation from meaning "rotate a conversation holding nothing"; it does
    ///     not make such a policy rotate rarely, because a host that asks to rotate at a fraction of
    ///     a token has asked to rotate on every turn and receives exactly that.
    ///     <para>
    ///     This is the threshold that governs a provider reporting no window of its own. A provider
    ///     that reports one is measured against <em>that</em> window instead, by the same
    ///     arithmetic: the guarantee the threshold exists to deliver — rotating before the
    ///     provider's own compactor fires — is about the window the provider actually has, so a
    ///     configured window that disagrees with a reported one does not get to decide.
    ///     </para>
    /// </remarks>
    public int RotationThresholdTokens { get; }
}
