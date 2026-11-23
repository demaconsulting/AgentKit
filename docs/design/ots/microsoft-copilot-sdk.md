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

- `CopilotClient` and `CopilotClientOptions` — the client `CopilotRuntime` builds, starts and owns:
  `CopilotClientMode.Empty`, a `RuntimeConnection.ForSpawnedStdio` connection — to the installed
  Copilot CLI by its path, or to the provisioned runtime when no path is given — with a replacement
  environment and a base directory, and the identity the host chooses; AgentKit creates, resumes and
  deletes sessions on it
- `CopilotClient.GetStatusAsync` and `GetAuthStatusAsync` — read once the runtime has started:
  `GetStatusResponse.Version` and `ProtocolVersion` are kept on the runtime, and
  `GetAuthStatusResponse.IsAuthenticated` and `StatusMessage` decide whether the started runtime is
  refused as not signed in; its `AuthType`, `Host` and `Login` say how it signed in
- **GitHub authentication** — `ClientGitHubOptions.Token`, the client-level token the confined runtime
  starts with (the SDK passes it in a variable of its own and turns off the runtime's own sign-ins);
  a second, short-lived `CopilotClient` in `CopilotClientMode.CodingAgent` on a supplied base
  directory, the only mode whose spawned runtime reads the system credential store, whose
  `GetAuthStatusAsync` and `Rpc.Account.GetAllUsersAsync` report the stored account and its token; and
  `SessionGitHubOptions.TokenProvider`, with `GitHubTokenProviderArgs` (host and `Initial` or
  `Refresh`), `GitHubTokenProviderResult` and `GitHubToken`, which asks the host's token provider for
  each session's token. See *CopilotSignIn Unit Design*
- **The protocol handshake** — `CopilotClient.StartAsync` refuses a runtime whose protocol version the
  SDK does not support with an `InvalidOperationException` reporting a protocol version mismatch and
  the version the server reports; AgentKit recognizes that error and explains it
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
- **MCP and plugin control** — the client's MCP discovery (`mcp.discover`), which the SDK documents as discovering
  servers from the user, workspace, plugin and builtin sources; the session's disabled-servers option, which keeps a
  named server from starting on creation or cold resumption; the session's effective MCP configuration listing
  (`session.mcp.listConfigured`), which reports the session's effective MCP configuration, with each server's enablement
  after configuration and policy filtering and its provenance (`McpServerSource`: user, workspace, plugin, builtin,
  managed or account); the session-scoped disable (`session.mcp.disable`); and the session-options patch whose plugin
  options replace the session's installed plugins (`plugins.installed`). AgentKit uses them to keep discovered servers
  from starting, to disable before any turn every server a session lists, and to clear installed plugins on every
  session
- **The model listing** — the server-level, uncached model query `CopilotClient.Rpc.Models.ListAsync`,
  returning `Rpc.ModelList.Models`, read by `CopilotModelCatalog`. Of each `Rpc.Model` AgentKit reads
  the identifier, name and vendor; `Capabilities.Supports` (`Vision`, `ToolCalls`, `ReasoningEffort`,
  `Thinking`) and `Capabilities.Limits` (`MaxContextWindowTokens`, `MaxOutputTokens`,
  `MaxPromptTokens` and the vision limits); `Billing` (`Multiplier`, `TokenPrices.MaxPromptTokens` and
  `TokenPrices.LongContext`); `Policy` (`State` and `Terms`); `SupportedReasoningEfforts`,
  `DefaultReasoningEffort` and `SupportedContextTiers`; the information and warning messages and
  `WarningText.DataRetention`; and the picker category and price category
- **The per-session inference options** — `SessionInferenceOptions`, with its `ReasoningEffort` and
  `ContextTier`, on the shared session-options base, so a created and a resumed session carry the
  same chosen effort and tier

### Integration Pattern

`Microsoft.Copilot.SDK` is consumed by AgentKitAgentsCopilot, which references it as a **direct**
`PackageReference`. `CopilotAgentFactory` builds every session's options — create options for an
agent's first run and for the engine and consolidation paths, resume options for an agent's later
runs — through one confinement block, and `CopilotTurnChannel` hands them to the client's create or
resume call. The host owns the runtime's lifetime. Each session handle AgentKit opens is AgentKit's and
is released after use; sessions the engine and the summarizer open are also deleted, while an agent's
are kept until the host deletes them.

**The catalog reads the model listing through the runtime.** `CopilotModelCatalog` reads
`runtime.Client` at every fetch rather than keeping it, so a runtime the host disposed is refused
rather than restarted. The uncached query is used, so a refresh sees a model the user was just
granted. A test seam takes the read as a delegate, because the SDK's `ServerModelsApi` is sealed with
no public constructor. The catalog imports the SDK's RPC namespace as the alias `Rpc` rather than
wholesale, because it declares model types whose names collide with Core's, such as
`ModelCapabilities`. The inference options are set only when the user chose an effort or a tier, so
the runtime's own default applies otherwise.

**AgentKit builds and owns the client.** `CopilotRuntime` constructs the only `CopilotClient` the
package runs on, and the host cannot supply one. Its options are fixed except for the token, the log
level and the logger:

- **`Mode = CopilotClientMode.Empty`.** Per session the SDK then sets telemetry, experimental mode and
  the cross-session store off, memory off, embedding retrieval skipped, hooks, git and skills off,
  MCP OAuth tokens in memory and local agent discovery only; after create and resume it patches
  installed plugins and included built-in skills to none and co-author trailers and scheduling off;
  and it removes the system message's environment context section. On the spawned process it sets
  the variable that disables the system keychain, so credentials go to the data directory instead,
  and it does not enable runtime file logging, which it does in other modes. AgentKit sets every one of
  these session defaults itself as well, so the confinement does not rest on the mode alone.
- **`Runtime = RuntimeConnection.ForSpawnedStdio(path, environment, baseDirectory)`.** With a `path`,
  the SDK launches that executable — a `.js` path with `node` — and with none, the provisioned runtime
  wrapper (`runtimes/<rid>/native/copilot-runtime`); either way with `--headless`, `--no-auto-update`
  and `--stdio`, and an explicit path is not checked for the provisioned runtime's files. Because of
  `--no-auto-update`, an installed CLI runs the version built into its binary. The SDK resolves the
  executable from the supplied path first, then `COPILOT_CLI_PATH`, then the provisioned runtime. A
  supplied `Environment` **replaces** the inherited environment;
  the SDK then adds `COPILOT_HOME` (the base directory), the keychain switch and, with a token,
  `COPILOT_SDK_AUTH_TOKEN`. The SDK reads `COPILOT_CLI_PATH` from that replacement environment rather
  than from the process, so withholding it from the environment removes the runtime-path override. In-process
  hosting, by contrast, hands the runtime the entire process environment, which a library cannot
  narrow without mutating process-global state.
- **Constructor validation.** The client constructor rejects Empty mode without a base directory or a
  session filesystem, which AgentKit relies on as a guard. It skips that check for an external TCP
  runtime, whose mode, data directory and environment are not the client's — one reason AgentKit
  offers no external runtime.

`ClientGitHubOptions` names the identity: the logged-in user, for which the runtime also honors the
`COPILOT_GITHUB_TOKEN`, `GH_TOKEN` and `GITHUB_TOKEN` environment variables, or an explicit token.
AgentKit sets an explicit token when the host, its token provider or the user's Copilot CLI sign-in
supplies one, and the GitHub host through `COPILOT_GH_HOST` in the replacement environment, as a bare
host name, the form the runtime reads. There is no client-level working directory; AgentKit's
factories carry one per session.

**The keychain switch cannot be turned back on in Empty mode.** For every runtime the SDK spawns in
Empty mode it sets the variable that disables the system keychain after the caller's replacement environment; no client,
GitHub or connection option controls it. Measured live: an Empty-mode runtime on a home holding only
the Copilot CLI's account entry, with that variable set to `0` in the replacement environment and the GitHub CLI hidden,
reports `Not authenticated`, while the same home in CodingAgent mode reports the account signed in.
That is why AgentKit reads the Copilot CLI's token through a separate CodingAgent-mode helper client
rather than through the confined runtime; see *CopilotSignIn Unit Design*.

**Known defect: the account list fails for any account but a stored sign-in.** `Rpc.Account
.GetCurrentAuthAsync` and `GetAllUsersAsync` throw a `JsonException` ("JSON deserialization for type
'Microsoft.Copilot.Rpc.AuthInfoGhCli' was missing required properties including: 'token'") whenever
the runtime holds a GitHub CLI, explicit-token or environment account: the runtime omits the token for
those variants, but the SDK's generated types mark it required. Only the stored-sign-in (`user`)
variant deserializes. AgentKit therefore never calls `GetCurrentAuthAsync`, uses `GetAuthStatusAsync`
for status everywhere, calls `GetAllUsersAsync` only in the helper, only after the status reports a
stored sign-in as the expected account, with every token variable withheld and the GitHub CLI hidden
from the helper's `PATH` (an empty `GH_CONFIG_DIR` alone does not hide a GitHub CLI that keeps its
token in the system credential store), and treats a failure as a finding.

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
- **The built-in GitHub MCP server runs in Empty mode, and the listing omits it.** The runtime starts
  it lazily, on a session's first turn, authenticated with the user's GitHub identity, even in Empty
  mode (L14b: pending, then connected). Named in the disabled servers it is reported disabled and never
  reaches pending or connected (L14a). The session's configuration listing does not report it even
  while it is connected (L14b), so only the name prevents it. The SDK documents a server call that
  lists the built-in server names, but does not make it public, so AgentKit names the server itself.
- **Empty mode does not stop ambient MCP servers.** Its only MCP default is in-memory token storage.
  On an Empty-mode client the runtime executes a server configured in the data directory's
  `mcp-config.json` when the session is created directly (L14d), and a server in a trusted
  workspace's `.mcp.json` when configuration discovery is on (L14f). Session creation waits for such a
  local server to complete or fail its handshake. AgentKit therefore keeps the data directory fresh,
  leaves configuration discovery off, disables every discovered server before creating or resuming a
  session — discovery reports the data directory's server, and that server is then never executed
  (L14c) — and, after creation, lists the session's effective MCP configuration, disables any server
  still enabled for that session, and refuses the session if one stays enabled. A server from a
  source discovery cannot enumerate before creation, such as a managed catalog or a signed-in
  account, may start, and authenticate, while the session is created; the seal contains it, it does
  not prevent it. The managed catalog is delivered to the runtime as organization policy, and account
  servers come from connectors on the signed-in account, both behind a staged feature rollout, so
  neither can be configured for a test. A server named in the disabled servers but configured by no
  source appears in the loaded-servers event as disabled with no source, which is how the workspace
  probe appears when configuration discovery is off (L14e). The live suite also configures a
  non-GitHub server
  in a CodingAgent client's data directory, and discovery reports it in every scenario. On a session
  opened through AgentKit's channel it is named on the create-time disabled list and reported
  disabled, with no server enabled (L12a); on a session created directly it is enabled (L12b); when
  discovery is replaced with one that finds nothing, it is enabled when the seal begins and the seal's
  session-scoped disable turns it off, which the re-listing confirms (L12c); and on a session resumed
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
AgentKit necessarily references them to wire a permission handler and to disable ambient MCP servers
and clear installed plugins, so that diagnostic is suppressed in the package and in its tests, and a
consumer that references the permission-handler parameter will see the same diagnostic from its own
build. A change to the MCP and plugin calls fails safe — a failing call refuses the session — and is
caught by the opt-in live suite.

**The `COPILOT_CLI_PATH` environment variable redirects in-process hosting.** When it is set,
in-process hosting loads the runtime library from beside that path instead of from the application's
output, and there is no per-client override; a Copilot CLI installation does not ship that library,
and every shell the Copilot CLI starts sets the variable. Measured under a bogus value, an in-process
client fails with `FFI runtime library not found. Looked for '<directory>\copilot_runtime.dll', …`,
while a spawned client whose replacement environment omits the variable starts and opens a session
(L11). `CopilotRuntime` withholds the variable, so it cannot affect the runtime AgentKit starts; see
*CopilotRuntime Unit Design*.

### Deployment and Runtime Provisioning

AgentKit runs the installed Copilot CLI by default, which needs nothing from this section: the host
references no SDK package of its own and ships no runtime. What follows applies to a host that opts
into the bundled runtime (`CopilotRuntimeSource.Bundled`).

The Copilot runtime is a native component the package does not carry. The SDK's build targets
download it at build time from the Copilot CLI's GitHub releases, verify the archive against the
release's published SHA-256 checksums, extract it under the project's intermediate folder — about
270 MB per target framework — and copy it into the output, about 356 MB per output directory in some
hundred files under `runtimes/<rid>/native`. The runtime version is pinned by the SDK version.

- **Only a direct reference provisions.** The targets ship in the package's `build` folder, not
  `buildTransitive`, so they run only in a project that references the SDK directly. A host
  application that opts into the bundled runtime must therefore reference `Microsoft.Copilot.SDK`
  itself; reaching it transitively through AgentKit provisions nothing.
- **AgentKit ships no runtime.** The AgentKit library sets `CopilotSkipCliDownload`, which skips the
  download, the copy to the output and the content registration alike, keeping the runtime out of its
  outputs, its tests' outputs and its package. Its SDK reference does not flow build assets to
  consumers, so a consumer's other projects never download the runtime either.
- **Hermetic continuous integration.** The build workflow sets `CopilotSkipCliDownload` as a
  workflow-level environment variable, which MSBuild reads as a property, so the merge gate never
  contacts GitHub releases. The live-samples workflow installs a standalone Copilot CLI for the samples
  and the installed-CLI live variants, and only its bundled live variant builds with the download and
  caches the extracted runtime.
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
native runtime. See *Deployment: The Installed Copilot CLI by Default, the Bundled Runtime on Request*
in *AgentKitAgentsCopilot System Design*.
