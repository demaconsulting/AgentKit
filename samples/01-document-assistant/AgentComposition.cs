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
///     paths at the workspace folder and grants that same folder read-write, so every read, write,
///     and listing is confined to it; the image pack is gated behind the
///     <see cref="HostCapabilities.Vision"/> declaration, so with vision off <c>image_read</c> is
///     never even created; and each adapter suppresses or repairs its provider's own quirk (Copilot's
///     built-in shell/fetch tools, an <see cref="IChatClient"/> dropping tool-returned images). None
///     of that is reimplemented here — it lives inside the shipped packages.
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
    ///     The system instructions handed to the agent, shared verbatim by both providers.
    /// </summary>
    /// <remarks>
    ///     The instructions describe the agent's real, confined capability set truthfully: it has
    ///     only the workspace file tools, and specifically has no shell, terminal, or web access.
    ///     Stating this is not what enforces it — the adapters and policy do — but it keeps the model
    ///     from inventing capabilities it will only be refused, and it lets a user verify suppression
    ///     by simply asking the agent what it can do.
    /// </remarks>
    private const string Instructions =
        "You are a document assistant confined to a single workspace folder. You can read text " +
        "files, list the files in the workspace, and (when a vision tool is offered) look at " +
        "images, but only within that workspace — a path outside it will be refused, and that is " +
        "by design. You have no shell, terminal, code-execution, or web/fetch tool; do not claim " +
        "otherwise. When a tool refuses a request, read the refusal: it usually names the correct " +
        "way to make the request, such as using an image tool for an image or a path inside the " +
        "workspace. Prefer relative paths as a user would type them (for example 'welcome.txt').";

    /// <summary>
    ///     Composes the guarded tool set for a workspace, gating the image family on vision.
    /// </summary>
    /// <remarks>
    ///     The image pack is added only when vision is enabled, and the <c>Vision</c> capability is
    ///     declared only then too. Because a pack whose required capabilities are not declared is
    ///     never asked to create its tools, omitting the declaration is sufficient on its own — but
    ///     omitting the pack as well makes the intent obvious to a reader. The result is materialized
    ///     into an <see cref="IList{T}"/> because the builder returns a read-only list while the
    ///     agent factories take a mutable one.
    /// </remarks>
    /// <param name="workspaceRoot">The absolute workspace path the policy confines every tool to.</param>
    /// <param name="visionEnabled">Whether to offer the image tool and declare the vision capability.</param>
    /// <returns>The tool list to hand to a provider factory.</returns>
    public static IList<AIFunction> BuildTools(string workspaceRoot, bool visionEnabled)
    {
        // One policy governs the whole tool set. The workspace is the working directory relative
        // paths anchor to, and it is granted read-write so reads, writes, and listings are all
        // confined to it. Granting the anchor explicitly is the model: the working directory carries
        // no permission on its own.
        var policy = new PathPolicy(workspaceRoot, [PathRule.ReadWrite(workspaceRoot)]);

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
    /// <param name="workspaceRoot">The absolute workspace path the tools are confined to.</param>
    /// <param name="cancellationToken">Cancels a slow Copilot start.</param>
    /// <returns>The agent and its cleanup handle.</returns>
    public static async Task<AgentSetup> CreateAgentAsync(
        CommandLineOptions options,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var tools = BuildTools(workspaceRoot, options.VisionEnabled);

        // The one and only place the sample branches on provider. Everything the caller does with
        // the returned AgentSetup is identical for both.
        return options.Provider switch
        {
            AgentProvider.Copilot => await CreateCopilotAgentAsync(workspaceRoot, tools, cancellationToken),
            AgentProvider.Ollama => CreateOllamaAgent(options, tools),
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
    /// <param name="cancellationToken">Cancels a slow start.</param>
    /// <returns>The Copilot agent and the client as its cleanup handle.</returns>
    private static async Task<AgentSetup> CreateCopilotAgentAsync(
        string workspaceRoot,
        IList<AIFunction> tools,
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

        var agent = CopilotAgentFactory.Create(client, tools, Instructions, name: AgentName);
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
    /// <returns>The Ollama agent and a cleanup handle disposing the HTTP and API clients.</returns>
    private static AgentSetup CreateOllamaAgent(CommandLineOptions options, IList<AIFunction> tools)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri(options.Host),
            Timeout = TimeSpan.FromMinutes(10),
        };

        var ollama = new OllamaApiClient(http, options.Model);
        IChatClient chatClient = ollama;

        var agent = ChatClientAgentFactory.Create(chatClient, tools, Instructions, name: AgentName);

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
