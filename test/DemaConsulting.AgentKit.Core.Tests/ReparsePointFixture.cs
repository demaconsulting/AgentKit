using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DemaConsulting.AgentKit.Core.Tests;

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
///     <b>Why <c>mklink /J</c> on Windows.</b> Creating a symbolic link on Windows requires
///     <c>SeCreateSymbolicLinkPrivilege</c>, which an unelevated developer session does not
///     have — verified to fail on a developer workstation. GitHub's <c>windows-latest</c>
///     runner <em>is</em> elevated, so a symbolic-link-based fixture would be green in
///     continuous integration and red on every workstation: a false green in a safety-critical
///     test. A directory junction created by <c>cmd.exe /c mklink /J</c> needs no privilege and
///     is the same class of reparse point an attacker would use. Linux and macOS have no such
///     restriction, so they use <see cref="Directory.CreateSymbolicLink(string, string)"/>.
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
    ///     A reparse tag outside the Microsoft-reserved range, so nothing on the system can
    ///     decode it and no link target is ever reported for an entry carrying it.
    /// </summary>
    /// <remarks>
    ///     The high bit distinguishes Microsoft-reserved tags from third-party ones. This value
    ///     has it clear and belongs to no product, which is precisely the property the test
    ///     needs: the file system records a redirection it cannot interpret.
    /// </remarks>
    private const uint ThirdPartyReparseTag = 0x00000042;

    /// <summary>
    ///     The vendor identifier the platform requires in a third-party reparse point.
    /// </summary>
    /// <remarks>
    ///     A fixed value rather than a fresh one per call, so that a leaked entry on a
    ///     developer machine is recognizably this fixture's doing.
    /// </remarks>
    private static readonly Guid VendorGuid = new("0f2b7c9a-4c5e-4a1d-9f3b-7e6a5d4c3b2a");

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
        _baseDirectory = Path.Combine(Path.GetTempPath(), $"agent-kit-tests-{Guid.NewGuid():N}");
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
    ///     Creates an empty directory beneath <see cref="Root"/> that is marked as a reparse
    ///     point carrying a tag the platform does not decode, so no link target is reported
    ///     for it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Why this shape is needed.</b> A path component can exist, be marked as a link by
    ///     the file system, and still yield no target, because <c>ResolveLinkTarget</c> decodes
    ///     only the link kinds it knows. Testing that the resolver refuses such a component
    ///     requires a genuine one: a simulation would verify the simulation rather than the
    ///     control, exactly as for the ordinary links this fixture creates.
    ///     </para>
    ///     <para>
    ///     <b>Why a third-party tag.</b> The reparse point is written with a tag outside the
    ///     Microsoft-reserved range, in the GUID-carrying form the platform defines for
    ///     third-party tags. That form needs write access to an empty directory and no
    ///     privilege at all — confirmed on an unelevated developer session — so this fixture
    ///     keeps the same property as <see cref="CreateDirectoryLink"/>: it must not need an
    ///     elevation that would make a test green in continuous integration and red on a
    ///     workstation. It also needs no external subsystem, so the scenario does not depend on
    ///     anything beyond Windows itself.
    ///     </para>
    ///     <para>
    ///     <b>Windows only.</b> POSIX platforms have no equivalent: every link a POSIX file
    ///     system can express is a symbolic link, which the platform always decodes, so there
    ///     is no undecodable case to create. Callers therefore guard this with a skip condition
    ///     rather than assert something weaker elsewhere.
    ///     </para>
    /// </remarks>
    /// <param name="linkName">
    ///     The name of the entry to create. Relative names are created beneath
    ///     <see cref="Root"/>; an absolute path is used as given.
    /// </param>
    /// <returns>The full path of the created entry.</returns>
    public string CreateUndecodableReparsePoint(string linkName)
    {
        var linkPath = Path.IsPathRooted(linkName) ? linkName : Path.Combine(Root, linkName);
        Directory.CreateDirectory(linkPath);

        // Open the directory itself rather than anything it might redirect to: the reparse
        // point is being written onto this entry, so the open must not follow one.
        var handle = Interop.CreateFileW(
            linkPath,
            Interop.GenericWrite,
            shareMode: 0,
            IntPtr.Zero,
            Interop.OpenExisting,
            Interop.FileFlagBackupSemantics | Interop.FileFlagOpenReparsePoint,
            IntPtr.Zero);

        if (handle == Interop.InvalidHandle)
        {
            Assert.Fail(
                $"Failed to open '{linkPath}' to mark it as a reparse point: " +
                $"Win32 error {Marshal.GetLastWin32Error()}.");
            return linkPath;
        }

        try
        {
            var reparseData = BuildThirdPartyReparseData();
            if (!Interop.DeviceIoControl(
                    handle,
                    Interop.SetReparsePointControlCode,
                    reparseData,
                    reparseData.Length,
                    IntPtr.Zero,
                    0,
                    out _,
                    IntPtr.Zero))
            {
                Assert.Fail(
                    $"Failed to mark '{linkPath}' as a reparse point: " +
                    $"Win32 error {Marshal.GetLastWin32Error()}.");
            }
        }
        finally
        {
            Interop.CloseHandle(handle);
        }

        // Confirm the arrangement the test depends on actually exists: marked as a reparse
        // point, and reporting no target. Without both, the scenario would pass vacuously.
        var info = new DirectoryInfo(linkPath);
        if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            Assert.Fail($"'{linkPath}' was not marked as a reparse point.");
        }

        if (info.ResolveLinkTarget(returnFinalTarget: true) is not null)
        {
            Assert.Fail($"'{linkPath}' unexpectedly reports a link target; the tag was decoded.");
        }

        return linkPath;
    }

    /// <summary>
    ///     Builds the reparse data for a third-party tag: the tag, the payload length, the
    ///     vendor GUID the platform requires for a non-Microsoft tag, and a token payload.
    /// </summary>
    /// <remarks>
    ///     The payload content is irrelevant to the property under test — only that the tag is
    ///     one nothing on the system knows how to decode — so it is a short fixed token rather
    ///     than anything resembling a path, which keeps it obvious that nothing is meant to
    ///     follow it.
    /// </remarks>
    /// <returns>The reparse data buffer to hand to the file system control.</returns>
    private static byte[] BuildThirdPartyReparseData()
    {
        var payload = new byte[] { 0, 0, 0, 0 };

        // Layout: 4-byte tag, 2-byte data length, 2-byte reserved, 16-byte GUID, then payload.
        var buffer = new byte[24 + payload.Length];
        BitConverter.GetBytes(ThirdPartyReparseTag).CopyTo(buffer, 0);
        BitConverter.GetBytes((ushort)payload.Length).CopyTo(buffer, 4);
        VendorGuid.ToByteArray().CopyTo(buffer, 8);
        payload.CopyTo(buffer, 24);
        return buffer;
    }

    /// <summary>
    ///     Writes a file with known content into a directory of the temporary tree.
    /// </summary>
    /// <remarks>
    ///     Tests need a file whose content identifies it unambiguously, so that a resolved path
    ///     can be shown to reach the real file rather than merely to look plausible.
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
    ///     Deletes the temporary tree, removing directory links before the recursive delete.
    /// </summary>
    /// <remarks>
    ///     <b>Order matters.</b> A recursive delete over a tree that still contains a junction
    ///     throws — verified on a developer workstation — and on some platforms would otherwise
    ///     risk deleting the link's target rather than the link. Links are therefore removed
    ///     individually first. A failure to remove a link is reported after the rest of the
    ///     cleanup has been attempted, because a silently leaked junction on a developer
    ///     machine causes confusing failures in later runs.
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
            // The reparse-point attribute, not a reported link target, is what identifies an
            // entry as a redirection. A target is reported only for a link kind the platform
            // decodes, so testing the target alone would walk into an entry that cannot be
            // opened at all and turn cleanup into a spurious failure.
            if (new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(child, recursive: false);
            }
            else
            {
                DeleteDirectoryLinks(child);
            }
        }
    }

    /// <summary>
    ///     The Windows file system calls needed to write a reparse point the platform does not
    ///     decode, which no managed API exposes.
    /// </summary>
    /// <remarks>
    ///     Declared in the test project only. Nothing shipped depends on these: they exist
    ///     solely to build the adverse arrangement the resolver must refuse, which cannot be
    ///     created through <see cref="Directory"/> or <see cref="File"/> because those create
    ///     only link kinds the platform does decode.
    /// </remarks>
    private static class Interop
    {
        /// <summary>Write access, required to set a reparse point on an entry.</summary>
        public const uint GenericWrite = 0x40000000;

        /// <summary>Opens an entry that already exists, and fails when it does not.</summary>
        public const uint OpenExisting = 3;

        /// <summary>Permits a directory, rather than a file, to be opened.</summary>
        public const uint FileFlagBackupSemantics = 0x02000000;

        /// <summary>Opens the entry itself rather than following a redirection recorded on it.</summary>
        public const uint FileFlagOpenReparsePoint = 0x00200000;

        /// <summary>The file system control that writes a reparse point onto an open entry.</summary>
        public const uint SetReparsePointControlCode = 0x000900A4;

        /// <summary>The handle value returned when an open fails.</summary>
        public static readonly IntPtr InvalidHandle = new(-1);

        /// <summary>
        ///     Opens a file system entry and returns a handle to it.
        /// </summary>
        /// <param name="path">The entry to open.</param>
        /// <param name="access">The access rights required.</param>
        /// <param name="shareMode">The sharing mode; zero for exclusive access.</param>
        /// <param name="securityAttributes">Security attributes; unused here.</param>
        /// <param name="disposition">What to do when the entry does or does not exist.</param>
        /// <param name="flags">Flags and attributes controlling how the entry is opened.</param>
        /// <param name="template">Template file; unused here.</param>
        /// <returns>A handle to the entry, or <see cref="InvalidHandle"/> on failure.</returns>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFileW(
            string path,
            uint access,
            uint shareMode,
            IntPtr securityAttributes,
            uint disposition,
            uint flags,
            IntPtr template);

        /// <summary>
        ///     Issues a file system control on an open handle.
        /// </summary>
        /// <param name="handle">The open entry to act on.</param>
        /// <param name="controlCode">The control to issue.</param>
        /// <param name="inputBuffer">The input data for the control.</param>
        /// <param name="inputSize">The length of the input data in bytes.</param>
        /// <param name="outputBuffer">The output buffer; unused here.</param>
        /// <param name="outputSize">The output buffer length; zero here.</param>
        /// <param name="returned">The number of bytes written to the output buffer.</param>
        /// <param name="overlapped">Overlapped state; unused here.</param>
        /// <returns><see langword="true"/> when the control succeeded.</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(
            IntPtr handle,
            uint controlCode,
            byte[] inputBuffer,
            int inputSize,
            IntPtr outputBuffer,
            int outputSize,
            out int returned,
            IntPtr overlapped);

        /// <summary>
        ///     Closes a handle returned by <see cref="CreateFileW"/>.
        /// </summary>
        /// <param name="handle">The handle to close.</param>
        /// <returns><see langword="true"/> when the handle was closed.</returns>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
