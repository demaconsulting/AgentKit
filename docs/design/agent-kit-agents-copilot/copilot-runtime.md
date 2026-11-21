## CopilotRuntime

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotRuntime` class is the GitHub Copilot runtime AgentKit starts, confines and owns.
`CopilotRuntimeOptions` carries the few options a host may give it. Every agent, provider-session
factory and summarizer in this system runs on a `CopilotRuntime`; none accepts a client the host built.

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

### Data Model

`CopilotRuntimeOptions` (immutable, safe for concurrent use):

- **`DataDirectory`** (`string`) — the full path of the runtime's data directory. A relative path is
  resolved against the current directory when the options are built; a null, empty or whitespace
  path is refused.
- **`GitHubToken`** (`string?`) — the token to authenticate with, or `null` (a blank token is
  normalized to `null`) to authenticate as the signed-in user.
- **`LogLevel`** (`CopilotLogLevel?`) — the runtime's log level, or `null` for its default.
- **`Logger`** (`ILogger?`) — a logger for the SDK's diagnostic output.

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
   runtime reads it.
3. Build the client options (`BuildClientOptions`) from the options, the prepared directory and the
   process environment, read once.
4. Create the client and start it through `StartOwnedAsync`, whose start step is `StartClientAsync`
   over the client's own start. A failed or canceled start disposes the client.
5. Return a runtime over the started client.

**Postconditions:** The runtime process is running on the prepared data directory; the caller owns
the runtime and disposes it.

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
it as invalid rather than trusting it. A marker another writer created first is the reuse path: when
valid it is accepted without enumerating, as on a later start — the call that writes the marker is
the one that checks. For tests, an internal `PrepareDataDirectory(string, Action<string>?)` overload
invokes a `beforeMark` callback with the full path just before the directory is marked, so a test can
populate it as a concurrent writer could; the production overload passes `null`.

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

#### BuildClientOptions(CopilotRuntimeOptions options, string dataDirectory, IDictionary hostEnvironment)

**Purpose:** The single place the client is configured.

**Algorithm:** Set `Mode` to `CopilotClientMode.Empty` and `Runtime` to
`RuntimeConnection.ForSpawnedStdio(environment: BuildRuntimeEnvironment(hostEnvironment),
baseDirectory: dataDirectory)`, with no runtime path, no arguments and no child working directory.
Copy the token, log level and logger. Everything else keeps the SDK's default, which for every
capability channel is none.

The SDK's client options, classified:

| Option | Classification | Rationale |
| --- | --- | --- |
| `Mode` | Forced: Empty | No coding-agent defaults; credentials in the data directory |
| `Runtime` spawned over stdio | Forced | A child process whose environment AgentKit decides |
| ↳ `BaseDirectory` | Forced | The prepared data directory |
| ↳ `Environment` | Forced | The host's minus Copilot configuration variables |
| ↳ `Path`, `Args` | Not expressible | A foreign runtime or flags such as approve-all |
| ↳ `WorkingDirectory`, `SessionIdleTimeoutSeconds` | Not exposed | Neutral; sessions carry a working directory |
| In-process, spawned TCP | Not expressible | The host's whole environment; a TCP listener |
| External TCP, default | Not expressible | A runtime not AgentKit's; an environment switch |
| `SessionFs` | Not expressible | Moves state out of the data directory |
| `InternalTestOptions` | Not expressible | Injects environment variables |
| `Plugins.BuiltinDirectories` | Not expressible | Registers plugins with servers, hooks, skills and agents |
| `Extensions.LaunchProvider` | Not expressible | A channel for injected capability |
| `Installations.ConfirmationHandler` | Not expressible | Plugin and MCP installation flows |
| `Models.RequestHandler` | Not expressible | Intercepts every inference request |
| `GitHub.Token` | Allowed | Authentication |
| `Observability.LogLevel`, `Observability.Logger` | Allowed | Host-side diagnostics |
| `GitHub.UseLoggedInUser` | Derived | The SDK default: the signed-in user unless a token is given |
| `Observability.Telemetry`, `Network`, `ClientInfo` | Not exposed | Neutral; deferred |
| `Entra`, `Models.OnListModels`, `GitHub.OnTelemetry` | Not exposed | Neutral; deferred |

#### StartClientAsync(Func&lt;CancellationToken, Task&gt; start, CancellationToken cancellationToken)

**Purpose:** Run a client's start and explain its failure. Shared by `StartAsync` and by the turn
channel's discovery, which starts a client idempotently before using its RPC surface.

**Algorithm:** Await the start. An `OperationCanceledException` propagates unchanged. Any other
exception is rethrown as an `InvalidOperationException` whose message is `ExplainStartFailure` of it
and whose `InnerException` is the original.

#### ExplainStartFailure(Exception failure)

**Purpose:** Compose the explanation.

**Algorithm:** The runtime's own error first. Then the runtime AgentKit runs (the one
`Microsoft.Copilot.SDK` provisions into the application's output, as a child process), and the common
cause of a runtime that was not found: a host application that does not reference
`Microsoft.Copilot.SDK` directly, with the remedy of adding that reference. Then the pass-through set
and the authentication sources — the token in the options, `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`,
`GITHUB_TOKEN` and the signed-in GitHub CLI user — so an authentication or network failure can be
traced. A runtime path
override can no longer be the cause, because `COPILOT_CLI_PATH` is withheld. A null message is
tolerated.

#### StartOwnedAsync&lt;T&gt;(create, start, dispose, cancellationToken)

**Purpose:** The create-start-dispose-on-failure ordering, factored out so it is testable without a
runtime.

**Algorithm:** Create; start; on any exception, dispose (swallowing a disposal failure so it cannot
replace the start's) and rethrow.

#### DeleteSessionAsync(string sessionId, CancellationToken cancellationToken)

**Purpose:** Let the host delete a conversation the runtime kept — the one session operation a host
needs — without handing out the client, through which a session could be opened outside the
confinement.

**Algorithm:** Reject a null, empty or whitespace identifier; ask the client to delete the session.
The SDK's own exception for a session the runtime does not hold propagates unchanged.

#### ThrowIfDisposed()

**Purpose:** Refuse a disposed runtime where something is built on it.

**Algorithm:** Read `Client` and discard it, so a disposed runtime throws `ObjectDisposedException`
with the object name `CopilotRuntime`. Called by `CopilotAgentFactory.Create` and by the
`CopilotProviderSessionFactory` and `CopilotSummarizer` constructors.

#### DisposeAsync()

**Purpose:** Stop the runtime process, for good.

**Algorithm:** Atomically take the client and dispose it; a second call finds none and returns.
Sessions open on the runtime end with it; their persisted state stays in the data directory.

**Postconditions:** Every agent, provider-session factory and summarizer built on the runtime fails
its next open, resume or deletion with `ObjectDisposedException` before the SDK is reached, so nothing
starts the runtime again. The host disposes the runtime only once no run is in progress on it: an
open that has already read the client when disposal begins races the disposal, and the SDK may then
restart the client for that open. Disposal is not serialized against open runs, as is usual for
`IAsyncDisposable`.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null options | `ArgumentNullException` |
| Null, blank or invalid data directory | `ArgumentNullException` or `ArgumentException` naming `dataDirectory` |
| Data directory path names a file | `InvalidOperationException` naming the path |
| Non-empty data directory without the marker | `InvalidOperationException` naming the directory |
| Directory or marker cannot be created | `IOException` or `UnauthorizedAccessException` propagates |
| Runtime fails to start | client disposed; `InvalidOperationException` with the runtime's error as cause |
| Start canceled | client disposed; `OperationCanceledException` propagates unchanged |
| Runtime used after disposal | `ObjectDisposedException` from `Client` |
| Built-on object used after disposal | `ObjectDisposedException` on its next open or resume; nothing started |
| Null or blank session identifier to delete | `ArgumentNullException` or `ArgumentException` |

### Dependencies

- **Microsoft.Copilot.SDK** — supplies `CopilotClient`, its options, the spawned stdio connection and
  the provisioned runtime; see *Microsoft.Copilot.SDK Design*.

### Callers

A host application calls `CopilotRuntime.StartAsync` once and disposes the runtime when finished,
handing it to `CopilotAgentFactory.Create`, `CopilotProviderSessionFactory` and `CopilotSummarizer`,
which check it is not disposed, pass it to their channel openers — each open, resume and release
reads its client — and never dispose it. A host deletes a finished
conversation with `DeleteSessionAsync`, passing `CopilotAgentSession.SessionId`. The production turn channel
calls `StartClientAsync` before every session it opens or resumes; see *CopilotTurnChannel Unit
Design*.
