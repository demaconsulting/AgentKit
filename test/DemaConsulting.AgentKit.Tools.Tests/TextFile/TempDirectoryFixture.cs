namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Creates a temporary directory tree holding a permitted root and a sibling directory
///     outside it, and tears it down afterwards.
/// </summary>
/// <remarks>
///     <para>
///     Containment tests need two locations that no ordinary path arithmetic can confuse: a
///     <see cref="Root"/> standing in for a permitted location, and a sibling
///     <see cref="Outside"/> standing in for everywhere else. Real directories are used rather
///     than a simulated file system because the decision under test is made about real paths.
///     </para>
///     <para>
///     <b>Why this is a second copy rather than a shared one.</b> The Core test project's
///     identical fixture is internal to that project, and a test project that published a
///     fixture for another test project to reference would make one test assembly a dependency
///     of another — a coupling that turns an unrelated Core test change into a Tools test
///     failure. The duplication is deliberate and its cost is bounded by the fixture being small
///     and stable.
///     </para>
///     <para>
///     Each test constructs its own fixture, so no state is shared between tests. Instances are
///     not thread-safe and are not intended to be used from more than one test at a time.
///     </para>
/// </remarks>
internal sealed class TempDirectoryFixture : IDisposable
{
    /// <summary>
    ///     The temporary directory containing both <see cref="Root"/> and <see cref="Outside"/>.
    /// </summary>
    private readonly string _baseDirectory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TempDirectoryFixture"/> class, creating
    ///     an empty allowed root and an empty sibling directory outside it.
    /// </summary>
    /// <remarks>
    ///     The base directory is named with a fresh identifier so that concurrently executing
    ///     tests, and leftovers from an interrupted run, can never collide.
    /// </remarks>
    public TempDirectoryFixture()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), $"agent-kit-tools-tests-{Guid.NewGuid():N}");
        Root = Path.Combine(_baseDirectory, "root");
        Outside = Path.Combine(_baseDirectory, "outside");

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Outside);
    }

    /// <summary>
    ///     Gets the directory that stands in for a permitted location.
    /// </summary>
    public string Root { get; }

    /// <summary>
    ///     Gets a sibling directory that stands in for a location outside the permitted one.
    /// </summary>
    /// <remarks>
    ///     It is a sibling rather than a child so that no amount of ordinary path arithmetic
    ///     can make it appear contained.
    /// </remarks>
    public string Outside { get; }

    /// <summary>
    ///     Writes a file with known content into a directory of the temporary tree.
    /// </summary>
    /// <remarks>
    ///     Tests need a file whose content identifies it unambiguously, so that a result can be
    ///     shown to have reached the real file rather than merely to look plausible.
    /// </remarks>
    /// <param name="directory">The directory to write into; created when it does not exist.</param>
    /// <param name="fileName">The name of the file to write.</param>
    /// <param name="content">The content to write.</param>
    /// <returns>The full path of the written file.</returns>
    public static string WriteFile(string directory, string fileName, string content)
    {
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, fileName);
        System.IO.File.WriteAllText(filePath, content);
        return filePath;
    }

    /// <summary>
    ///     Writes a file with known bytes into a directory of the temporary tree.
    /// </summary>
    /// <remarks>
    ///     The string writer above always writes UTF-8 with no byte-order mark, so a scenario
    ///     that needs raw binary content, a byte-order-marked encoding or a deliberately invalid
    ///     byte sequence writes the exact bytes here instead. The tool under test does not parse
    ///     the bytes, so any distinctive sequence proves the real file was reached.
    /// </remarks>
    /// <param name="directory">The directory to write into; created when it does not exist.</param>
    /// <param name="fileName">The name of the file to write.</param>
    /// <param name="content">The bytes to write.</param>
    /// <returns>The full path of the written file.</returns>
    public static string WriteBytes(string directory, string fileName, byte[] content)
    {
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, fileName);
        System.IO.File.WriteAllBytes(filePath, content);
        return filePath;
    }

    /// <summary>
    ///     Deletes the temporary tree.
    /// </summary>
    /// <remarks>
    ///     A residual temporary directory is tolerable — it costs disk space on a developer
    ///     machine and nothing more — so a failure to delete is swallowed rather than turned
    ///     into a failure that would mask the test's own outcome.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_baseDirectory))
            {
                Directory.Delete(_baseDirectory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best-effort; the test's outcome is what matters here.
        }
    }
}
