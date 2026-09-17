using DemaConsulting.AgentKit.Core;
using GitHub.Copilot;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     A summarizer that consolidates history on a short-lived, tool-free GitHub Copilot session of
///     its own.
/// </summary>
/// <remarks>
///     <para>
///     <b>Shipped because the mechanism is this library's to guarantee.</b> Compaction cannot happen
///     without a summarizer, so a provider AgentKit carries a session for but no summarizer is a
///     provider on which compaction quietly never happens. This is that part, using the
///     consolidation prompt Core publishes and the aggressiveness levels the session drives it with.
///     An application remains free to write its own — the contract is one method — but it should not
///     have to.
///     </para>
///     <para>
///     <b>It runs out of the session it is compacting, which is the whole reason the contract takes
///     a summarizer at all.</b> A consolidation sent through the live conversation would spend the
///     very context it exists to reclaim, and would itself count toward the occupancy that triggered
///     the rotation. So each call creates a session of its own, uses it once, and releases it — on
///     the failure path as well as the successful one.
///     </para>
///     <para>
///     <b>The session it creates carries no tools at all.</b> A consolidation is a pure function
///     from material to a record of it; there is nothing for a tool to do, and a tool that could
///     act would be acting outside everything the conversation's own confinement was reasoned
///     about. An empty tool list derives an empty allow-list and a permission handler that approves
///     nothing, which is the strongest confinement this package can express.
///     </para>
///     <para>
///     <b>The runtime's own compaction is disabled on this session too.</b> A consolidation is one
///     prompt and one answer; there is nothing to compact, and a runtime that reshaped the material
///     mid-consolidation would produce a record of something other than what it was given. See
///     <c>CopilotAgentFactory.BuildEngineSessionConfig</c>.
///     </para>
///     <para>
///     <b>Ownership.</b> The <see cref="CopilotClient"/> is the host's and is disposed by the host;
///     every session this summarizer creates is its own and is released by it.
///     </para>
///     <para>
///     Safe for concurrent use: each call creates a session of its own and shares nothing but the
///     client reference and the model name.
///     </para>
/// </remarks>
public sealed class CopilotSummarizer : ISummarizer
{
    /// <summary>
    ///     Opens one short-lived runtime session per consolidation.
    /// </summary>
    private readonly CopilotChannelOpener _opener;

    /// <summary>
    ///     The Copilot model performing each consolidation, or <see langword="null"/> to leave the
    ///     choice to the runtime.
    /// </summary>
    private readonly string? _model;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotSummarizer"/> class.
    /// </summary>
    /// <param name="client">
    ///     The Copilot client each consolidation runs on. Must not be <see langword="null"/>, and
    ///     must already be started. The host owns it: this summarizer disposes it never.
    /// </param>
    /// <param name="model">
    ///     The Copilot model to perform each consolidation — for example <c>gpt-5.4-mini</c>. When
    ///     <see langword="null"/>, empty, or whitespace, the runtime applies its own default. A
    ///     smaller and cheaper model is usually the right choice, because consolidation is
    ///     summarization rather than reasoning.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    public CopilotSummarizer(CopilotClient client, string? model = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        _opener = CopilotSessionChannel.Open(client);
        _model = model;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotSummarizer"/> class over a supplied
    ///     channel opener.
    /// </summary>
    /// <remarks>
    ///     The test seam, for the reason <see cref="ICopilotTurnChannel"/> gives: the SDK's session
    ///     types are sealed and non-constructable, so a consolidation cannot be exercised without a
    ///     live runtime unless the opening of a session is injectable.
    /// </remarks>
    /// <param name="opener">Opens one short-lived runtime session per consolidation.</param>
    /// <param name="model">The model to name, or <see langword="null"/> for the runtime's default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="opener"/> is <see langword="null"/>.</exception>
    internal CopilotSummarizer(CopilotChannelOpener opener, string? model)
    {
        ArgumentNullException.ThrowIfNull(opener);

        _opener = opener;
        _model = model;
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     <para>
    ///     Sends the composed prompt as the only message of a fresh session and returns what comes
    ///     back. No history is carried and no tools are offered: a consolidation is a pure function
    ///     from the material to a record of it, which is what lets a rotation be replayed and
    ///     reasoned about.
    ///     </para>
    ///     <para>
    ///     An empty answer — including a session that went idle without answering at all — is
    ///     returned as an empty record rather than raised as an exception. The engine already treats
    ///     a consolidation it could not obtain as material to keep rather than material to lose, and
    ///     a model declining to answer is a thing that happens rather than a defect to escalate.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The channel opener returned <see langword="null"/>.</exception>
    public async Task<string> ConsolidateAsync(
        ConsolidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var config = CopilotAgentFactory.BuildEngineSessionConfig(
            [],
            instructions: null,
            _model);

        var channel = await _opener(config, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The Copilot channel opener returned null.");

        try
        {
            var answer = await channel
                .SendAndWaitAsync(ConsolidationPrompt.Compose(request), cancellationToken)
                .ConfigureAwait(false);

            return answer?.Data?.Content ?? string.Empty;
        }
        finally
        {
            // Released on every path. A consolidation that failed, timed out or was canceled has
            // still left a session on the runtime, and a long conversation performs one of these per
            // rotation - so a session leaked here would accumulate for exactly as long as the
            // conversation this is meant to prolong.
            await channel.DisposeAsync().ConfigureAwait(false);
        }
    }
}
