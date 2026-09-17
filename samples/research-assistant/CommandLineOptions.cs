namespace DemaConsulting.AgentKit.Samples.ResearchAssistant;

/// <summary>
///     The provider whose agent runtime the sample runs the conversation on.
/// </summary>
/// <remarks>
///     The provider is the <em>only</em> thing that changes how the agent is constructed. The tool
///     set — the task list, the memories, and delegation — is composed once and is identical on
///     either runtime, which is the point: an application never learns, and never needs to learn,
///     which backend answers it.
/// </remarks>
public enum AgentProvider
{
    /// <summary>
    ///     The GitHub Copilot SDK runtime, used through the logged-in Copilot CLI or an explicit
    ///     GitHub token. Ignores <see cref="CommandLineOptions.Host"/>; honors
    ///     <see cref="CommandLineOptions.Model"/>.
    /// </summary>
    Copilot,

    /// <summary>
    ///     Any Ollama server reached over HTTP, addressed by <see cref="CommandLineOptions.Host"/>
    ///     and <see cref="CommandLineOptions.Model"/>.
    /// </summary>
    Ollama,
}

/// <summary>
///     The backend the sample turns a memory descriptor into a vector with.
/// </summary>
/// <remarks>
///     <para>
///     <b>This choice belongs to the application, not to AgentKit.</b> <c>MemoryPack</c> takes an
///     <c>IEmbeddingGenerator</c> and never inspects it, so the whole of this enumeration lives in
///     the sample. The sample offers two so that a reader can see both halves of that statement:
///     an entirely offline generator the sample itself implements, and a real model served by
///     Ollama, swapped by one flag with no other change anywhere.
///     </para>
/// </remarks>
public enum EmbeddingBackend
{
    /// <summary>
    ///     The sample's own offline <see cref="LexicalEmbeddingGenerator"/>. Needs no server, no
    ///     credential, and no downloaded model, so the sample runs anywhere — but it measures
    ///     shared wording rather than shared meaning. See that type's remarks for what that costs.
    /// </summary>
    Local,

    /// <summary>
    ///     An embedding model served by Ollama, addressed by <see cref="CommandLineOptions.Host"/>
    ///     and <see cref="CommandLineOptions.EmbeddingModel"/>. A real semantic embedding, and what
    ///     a production application would use.
    /// </summary>
    Ollama,
}

/// <summary>
///     The parsed command-line configuration for one run of the research assistant.
/// </summary>
/// <remarks>
///     <para>
///     The parser accepts <b>named flags only</b>, for the same reason the document-assistant
///     sample does: every option here describes something the reader must be able to see — which
///     locations were granted, which embedding backend produced the vectors, and which runtime
///     answered — and a positional argument would blur one of them into the prompt text.
///     </para>
///     <para>
///     This type only holds and validates configuration; it constructs nothing and performs no I/O,
///     so it is trivially testable and safe to reason about in isolation.
///     </para>
/// </remarks>
public sealed class CommandLineOptions
{
    /// <summary>
    ///     The default Ollama host used when <c>--host</c> is not supplied.
    /// </summary>
    /// <remarks>
    ///     Ollama's own default listening address, so a developer running Ollama locally needs only
    ///     <c>--provider ollama</c>. The default is plain HTTP because Ollama serves HTTP; the
    ///     S5332 "use HTTPS" analyzer rule is suppressed for this line for that reason.
    /// </remarks>
#pragma warning disable S5332 // Ollama serves plain HTTP on the loopback interface; HTTPS is not available.
    public const string DefaultOllamaHost = "http://localhost:11434";
#pragma warning restore S5332

    /// <summary>
    ///     The default Ollama chat model used when <c>--model</c> is not supplied.
    /// </summary>
    /// <remarks>
    ///     Chosen because it calls tools reliably enough to plan, file memories and delegate, which
    ///     is the whole of what this sample asks a model to do.
    /// </remarks>
    public const string DefaultOllamaModel = "qwen3.5:9b";

    /// <summary>
    ///     The default Ollama embedding model used when <c>--embedding-model</c> is not supplied.
    /// </summary>
    /// <remarks>
    ///     A small, widely pulled embedding model. It is named separately from
    ///     <see cref="DefaultOllamaModel"/> because embedding and chat are different jobs served by
    ///     different models, and conflating them is the mistake this pair of flags exists to make
    ///     impossible to stumble into.
    /// </remarks>
    public const string DefaultOllamaEmbeddingModel = "nomic-embed-text";

    /// <summary>
    ///     The folder name, beneath the system temporary directory, the sample uses for notes when
    ///     <c>--notes</c> is not supplied.
    /// </summary>
    /// <remarks>
    ///     <b>This is the sample's choice, not an AgentKit convention.</b> A temporary-directory
    ///     default keeps the repository clean and puts the writable location unambiguously outside
    ///     the read-only corpus, so the asymmetry the sample demonstrates is real rather than
    ///     staged.
    /// </remarks>
    public const string DefaultNotesFolderName = "research-assistant-notes";

    /// <summary>
    ///     Gets the corpus directory the agent researches. Required.
    /// </summary>
    /// <remarks>
    ///     This path becomes the policy's working directory — the anchor a relative path a model
    ///     supplies resolves against — and is granted <b>read-only</b>: a research assistant reads
    ///     sources and writes conclusions elsewhere, so the corpus it cites cannot be edited by the
    ///     agent citing it.
    /// </remarks>
    public required string Corpus { get; init; }

    /// <summary>
    ///     Gets the notes directory the agent may write into, or <see langword="null"/> to use the
    ///     default beneath the system temporary directory. Set by <c>--notes</c>.
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    ///     Gets the provider whose runtime the agent runs on. Defaults to <see cref="AgentProvider.Copilot"/>.
    /// </summary>
    public AgentProvider Provider { get; init; } = AgentProvider.Copilot;

    /// <summary>
    ///     Gets the embedding backend the memory family uses. Defaults to
    ///     <see cref="EmbeddingBackend.Local"/> so the sample runs with nothing installed.
    /// </summary>
    public EmbeddingBackend Embeddings { get; init; } = EmbeddingBackend.Local;

    /// <summary>
    ///     Gets the Ollama host URL, used for the chat model, the embedding model, or both.
    ///     Defaults to <see cref="DefaultOllamaHost"/>.
    /// </summary>
    public string Host { get; init; } = DefaultOllamaHost;

    /// <summary>
    ///     Gets the model name requested with <c>--model</c>, or <see langword="null"/> when none
    ///     was requested and each provider's own default applies.
    /// </summary>
    /// <remarks>
    ///     Nullable rather than pre-filled, because Ollama needs a concrete model name on the wire
    ///     while the Copilot runtime picks its own — so the sample must be able to say
    ///     <em>nothing</em> on the Copilot branch.
    /// </remarks>
    public string? Model { get; init; }

    /// <summary>
    ///     Gets the Ollama embedding model name. Used only when <see cref="Embeddings"/> is
    ///     <see cref="EmbeddingBackend.Ollama"/>. Defaults to
    ///     <see cref="DefaultOllamaEmbeddingModel"/>.
    /// </summary>
    public string EmbeddingModel { get; init; } = DefaultOllamaEmbeddingModel;

    /// <summary>
    ///     Gets the model each context consolidation is sent to, or <see langword="null"/> to use
    ///     the conversation's own model. Set by <c>--summary-model</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Named separately because consolidation is summarization rather than reasoning, and a
    ///     smaller model is usually the right choice for it — the same reason the embedding model is
    ///     named separately from the chat model. It is also a different <em>conversation</em>: a
    ///     consolidation runs outside the session being compacted, so sending it to another model
    ///     changes nothing the agent can observe.
    ///     </para>
    ///     <para>
    ///     Applies on both providers, because both carry an AgentKit compacting session. On Ollama
    ///     an unstated value falls back to the conversation's model; on Copilot it falls back to
    ///     whichever model the runtime chooses by default.
    ///     </para>
    /// </remarks>
    public string? SummaryModel { get; init; }

    /// <summary>
    ///     Gets the context window the compacting session accounts against, or
    ///     <see langword="null"/> to read it from the provider. Set by <c>--context-window</c>;
    ///     Ollama only.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     An <c>IChatClient</c> publishes no context window, so AgentKit is told one once, where
    ///     the provider is configured, and answers with it thereafter. The sample reads it from
    ///     Ollama rather than asking for it — see <see cref="OllamaContextWindow"/> — and this flag
    ///     exists for the case the reading gets wrong: a server that has loaded the model with a
    ///     context length smaller than the model publishes, which nothing else reveals.
    ///     </para>
    ///     <para>
    ///     Unused on the Copilot runtime, which reports its occupancy and its limit with every turn.
    ///     There is nothing to state there and nothing a stated figure could do but disagree with the
    ///     provider's own.
    ///     </para>
    /// </remarks>
    public int? ContextWindow { get; init; }

    /// <summary>
    ///     Gets the GitHub token to authenticate the Copilot runtime with, or
    ///     <see langword="null"/> to use whatever the machine is already logged in as.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Set by <c>--github-token</c>, or defaulted from the <c>GH_TOKEN</c> or
    ///     <c>GITHUB_TOKEN</c> environment variable by <see cref="ResolveGitHubToken"/>. It exists
    ///     because an unattended run — a scheduled CI job exercising this sample against a live
    ///     model — has no logged-in user to borrow, and the alternative would be a sample that can
    ///     only ever be run by hand.
    ///     </para>
    ///     <para>
    ///     The value is never printed. The startup banner reports only <em>whether</em> a token was
    ///     supplied, because a token echoed into a CI log is a leaked credential.
    ///     </para>
    /// </remarks>
    public string? GitHubToken { get; init; }

    /// <summary>
    ///     Gets the path a machine-readable record of every tool call is appended to, or
    ///     <see langword="null"/> for none. Set by <c>--transcript</c>.
    /// </summary>
    /// <remarks>
    ///     The human transcript on the console is the sample's demonstration; this file is its
    ///     testable form. A live-model CI job asserts on the tool names in this file rather than on
    ///     the model's prose, because prose varies between runs and a tool name does not.
    /// </remarks>
    public string? Transcript { get; init; }

    /// <summary>
    ///     Gets the single prompt to run non-interactively, or <see langword="null"/> to run the
    ///     interactive loop. Set by <c>--prompt</c>, repeatable.
    /// </summary>
    /// <remarks>
    ///     Repeatable because this sample's subject is work that spans turns: a plan made in one
    ///     turn, findings filed in another, a contradiction resolved in a third. A single prompt
    ///     could not show a memory outliving the turn that filed it.
    /// </remarks>
    public IReadOnlyList<string> Prompts { get; init; } = [];

    /// <summary>
    ///     Gets the question answered from memory alone after every prompt has run, or
    ///     <see langword="null"/> for none. Set by <c>--recall-question</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>This option exists because the ordinary prompts cannot prove what they appear to.</b>
    ///     A final turn in the same session has the earlier turns' document contents in its
    ///     context, so an answer containing a fact from the corpus demonstrates nothing about
    ///     whether the memories were ever consulted — and in live runs the answering turn made no
    ///     <c>memory_recall</c> call at all, because it had no need to.
    ///     </para>
    ///     <para>
    ///     The question named here is run on a separate agent, in a fresh session, composed with
    ///     the memory family and no reading tool of any kind. Neither the conversation nor the
    ///     documents are available to it, so an answer can only have come from
    ///     <c>memory_recall</c>.
    ///     </para>
    /// </remarks>
    public string? RecallQuestion { get; init; }

    /// <summary>
    ///     Gets a value indicating whether delegation is offered. When <see langword="false"/> the
    ///     agent pack is omitted and the <c>Delegation</c> capability is not declared, so
    ///     <c>agent_run</c> is never created.
    /// </summary>
    /// <remarks>
    ///     Set by <c>--no-delegation</c>. Its purpose is capability gating: with delegation off the
    ///     tool is not refused at call time, it is never offered at all.
    /// </remarks>
    public bool DelegationEnabled { get; init; } = true;

    /// <summary>
    ///     Gets a value indicating whether help was requested and no run should occur.
    /// </summary>
    public bool HelpRequested { get; init; }

    /// <summary>
    ///     Parses command-line arguments into a validated <see cref="CommandLineOptions"/>.
    /// </summary>
    /// <remarks>
    ///     Parsing and validation are one step so an invalid combination is reported here with an
    ///     actionable message rather than surfacing later as a confusing failure while constructing
    ///     the agent. <c>--help</c> short-circuits all other validation.
    /// </remarks>
    /// <param name="args">The raw arguments as received by <c>Main</c>. Must not be <see langword="null"/>.</param>
    /// <returns>The parsed and validated options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
    /// <exception cref="CommandLineException">
    ///     An argument is unknown, a flag is missing its value, <c>--provider</c> or
    ///     <c>--embeddings</c> names something unrecognized, <c>--context-window</c> is not a
    ///     positive number, or the required <c>--corpus</c> is absent.
    /// </exception>
    public static CommandLineOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // A user asking for help gets it regardless of what else is (or is not) on the line.
        if (Array.Exists(args, arg => arg is "--help" or "-h" or "-?"))
        {
            return new CommandLineOptions { Corpus = string.Empty, HelpRequested = true };
        }

        string? corpus = null;
        string? notes = null;
        var provider = AgentProvider.Copilot;
        var embeddings = EmbeddingBackend.Local;
        var host = DefaultOllamaHost;
        string? model = null;
        var embeddingModel = DefaultOllamaEmbeddingModel;
        string? summaryModel = null;
        int? contextWindow = null;
        string? githubToken = null;
        string? transcript = null;
        string? recallQuestion = null;
        var delegationEnabled = true;
        var prompts = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--corpus":
                    corpus = TakeValue(args, ref index, arg);
                    break;

                case "--notes":
                    notes = TakeValue(args, ref index, arg);
                    break;

                case "--provider":
                    provider = ParseProvider(TakeValue(args, ref index, arg));
                    break;

                case "--embeddings":
                    embeddings = ParseEmbeddingBackend(TakeValue(args, ref index, arg));
                    break;

                case "--host":
                    host = TakeValue(args, ref index, arg);
                    break;

                case "--model":
                    model = TakeValue(args, ref index, arg);
                    break;

                case "--embedding-model":
                    embeddingModel = TakeValue(args, ref index, arg);
                    break;

                case "--summary-model":
                    summaryModel = TakeValue(args, ref index, arg);
                    break;

                case "--context-window":
                    contextWindow = ParseContextWindow(TakeValue(args, ref index, arg));
                    break;

                case "--github-token":
                    githubToken = TakeValue(args, ref index, arg);
                    break;

                case "--transcript":
                    transcript = TakeValue(args, ref index, arg);
                    break;

                case "--prompt":
                    prompts.Add(TakeValue(args, ref index, arg));
                    break;

                case "--recall-question":
                    recallQuestion = TakeValue(args, ref index, arg);
                    break;

                case "--no-delegation":
                    delegationEnabled = false;
                    break;

                default:
                    // An unrecognized token is a mistake, not something to silently ignore: a
                    // misspelled --corpus would otherwise run the agent over the wrong location.
                    throw new CommandLineException($"Unknown argument '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(corpus))
        {
            throw new CommandLineException(
                "The --corpus <path> argument is required; it is the folder of source documents the "
                + "assistant researches, granted read-only.");
        }

        return new CommandLineOptions
        {
            Corpus = corpus,
            Notes = notes,
            Provider = provider,
            Embeddings = embeddings,
            Host = host,
            Model = model,
            EmbeddingModel = embeddingModel,
            SummaryModel = summaryModel,
            ContextWindow = contextWindow,
            GitHubToken = githubToken,
            Transcript = transcript,
            Prompts = prompts,
            RecallQuestion = recallQuestion,
            DelegationEnabled = delegationEnabled,
        };
    }

    /// <summary>
    ///     Resolves the GitHub token for this run: the one stated on the command line, else one
    ///     from the environment, else none.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The environment fallback is what makes an unattended run possible without a credential
    ///     ever appearing in a command line — which would put it in a process list and, on CI, in a
    ///     log. <c>GH_TOKEN</c> is tried before <c>GITHUB_TOKEN</c> because it is the more specific
    ///     of the two: a workflow that sets both means the first to authenticate the GitHub tooling
    ///     it runs deliberately, and the second is often the automatically provided workflow token.
    ///     </para>
    ///     <para>
    ///     A blank value is treated as absent. An environment variable set to the empty string is
    ///     how a workflow expresses "no secret was configured", and treating it as a token would
    ///     turn a clean skip into an authentication failure.
    ///     </para>
    /// </remarks>
    /// <returns>The token to authenticate with, or <see langword="null"/> when there is none.</returns>
    public string? ResolveGitHubToken()
    {
        if (!string.IsNullOrWhiteSpace(GitHubToken))
        {
            return GitHubToken;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        fromEnvironment = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment;
    }

    /// <summary>
    ///     Describes the model this run will actually use, or states why it cannot be named.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>A run whose model is unrecorded cannot be attributed or reproduced.</b> The startup
    ///     banner previously printed "(provider default)" whenever <c>--model</c> was absent, which
    ///     was wrong in one case and unhelpful in the other: on Ollama the sample's own default was
    ///     already known and could simply have been named, and on Copilot the phrase suggested a
    ///     fixed model that could be looked up.
    ///     </para>
    ///     <para>
    ///     It cannot be looked up. <c>CopilotAgentFactory</c> leaves <c>SessionConfig.Model</c>
    ///     unset when no model is named, and the runtime neither reports nor promises which model
    ///     it then selects — it depends on what the signed-in user may use, and is resolved inside
    ///     the runtime at session time. So this says so plainly and names the flag that removes the
    ///     ambiguity, rather than printing a placeholder that reads like an answer.
    ///     </para>
    /// </remarks>
    /// <returns>The model name, or a statement of why none can be given.</returns>
    public string DescribeModel()
    {
        if (!string.IsNullOrWhiteSpace(Model))
        {
            return Model;
        }

        return Provider == AgentProvider.Ollama
            ? $"{DefaultOllamaModel} (this sample's default; --model was not given)"
            : "unknown — the Copilot runtime selects one at session time and does not report "
              + "which. Pass --model to pin it, which a run worth citing should do.";
    }

    /// <summary>
    ///     Reads the value that must follow a value-taking flag, advancing the argument index.
    /// </summary>
    /// <param name="args">The full argument list.</param>
    /// <param name="index">The index of the flag; advanced to the consumed value on success.</param>
    /// <param name="flag">The flag whose value is being read, for the error message.</param>
    /// <returns>The value token.</returns>
    /// <exception cref="CommandLineException">The flag has no following value token.</exception>
    private static string TakeValue(string[] args, ref int index, string flag)
    {
        var next = index + 1;
        if (next >= args.Length || args[next].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CommandLineException($"The {flag} argument requires a value.");
        }

        index = next;
        return args[next];
    }

    /// <summary>
    ///     Maps a provider name to an <see cref="AgentProvider"/>.
    /// </summary>
    /// <param name="value">The provider token, case-insensitive.</param>
    /// <returns>The matching provider.</returns>
    /// <exception cref="CommandLineException"><paramref name="value"/> names no known provider.</exception>
    private static AgentProvider ParseProvider(string value) => value.ToLowerInvariant() switch
    {
        "copilot" => AgentProvider.Copilot,
        "ollama" => AgentProvider.Ollama,
        _ => throw new CommandLineException(
            $"Unknown provider '{value}'. Valid providers are 'copilot' and 'ollama'."),
    };

    /// <summary>
    ///     Maps an embedding backend name to an <see cref="EmbeddingBackend"/>.
    /// </summary>
    /// <param name="value">The backend token, case-insensitive.</param>
    /// <returns>The matching backend.</returns>
    /// <exception cref="CommandLineException"><paramref name="value"/> names no known backend.</exception>
    private static EmbeddingBackend ParseEmbeddingBackend(string value) => value.ToLowerInvariant() switch
    {
        "local" => EmbeddingBackend.Local,
        "ollama" => EmbeddingBackend.Ollama,
        _ => throw new CommandLineException(
            $"Unknown embedding backend '{value}'. Valid backends are 'local' and 'ollama'."),
    };

    /// <summary>
    ///     Parses a stated context window, rejecting anything a session could not be accounted
    ///     against.
    /// </summary>
    /// <remarks>
    ///     A window that is not a positive number is refused here rather than carried to the
    ///     provider-session factory, which would reject it with a message about an argument the
    ///     user never wrote.
    /// </remarks>
    /// <param name="value">The token following <c>--context-window</c>.</param>
    /// <returns>The window in tokens.</returns>
    /// <exception cref="CommandLineException"><paramref name="value"/> is not a positive integer.</exception>
    private static int ParseContextWindow(string value)
    {
        if (!int.TryParse(value, out var tokens) || tokens <= 0)
        {
            throw new CommandLineException(
                $"The --context-window value '{value}' is not a positive number of tokens.");
        }

        return tokens;
    }

    /// <summary>
    ///     Builds the help text describing every flag, its default, and the things worth trying.
    /// </summary>
    /// <returns>The multi-line help text.</returns>
    public static string HelpText() =>
        $"""
         research-assistant — an agent that plans, remembers, and delegates.

         The assistant researches a read-only corpus of documents. It writes its plan down with the
         task-list tools, files what it learns as searchable memories, delegates the reading of a
         single document to a child agent the application registered, and writes its conclusions
         into a separate notes folder. It composes the todo, memory and agent tool families onto one
         policy — the three families that make an agent capable of work that spans turns.

         On --provider ollama the conversation runs on an AgentKit compacting session, so it
         outlives the model's context window: when the window fills, older history is consolidated
         into tiered records, a fresh provider session is seeded with them, and the turn loop
         carries on. Each turn reports its occupancy, whether it rotated, and whether compacting
         bought nothing and history had to be dropped. On --provider copilot the runtime carries its
         own session and none of that happens, because AgentKit ships no provider session for it.

         The memory family needs an embedding backend, and choosing one is the application's job,
         not AgentKit's. This sample offers an offline generator of its own (no server, no model
         download, lexical rather than semantic) and a real embedding model served by Ollama.

         Usage:
           research-assistant --corpus <path> [options]

         Options:
           --corpus <path>           Folder of source documents, granted read-only and used as the
                                     relative-path anchor (required).
           --notes <path>            Folder for the assistant's own notes, granted read-write and
                                     created if absent (default: a '{DefaultNotesFolderName}'
                                     folder beneath the system temporary directory).
           --provider copilot|ollama Runtime to run the agent on (default: copilot).
           --embeddings local|ollama Embedding backend for memories (default: local).
           --host <url>              Ollama server URL, for the chat and/or embedding model
                                     (default: {DefaultOllamaHost}).
           --model <name>            Model backing the agent. Without it, --provider ollama uses
                                     {DefaultOllamaModel} and --provider copilot lets the runtime
                                     pick one it does not report — so pass this for any run whose
                                     behavior you intend to cite.
           --embedding-model <name>  Ollama embedding model (default: {DefaultOllamaEmbeddingModel};
                                     --embeddings ollama only).
           --summary-model <name>    Model each context consolidation is sent to (default: the
                                     conversation's own model on Ollama, the runtime's default on
                                     Copilot). Consolidation is summarization rather than reasoning,
                                     so a smaller model is usually right.
           --context-window <tokens> Context window the compacting session accounts against; Ollama
                                     only (default: read from Ollama — the loaded model's length
                                     where one is loaded, else the model's published maximum). State
                                     it when the server loaded the model with a smaller length than
                                     the model publishes, which nothing else reveals. Copilot reports
                                     its own window with every turn, so this does not apply there.
           --github-token <token>    GitHub token for the Copilot runtime (default: the GH_TOKEN or
                                     GITHUB_TOKEN environment variable, else the logged-in user).
           --transcript <path>       Append a machine-readable record of every tool call to a file.
           --no-delegation           Omit the agent pack and the Delegation capability entirely.
           --prompt "<text>"         Run a prompt and exit; repeat to run several turns in order.
           --recall-question "<text>"
                                     After the prompts, answer this question in a fresh session on
                                     an agent carrying the memory tools and no reading tool at all,
                                     so the answer can only have come from memory_recall.
           --help                    Show this help and exit.

         Try:
           --prompt "Plan and carry out a review of every document in the corpus, then summarize
                     what the relief valve is set to and cite the document you got it from"
                                     (watch the plan appear as todo items, findings become memories,
                                      the reading of a document be delegated, and the conclusions be
                                      written into the notes folder)
           --prompt "Review every document in the corpus and record what you find"
           --recall-question "What is the relief valve set to, and which document says so?"
                                     (the recall turn has no documents and no conversation history;
                                      watch memory_recall be the only thing the answer can rest on)
           --prompt "What is the relief valve set to?" --prompt "Now read 02-field-revision.md and
                     correct what you recorded"
                                     (watch memory_file refuse the near-duplicate, and watch the
                                      correction go through memory_revise citing the NEW document)
           --prompt "What do you know about the mooring winch?"
                                     (nothing in the corpus is about one; watch whether the nearest
                                      matches recall returns are mistaken for an answer)
           --no-delegation --prompt "Delegate the reading of 01-initial-spec.md to a child agent"
                                     (the tool is not refused — it was never offered)
         """;
}
