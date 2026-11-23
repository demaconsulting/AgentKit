## CopilotRuntime

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotRuntime` class is the GitHub Copilot runtime AgentKit starts, confines and owns.
`CopilotRuntimeOptions` carries the few options a host may give it, and `CopilotRuntimeSource` names
the two executables it can run: the Copilot CLI installed on the machine (the default) or the runtime
`Microsoft.Copilot.SDK` bundles. Every agent, provider-session factory and summarizer in this system
runs on a `CopilotRuntime`; none accepts a client the host built.

### Purpose

**The confinement a Copilot session needs is only partly a property of the session.** The rest belongs
to the client the session runs on. The client's mode, its data directory and the environment of its
runtime decide which MCP servers, plugins and configuration exist before any session option is read,
and the SDK offers no way to read any of them back from a client. Measured against the live runtime:

- **Empty mode alone does not prevent ambient MCP servers.** It starts the built-in GitHub MCP server
  on a session's first turn, and it executes an MCP server that the data directory's `mcp-config.json`
  configures.
- **In-process hosting gives the runtime the host's whole environment.** The runtime reads hundreds of
  `COPILOT_` variables, among them approve-all switches, instruction, skill, hook and plugin
  directories, provider rerouting and a runtime path override (`COPILOT_CLI_PATH`) that every Copilot
  CLI shell sets. A library cannot change that without mutating process-global state.
- **A client of the host's can be anything.** It can be in CodingAgent mode, use a shared Copilot CLI
  home, or connect to an external runtime whose mode, data directory and environment are not the
  client's at all.

So AgentKit does not take a client; it builds one. Only options that cannot widen what reaches the
model are expressible, which matches the project's principle that an unsafe configuration should be
impossible to express rather than refused when used.

**Which executable runs is a separate decision from how it is confined.** By default the runtime is
the Copilot CLI installed on the machine, so an application ships no runtime; the runtime the SDK
bundles is the opt-in for a machine without a suitable CLI. The choice is the host's code — an
explicit CLI path or `CopilotRuntimeSource.Bundled` — and never the ambient environment. Whichever
executable is chosen, everything below the executable is the same code path: Empty mode, a child
process over stdio, the withheld environment and the data directory AgentKit owns. Finding the CLI
and checking its version belong to the *CopilotCli Unit Design*; this unit decides which executable
to launch, launches it, and verifies the started runtime.

### Data Model

`CopilotRuntimeOptions` (immutable, safe for concurrent use):

- **`DataDirectory`** (`string`) — the full path of the runtime's data directory. A relative path is
  resolved against the current directory when the options are built; a null, empty or whitespace
  path is refused.
- **`GitHubToken`** (`string?`) — the token to sign in with, or `null` (a blank token is
  normalized to `null`) to let the next sign-in source apply (see the *CopilotSignIn Unit Design*).
- **`GitHubHost`** (`string?`) — the GitHub host to sign in to, normalized to `https://name` in lower
  case, or `null` to leave it to the sign-in source. `example.ghe.com`, `https://example.ghe.com` and
  `https://example.ghe.com/` are accepted; another scheme, a path, query, fragment, user name or port is
  refused with `ArgumentException` naming `gitHubHost`. A blank host is `null`.
- **`GitHubTokenProvider`**
  (`Func<CopilotGitHubTokenRequest, CancellationToken, Task<CopilotGitHubToken>>?`) — a callback that
  supplies tokens, asked once at start and again by each session; `null` for none. Given together with
  a non-blank `gitHubToken` it is refused with `ArgumentException` naming `gitHubTokenProvider`, since
  one of the two would otherwise be silently ignored.
- **`UseCopilotCliSignIn`** (`bool`, default `true`) — whether the user's Copilot CLI sign-in may be
  used. `false` removes that source; the refusal message then reports it as turned off by the host.
- **`LogLevel`** (`CopilotLogLevel?`) — the runtime's log level, or `null` for its default.
- **`Logger`** (`ILogger?`) — a logger for the SDK's diagnostic output.
- **`RuntimeSource`** (`CopilotRuntimeSource`) — `InstalledCli` (the default) or `Bundled`; an
  undefined value is refused with `ArgumentOutOfRangeException`.
- **`CliPath`** (`string?`) — the installed CLI to run, or `null` to let discovery choose. A blank
  path is normalized to `null`; a path that is not fully qualified, names a batch file (`.cmd`/`.bat`,
  which Windows would run through `cmd.exe`), is not a valid path, or is given with `Bundled` is
  refused with `ArgumentException` naming `cliPath`. Its existence is checked at
  start, because a CLI may be installed after the options are built. Always `null` for `Bundled`.

`CopilotRuntimeSource` (public enumeration): `InstalledCli = 0`, `Bundled = 1`.

`CopilotGitHubTokenRequest` (public, immutable): what AgentKit asks a token provider for — `Host`
(`https://github.com` or a GitHub Enterprise Cloud host such as `https://example.ghe.com`) and
`IsRefresh` (`false` at start and for a session's first token, `true` when the runtime replaces a
token about to expire). Constructed only by AgentKit.

`CopilotGitHubToken` (public, immutable): what a provider returns — `Token` (non-blank, else
`ArgumentException`) and `ExpiresIn` (`TimeSpan?`; `null` when not known, which AgentKit reports to
the runtime as eight hours; when given it must exceed one hour, the runtime's minimum, else
`ArgumentOutOfRangeException`). `ToString` names the token's length, never the token.

`CopilotSignInSource` (public enumeration): `Unknown`, `GitHubToken`, `GitHubTokenProvider`,
`EnvironmentVariable`, `CopilotCli`, `DataDirectory`, `GitHubCli` (see the *CopilotSignIn Unit
Design*).

`CopilotRuntimeLaunch` (internal record): what `StartAsync` launched — the `Source`, the installed
CLI's `CliPath` (`null` for the bundled runtime) and the `CliVersion` it reported (`null` for the
bundled runtime or when its output named none). It travels from `ResolveLaunchAsync` to
`StartClientAsync`, so a start failure is explained for what was actually launched.

`CopilotRuntime`:

- **`_client`** (`CopilotClient?`) — the client this runtime owns; `null` once disposed. Exposed
  internally as `Client`, which throws `ObjectDisposedException` (object name `CopilotRuntime`) after
  disposal. Never exposed publicly, so nothing outside AgentKit can open a session on it or
  reconfigure it. Never captured either: everything built on the runtime reads `Client` at each open,
  resume and release, because the SDK restarts a disposed client on demand — its start, create,
  resume and delete calls all reconnect a client that is not running.
- **`DataDirectory`** (`string`) — the prepared data directory's full path; available after disposal.
  The runtime's credentials and kept conversations live there. Its caches do not: the runtime writes
  them to the platform cache folder (`%LOCALAPPDATA%` on Windows, `XDG_CACHE_HOME` or its default
  elsewhere), because the SDK sets `COPILOT_CACHE_HOME` only for in-process hosting and the host's own
  copy is withheld.
- **`CliPath`** (`string?`) — the installed CLI the runtime runs, or `null` for the bundled runtime
  (and for a runtime a test built without a launch); available after disposal.
- **`RuntimeVersion`** (`string`) and **`ProtocolVersion`** (`int`) — what the started runtime
  reported through the SDK's status call, so a host can log the version that actually runs; empty and
  0 for a runtime a test built without a status; available after disposal.
- **`SignInSource`** (`CopilotSignInSource`), **`GitHubHost`** (`string?`) and **`GitHubLogin`**
  (`string?`) — how the started runtime signed in: the source classified from the `authType` the
  runtime's authentication status reports (`CopilotSignIn.Classify`), and the host and login it
  reports. `Unknown` and `null` for a runtime built without a sign-in plan or authentication status;
  available after disposal.
- **`SignIn`** (`CopilotSignInPlan?`, internal) — the sign-in plan the runtime started with, or `null`
  for a runtime built without one. Its `Redact` removes the start token, the environment's token
  values and every session token the host's provider has returned from text AgentKit quotes, such as
  a failed reconnect's, and from the redacted copy of a failure kept as its cause. The agent, provider
  session and model catalog built on the runtime redact with it too.
- **`_sessionTokenProvider`** — the SDK session token provider built from the plan's token provider
  (`CopilotSignIn.CreateSessionTokenProvider`, given the plan's `Secrets`, into which it records each
  session token), or `null` when the runtime was not started with one.
- **`PassThroughVariables`** (`FrozenSet<string>`) — the `COPILOT_` variables that reach the runtime:
  `COPILOT_GITHUB_TOKEN`, `COPILOT_GH_HOST`, `COPILOT_SYSTEM_PROXY`, `COPILOT_PROXY_KERBEROS`,
  `COPILOT_PROXY_KERBEROS_SPN`, `COPILOT_TELEMETRY_DISABLED` and `COPILOT_GITHUB_TELEMETRY_DISABLED`.
  Each carries only a credential, network settings, or a switch that can only turn telemetry off.
- **`OwnershipMarkerFileName`** (`const string`) — `.agentkit-copilot-runtime`, the file that marks a
  data directory as AgentKit's.
- **`OwnershipMarkerContent`** (`ReadOnlySpan<byte>`, a UTF-8 literal) — the marker's exact bytes:
  UTF-8 without a byte order mark, LF line endings and a final line feed. The first line,
  `AgentKit Copilot runtime data directory, marker format 1`, is a fixed signature with the format
  version; the rest tells a person who finds the file what the directory is and not to edit it. It
  holds no secret, path or time stamp, so it is the same on every machine.

### Key Methods

#### StartAsync(CopilotRuntimeOptions options, CancellationToken cancellationToken)

**Purpose:** The only way to obtain a runtime: build the confined client and start it.

**Algorithm:**

1. Reject null options; honor a cancellation already requested.
2. Prepare the data directory (`PrepareDataDirectory`), so a foreign directory is refused before any
   discovery, probe or runtime reads it.
3. Read the process environment once.
4. Resolve the launch (`ResolveLaunchAsync`) with the current machine's search context and the real
   process runner: for the installed CLI, find it and check its version, refusing an unsupported one
   before any client exists.
5. Resolve the sign-in (`CopilotSignIn.ResolveAsync`) with the current machine's sign-in context: the
   host's token or token provider, a token in the environment, the user's Copilot CLI sign-in
   (through the token-fetch helper, on the same executable as the launch) or the runtime's own
   fallbacks, each with its host. A token provider that fails, or a `COPILOT_GH_HOST` that is not a
   valid host when no `gitHubHost` was given, refuses the start here, before any runtime process.
6. Build the client options (`BuildClientOptions`) from the options, the prepared directory, the
   environment, the resolved CLI path and the sign-in plan.
7. Create the client and start it through `StartOwnedAsync`, whose start step is `StartClientAsync`
   over the client's own start with the launch and the sign-in plan, followed by
   `VerifyStartedAsync` over the client's
   status and authentication reads and the sign-in plan. A failed, canceled or unauthenticated start
   disposes the client.
8. Return a runtime over the started client, carrying the launch, the status, the sign-in plan and the
   authentication status, and log the sign-in through the host's logger (`CopilotSignIn.Report`).

**Postconditions:** The runtime process — the installed CLI or the bundled runtime — is running on
the prepared data directory and is signed in; the caller owns the runtime and disposes it.

#### ResolveLaunchAsync(options, dataDirectory, hostEnvironment, context, runner, cancellationToken)

**Purpose:** Decide which executable a runtime runs, and check an installed CLI before it is used.

**Algorithm:**

1. For `Bundled`, return a bundled launch: nothing is searched or run, because the SDK provisioned
   that runtime and pins its version.
2. Otherwise take the host's `CliPath` when one is given, refusing it with `InvalidOperationException`
   (`CopilotCli.ExplainExplicitPathMissing`) when the search context reports no such file; else run
   discovery (`CopilotCli.FindInstalled(context)`), refusing with `CopilotCli.ExplainNotFound` when it
   finds none. `COPILOT_CLI_PATH` is never read.
3. Read the CLI's version (`CopilotCli.ReadVersionAsync`), passing `BuildRuntimeEnvironment` of the host's environment,
   so the probe runs under the runtime's own environment and data directory. A version below `CopilotCli.MinimumVersion`
   is refused with `CopilotCli.ExplainTooOld`. Output that names no version is accepted, because the SDK's protocol
   handshake at start is authoritative and fails closed.
4. Return an installed launch with the path and the version.

The search context and the process runner are parameters, so every outcome is tested on a fake
machine without a CLI.

#### VerifyStartedAsync(readStatus, readAuthentication, signIn, cancellationToken)

**Purpose:** Read what the started runtime is and refuse one that is not signed in.

**Algorithm:** Read the status (version and protocol version), then the authentication status. When
the runtime is not authenticated, throw `InvalidOperationException` with
`CopilotSignIn.ExplainUnauthenticated(signIn, authentication)`, which lists what each sign-in source
found; otherwise return the status and the authentication status. Run inside `StartOwnedAsync`'s
start step, so a refused runtime's client is disposed. The reads are delegates so the outcome is
testable with fabricated SDK responses.

#### ApplySessionSignIn(SessionOptionsBase config)

**Purpose:** Give a session the runtime's session-level sign-in.

**Algorithm:** When the runtime was started with the host's token provider, set the session's
`GitHub.TokenProvider` to the provider built at construction, so the runtime asks the host's provider
for the session's token and refreshes it; otherwise leave the session's GitHub options untouched.
Called by the *CopilotTurnChannel Unit Design*'s create and resume paths before anything else.

#### PrepareDataDirectory(string dataDirectory)

**Purpose:** Make sure the data directory is one AgentKit owns.

**Algorithm:** Resolve the full path. A path naming an existing file is refused. For an existing
directory, look for an entry with the marker's name without following links; unlike `File.Exists`,
this finds a directory, a junction and a dangling link as well as a file. When the entry exists it is
validated, checking in this order and following no link:

1. A symbolic link, junction or other reparse point (`LinkTarget` set or `ReparsePoint` attribute) is
   invalid.
2. A directory is invalid.
3. A length other than `OwnershipMarkerContent.Length` is invalid. This is checked before the file is
   opened, so it caps what is read and refuses a FIFO or device without blocking on it.
4. The file is read, one byte past the marker's length so a file that grew since its length was read
   is refused, and compared byte for byte with `OwnershipMarkerContent`.

A valid marker accepts the directory as it is, whatever the runtime has written there since. An
invalid one refuses it with an `InvalidOperationException` naming the directory, saying its
`.agentkit-copilot-runtime` entry "is not the marker AgentKit writes", giving the reason, and telling
the host to delete a directory an earlier build created or to use a new one. A non-empty directory
without the entry is refused with an `InvalidOperationException` naming the directory and explaining
that it was not created by AgentKit (it has no marker). A missing directory is created with its
parents.

No variant of the bytes is tolerated — no byte order mark, CRLF line endings or a different final
line feed. AgentKit is the marker's only intended writer and writes one fixed form that depends on
neither the platform newline nor encoder settings; a data directory is not checked out of git or
passed through an editor, and copying it preserves bytes. A marker someone edited is no longer
evidence that AgentKit created the directory, and a wrong refusal fails closed with a remedy.

The marker is written only after the directory is known to be new or empty, so a refused directory is
left untouched. `MarkDataDirectory` creates it with `FileMode.CreateNew`, so an existing entry is
never overwritten. When another writer created the entry first — `CreateNew` then reports
`IOException` for a file, or `UnauthorizedAccessException` for a directory or junction — the entry is
validated as above and the directory accepted or refused accordingly. A write that fails after the
file was created deletes the partial file, as a best effort, and the failure propagates. A concurrent
preparation of the same directory therefore fails closed.

When `MarkDataDirectory` created the marker, it enumerates the directory again before releasing the
`CreateNew` handle (opened with `FileShare.None`). Any entry besides the marker means another writer
populated the path between the emptiness check and the marker, so the marker is emptied through that
handle with `SetLength(0)` — touching only the file created here — then deleted as a best effort, and
the directory is refused with the same not-created-by-AgentKit `InvalidOperationException` as any
other foreign directory. A leftover marker after a failed delete is empty, so the next start refuses
it as invalid rather than trusting it. If the re-enumeration itself fails, the marker is emptied
through the same handle as a best effort before the failure propagates and the delete is attempted,
so a leftover is invalid too. A marker another writer created first is the reuse path: when
valid it is accepted without enumerating, as on a later start — the call that writes the marker is
the one that checks. For tests, an internal `PrepareDataDirectory(string, Action<string>?)` overload
invokes a `beforeMark` callback with the full path just before the directory is marked, so a test can
populate it as a concurrent writer could; the production overload passes `null`. Likewise, an
internal `MarkDataDirectory(string, Func<string, bool>, Action<string>)` overload takes the
re-enumeration and the best-effort delete, so a test can make both fail; the production overload
passes the real ones.

**What the marker does and does not guarantee.** The marker guards against reusing a foreign Copilot
home or a shared folder by mistake. It is not a security boundary against a process that can already
write to the directory: such a process can write the exact marker, swap the entry for a link between
the check and the read, or add configuration after the check, and a hard link to a marker elsewhere
is not detected. The guarantee rests on the host keeping the directory private to the application.
Re-enumerating after the marker is written narrows, but does not close, the window for a concurrent
writer: content added after it, or (on Unix, where the lock is advisory) an entry swapped for
another, is not detected.

A fresh directory is what keeps user-configured MCP servers, installed plugins, user hooks and trusted
folders from existing at all. A directory another tool populated is refused rather than inspected,
because a list of files to object to would go stale with every runtime release. The runtime's own
content in a marked directory is not policed; an MCP server added there later is still found by
discovery and disabled before a session is created (see *CopilotTurnChannel Unit Design*).

**Postconditions:** The directory exists, carries exactly the marker AgentKit writes, and its full
path is returned.

#### BuildRuntimeEnvironment(IDictionary hostEnvironment)

**Purpose:** Build the runtime process's replacement environment.

**Algorithm:** Copy every host variable except those whose name starts with `COPILOT_` or
`GITHUB_COPILOT_`, unless the name is in `PassThroughVariables`. Names are compared case-insensitively
on Windows, whose variable names are, and ordinally elsewhere — the rule the SDK applies when it reads
the replacement environment. An entry whose name or value is not a string is skipped. The SDK then
adds its own settings — the data directory as `COPILOT_HOME`, the switch that keeps credentials out
of the system keychain, and a supplied token — so those cannot be overridden from the host either.

#### BuildClientOptions(CopilotRuntimeOptions options, string dataDirectory, IDictionary hostEnvironment, string? cliPath)

**Purpose:** The single place the client is configured.

**Algorithm:** Use the sign-in plan given, or, when none is (a test's client), the plan the options
alone describe (`CopilotSignInPlan.FromOptions`: the host's token and chosen host). Build the
runtime's environment with `BuildRuntimeEnvironment(hostEnvironment)` and, when the plan names a host,
set `COPILOT_GH_HOST` to its bare host name, overriding the host environment's value — which, when it
named a valid host, is the same host, normalized, and when it named an invalid one already refused
the start. Set `Mode` to
`CopilotClientMode.Empty` and `Runtime` to
`RuntimeConnection.ForSpawnedStdio(path: cliPath, environment: <that environment>,
baseDirectory: dataDirectory)`, with no arguments and no child working directory. `cliPath` is the
CLI `ResolveLaunchAsync` resolved, or `null` (the parameter's default) for the runtime the SDK
provisions; the SDK would otherwise consult `COPILOT_CLI_PATH`, but reads it from the replacement
environment, which withholds it. Copy the plan's token, and the options' log level and logger.
Everything else keeps the SDK's default, which for every capability channel is none, and is identical
for both executables.

The SDK's client options, classified:

| Option | Classification | Rationale |
| --- | --- | --- |
| `Mode` | Forced: Empty | No coding-agent defaults; credentials in the data directory |
| `Runtime` spawned over stdio | Forced | A child process whose environment AgentKit decides |
| ↳ `BaseDirectory` | Forced | The prepared data directory |
| ↳ `Environment` | Forced | The host's minus Copilot configuration variables |
| ↳ `Path` | Forced | The resolved installed CLI, or none for the bundled runtime |
| ↳ `Args` | Not expressible | Flags such as approve-all |
| ↳ `WorkingDirectory`, `SessionIdleTimeoutSeconds` | Not exposed | Neutral; sessions carry a working directory |
| In-process, spawned TCP | Not expressible | The host's whole environment; a TCP listener |
| External TCP, default | Not expressible | A runtime not AgentKit's; an environment switch |
| `SessionFs` | Not expressible | Moves state out of the data directory |
| `InternalTestOptions` | Not expressible | Injects environment variables |
| `Plugins.BuiltinDirectories` | Not expressible | Registers plugins with servers, hooks, skills and agents |
| `Extensions.LaunchProvider` | Not expressible | A channel for injected capability |
| `Installations.ConfirmationHandler` | Not expressible | Plugin and MCP installation flows |
| `Models.RequestHandler` | Not expressible | Intercepts every inference request |
| `GitHub.Token` | Allowed | The resolved sign-in's token (host's, provider's or the Copilot CLI's) |
| ↳ `Environment["COPILOT_GH_HOST"]` | Derived | The resolved sign-in's host, a pass-through variable |
| `Observability.LogLevel`, `Observability.Logger` | Allowed | Host-side diagnostics |
| `GitHub.UseLoggedInUser` | Derived | The SDK default: the signed-in user unless a token is given |
| `Observability.Telemetry`, `Network`, `ClientInfo` | Not exposed | Neutral; deferred |
| `Entra`, `Models.OnListModels`, `GitHub.OnTelemetry` | Not exposed | Neutral; deferred |

#### StartClientAsync(Func&lt;CancellationToken, Task&gt; start, CopilotRuntimeLaunch? launch, CopilotSignInPlan? signIn, CancellationToken cancellationToken)

**Purpose:** Run a client's start and explain its failure for what was launched. `StartAsync` passes
its launch and sign-in plan; the turn channel's discovery, which starts a client idempotently before
using its RPC surface, passes no launch and the runtime's `SignIn`. An overload without the launch
and plan passes `null` for both.

**Algorithm:** Await the start. An `OperationCanceledException` goes through
`CopilotSignIn.RedactRelayed` with the plan: one with no cause and nothing in its message to redact
propagates unchanged, with its stack; any other is rebuilt with the same token, the redacted message
and a redacted copy as its cause, a `TaskCanceledException` staying one. Any other
exception is rethrown as an `InvalidOperationException` whose message is `ExplainStartFailure` of it,
the launch and the plan, and whose `InnerException` is `CopilotSignIn.RedactFailure` of it and the
plan: a `CopilotRedactedException` carrying the original's type name, message and stack trace, each
redacted, with every chained exception copied the same way. The original is never kept, so no
exception in the chain carries a token; the outer exception's type is unchanged.

#### ExplainStartFailure(Exception failure, CopilotRuntimeLaunch? launch, CopilotSignInPlan? signIn)

**Purpose:** Compose the explanation.

**Algorithm:** The runtime's own error first, redacted: with `signIn.Redact` when a plan is given (the
plan's token, its secrets — environment token values, the host's token, and every session token the
host's provider returned — and token-shaped text), else with the token-shape pattern only. The
protocol-version mismatch is detected on the error as raised. Then the cause:

- When the error is the SDK's protocol-version mismatch and the launch is not the bundled runtime,
  `CopilotCli.ExplainProtocolMismatch` with the CLI's path and version and the protocol the runtime
  reported: a CLI too old to upgrade, or one newer than this release of AgentKit.
- For the bundled runtime, the runtime `Microsoft.Copilot.SDK` provisions into the application's
  output and the common cause of a runtime that was not found — a host application that does not
  reference `Microsoft.Copilot.SDK` directly — with the remedy of adding that reference or running the
  installed CLI instead.
- For an installed CLI, the CLI's path and version, that the error is that CLI's, and how to check it;
  no package-reference advice.
- When the launch is not known, both sources.

Then the pass-through set and the sign-in sources, in order — the host's `gitHubToken` or
`gitHubTokenProvider`, `COPILOT_GITHUB_TOKEN`, `GH_TOKEN` or `GITHUB_TOKEN`, the user's Copilot CLI
sign-in, a sign-in stored in the data directory and the signed-in GitHub CLI user — so an
authentication or network failure can be traced. A runtime path override can no longer be the cause,
because `COPILOT_CLI_PATH` is withheld. A null message is tolerated. The explanation ends by saying
the inner exception carries the runtime's own error, redacted, with its type name and stack trace.

#### StartOwnedAsync&lt;T&gt;(create, start, dispose, cancellationToken)

**Purpose:** The create-start-dispose-on-failure ordering, factored out so it is testable without a
runtime.

**Algorithm:** Create; start; on any exception, dispose (swallowing a disposal failure so it cannot
replace the start's) and rethrow.

#### DeleteSessionAsync(string sessionId, CancellationToken cancellationToken)

**Purpose:** Let the host delete a conversation the runtime kept — the one session operation a host
needs — without handing out the client, through which a session could be opened outside the
confinement.

**Algorithm:** Reject a null, empty or whitespace identifier; read `Client`, so a disposed runtime
throws `ObjectDisposedException` before any relay; then delegate to `RelayDeleteSessionAsync` with the
client's deletion. A session the runtime does not hold is reported by the SDK's RPC error,
`Microsoft.Copilot.RemoteRpcException`, which therefore reaches the host as an
`InvalidOperationException`.

#### RelayDeleteSessionAsync(Func&lt;string, CancellationToken, Task&gt; delete, string sessionId, CancellationToken cancellationToken)

**Purpose:** Relay a deletion's failure redacted, never as the SDK's own exception. The deletion is
injected because the SDK's client cannot be made to fail on demand without a runtime.

**Algorithm:** Run the deletion with the session identifier and token through
`CopilotSignIn.RelayAsync` with the runtime's `SignIn`: a failure, thrown synchronously or carried
by the task, becomes a new exception of the same kind (an RPC error becomes an
`InvalidOperationException`) with a redacted message and a redacted copy as its cause; a clean
cancellation propagates unchanged.

#### ThrowIfDisposed()

**Purpose:** Refuse a disposed runtime where something is built on it.

**Algorithm:** Read `Client` and discard it, so a disposed runtime throws `ObjectDisposedException`
with the object name `CopilotRuntime`. Called by `CopilotAgentFactory.Create`, by the
`CopilotProviderSessionFactory`, `CopilotSummarizer` and `CopilotModelCatalog` constructors, and by
the catalog at each read.

#### DisposeAsync()

**Purpose:** Stop the runtime process, for good.

**Algorithm:** Atomically take the client and dispose it; a second call finds none and returns.
Sessions open on the runtime end with it; their persisted state stays in the data directory.

**Postconditions:** Every agent, provider-session factory and summarizer built on the runtime fails its next open,
resume or deletion, and every model catalog its next read of the service, with `ObjectDisposedException` before the SDK
is reached, so nothing starts the runtime again. The host disposes the runtime only once no run is in progress on it: an
open that has already read the client when disposal begins races the disposal, and the SDK may then restart the client
for that open. Disposal is not serialized against open runs, as is usual for `IAsyncDisposable`.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null options | `ArgumentNullException` |
| Null, blank or invalid data directory | `ArgumentNullException` or `ArgumentException` naming `dataDirectory` |
| Undefined runtime source | `ArgumentOutOfRangeException` naming `runtimeSource` |
| CLI path relative, a batch file, invalid, or given with `Bundled` | `ArgumentException` naming `cliPath` |
| GitHub host not an https host without a path, query, user name or port | `ArgumentException` naming `gitHubHost` |
| Both a GitHub token and a token provider | `ArgumentException` naming `gitHubTokenProvider` |
| Token provider fails or returns no token at start | `InvalidOperationException` naming the provider; nothing started |
| `COPILOT_GH_HOST` invalid, no `gitHubHost` | `InvalidOperationException` naming the variable; nothing started |
| Token provider canceled at start | `OperationCanceledException` propagates unchanged |
| Named CLI does not exist | `InvalidOperationException` before any client exists |
| No installed CLI found | `InvalidOperationException` listing where AgentKit looked and how to install |
| CLI version cannot be read, or is too old | `InvalidOperationException` before any client exists |
| SDK refuses the runtime's protocol | client disposed; `InvalidOperationException` explaining too old or too new |
| Started runtime not signed in | client disposed; `InvalidOperationException` listing what each sign-in source found |
| Data directory path names a file | `InvalidOperationException` naming the path |
| Non-empty data directory without the marker | `InvalidOperationException` naming the directory |
| Directory or marker cannot be created | `IOException` or `UnauthorizedAccessException` propagates |
| Runtime fails to start | client disposed; `InvalidOperationException`, redacted copy of the error as cause |
| Start canceled | client disposed; clean: unchanged; else rebuilt redacted, same token |
| Runtime used after disposal | `ObjectDisposedException` from `Client` |
| Built-on object used after disposal | `ObjectDisposedException` on its next open or resume; nothing started |
| Null or blank session identifier to delete | `ArgumentNullException` or `ArgumentException` |
| Runtime refuses or fails a deletion | relayed redacted; RPC error as `InvalidOperationException` |

### Dependencies

- **CopilotCli** — discovery of the installed CLI, the version probe and policy, and the guidance its
  refusals carry; see *CopilotCli Unit Design*.
- **CopilotSignIn** — the sign-in resolution, the token-fetch helper, the refusal's text, the classification of the
  runtime's sign-in, the session token provider, the redaction of a start failure's text and cause, and the relay of a
  deletion failure and of a start cancellation; see *CopilotSignIn Unit Design*.
- **Microsoft.Copilot.SDK** — supplies `CopilotClient`, its options, the spawned stdio connection,
  the status and authentication reads, the protocol handshake and the provisioned runtime; see
  *Microsoft.Copilot.SDK Design*.

### Callers

A host application calls `CopilotRuntime.StartAsync` once and disposes the runtime when finished,
handing it to `CopilotAgentFactory.Create`, `CopilotProviderSessionFactory` and `CopilotSummarizer`,
which check it is not disposed, pass it to their channel openers — each open, resume and release
reads its client — and never dispose it. It hands the same runtime to `CopilotModelCatalog`, which
checks it is not disposed when built and at each read, reads its client to list the service's
models, and never disposes it. A host deletes a finished
conversation with `DeleteSessionAsync`, passing `CopilotAgentSession.SessionId`. The production turn channel
calls `ApplySessionSignIn` and then `StartClientAsync` before every session it opens or resumes; see
*CopilotTurnChannel Unit Design*.
