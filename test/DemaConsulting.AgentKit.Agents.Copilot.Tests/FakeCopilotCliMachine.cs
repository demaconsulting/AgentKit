using System.Diagnostics;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     A fake machine for Copilot CLI discovery and version probing: files, folders, execute bits and
///     a scripted process runner, for any platform.
/// </summary>
/// <remarks>
///     Paths are kept exactly as given, so a test composes them with <see cref="Path.Combine(string[])"/>
///     from roots that start with <c>/</c> — rooted on every platform — and expects the same
///     composition back. Nothing touches the real file system or starts a process.
/// </remarks>
internal sealed class FakeCopilotCliMachine
{
    /// <summary>
    ///     The files that exist, with their content.
    /// </summary>
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    /// <summary>
    ///     The lengths reported for files whose content is not stored, such as a large real CLI.
    /// </summary>
    private readonly Dictionary<string, long> _lengths = new(StringComparer.Ordinal);

    /// <summary>
    ///     The files without an execute bit.
    /// </summary>
    private readonly HashSet<string> _notExecutable = new(StringComparer.Ordinal);

    /// <summary>
    ///     The subfolders of each folder.
    /// </summary>
    private readonly Dictionary<string, List<string>> _directories = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the start information of every process the runner was asked to run, in order.
    /// </summary>
    public List<ProcessStartInfo> Started { get; } = [];

    /// <summary>
    ///     Gets the time limit of every process the runner was asked to run, in order.
    /// </summary>
    public List<TimeSpan> Timeouts { get; } = [];

    /// <summary>
    ///     Gets or sets the result the runner returns.
    /// </summary>
    public CopilotCliProcessResult Result { get; set; } =
        new(0, "GitHub Copilot CLI 1.0.113.\nRun 'copilot update' to check for updates.\n", string.Empty, TimedOut: false);

    /// <summary>
    ///     Gets or sets the failure the runner raises instead of returning <see cref="Result"/>.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>
    ///     Adds a file.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <param name="content">The file's content; empty when <see langword="null"/>.</param>
    /// <param name="executable">Whether the file has an execute bit.</param>
    /// <param name="length">The length to report instead of the content's, such as a large real CLI's.</param>
    /// <returns>The path.</returns>
    public string AddFile(string path, byte[]? content = null, bool executable = true, long? length = null)
    {
        _files[path] = content ?? [];
        if (!executable)
        {
            _notExecutable.Add(path);
        }

        if (length is not null)
        {
            _lengths[path] = length.Value;
        }

        return path;
    }

    /// <summary>
    ///     Adds a subfolder to a folder.
    /// </summary>
    /// <param name="parent">The folder.</param>
    /// <param name="name">The subfolder's name.</param>
    /// <returns>The subfolder's path.</returns>
    public string AddDirectory(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        if (!_directories.TryGetValue(parent, out var children))
        {
            children = [];
            _directories[parent] = children;
        }

        children.Add(path);
        return path;
    }

    /// <summary>
    ///     Builds a search context over this machine.
    /// </summary>
    /// <param name="platform">The platform whose conventions apply.</param>
    /// <param name="pathVariable">The <c>PATH</c> value.</param>
    /// <param name="userProfile">The home folder.</param>
    /// <param name="localAppData">The local application data folder.</param>
    /// <param name="appData">The roaming application data folder.</param>
    /// <returns>The context.</returns>
    public CopilotCliSearchContext Context(
        CopilotCliPlatform platform,
        string? pathVariable = null,
        string? userProfile = null,
        string? localAppData = null,
        string? appData = null) =>
        new(
            platform,
            pathVariable,
            userProfile,
            localAppData,
            appData,
            _files.ContainsKey,
            ListDirectories,
            path => !_notExecutable.Contains(path),
            path => _lengths.TryGetValue(path, out var length) ? length : _files[path].LongLength,
            path => _files[path]);

    /// <summary>
    ///     Runs a process the way the probe would: records it and returns the scripted result.
    /// </summary>
    /// <param name="startInfo">The process.</param>
    /// <param name="timeout">Its time limit.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The scripted result.</returns>
    public Task<CopilotCliProcessResult> RunAsync(ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Started.Add(startInfo);
        Timeouts.Add(timeout);
        return Failure is null ? Task.FromResult(Result) : Task.FromException<CopilotCliProcessResult>(Failure);
    }

    /// <summary>
    ///     Lists a folder's subfolders whose names match a pattern with one trailing <c>*</c>.
    /// </summary>
    /// <param name="path">The folder.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The matching subfolders.</returns>
    private IEnumerable<string> ListDirectories(string path, string pattern)
    {
        var prefix = pattern.TrimEnd('*');
        return _directories.TryGetValue(path, out var children)
            ? children.Where(child => Path.GetFileName(child).StartsWith(prefix, StringComparison.Ordinal))
            : [];
    }
}
