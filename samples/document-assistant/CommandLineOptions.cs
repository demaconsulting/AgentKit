namespace DemaConsulting.AgentKit.Samples.DocumentAssistant;

/// <summary>
///     The provider whose agent runtime the sample runs the conversation on.
/// </summary>
/// <remarks>
///     The provider is the <em>only</em> thing that changes how the agent is constructed. Every
///     other option describes the locations, the model, or the interaction, not the runtime — which
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
///     options that actually matter for safety: the two locations the agent is granted and what
///     access each carries. Every flag has an actionable failure message so a mistake names itself
///     rather than surfacing as a later runtime error.
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
    ///     The folder name, beneath the system temporary directory, the sample uses for session
    ///     artifacts when <c>--session</c> is not supplied.
    /// </summary>
    /// <remarks>
    ///     <b>This is the sample's choice, not an AgentKit convention.</b> AgentKit has no opinion
    ///     about session folders — an application supplies whatever locations it likes, and the
    ///     library treats each one identically. A temporary-directory default was chosen here for
    ///     two reasons: it is unambiguously <em>outside</em> the workspace, so the cross-location
    ///     behaviors the sample demonstrates are real rather than staged, and it keeps the
    ///     repository clean. Any other location works exactly as well; pass <c>--session</c> to use
    ///     one.
    /// </remarks>
    public const string DefaultSessionFolderName = "document-assistant-session";

    /// <summary>
    ///     Gets the workspace directory the agent's relative paths are anchored to. Required.
    /// </summary>
    /// <remarks>
    ///     This path becomes the policy's <em>working directory</em> — the single location a
    ///     relative path a model supplies is resolved against — and it is also granted, either
    ///     read-only or read-write depending on <see cref="WorkspaceReadOnly"/>. The two facts are
    ///     independent: anchoring grants nothing, and granting anchors nothing. It has no default
    ///     and must be supplied explicitly, because it is the location the demonstration turns on.
    /// </remarks>
    public required string Workspace { get; init; }

    /// <summary>
    ///     Gets the session directory the agent may write artifacts to, or <see langword="null"/>
    ///     to use the default beneath the system temporary directory. Set by <c>--session</c>.
    /// </summary>
    /// <remarks>
    ///     The session location is a second granted location, always read-write, and always outside
    ///     the workspace. It exists to make the sample the shape a real application has: essentially
    ///     every application has a user work folder plus somewhere of its own to put what the agent
    ///     produces. It is created if it does not exist, because an application that asks an agent
    ///     to write somewhere is responsible for that location existing.
    /// </remarks>
    public string? Session { get; init; }

    /// <summary>
    ///     Gets a value indicating whether the workspace is granted read-only rather than
    ///     read-write. Set by <c>--read-only-workspace</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is the sample's asymmetric-grant demonstration: the user's documents become
    ///     readable but unmodifiable while the session location stays writable. A write into the
    ///     workspace is then refused, and the refusal enumerates every permitted location with its
    ///     access level — so the agent learns where it <em>may</em> write and can recover, rather
    ///     than simply failing.
    ///     </para>
    ///     <para>
    ///     Note what this switch does <em>not</em> change: the workspace is still the working
    ///     directory and is still granted, so relative paths still resolve against it and results
    ///     inside it are still reported as relative names. Permission and addressing are orthogonal.
    ///     </para>
    /// </remarks>
    public bool WorkspaceReadOnly { get; init; }

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
        string? session = null;
        var workspaceReadOnly = false;
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

                case "--session":
                    session = TakeValue(args, ref index, arg);
                    break;

                case "--read-only-workspace":
                    workspaceReadOnly = true;
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

        // The workspace is the relative anchor and the location the user's documents live in.
        // Without it there is nothing to anchor and nothing to grant, so its absence is an error
        // rather than a defaulted convenience. The session location is different: the sample picks
        // a default for it and prints that choice at startup.
        if (string.IsNullOrWhiteSpace(workspace))
        {
            throw new CommandLineException(
                "The --workspace <path> argument is required; it is the folder relative paths are "
                + "anchored to and the folder the agent is granted access to.");
        }

        return new CommandLineOptions
        {
            Workspace = workspace,
            Session = session,
            WorkspaceReadOnly = workspaceReadOnly,
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
         document-assistant — chat with an agent granted exactly two locations.

         The agent reads text files and (with vision) looks at images in a workspace folder, and
         writes whatever it produces into a separate session folder. Anything outside those two
         locations is impossible to reach, not merely discouraged.

         The two locations are orthogonal ideas working together. The workspace is the working
         directory — the single anchor a relative path resolves against — and it is also granted,
         read-write by default or read-only with --read-only-workspace. The session folder is
         granted read-write but is not an anchor, so it is always addressed by absolute path.
         AgentKit has no session-folder convention; this location is the sample's own choice.

         Usage:
           document-assistant --workspace <path> [options]

         Options:
           --workspace <path>        Folder relative paths anchor to, and which is granted (required).
           --session <path>          Folder for agent-written artifacts, granted read-write and
                                     created if absent (default: a '{DefaultSessionFolderName}'
                                     folder beneath the system temporary directory).
           --read-only-workspace     Grant the workspace read-only; the session stays writable.
           --provider copilot|ollama Runtime to run the agent on (default: copilot).
           --host <url>              Ollama server URL (default: {DefaultOllamaHost}; ollama only).
           --model <name>            Ollama model name (default: {DefaultOllamaModel}; ollama only).
           --no-vision               Omit the image tool and the Vision capability entirely.
           --prompt "<text>"         Run a single prompt and exit (otherwise start an interactive chat).
           --help                    Show this help and exit.

         Try:
           --prompt "List every location you can reach and say which you can write to"
           --prompt "Read welcome.txt, then save a summary into the session folder"
                                                        (watch the absolute session path come back)
           --read-only-workspace --prompt "Summarize welcome.txt into summary.md next to it"
                                     (watch the write be refused, the refusal name the writable
                                      session folder, and the agent recover by writing there)
           --prompt "Read ../outside-workspace.txt"      (watch containment refuse it)
           --prompt "Use text_file_read on diagram.png"  (watch the binary guard redirect to image_read)
         """;
}
