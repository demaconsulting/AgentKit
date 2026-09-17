using DemaConsulting.AgentKit.Core;
using GitHub.Copilot;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     Creates <see cref="CopilotProviderSession"/> instances, once at the start of a conversation
///     and again at every rotation.
/// </summary>
/// <remarks>
///     <para>
///     Holds the client and, optionally, the model, so an application states both once — where it
///     configures its provider — and a rotating session asks for neither again. The client is shared
///     by every session this factory creates and is disposed by none of them: the host constructs,
///     starts and disposes it, exactly as <see cref="CopilotAgentFactory"/> records. Each session is
///     AgentKit's and is released when the engine finishes with it.
///     </para>
///     <para>
///     <b>There is no window parameter, unlike the ChatClient factory.</b> Copilot reports its own
///     occupancy and its own limit, so the application is not asked for a figure the provider
///     already knows. See <see cref="CopilotProviderSession.CurrentUsage"/>.
///     </para>
///     <para>
///     <b>There is no permission-handler parameter either.</b> A session created here is driven by
///     AgentKit's session engine rather than by the host turn by turn, so it always takes
///     <see cref="CopilotAgentFactory"/>'s default-safe handler: approve exactly the seeded tools by
///     name, reject everything else including every built-in. A host that wants a policy of its own
///     composes an agent, where that choice is offered.
///     </para>
///     <para>
///     Safe for concurrent use: creating a session reads the client reference and the model name and
///     touches nothing else this factory owns, and each session gets an observer and a runtime
///     session of its own.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Running a long conversation on Copilot that compacts itself. The host owns the client; the
///     factory and the summarizer are what AgentKit needs to rotate the conversation as its window
///     fills.
///     </para>
///     <code>
///     public async Task&lt;CompactingAgentSession&gt; StartAsync(
///         CopilotClient client,
///         IList&lt;AIFunction&gt; tools,
///         CancellationToken cancellationToken)
///     {
///         var options = new AgentSessionOptions(
///             new CopilotSummarizer(client),
///             instructions: "You are a research assistant confined to the permitted locations.",
///             tools: [.. tools]);
///
///         return await CompactingAgentSession.CreateAsync(
///             options,
///             new CopilotProviderSessionFactory(client),
///             cancellationToken);
///     }
///     </code>
/// </example>
public sealed class CopilotProviderSessionFactory : IProviderSessionFactory
{
    /// <summary>
    ///     The line introducing the seeded conversation record on the session's first message.
    /// </summary>
    /// <remarks>
    ///     Fixed text, and deliberately not a generated token. An earlier version fenced the record
    ///     with a random marker so the material could not imitate the boundary; a live run showed the
    ///     model reading that marker back out and offering it as an answer, because sixteen
    ///     characters of hexadecimal in the middle of a conversation look exactly like a reference
    ///     code. The fence is structural punctuation, so it reads as punctuation.
    /// </remarks>
    internal const string RecordOpening =
        "--- begin record of earlier turns in this conversation ---";

    /// <summary>
    ///     The line closing the seeded conversation record on the session's first message.
    /// </summary>
    internal const string RecordClosing =
        "--- end record of earlier turns; the message below is the current one ---";

    /// <summary>
    ///     The line separator the seeded record is rendered with.
    /// </summary>
    /// <remarks>
    ///     A newline rather than <c>Environment.NewLine</c>, deliberately. The record is content sent
    ///     to a model, not text written for this machine: rendering the same history differently on
    ///     Windows and on Linux would make a rotation's output platform-dependent, which defeats a
    ///     provider's prompt cache and makes a run harder to reproduce. Core renders its own seeded
    ///     record labels the same way.
    /// </remarks>
    private const string RecordNewLine = "\n";

    /// <summary>
    ///     Opens one runtime session per rotation. In production a real Copilot session on the
    ///     host's client; in a test, a scripted channel.
    /// </summary>
    private readonly CopilotChannelOpener _opener;

    /// <summary>
    ///     The Copilot model backing every session this factory creates, or <see langword="null"/>
    ///     to leave the choice to the runtime.
    /// </summary>
    private readonly string? _model;

    /// <summary>
    ///     The ceiling every session accounts its window against, or <see langword="null"/> to
    ///     account against whatever the runtime reports.
    /// </summary>
    private readonly int? _maxWindowTokens;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotProviderSessionFactory"/> class.
    /// </summary>
    /// <param name="client">
    ///     The Copilot client every session runs on. Must not be <see langword="null"/>, and must
    ///     already be started. The host owns it: this factory disposes it never.
    /// </param>
    /// <param name="model">
    ///     The Copilot model to back each session — for example <c>gpt-5.4-mini</c>. When
    ///     <see langword="null"/>, empty, or whitespace, no model is set and the runtime applies its
    ///     own default. The name is not validated here: only the runtime knows which models the
    ///     signed-in user may use, so an unrecognized name is refused at session creation.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <param name="maxWindowTokens">
    ///     A ceiling on the window each session accounts against, or <see langword="null"/> to
    ///     account against whatever the runtime reports. Only ever lowers: the runtime's figure is
    ///     what the conversation may actually reach, so a ceiling above it is ignored rather than
    ///     honored. Supplying one makes the engine rotate sooner, which costs <em>more</em> rather
    ///     than less — the runtime serves a repeated prompt almost entirely from cache, and a
    ///     replacement session starts a new cached prefix and adds a summarizer call. Set it for
    ///     answer quality across a long conversation, or to make rotation reachable at all against
    ///     a window larger than any test conversation; not to save money.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxWindowTokens"/> is less than 1.</exception>
    public CopilotProviderSessionFactory(CopilotClient client, string? model = null, int? maxWindowTokens = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWindowTokens ?? 1, 1);

        _opener = CopilotSessionChannel.Open(client);
        _model = model;
        _maxWindowTokens = maxWindowTokens;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotProviderSessionFactory"/> class over
    ///     a supplied channel opener.
    /// </summary>
    /// <remarks>
    ///     The test seam. <c>CopilotSession</c> is sealed with a non-public constructor and no
    ///     virtual members, and <c>CopilotClient</c> is sealed, so nothing above them can be
    ///     exercised without a live runtime and credentials unless the opening of a session is
    ///     injectable. See <see cref="ICopilotTurnChannel"/>.
    /// </remarks>
    /// <param name="opener">Opens one runtime session per rotation.</param>
    /// <param name="model">The model to name on each session, or <see langword="null"/> for the runtime's default.</param>
    /// <param name="maxWindowTokens"></param>
    /// <exception cref="ArgumentNullException"><paramref name="opener"/> is <see langword="null"/>.</exception>
    internal CopilotProviderSessionFactory(CopilotChannelOpener opener, string? model, int? maxWindowTokens = null)
    {
        ArgumentNullException.ThrowIfNull(opener);

        _opener = opener;
        _model = model;
        _maxWindowTokens = maxWindowTokens;
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     <para>
    ///     The order is what keeps nothing unowned. The observer is built first and handed to the
    ///     configuration, so the SDK registers it on the session <em>before</em> the create RPC is
    ///     issued and the very first turn's events are caught. Opening the session is the only
    ///     awaited allocation, and the window between it returning and the session taking ownership
    ///     is guarded: a cancellation that arrives while the create RPC was in flight would
    ///     otherwise discard the only reference to a session the runtime had just created.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The channel opener returned <see langword="null"/>.</exception>
    public async Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        var observer = new CopilotSessionObserver();
        var config = BuildProviderSessionConfig(seed, observer.OnEvent, _model);

        var channel = await _opener(config, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The Copilot channel opener returned null.");

        try
        {
            // Checked here rather than before the open: a token canceled while the create RPC was in
            // flight leaves a session the runtime holds and nothing references, which is the one
            // window in this method where that is true.
            cancellationToken.ThrowIfCancellationRequested();

            return new CopilotProviderSession(
                channel,
                observer,
                _maxWindowTokens,
                ComposeHistoryPreamble(seed));
        }
        catch
        {
            // Released rather than abandoned. Nothing else holds this session: the instance that
            // would have owned it does not exist, and the caller was never given one.
            //
            // This relies on the channel's disposal not throwing, which its contract requires. A
            // throwing disposal here would replace the failure being handled - typically the
            // cancellation above - with a teardown error the caller cannot act on, and defeat any
            // catch of OperationCanceledException.
            await channel.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    ///     Builds the session configuration one seed produces: the confinement, the runtime's own
    ///     compaction threshold raised clear of the engine's rotation point, the observer
    ///     registered, and the application's instructions as the system message.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Exposed as a seam so a test can assert what a rotation actually configures — the derived
    ///     allow-list, the raised runtime compaction threshold and the registered observer — without
    ///     a live <see cref="CopilotClient"/>.
    ///     </para>
    ///     <para>
    ///     <b>The seeded history is not configured here.</b> Copilot's session configuration carries
    ///     no history or messages field of any kind, so a rotation that must seed a rewritten history
    ///     has to put it somewhere else — and the system message, which this method does set, is the
    ///     wrong place for it. The record contains tool results, which may be the contents of a file
    ///     the agent was pointed at. See <see cref="ComposeHistoryPreamble"/> for where it goes and
    ///     why. What this method configures is the application's own instructions, unchanged, exactly
    ///     as the plain agent path configures them.
    ///     </para>
    ///     <para>
    ///     <b>What was rejected, and why.</b> <c>MessageOptions.Attachments</c> carries only file-like
    ///     and GitHub references, several of which the runtime may omit with a stated reason, and an
    ///     attachment the runtime may omit is precisely the silent-divergence channel this must not
    ///     use. <c>ResumeSessionConfig</c> resumes <em>Copilot's own</em> stored history for an
    ///     existing session id, which would resurrect the history a rotation exists to discard. And
    ///     <c>SystemMessageMode.Customize</c> writes into the runtime's own prompt sections, which
    ///     replaces part of the runtime's system prompt and forks the single configuration path the
    ///     safety argument depends on. A priming message sent on its own was rejected too, for
    ///     costing a round trip and a billed answer per rotation — which is why the record is
    ///     prepended to a message the engine was already sending rather than sent by itself.
    ///     </para>
    /// </remarks>
    /// <param name="seed">What the new session must start from. Must not be <see langword="null"/>.</param>
    /// <param name="onEvent">The observer's handler, registered before the session is created.</param>
    /// <param name="model">The model to name, or <see langword="null"/> for the runtime's default.</param>
    /// <returns>The configuration the runtime session is created from.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="seed"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">Two seeded tools carry the same name.</exception>
    internal static SessionConfig BuildProviderSessionConfig(
        ProviderSessionSeed seed,
        Action<SessionEvent> onEvent,
        string? model)
    {
        ArgumentNullException.ThrowIfNull(seed);

        var config = CopilotAgentFactory.BuildEngineSessionConfig(
            [.. seed.Tools],
            seed.Instructions,
            model);

        // Registered on the configuration rather than on the session, because the SDK installs this
        // handler before the session.create RPC is issued - which is what makes the first turn's
        // usage reading and tool traffic observable at all.
        config.OnEvent = onEvent;

        return config;
    }

    /// <summary>
    ///     Composes the seeded history into a preamble for the session's first message.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The record goes on the first message, not in the system message, because of how the
    ///     window is accounted.</b> The engine reasons about the window as overhead — the system
    ///     message and the tool declarations — against the conversation. A record placed in the
    ///     system message is charged as overhead, so overhead grows at every rotation while the
    ///     conversation appears small, and the engine's model of its own window drifts. Compacted
    ///     content belongs in the region that gets compacted. It also reads with more authority than
    ///     it has earned in the instructions channel, since some of it is tool output.
    ///     </para>
    ///     <para>
    ///     It costs no extra request. The preamble is prepended to the message the engine was
    ///     already about to send, so a rotation still produces exactly one call, and the runtime
    ///     holds the result for the rest of the session as it holds any other turn.
    ///     </para>
    ///     <para>
    ///     The two fence lines mark where the handover ends and the current message begins. They are
    ///     fixed text: an earlier version drew a marker per record, and a live run showed the model
    ///     reading it back out and offering it as an answer.
    ///     </para>
    ///     <para>
    ///     Each entry is rendered with Core's own <c>TranscriptEntry.ToTranscriptLine</c>, which is
    ///     already the labeled, mechanical rendering the summarizer is given. Using it rather than
    ///     writing a second renderer means the history a model is seeded with and the material a
    ///     consolidation is performed on describe the conversation the same way.
    ///     </para>
    /// </remarks>
    /// <param name="seed">The seed whose history to render.</param>
    /// <returns>
    ///     The fenced record, or <see langword="null"/> when the seed carries no history — in which
    ///     case the first message is sent exactly as the caller wrote it.
    /// </returns>
    internal static string? ComposeHistoryPreamble(ProviderSessionSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        if (seed.History.Count == 0)
        {
            return null;
        }

        var record = string.Join(
            RecordNewLine,
            seed.History.Select(entry => entry.ToTranscriptLine()));

        return $"{RecordOpening}{RecordNewLine}{record}{RecordNewLine}{RecordClosing}";
    }


}
