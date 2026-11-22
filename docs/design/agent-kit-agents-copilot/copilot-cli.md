## CopilotCli

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotCli` static class is the installed GitHub Copilot CLI that `CopilotRuntime` runs by
default: where it is found, which version it must be, and how that version is read before the CLI is
used. `CopilotCliSearchResult` is what discovery reports.

### Purpose

The installed CLI is the default runtime because the runtime the SDK bundles adds several hundred
megabytes to every output folder of every application. Using it brings three problems the bundled
runtime does not have, and this unit owns all three:

- **Where is it?** The usual installation methods — WinGet, Homebrew, npm and the install script —
  put it in different places, not all on `PATH`, and an editor can put a launcher named `copilot` on
  `PATH` that forwards to another CLI and can prompt to install one.
- **Which version runs?** AgentKit starts the CLI with `--no-auto-update`, which makes the CLI run
  the version built into the installed executable rather than a newer one that auto-update or
  `copilot update` downloaded into the CLI's package cache. The version a user sees interactively
  (`copilot --version`) can therefore be newer than the one AgentKit runs.
- **Is that version supported?** The SDK accepts exactly one protocol version and refuses any other
  runtime with an error that tells the user to update the SDK — not the remedy for an old CLI.

Keeping this in its own unit leaves `CopilotRuntime` to decide only which executable to launch and to
verify the started runtime.

### Data Model

`CopilotCli` (public, static, stateless):

- **`MinimumVersion`** (`Version`, public) — the oldest CLI AgentKit runs: the first release that
  speaks the SDK protocol the referenced `Microsoft.Copilot.SDK` requires. Tied to the SDK reference;
  the project file's comment beside that reference says to revisit it whenever the SDK is updated.
- **`SupportedProtocolVersion`** (`const int`, internal) — the protocol version the SDK speaks and
  accepts, revisited with `MinimumVersion`.
- **`VersionProbeTimeout`** (`TimeSpan`, internal) — two minutes. A cold first run of a CLI version
  unpacks its package into the user's cache before the version is printed, and under antivirus
  scanning has been measured at well over half a minute; the limit leaves margin for a slow disk.
- Fixed guidance text: the install commands per method (`winget install GitHub.Copilot`,
  `brew install --cask copilot-cli`, `npm install -g @github/copilot`, the install script), the
  upgrade commands per method, the note on the built-in version and `copilot update`, and the
  alternatives (an explicit `cliPath`, or `CopilotRuntimeSource.Bundled`).

`CopilotCliSearchResult` (public, immutable): `Path` (`string?`, the selected CLI or `null`),
`SearchedLocations` (`IReadOnlyList<string>`, each candidate file looked for, in order) and
`SkippedCandidates` (`IReadOnlyList<string>`, `"<path> (<reason>)"` for each candidate passed over).
Built only by discovery.

Internal seams:

- **`CopilotCliSearchContext`** (record) — the platform family (`CopilotCliPlatform`: Windows, macOS,
  Linux), the `PATH` value, the home, local and roaming application data folders, and delegates for
  file existence, folder listing, execute bit, file length and file content. `Current()` builds it from
  the process and the real file system; tests build one for any platform. It also supplies the
  platform's path comparison (case-insensitive on Windows), `PATH` separator and executable name.
- **`CopilotCliProcessRunner`** (delegate) — runs a process within a time limit and returns a
  `CopilotCliProcessResult` (exit code, standard output, standard error, timed out). Production passes
  `RunProcessAsync`.
- **`CopilotCliProcessTree`** (internal, disposable) — tracks the processes a probe starts so a probe
  that is stopped is stopped with them (see `RunProcessAsync`). On Windows it holds a job object, created
  unnamed and non-inheritable with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`; on Linux it holds the `/proc`
  identities (`pipe:[inode]`) of the probe's standard output and error pipes, read from this process's
  own descriptors; elsewhere it holds nothing beyond the probe.

### Key Methods

#### FindInstalled() / FindInstalled(CopilotCliSearchContext context)

**Purpose:** Find the CLI an installed-CLI runtime runs when the host names none.

**Algorithm:**

1. List candidates in order. First every `PATH` entry rooted by the platform's rules — on Windows a
   drive root (`C:\`), a root-relative path or a UNC path; elsewhere a path starting with `/` — so
   empty, relative and drive-relative (`C:bin`) entries are skipped and no entry is resolved against the
   current directory. A root-relative Windows entry such as `\bin` is still searched and resolves on the
   current drive: the current directory's drive decides which folder that is, but no folder below the
   current directory can supply the CLI. The check is a string check on the searched platform,
   independent of the host.
   Surrounding quotes are removed. Each entry is joined with `copilot.exe` on Windows — npm's `.cmd`
   and `.ps1` shims cannot be started without a shell — or `copilot` elsewhere. Then the platform's
   well-known locations:
   - Windows: the WinGet link (`<local app data>\Microsoft\WinGet\Links\copilot.exe`), each WinGet
     `GitHub.Copilot_*` package folder in ordinal name order, and npm's global package entry point
     (`<roaming app data>\npm\node_modules\@github\copilot\npm-loader.js`).
   - macOS: Homebrew on Apple silicon and Intel, the install script's `~/.local/bin`, then npm's global
     package under each Homebrew prefix.
   - Linux: the install script's `~/.local/bin`, `/usr/local/bin`, Linuxbrew, then npm's global
     package under the usual prefixes and `~/.npm-global`.
2. Skip a candidate already seen (by the platform's path comparison), and record each one looked for.
3. Skip a candidate that does not exist or cannot be run: elsewhere than Windows an executable needs an
   execute bit; a `.js` entry point needs only to exist, because it runs with `node`.
4. Skip and report a launcher: a candidate in a `copilot-shim` folder, or a file of at most 16 MiB
   containing `VSCODE_COPILOT_RUST_SHIM_V1` (the launcher executable) or `vscode-copilot-launcher` (its
   POSIX script). A real CLI is a large single-file executable that is never read. A file that cannot
   be read is not judged a launcher.
5. Return the first candidate left, with the lists; or none, with the lists.

`COPILOT_CLI_PATH` is never read: the ambient environment does not choose the executable a confined
runtime runs. A launcher the host passes explicitly as `cliPath` is accepted, because that is the
host's choice.

**Limitation:** npm's global package is looked for only under the default prefixes listed above. An
installation under a custom npm prefix (`npm config get prefix` naming another folder) is not found:
reading npm's configuration would mean running npm through a shell or parsing `.npmrc`, which discovery
does not do, and the `copilot.cmd` shim npm puts on `PATH` cannot be started without a shell. The host
names that prefix's entry point with `cliPath`, for example
`<prefix>\node_modules\@github\copilot\npm-loader.js` on Windows.

#### ReadVersionAsync(cliPath, dataDirectory, runtimeEnvironment, context, runner, cancellationToken)

**Purpose:** Read the version the CLI runs when the SDK starts it.

**Algorithm:**

1. Build the probe: `<cli> --no-auto-update --version`, or for a `.js` entry point
   `<node> <cli> --no-auto-update --version` with `node` found on `PATH` by full path (refused with
   `InvalidOperationException` when there is none). No shell (`UseShellExecute = false`, arguments by
   `ArgumentList`), all three streams redirected, no window, the data directory as working directory,
   and an environment that is exactly the runtime environment the caller passes — the host's with its
   Copilot configuration variables withheld, as `CopilotRuntime` builds it — plus `COPILOT_HOME` set
   to the data directory. That is the environment the runtime is spawned with, less what the SDK adds
   when it spawns the runtime itself (the keychain switch and a supplied token), neither of which
   affects `--version`.
2. Run it through the runner within `VersionProbeTimeout`. A failure to start, a time-out and a
   non-zero exit are refused with `InvalidOperationException` naming the CLI, the command, the failure
   and the end of its error output.
3. Parse the version from standard output (`ParseVersion`) and return it, or `null` when it names none.

#### ParseVersion(string? output)

**Purpose:** Read a version from `--version` output.

**Algorithm:** Prefer the banner `Copilot CLI X.Y.Z` (an optional `v` allowed); otherwise take the first
`X.Y.Z` in the output. A prerelease suffix is ignored, so the core version is compared. Returns `null`
for output naming no version.

#### IsSupported(Version version)

**Purpose:** The version policy.

**Algorithm:** True when the version's major, minor and build are at least `MinimumVersion`.

#### RunProcessAsync(ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken cancellationToken)

**Purpose:** The real process runner.

**Algorithm:** Start the process and track it at once with `CopilotCliProcessTree.Track`, then close its
standard input, so a CLI that would prompt sees end of input. Read both output streams concurrently
while waiting, so neither pipe fills. Wait for exit, and then for both output streams to close, under a
token linked to the caller's and cancelled after the timeout: output held open past the limit by a
process the CLI started counts as a time-out. On time-out or cancellation, stop the probe with what it
started (`CopilotCliProcessTree.Kill`), as a best effort — a process that cannot be stopped does not
mask the result; a cancellation then propagates as `OperationCanceledException`, while a time-out
returns a result marked timed out with whatever output was captured within a short drain period. The
tracker is disposed when the run returns. If `Track` fails unexpectedly, it stops the probe's tree
before the exception propagates, so a started probe is never left running untracked.

Stopping the probe's tree (`Process.Kill(entireProcessTree: true)`) finds descendants through their
parent process IDs, so it misses a process whose parent has exited — the case of a child that holds the
probe's output open after the probe exits. What is stopped therefore differs per platform:

- **Windows** — `Track` creates the job object, sets kill-on-close and assigns the probe to it; every
  process the probe starts afterwards belongs to the job, whatever becomes of its parent. `Kill` stops
  the tree, then terminates the job. Disposing the tracker closes the job, which ends any process the
  probe left running at the end of a normal run, and the job is also closed if the host process ends
  during a probe. A process the probe starts before it is assigned to the job (the moment between
  `Process.Start` returning and the assignment) is not in the job and is stopped only while it is still
  in the tree. If the job cannot be created, configured or assigned (for example under a host job that
  forbids it), the tracker falls back to stopping the tree only. Ending a process the CLI deliberately
  left running is acceptable for a `--version` probe with automatic updates off.
- **Linux** — `Track` records the identities of the probe's output pipes. `Kill` stops the tree, then
  scans `/proc/<pid>/fd` of every other process and stops, with its tree, each one holding either pipe
  open — exactly the processes keeping the probe's output open, orphaned or not. Each holder is checked
  again once it is opened and just before it is stopped, so a process ID reused since the scan is not
  stopped; a reuse in the moment between that check and the stop, or a process the host forks at that
  moment that holds the pipe until its `exec` closes it, remains a theoretical race. Processes the scan
  cannot read (another user's) are passed over. A process that has left the tree and holds neither pipe
  is not found. If the output streams are not pipes, only the tree is stopped.
- **macOS and other platforms** — `Kill` stops only the processes still in the tree. .NET 8 to 10 offer
  no managed means to find an orphan there; the library's ends of the pipes are closed when the run
  returns, so nothing in the library waits on such a process.

#### Explanations

- **`ExplainNotFound(result)`** — where AgentKit looked, what it passed over, that `COPILOT_CLI_PATH`
  is not consulted, the install command per method with the minimum version, and the alternatives.
- **`ExplainExplicitPathMissing(cliPath)`** — the named CLI does not exist; name an installed CLI,
  leave `cliPath` unset, or use the bundled runtime.
- **`ExplainTooOld(cliPath, installed)`** — the CLI, its version, the minimum and its protocol; that
  AgentKit starts the CLI with `--no-auto-update`, which runs the version built into the installed
  executable, and how to check it (`copilot --no-auto-update --version`); that a newer version
  auto-update or `copilot update` downloaded is kept in the CLI's package cache, where
  `copilot --version` can report it, but is not what AgentKit runs, so only upgrading the installation
  changes it; the upgrade command per method; and the alternatives.
- **`ExplainProtocolMismatch(cliPath, cliVersion, serverProtocol)`** — for a runtime reporting an older
  protocol (or none), the too-old guidance; for a newer protocol, that the CLI is newer than this
  release of AgentKit, with the remedies of updating AgentKit, running a supported CLI by `cliPath`,
  or the bundled runtime. `IsProtocolMismatch` recognizes the SDK's error and `ParseServerProtocol`
  reads the protocol the runtime reported from it.

### Error Handling

| Condition | Handling |
| --- | --- |
| A candidate file cannot be read during the launcher check | Not judged a launcher; the probe decides |
| A well-known folder is missing or unreadable | Listed as empty |
| `.js` CLI without `node` on `PATH` | `InvalidOperationException` naming Node.js |
| Probe cannot start | `InvalidOperationException` naming the CLI and command; the cause is the inner exception |
| Probe exits non-zero | `InvalidOperationException` with the exit code and error output |
| Probe or its output outlasts the limit | Stopped with what it started; `InvalidOperationException` naming the limit |
| A child holds the output past the limit | Windows: job ended; Linux: pipe holders stopped; macOS: tree only |
| Probe canceled | Stopped with what it started; `OperationCanceledException` propagates |
| Job object cannot be set up (Windows) | Tree stopped only |
| Tracking fails unexpectedly | Probe tree stopped; the exception propagates |
| Output names no version | `null`; the SDK handshake decides |

Refusing the CLI (not found, too old) is `CopilotRuntime`'s decision, made with these explanations.

### Dependencies

- **.NET base class library** — `System.Diagnostics.Process` and the file system; on Windows, the
  kernel32 job object functions by P/Invoke (`CreateJobObjectW`, `SetInformationJobObject`,
  `AssignProcessToJobObject`, `TerminateJobObject`, `CloseHandle`); no OTS item.
- No other unit: the runtime environment the probe runs under is passed in by `CopilotRuntime`, so
  this unit does not depend on it.

### Callers

`CopilotRuntime.ResolveLaunchAsync` calls `FindInstalled`, `ReadVersionAsync`, `IsSupported` and the
explanations; `CopilotRuntime.ExplainStartFailure` calls `IsProtocolMismatch`, `ParseServerProtocol`
and `ExplainProtocolMismatch`. A host calls `FindInstalled()` to show a user which CLI AgentKit would
run, and reads `MinimumVersion` for its own diagnostics.
