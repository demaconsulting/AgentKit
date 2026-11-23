using DemaConsulting.AgentKit.Agents.ChatClient;
using DemaConsulting.AgentKit.Agents.Copilot;
using DemaConsulting.AgentKit.Agents.Ollama;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Markdown;
using DemaConsulting.AgentKit.Tools.TextFile;
using DemaConsulting.AgentKit.Tools.Visual.Diagram;
using DemaConsulting.AgentKit.Tools.Visual.Image;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace DemaConsulting.AgentKit.Samples.DocumentAssistant;

/// <summary>
///     A built agent paired with the handle that releases whatever runtime resources it owns.
/// </summary>
/// <remarks>
///     The two providers own different kinds of resource — a Copilot runtime is
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
///     never even created; the diagram pack follows the host instead of being gated by it, showing
///     the model each drawing where the host can see and describing it in words where it cannot;
///     and each adapter suppresses or repairs its provider's own quirk (Copilot's
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
    ///     before it composes anything — so stating them outright is the honest, robust design: it
    ///     removes a discovery round-trip and makes the session folder's absolute path available to
    ///     the agent immediately, on the very first turn, before it has listed anything. Discovery
    ///     and the named instructions are complementary rather than one compensating for a defect in
    ///     the other: a no-argument <c>file_list</c> reports every permitted location, including
    ///     one that is currently empty (rendered under its absolute header with a marker naming its
    ///     access level), so an agent could learn the empty session folder's path from discovery
    ///     alone — but naming it up front is simpler and saves the round-trip.
    ///     </para>
    ///     <para>
    ///     Discovery is still requested, because it is genuinely useful and it demonstrates the
    ///     discovery dialect — it simply is no longer the only way the agent can learn where it may
    ///     write. The relative-versus-absolute guidance is unchanged and follows from the policy:
    ///     the workspace is the anchor, so it is addressed by bare relative names, while the session
    ///     folder lies outside the anchor and can only be addressed absolutely.
    ///     </para>
    ///     <para>
    ///     <b>The figure-preparation sentences extend the create-in-the-session-folder thread
    ///     rather than opening a new one.</b> They sit immediately after it and before the
    ///     refusal-handling guidance, because they are the same fact applied to a second kind of
    ///     artifact: the session folder is the only location writable in both workspace modes, so
    ///     a <c>.png</c> destination stated as that folder's absolute path is the only destination
    ///     that always works. Naming the workspace instead would be wrong under
    ///     <c>--read-only-workspace</c>, and stating the extension up front means the agent learns
    ///     the rule from the instructions rather than only by being refused.
    ///     </para>
    ///     <para>
    ///     <b>The diagram paragraph is appended by the application, not by the pack.</b> When the
    ///     run offers the diagram tools, <paramref name="diagramInstructions"/> carries the composed
    ///     pack's own <see cref="DiagramPack.Instructions"/> — which follow its preview mode — and
    ///     this method appends it verbatim, followed by one sentence of the sample's own: the
    ///     document-diagramming workflow, which keeps the source as a <c>.mmd</c> file and saves the
    ///     picture as a <c>.png</c> in the session folder, for the same reason figures go there. The
    ///     pack never adds its paragraph anywhere itself, so a run without diagrams carries no
    ///     mention of them.
    ///     </para>
    /// </remarks>
    /// <param name="workspaceRoot">The absolute workspace path named in the instructions.</param>
    /// <param name="sessionRoot">The absolute session path named in the instructions.</param>
    /// <param name="workspaceReadOnly">Whether the workspace is granted read-only rather than read-write.</param>
    /// <param name="diagramInstructions">
    ///     The composed diagram pack's <see cref="DiagramPack.Instructions"/>, or
    ///     <see langword="null"/> when the run offers no diagram tools.
    /// </param>
    /// <returns>The system instructions for this run.</returns>
    public static string BuildInstructions(
        string workspaceRoot,
        string sessionRoot,
        bool workspaceReadOnly,
        string? diagramInstructions = null)
    {
        var workspaceAccess = workspaceReadOnly ? "read-only" : "read-write";

        var instructions =
            "You are a document assistant with access to a small, fixed set of locations and " +
            "nothing else. There are exactly two of them. The workspace is '" + workspaceRoot +
            "' (" + workspaceAccess + "); it is what relative names are interpreted against. The " +
            "session folder is '" + sessionRoot + "' (read-write); it lies outside the workspace, " +
            "so only its full absolute path reaches it. You can search and read text files, create " +
            "and edit them, list, copy, move and delete files of any type, create, rename and " +
            "delete whole directories, outline a Markdown " +
            "file's sections, and (when a vision tool is offered) look at images, draw a document " +
            "or diagram file as a picture with image_view, and cut a region " +
            "out of one — but only within " +
            "those two locations, and only where you have write access. A path outside them will " +
            "be refused, and that is by design. You have no shell, terminal, code-execution, or " +
            "web/fetch tool; do not claim otherwise. " +
            "To see what files exist, call file_list with no directory argument: it reports " +
            "every location you may read, each as an absolute path with its files beneath it — and " +
            "an empty location still appears, shown under its absolute path with a marker naming " +
            "its access level, so you always learn every location you may use. Read files from the " +
            "workspace using the plain relative names the " +
            "listing shows for it (for example 'welcome.txt'). text_file_read prefixes each line " +
            "with its 1-based number and a '| ' delimiter, which are not part of the file; to edit, " +
            "give text_file_replace the file's raw text, not the numbered prefixes. Use " +
            "text_file_write when a file's whole content should become the text you supply, and " +
            "text_file_replace when only part of it should change. Create new " +
            "files in the session " +
            "folder using its full absolute path exactly as given above — a relative name is " +
            "always interpreted against the workspace, so it will not reach any other location. " +
            "When an image holds the detail that matters, crop the region that matters into the " +
            "session folder with image_crop, giving it the destination's full absolute path " +
            "ending in '.png', then reference that produced file from the document you are " +
            "writing. Cropping with no destination shows you the region; cropping with one " +
            "produces a file you can point at. " +
            "When a tool refuses a request, read the refusal: it echoes what you asked for, states " +
            "how it was interpreted, and lists every permitted location with its access level, " +
            "which is enough to reissue the request correctly. Do not retry the identical call, " +
            "and do not invent a path.";

        if (diagramInstructions is null)
        {
            return instructions;
        }

        // The pack's paragraph, verbatim, then the sample's own workflow for diagramming a document.
        return instructions + " " + diagramInstructions + " " +
            "To diagram a document, read it first (markdown_outline shows a Markdown file's " +
            "sections), then write the diagram as Mermaid source into the session folder as " +
            "<name>.mmd, using its full absolute path, so the source can be edited later. Draw that " +
            "source with diagram_render, giving save_as the session folder's full absolute path " +
            "ending in <name>.png; check the result and fix the source, keeping the .mmd file in " +
            "step, until it is right. Then link the saved picture from the Markdown you write, for " +
            "example ![Diagram](<name>.png).";
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
    ///     <para>
    ///     The diagram pack is passed in rather than created here, because the same instance's
    ///     <see cref="DiagramPack.Instructions"/> go into the agent's instructions: one pack, so the
    ///     paragraph always describes the preview mode the tools were composed in. It needs no
    ///     capability declaration of its own — <see cref="CreateDiagramPack"/> chooses the preview
    ///     from the same host capabilities, so an image-preview pack only ever meets a builder that
    ///     declared <see cref="HostCapabilities.Vision"/>, and a text-preview pack requires nothing.
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
    /// <param name="hostCapabilities">
    ///     The host capabilities to declare. Derived from the selected model where the model was
    ///     chosen from a catalog; <see cref="HostCapabilities.Vision"/> offers the image tools.
    /// </param>
    /// <param name="diagrams">
    ///     The diagram pack to add, as <see cref="CreateDiagramPack"/> returns it, or
    ///     <see langword="null"/> to offer no diagram tools.
    /// </param>
    /// <returns>The tool list to hand to a provider factory.</returns>
    public static IList<AIFunction> BuildTools(
        string workspaceRoot,
        string sessionRoot,
        bool workspaceReadOnly,
        HostCapabilities hostCapabilities,
        DiagramPack? diagrams = null)
    {
        // One policy governs the whole tool set. The workspace is the working directory relative
        // paths anchor to; the grants below are what actually permit anything. Granting the anchor
        // explicitly is the model: the working directory carries no permission on its own, and a
        // grant carries no addressing meaning.
        var grants = workspaceReadOnly
            ? new[] { PathRule.ReadOnly(workspaceRoot), PathRule.ReadWrite(sessionRoot) }
            : new[] { PathRule.ReadWrite(workspaceRoot), PathRule.ReadWrite(sessionRoot) };

        var policy = new PathPolicy(workspaceRoot, grants);

        var builder = new ToolPackBuilder(policy)
            .Add(new TextFilePack())
            .Add(new FilePack())
            .Add(new MarkdownPack());

        // Vision is declared by the host - derived from the selected model where there is one - and
        // declaring it adds the image pack for either provider, while withholding it removes
        // image_read from either provider.
        if (hostCapabilities.HasFlag(HostCapabilities.Vision))
        {
            builder = builder
                .WithHostCapabilities(hostCapabilities)
                .Add(new ImagePack());
        }

        // The diagram pack, when the run offers one: image preview for a host that can see, text
        // preview - each drawing saved and described in words - for one that cannot.
        if (diagrams is not null)
        {
            builder = builder.Add(diagrams);
        }

        return [.. builder.Build()];
    }

    /// <summary>
    ///     Creates the diagram pack a run offers, with the preview mode its host can use.
    /// </summary>
    /// <remarks>
    ///     <see cref="DiagramPack.For"/> chooses image preview when the host declares
    ///     <see cref="HostCapabilities.Vision"/> and text preview otherwise, so <c>--no-vision</c>
    ///     keeps the diagram tools and only changes how a drawing is shown to the model.
    ///     <c>--no-diagrams</c> is the only thing that removes them.
    /// </remarks>
    /// <param name="hostCapabilities">The host capabilities the run declares.</param>
    /// <param name="diagramsEnabled">Whether the run offers the diagram tools at all.</param>
    /// <returns>The pack, or <see langword="null"/> when the run offers no diagram tools.</returns>
    public static DiagramPack? CreateDiagramPack(HostCapabilities hostCapabilities, bool diagramsEnabled) =>
        diagramsEnabled ? DiagramPack.For(hostCapabilities) : null;

    /// <summary>
    ///     The capabilities a model must report to run this sample.
    /// </summary>
    /// <remarks>
    ///     Every AgentKit agent calls tools; with vision on, the sample is a vision application and
    ///     offers only models that can see what <c>image_read</c> returns. The same requirement filters
    ///     the listing and is enforced again when the session starts.
    /// </remarks>
    /// <param name="options">The validated options.</param>
    /// <returns>The required capabilities.</returns>
    public static ModelCapabilities RequiredCapabilities(CommandLineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.VisionEnabled
            ? ModelCapabilities.Vision | ModelCapabilities.ToolCalls
            : ModelCapabilities.ToolCalls;
    }

    /// <summary>
    ///     Builds the model settings the command line names, as an application would load them from
    ///     its own settings store.
    /// </summary>
    /// <param name="options">The validated options.</param>
    /// <param name="modelId">The model the settings are for.</param>
    /// <returns>The settings.</returns>
    public static ModelSettings SettingsFor(CommandLineOptions options, string modelId)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new ModelSettings(modelId)
        {
            ReasoningEffort = options.ReasoningEffort,
            ContextTier = options.ContextTier,
            ContextWindowTokens = options.ContextWindow,
        };
    }

    /// <summary>
    ///     Writes the models that can run this sample, grouped by the provider's category, as a model
    ///     picker would show them.
    /// </summary>
    /// <param name="catalog">The provider's catalog.</param>
    /// <param name="options">The validated options, which decide the requirement.</param>
    /// <param name="output">Where to write the listing.</param>
    /// <param name="cancellationToken">Cancels the catalog read.</param>
    /// <returns>A task that completes when the listing is written.</returns>
    public static async Task WriteModelListAsync(
        IModelCatalog catalog,
        CommandLineOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(output);

        var models = await catalog.ListAsync(
            new ModelQuery { Require = RequiredCapabilities(options) },
            cancellationToken);

        // Grouped by the provider's category, in the provider's order within each group.
        foreach (var group in models.GroupBy(model => model.Category ?? "other"))
        {
            await output.WriteLineAsync($"{group.Key}:");
            foreach (var model in group)
            {
                var price = model.BillingMultiplier is { } multiplier ? $" x{multiplier:0.##}" : string.Empty;
                var window = model.ContextWindowTokens is { } tokens ? $" up to {tokens:N0} tokens" : string.Empty;
                await output.WriteLineAsync($"  {model.Id} ({model.DisplayName}){price}{window}");
                if (model.ReasoningEfforts.Count > 0)
                {
                    await output.WriteLineAsync($"    efforts: {string.Join(", ", model.ReasoningEfforts.Select(effort => effort.Id))}");
                }

                if (model.ContextTiers.Count > 0)
                {
                    await output.WriteLineAsync($"    tiers:   {string.Join(", ", model.ContextTiers.Select(tier => tier.WindowTokens is { } size ? $"{tier.Id} ({size:N0})" : tier.Id))}");
                }

                foreach (var notice in model.Notices)
                {
                    await output.WriteLineAsync($"    {notice.Kind}: {notice.Message}");
                }
            }
        }
    }

    /// <summary>
    ///     Builds the provider agent — the single provider-selection switch in the whole sample.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The tool set is composed identically for both branches before the switch, so the only
    ///     provider-specific work is choosing a factory and, for Copilot, starting the runtime. Each
    ///     branch returns the built agent together with a cleanup handle, absorbing the lifetime
    ///     difference (async-disposable runtime vs. disposable HTTP client) at the boundary so no
    ///     caller has to know it.
    ///     </para>
    ///     <para>
    ///     Each Copilot session's working directory is set to the workspace as defense in depth; the
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

        // The one and only place the sample branches on provider. Everything the caller does with
        // the returned AgentSetup is identical for both. Each branch selects its model through the
        // provider's catalog, which is where the host capabilities - and so the tools and the
        // instructions - come from.
        return options.Provider switch
        {
            AgentProvider.Copilot => await CreateCopilotAgentAsync(
                options,
                workspaceRoot,
                sessionRoot,
                cancellationToken),
            AgentProvider.Ollama => await CreateOllamaAgentAsync(
                options,
                workspaceRoot,
                sessionRoot,
                cancellationToken),
            _ => throw new CommandLineException($"Unsupported provider '{options.Provider}'."),
        };
    }

    /// <summary>
    ///     Composes the tools and the instructions for a run from the host capabilities its model
    ///     selection produced — identically for both providers.
    /// </summary>
    /// <remarks>
    ///     The tools and the instructions are composed together because the diagram pack contributes
    ///     to both: its tools go into the list, and its <see cref="DiagramPack.Instructions"/> are
    ///     appended to the instructions by the application. The instructions name this run's two
    ///     locations, so they are built from the same facts the policy is built from.
    /// </remarks>
    /// <param name="options">The validated options.</param>
    /// <param name="workspaceRoot">The absolute workspace path.</param>
    /// <param name="sessionRoot">The absolute session path.</param>
    /// <param name="hostCapabilities">The host capabilities the run declares.</param>
    /// <returns>The tool list and the system instructions.</returns>
    private static (IList<AIFunction> Tools, string Instructions) Compose(
        CommandLineOptions options,
        string workspaceRoot,
        string sessionRoot,
        HostCapabilities hostCapabilities)
    {
        var diagrams = CreateDiagramPack(hostCapabilities, options.DiagramsEnabled);
        var tools = BuildTools(workspaceRoot, sessionRoot, options.WorkspaceReadOnly, hostCapabilities, diagrams);
        var instructions = BuildInstructions(
            workspaceRoot,
            sessionRoot,
            options.WorkspaceReadOnly,
            diagrams?.Instructions);
        return (tools, instructions);
    }

    /// <summary>
    ///     Lists the models that can run this sample on the chosen provider.
    /// </summary>
    /// <param name="options">The validated options.</param>
    /// <param name="output">Where to write the listing.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes when the listing is written.</returns>
    public static async Task ListModelsAsync(CommandLineOptions options, TextWriter output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Provider == AgentProvider.Copilot)
        {
            await using var runtime = await StartCopilotRuntimeAsync(cancellationToken);
            await WriteModelListAsync(new CopilotModelCatalog(runtime), options, output, cancellationToken);
            return;
        }

        using var http = new HttpClient { BaseAddress = new Uri(options.Host), Timeout = TimeSpan.FromMinutes(2) };
        using var ollama = new OllamaApiClient(http);
        await WriteModelListAsync(new OllamaModelCatalog(ollama), options, output, cancellationToken);
    }

    /// <summary>
    ///     Validates the command line's model choice against a catalog, failing with every problem
    ///     as a usage error.
    /// </summary>
    /// <param name="catalog">The provider's catalog.</param>
    /// <param name="options">The validated options.</param>
    /// <param name="modelId">The model to select.</param>
    /// <param name="cancellationToken">Cancels the catalog read.</param>
    /// <returns>The validated selection.</returns>
    /// <exception cref="CommandLineException">The model is unavailable or the settings are invalid for it.</exception>
    private static async Task<ModelSelection> SelectAsync(
        IModelCatalog catalog,
        CommandLineOptions options,
        string modelId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await catalog.SelectAsync(SettingsFor(options, modelId), RequiredCapabilities(options), cancellationToken);
        }
        catch (ModelSettingsException invalid)
        {
            throw new CommandLineException(
                string.Join(Environment.NewLine, invalid.Problems)
                + Environment.NewLine + "Run with --list-models to see the models and settings available.");
        }
    }

    /// <summary>
    ///     Starts the Copilot runtime this sample owns: the installed Copilot CLI, AgentKit's default.
    /// </summary>
    /// <param name="cancellationToken">Cancels a slow start.</param>
    /// <returns>The started runtime, which the caller disposes.</returns>
    private static Task<CopilotRuntime> StartCopilotRuntimeAsync(CancellationToken cancellationToken) =>
        CopilotRuntime.StartAsync(
            new CopilotRuntimeOptions(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgentKit",
                "document-assistant",
                "copilot-runtime")),
            cancellationToken);

    /// <summary>
    ///     Builds an agent on the GitHub Copilot runtime, which the host owns and disposes.
    /// </summary>
    /// <remarks>
    ///     The runtime is started here and never disposed by the agent the factory builds, so this
    ///     method returns the runtime as the cleanup handle: whoever started it disposes it. The Copilot adapter suppresses the runtime's built-in tools by deriving the
    ///     session allow-list from exactly these tools.
    ///     <para>
    ///     The model is passed through the adapter rather than configured around it. Reaching the
    ///     session's model setting by building the session directly would mean forgoing the
    ///     adapter's suppression and its default-safe permission handler — an agent that looks
    ///     configured but denies every tool call.
    ///     </para>
    /// </remarks>
    /// <param name="options">The validated options carrying the model choice.</param>
    /// <param name="workspaceRoot">The workspace set as each session's working directory.</param>
    /// <param name="sessionRoot">The session folder, granted read-write.</param>
    /// <param name="cancellationToken">Cancels a slow start.</param>
    /// <returns>The Copilot agent and the runtime as its cleanup handle.</returns>
    private static async Task<AgentSetup> CreateCopilotAgentAsync(
        CommandLineOptions options,
        string workspaceRoot,
        string sessionRoot,
        CancellationToken cancellationToken)
    {
        // AgentKit starts the runtime itself - the Copilot CLI installed on the machine, found and
        // version-checked by AgentKit (pass runtimeSource: CopilotRuntimeSource.Bundled, with a direct
        // Microsoft.Copilot.SDK reference, to run the SDK's bundled runtime instead), in Empty mode,
        // the shell's COPILOT_/GITHUB_COPILOT_ variables withheld apart from an allow-listed
        // credential, network and telemetry-off set, a data directory AgentKit owns - and the host
        // disposes it; the factory takes no ownership. A per-user application-data folder keeps that
        // state out of the workspace. A start that fails or is canceled has already released the
        // runtime.
        var runtime = await StartCopilotRuntimeAsync(cancellationToken);

        try
        {
            // A named model is selected through the catalog, which validates the effort and tier and
            // tells the host whether the model can see. With no model named the runtime chooses,
            // nothing describes its choice, and the host declares vision explicitly - the override a
            // catalog never takes away. A stated window was refused when the arguments were parsed.
            ModelSelection? selection = null;
            if (options.Model is { } model)
            {
                selection = await SelectAsync(new CopilotModelCatalog(runtime), options, model, cancellationToken);
            }
            else if (options.ReasoningEffort is not null || options.ContextTier is not null)
            {
                throw new CommandLineException("--reasoning-effort and --context-tier need --model on copilot.");
            }

            var host = selection?.HostCapabilities ?? HostCapabilities.Vision;
            var (tools, instructions) = Compose(
                options,
                workspaceRoot,
                sessionRoot,
                options.VisionEnabled ? host : HostCapabilities.None);

            var agent = CopilotAgentFactory.Create(
                runtime,
                tools,
                instructions,
                name: AgentName,
                workingDirectory: workspaceRoot,
                selection: selection);
            return new AgentSetup(agent, runtime);
        }
        catch
        {
            // Do not leak a started runtime if the agent cannot be built.
            await runtime.DisposeAsync();
            throw;
        }
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
    /// <param name="workspaceRoot">The workspace: the anchor, and a granted location.</param>
    /// <param name="sessionRoot">The session folder, granted read-write.</param>
    /// <param name="cancellationToken">Cancels the catalog read.</param>
    /// <returns>The Ollama agent and a cleanup handle disposing the HTTP and API clients.</returns>
    private static async Task<AgentSetup> CreateOllamaAgentAsync(
        CommandLineOptions options,
        string workspaceRoot,
        string sessionRoot,
        CancellationToken cancellationToken)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri(options.Host),
            Timeout = TimeSpan.FromMinutes(10),
        };

        // Ollama needs a concrete model name on the wire, so an unstated --model resolves to the
        // sample's own default here rather than being pre-filled on the options — see the remarks
        // on CommandLineOptions.Model for why that distinction matters to the Copilot branch.
        var ollama = new OllamaApiClient(http, options.Model ?? CommandLineOptions.DefaultOllamaModel);

        // The OllamaApiClient does not own the HttpClient it was handed, so dispose both; disposal
        // order (client then transport) is the safe one.
        var cleanup = new AsyncDisposableAction(() =>
        {
            ollama.Dispose();
            http.Dispose();
            return ValueTask.CompletedTask;
        });

        // The model is selected through the server's catalog: a model that cannot see is refused
        // while vision is on, and an effort or a window above the trained length is refused here
        // rather than by the server mid-conversation. The tools follow the selected model.
        ModelSelection selection;
        try
        {
            selection = await SelectAsync(new OllamaModelCatalog(ollama), options, ollama.SelectedModel, cancellationToken);
        }
        catch
        {
            // Do not leak the transport when the choice is refused.
            await cleanup.DisposeAsync();
            throw;
        }

        var (tools, instructions) = Compose(
            options,
            workspaceRoot,
            sessionRoot,
            options.VisionEnabled ? selection.HostCapabilities : HostCapabilities.None);

        // A stated window is asked of the server on every request, and the agent factory confirms the
        // selection's window is the one asked for; with none stated the server keeps its own, rather
        // than the sample guessing one.
        IChatClient chatClient = options.ContextWindow is { } window
            ? new OllamaContextSizingChatClient(ollama, window)
            : ollama;

        var agent = ChatClientAgentFactory.Create(chatClient, tools, instructions, name: AgentName, selection: selection);

        return new AgentSetup(agent, cleanup);
    }

    /// <summary>
    ///     An <see cref="IAsyncDisposable"/> that runs a supplied action once on disposal.
    /// </summary>
    /// <remarks>
    ///     Used to present a disposable (the Ollama HTTP client) through the same async-disposable
    ///     handle the Copilot runtime already satisfies, so the host's cleanup is uniform across
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
