namespace DemaConsulting.AgentKit.Samples.CustomTools;

/// <summary>
///     The provider whose agent runtime the sample runs the conversation on.
/// </summary>
/// <remarks>
///     The provider is the <em>only</em> thing that changes how the agent is constructed. Every
///     other option describes the workspace, the model, or the interaction, not the runtime — which
///     is the same governing principle the document-assistant sample follows: a tool set composed
///     once, here including the author-written custom packs, runs unchanged on either provider.
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
///     The parsed command-line configuration for one run of the custom-tools sample.
/// </summary>
/// <remarks>
///     <para>
///     The parser accepts <b>named flags only</b> — there are no positional arguments — so a
///     mistake names itself rather than surfacing later as a confusing runtime error. This type
///     only holds and validates configuration; it constructs nothing and performs no I/O, so it is
///     trivially testable and safe to reason about in isolation.
///     </para>
/// </remarks>
public sealed class CommandLineOptions
{
    /// <summary>
    ///     The default Ollama host used when <c>--host</c> is not supplied.
    /// </summary>
    /// <remarks>
    ///     Ollama's own default listening address, so a developer running Ollama locally needs only
    ///     <c>--provider ollama</c>. The default is plain HTTP because Ollama serves HTTP; the S5332
    ///     "use HTTPS" analyzer rule is suppressed for this line for that reason.
    /// </remarks>
#pragma warning disable S5332 // Ollama serves plain HTTP on the loopback interface; HTTPS is not available.
    public const string DefaultOllamaHost = "http://localhost:11434";
#pragma warning restore S5332

    /// <summary>
    ///     The default Ollama model used when <c>--model</c> is not supplied.
    /// </summary>
    /// <remarks>
    ///     Chosen because it carries tool-calling, which is all this sample's custom tools require;
    ///     no vision is needed here.
    /// </remarks>
    public const string DefaultOllamaModel = "qwen3.5:9b";

    /// <summary>
    ///     Gets the workspace directory the agent's relative paths are anchored to and which is
    ///     granted read-write. Required.
    /// </summary>
    /// <remarks>
    ///     This path becomes the policy's <em>working directory</em> — the single location a
    ///     relative path a model supplies is resolved against — and it is also granted read-write,
    ///     so the custom Markdown tool has files to inspect and the shipped text-file tools have a
    ///     location to work in. It has no default and must be supplied explicitly.
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
    ///     is reported here with an actionable message rather than surfacing later. When
    ///     <c>--help</c> is present it short-circuits all other validation.
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

        // A user asking for help gets it regardless of what else is (or is not) on the line.
        if (Array.Exists(args, arg => arg is "--help" or "-h" or "-?"))
        {
            return new CommandLineOptions { Workspace = string.Empty, HelpRequested = true };
        }

        // Accumulate into locals so the immutable options object can be built once at the end.
        string? workspace = null;
        var provider = AgentProvider.Copilot;
        var host = DefaultOllamaHost;
        var model = DefaultOllamaModel;
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

                default:
                    // An unrecognized token is a mistake, not something to silently ignore. The
                    // "run with --help" hint is appended once by the top-level usage-error handler.
                    throw new CommandLineException($"Unknown argument '{arg}'.");
            }
        }

        // The workspace is the relative anchor and the location the custom Markdown tool inspects.
        // Without it there is nothing to anchor and nothing to grant, so its absence is an error.
        if (string.IsNullOrWhiteSpace(workspace))
        {
            throw new CommandLineException(
                "The --workspace <path> argument is required; it is the folder relative paths are "
                + "anchored to and the folder the agent is granted access to.");
        }

        return new CommandLineOptions
        {
            Workspace = workspace,
            Provider = provider,
            Host = host,
            Model = model,
            Prompt = prompt,
        };
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
    ///     Builds the help text describing every flag, its default, and the things worth trying.
    /// </summary>
    /// <returns>The multi-line help text.</returns>
    public static string HelpText() =>
        $"""
         custom-tools — chat with an agent given author-written guarded tools.

         This sample shows how an application author writes their own guarded tools with
         GuardedToolFactory and publishes them as packs, composed alongside a shipped pack. It
         registers two custom packs and one shipped one:

           markdown_sections  (custom) lists a Markdown file's headings with line numbers, going
                              through the same PathPolicy containment and path dialect the shipped
                              tools use, and returning a structured result.
           clock_now          (custom) reports the current local and UTC time. It takes no path and
                              consults no policy — the deliberate contrast showing GuardedToolFactory
                              builds every tool, not only path-based ones.
           text_file_*        (shipped) the text-file pack, composed alongside the custom packs.

         Usage:
           custom-tools --workspace <path> [options]

         Options:
           --workspace <path>        Folder relative paths anchor to, granted read-write (required).
           --provider copilot|ollama Runtime to run the agent on (default: copilot).
           --host <url>              Ollama server URL (default: {DefaultOllamaHost}; ollama only).
           --model <name>            Ollama model name (default: {DefaultOllamaModel}; ollama only).
           --prompt "<text>"         Run a single prompt and exit (otherwise start an interactive chat).
           --help                    Show this help and exit.

         Try:
           --prompt "What time is it, in local time and UTC?"
           --prompt "List the sections of sample.md with their line numbers"
           --prompt "List the sections of ../outside.md"   (watch containment refuse it)
           --prompt "What locations can the markdown tool search?"
         """;
}
