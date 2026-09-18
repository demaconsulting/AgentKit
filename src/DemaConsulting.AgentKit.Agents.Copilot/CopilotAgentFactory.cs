using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     Builds a Microsoft Agent Framework <see cref="AIAgent"/> from a GitHub Copilot
///     <see cref="CopilotClient"/> and a supplied tool list, suppressing the built-in tools the
///     Copilot runtime would otherwise inject.
/// </summary>
/// <remarks>
///     <para>
///     Copilot arrives as a complete agent runtime carrying its own tools — shell, fetch, file
///     reading and writing, and more. Supplying an application's own tools is not enough: unless
///     the built-ins are suppressed, a document assistant silently retains command execution.
///     <c>SessionConfig.AvailableTools</c> is an allow-list, so this factory derives it from
///     the <em>same</em> collection it assigns to <c>SessionConfig.Tools</c>, which makes
///     the tool set and its allow-list impossible to drift apart. This derivation is the
///     safety-critical behavior of the package.
///     </para>
///     <para>
///     <b>Every Copilot session this package builds is configured here, on one path.</b> The agent
///     path goes through <see cref="BuildSessionConfig"/>; the session-engine paths —
///     <see cref="CopilotProviderSessionFactory"/> and <see cref="CopilotSummarizer"/> — go through
///     <see cref="BuildEngineSessionConfig"/>. Both end in <see cref="ApplyConfinement"/>, which is
///     the single site where the allow-list is derived, the skill and custom-instruction channels
///     are closed, and the default-safe permission handler is installed. A second builder with its
///     own derivation is precisely the drift this arrangement exists to prevent, so there is not
///     one.
///     </para>
///     <para>
///     <b>The two paths differ in exactly one respect, deliberately.</b>
///     <see cref="BuildEngineSessionConfig"/> raises the Copilot runtime's own compaction threshold
///     clear of the session engine's rotation point; <see cref="BuildSessionConfig"/> leaves the
///     runtime's compaction alone. A session the AgentKit engine drives has an AgentKit compactor
///     behind it, and two compactors reading the same occupancy signal would fight — see
///     <see cref="BuildEngineSessionConfig"/> for the full argument. A plain Copilot agent has no
///     AgentKit compactor behind it, so changing the runtime's would remove protection rather than
///     prevent a conflict. Do not "tidy" the asymmetry away.
///     </para>
///     <para>
///     <b>Permission handling is default-safe.</b> When no handler is supplied, the factory
///     installs one that approves exactly the supplied tools by name and rejects every other
///     request — including every built-in tool, none of which is one of the supplied custom tools.
///     A host may supply its own handler to override that behavior.
///     </para>
///     <para>
///     <b>Ownership contract.</b> The host constructs, starts, and disposes the
///     <see cref="CopilotClient"/>; this factory takes no ownership of it. The agent is built with
///     <c>ownsClient: false</c>, and the factory creates nothing disposable of its own, so the
///     rule is unambiguous: <em>whoever created the client disposes it</em>.
///     </para>
///     <para>
///     <b>No image-promoting decorator.</b> Unlike the <c>IChatClient</c> adapter, this factory
///     does not install <c>ImagePromotingChatClient</c>: the Copilot runtime already delivers
///     images from tool results to the model, verified in the spike, so promoting them onto a user
///     message would duplicate content the model already received. A future maintainer should not
///     "helpfully" add it here.
///     </para>
///     <para>
///     <b>Model selection belongs to the host.</b> Copilot backs a session with a model, and which
///     model that is materially changes how well a confined agent uses its tools. The factory
///     therefore lets the host name one, and applies it only when the host does: a host that names
///     no model gets the runtime's own default, exactly as before this parameter existed. Reaching
///     the setting must not require going around this factory, because doing so silently forfeits
///     the built-in suppression, the skill and custom-instruction withholding, and the default-safe
///     permission handler above.
///     </para>
///     <para>
///     This factory is a thin adapter. It holds no state and shares no code with the ChatClient
///     adapter.
///     </para>
/// </remarks>
public static class CopilotAgentFactory
{
    /// <summary>
    ///     The share of the context window at which the Copilot runtime is allowed to compact a
    ///     session AgentKit's engine drives.
    /// </summary>
    /// <remarks>
    ///     Raised well above the engine's own rotation point so the engine always acts first. The
    ///     runtime's default is 0.80 and the engine rotates at 0.70, which leaves a tenth of the
    ///     window between them — close enough that a single turn returning a large tool result can
    ///     cross it in one step. It is not set to 1.0: the runtime's last-resort behavior is worth
    ///     keeping for the case where something extraordinary happens, and this engine refuses a
    ///     session the runtime has rewritten rather than silently continuing, so the outcome is
    ///     diagnosable either way.
    /// </remarks>
    private const double RuntimeCompactionThreshold = 0.95;

    /// <summary>
    ///     Builds an agent from a Copilot client and a supplied tool list, with the runtime's
    ///     built-in tools suppressed.
    /// </summary>
    /// <remarks>
    ///     <paramref name="model"/> is the last parameter rather than sitting beside
    ///     <paramref name="instructions"/>, where it would read more naturally, so that every
    ///     existing positional call keeps binding to the parameter it always bound to. Inserting it
    ///     earlier would shift <paramref name="onPermissionRequest"/> and <paramref name="name"/>
    ///     and break source compatibility for a released public API; a slightly awkward position is
    ///     the cheaper price.
    /// </remarks>
    /// <param name="client">
    ///     The Copilot client the agent runs on. Must not be <see langword="null"/>. The host owns
    ///     the client: it constructs, starts, and disposes it, and this factory builds the agent
    ///     with <c>ownsClient: false</c>.
    /// </param>
    /// <param name="tools">
    ///     The tools the agent may call. Must not be <see langword="null"/> or empty, must contain
    ///     no <see langword="null"/> entry, and must carry no two tools of the same name. The
    ///     session's allow-list is derived from this collection.
    /// </param>
    /// <param name="instructions">The system instructions for the agent, if any.</param>
    /// <param name="onPermissionRequest">
    ///     The permission handler. When <see langword="null"/>, a safe default is installed that
    ///     approves exactly the supplied tools by name and rejects everything else.
    /// </param>
    /// <param name="name">The name of the agent, if any.</param>
    /// <param name="model">
    ///     The Copilot model to back the session — for example <c>gpt-5.4-mini</c>. When
    ///     <see langword="null"/>, empty, or whitespace, no model is set and the Copilot runtime
    ///     applies its own default, which is the behavior every caller that omits this parameter
    ///     gets. The name is not validated here: an unrecognized name is rejected by the runtime at
    ///     session time, because only the runtime knows which models the signed-in user may use.
    /// </param>
    /// <returns>An agent that runs on the supplied client with only the supplied tools available.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="client"/> or <paramref name="tools"/> is <see langword="null"/>, or a
    ///     tool in <paramref name="tools"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> is empty, or two tools carry the same name.
    /// </exception>
    /// <example>
    ///     <para>
    ///     Assembling a complete agent on the GitHub Copilot runtime. The host constructs and starts
    ///     the client and keeps ownership of it; this factory derives the session allow-list from
    ///     the same tool list it publishes, which is what suppresses the runtime's built-in shell,
    ///     fetch and file tools. In a real application <c>textFiles</c> and
    ///     <c>images</c> are <c>new TextFilePack()</c> and <c>new ImagePack()</c> from
    ///     the <c>DemaConsulting.AgentKit.Tools</c> package.
    ///     </para>
    ///     <code>
    ///     public async Task&lt;AIAgent&gt; CreateAgentAsync(
    ///         string workspace,
    ///         string session,
    ///         IToolPack textFiles,
    ///         IToolPack images,
    ///         CancellationToken cancellationToken)
    ///     {
    ///         // The anchor is the workspace; the grants say what may be read and what may be written.
    ///         var policy = new PathPolicy(
    ///             workingDirectory: workspace,
    ///             grants: [PathRule.ReadOnly(workspace), PathRule.ReadWrite(session)]);
    ///
    ///         IList&lt;AIFunction&gt; tools =
    ///         [
    ///             .. new ToolPackBuilder(policy)
    ///                 .WithHostCapabilities(HostCapabilities.Vision)
    ///                 .Add(textFiles)
    ///                 .Add(images)
    ///                 .Build()
    ///         ];
    ///
    ///         // The host owns the client: it constructs, starts, and disposes it.
    ///         var client = new CopilotClient(new CopilotClientOptions
    ///         {
    ///             WorkingDirectory = workspace,
    ///             UseLoggedInUser = true,
    ///         });
    ///
    ///         await client.StartAsync(cancellationToken);
    ///
    ///         return CopilotAgentFactory.Create(
    ///             client,
    ///             tools,
    ///             instructions: "You are a document assistant confined to the permitted locations.",
    ///             name: "document-assistant");
    ///     }
    ///     </code>
    /// </example>
    public static AIAgent Create(
        CopilotClient client,
        IList<AIFunction> tools,
        string? instructions = null,
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>? onPermissionRequest = null,
        string? name = null,
        string? model = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ValidateTools(tools);

        var config = BuildSessionConfig(tools, instructions, onPermissionRequest, model);

        // ownsClient: false — the host owns the client's lifetime; see the ownership contract in
        // the type remarks. The SessionConfig overload of AsAIAgent is the only path that carries
        // the AvailableTools allow-list, and therefore the only one that suppresses the built-ins.
        return client.AsAIAgent(config, ownsClient: false, name: name);
    }

    /// <summary>
    ///     Builds the session configuration that suppresses the built-in tools, deriving the
    ///     allow-list from the supplied tools.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Exposed as a seam so a test can assert the safety-critical property — that
    ///     <c>SessionConfig.AvailableTools</c> is derived from the same collection as
    ///     <c>SessionConfig.Tools</c> — without a live <see cref="CopilotClient"/>. Both are
    ///     built from <paramref name="tools"/> in one place so the two cannot diverge.
    ///     </para>
    ///     <para>
    ///     This is the <em>agent</em> path. It deliberately leaves <c>InfiniteSessions</c> untouched,
    ///     so the Copilot runtime keeps compacting a plain agent's session as it always has, at its own
    ///     threshold: nothing else is watching that session's window. The session-engine path,
    ///     <see cref="BuildEngineSessionConfig"/>, raises that threshold for the opposite reason.
    ///     </para>
    /// </remarks>
    /// <param name="tools">The tools to publish and allow.</param>
    /// <param name="instructions">The system instructions, if any.</param>
    /// <param name="onPermissionRequest">The permission handler, or <see langword="null"/> for the safe default.</param>
    /// <param name="model">
    ///     The Copilot model to back the session, or <see langword="null"/>/blank to leave
    ///     <c>SessionConfig.Model</c> unset so the runtime applies its own default. Defaulted so a
    ///     caller that has no opinion about the model need not say so.
    /// </param>
    /// <returns>The configured session.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tools"/> is <see langword="null"/>, or contains a <see langword="null"/> entry.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> is empty, or two tools carry the same name.
    /// </exception>
    internal static SessionConfig BuildSessionConfig(
        IList<AIFunction> tools,
        string? instructions,
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>? onPermissionRequest,
        string? model = null)
    {
        ValidateTools(tools);

        return CreateSessionConfig(tools, instructions, onPermissionRequest, model);
    }

    /// <summary>
    ///     Builds the session configuration for a session whose context AgentKit's own session
    ///     engine manages: the same confinement the agent path receives, plus the Copilot runtime's
    ///     own compaction threshold raised clear of the engine's rotation point.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Why the runtime's compaction is held off here.</b> Copilot compacts its own session:
    ///     <c>InfiniteSessionConfig</c> defaults to enabled, with background compaction at 0.80 of
    ///     the window and buffer exhaustion at 0.95. AgentKit's session engine rotates at 0.70,
    ///     against the same occupancy signal. Left at that default, both compactors would act on one
    ///     conversation: Copilot would rewrite history underneath a session whose transcript the
    ///     engine believes it owns, and the occupancy the engine reads afterwards would move for
    ///     reasons it cannot see — so the engine would seed a replacement from a history the
    ///     provider no longer holds, and the two accounts of the conversation would diverge
    ///     silently. A tenth of the window is not a margin: one turn returning a large tool result
    ///     can cross it in a single step. <b>A future maintainer must not lower the threshold back
    ///     toward the engine's own.</b>
    ///     </para>
    ///     <para>
    ///     <b>The threshold is what the runtime honors; the enablement flag is not.</b> Measured
    ///     against the live runtime on SDK 1.0.11, a session created with <c>Enabled = false</c>
    ///     compacted as soon as its threshold was crossed, exactly as one created with it true did.
    ///     The flag is still set, because it states the intent and costs nothing if the runtime ever
    ///     begins honoring it, but nothing here depends on it.
    ///     </para>
    ///     <para>
    ///     <b>The threshold is deliberately not 1.0, so a rewrite stays possible.</b> The runtime's
    ///     last-resort behavior is worth keeping, so the margin is a margin rather than a guarantee:
    ///     <see cref="CopilotSessionObserver"/> watches for the runtime's own compaction and
    ///     truncation events, and <see cref="CopilotProviderSession"/> refuses the next turn if one
    ///     arrives. That turns a silent divergence into a diagnosable failure.
    ///     </para>
    ///     <para>
    ///     <b>An empty tool list is accepted here and refused on the agent path.</b> A consolidation
    ///     runs on a session that must offer no tools at all, which is a correct engine-driven
    ///     session and an incorrect agent. The confinement is identical either way: an empty tool
    ///     list derives an empty allow-list and a permission handler that approves nothing, which is
    ///     the strongest confinement this factory can express rather than the weakest.
    ///     </para>
    /// </remarks>
    /// <param name="tools">
    ///     The tools to publish and allow. Must not be <see langword="null"/>; may be empty. Must
    ///     contain no <see langword="null"/> entry and no two tools of the same name.
    /// </param>
    /// <param name="instructions">The system instructions, if any.</param>
    /// <param name="model">
    ///     The Copilot model to back the session, or <see langword="null"/>/blank to leave
    ///     <c>SessionConfig.Model</c> unset so the runtime applies its own default.
    /// </param>
    /// <returns>
    ///     The configured session, with the runtime's own compaction threshold raised clear of the
    ///     engine's rotation point.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tools"/> is <see langword="null"/>, or contains a <see langword="null"/> entry.
    /// </exception>
    /// <exception cref="ArgumentException">Two tools carry the same name.</exception>
    internal static SessionConfig BuildEngineSessionConfig(
        IList<AIFunction> tools,
        string? instructions,
        string? model)
    {
        ValidateToolNames(tools);

        var config = CreateSessionConfig(tools, instructions, onPermissionRequest: null, model);

        // Enabled = false does not work, and is set anyway: measured against SDK 1.0.11, a session
        // created with it still compacted as soon as the threshold was crossed, identically to one
        // created with it true. It is kept because it states the intent and costs nothing if the
        // runtime ever starts honoring it - but nothing may depend on it.
        //
        // The threshold is honored, and is what actually holds the runtime off. It is raised rather
        // than the flag being trusted: AgentKit rotates at 0.70 of the window, so the runtime's
        // default of 0.80 leaves only a tenth of the window between the two. One turn returning a
        // large tool result can cross that gap in a single step, and then the runtime rewrites a
        // history this engine believes it owns. At 0.95 the margin is a quarter of the window, and
        // the runtime's own last-resort behavior is still there if something extraordinary happens.
        config.InfiniteSessions = new InfiniteSessionConfig
        {
            Enabled = false,
            BackgroundCompactionThreshold = RuntimeCompactionThreshold,
        };

        return config;
    }

    /// <summary>
    ///     Builds a confined session configuration from validated tools.
    /// </summary>
    /// <remarks>
    ///     The single place a <see cref="SessionConfig"/> is constructed in this package. Both
    ///     public-facing builders reach it, which is what keeps the confinement, the model choice
    ///     and the system message on one path; they differ only in which tool lists they accept and
    ///     in whether they adjust the runtime's own compaction afterwards.
    /// </remarks>
    /// <param name="tools">The validated tools to publish and allow.</param>
    /// <param name="instructions">The system instructions, if any.</param>
    /// <param name="onPermissionRequest">The permission handler, or <see langword="null"/> for the safe default.</param>
    /// <param name="model">The model to back the session, or <see langword="null"/>/blank for the runtime's default.</param>
    /// <returns>The configured session.</returns>
    private static SessionConfig CreateSessionConfig(
        IList<AIFunction> tools,
        string? instructions,
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>? onPermissionRequest,
        string? model)
    {
        var config = new SessionConfig();

        ApplyConfinement(config, tools, onPermissionRequest);

        if (!string.IsNullOrWhiteSpace(model))
        {
            // Assigned only when the host named a model. Leaving the property untouched otherwise is
            // what preserves the runtime's own default for every caller that says nothing, so this
            // parameter is purely additive.
            config.Model = model;
        }

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            config.SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = instructions,
            };
        }

        return config;
    }

    /// <summary>
    ///     Confines a session to the supplied tools: derives the allow-list from them, closes the
    ///     runtime's injection channels, and installs a permission handler.
    /// </summary>
    /// <remarks>
    ///     <b>The one safety-critical block in this package, and it exists exactly once.</b>
    ///     <c>Tools</c> and <c>AvailableTools</c> are assigned from the same collection in the same
    ///     two statements, so no reachable state has a published tool that is not allowed or an
    ///     allowed name that is not published. A second copy of this derivation — in a session
    ///     builder for the engine paths, say — is the drift the whole design exists to prevent, so
    ///     every builder calls this rather than repeating it.
    /// </remarks>
    /// <param name="config">The session being configured.</param>
    /// <param name="tools">The tools to publish and allow.</param>
    /// <param name="onPermissionRequest">The permission handler, or <see langword="null"/> for the safe default.</param>
    private static void ApplyConfinement(
        SessionConfig config,
        IList<AIFunction> tools,
        Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>? onPermissionRequest)
    {
        // AvailableTools and Tools are both derived from the same collection so the tool set and its
        // allow-list cannot drift apart. This is the suppression the package exists for.
        config.Tools = [.. tools];
        config.AvailableTools = [.. tools.Select(tool => tool.Name)];

        // The runtime's skills are a further channel of injected capability an untrained user
        // could not adjudicate; a confined agent receives only what its host attached.
        config.EnableSkills = false;
        config.SkipCustomInstructions = true;

        config.OnPermissionRequest = onPermissionRequest ?? CreateDefaultPermissionHandler(tools);
    }

    /// <summary>
    ///     Creates the safe-default permission handler: approve exactly the supplied tools by name,
    ///     reject everything else.
    /// </summary>
    /// <remarks>
    ///     A built-in request — shell, read, write, url and the rest — is never a
    ///     <see cref="PermissionRequestCustomTool"/>, so it can never match a supplied tool name
    ///     and is always rejected. Only a supplied custom tool, requested by its exact name, is
    ///     approved. The allow-list is captured once, so the handler cannot approve a name the
    ///     session did not publish.
    /// </remarks>
    /// <param name="tools">The tools whose names are approved.</param>
    /// <returns>A permission handler enforcing the allow-list.</returns>
    internal static Func<PermissionRequest, PermissionInvocation, Task<PermissionDecision>>
        CreateDefaultPermissionHandler(IList<AIFunction> tools)
    {
        var permitted = new HashSet<string>(tools.Select(tool => tool.Name), StringComparer.Ordinal);

        return (request, _) =>
        {
            if (request is PermissionRequestCustomTool custom && permitted.Contains(custom.ToolName))
            {
                return Task.FromResult(PermissionDecision.ApproveOnce());
            }

            var requested = DescribeRequest(request);
            return Task.FromResult(PermissionDecision.Reject($"'{requested}' is not an allowed tool."));
        };
    }

    /// <summary>
    ///     Names the tool a permission request concerns, for a rejection message and diagnostics.
    /// </summary>
    /// <param name="request">The request to describe.</param>
    /// <returns>The custom tool name, or the request kind for a built-in request.</returns>
    private static string DescribeRequest(PermissionRequest request)
    {
        return request switch
        {
            PermissionRequestCustomTool custom => custom.ToolName,
            _ => request.Kind ?? "(unknown)",
        };
    }

    /// <summary>
    ///     Rejects a tool list that a well-formed agent could not be built from.
    /// </summary>
    /// <remarks>
    ///     A missing, empty, or malformed tool list is a defect in the composing application. Two
    ///     tools sharing a name would make both the published tool set and the derived allow-list
    ///     ambiguous, so the collision is refused here.
    /// </remarks>
    /// <param name="tools">The tool list to validate.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tools"/> is <see langword="null"/>, or contains a <see langword="null"/> entry.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tools"/> is empty, or two tools carry the same name.
    /// </exception>
    internal static void ValidateTools(IList<AIFunction> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        if (tools.Count == 0)
        {
            throw new ArgumentException("At least one tool is required.", nameof(tools));
        }

        ValidateToolNames(tools);
    }

    /// <summary>
    ///     Rejects a tool list whose entries could not produce an unambiguous allow-list.
    /// </summary>
    /// <remarks>
    ///     Split out from <see cref="ValidateTools"/> because emptiness is the one rule the two
    ///     paths disagree about: an agent publishing no tools is a defect in its host, while a
    ///     session the engine drives for a consolidation must offer none. Everything else — a
    ///     missing list, a null entry, a duplicated name — is a defect on either path and is refused
    ///     here for both.
    /// </remarks>
    /// <param name="tools">The tool list to validate.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="tools"/> is <see langword="null"/>, or contains a <see langword="null"/> entry.
    /// </exception>
    /// <exception cref="ArgumentException">Two tools carry the same name.</exception>
    internal static void ValidateToolNames(IList<AIFunction> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (tool is null)
            {
                throw new ArgumentNullException(nameof(tools), "A tool in the list is null.");
            }

            if (!seen.Add(tool.Name))
            {
                throw new ArgumentException(
                    $"Two tools carry the name '{tool.Name}'; tool names must be unique.",
                    nameof(tools));
            }
        }
    }
}

