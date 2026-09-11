using System.Diagnostics;

namespace DemaConsulting.AgentKit.Tools.Tests.TextFile;

/// <summary>
///     Creates a temporary directory tree containing a real reparse point that escapes an
///     allowed root, and tears it down safely.
/// </summary>
/// <remarks>
///     <para>
///     Path containment is a security control, so the tests that verify it must exercise a
///     genuine reparse point rather than a simulated one. This fixture builds the smallest tree
///     that can express an escape: an allowed <see cref="Root"/> and a sibling
///     <see cref="Outside"/>, with the test free to link one to the other.
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
///     <b>Why <c>mklink /J</c> on Windows.</b> Creating a symbolic link on Windows requires
///     <c>SeCreateSymbolicLinkPrivilege</c>, which an unelevated developer session does not
///     have. GitHub's <c>windows-latest</c> runner <em>is</em> elevated, so a
///     symbolic-link-based fixture would be green in continuous integration and red on every
///     workstation: a false green in a safety-critical test. A directory junction created by
///     <c>cmd.exe /c mklink /J</c> needs no privilege and is the same class of reparse point an
///     attacker would use. Linux and macOS have no such restriction, so they use
///     <see cref="Directory.CreateSymbolicLink(string, string)"/>.
///     </para>
///     <para>
///     <b>Why failure is fatal rather than skipped.</b> A skipped test produces no entry in the
///     TRX results, so the containment requirement would appear covered while no evidence
///     exists. The fixture therefore fails the test with the operating system's own error text.
///     </para>
///     <para>
///     Each test constructs its own fixture, so no state is shared between tests. Instances are
///     not thread-safe and are not intended to be used from more than one test at a time.
///     </para>
/// </remarks>
internal sealed class ReparsePointFixture : IDisposable
{
    /// <summary>
    ///     The temporary directory containing both <see cref="Root"/> and <see cref="Outside"/>.
    /// </summary>
    private readonly string _baseDirectory;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ReparsePointFixture"/> class, creating
    ///     an empty allowed root and an empty sibling directory outside it.
    /// </summary>
    /// <remarks>
    ///     The base directory is named with a fresh identifier so that concurrently executing
    ///     tests, and leftovers from an interrupted run, can never collide.
    /// </remarks>
    public ReparsePointFixture()
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
    ///     can make it appear contained; only a reparse point can bridge the two.
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
        File.WriteAllText(filePath, content);
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
        File.WriteAllBytes(filePath, content);
        return filePath;
    }

    /// <summary>
    ///     Creates a directory link beneath <see cref="Root"/> pointing at a target directory.
    /// </summary>
    /// <remarks>
    ///     Fails the current test rather than throwing or skipping when the platform refuses to
    ///     create the link, so that a missing safety control is always visible in the results.
    /// </remarks>
    /// <param name="linkName">
    ///     The name of the link to create. Relative names are created beneath
    ///     <see cref="Root"/>; an absolute path is used as given.
    /// </param>
    /// <param name="targetDirectory">The existing directory the link points at.</param>
    /// <returns>The full path of the created link.</returns>
    public string CreateDirectoryLink(string linkName, string targetDirectory)
    {
        var linkPath = Path.IsPathRooted(linkName) ? linkName : Path.Combine(Root, linkName);

        // Windows and the POSIX platforms need different mechanisms; see the type remarks for
        // why a junction, not a symbolic link, is the correct choice on Windows.
        if (OperatingSystem.IsWindows())
        {
            CreateJunction(linkPath, targetDirectory);
        }
        else
        {
            CreateSymbolicLink(linkPath, targetDirectory);
        }

        // Confirm the link actually exists before any test relies on it, so that a silent
        // platform failure cannot turn a containment test into a vacuous pass.
        if (!Directory.Exists(linkPath))
        {
            Assert.Fail($"Directory link '{linkPath}' was not created.");
        }

        return linkPath;
    }

    /// <summary>
    ///     Deletes the temporary tree, removing directory links before the recursive delete.
    /// </summary>
    /// <remarks>
    ///     <b>Order matters.</b> A recursive delete over a tree that still contains a junction
    ///     throws, and on some platforms would otherwise risk deleting the link's target rather
    ///     than the link. Links are therefore removed individually first. A failure to remove a
    ///     link is reported after the rest of the cleanup has been attempted, because a silently
    ///     leaked junction on a developer machine causes confusing failures in later runs.
    /// </remarks>
    public void Dispose()
    {
        if (!Directory.Exists(_baseDirectory))
        {
            return;
        }

        // Remove every directory link first; capture rather than propagate so that the
        // remaining cleanup still runs.
        Exception? linkFailure = null;
        try
        {
            DeleteDirectoryLinks(_baseDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            linkFailure = exception;
        }

        // Now the tree contains no reparse points and can be deleted recursively.
        try
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A residual directory is tolerable; a residual link is not, so the link failure
            // below takes precedence when both occur.
            linkFailure ??= exception;
        }

        if (linkFailure is not null)
        {
            // Reported as a test failure rather than a thrown exception: a failure surfacing
            // from Dispose must still be visible in the results, but throwing from a disposal
            // path would mask the test's own outcome.
            Assert.Fail(
                $"Failed to clean up the reparse-point fixture at '{_baseDirectory}': " +
                linkFailure.Message);
        }
    }

    /// <summary>
    ///     Creates a Windows directory junction using <c>cmd.exe /c mklink /J</c>.
    /// </summary>
    /// <remarks>
    ///     The external process is used deliberately: the managed API creates a symbolic link,
    ///     which requires a privilege developer workstations do not have. Standard output and
    ///     error are captured so that a failure reports the operating system's own message
    ///     rather than an opaque exit code.
    /// </remarks>
    /// <param name="linkPath">The full path of the junction to create.</param>
    /// <param name="targetDirectory">The existing directory the junction points at.</param>
    private static void CreateJunction(string linkPath, string targetDirectory)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c mklink /J \"{linkPath}\" \"{targetDirectory}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            Assert.Fail($"Failed to start cmd.exe to create the junction '{linkPath}'.");
            return;
        }

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            Assert.Fail(
                $"mklink /J failed with exit code {process.ExitCode} creating '{linkPath}' " +
                $"-> '{targetDirectory}': {standardError.Trim()} {standardOutput.Trim()}");
        }
    }

    /// <summary>
    ///     Creates a POSIX symbolic link to a directory.
    /// </summary>
    /// <remarks>
    ///     Linux and macOS place no privilege requirement on symbolic-link creation, so the
    ///     managed API is both sufficient and clearer than shelling out.
    /// </remarks>
    /// <param name="linkPath">The full path of the link to create.</param>
    /// <param name="targetDirectory">The existing directory the link points at.</param>
    private static void CreateSymbolicLink(string linkPath, string targetDirectory)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Fail(
                $"Failed to create the symbolic link '{linkPath}' -> '{targetDirectory}': " +
                exception.Message);
        }
    }

    /// <summary>
    ///     Recursively removes every directory link beneath a directory, without descending
    ///     into the links themselves.
    /// </summary>
    /// <remarks>
    ///     Descending into a link would walk into the target's contents, which are not part of
    ///     the fixture's tree and must never be deleted. The link is therefore removed as a
    ///     single entry and the walk continues with its siblings.
    /// </remarks>
    /// <param name="directory">The directory to walk.</param>
    private static void DeleteDirectoryLinks(string directory)
    {
        foreach (var child in Directory.GetDirectories(directory))
        {
            // A non-null link target is what identifies this entry as a reparse point.
            if (new DirectoryInfo(child).LinkTarget is not null)
            {
                Directory.Delete(child, recursive: false);
            }
            else
            {
                DeleteDirectoryLinks(child);
            }
        }
    }
}
