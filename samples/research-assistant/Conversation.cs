using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Memory;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Samples.ResearchAssistant;

/// <summary>
///     What an application needs in order to start an AgentKit compacting session: the options it
///     configured, the factory that produces provider sessions, and the window the whole
///     arrangement is accounted against.
/// </summary>
/// <param name="Options">
///     The summarizer, instructions, tools and verbatim tail this session is configured with.
/// </param>
/// <param name="ProviderSessions">
///     Produces the first provider session and every replacement a rotation needs.
/// </param>
/// <param name="Window">
///     The context window the factory was told and where that figure came from, or
///     <see langword="null"/> when the provider reports its own window and was told nothing. Ollama
///     publishes no window through <c>IChatClient</c>, so the application must answer for it;
///     Copilot reports both its occupancy and its limit with every turn, so there is nothing to
///     state and nothing that could disagree.
/// </param>
public sealed record CompactingSessionPlan(
    AgentSessionOptions Options,
    IProviderSessionFactory ProviderSessions,
    ContextWindow? Window);

/// <summary>
///     Everything needed to start one conversation, in whichever shape the chosen provider
///     supports.
/// </summary>
/// <remarks>
///     <para>
///     <b>Both providers this sample supports carry an AgentKit compacting session</b>, so a
///     conversation on either outlives the model's context window. What differs is where the window
///     comes from: Ollama publishes none through <c>IChatClient</c>, so the application reads it and
///     states it; the GitHub Copilot runtime reports its occupancy and its limit with every turn, so
///     nothing is stated and nothing could disagree.
///     </para>
///     <para>
///     A plan carrying no compacting session is still representable — a provider AgentKit ships no
///     provider session for would produce one — and is named plainly in the banner rather than
///     hidden behind one loop, because a conversation that silently stops compacting is exactly the
///     failure the session engine exists to prevent.
///     </para>
/// </remarks>
/// <param name="Agent">
///     The built agent. On either provider it is what a delegated child is started from.
/// </param>
/// <param name="Compacting">
///     The compacting session to run this conversation on, or <see langword="null"/> when the
///     provider has no AgentKit provider session and the agent's own session is used instead.
/// </param>
public sealed record ConversationPlan(AIAgent Agent, CompactingSessionPlan? Compacting)
{
    /// <summary>
    ///     Describes which conversation shape this run will use, for the startup banner.
    /// </summary>
    /// <returns>A sentence naming the shape and, for a compacting session, its settings.</returns>
    public string Describe() => Compacting is null
        ? "provider-managed (the runtime holds the conversation; AgentKit ships no provider "
          + "session for it, so nothing compacts when its window fills)"
        : $"AgentKit compacting session — window {DescribeWindow(Compacting.Window)}, "
          + $"{Compacting.Options.VerbatimTurns} recent turns kept verbatim";

    /// <summary>
    ///     Describes where the window this conversation is accounted against came from.
    /// </summary>
    /// <remarks>
    ///     A provider that answers for its own window is said to, rather than having a plausible
    ///     number invented for the banner. The window is the one figure the whole arrangement turns
    ///     on, and printing an invented one would be worse than printing none.
    /// </remarks>
    /// <param name="window">The stated window, or <see langword="null"/> when the provider reports its own.</param>
    /// <returns>One clause naming the window and its provenance.</returns>
    private static string DescribeWindow(ContextWindow? window) =>
        window?.Describe() ?? "reported by the provider with every turn, so none was stated";
}

/// <summary>
///     An agent paired with the provider runtime's own session, for a conversation AgentKit does
///     not carry.
/// </summary>
/// <param name="Agent">The agent running each turn.</param>
/// <param name="Session">The runtime's session, holding the conversation across turns.</param>
internal sealed record AgentTurns(AIAgent Agent, AgentSession Session);

/// <summary>
///     One conversation with the research assistant, carried on whichever session shape the
///     provider supports.
/// </summary>
/// <remarks>
///     <para>
///     <b>Written so the turn loop above it does not branch on provider.</b> The loop asks a
///     question and gets an answer; whether that answer came from a compacting session that may
///     have just consolidated half its history, or from the provider runtime's own session, is
///     settled once here.
///     </para>
///     <para>
///     Tool activity is printed on both shapes, from different places, because they reveal it
///     differently: an agent streams it, and a compacting session does not report it at all. On the
///     Ollama path a <see cref="ToolCallReportingChatClient"/> sits beneath the session for exactly
///     that reason. On the Copilot path there is no such seam — the runtime carries the tool loop
///     itself — so a compacting Copilot run prints answers rather than a tool trace.
///     </para>
///     <para>
///     Instances are not safe for concurrent use: one conversation is one thread of turns.
///     </para>
/// </remarks>
public sealed class Conversation : IAsyncDisposable
{
    /// <summary>
    ///     The agent and the provider runtime's session, when the runtime carries the conversation.
    /// </summary>
    private readonly AgentTurns? _agentTurns;

    /// <summary>
    ///     The compacting session carrying this conversation, when AgentKit owns its lifecycle.
    /// </summary>
    private readonly CompactingAgentSession? _session;

    /// <summary>
    ///     The machine-readable tool-call record, or <see langword="null"/> when none was asked for.
    /// </summary>
    private readonly TextWriter? _transcript;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Conversation"/> class.
    /// </summary>
    /// <remarks>
    ///     Private because starting either shape is asynchronous; <see cref="StartAsync"/> is the
    ///     entry point.
    /// </remarks>
    /// <param name="agentTurns">The agent and runtime session, when the runtime carries the conversation.</param>
    /// <param name="session">The compacting session, when AgentKit carries the conversation.</param>
    /// <param name="transcript">The tool-call record, or <see langword="null"/>.</param>
    private Conversation(
        AgentTurns? agentTurns,
        CompactingAgentSession? session,
        TextWriter? transcript)
    {
        _agentTurns = agentTurns;
        _session = session;
        _transcript = transcript;
    }

    /// <summary>
    ///     Gets the compacting session carrying this conversation, or <see langword="null"/> when
    ///     the provider's own session carries it.
    /// </summary>
    /// <remarks>
    ///     Exposed so the turn loop can report the running totals a single turn does not carry —
    ///     how many times the session has rotated, and how many consolidations that cost.
    /// </remarks>
    public CompactingAgentSession? Session => _session;

    /// <summary>
    ///     Starts a conversation from a plan.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The compacting path is three lines an application author writes once: state the
    ///     summarizer, the instructions and the tools in <c>AgentSessionOptions</c>, hand those and
    ///     a provider-session factory to <c>CompactingAgentSession.CreateAsync</c>, and send turns.
    ///     Everything the rotation does afterwards — consolidating older history, seeding a
    ///     replacement provider session, disposing the one it replaced — happens without the
    ///     application asking for it.
    ///     </para>
    /// </remarks>
    /// <param name="plan">The conversation shape and its settings. Must not be <see langword="null"/>.</param>
    /// <param name="transcript">The tool-call record, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">Cancels a slow session start.</param>
    /// <returns>A conversation ready to take its first turn.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is <see langword="null"/>.</exception>
    public static async Task<Conversation> StartAsync(
        ConversationPlan plan,
        TextWriter? transcript,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Compacting is { } compacting)
        {
            var session = await CompactingAgentSession.CreateAsync(
                compacting.Options,
                compacting.ProviderSessions,
                cancellationToken);

            return new Conversation(agentTurns: null, session, transcript);
        }

        var agentSession = await plan.Agent.CreateSessionAsync(cancellationToken);
        return new Conversation(new AgentTurns(plan.Agent, agentSession), session: null, transcript);
    }

    /// <summary>
    ///     Runs one turn, printing the answer as it becomes available.
    /// </summary>
    /// <remarks>
    ///     The answer is written to the console by this method on both paths, because only the
    ///     agent path can write it incrementally and a caller should not have to know which path it
    ///     is on to get an answer printed the best way that path allows.
    /// </remarks>
    /// <param name="message">The user's message for this turn. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the turn.</param>
    /// <returns>
    ///     What the compacting session reported about this turn, or <see langword="null"/> when the
    ///     provider's own session carried it and there is nothing to report.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<AgentSessionResponse?> AskAsync(string message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_session is not null)
        {
            // The whole turn is one call. On the Ollama path the tool activity was already printed
            // by the reporting client beneath the session, which is the only place it is visible;
            // on the Copilot path the runtime carries the tool loop and there is no such seam.
            var response = await _session.SendAsync(message, cancellationToken);
            Console.Write(response.Text);
            return response;
        }

        if (_agentTurns is { } turns)
        {
            await RunStreamingTurnAsync(turns, message, cancellationToken);
        }

        return null;
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     Disposing a compacting session releases the provider session it currently holds, which
    ///     for a provider billing server-side state is not optional. The provider runtime's own
    ///     session is owned by the runtime and is released with it.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }

    /// <summary>
    ///     Runs one turn on the provider runtime's own session, streaming the answer and printing
    ///     every tool call and result as it happens.
    /// </summary>
    /// <param name="turns">The agent and the runtime session carrying this conversation.</param>
    /// <param name="message">The user's message for this turn.</param>
    /// <param name="cancellationToken">Cancels the turn.</param>
    /// <returns>A task that completes when the turn's stream is exhausted.</returns>
    private async Task RunStreamingTurnAsync(
        AgentTurns turns,
        string message,
        CancellationToken cancellationToken)
    {
        // Parallel tool calls arrive as a block of calls followed by a block of results in
        // completion order, so a result printed on its own says nothing about which call produced
        // it. Remembering each call's identifier lets the result carry the tool name and the
        // arguments back, which is the difference between a reader following the run and a reader
        // watching eleven interchangeable lines go past.
        var callsInFlight = new Dictionary<string, FunctionCallContent>(StringComparer.Ordinal);

        await foreach (var update in turns.Agent.RunStreamingAsync(
                           message,
                           turns.Session,
                           cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                Console.Write(update.Text);
            }

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        callsInFlight[call.CallId] = call;
                        ToolTrace.PrintCall(call);
                        ToolTrace.Record(_transcript, call);
                        break;

                    case FunctionResultContent result:
                        ToolTrace.PrintResult(
                            result,
                            callsInFlight.GetValueOrDefault(result.CallId),
                            MemoryPack.FamilyPrefix);
                        break;

                    default:
                        break;
                }
            }
        }
    }
}
