using System.Diagnostics;

namespace DemaConsulting.AgentKit.Tools.Tests.File;

/// <summary>
///     Creates a real directory link — an NTFS junction on Windows, a symbolic link elsewhere —
///     and removes it afterwards without following it.
/// </summary>
/// <remarks>
///     <para>
///     Refusing to follow a link out of a directory being removed is a security control, so the
///     tests that verify it must exercise a genuine link rather than a simulated one. This helper
///     creates the smallest thing that can express an escape: one link entry pointing at a
///     directory the test placed outside the permitted location.
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
///     exists. The helper therefore fails the test with the operating system's own error text.
///     </para>
///     <para>
///     <b>Why disposal removes the link first.</b> A recursive delete over a tree that still
///     contains a junction throws and leaves the tree partly deleted, and on some platforms would
///     risk deleting the link's target rather than the link. The link is therefore removed as a
///     single entry, unfollowed, before any surrounding fixture tears its tree down.
///     </para>
///     <para>
///     Each test creates its own link, so no state is shared between tests. Instances are not
///     thread-safe and are not intended to be used from more than one test at a time.
///     </para>
/// </remarks>
internal sealed class DirectoryLink : IDisposable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DirectoryLink"/> class over an
    ///     already-created link.
    /// </summary>
    /// <remarks>
    ///     Private so that <see cref="Create"/> is the only way to obtain one: an instance must
    ///     never exist for a link that was not actually created, or disposal would report a
    ///     cleanup failure for something the test never made.
    /// </remarks>
    /// <param name="path">The full path of the created link.</param>
    private DirectoryLink(string path)
    {
        Path = path;
    }

    /// <summary>
    ///     Gets the full path of the created link.
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     Creates a directory link at one path pointing at an existing directory.
    /// </summary>
    /// <remarks>
    ///     Fails the current test rather than throwing or skipping when the platform refuses to
    ///     create the link, so that a missing safety control is always visible in the results.
    /// </remarks>
    /// <param name="linkPath">The full path of the link to create.</param>
    /// <param name="targetDirectory">The existing directory the link points at.</param>
    /// <returns>The created link, which removes itself on disposal.</returns>
    public static DirectoryLink Create(string linkPath, string targetDirectory)
    {
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

        // Confirm the link actually exists, and is genuinely a link, before any test relies on
        // it: a silent platform failure must not turn a containment test into a vacuous pass.
        if (!Directory.Exists(linkPath))
        {
            Assert.Fail($"Directory link '{linkPath}' was not created.");
        }

        if (new DirectoryInfo(linkPath).LinkTarget is null)
        {
            Assert.Fail($"'{linkPath}' was created but is not a link.");
        }

        return new DirectoryLink(linkPath);
    }

    /// <summary>
    ///     Removes the link itself, never what it points at.
    /// </summary>
    /// <remarks>
    ///     A non-recursive delete of a link entry removes the link and leaves the target's
    ///     contents intact, which is exactly what a fixture must do — a test that leaked a
    ///     junction would make a later recursive cleanup destroy unrelated content. A link the
    ///     test under verification already removed is tolerated, because several scenarios
    ///     legitimately remove it themselves.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Reported as a test failure rather than a thrown exception: a leaked junction on a
            // developer machine causes confusing failures in later runs, but throwing from a
            // disposal path would mask the test's own outcome.
            Assert.Fail($"Failed to remove the directory link '{Path}': {exception.Message}");
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
}
