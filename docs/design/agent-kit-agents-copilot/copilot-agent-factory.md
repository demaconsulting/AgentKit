## CopilotAgentFactory

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotAgentFactory` class builds AgentKit's own Microsoft Agent Framework `AIAgent` over a GitHub
Copilot `CopilotClient` and a supplied tool list, suppressing the runtime's built-in tools by deriving
the session allow-list from the supplied tools, and is the single place any Copilot session's options
are built.

### Purpose

`CopilotAgentFactory` builds the agent this package began as, and is also the **single place a
Copilot session's options are built** anywhere in the system. Its essential job is suppression: the
Copilot runtime injects its own tools, and an application confining an agent to a supplied tool set
must withhold them. The available-tools list on the session options is an allow-list, so the factory
derives it from the same collection it assigns as the session's tool definitions. Deriving both from
one collection, in one place, makes them impossible to drift apart.

That "one place" has four callers: an agent's first run, which creates a session; every later run of
that agent, which resumes it; `CopilotProviderSessionFactory`, for a compacting session; and
`CopilotSummarizer`, for a consolidation. All four end in the same confinement block. A second
builder with its own derivation is precisely the drift this design exists to prevent, so there is
not one. The half of the confinement that needs the live client and session — closing every ambient
MCP server and clearing the session's installed plugins — is applied by the production turn channel
on the one path every opener and resumer shares; see *CopilotTurnChannel Unit Design*.

The class is static and holds no state.

### Data Model

The factory constructs `CreateSessionOptions` for a created session and `ResumeSessionOptions` for a
resumed one. Every confinement setting lives on `SessionOptionsBase`, the base the two share, so the
confinement block targets that base and a resumed session is confined exactly as a created one. Only
a handful of members — the session's identifier and name, its cloud settings, filesystem trust and
the refresh of custom instructions — are create-only, and AgentKit sets none of them.

**Tools and permissions:**

| Member | Value | Purpose |
| --- | --- | --- |
| `Tools.Definitions` | the supplied tools | the tools published to the session |
| `Tools.AvailableTools` | `custom:<name>` for each of the **same** tools | the allow-list suppressing the built-ins |
| `Permissions.Mode` | `PermissionMode.Manual` | every request reaches the handler |
| `Permissions.OnRequest` | the host handler, or the default-deny one | adjudicates each permission request |

**Every other channel of injected capability is closed.** Each is set explicitly on every session,
so the confinement does not depend on the client's mode, which the factory cannot read:

| Member | Value | Reason |
| --- | --- | --- |
| `Tools.LargeOutput.Enabled` | `false` | a result diverted to a file is a path no confined tool can read |
| `Tools.Search.Enabled` | `false` | would hide allowed tools behind a search tool the list never grants |
| `Tools.ManageScheduleEnabled` | `false` | scheduling is runtime capability the host never granted |
| `Tools.ImageGeneration.Enabled` | `false` | a further capability the runtime can offer a session |
| `Skills.Enabled`, `Skills.IncludedBuiltinSkills` | `false`, empty | withholds installed and built-in skills |
| `Instructions.SkipCustomInstructions` | `true` | withholds instructions discovered on disk |
| `Instructions.OnDemandDiscovery` | `false` | withholds instructions discovered during a run |
| `Hooks.EnableRepositoryHooks` | `false` | a repository's hooks would run code the host never saw |
| `Git.EnableHostGitOperations` | `false` | withholds git operations on the host |
| `Git.CoauthorEnabled` | `false` | no co-author trailer is added to a commit the host never chose |
| `State.EnableCrossSessionStore` | `false` | nothing carries over from another session |
| `State.Memory.Enabled` | `false` | withholds the runtime's own memory |
| `Agents.IncludedBuiltInAgents` | empty | withholds the built-in sub-agents |
| `Agents.LocalDiscoveryOnly` | `true` | no agent definitions are discovered beyond the local set |
| `Extensions.Enabled` | `false` | withholds runtime extensions |
| `EnableConfigDiscovery` | `false` | withholds configuration discovered on disk |
| `SystemMessage` | `Customize`; `EnvironmentContext` removed | no host environment detail in the prompt |
| `Experiments.EnableExperimentalMode` | `false` | withholds behavior the host never chose |
| `State.SkipEmbeddingRetrieval` | `true` | no context retrieved from a cache shared across sessions |
| `Mcp.DisabledServers` | the built-in GitHub MCP server | the production channel adds every discovered server |

**The host environment context is removed from the system prompt.** The runtime's environment
context names the working directory, the operating system, the git root, a directory listing and the
available tools. Empty mode removes it client-side; the factory removes it on every session through
the system message's section overrides, the same shape Empty mode sends, so it is removed whatever
the client's mode. For an Empty-mode client the wire payload is unchanged. The host's instructions,
if any, are this message's content, which the runtime appends in this mode just as it does in append mode.

**Commit co-author trailers are set off, not left unset.** Off is an Empty-mode default: in Empty
mode the SDK sends the setting as off unless it is set, but in any other mode it forwards the
setting only when it is set, in the options patch it sends after the session opens. Leaving it
unset would hand a non-Empty client's own default to the session, so the factory sets it off.

**The built-in GitHub MCP server is disabled by name.** Measured against the live runtime, it is
started and authenticated with the user's GitHub identity on every session, even in the client's
Empty mode. The source-qualified allow-list already keeps its tools out of reach, but a server
holding the user's identity is disabled outright rather than merely filtered. Every other MCP server
— from the user, workspace, plugin, builtin or managed source
— is closed by the production turn channel, which adds every server the runtime discovers to this
list before the session is created, then disables and verifies whatever is still enabled once the
session exists. That needs the live client and session, which this builder does not have.

**Large-output diversion stays disabled.** The runtime would divert a result larger than about twenty
kilobytes to a temporary file and hand the model its path. A confined agent cannot read that file —
the runtime's read tool is suppressed and the application's tools are confined to their grants — and,
measured against the live runtime, a model handed such a path failed the task. AgentKit's tools bound
their own output through Core's tool limits, so a result reaching the runtime is already the size the
application chose to deliver. See *Tool Results Are Delivered Inline* in *AgentKitAgentsCopilot System
Design*.

**The host's settings**, each assigned only when the host supplied a non-blank value, so that leaving
it unset preserves the runtime's own default:

| Member | Source | Purpose |
| --- | --- | --- |
| `Model` | the supplied model name | backs the session with the chosen model |
| `WorkingDirectory` | the supplied working directory | the directory the runtime gives the session |
| `SystemMessage.Content` | the supplied instructions | governs a confined agent as the host intended |

**Per-path settings:**

| Member | Value | Path |
| --- | --- | --- |
| `Events.Streaming` | `true` | agent, created and resumed |
| `State.InfiniteSessions` | enablement false, threshold 0.95 | engine and consolidation only |

**`ReservedBuiltInToolNames`** (`FrozenSet<string>`, case-insensitive) — The names no supplied tool may
carry: the built-ins the runtime lists on Windows (`create`, `edit`, `glob`, `grep`, `list_agents`,
`list_powershell`, `powershell`, `read_agent`, `read_powershell`, `skill`, `stop_powershell`, `task`,
`view`, `web_fetch`, `write_agent`, `write_powershell`); the shell family it uses on Linux and macOS
(`bash`, `read_bash`, `write_bash`, `stop_bash`, `list_bash`); the model- or feature-gated built-ins
(`apply_patch`, `str_replace_editor`, `report_intent`, `update_todo`, `web_search`,
`image_generation`); and the SDK's own published set of isolated built-in names, taken from the SDK at
type initialization rather than copied. Invariant: a conservative union across platforms.

**`BuiltInGitHubMcpServer`** (`const string`) — The name of the runtime's built-in GitHub MCP server.

### Key Methods

#### Create(CopilotClient client, IList&lt;AIFunction&gt; tools, instructions, onPermissionRequest, name, model, workingDirectory)

The single public entry point for an agent.

**Algorithm:**

1. Reject a null `client`.
2. Hand the arguments to `CreateAgent` with the production channels over the client: an opener that
   creates a session and **keeps** it on release, a resumer that resumes a recorded session and keeps
   it too, and an opener for a run made without a conversation that **deletes** the session on
   release. A conversation continues on its next run, so its sessions are never deleted by AgentKit.
   A run made without a conversation has nothing that could resume it, so its session is deleted;
   see *CopilotTurnChannel Unit Design*.
3. Return the agent as an `AIAgent`.

**Throws:** `ArgumentNullException` when `client` or `tools` is null, or a tool is null;
`ArgumentException` when `tools` is empty, two tools share a name, or a tool carries a reserved
built-in name.

**Parameter ordering.** `model` and `workingDirectory` are trailing optional parameters rather than
sitting beside `instructions`, which is where they would read most naturally. The two goals conflict:
inserting a parameter earlier would shift `onPermissionRequest` and `name`, breaking source
compatibility for every positional caller of a released public API. Compatibility wins, and the
awkward position is recorded here so a later maintainer does not "tidy" it.

**The working directory** is the directory the runtime gives the agent's sessions — typically the
root of the workspace the tools are confined to. In-process hosting has no client-level working
directory, so this parameter is the only place a host can set one without building a session by
hand, which would forfeit the confinement. It is not validated here: the runtime resolves it when a
session is created. It grants nothing: what the agent may read or write is decided by the tools' own
path policy.

#### CreateAgent(tools, instructions, onPermissionRequest, name, model, workingDirectory, opener, resumer, ephemeralOpener)

Internal seam behind `Create`: production passes channels over the host's client, a test passes a
scripted runtime, and everything else is shared.

**Algorithm:**

1. Validate `tools` (see `ValidateTools`), so a malformed tool list fails at composition time rather
   than on the first run.
2. Snapshot the tool list, so a later change to the host's list can neither widen nor narrow sessions
   the agent opens afterwards: every run is confined to the tools validated here.
3. Construct a `CopilotAgent` with two builder delegates — one calling `BuildSessionConfig`, one
   calling `BuildResumeSessionConfig`, both over the snapshot and the host's settings — together with
   the opener, the ephemeral opener (the opener itself when none is supplied, as a test may), the
   resumer and the name.

The agent calls a builder on every run, so each run receives a **fresh** options object. The SDK
mutates the options it is handed when it applies its mode defaults, and its clone is shallow, so an
options object is never reused or cloned.

#### BuildSessionConfig(tools, instructions, onPermissionRequest, model, workingDirectory)

Internal seam producing the `CreateSessionOptions` for an agent's first run. It exists as a named,
internal method so a test can assert the safety-critical property — that the allow-list is derived
from the same collection as the tool definitions — without a live `CopilotClient`, since the options
type is a plain constructable object.

**Algorithm:** Validate `tools`; build the options through `CreateSessionConfig`; turn on
`Events.Streaming`, because an agent's caller reads a run as it happens.

It leaves `State.InfiniteSessions` untouched, so a plain agent keeps the runtime's own compaction at
the runtime's own threshold. See *Session Options Are Built on One Path* below.

**Throws:** `ArgumentNullException` when `tools` is null or contains a null entry;
`ArgumentException` when `tools` is empty, two tools share a name, or a tool carries a reserved name.

#### BuildResumeSessionConfig(tools, instructions, onPermissionRequest, model, workingDirectory)

Internal seam producing the `ResumeSessionOptions` an agent resumes a recorded session with.

**A resumed session is confined afresh, not trusted to remember.** The runtime applies a resume's
options to the session it reopens, so a resume built without the allow-list, the closed channels and
the permission handler would hand an existing conversation the runtime's defaults — every built-in
tool, and permission requests no handler answers.

**Algorithm:** Validate `tools`; construct a new `ResumeSessionOptions`; apply `ApplyConfinement` and
`ApplySessionSettings` exactly as the create path does; turn on `Events.Streaming`. It differs from
`BuildSessionConfig` only in its type, and like it leaves the runtime's compaction alone.

**Throws:** as `BuildSessionConfig`.

#### BuildEngineSessionConfig(IList&lt;AIFunction&gt; tools, string? instructions, string? model, string? workingDirectory)

Internal seam producing the `CreateSessionOptions` for a session whose context AgentKit's own session
engine manages — a compacting conversation, or a consolidation. It applies exactly the same
confinement as the agent path, through the same private helper, and then sets one further property:
the Copilot runtime's background-compaction threshold is raised to **0.95**, a quarter of the window
clear of the engine's rotation point at 0.70. The infinite-session enablement flag is also set false,
but only as a statement of intent — the runtime ignores it, and nothing on this path depends on it.
See *The Runtime's Own Compaction Is Held Clear of Rotation* in *AgentKitAgentsCopilot System Design*
for the measurement behind that. Streaming is not turned on: the engine and the summarizer take only
a turn's final answer and would gain nothing but extra events.

It differs from the agent path in one further respect: it accepts an **empty** tool list. A
consolidation runs on a session that must offer no tools at all, which is a correct engine-driven
session and an incorrect agent. The confinement is identical either way — an empty tool list derives
an empty allow-list and a permission handler that approves nothing, which is the strongest
confinement this factory can express rather than the weakest.

**Throws:** `ArgumentNullException` when `tools` is null or contains a null entry;
`ArgumentException` when two tools share a name or a tool carries a reserved built-in name.

#### CreateSessionConfig(tools, instructions, onPermissionRequest, model, workingDirectory)

Private helper: the single place a `CreateSessionOptions` is constructed in this package. Both
creating builders reach it, which is what keeps the confinement, the model choice, the working
directory and the system message on one path; they differ only in which tool lists they accept and in
what they adjust afterwards. A new object is built on every call and never reused.

#### ApplyConfinement(SessionOptionsBase config, IList&lt;AIFunction&gt; tools, handler? onPermissionRequest)

Private helper: the one safety-critical block in this package, targeting the options base a created
and a resumed session share.

**Algorithm:**

1. Assign `Tools.Definitions` and `Tools.AvailableTools` from the same collection in two adjacent
   statements, qualifying each allow-list entry as `custom:<name>`, so no reachable state has a
   published tool that is not allowed or an allowed name that is not published.
2. Close every channel in the *Data Model* table above, including the built-in GitHub MCP server, and
   configure the system message to remove the host environment context.
3. Pin `Permissions.Mode` to `PermissionMode.Manual`.
4. Install the supplied handler, or the default-deny one from `CreateDefaultPermissionHandler`.

**Why the qualifier.** Measured against the live runtime, and re-verified by the opt-in live suite, a
bare-name allow-list also admits a built-in tool whose name matches — the runtime's `view` or
`powershell`, for instance. The qualifier restricts each entry to a tool this client registered. The
runtime classifies a tool by where it was registered rather than by parsing its name, so a colliding
name cannot spoof the qualifier.

**Why the pinned mode.** In the runtime's allow-all mode tool, path and URL requests are approved
without asking the handler, and in its assisted mode a client may approve them itself — either would
bypass default-deny without anything here changing. Pinning the manual mode keeps the handler the only
authority, whatever mode ambient configuration would choose.

#### ApplySessionSettings(SessionOptionsBase config, string? instructions, string? model, string? workingDirectory)

Private helper applying the host's own preferences — kept apart from `ApplyConfinement` because they
are preferences rather than safety guarantees. Each is assigned only when non-blank: the model; the
working directory; and the instructions as the content of the system message `ApplyConfinement`
configured, which the runtime appends, never replacing that message, so the removal of the host
environment context survives every combination of settings. It runs after `ApplyConfinement`. Leaving
a member untouched
is what preserves the runtime's default for every caller that says nothing, so each parameter is
purely additive. The model name is not validated: only the runtime knows which models the signed-in
user may use.

#### CreateDefaultPermissionHandler(IList&lt;AIFunction&gt; tools)

Internal helper producing the default-deny permission handler. It captures the supplied tool names
once, compared ordinally, and returns a delegate that:

- approves **once** — `PermissionDecision.ApproveOnce()` — a request that is a custom-tool request
  naming one of those tools;
- rejects every other request with `PermissionDecision.Reject`, naming the custom tool or, for any
  other request, its kind: `'<name>' is not an allowed tool.`

A built-in request — shell, read, write, URL, MCP, memory, hook, extension and the rest — is never a
custom-tool request, so it can never match a supplied name and is always rejected. Measured against
the live runtime, every custom-tool call raises a custom-tool permission request, so this handler is
on the hot path of every tool call; approve-once means every call is adjudicated individually and no
grant outlives the call.

#### QualifyCustomToolName(string name)

Internal helper returning `custom:<name>`, the source-qualified allow-list entry.

#### ValidateTools(IList&lt;AIFunction&gt; tools)

Internal helper rejecting a null or empty tool list, then applying `ValidateToolNames`. An empty list
would produce an empty allow-list for an *agent*, which is a defect in its host.

#### ValidateToolNames(IList&lt;AIFunction&gt; tools)

Internal helper rejecting a null list, a null entry, two tools sharing a name (compared ordinally) and
a tool carrying a name in `ReservedBuiltInToolNames` (compared case-insensitively), and accepting an
empty list. Split out from `ValidateTools` because emptiness is the one rule the paths disagree about;
everything else it checks is a defect on every path.

### Built-In Tool Names Are Refused

The runtime refuses to create a session that registers a custom tool under a built-in tool's name
unless the tool is marked as overriding the built-in. **AgentKit never marks a tool so**: an override
would silently replace the runtime's tool with the application's under a name the model already
associates with other behavior. Left to the runtime, the collision would surface only when a session
is created — on an agent's first run, as a remote error. `ValidateToolNames` refuses it instead, at
composition time, with an `ArgumentException` whose parameter is `tools`:

> The tool name 'view' is the name of a built-in tool of the Copilot runtime; the runtime refuses to
> create a session that registers a custom tool under a built-in's name. Rename the tool.

The guard applies on every path — `Create`, every resume, every engine session and every
consolidation — because every builder validates.

**Why a static union, matched case-insensitively.** Validation stays synchronous and early, and can be
tested offline. The built-in set depends on the platform — the shell family is `powershell` on Windows
and `bash` elsewhere — so the union is reserved, and an application composed on one platform does not
fail later on another. Over-reserving costs only a rename; under-reserving still fails safe, because
the runtime itself refuses at session creation. The opt-in live suite lists the runtime's built-ins on
the platform it runs on and fails if any is missing from the reserved set, so a runtime that adds one
is caught by the scheduled live run rather than by an application.

### Session Options Are Built on One Path

Every Copilot session this package creates or resumes — for an agent, for a compacting conversation,
and for a consolidation — is configured by this class, through `ApplyConfinement` and
`ApplySessionSettings`. The builders differ in exactly these respects, all deliberate:

| | `BuildSessionConfig` | `BuildResumeSessionConfig` | `BuildEngineSessionConfig` |
| --- | --- | --- | --- |
| Options type | create | resume | create |
| Empty tool list | refused | refused | accepted |
| Streaming | on | on | off |
| Runtime's own compaction | untouched | untouched | threshold raised to 0.95 |
| Confinement, handler, host settings | identical | identical | identical |

**The compaction asymmetry is the one a maintainer is most likely to "tidy", and must not.** A
session the engine drives has an AgentKit compactor behind it, and two compactors reading the same
occupancy signal would fight: the runtime would rewrite history underneath a session whose transcript
the engine believes it owns. A plain agent has no AgentKit compactor behind it, so changing the
runtime's there would remove the only protection that session has when its window fills. A test pins
each side.

**The threshold is the setting that carries this, not the enablement flag.** Both are set on the
engine path, but only the threshold is honored by the runtime; the flag states the intent and nothing
depends on it. The measurement establishing that is recorded in *AgentKitAgentsCopilot System
Verification Design*.

### Ownership and Disposal Contract

**The host owns the client; the factory owns nothing disposable.** The host constructs and starts the
`CopilotClient` and therefore disposes it. Nothing the factory builds ever disposes it, and the
factory creates nothing disposable of its own, so the rule is unambiguous: *whoever created the client
disposes it.* The agent it returns releases each runtime session handle after the run that opened it
and keeps a conversation's session, deleting only the session of a run made without a conversation,
which nothing could resume. Deleting a finished conversation is the host's decision, through
`CopilotAgentSession.SessionId`. This is why the public entry point returns a bare `AIAgent` rather
than an agent paired with an ownership handle.

### Design Constraints

**The allow-list is derived, never independently supplied.** The tool definitions and the allow-list
are both built from the one `tools` collection in `ApplyConfinement`, so a caller cannot publish one
tool set while allowing a different one. This is the safety property the package exists for.

**The confinement is AgentKit's own path end to end.** The factory builds the options and hands them,
through the turn channel, straight to the SDK's create or resume call; no third-party agent stands
between them. A session the factory did not configure does not exist in this package.

**No image-promoting decorator.** The Copilot runtime already delivers a tool-returned image to the
model — measured against the live runtime and re-verified by the opt-in live suite — so the factory
does not install Core's `ImagePromotingChatClient`; doing so would duplicate content the model already
received. This absence is intentional and must not be "corrected".

**Default-deny permission handling with host override.** When no handler is supplied the factory
installs the default-deny handler. When one is supplied it is installed unchanged and **replaces** the
default: it is not composed with it, so a host that supplies a handler forfeits default-deny unless
its handler reproduces it. The pinned manual mode applies either way.

**Client precondition: Empty mode on a data directory the host owns.** The integration requires it
and cannot enforce it: the SDK offers no way to read a client's mode back. The factory therefore
relies on the mode for nothing that reaches the model: every capability or context default Empty
mode supplies is set on each session here, and the turn channel clears the installed plugins and
closes every MCP server the session lists — a server from a source discovery does not cover may
start before it is closed, though no turn runs first. What a client that violates the precondition
can still add is host-side
behavior no session option reaches: settings in a shared data directory such as user-level hooks,
credential storage in the system keychain rather than the data directory, telemetry and runtime
file logging, and anything the host attaches at client level, such as plugin directories or
extensions. The `client` parameter's documentation states the precondition.

**Model and working directory are reachable through the factory, not around it.** Which Copilot model
backs a session materially changes how reliably a confined agent uses its tools and how accurately it
reads an image, and a working directory has no client-level setting at all, so the host must be able
to choose both *here*. A host that built session options itself to reach either setting would forfeit
the derived allow-list, the closed channels and the default-deny permission handler — producing an
agent that looks configured but is not confined. Exposing the settings on the factory removes the
incentive to bypass it.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `client` | `ArgumentNullException` propagates |
| Null `tools`, or a null tool entry | `ArgumentNullException` propagates |
| Empty `tools` on the agent path | `ArgumentException` propagates |
| Two tools sharing a name | `ArgumentException` propagates |
| A tool named like a runtime built-in | `ArgumentException` naming the tool propagates |
| A request the default handler does not approve | Rejected with a message naming the tool or kind |
| An MCP server stays enabled on a run's session | `InvalidOperationException` from the turn channel |

### Dependencies

- **CopilotAgent** — the agent `CreateAgent` constructs; see *CopilotAgent Unit Design*.
- **CopilotTurnChannel** — supplies the keeping opener, the resumer and the deleting opener (for a run
  made without a conversation) that `Create` hands the agent; see *CopilotTurnChannel Unit Design*.
- **Microsoft.Copilot.SDK** — supplies `CopilotClient`, `CreateSessionOptions`, `ResumeSessionOptions`
  and `SessionOptionsBase` with their option groups, the permission RPC types, and the published set of
  isolated built-in tool names; see *Microsoft.Copilot.SDK Design*.
- **Microsoft.Agents.AI.Abstractions** — supplies the `AIAgent` type `Create` returns; see
  *Microsoft.Agents.AI.Abstractions Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies the `AIFunction` currency the supplied tools
  are; reached transitively.

### Callers

`CopilotAgentFactory.Create` is a public API entry point. An application calls it once, after
constructing and starting its own `CopilotClient`, to obtain a Copilot agent confined to a supplied
tool set. Within this system, `CopilotAgent` calls the two agent builders on every run through the
delegates `CreateAgent` gave it, and `CopilotProviderSessionFactory` and `CopilotSummarizer` call
`BuildEngineSessionConfig` — which is what keeps every session in the package on one confinement path;
see *CopilotAgent Unit Design*, *CopilotProviderSessionFactory Unit Design* and *CopilotSummarizer Unit
Design*.
