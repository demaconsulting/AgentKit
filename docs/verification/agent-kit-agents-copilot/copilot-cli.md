## CopilotCli Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotCli` class, its
result type, `CopilotCliSearchResult`, and the probe's process tracker, `CopilotCliProcessTree`.

### Verification Approach

`CopilotCli` is verified through unit tests of its internal seams, so no test needs a Copilot CLI on
the machine and every platform's behavior is verified on every platform:

- **Discovery** runs against `FakeCopilotCliMachine`, a test double that builds a
  `CopilotCliSearchContext` for any platform from an in-memory set of files (with content, reported
  length and execute bit) and folders. Its paths start with `/`, which is rooted on every platform, and
  each test composes its expected paths the way discovery does, so a Windows, macOS or Linux search
  order is asserted on any build machine. The drive-relative test also uses Windows drive forms
  (`C:bin`, `C:\real`), which the fake keeps as plain strings and discovery judges by the searched
  platform's rules, not the host's. One smoke test runs discovery on the real machine and checks
  only that the result is well formed, because whether a CLI is installed varies.
- **The version probe** runs through the fake machine's scripted process runner, which records the
  `ProcessStartInfo` and time limit it was given and returns a scripted exit code and output, a
  time-out, or a start failure. The probe's command line, shell use, streams, working directory,
  environment and time limit are asserted from what was recorded.
- **The real process runner** (`RunProcessAsync`) is exercised with the platform's own programs, never
  a Copilot CLI: the shell (`cmd.exe` on Windows, `/bin/sh` elsewhere) printing a version banner to
  standard output and a line to standard error and exiting with a code; a long-running program
  (`PING.EXE` on Windows, `/bin/sleep` elsewhere) stopped by a short time limit or a cancellation; and
  a shell that exits at once after starting that program in the background, which inherits and holds
  its output open past a short limit. Whether what the probe started is stopped is checked on Windows
  and Linux, where the requirement covers it, with a child the test can find: on Windows a copy of `PING.EXE`
  under a unique name in the test output folder, started by a second shell that exits so the copy's
  parent is gone; on Linux `/bin/sleep`, whose process ID the shell records in a temporary file. Each
  test waits, up to a bound of half a minute, for that child to be gone — a bound on an event the
  child would otherwise not reach for two minutes, not a timing measurement — and stops and deletes
  whatever is left in a `finally` block. A one-second lead in the Windows shells places the probe in its
  job object before the child starts, so the documented start-up window does not affect the result.
- **The version policy and the explanations** are pure functions, asserted on their output; the
  version banner used is the one a real `1.0.106` CLI prints, including its update hint.

Nothing about discovery's order, the launcher check, the probe's construction, the parsing, the policy
or the explanations is stubbed; only the file system and the process are.

**What is out of automated scope, stated honestly.** That a real installed CLI prints its version as
assumed, that the SDK accepts the minimum version, and that the guidance appears for a real old CLI are
properties of the CLI and the SDK. The opt-in live suite's installed variants show them: L0 asserts the
started runtime is at least the minimum version on the supported protocol, and the recorded run
includes the refusal of a real too-old installed CLI. That suite is supplementary evidence outside the
trace matrix, described in *AgentKitAgentsCopilot System Verification Design*.

Unit tests reside in `CopilotCliTests.cs`, and the test double in `FakeCopilotCliMachine.cs`, within the
`DemaConsulting.AgentKit.Agents.Copilot.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Copilot CLI or runtime is run, no credential is used, and no network
  access is made
- **Mocking**: `FakeCopilotCliMachine` for the file system and the process runner; no mocking framework
- **Processes**: The real-runner tests start the platform's shell and a sleeping program, bounded by
  their own time limits; on Windows two tests copy `PING.EXE` into the test output folder under a unique
  name and delete it afterwards; every other test starts nothing
- **Isolation**: No test reads or changes the process environment, apart from the one smoke test
  reading the real machine; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those explicitly asserted. A
CLI chosen out of search order, a candidate that cannot be started without a shell or lacks an execute bit chosen, a
relative or drive-relative `PATH` entry searched, a launcher chosen or not reported, a file over the launcher-scan limit
read, the same candidate searched twice, a search that does not report where it looked, a search report that changes
after it is returned; a probe run through a shell, without `--no-auto-update --version`, outside the data directory,
with an environment other than the given runtime environment and the data-directory Copilot home, without its time
limit, or of a `.js` CLI not under `node`; a version misread, or a policy that accepts a version below the minimum or
refuses one at or above it; a refusal or failure explanation missing the CLI, the versions, the command, the version
check, an install or upgrade command or the alternatives; or a real process whose output or exit code is lost, that is
not stopped on time-out or cancellation, or whose output held open past the limit is not bounded; or, on Windows or
Linux, a process holding the probe's output left running after a time-out, or on Windows a process the probe left
running not stopped when its tracking ends, constitutes a failure.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotCli-FindsTheInstalledCli: The First Runnable CLI, PATH First

**Tests**:

- `CopilotCli_FindInstalled_OnPath_ReturnsFirstPathMatch`
- `CopilotCli_FindInstalled_OnPathAndWellKnown_PrefersPath`
- `CopilotCli_FindInstalled_WindowsCmdAndPs1OnPath_AreNotCandidates`
- `CopilotCli_FindInstalled_WinGetPackage_IsFound`
- `CopilotCli_FindInstalled_WinGetLink_IsFound`
- `CopilotCli_FindInstalled_NpmLoaderOnWindows_IsFound`
- `CopilotCli_FindInstalled_HomebrewOnMacOS_IsFound`
- `CopilotCli_FindInstalled_UsrLocalBinOnMacOS_IsFound`
- `CopilotCli_FindInstalled_LocalBinOnLinux_IsFound`
- `CopilotCli_FindInstalled_LinuxbrewOnLinux_IsFound`
- `CopilotCli_FindInstalled_NpmLoaderOnUnix_IsFound` (Linux and macOS)
- `CopilotCli_FindInstalled_UnixWithoutExecuteBit_IsNotACandidate`
- `CopilotCli_FindInstalled_RelativePathEntries_AreNotSearched`
- `CopilotCli_FindInstalled_DriveRelativePathEntries_AreNotSearched`
- `CopilotCli_FindInstalled_CurrentMachine_ReturnsAResult`

Normal operation and boundaries. With `copilot.exe` in the second and third `PATH` entries, the second
is chosen and the searched locations stop there. A CLI on `PATH` wins over a WinGet link. npm's
`copilot.cmd`, `copilot.ps1` and extensionless shim on a Windows `PATH` are not candidates. With
nothing on `PATH`, the CLI is found in the later of two WinGet `GitHub.Copilot_*` package folders
after the link and the earlier folder were searched, in name order (an unrelated package is not); in
the WinGet links folder; and as npm's `npm-loader.js` under roaming application data. On macOS
Homebrew's `/opt/homebrew/bin/copilot` is found after the `PATH` entry, and `/usr/local/bin/copilot`
after `/opt/homebrew/bin` when Apple silicon Homebrew holds none; on Linux `~/.local/bin` is
preferred to `/usr/local/bin`, and Linuxbrew is found after both. npm's `npm-loader.js` is found on
Linux under `/usr/local/lib` and on macOS under `/opt/homebrew/lib` without an execute bit. On Unix a
file without an execute bit is passed over silently for the next one. `PATH` entries `.`, empty and
`bin` are never searched; on Windows the drive-relative entries `C:bin`, `c:tools` and `C:` are never
searched, while the drive roots `C:/first` and `C:\real` are, and the CLI under `C:\real` is chosen. On
the real machine the result is well formed and a selected CLI is among the searched locations.

#### AgentKitAgentsCopilot-CopilotCli-SkipsLaunchers: An Editor's Launcher Is Passed Over

**Tests**:

- `CopilotCli_FindInstalled_VsCodeShimFolder_IsSkippedAndReported`
- `CopilotCli_FindInstalled_LauncherMarkerInSmallFile_IsSkippedAndReported` (both markers)
- `CopilotCli_FindInstalled_LauncherMarkerScanLimit_LargerFileIsNotScanned` (16 MiB and one byte more)

With Visual Studio Code's `copilot-shim\copilot.exe` first on `PATH` and a real CLI second, the real
CLI is chosen and the launcher reported, naming Visual Studio Code. A small file carrying either
launcher marker — placed past the first half-megabyte, where it sits in the real launcher — is passed
over and reported, and the large real CLI after it is chosen without being read. A file carrying a
marker and reported as exactly 16 MiB is read and passed over; one byte more and it is not read, and
is chosen.

#### AgentKitAgentsCopilot-CopilotCli-ReportsWhereItLooked: Where AgentKit Looked, and What to Do

**Tests**:

- `CopilotCli_FindInstalled_NothingInstalled_ReturnsNullWithSearchedLocations`
- `CopilotCli_FindInstalled_SameCandidateTwice_IsSearchedOnce`
- `CopilotCli_ExplainNotFound_ListsSearchedSkippedAndInstallCommands`
- `CopilotCliSearchResult_Constructor_CallerListsChanged_ResultUnchanged`

Error path. On a Linux machine with nothing installed no CLI is returned; the searched locations
start with the `PATH` entry and `~/.local/bin` and include npm's global package, and nothing is
reported as skipped. The same candidate reached twice is searched once: `/usr/local/bin` on a Linux
`PATH`, which is also a well-known location, and on Windows two `PATH` entries differing only in case.
The explanation lists the searched and skipped locations, says `COPILOT_CLI_PATH`
is not consulted, names the minimum version, the WinGet, Homebrew, npm and install-script commands,
the `cliPath` option and `CopilotRuntimeSource.Bundled`. A report built from two lists keeps their
original entries after those lists are changed and cleared, and exposes them as read-only
collections.

#### AgentKitAgentsCopilot-CopilotCli-ReadsTheVersionTheRuntimeRuns: The Built-In Version, Confined

**Tests**:

- `CopilotCli_ReadVersionAsync_RunsTheCliWithNoAutoUpdateAndVersion`
- `CopilotCli_ReadVersionAsync_RunsWithTheRuntimeEnvironmentAndDataDirectoryHome`
- `CopilotCli_ReadVersionAsync_JsLoader_RunsUnderNodeFromPath`
- `CopilotCli_ReadVersionAsync_NoVersionInOutput_ReturnsNull`
- `CopilotCli_ParseVersion_CliBanner_ReturnsVersion`
- `CopilotCli_ParseVersion_PrereleaseSuffix_ReturnsCoreVersion`
- `CopilotCli_ParseVersion_NoVersion_ReturnsNull`
- `CopilotCli_RunProcessAsync_RealProcess_CapturesOutputAndExitCode`
- `CopilotCli_RunProcessAsync_ExceedsTimeout_KillsAndReportsTimedOut`
- `CopilotCli_RunProcessAsync_ExitsWithOutputHeldOpen_ReportsTimedOut`
- `CopilotCli_RunProcessAsync_Canceled_KillsAndThrowsOperationCanceled`

Normal operation and boundaries. The probe runs the CLI itself with exactly
`--no-auto-update --version`, without a shell, with all three streams redirected, in the data
directory, within `VersionProbeTimeout` (at least a minute), and returns the printed version.
Given a runtime environment carrying `PATH`, a token, `HOME` and a `COPILOT_HOME` of its own, the
probe's environment is exactly that environment with `COPILOT_HOME` set to the data directory: no
variable is missing and none is added. (That the runtime environment withholds the host's Copilot
configuration variables is verified with `CopilotRuntime`.) An `npm-loader.js` runs under the `node`
found in the second `PATH` entry, by full path, with the loader as first argument. Output naming no
version yields none. The banner `GitHub Copilot CLI 1.0.106.` with its update hint reads as that
version; a prerelease suffix is dropped, with or without the banner; output without a version, empty
and null read as none. The real runner captures a shell's version banner, its error line and its exit
code; a half-minute program is stopped by a half-second limit and reported as timed out; a shell that
exits at once while a half-minute background program it started holds its output open is reported as
timed out under a two-second limit; and one canceled after half a second is stopped and the
cancellation propagates.

#### AgentKitAgentsCopilot-CopilotCli-StopsWhatTheProbeStarted: A Stopped Probe Takes What It Started With It

**Tests**:

- `CopilotCli_RunProcessAsync_ExceedsTimeout_KillsAndReportsTimedOut`
- `CopilotCli_RunProcessAsync_Canceled_KillsAndThrowsOperationCanceled`
- `CopilotCli_RunProcessAsync_ExitsWithOutputHeldOpen_StopsTheProcessHoldingIt` (Windows and Linux;
  skipped elsewhere)
- `CopilotCliProcessTree_Dispose_ProbeLeftAChildRunning_StopsIt` (Windows; skipped elsewhere)

Error paths. A half-minute program is stopped by a half-second limit, and one canceled after half a
second is stopped with the cancellation propagated. On Windows, after a one-second lead, a shell starts
a second shell that starts a two-minute copy of `PING.EXE` under a unique name and exits, and the first
shell exits too, leaving the copy holding the output with no parent running; under a five-second limit
the run is reported as timed out and the copy is gone within the bound (stopping the tree alone does
not reach it). On Linux a shell starts a two-minute `sleep` in the background, records its process ID
and exits; under a five-second limit the run is reported as timed out and the `sleep` is gone within
the bound (the pipe-holder scan stops it; stopping the tree alone does not). On Windows a shell
tracked directly starts a two-minute copy of `PING.EXE` and exits; with the shell gone and the copy
confirmed running, ending the tracking stops the copy within the bound. On macOS only the tree is
stopped, as the requirement states, so no orphan test runs there.

#### AgentKitAgentsCopilot-CopilotCli-RequiresTheMinimumVersion: The Minimum, and Why

**Tests**:

- `CopilotCli_IsSupported_ComparesWithMinimumVersion` (six versions)
- `CopilotCli_MinimumVersion_IsTheFirstCliOnTheSupportedProtocol`
- `CopilotCli_ExplainTooOld_NamesVersionsPathAndUpgradeCommands`
- `CopilotCli_ExplainProtocolMismatch_OlderServer_GivesUpgradeGuidance`
- `CopilotCli_ExplainProtocolMismatch_NewerServer_AdvisesUpdatingAgentKit`
- `CopilotCli_ParseServerProtocol_SdkMismatchMessage_ReturnsTheServerVersion`

Boundaries and error paths. Versions just below the minimum are refused and the minimum and anything
newer accepted, including a two-part version. The minimum and the supported protocol are the
documented pair. The too-old refusal names the CLI, its version, the minimum and the protocol, the
`copilot --no-auto-update --version` check and `copilot update`, the WinGet, Homebrew, npm and
install-script upgrades, `cliPath` and `CopilotRuntimeSource.Bundled`. A runtime on an older protocol
is explained with the CLI, its version, the protocol it speaks and the upgrade guidance; one on a newer
protocol as newer than this release of AgentKit, advising an AgentKit update, `cliPath` or the bundled
runtime, and no CLI upgrade. The SDK's real mismatch message is recognized and its server protocol
read; another message is neither.

#### AgentKitAgentsCopilot-CopilotCli-ExplainsAProbeThatFails: A CLI That Cannot Report Its Version

**Tests**:

- `CopilotCli_ReadVersionAsync_JsLoaderWithoutNode_ThrowsNamingNode`
- `CopilotCli_ReadVersionAsync_TimedOut_ThrowsNamingTheTimeout`
- `CopilotCli_ReadVersionAsync_NonZeroExit_ThrowsWithStandardError`
- `CopilotCli_ReadVersionAsync_CannotStart_ThrowsNamingTheCli`
- `CopilotCli_ReadVersionAsync_ProbeQuotingASecret_IsRedacted` (cannot start, timed out, non-zero exit, no `node`)

Error paths. An `npm-loader.js` on Windows with no `node.exe` on `PATH` is refused naming the loader,
Node.js and `node.exe`, and no process runs. A probe that timed out is refused naming the CLI, the
quoted `--no-auto-update --version` arguments, the time limit in seconds and its error output. One that
exited with code 2 is refused with the code and the CLI's error. One that could not be started is
refused naming the CLI and "could not be started", with a redacted copy of the platform's failure —
naming its type, with its message — as the inner exception, never the original.

The probe's environment holds `GH_ENTERPRISE_TOKEN` set to `s3cr3t-value-xyz`, and the caller passes the host's token
`s3cr3t-host-token`; both name the CLI's folder. For a CLI that cannot be started, one that times out, one that exits
with an error and an npm installation without `node`, the refusal names the folder as `/opt/<redacted>/<redacted>/bin`,
so every probe refusal is redacted of the host's token the caller passes, in the path and the cause. The error output
holds the secret placed so that a cut of its last 500 characters made on the raw text would fall inside it, followed by
a token-shaped value: the refusal quotes `redacted> failed with gho_<redacted>`, proving the output is redacted before
it is cut, and `SecretAssert` finds neither the secret, the host's token, the secret's first or last eight characters,
nor the token-shaped value anywhere in the chain.
