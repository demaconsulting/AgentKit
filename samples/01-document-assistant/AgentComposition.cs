using DemaConsulting.AgentKit.Agents.ChatClient;
using DemaConsulting.AgentKit.Agents.Copilot;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.Image;
using DemaConsulting.AgentKit.Tools.TextFile;
using GitHub.Copilot;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace DemaConsulting.AgentKit.Samples.DocumentAssistant;

/// <summary>
///     A built agent paired with the handle that releases whatever runtime resources it owns.
/// </summary>
/// <remarks>
///     The two providers own different kinds of resource — a Copilot client is
///     <see cref="IAsyncDisposable"/>, an Ollama <see cref="System.Net.Http.HttpClient"/> is
///     <see cref="IDisposable"/> — so the composition returns a single uniform cleanup handle. That
///     is what lets every line after construction be provider-neutral: the host disposes one thing
///     and never learns which provider it built.
/// </remarks>
/// <param name="Agent">The constructed agent, ready to create a session and run.</param>
/// <param name="Cleanup">The handle that releases the agent's runtime resources; disposed by the host.</param>
public sealed record AgentSetup(AIAgent Agent, IAsyncDisposable Cleanup);

/// <summary>
///     Composes the guarded tool set and builds the provider agent — the one place in the sample
///     where the choice of provider is visible.
/// </summary>
/// <remarks>
///     <para>
///     This type embodies the sample's governing principle: <b>no provider-specific logic beyond
///     choosing which factory to call.</b> The tool set is composed once, identically, for both
///     providers; only <see cref="CreateAgentAsync"/> branches, and it branches solely to pick a
///     factory and hand back a uniform cleanup handle. Everything downstream — sessions, streaming,
///     tool display, the REPL — never asks which provider it got.
///     </para>
///     <para>
///     The tools themselves are the safety story. A single <see cref="PathPolicy"/> anchors relative
///     paths at the workspace folder and carries two <em>independent</em> grants: the workspace
///     (read-only or read-write, as the application chooses) and a separate session folder
///     (always read-write). Anchoring grants nothing and granting anchors nothing, so the
///     asymmetric case — read the user's documents, write only into the session folder — is
///     expressed directly rather than worked around. The image pack is gated behind the
///     <see cref="HostCapabilities.Vision"/> declaration, so with vision off <c>image_read</c> is
///     never even created; and each adapter suppresses or repairs its provider's own quirk (Copilot's
///     built-in shell/fetch tools, an <see cref="IChatClient"/> dropping tool-returned images). None
///     of that is reimplemented here — it lives inside the shipped packages.
///     </para>
///     <para>
///     <b>The dialect follows from the configuration, not from a setting.</b> Because the workspace
///     is granted and is the anchor, results inside it come back as bare relative names. Results in
///     the session folder lie outside the anchor, so they come back as absolute paths. A reader
///     watching one session sees both dialects and sees why each is the only truthful answer.
///     </para>
/// </remarks>
public static class AgentComposition
{
    /// <summary>
    ///     The name every constructed agent carries, so tool-call transcripts and any provider-side
    ///     logging identify the sample.
    /// </summary>
    private const string AgentName = "document-assistant";

    /// <summary>
    ///     Builds the system instructions handed to the agent, naming the two locations this run
    ///     actually granted. Shared verbatim by both providers.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The instructions describe the agent's real, confined capability set truthfully: it has
    ///     only the file tools over the locations the policy granted, and specifically has no shell,
    ///     terminal, or web access. Stating this is not what enforces it — the adapters and policy
    ///     do — but it keeps the model from inventing capabilities it will only be refused, and it
    ///     lets a user verify suppression by simply asking the agent what it can do.
    ///     </para>
    ///     <para>
    ///     <b>The instructions name the locations, which is why they are built per run rather than
    ///     held as a constant.</b> An application knows its own locations — this one resolves both
    ///     before it composes anything — so stating them is the honest and robust thing to do.
    ///     Withholding them and relying on the agent to discover them was tried first and is not
    ///     sound: a no-argument <c>text_file_list</c> reports only those permitted locations that
    ///     currently contain a matching file, so a granted location that is empty contributes no
    ///     block and is absent from the listing entirely. On a first run the session folder has just
    ///     been created and is therefore empty, which is exactly when the agent most needs its path;
    ///     an agent told only to discover then never learns it, guesses a relative name, and has the
    ///     guess joined to the workspace. This is an observed limitation of
    ///     <see cref="DemaConsulting.AgentKit.Tools.TextFile.TextFilePack"/>'s <c>text_file_list</c>
    ///     discovery listing, recorded as a known limitation in that unit's design documentation. It
    ///     is not fixed here, and this sample does not work around it beyond telling the truth up
    ///     front.
    ///     </para>
    ///     <para>
    ///     Discovery is still requested, because it is genuinely useful and it demonstrates the
    ///     discovery dialect — it simply is no longer the only way the agent can learn where it may
    ///     write. The relative-versus-absolute guidance is unchanged and follows from the policy:
    ///     the workspace is the anchor, so it is addressed by bare relative names, while the session
    ///     folder lies outside the anchor and can only be addressed absolutely.
    ///     </para>
    /// </remarks>
    /// <param name="workspaceRoot">The absolute workspace path named in the instructions.</param>
    /// <param name="sessionRoot">The absolute session path named in the instructions.</param>
    /// <param name="workspaceReadOnly">Whether the workspace is granted read-only rather than read-write.</param>
    /// <returns>The system instructions for this run.</returns>
    public static string BuildInstructions(
        string workspaceRoot,
        string sessionRoot,
        bool workspaceReadOnly)
    {
        var workspaceAccess = workspaceReadOnly ? "read-only" : "read-write";

        return
            "You are a document assistant with access to a small, fixed set of locations and " +
            "nothing else. There are exactly two of them. The workspace is '" + workspaceRoot +
            "' (" + workspaceAccess + "); it is what relative names are interpreted against. The " +
            "session folder is '" + sessionRoot + "' (read-write); it lies outside the workspace, " +
            "so only its full absolute path reaches it. You can read text files, list files, write " +
            "text files, and (when a vision tool is offered) look at images — but only within " +
            "those two locations, and only where you have write access. A path outside them will " +
            "be refused, and that is by design. You have no shell, terminal, code-execution, or " +
            "web/fetch tool; do not claim otherwise. " +
            "To see what files exist, call text_file_list with no directory argument: it reports " +
            "the locations it found files in, each as an absolute path with its files beneath it. " +
            "A location it does not report is not a location you lack — an empty one simply has " +
            "nothing to list. Read files from the workspace using the plain relative names the " +
            "listing shows for it (for example 'welcome.txt'). Write new files into the session " +
            "folder using its full absolute path exactly as given above — a relative name is " +
            "always interpreted against the workspace, so it will not reach any other location. " +
            "When a tool refuses a request, read the refusal: it echoes what you asked for, states " +
            "how it was interpreted, and lists every permitted location with its access level, " +
            "which is enough to reissue the request correctly. Do not retry the identical call, " +
            "and do not invent a path.";
    }

    /// <summary>
    ///     Composes the guarded tool set for the two locations, gating the image family on vision.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The whole location model of the sample is the few lines that build the policy. The
    ///     workspace is passed as the <em>working directory</em>, which makes it the anchor for
    ///     relative paths and nothing more; it is then granted separately, exactly like any other
    ///     location, at whichever access level the application chose. The session folder is granted
    ///     read-write and is never an anchor. Nothing is implied: an ungranted working directory
    ///     would be equally valid and would simply have its relative paths denied.
    ///     </para>
    ///     <para>
    ///     Note what <paramref name="workspaceReadOnly"/> does and does not change. It changes the
    ///     workspace grant's access level, so a write there is refused. It does not change
    ///     addressing: the workspace is still granted, so relative names are still emitted for
    ///     results inside it.
    ///     </para>
    ///     <para>
    ///     The image pack is added only when vision is enabled, and the <c>Vision</c> capability is
    ///     declared only then too. Because a pack whose required capabilities are not declared is
    ///     never asked to create its tools, omitting the declaration is sufficient on its own — but
    ///     omitting the pack as well makes the intent obvious to a reader. The result is materialized
    ///     into an <see cref="IList{T}"/> because the builder returns a read-only list while the
    ///     agent factories take a mutable one.
    ///     </para>
    /// </remarks>
    /// <param name="workspaceRoot">
    ///     The absolute workspace path: the working directory relative paths anchor to, and a
    ///     granted location.
    /// </param>
    /// <param name="sessionRoot">
    ///     The absolute session path the agent may write artifacts to. Granted read-write; never an
    ///     anchor, so it is always addressed absolutely.
    /// </param>
    /// <param name="workspaceReadOnly">
    ///     Whether the workspace grant is read-only rather than read-write. Affects permission only.
    /// </param>
    /// <param name="visionEnabled">Whether to offer the image tool and declare the vision capability.</param>
    /// <returns>The tool list to hand to a provider factory.</returns>
    public static IList<AIFunction> BuildTools(
        string workspaceRoot,
        string sessionRoot,
        bool workspaceReadOnly,
        bool visionEnabled)
    {
        // One policy governs the whole tool set. The workspace is the working directory relative
        // paths anchor to; the grants below are what actually permit anything. Granting the anchor
        // explicitly is the model: the working directory carries no permission on its own, and a
        // grant carries no addressing meaning.
        var grants = workspaceReadOnly
            ? new[] { PathRule.ReadOnly(workspaceRoot), PathRule.ReadWrite(sessionRoot) }
            : new[] { PathRule.ReadWrite(workspaceRoot), PathRule.ReadWrite(sessionRoot) };

        var policy = new PathPolicy(workspaceRoot, grants);

        var builder = new ToolPackBuilder(policy).Add(new TextFilePack());

        // Vision is a host/user capability, not a provider trait: declaring it adds the image pack
        // for either provider, and withholding it removes image_read from either provider.
        if (visionEnabled)
        {
            builder = builder
                .WithHostCapabilities(HostCapabilities.Vision)
                .Add(new ImagePack());
        }

        return [.. builder.Build()];
    }

    /// <summary>
    ///     Builds the provider agent — the single provider-selection switch in the whole sample.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The tool set is composed identically for both branches before the switch, so the only
    ///     provider-specific work is choosing a factory and, for Copilot, starting the client. Each
    ///     branch returns the built agent together with a cleanup handle, absorbing the lifetime
    ///     difference (async-disposable client vs. disposable HTTP client) at the boundary so no
    ///     caller has to know it.
    ///     </para>
    ///     <para>
    ///     The Copilot client's working directory is set to the workspace as defense in depth; the
    ///     tools are already confined by the policy regardless of where the runtime is rooted.
    ///     </para>
    /// </remarks>
    /// <param name="options">The validated command-line options selecting the provider and model.</param>
    /// <param name="workspaceRoot">The absolute workspace path: the anchor, and a granted location.</param>
    /// <param name="sessionRoot">The absolute session path, granted read-write for agent artifacts.</param>
    /// <param name="cancellationToken">Cancels a slow Copilot start.</param>
    /// <returns>The agent and its cleanup handle.</returns>
    public static async Task<AgentSetup> CreateAgentAsync(
        CommandLineOptions options,
        string workspaceRoot,
        string sessionRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var tools = BuildTools(
            workspaceRoot,
            sessionRoot,
            options.WorkspaceReadOnly,
            options.VisionEnabled);

        // The instructions name this run's two locations, so they are built alongside the tools
        // from the same three facts the policy was built from.
        var instructions = BuildInstructions(workspaceRoot, sessionRoot, options.WorkspaceReadOnly);

        // The one and only place the sample branches on provider. Everything the caller does with
        // the returned AgentSetup is identical for both.
        return options.Provider switch
        {
            AgentProvider.Copilot => await CreateCopilotAgentAsync(
                workspaceRoot,
                tools,
                instructions,
                cancellationToken),
            AgentProvider.Ollama => CreateOllamaAgent(options, tools, instructions),
            _ => throw new CommandLineException($"Unsupported provider '{options.Provider}'."),
        };
    }

    /// <summary>
    ///     Builds an agent on the GitHub Copilot runtime, which the host owns and disposes.
    /// </summary>
    /// <remarks>
    ///     The client is started here but built into the agent with <c>ownsClient: false</c> by the
    ///     factory, so this method returns the client as the cleanup handle: whoever created it
    ///     disposes it. The Copilot adapter suppresses the runtime's built-in tools by deriving the
    ///     session allow-list from exactly these tools.
    /// </remarks>
    /// <param name="workspaceRoot">The workspace set as the client's working directory.</param>
    /// <param name="tools">The composed tool set; also the source of the suppression allow-list.</param>
    /// <param name="instructions">The system instructions built for this run.</param>
    /// <param name="cancellationToken">Cancels a slow start.</param>
    /// <returns>The Copilot agent and the client as its cleanup handle.</returns>
    private static async Task<AgentSetup> CreateCopilotAgentAsync(
        string workspaceRoot,
        IList<AIFunction> tools,
        string instructions,
        CancellationToken cancellationToken)
    {
        // The host constructs, starts, and disposes the client; the factory takes no ownership.
        var client = new CopilotClient(new CopilotClientOptions
        {
            WorkingDirectory = workspaceRoot,
            UseLoggedInUser = true,
        });

        try
        {
            await client.StartAsync(cancellationToken);
        }
        catch
        {
            // Do not leak a partially started client if StartAsync (or cancellation) fails.
            await client.DisposeAsync();
            throw;
        }

        var agent = CopilotAgentFactory.Create(client, tools, instructions, name: AgentName);
        return new AgentSetup(agent, client);
    }

    /// <summary>
    ///     Builds an agent on an Ollama server reached over HTTP.
    /// </summary>
    /// <remarks>
    ///     A generous timeout is set because a first vision request to a cold model can be slow, and
    ///     a premature timeout would masquerade as a tool or model failure. The
    ///     <see cref="ChatClientAgentFactory"/> installs the image-promoting decorator unconditionally,
    ///     so a tool-returned image reaches the model rather than being dropped at the wire.
    /// </remarks>
    /// <param name="options">The options carrying the Ollama host and model.</param>
    /// <param name="tools">The composed tool set.</param>
    /// <param name="instructions">The system instructions built for this run.</param>
    /// <returns>The Ollama agent and a cleanup handle disposing the HTTP and API clients.</returns>
    private static AgentSetup CreateOllamaAgent(
        CommandLineOptions options,
        IList<AIFunction> tools,
        string instructions)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri(options.Host),
            Timeout = TimeSpan.FromMinutes(10),
        };

        var ollama = new OllamaApiClient(http, options.Model);
        IChatClient chatClient = ollama;

        var agent = ChatClientAgentFactory.Create(chatClient, tools, instructions, name: AgentName);

        // The OllamaApiClient does not own the HttpClient it was handed, so dispose both; disposal
        // order (client then transport) is the safe one.
        var cleanup = new AsyncDisposableAction(() =>
        {
            ollama.Dispose();
            http.Dispose();
            return ValueTask.CompletedTask;
        });

        return new AgentSetup(agent, cleanup);
    }

    /// <summary>
    ///     An <see cref="IAsyncDisposable"/> that runs a supplied action once on disposal.
    /// </summary>
    /// <remarks>
    ///     Used to present a disposable (the Ollama HTTP client) through the same async-disposable
    ///     handle the Copilot client already satisfies, so the host's cleanup is uniform across
    ///     providers.
    /// </remarks>
    private sealed class AsyncDisposableAction : IAsyncDisposable
    {
        /// <summary>
        ///     The disposal action, cleared after it runs so a double-dispose is a no-op.
        /// </summary>
        private Func<ValueTask>? _action;

        /// <summary>
        ///     Creates a handle that runs <paramref name="action"/> the first time it is disposed.
        /// </summary>
        /// <param name="action">The cleanup to run on disposal. Must not be <see langword="null"/>.</param>
        public AsyncDisposableAction(Func<ValueTask> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            _action = action;
        }

        /// <summary>
        ///     Runs the cleanup action once; subsequent calls do nothing.
        /// </summary>
        /// <returns>A task that completes when the action has run.</returns>
        public ValueTask DisposeAsync()
        {
            var action = _action;
            _action = null;
            return action?.Invoke() ?? ValueTask.CompletedTask;
        }
    }
}
