using System.Diagnostics;

namespace DemaConsulting.AgentKit.Tools.Tests.File;

/// <summary>
///     Withdraws a file-system permission from a real directory for the duration of a test, and
///     restores it afterwards.
/// </summary>
/// <remarks>
///     <para>
///     A tool that walks and removes an arbitrary tree meets entries it is not allowed to
///     enumerate, and entries it is not allowed to remove. Neither is a defect and neither is a
///     model error: both are the ordinary state of a workspace holding something the process does
///     not own. The library's rule is that such a failure is returned as a refusal rather than
///     thrown, and the only way to prove that rule holds is to produce the failure for real — a
///     substitute file system would prove only that the substitute behaves consistently with
///     itself.
///     </para>
///     <para>
///     <b>Why two restrictions rather than one.</b> They fail the unit in different phases. An
///     unreadable directory fails the planning walk, which mutates nothing; a directory whose
///     contents cannot be removed fails the removal, after part of the tree has already gone.
///     Those are the two guarded paths, and they have different correct answers.
///     </para>
///     <para>
///     <b>Why two mechanisms per restriction.</b> Withdrawing a permission is a platform concept
///     with no portable managed API. On Linux and macOS <c>File.SetUnixFileMode</c> is a portable
///     managed API: clearing every bit makes a directory unreadable, and clearing the write bit
///     alone leaves it enumerable while making <c>unlink</c> of any child fail, because removing
///     an entry is a write to the directory that holds it. Windows needs a different mechanism
///     for each: a deny access-control entry on the directory's list-data right, applied with
///     <c>icacls</c>, for the first — a deny entry binds an elevated session too, which matters
///     because the Windows continuous-integration runner is elevated — and the read-only file
///     attribute for the second, because Windows permits a deletion to the holder of either the
///     file's own delete right or the parent directory's delete-child right, so denying the
///     file's alone changes nothing on a tree the test itself created. The external process is
///     used only where no managed API exists, exactly as <see cref="DirectoryLink"/> does.
///     </para>
///     <para>
///     <b>Why failure is fatal rather than skipped.</b> A skipped test leaves no entry in the
///     results, so the "a refusal is a result, never an exception" requirement would appear
///     covered with no evidence behind it — and it would appear covered on precisely the platform
///     where the mechanism silently did nothing. The helper therefore proves the permission
///     really is gone before returning, by attempting the operation the unit under verification
///     will attempt, and fails the test when it succeeds.
///     </para>
///     <para>
///     <b>Why disposal restores the permission.</b> A directory left restricted cannot be removed
///     by the surrounding fixture's recursive cleanup, which would leak the tree and, on a
///     developer workstation, fail every later run. Disposal therefore has to run before the
///     temporary-directory fixture's, which the declaration order of the <c>using</c> statements
///     guarantees.
///     </para>
///     <para>
///     Each test makes its own instance, so no state is shared between tests. Instances are not
///     thread-safe and are not intended to be used from more than one test at a time.
///     </para>
/// </remarks>
internal sealed class RestrictedDirectory : IDisposable
{
    /// <summary>
    ///     The files whose read-only attribute this fixture set, on Windows.
    /// </summary>
    /// <remarks>
    ///     Empty on the POSIX platforms and for the unreadable restriction, both of which
    ///     change the directory itself rather than its children.
    /// </remarks>
    private readonly string[] _readOnlyEntries;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RestrictedDirectory"/> class over a
    ///     directory whose permission has already been withdrawn.
    /// </summary>
    /// <remarks>
    ///     Private so the two factories are the only way to obtain one: an instance must never
    ///     exist for a directory nothing was withdrawn from, or disposal would restore something
    ///     the test never changed.
    /// </remarks>
    /// <param name="path">The full path of the directory.</param>
    /// <param name="readOnlyEntries">The files marked read-only, if any.</param>
    private RestrictedDirectory(string path, string[] readOnlyEntries)
    {
        Path = path;
        _readOnlyEntries = readOnlyEntries;
    }

    /// <summary>
    ///     Gets the full path of the restricted directory.
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     Makes an existing directory impossible to enumerate.
    /// </summary>
    /// <remarks>
    ///     The entry itself stays visible in its parent, so a walk still discovers the directory
    ///     and fails only when it tries to look inside — which is the state a walk actually
    ///     meets, rather than a directory that simply is not there.
    /// </remarks>
    /// <param name="path">The full path of the directory to close off.</param>
    /// <returns>The restriction, which lifts itself on disposal.</returns>
    public static RestrictedDirectory Unreadable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            RunIcacls($"\"{path}\" /deny *S-1-1-0:(RD)", failOnError: true);
        }
        else
        {
            System.IO.File.SetUnixFileMode(path, UnixFileMode.None);
        }

        var restriction = new RestrictedDirectory(path, []);

        // Prove the mechanism worked before any test relies on it. Enumerating is exactly what
        // the unit under verification does, so this is that operation rather than a proxy for it.
        if (!Fails(() => Directory.GetFiles(path)))
        {
            restriction.Dispose();
            Assert.Fail(
                $"'{path}' is still enumerable after its read permission was withdrawn, so this "
                + "platform cannot produce the failure the scenario exists to verify.");
        }

        return restriction;
    }

    /// <summary>
    ///     Makes the files already inside an existing directory impossible to remove, while
    ///     leaving the directory enumerable.
    /// </summary>
    /// <remarks>
    ///     Being able to list it is the point: the planning walk has to succeed so that the removal is
    ///     the phase that fails. Only the files present when this is called are protected, which
    ///     is sufficient because the scenarios build their tree first.
    /// </remarks>
    /// <param name="path">The full path of the directory whose files must survive.</param>
    /// <returns>The restriction, which lifts itself on disposal.</returns>
    public static RestrictedDirectory ContentsUndeletable(string path)
    {
        var entries = Directory.GetFiles(path);

        if (OperatingSystem.IsWindows())
        {
            // The read-only attribute, not a deny access-control entry: Windows permits a
            // deletion when the caller holds either the file's own delete right or the parent
            // directory's delete-child right, so denying the former alone changes nothing on a
            // tree the test itself created. The read-only attribute is checked separately and
            // refuses the deletion outright.
            foreach (var entry in entries)
            {
                System.IO.File.SetAttributes(entry, FileAttributes.ReadOnly);
            }
        }
        else
        {
            // Read and execute, but not write: the directory can still be listed and its entries
            // still stat'd, while removing one of them is refused.
            System.IO.File.SetUnixFileMode(
                path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        var restriction = new RestrictedDirectory(
            path, OperatingSystem.IsWindows() ? entries : []);

        if (entries.Length == 0)
        {
            Assert.Fail($"'{path}' holds no file for the scenario to protect.");
        }
        else if (!Fails(() => System.IO.File.Delete(entries[0])))
        {
            restriction.Dispose();
            Assert.Fail(
                $"'{entries[0]}' was removed despite the withdrawn permission, so this platform "
                + "cannot produce the failure the scenario exists to verify.");
        }

        return restriction;
    }

    /// <summary>
    ///     Restores the permission that was withdrawn.
    /// </summary>
    /// <remarks>
    ///     Reported as a test failure rather than a thrown exception, on the reasoning
    ///     <see cref="DirectoryLink.Dispose"/> records: throwing from a disposal path would mask
    ///     the test's own outcome.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            foreach (var entry in _readOnlyEntries)
            {
                if (System.IO.File.Exists(entry))
                {
                    System.IO.File.SetAttributes(entry, FileAttributes.Normal);
                }
            }

            if (!Directory.Exists(Path))
            {
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                RunIcacls($"\"{Path}\" /remove:d *S-1-1-0", failOnError: false);
            }
            else
            {
                System.IO.File.SetUnixFileMode(
                    Path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            Assert.Fail($"Failed to restore access to '{Path}': {exception.Message}");
        }
    }

    /// <summary>
    ///     Determines whether an operation fails the way a withdrawn permission makes it fail.
    /// </summary>
    /// <param name="operation">The file-system operation to attempt.</param>
    /// <returns>
    ///     <see langword="true"/> when the operation was refused; otherwise
    ///     <see langword="false"/>.
    /// </returns>
    private static bool Fails(Action operation)
    {
        try
        {
            operation();

            return false;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }

    /// <summary>
    ///     Runs <c>icacls</c> with the supplied arguments.
    /// </summary>
    /// <remarks>
    ///     <c>*S-1-1-0</c> is the well-known security identifier for <c>Everyone</c>, used in
    ///     place of the localized account name so the fixture works on a non-English installation.
    /// </remarks>
    /// <param name="arguments">The arguments to pass.</param>
    /// <param name="failOnError">
    ///     Whether a non-zero exit code should fail the test. Withdrawal must succeed for a
    ///     scenario to mean anything; restoration is best-effort, because the entry may already
    ///     have been removed by the unit under verification.
    /// </param>
    private static void RunIcacls(string arguments, bool failOnError)
    {
        var startInfo = new ProcessStartInfo("icacls")
        {
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            if (failOnError)
            {
                Assert.Fail($"Failed to start icacls with arguments '{arguments}'.");
            }

            return;
        }

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (failOnError && process.ExitCode != 0)
        {
            Assert.Fail(
                $"icacls {arguments} failed with exit code {process.ExitCode}: "
                + $"{standardError.Trim()} {standardOutput.Trim()}");
        }
    }
}
