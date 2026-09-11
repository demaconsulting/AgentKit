namespace DemaConsulting.AgentKit.Samples.DocumentAssistant;

/// <summary>
///     The provider whose agent runtime the sample runs the conversation on.
/// </summary>
/// <remarks>
///     The provider is the <em>only</em> thing that changes how the agent is constructed. Every
///     other option describes the workspace, the model, or the interaction, not the runtime — which
///     is the whole point of the sample: a tool set composed once runs unchanged on either provider.
/// </remarks>
public enum AgentProvider
{
    /// <summary>
    ///     The GitHub Copilot SDK runtime, used through the logged-in Copilot CLI. Ignores
    ///     <see cref="CommandLineOptions.Host"/> and <see cref="CommandLineOptions.Model"/>.
    /// </summary>
    Copilot,

    /// <summary>
    ///     Any Ollama server reached over HTTP, addressed by <see cref="CommandLineOptions.Host"/>
    ///     and <see cref="CommandLineOptions.Model"/>.
    /// </summary>
    Ollama,
}

/// <summary>
///     The parsed command-line configuration for one run of the document assistant.
/// </summary>
/// <remarks>
///     <para>
///     The parser accepts <b>named flags only</b> — there are no positional arguments — because a
///     positional path argument would be easy to confuse with the prompt text and would obscure the
///     one option that actually matters for safety: the workspace the agent is confined to. Every
///     flag has an actionable failure message so a mistake names itself rather than surfacing as a
///     later runtime error.
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
    ///     <c>--provider ollama</c>. A remote server is reached with <c>--host</c>. The default is
    ///     deliberately not any particular machine's address: a sample that shipped pointing at one
    ///     team's box would fail for every other reader with a confusing connection error. The
    ///     default is plain HTTP because Ollama serves HTTP; the S5332 "use HTTPS" analyzer rule is
    ///     suppressed for this line for that reason.
    /// </remarks>
#pragma warning disable S5332 // Ollama serves plain HTTP on the loopback interface; HTTPS is not available.
    public const string DefaultOllamaHost = "http://localhost:11434";
#pragma warning restore S5332

    /// <summary>
    ///     The default Ollama model used when <c>--model</c> is not supplied.
    /// </summary>
    /// <remarks>
    ///     Chosen because it carries both tool-calling and vision, so the default run exercises the
    ///     whole sample — listing, reading, and describing an image — without extra flags.
    /// </remarks>
    public const string DefaultOllamaModel = "qwen3.5:9b";

    /// <summary>
    ///     Gets the workspace directory the agent is confined to. Required.
    /// </summary>
    /// <remarks>
    ///     This single path becomes the read root, the write root, and the base directory a
    ///     relative path a model supplies is resolved against. It is the containment boundary the
    ///     whole demonstration turns on, so it has no default and must be supplied explicitly.
    /// </remarks>
    public required string Workspace { get; init; }

    /// <summary>
    ///     Gets the provider whose runtime the agent runs on. Defaults to <see cref="AgentProvider.Copilot"/>.
    /// </summary>
    public AgentProvider Provider { get; init; } = AgentProvider.Copilot;

    /// <summary>
    ///     Gets the Ollama host URL. Used only when <see cref="Provider"/> is
    ///     <see cref="AgentProvider.Ollama"/>. Defaults to <see cref="DefaultOllamaHost"/>.
    /// </summary>
    public string Host { get; init; } = DefaultOllamaHost;

    /// <summary>
    ///     Gets the Ollama model name. Used only when <see cref="Provider"/> is
    ///     <see cref="AgentProvider.Ollama"/>. Defaults to <see cref="DefaultOllamaModel"/>.
    /// </summary>
    public string Model { get; init; } = DefaultOllamaModel;

    /// <summary>
    ///     Gets a value indicating whether vision is enabled. When <see langword="false"/>, the
    ///     image pack and the <c>Vision</c> host capability are both omitted, so <c>image_read</c>
    ///     is never offered to the model.
    /// </summary>
    /// <remarks>
    ///     Set by <c>--no-vision</c>. Its purpose is to demonstrate capability gating: with vision
    ///     off, the image tool is not merely refused at call time — it is never presented at all.
    /// </remarks>
    public bool VisionEnabled { get; init; } = true;

    /// <summary>
    ///     Gets the single prompt to run non-interactively, or <see langword="null"/> to run the
    ///     interactive REPL. Set by <c>--prompt</c>.
    /// </summary>
    public string? Prompt { get; init; }

    /// <summary>
    ///     Gets a value indicating whether help was requested and no run should occur. Set by
    ///     <c>--help</c>.
    /// </summary>
    public bool HelpRequested { get; init; }

    /// <summary>
    ///     Parses command-line arguments into a validated <see cref="CommandLineOptions"/>.
    /// </summary>
    /// <remarks>
    ///     Parsing and validation are one step so that an invalid combination — an unknown flag, a
    ///     flag missing its value, a bad provider name, or the absence of the required workspace —
    ///     is reported here with an actionable message rather than surfacing later as a confusing
    ///     failure while constructing the agent. When <c>--help</c> is present it short-circuits all
    ///     other validation, because a user asking for help has not necessarily supplied a valid
    ///     command yet.
    /// </remarks>
    /// <param name="args">The raw arguments as received by <c>Main</c>. Must not be <see langword="null"/>.</param>
    /// <returns>The parsed and validated options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="args"/> is <see langword="null"/>.</exception>
    /// <exception cref="CommandLineException">
    ///     An argument is unknown, a flag is missing its value, <c>--provider</c> names something
    ///     other than <c>copilot</c> or <c>ollama</c>, or the required <c>--workspace</c> is absent.
    /// </exception>
    public static CommandLineOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // A user asking for help gets it regardless of what else is (or is not) on the line, so
        // scan for --help first and return before any requirement is enforced.
        if (Array.Exists(args, arg => arg is "--help" or "-h" or "-?"))
        {
            return new CommandLineOptions { Workspace = string.Empty, HelpRequested = true };
        }

        // Accumulate into locals so the immutable options object can be built once at the end with
        // every value validated.
        string? workspace = null;
        var provider = AgentProvider.Copilot;
        var host = DefaultOllamaHost;
        var model = DefaultOllamaModel;
        var visionEnabled = true;
        string? prompt = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--workspace":
                    workspace = TakeValue(args, ref index, arg);
                    break;

                case "--provider":
                    provider = ParseProvider(TakeValue(args, ref index, arg));
                    break;

                case "--host":
                    host = TakeValue(args, ref index, arg);
                    break;

                case "--model":
                    model = TakeValue(args, ref index, arg);
                    break;

                case "--prompt":
                    prompt = TakeValue(args, ref index, arg);
                    break;

                case "--no-vision":
                    visionEnabled = false;
                    break;

                default:
                    // An unrecognized token is a mistake, not something to silently ignore, because
                    // ignoring it would let a misspelled --workspace run the agent unconfined.
                    // The "run with --help" hint is appended once by the top-level usage-error
                    // handler, so it is deliberately not repeated here.
                    throw new CommandLineException($"Unknown argument '{arg}'.");
            }
        }

        // The workspace is the containment boundary; without it there is nothing to confine the
        // agent to, so its absence is an error rather than a defaulted convenience.
        if (string.IsNullOrWhiteSpace(workspace))
        {
            throw new CommandLineException(
                "The --workspace <path> argument is required; it is the folder the agent is confined to.");
        }

        return new CommandLineOptions
        {
            Workspace = workspace,
            Provider = provider,
            Host = host,
            Model = model,
            VisionEnabled = visionEnabled,
            Prompt = prompt,
        };
    }

    /// <summary>
    ///     Reads the value that must follow a value-taking flag, advancing the argument index.
    /// </summary>
    /// <remarks>
    ///     A flag whose value is missing (it is the last token, or the next token is another flag)
    ///     is a user error worth catching precisely, so this reports the offending flag by name
    ///     rather than letting a following flag be misread as the value.
    /// </remarks>
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
    ///     Builds the help text describing every flag, its default, and the things worth trying.
    /// </summary>
    /// <remarks>
    ///     Returned rather than written so the caller decides the stream; help goes to standard
    ///     output while a usage error's help hint goes to standard error.
    /// </remarks>
    /// <returns>The multi-line help text.</returns>
    public static string HelpText() =>
        $"""
         document-assistant — chat with an agent confined to a workspace folder.

         The agent can read text files, list them, and (with vision) look at images, but only
         within the workspace: a path outside it is impossible to reach, not merely discouraged.

         Usage:
           document-assistant --workspace <path> [options]

         Options:
           --workspace <path>        Folder the agent is confined to (required).
           --provider copilot|ollama Runtime to run the agent on (default: copilot).
           --host <url>              Ollama server URL (default: {DefaultOllamaHost}; ollama only).
           --model <name>            Ollama model name (default: {DefaultOllamaModel}; ollama only).
           --no-vision               Omit the image tool and the Vision capability entirely.
           --prompt "<text>"         Run a single prompt and exit (otherwise start an interactive chat).
           --help                    Show this help and exit.

         Try:
           --prompt "List the files, read welcome.txt, then describe diagram.png"
           --prompt "Read ../outside-workspace.txt"      (watch containment refuse it)
           --prompt "Use text_file_read on diagram.png"  (watch the binary guard redirect to image_read)
         """;
}
