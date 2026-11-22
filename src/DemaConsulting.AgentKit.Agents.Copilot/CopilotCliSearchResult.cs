namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     What <see cref="CopilotCli.FindInstalled()"/> found: the Copilot CLI an installed-CLI runtime
///     would run, every location searched, and every candidate passed over.
/// </summary>
/// <remarks>
///     Immutable and safe for concurrent use. The lists are in search order, so a host can show a
///     user exactly where AgentKit looked and why a CLI it found was not chosen.
/// </remarks>
public sealed class CopilotCliSearchResult
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotCliSearchResult"/> class.
    /// </summary>
    /// <remarks>
    ///     Internal: only discovery builds a result.
    /// </remarks>
    /// <param name="path">The selected CLI's full path, or <see langword="null"/> when none was found.</param>
    /// <param name="searchedLocations">Every location searched, in search order.</param>
    /// <param name="skippedCandidates">Every candidate passed over, each with the reason, in search order.</param>
    internal CopilotCliSearchResult(
        string? path,
        IReadOnlyList<string> searchedLocations,
        IReadOnlyList<string> skippedCandidates)
    {
        Path = path;
        SearchedLocations = searchedLocations;
        SkippedCandidates = skippedCandidates;
    }

    /// <summary>
    ///     Gets the full path of the Copilot CLI found, or <see langword="null"/> when none was.
    /// </summary>
    /// <remarks>
    ///     The first candidate in search order that exists, can be run and is not an editor's launcher.
    ///     Its version is not checked here; <see cref="CopilotRuntime.StartAsync"/> checks it before
    ///     the CLI is used.
    /// </remarks>
    public string? Path { get; }

    /// <summary>
    ///     Gets every location searched, in search order: the full path of each candidate file looked
    ///     for, from <c>PATH</c> and then the platform's well-known installation locations.
    /// </summary>
    public IReadOnlyList<string> SearchedLocations { get; }

    /// <summary>
    ///     Gets every candidate that exists but was passed over, as <c>"&lt;path&gt; (&lt;reason&gt;)"</c>,
    ///     in search order — such as the Copilot launcher Visual Studio Code puts on <c>PATH</c>, which
    ///     forwards to another CLI and may prompt to install one.
    /// </summary>
    public IReadOnlyList<string> SkippedCandidates { get; }
}
