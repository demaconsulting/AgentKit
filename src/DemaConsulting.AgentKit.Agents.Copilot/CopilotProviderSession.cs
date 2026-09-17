using DemaConsulting.AgentKit.Core;
using GitHub.Copilot;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     A provider session over one GitHub Copilot session, for a provider that holds the
///     conversation itself and reports what it holds.
/// </summary>
/// <remarks>
///     <para>
///     <b>The opposite shape to the ChatClient adapter, and almost everything follows from that.</b>
///     A stateless provider is resent the whole conversation each turn, so its adapter keeps a
///     message list and can count what it sent. Copilot keeps the conversation server-side: a turn
///     sends one prompt, and what the session holds afterwards is the runtime's business. So this
///     class holds no history at all. Seeding is done once, at creation, by
///     <see cref="CopilotProviderSessionFactory"/>; releasing is disposing the runtime's session.
///     </para>
///     <para>
///     <b>There is no window parameter, deliberately.</b> The ChatClient adapter has to be told its
///     window because an <c>IChatClient</c> publishes none. Copilot reports both figures —
///     occupancy and limit — in the same <c>session.usage_info</c> event, so asking an application
///     for a number the provider already knows would create a second source of truth for one fact,
///     which is exactly what the engine's design retired. Nothing here is supplied and nothing is
///     estimated.
///     </para>
///     <para>
///     <b>Three conditions refuse a turn rather than guess around it</b>, and all three are checked
///     before anything is recorded, so a refused turn leaves this session exactly as it was: the
///     runtime rewrote history AgentKit believes it owns; the session went idle without answering;
///     or the runtime answered without ever reporting its occupancy. Each names a fact the engine
///     cannot proceed without, and each is something an application can see and act on.
///     </para>
///     <para>
///     <b>No image-promoting decorator, for the reason <see cref="CopilotAgentFactory"/> records.</b>
///     The Copilot runtime already delivers a tool's binary results to the model — the SDK's own
///     tool-completion result carries them explicitly — so promoting an image onto a user message
///     here would duplicate content the model already received. A future maintainer should not add
///     it.
///     </para>
///     <para>
///     <b>Construction is internal.</b> A session is obtained from
///     <see cref="CopilotProviderSessionFactory"/>, which is the only thing that can build one
///     correctly: a channel opened without the observer registered as the session's event handler
///     would answer turns and report no occupancy at all.
///     </para>
///     <para>
///     Instances are not safe for concurrent use, consistent with <see cref="IProviderSession"/>.
///     </para>
/// </remarks>
public sealed class CopilotProviderSession : IProviderSession
{
    /// <summary>
    ///     The window reported before the runtime has reported one.
    /// </summary>
    /// <remarks>
    ///     <b>Deliberately not a plausible window.</b> It is a placeholder that cannot be mistaken
    ///     for a measurement, chosen because the engine reads
    ///     <see cref="IProviderSession.CurrentUsage"/> at construction — before any turn, and so
    ///     before Copilot has said anything. It cannot affect a decision: the rotation test compares
    ///     the conversation against a threshold that is never below one token, the conversation is
    ///     zero until the provider has reported, and zero is below one. By the time the test is
    ///     evaluated in earnest a real reading exists, or the turn has already been refused.
    /// </remarks>
    private const int ProvisionalWindowTokens = 1;

    /// <summary>
    ///     The runtime session this provider session owns and releases.
    /// </summary>
    private readonly ICopilotTurnChannel _channel;

    /// <summary>
    ///     Watches the runtime's event stream, holding the occupancy and the current turn's work.
    /// </summary>
    private readonly CopilotSessionObserver _observer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotProviderSession"/> class.
    /// </summary>
    /// <remarks>
    ///     Internal because only <see cref="CopilotProviderSessionFactory"/> can supply a channel
    ///     whose session was created with <paramref name="observer"/> already registered as its
    ///     event handler, and a pair that does not match is an arrangement no application can have.
    ///     The argument checks are therefore a guard on an internal contract rather than a message
    ///     to an application.
    /// </remarks>
    /// <param name="channel">The runtime session this instance takes ownership of.</param>
    /// <param name="observer">The observer registered on that session before it was created.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="channel"/> or <paramref name="observer"/> is <see langword="null"/>.
    /// </exception>
    internal CopilotProviderSession(ICopilotTurnChannel channel, CopilotSessionObserver observer)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(observer);

        _channel = channel;
        _observer = observer;
    }

    /// <summary>
    ///     Gets a value indicating whether this session has been released.
    /// </summary>
    public bool IsReleased { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    ///     <para>
    ///     The runtime's own account, passed through. Copilot reports the tokens it currently holds,
    ///     the limit it will hold them to, and how much of the total the conversation accounts for —
    ///     all counted with the same tokenizer, in the same event. The conversation figure is passed
    ///     explicitly rather than left to be inferred, which is what
    ///     <see cref="IProviderSession.CurrentUsage"/> asks of an adapter that can distinguish it,
    ///     and it makes every threshold comparison downstream happen in Copilot's own tokens.
    ///     </para>
    ///     <para>
    ///     <b>The narrowing to <see cref="int"/> is a clamp, not a checked cast.</b> Reading usage is
    ///     documented as unable to fail, so throwing here would be a contract violation. A context
    ///     window is some millions of tokens against a limit of some billions, so the clamp is
    ///     unreachable in practice; it exists to say in one place what happens if that ever stops
    ///     being true.
    ///     </para>
    ///     <para>
    ///     <b>Before the runtime has reported, the session occupies nothing</b> out of
    ///     <see cref="ProvisionalWindowTokens"/> — see that constant for why the placeholder is safe
    ///     and why it is not a plausible-looking number. A turn that completes with no reading at all
    ///     is refused by <see cref="SendAsync"/> rather than reported from here, because reporting is
    ///     not allowed to fail and that condition must not pass silently.
    ///     </para>
    /// </remarks>
    public ContextUsage CurrentUsage
    {
        get
        {
            if (_observer.LatestUsage is not { } reading)
            {
                return ContextUsage.FromProvider(0, ProvisionalWindowTokens, 0);
            }

            // Reported as it came back, including past the window: a session that has overrun its
            // own limit is the condition an application most needs to see.
            var used = Narrow(reading.CurrentTokens, minimum: 0);
            var window = Narrow(reading.TokenLimit, minimum: 1);

            // Held to the total rather than passed through. ContextUsage refuses a conversation
            // larger than everything occupied - correctly, because it would enlarge the effective
            // window and rotate later than the provider's own limit allows - and this property must
            // not throw. A runtime reporting the two out of step is capped, not rejected.
            var conversation = Math.Min(Narrow(reading.ConversationTokens ?? reading.CurrentTokens, minimum: 0), used);

            return ContextUsage.FromProvider(used, window, conversation);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     <para>
    ///     One prompt, one turn. What the turn did along the way arrives as events rather than in
    ///     the answer, so the observer is told the turn is starting, the prompt is sent, and the
    ///     entries the observer collected are handed back with the answer. The tool calls and
    ///     results carry the runtime's own call identifiers, so a rotation keeps each pair together.
    ///     </para>
    ///     <para>
    ///     The answer is also collected by the observer as the turn's final assistant message, and is
    ///     returned here as well. <see cref="ProviderTurn"/> collapses that: a trailing assistant
    ///     entry whose text is the answer <em>is</em> the answer, and is not recorded twice.
    ///     </para>
    ///     <para>
    ///     Every refusal below happens before the turn is recorded, and the entries are drained only
    ///     on the successful path, so a refused turn leaves no half-recorded history behind.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     The runtime rewrote this session's history, went idle without answering, or answered
    ///     without reporting its occupancy.
    /// </exception>
    public async Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(IsReleased, this);

        // Refused before the turn is sent, not only after. The flag latches, so once the runtime has
        // rewritten history every later turn is a real send against a conversation the engine no
        // longer describes - the model would run this application's tools, with their side effects,
        // against corrupted state, and a host that responds to the failure below by retrying would
        // do it again on every attempt. Detecting the first rewrite is unavoidably after the fact;
        // letting the second one happen is not.
        ThrowIfHistoryRewritten();

        cancellationToken.ThrowIfCancellationRequested();

        // Discard anything a previous turn left behind before the runtime can produce anything new,
        // so what is drained below is this turn's work and no other's.
        _observer.BeginTurn();

        var answer = await _channel.SendAndWaitAsync(message, cancellationToken).ConfigureAwait(false);

        // Checked first, because it invalidates everything else. AgentKit asks the runtime not to
        // compact or truncate a session its own engine drives; if it did so anyway, the transcript
        // the engine believes it owns no longer describes what the provider holds, and every figure
        // below is about a conversation that no longer exists.
        ThrowIfHistoryRewritten();

        // The runtime went idle without producing an assistant message. Recording an empty answer
        // would hide a runtime failure as a model that had nothing to say, so the error the session
        // reported - when it reported one - is named instead.
        if (answer?.Data is not { } data)
        {
            var reported = _observer.LastErrorMessage;
            throw new InvalidOperationException(
                reported is null
                    ? "The Copilot session went idle without producing an assistant message, and "
                      + "reported no error explaining why."
                    : $"The Copilot session went idle without producing an assistant message. The "
                      + $"runtime reported: {reported}");
        }

        // Knowing when the window is filling is the one thing the session engine needs a token count
        // for, and Copilot reports both figures itself - so an absent reading means something is
        // wrong that an application can see and fix, rather than a number to be estimated around.
        if (_observer.LatestUsage is null)
        {
            throw new InvalidOperationException(
                "The Copilot session answered without reporting its token usage, so this session "
                + "cannot tell when its context window is filling. The runtime normally reports "
                + "occupancy and its limit with every turn; a session that reports neither cannot "
                + "be compacted.");
        }

        return new ProviderTurn(data.Content ?? string.Empty, _observer.DrainEntries());
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     <para>
    ///     Releases the runtime session this instance owns — on the caller's release and on a
    ///     rotation's failure paths alike — and nothing else. The <c>CopilotClient</c> is the host's:
    ///     it is shared by every session a rotation creates and outlives all of them, so it is
    ///     disposed by whoever constructed it and never here.
    ///     </para>
    ///     <para>
    ///     Repeated disposal is permitted, because a rotation and an application's own release may
    ///     both reach the same session.
    ///     </para>
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        IsReleased = true;
        await _channel.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Refuses the session if the runtime has rewritten history the engine believes it owns.
    /// </summary>
    /// <remarks>
    ///     Called both before a turn is sent and after it returns, so the failure is raised on the
    ///     turn that first observes the rewrite and on every turn that follows. A single call site
    ///     after the send would report the divergence once and then let the next turn proceed, which
    ///     is the worse half of the problem: the first rewrite can only be detected after the fact,
    ///     but every send after it is avoidable.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The runtime compacted or truncated the session.</exception>
    private void ThrowIfHistoryRewritten()
    {
        if (!_observer.ProviderRewroteHistory)
        {
            return;
        }

        throw new InvalidOperationException(
            "The Copilot runtime compacted or truncated this session's history, which AgentKit's "
            + "session engine believes it owns. The session was created with the runtime's "
            + "infinite-session compaction disabled, so that request was not honored. The "
            + "engine's transcript and the conversation the runtime holds have diverged, and "
            + "this session cannot be used further.");
    }

    /// <summary>
    ///     Narrows one of the runtime's token figures into the range Core's usage shape accepts.
    /// </summary>
    /// <remarks>
    ///     A clamp rather than a checked conversion, for the reason <see cref="CurrentUsage"/> gives:
    ///     reading usage must not fail. Kept as a named method so the narrowing happens in exactly
    ///     one place and a reader can see that it is a clamp on sight.
    /// </remarks>
    /// <param name="value">The figure the runtime reported.</param>
    /// <param name="minimum">The smallest value Core accepts for this figure.</param>
    /// <returns>The figure, held within <paramref name="minimum"/> and <see cref="int.MaxValue"/>.</returns>
    private static int Narrow(long value, int minimum) =>
        (int)Math.Clamp(value, minimum, int.MaxValue);
}
