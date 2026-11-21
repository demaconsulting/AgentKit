using System.Runtime.CompilerServices;
using DemaConsulting.AgentKit.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.ChatClient;

/// <summary>
///     The options of a <see cref="ToolPackContextProvider"/>.
/// </summary>
/// <remarks>
///     Immutable once constructed; safe to share between providers.
/// </remarks>
public sealed class ToolPackContextProviderOptions
{
    /// <summary>
    ///     The state key used when none is specified.
    /// </summary>
    public const string DefaultStateKey = "AgentKit.ToolPackContextProvider";

    /// <summary>
    ///     Gets the instructions contributed to every run alongside the tools, or
    ///     <see langword="null"/> to contribute none.
    /// </summary>
    /// <remarks>
    ///     Typically how to use the tools — for example that paths are relative to the workspace, or
    ///     that a retained output is read with <c>tool_output_read</c>. The agent merges them with its
    ///     own instructions.
    /// </remarks>
    public string? Instructions { get; init; }

    /// <summary>
    ///     Gets the host capabilities declared when composing tool packs. Used only by the pack
    ///     constructor; defaults to <see cref="HostCapabilities.None"/>.
    /// </summary>
    public HostCapabilities HostCapabilities { get; init; } = HostCapabilities.None;

    /// <summary>
    ///     Gets the retention options for large tool outputs, or <see langword="null"/> to compose no
    ///     retention.
    /// </summary>
    /// <remarks>
    ///     When set, every conversation gets its own <see cref="ToolOutputRetention"/>: the tools are
    ///     wrapped into it and its reader tools are contributed with them.
    /// </remarks>
    public ToolOutputOptions? ToolOutput { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the text of retained outputs is persisted with the session.
    ///     Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    ///     When <see langword="true"/>, a serialized session carries the retained outputs — up to
    ///     <see cref="ToolOutputOptions.MaxRetainedCharacters"/> characters — so a reference survives a
    ///     reload. When <see langword="false"/>, only the reference counter is persisted: a reference
    ///     made before a reload is then answered "no longer retained", never re-issued to different
    ///     content. Persisted outputs have the privacy class of the chat history.
    /// </remarks>
    public bool PersistRetainedOutputs { get; init; } = true;

    /// <summary>
    ///     Gets the key under which the provider keeps its state in a session's state bag. Defaults to
    ///     <see cref="DefaultStateKey"/>; set a distinct key when two providers share one agent.
    /// </summary>
    public string StateKey { get; init; } = DefaultStateKey;
}

/// <summary>
///     A Microsoft Agent Framework <see cref="AIContextProvider"/> that contributes AgentKit tools —
///     composed from tool packs, or from any function list — and optional instructions to every run
///     of a <see cref="ChatClientAgent"/>, composed once per conversation and with each
///     conversation's large-output retention persisted in its session.
/// </summary>
/// <remarks>
///     <para>
///     <b>Why it exists.</b> A host that builds its own <see cref="ChatClientAgent"/> can hand it a
///     tool list once, but AgentKit's tools carry per-conversation state — a retention store, a task
///     list, a memory store — that must not be shared between conversations. This provider composes
///     the tools once per <see cref="AgentSession"/>, so each conversation has its own, and
///     contributes them on every run of that session.
///     </para>
///     <para>
///     <b>Retention survives persistence.</b> With <see cref="ToolPackContextProviderOptions.ToolOutput"/>
///     set, each conversation gets its own <see cref="ToolOutputRetention"/>. After every run,
///     successful or not, its snapshot is written to the session's state bag under
///     <see cref="ToolPackContextProviderOptions.StateKey"/>, so it is serialized with the session; a
///     session reloaded by the agent restores the retention on its first run and the model's earlier
///     references still read. A session serialized while a run is in flight carries the state of the
///     previous run. Task lists and in-memory memory stores are not persisted: a reloaded
///     conversation starts them empty.
///     </para>
///     <para>
///     <b>Only <see cref="ChatClientAgent"/> honors it.</b> The framework merges the tools a provider
///     contributes into the request only for a chat-client agent. Other agents — including AgentKit's
///     Copilot agent, whose runtime fixes a session's tools when the session is created — would
///     silently ignore them, so this provider refuses to run for any agent that is not, and does not
///     wrap, a <see cref="ChatClientAgent"/>. Compose tools for those agents directly, through
///     <c>CopilotAgentFactory</c> or a <see cref="ToolOutputRetention"/> the host owns.
///     </para>
///     <para>
///     A run with no session composes a throwaway tool set and persists nothing. The provider is safe
///     for concurrent use: per-session state is held in a <see cref="ConditionalWeakTable{TKey,TValue}"/>
///     and each retention is itself thread-safe.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Giving a host-built agent AgentKit's tools per conversation, with image promotion and a
///     reducer over its own history. In a real application <c>textFiles</c> is
///     <c>new TextFilePack()</c> from the <c>DemaConsulting.AgentKit.Tools</c> package.
///     </para>
///     <code>
///     public AIAgent CreateAgent(IChatClient client, IToolPack textFiles, ISummarizer summarizer, string workspace)
///     {
///         var policy = new PathPolicy(workspace, [PathRule.ReadOnly(workspace)]);
///         var tools = new ToolPackContextProvider(
///             policy,
///             [textFiles],
///             new ToolPackContextProviderOptions
///             {
///                 Instructions = "Paths are relative to the workspace.",
///                 ToolOutput = ToolOutputOptions.Default
///             });
///
///         return new ChatClientAgent(
///             client.AsBuilder().UseImagePromotion().Build(),
///             new ChatClientAgentOptions
///             {
///                 Name = "assistant",
///                 AIContextProviders = [tools],
///                 ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
///                 {
///                     ChatReducer = new AgentKitChatReducer(summarizer, thresholdCharacters: 40_000),
///                     ReducerTriggerEvent = InMemoryChatHistoryProviderOptions.ChatReducerTriggerEvent.BeforeMessagesRetrieval
///                 })
///             });
///     }
///     </code>
/// </example>
public sealed class ToolPackContextProvider : AIContextProvider
{
    /// <summary>
    ///     Composes one conversation's tools, before any retention is applied.
    /// </summary>
    private readonly Func<IReadOnlyList<AIFunction>> _compose;

    /// <summary>
    ///     The ceilings retention is created with.
    /// </summary>
    private readonly ToolLimits _limits;

    /// <summary>
    ///     The options in force.
    /// </summary>
    private readonly ToolPackContextProviderOptions _options;

    /// <summary>
    ///     Each conversation's composed tools and retention, held only as long as its session lives.
    /// </summary>
    private readonly ConditionalWeakTable<AgentSession, SessionTools> _sessions = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="ToolPackContextProvider"/> class that composes
    ///     tool packs under an access policy.
    /// </summary>
    /// <remarks>
    ///     Each conversation's tools are built with a <see cref="ToolPackBuilder"/> over
    ///     <paramref name="policy"/>, declaring <see cref="ToolPackContextProviderOptions.HostCapabilities"/>
    ///     and adding every pack, so each pack creates fresh tools — and fresh per-conversation stores —
    ///     for each conversation. A trial composition runs here, so a pack collision or a composition
    ///     the host's capabilities leave empty is reported at construction.
    /// </remarks>
    /// <param name="policy">The access policy every composed tool is governed by. Must not be null.</param>
    /// <param name="packs">The packs to compose. Must be non-null, non-empty and contain no null.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> or <paramref name="packs"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="packs"/> is empty or holds a null entry, when the packs compose no
    ///     tool for the declared capabilities, when two packs collide, when the state key is blank, or
    ///     when retention is requested and the policy's result ceiling is below
    ///     <see cref="ToolOutputSpill.MinimumResultCharacters"/>.
    /// </exception>
    public ToolPackContextProvider(
        PathPolicy policy,
        IEnumerable<IToolPack> packs,
        ToolPackContextProviderOptions? options = null)
        : base(null, null, null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(packs);

        var supplied = packs.ToList();
        if (supplied.Count == 0 || supplied.Any(pack => pack is null))
        {
            throw new ArgumentException("At least one pack, and no null pack, is required.", nameof(packs));
        }

        _options = ValidateOptions(options, policy.Limits);
        _limits = policy.Limits;
        var capabilities = _options.HostCapabilities;
        _compose = () =>
        {
            var builder = new ToolPackBuilder(policy).WithHostCapabilities(capabilities);
            foreach (var pack in supplied)
            {
                builder.Add(pack);
            }

            return builder.Build();
        };

        // A trial composition surfaces a collision or an empty composition where the host wrote it,
        // rather than at the first run of the first conversation.
        if (_compose().Count == 0)
        {
            throw new ArgumentException(
                "The packs compose no tool for the declared host capabilities.",
                nameof(packs));
        }
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="ToolPackContextProvider"/> class that composes
    ///     any function list, bounding each function by the supplied ceilings.
    /// </summary>
    /// <remarks>
    ///     <paramref name="composeTools"/> is called once per conversation and should return new tool
    ///     instances each time where tools carry state. Every function it returns is bounded with
    ///     <see cref="GuardedToolFactory.AsGuarded"/> and <paramref name="limits"/>, so a tool the
    ///     library did not build is held to the same ceilings as its own.
    /// </remarks>
    /// <param name="composeTools">Composes one conversation's tools. Must not be null.</param>
    /// <param name="limits">The ceilings every tool, and any retention, observes. Must not be null.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="composeTools"/> or <paramref name="limits"/> is
    ///     <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the state key is blank, or retention is requested and the result ceiling is
    ///     below <see cref="ToolOutputSpill.MinimumResultCharacters"/>.
    /// </exception>
    public ToolPackContextProvider(
        Func<IReadOnlyList<AIFunction>> composeTools,
        ToolLimits limits,
        ToolPackContextProviderOptions? options = null)
        : base(null, null, null)
    {
        ArgumentNullException.ThrowIfNull(composeTools);
        ArgumentNullException.ThrowIfNull(limits);

        _options = ValidateOptions(options, limits);
        _limits = limits;
        _compose = () => [.. (composeTools() ?? []).Select(tool => tool.AsGuarded(limits))];
    }

    /// <summary>
    ///     Gets the keys this provider stores state under in a session's state bag.
    /// </summary>
    /// <remarks>
    ///     Declared so the framework can detect two providers writing to the same key.
    /// </remarks>
    public override IReadOnlyList<string> StateKeys => [_options.StateKey];

    /// <summary>
    ///     Contributes the conversation's tools and the configured instructions to a run.
    /// </summary>
    /// <param name="context">The run's context. Must not be null.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>The context to merge: the conversation's tools and the instructions.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the agent is not a <see cref="ChatClientAgent"/>, which alone would use the
    ///     tools, or when the composition yields no tool.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when the session holds a retention snapshot under this provider's key that
    ///     deserializes but is malformed.
    /// </exception>
    /// <exception cref="System.Text.Json.JsonException">
    ///     Thrown by the session's state bag when the state under this provider's key does not
    ///     deserialize as a retention snapshot.
    /// </exception>
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Another agent would accept the tools and silently never offer them to the model.
        if (context.Agent.GetService(typeof(ChatClientAgent)) is null)
        {
            throw new InvalidOperationException(
                "ToolPackContextProvider contributes tools, which only a ChatClientAgent uses; the agent '"
                + context.Agent.Name + "' would ignore them. Compose tools for other agents directly, for "
                + "example through CopilotAgentFactory or a ToolOutputRetention the host owns.");
        }

        // One composition per conversation, so per-conversation state is never shared. A run with no
        // session gets a throwaway composition that nothing persists.
        var composed = context.Session is null
            ? Compose(session: null)
            : _sessions.GetValue(context.Session, Compose);

        // A fresh list every run: the framework hands it on as the request's ChatOptions.Tools, which
        // a middleware or later context provider may legitimately extend for that one request. The
        // conversation's composition itself is read-only and never handed out.
        return new ValueTask<AIContext>(new AIContext
        {
            Tools = new List<AITool>(composed.Tools),
            Instructions = _options.Instructions
        });
    }

    /// <summary>
    ///     Persists the conversation's retention in its session after every run, successful or not.
    /// </summary>
    /// <remarks>
    ///     Overridden rather than relying on the base behavior, which skips a failed run: a run that
    ///     failed after a tool retained output has still issued references the history may hold.
    /// </remarks>
    /// <param name="context">The run's outcome. Must not be null.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>A completed task.</returns>
    protected override ValueTask InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Session is { } session
            && _sessions.TryGetValue(session, out var composed)
            && composed.Retention is { } retention)
        {
            var snapshot = retention.Export();
            if (!_options.PersistRetainedOutputs)
            {
                // The counter alone keeps a reference made before a reload from being re-issued.
                snapshot = new ToolOutputRetentionSnapshot(snapshot.LastIssued, []);
            }

            session.StateBag.SetValue(_options.StateKey, snapshot, ToolOutputRetentionSnapshot.JsonTypeInfo.Options);
        }

        return default;
    }

    /// <summary>
    ///     Validates the options, returning the defaults when none were supplied.
    /// </summary>
    /// <param name="options">The options supplied, or null.</param>
    /// <param name="limits">The ceilings retention would be created with.</param>
    /// <returns>The options in force.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when the state key is blank, or retention is requested over a result ceiling too
    ///     small for it.
    /// </exception>
    private static ToolPackContextProviderOptions ValidateOptions(ToolPackContextProviderOptions? options, ToolLimits limits)
    {
        var resolved = options ?? new ToolPackContextProviderOptions();
        if (string.IsNullOrWhiteSpace(resolved.StateKey))
        {
            throw new ArgumentException("The state key must not be blank.", nameof(options));
        }

        if (resolved.ToolOutput is not null && limits.MaxResultCharacters < ToolOutputSpill.MinimumResultCharacters)
        {
            throw new ArgumentException(
                "Retaining large tool outputs requires a result ceiling of at least "
                + ToolOutputSpill.MinimumResultCharacters + " characters.",
                nameof(options));
        }

        return resolved;
    }

    /// <summary>
    ///     Composes one conversation's tools, wrapping them into a retention restored from the session
    ///     when retention is configured.
    /// </summary>
    /// <param name="session">The conversation's session, or null for a session-less run.</param>
    /// <returns>The composed tools and their retention.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the composition yields no tool.</exception>
    private SessionTools Compose(AgentSession? session)
    {
        var tools = _compose();
        if (tools.Count == 0)
        {
            throw new InvalidOperationException("The tool composition yielded no tool.");
        }

        if (_options.ToolOutput is not { } outputOptions)
        {
            return new SessionTools(Array.AsReadOnly<AITool>([.. tools]), Retention: null);
        }

        // A session reloaded by the agent carries the snapshot written after its last run.
        var retention = session is not null
                        && session.StateBag.TryGetValue<ToolOutputRetentionSnapshot>(
                            _options.StateKey,
                            out var snapshot,
                            ToolOutputRetentionSnapshot.JsonTypeInfo.Options)
                        && snapshot is not null
            ? ToolOutputRetention.Restore(snapshot, _limits, outputOptions)
            : new ToolOutputRetention(_limits, outputOptions);

        return new SessionTools(Array.AsReadOnly<AITool>([.. retention.Wrap(tools), .. retention.ReaderTools]), retention);
    }

    /// <summary>
    ///     One conversation's composed tools and the retention they share, if any.
    /// </summary>
    /// <param name="Tools">
    ///     The tools contributed to every run of the conversation, read-only so a run's changes to its
    ///     own copy never reach the composition.
    /// </param>
    /// <param name="Retention">The conversation's retention, or null when none is configured.</param>
    private sealed record SessionTools(IReadOnlyList<AITool> Tools, ToolOutputRetention? Retention);
}
