## Microsoft.Copilot.SDK

### Purpose

`Microsoft.Copilot.SDK` is GitHub's .NET SDK for the Copilot runtime. It is used because
AgentKitAgentsCopilot implements no agent runtime and no conversation runtime of its own: the Copilot
runtime runs the conversation, and the SDK is how a .NET application hosts it, opens sessions on it,
answers its permission requests and reads its event stream. It was chosen rather than a bespoke
integration so that AgentKit contributes only the confinement, the default-deny permission handling,
the agent's lifecycle and streaming, and the session accounting, while the SDK supplies the client,
the session lifecycle, the event stream, the permission RPC and the native runtime itself. It is a
runtime NuGet dependency of the AgentKitAgentsCopilot package only, deliberately kept out of Core.

### Features Used

- `CopilotClient` — the client the host constructs, starts and disposes, in the mode, runtime
  connection and identity the host chooses; AgentKit creates, resumes and deletes sessions on it
  without taking ownership of it
- `CreateSessionOptions`, `ResumeSessionOptions` and their shared base `SessionOptionsBase` — the
  session options. Every confinement-relevant option group sits on the shared base: the tool
  definitions and the available-tools allow-list, with its source-qualified `custom:` entries; the
  large-output, tool-search, schedule-management and image-generation tool settings; skills,
  instructions, hooks, git, state and memory, agents, MCP servers, extensions and configuration
  discovery, experimental features and embedding retrieval; the permission mode and handler; the event
  handler and streaming; the model, the working directory and the system message. That is what lets
  AgentKit confine a resumed session exactly as a created one
- `BuiltInTools.Isolated` — the SDK's published set of isolated built-in tool names, folded into
  AgentKit's reserved-name set at type initialization rather than copied
- **The permission RPC** — the permission-request hierarchy, including the custom-tool request
  carrying a tool name, the permission mode, and the permission-decision type with its approve-once
  and reject results, in which the default permission handler is expressed
- `SystemMessageConfig` — in customize mode, removes the runtime's host environment context section
  on every session, the shape Empty mode itself sends, and carries the supplied instructions as its
  content, which the runtime appends in this mode as in append mode. Nothing else: the seeded
  conversation record a rotation produces is deliberately kept out of it,
  because the SDK offers no history or messages field of any kind and a record charged as overhead
  rather than as conversation makes the engine's accounting drift. It travels on the session's first
  message instead
- **The session lifecycle** — creating a session from options, resuming a recorded session by its
  identifier with fresh options, sending one message (`MessageOptions`, with its prompt and any
  in-memory `AttachmentBlob` attachments) and waiting for the session to become idle under a
  caller-supplied deadline, releasing the session handle, and deleting the session. The deadline
  matters as much as the operations: the SDK applies a sixty-second default when told none, so
  AgentKit states an infinite one and lets the caller's cancellation token be the turn's only deadline
- **The session event stream** — the usage-information event carrying the occupancy, the limit and the
  conversation's share; the tool-execution start and completion events; the assistant message, its
  streamed deltas and the per-request assistant usage event; the session error event; and the events
  by which the runtime reports reshaping a conversation itself — a compaction starting, summarized or
  completed, a truncation, a cleared context window and a snapshot rewind. Each event's envelope
  carries an `AgentId`, absent for the root agent and naming a sub-agent otherwise, which is how
  AgentKit attributes events. A handler set on the options is installed before the create or resume
  request is issued, which is what makes a run's first events observable
- **The infinite-session configuration** — its background-compaction threshold raised clear of
  AgentKit's rotation point on every session AgentKit's own engine drives, so the runtime's compactor
  and AgentKit's do not act on one conversation
- **MCP and plugin control** — the client's MCP discovery (`mcp.discover`), which the SDK documents
  as discovering servers from the user, workspace, plugin and builtin sources; the session's
  disabled-servers option, which keeps a named server from starting on creation or cold resumption;
  the session's effective MCP configuration listing (`session.mcp.listConfigured`), which reports
  the session's effective MCP configuration, with each server's enablement after configuration and
  policy filtering and its provenance (`McpServerSource`: user, workspace, plugin, builtin, managed
  or account); the
  session-scoped disable (`session.mcp.disable`); and the session-options patch whose plugin options
  replace the session's installed plugins (`plugins.installed`). AgentKit uses them to close every
  ambient MCP server a session lists and clear installed plugins on every session, independently of
  the client's mode

### Integration Pattern

`Microsoft.Copilot.SDK` is consumed by AgentKitAgentsCopilot, which references it as a **direct**
`PackageReference`. `CopilotAgentFactory` builds every session's options — create options for an
agent's first run and for the engine and consolidation paths, resume options for an agent's later
runs — through one confinement block, and `CopilotTurnChannel` hands them to the client's create or
resume call. The host owns the client's lifetime. Each session handle AgentKit opens is AgentKit's and
is released after use; sessions the engine and the summarizer open are also deleted, while an agent's
are kept until the host deletes them.

**Client construction is the host's; Empty mode on a host-owned data directory is a precondition.**
The integration requires it, and AgentKit cannot enforce it: `CopilotClient` exposes no way to read
its mode back. AgentKit therefore relies on the mode for nothing that reaches the model. Every
capability or context default Empty mode supplies per session is set by AgentKit on each session —
the host environment context removed through the same section override, experimental features off,
embedding retrieval skipped, commit co-author trailers off, every other option channel closed — and
the installed plugins are
cleared with the same options patch Empty mode sends after every create and resume. Every MCP server
the session lists is closed and verified too, which Empty mode does not do. What a client that violates the
precondition can still add is host-side behavior no session option reaches: settings in a shared
data directory such as user-level hooks, credential storage in the system keychain rather than the
data directory, telemetry and runtime file logging, and anything the host attaches at client level
through plugin directories or extensions. Empty mode requires a base directory, supplied through the
`RuntimeConnection` — `ForInProcess(baseDirectory: …)` for in-process hosting, or a spawned-process
connection — and that directory is where the runtime preserves a kept session's state.
`ClientGitHubOptions` names the identity: the logged-in user, for which the runtime also honors the
`COPILOT_GITHUB_TOKEN`, `GH_TOKEN` and `GITHUB_TOKEN` environment variables, or an explicit token.
There is no client-level working directory; AgentKit's factories carry one per session.

**Session options are built fresh for every session.** The SDK mutates the options object it is
handed when it applies its mode defaults, and `Clone()` is shallow — it shares the infinite-session
configuration, the hooks and the delegates — so AgentKit never reuses or clones an options object; it
builds a new one for every session and every run.

**Runtime behaviors AgentKit depends on, each measured against the live runtime and re-verified by
the opt-in live suite where noted:**

- **A bare-name allow-list admits built-ins.** An allow-list entry naming a tool without its source
  also admits a built-in tool of the same name, so every entry AgentKit writes is qualified as
  `custom:<name>` (re-verified by the live suite).
- **Every custom-tool call raises a permission request**, of the custom-tool kind, naming the tool —
  so the permission handler is on every tool call's path (re-verified by the live suite).
- **Large-output diversion.** The runtime diverts a tool result larger than about twenty kilobytes to
  a temporary file and gives the model its path. No confined tool can read that file, so AgentKit
  switches the diversion off on every session (re-verified by the live suite).
- **The built-in GitHub MCP server runs in Empty mode.** The runtime starts it, authenticated with the
  user's GitHub identity, for every session even in Empty mode, so AgentKit disables it by name on
  every session.
- **Empty mode does not stop ambient MCP servers.** Its only MCP default is in-memory token storage.
  The runtime starts servers configured in the data directory's `mcp-config.json`, in the workspace,
  by installed plugins, by a managed catalog, and by a signed-in account; Empty mode's base-directory
  requirement keeps the data directory's configuration out only when the directory is the host's own,
  and is not documented to reach the workspace, managed or account sources. AgentKit
  therefore discovers and disables every server before creating or resuming a session, then lists the
  session's effective MCP configuration, disables any server still enabled for that session, and
  refuses the session if one stays enabled. The SDK documents the runtime's MCP discovery as covering
  the user, workspace, plugin and builtin sources, so a server from another source, such as a managed
  catalog or a signed-in account, may start before the seal disables it; no turn runs before the
  seal, and a server that cannot be closed refuses the session. The live suite configures a non-GitHub server
  in a CodingAgent client's data directory, and discovery reports it in every scenario. On a session
  opened through AgentKit's channel it is named on the create-time disabled list and reported
  disabled, with no server enabled (L12a); on a session created directly it is enabled (L12b); when
  discovery is replaced with one that finds nothing, it is enabled when the seal begins and the seal's
  session-scoped disable closes it, which the re-listing confirms (L12c); and on a session resumed
  through the channel it is named before the resume and no server is enabled afterwards (L12d). In
  each scenario through the channel the tool set is exactly the supplied tools and the plugins patch
  is accepted; no fixture installs a plugin, so clearing an installed plugin is shown only offline.
- **The infinite-session enablement flag is ignored.** A session created with it false still
  compacted as soon as its threshold was crossed; the flag only suppresses the session's workspace.
  The background-compaction threshold **is** honored, so AgentKit relies on the threshold and on
  detecting a rewrite, and sets the flag false only as a statement of intent.
- **Tool-returned images reach the model.** The runtime converts a binary tool result into content the
  model sees, so AgentKit installs no image-promoting decorator on this provider; an in-memory blob
  attachment reaches the model too (both re-verified by the live suite).
- **Built-in names are refused at session creation.** The runtime refuses a session registering a
  custom tool under a built-in's name unless the tool is marked as overriding it, which AgentKit never
  does; AgentKit refuses such a name earlier, and the live suite checks that the runtime's listed
  built-ins are all reserved.

The SDK's session and client types are **sealed with non-public constructors and no virtual
members**, so neither can be faked in a test. AgentKit therefore reaches them through one internal
seam — `ICopilotTurnChannel` — so everything above it can be exercised without a live runtime; see
*CopilotTurnChannel Unit Design*. The SDK's event types, by contrast, are all constructable, so the
tests drive the genuine event shapes through AgentKit's own matching code.

The SDK's permission-decision RPC surface, and the MCP discovery, MCP configuration and
session-options RPC surface, are marked with the `MCPSDK001` "evaluation purposes only" diagnostic.
AgentKit necessarily references them to wire a permission handler and to close ambient MCP servers
and installed plugins, so that diagnostic is suppressed in the package and in its tests, and a
consumer that references the permission-handler parameter will see the same diagnostic from its own
build. A change to the MCP and plugin calls fails safe — a failing call refuses the session — and is
caught by the opt-in live suite.

**The `COPILOT_CLI_PATH` environment variable redirects in-process hosting.** When it is set,
in-process hosting loads the runtime library from beside that path instead of from the application's
output, and there is no per-client override. A Copilot CLI installation does not ship that library,
and every shell the Copilot CLI starts sets the variable, so a host launched from such a shell fails to
start. A spawned-process connection reads the variable from its replacement environment when one is
supplied. AgentKit never changes the environment; `CopilotClientStarter` explains the failure and the
remedies; see *CopilotClientStarter Unit Design*.

### Deployment and Runtime Provisioning

The Copilot runtime is a native component the package does not carry. The SDK's build targets
download it at build time from the Copilot CLI's GitHub releases, verify the archive against the
release's published SHA-256 checksums, extract it under the project's intermediate folder — about
270 MB per target framework — and copy it into the output, about 356 MB per output directory in some
hundred files under `runtimes/<rid>/native`. The runtime version is pinned by the SDK version.

- **Only a direct reference provisions.** The targets ship in the package's `build` folder, not
  `buildTransitive`, so they run only in a project that references the SDK directly. A host
  application must therefore reference `Microsoft.Copilot.SDK` itself; reaching it transitively through
  AgentKit provisions nothing.
- **AgentKit ships no runtime.** The AgentKit library sets `CopilotSkipCliDownload`, which skips the
  download, the copy to the output and the content registration alike, keeping the runtime out of its
  outputs, its tests' outputs and its package. Its SDK reference does not flow build assets to
  consumers, so a consumer's other projects never download the runtime either.
- **Hermetic continuous integration.** The build workflow sets `CopilotSkipCliDownload` as a
  workflow-level environment variable, which MSBuild reads as a property, so the merge gate never
  contacts GitHub releases. The live-samples workflow builds with the download and caches the
  extracted runtime.
- **Mirrors and offline builds.** `CopilotCliReleaseBaseUrl`, or the `COPILOT_CLI_DOWNLOAD_BASE_URL`
  environment variable, redirects the download to a mirror; `CopilotCliBinaryPath` supplies a runtime
  already on disk; `CopilotCliDownloadTimeout` bounds the download; `CopilotSkipCliDownload` turns
  provisioning off.
- **Runtime identifiers.** The runtime is published for `win-x64`, `win-arm64`, `linux-x64`,
  `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`, `osx-x64` and `osx-arm64`. A build provisions
  the runtime for its explicit runtime identifier, or for the build machine's own platform when it has
  none.

This is a property of the off-the-shelf SDK, and it is part of the reason the dependency is isolated in
its own AgentKit package: an application that never builds a Copilot agent never takes the SDK or its
native runtime. See *Deployment: The Host Application Provisions the Runtime* in
*AgentKitAgentsCopilot System Design*.
