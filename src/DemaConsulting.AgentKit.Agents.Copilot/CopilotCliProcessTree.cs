using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     Tracks the processes a version probe starts, so a probe that runs too long or is canceled is
///     stopped together with them.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Process.Kill(bool)"/> finds a process's descendants through their parent process
///         IDs, so it misses a process whose parent has exited: elsewhere than Windows such a process is
///         adopted by another process, and on Windows nothing links it to the probe any more. A child the
///         probe left running, or a grandchild whose parent exited, can therefore hold the probe's output
///         open after the probe has gone. What is stopped differs per platform:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 Windows: the probe is placed in a job object as soon as it starts, so every process it
///                 starts afterwards belongs to the job, whether or not the probe is still running.
///                 <see cref="Kill"/> ends every process in the job, and <see cref="Dispose"/> closes the job,
///                 which ends any process the probe left running when the run ends. A process the probe
///                 starts before it is placed in the job is not in the job; it is stopped only while it is
///                 still in the probe's tree. When the job cannot be created or assigned, only the tree is
///                 stopped.
///             </description>
///         </item>
///         <item>
///             <description>
///                 Linux: <see cref="Kill"/> stops the probe's tree, then every other process that holds the
///                 probe's output pipes open, together with its tree, found through <c>/proc</c>. A process
///                 that holds neither pipe and has left the probe's tree is not found.
///             </description>
///         </item>
///         <item>
///             <description>
///                 Other platforms (macOS): <see cref="Kill"/> stops only the processes still in the probe's
///                 tree.
///             </description>
///         </item>
///     </list>
///     Every step is a best effort: a process that cannot be stopped does not change the probe's result.
/// </remarks>
internal sealed partial class CopilotCliProcessTree : IDisposable
{
    /// <summary>
    ///     <c>JobObjectExtendedLimitInformation</c>, the job information class carrying the limit flags.
    /// </summary>
    private const int JobObjectExtendedLimitInformationClass = 9;

    /// <summary>
    ///     <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>: closing the job's last handle ends its processes.
    /// </summary>
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    /// <summary>
    ///     The probe.
    /// </summary>
    private readonly Process _process;

    /// <summary>
    ///     The job object the probe runs in on Windows, or <see langword="null"/> elsewhere or when it could
    ///     not be set up.
    /// </summary>
    private readonly SafeJobHandle? _job;

    /// <summary>
    ///     The identities of the probe's output pipes on Linux (<c>/proc</c> link targets such as
    ///     <c>pipe:[12345]</c>); empty elsewhere.
    /// </summary>
    private readonly IReadOnlyList<string> _pipes;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotCliProcessTree"/> class.
    /// </summary>
    /// <param name="process">The probe.</param>
    /// <param name="job">The probe's job object, if any.</param>
    /// <param name="pipes">The probe's output pipe identities, if any.</param>
    private CopilotCliProcessTree(Process process, SafeJobHandle? job, IReadOnlyList<string> pipes)
    {
        _process = process;
        _job = job;
        _pipes = pipes;
    }

    /// <summary>
    ///     Starts tracking a probe; call it as soon as the probe has started.
    /// </summary>
    /// <param name="process">The started probe, with standard output and error redirected.</param>
    /// <returns>The tracker; dispose it when the run ends.</returns>
    /// <remarks>
    ///     If tracking fails unexpectedly, the probe and its tree are stopped before the exception propagates.
    /// </remarks>
    public static CopilotCliProcessTree Track(Process process)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return new CopilotCliProcessTree(process, CreateJob(process), []);
            }

            if (OperatingSystem.IsLinux())
            {
                return new CopilotCliProcessTree(process, null, PipeIdentities(process));
            }

            return new CopilotCliProcessTree(process, null, []);
        }
        catch
        {
            // An unexpected failure must not leave the started probe running untracked
            KillTree(process);
            throw;
        }
    }

    /// <summary>
    ///     Stops the probe and the processes it started, as far as the platform allows.
    /// </summary>
    public void Kill()
    {
        KillTree(_process);

        if (_job is not null && OperatingSystem.IsWindows())
        {
            _ = NativeMethods.TerminateJobObject(_job, 1);
        }

        if (_pipes.Count > 0 && OperatingSystem.IsLinux())
        {
            KillPipeHolders(_pipes);
        }
    }

    /// <summary>
    ///     Ends tracking; on Windows this closes the job, ending any process the probe left running.
    /// </summary>
    public void Dispose() => _job?.Dispose();

    /// <summary>
    ///     Stops a process and every process still in its tree, as a best effort.
    /// </summary>
    /// <param name="process">The process.</param>
    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or AggregateException
            or NotSupportedException)
        {
            // It exited on its own, or a process in its tree could not be stopped; the probe's result stands.
        }
    }

    /// <summary>
    ///     Creates a job object that ends its processes when closed, and places the probe in it.
    /// </summary>
    /// <param name="process">The started probe.</param>
    /// <returns>The job, or <see langword="null"/> when it could not be created or assigned.</returns>
    [SupportedOSPlatform("windows")]
    private static SafeJobHandle? CreateJob(Process process)
    {
        var job = NativeMethods.CreateJobObjectW(nint.Zero, nint.Zero);
        if (job.IsInvalid)
        {
            job.Dispose();
            return null;
        }

        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose },
        };
        try
        {
            if (NativeMethods.SetInformationJobObject(
                    job,
                    JobObjectExtendedLimitInformationClass,
                    ref limits,
                    (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>())
                && NativeMethods.AssignProcessToJobObject(job, process.SafeHandle))
            {
                return job;
            }
        }
        catch (InvalidOperationException)
        {
            // The probe's handle is unavailable; fall back to stopping its tree.
        }

        job.Dispose();
        return null;
    }

    /// <summary>
    ///     Reads the identities of the probe's standard output and error pipes on Linux.
    /// </summary>
    /// <param name="process">The started probe.</param>
    /// <returns>The pipe identities; empty when they cannot be read.</returns>
    [SupportedOSPlatform("linux")]
    private static string[] PipeIdentities(Process process)
    {
        var identities = new List<string>(2);
        foreach (var stream in new[] { process.StandardOutput.BaseStream, process.StandardError.BaseStream })
        {
            // Outside Windows .NET exposes redirected output as an AnonymousPipeClientStream over the
            // pipe's descriptor; FileStream is only the Windows implementation, where the job object is
            // used instead. A stream of any other kind is skipped, leaving only the tree kill.
            if (stream is not PipeStream pipe)
            {
                continue;
            }

            // The descriptor number is only read, while the stream is open, to name this process's own
            // /proc entry for it; the handle itself is never used outside its SafeHandle
#pragma warning disable S3869
            var descriptor = pipe.SafePipeHandle.DangerousGetHandle();
#pragma warning restore S3869
            if (ReadLink($"/proc/self/fd/{descriptor}") is { } target
                && target.StartsWith("pipe:", StringComparison.Ordinal))
            {
                identities.Add(target);
            }
        }

        return [.. identities];
    }

    /// <summary>
    ///     Stops every other process holding one of the pipes open, with its tree, on Linux.
    /// </summary>
    /// <param name="pipes">The pipe identities.</param>
    [SupportedOSPlatform("linux")]
    private static void KillPipeHolders(IReadOnlyList<string> pipes)
    {
        string[] processes;
        try
        {
            processes = Directory.GetDirectories("/proc");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var directory in processes)
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid)
                || pid == Environment.ProcessId
                || !HoldsAny(directory, pipes))
            {
                continue;
            }

            try
            {
                using var holder = Process.GetProcessById(pid);

                // Re-checked once opened, so a process ID reused since the scan is not stopped
                if (!HoldsAny(directory, pipes))
                {
                    continue;
                }

                KillTree(holder);
            }
            catch (ArgumentException)
            {
                // It exited after it was found.
            }
        }
    }

    /// <summary>
    ///     Determines whether a process holds one of the pipes open, as a best effort.
    /// </summary>
    /// <param name="directory">The process's <c>/proc</c> directory.</param>
    /// <param name="pipes">The pipe identities.</param>
    /// <returns><see langword="true"/> when one of its descriptors is one of the pipes.</returns>
    private static bool HoldsAny(string directory, IReadOnlyList<string> pipes)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(Path.Combine(directory, "fd"))
                .Any(descriptor => ReadLink(descriptor) is { } target && pipes.Contains(target));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // It exited, or its descriptors belong to another user.
            return false;
        }
    }

    /// <summary>
    ///     Reads a link's target, as a best effort.
    /// </summary>
    /// <param name="path">The link.</param>
    /// <returns>The target, or <see langword="null"/> when it cannot be read.</returns>
    private static string? ReadLink(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    ///     A job object handle, closed when released.
    /// </summary>
    internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="SafeJobHandle"/> class, for interop.
        /// </summary>
        public SafeJobHandle()
            : base(ownsHandle: true)
        {
        }

        /// <inheritdoc/>
        protected override bool ReleaseHandle() =>
            OperatingSystem.IsWindows() && NativeMethods.CloseHandle(handle);
    }

    /// <summary>
    ///     <c>JOBOBJECT_BASIC_LIMIT_INFORMATION</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    /// <summary>
    ///     <c>IO_COUNTERS</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    /// <summary>
    ///     <c>JOBOBJECT_EXTENDED_LIMIT_INFORMATION</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    /// <summary>
    ///     The kernel32 job object functions.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static partial class NativeMethods
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial SafeJobHandle CreateJobObjectW(nint jobAttributes, nint name);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetInformationJobObject(
            SafeJobHandle job,
            int informationClass,
            ref JobObjectExtendedLimitInformation information,
            uint length);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool TerminateJobObject(SafeJobHandle job, uint exitCode);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(nint handle);
    }
}
