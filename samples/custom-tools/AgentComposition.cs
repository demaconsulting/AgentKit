using DemaConsulting.AgentKit.Agents.ChatClient;
using DemaConsulting.AgentKit.Agents.Copilot;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using GitHub.Copilot;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace DemaConsulting.AgentKit.Samples.CustomTools;

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
///     Composes the tool set — one shipped pack plus the two author-written custom packs — and
///     builds the provider agent, the one place in the sample where the choice of provider is
///     visible.
/// </summary>
/// <remarks>
///     <para>
///     This type embodies the sample's governing principle, the same one the document-assistant
///     sample follows: <b>no provider-specific logic beyond choosing which factory to call.</b> The
///     tool set — including the custom <c>markdown</c> and <c>clock</c> packs — is composed once,
///     identically, for both providers; only <see cref="CreateAgentAsync"/> branches, and it
///     branches solely to pick a factory and hand back a uniform cleanup handle.
///     </para>
///     <para>
///     The point the sample makes is that <b>an author-written pack composes exactly like a shipped
///     one.</b> <see cref="MarkdownToolPack"/> and <see cref="ClockToolPack"/> are added to the same
///     <see cref="ToolPackBuilder"/> as <see cref="TextFilePack"/>, on the same policy, before the
///     provider switch. The builder verifies each pack's tool names carry its declared family
///     prefix, so a malformed custom pack fails at composition rather than at a model's call.
///     </para>
/// </remarks>
public static class AgentComposition
{
    /// <summary>
    ///     The name every constructed agent carries, so tool-call transcripts and any provider-side
    ///     logging identify the sample.
    /// </summary>
    private const string AgentName = "custom-tools";

    /// <summary>
    ///     Builds the system instructions handed to the agent, naming the workspace and the custom
    ///     tools it now carries. Shared verbatim by both providers.
    /// </summary>
    /// <remarks>
    ///     The instructions describe the agent's real capability set truthfully: the shipped
    ///     text-file tools plus the two author-written tools, over the one granted workspace, and no
    ///     shell or web access. Stating this is not what enforces it — the policy and adapters do —
    ///     but it keeps the model from inventing capabilities it will only be refused.
    /// </remarks>
    /// <param name="workspaceRoot">The absolute workspace path named in the instructions.</param>
    /// <returns>The system instructions for this run.</returns>
    public static string BuildInstructions(string workspaceRoot)
    {
        return
            "You are an assistant demonstrating author-written AgentKit tools. Your workspace is '" +
            workspaceRoot + "' (read-write); it is what relative names are interpreted against. You " +
            "have these tools and nothing else: 'markdown_sections', a custom tool that lists the " +
            "headings of a Markdown file in the workspace with their line numbers; 'clock_now', a " +
            "custom tool that reports the current local and UTC time and takes no arguments; and the " +
            "shipped text-file tools 'text_file_read', 'text_file_write', and 'text_file_list'. Read " +
            "and list files using plain relative names within the workspace (for example " +
            "'sample.md'). A path outside the workspace will be refused, and that is by design. You " +
            "have no shell, terminal, code-execution, or web/fetch tool; do not claim otherwise. " +
            "When a tool refuses a request, read the refusal: it explains why and, where useful, " +
            "which tool to use instead. Do not retry the identical call, and do not invent a path.";
    }

    /// <summary>
    ///     Composes the tool set: the shipped text-file pack plus the two author-written custom
    ///     packs, all governed by one policy over the workspace.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     One <see cref="PathPolicy"/> governs every path-taking tool. The workspace is passed as
    ///     the working directory — the anchor for relative paths — and granted read-write, exactly
    ///     as the document-assistant sample grants its workspace. Because the working directory is
    ///     granted, the custom Markdown tool reports workspace results as bare relative names,
    ///     mirroring the shipped tools' dialect.
    ///     </para>
    ///     <para>
    ///     The custom packs are added to the same builder as the shipped pack. The clock pack needs
    ///     no policy — its tool takes no path — but it is composed through the identical contract,
    ///     which is the sample's central point: a pack is a pack, whether or not its tools consult
    ///     the policy. The result is materialized into an <see cref="IList{T}"/> because the builder
    ///     returns a read-only list while the agent factories take a mutable one.
    ///     </para>
    /// </remarks>
    /// <param name="workspaceRoot">
    ///     The absolute workspace path: the working directory relative paths anchor to, and a
    ///     granted read-write location.
    /// </param>
    /// <returns>The tool list to hand to a provider factory.</returns>
    public static IList<AIFunction> BuildTools(string workspaceRoot)
    {
        // One policy governs the whole path-taking tool set. The workspace is the working directory
        // relative paths anchor to; the read-write grant is what actually permits anything.
        var policy = new PathPolicy(workspaceRoot, [PathRule.ReadWrite(workspaceRoot)]);

        // A shipped pack and the two author-written packs are added to the same builder, on the
        // same policy. Build verifies each tool name carries its pack's declared family prefix.
        var builder = new ToolPackBuilder(policy)
            .Add(new TextFilePack())
            .Add(new MarkdownToolPack())
            .Add(new ClockToolPack());

        return [.. builder.Build()];
    }

    /// <summary>
    ///     Builds the provider agent — the single provider-selection switch in the whole sample.
    /// </summary>
    /// <remarks>
    ///     The tool set is composed identically for both branches before the switch, so the only
    ///     provider-specific work is choosing a factory and, for Copilot, starting the client. Each
    ///     branch returns the built agent together with a cleanup handle, absorbing the lifetime
    ///     difference at the boundary so no caller has to know it.
    /// </remarks>
    /// <param name="options">The validated command-line options selecting the provider and model.</param>
    /// <param name="workspaceRoot">The absolute workspace path: the anchor, and a granted location.</param>
    /// <param name="cancellationToken">Cancels a slow Copilot start.</param>
    /// <returns>The agent and its cleanup handle.</returns>
    public static async Task<AgentSetup> CreateAgentAsync(
        CommandLineOptions options,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var tools = BuildTools(workspaceRoot);
        var instructions = BuildInstructions(workspaceRoot);

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
    ///     factory, so this method returns the client as the cleanup handle. The Copilot adapter
    ///     suppresses the runtime's built-in tools by deriving the session allow-list from exactly
    ///     these tools — so the agent is offered only the shipped and custom tools composed above.
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
    ///     A generous timeout is set because a first request to a cold model can be slow, and a
    ///     premature timeout would masquerade as a tool or model failure.
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
