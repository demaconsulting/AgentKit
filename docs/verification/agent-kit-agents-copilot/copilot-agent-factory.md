## CopilotAgentFactory Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotAgentFactory` class.

### Verification Approach

`CopilotAgentFactory` is verified through unit tests that exercise its session-building seams and its
construction-time validation, all offline. The factory builds three kinds of session options through
one confinement block: `BuildSessionConfig` for an agent's first run, `BuildResumeSessionConfig` for
every later run of the same conversation, and `BuildEngineSessionConfig` for the sessions the session
engine and the summarizer drive. Each is internal and exposed to the tests for exactly this purpose,
and each returns the SDK's own options object — a plain constructable object requiring no client — so
every assertion below reads back what the runtime would be handed rather than inferring it.

The host-handler override, the withheld runtime-injected capability, the pinned permission mode, the
instruction handling, the model and working-directory selection, the streaming switch and the
runtime compaction are asserted against built options; the validation scenarios pin each rejected
condition, including a tool named like one of the runtime's built-ins; and the null-client refusal is
asserted through the public entry point, which validates before any session is requested.

**The two safety-critical guarantees — the derived allow-list and the default-safe permission
handler — are evidenced by tests that live with this unit's callers as well as its own, and are named
in the scenarios below rather than copied into this unit's own test file.** Every path reaches this
factory's single confinement block, so a test at any caller falsifies the derivation here; writing
another copy against the same code would add no evidence.

Tools are built through the framework's own function factory, and permission requests and decisions
are constructed directly from the SDK's plain types. No Copilot runtime, credential, or network access
is used.

**What is out of automated scope, stated honestly.** That the runtime honors the options — that a
custom-qualified allow-list exposes only the supplied tools, that the reserved-name set matches the
runtime's built-ins, and that a large result arrives inline — is not provable offline. It is shown on
the real runtime by the opt-in live Copilot suite, which is supplementary evidence outside the trace
matrix, described in *AgentKitAgentsCopilot System Verification Design*.

Unit tests reside in `CopilotAgentFactoryTests.cs` within the
`DemaConsulting.AgentKit.Agents.Copilot.Tests` project; the cited caller-level tests reside in
`AgentKitAgentsCopilotTests.cs` and `CopilotProviderSessionFactoryTests.cs` in the same project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; **no Copilot runtime and no network access** is used
- **Isolation**: Each test constructs its own tools and options; no state is shared

### Acceptance Criteria

A unit test run passes when all twenty scenarios below pass without error or exception beyond those
explicitly asserted. Any allow-list that diverges from the published tools or carries an unqualified
entry, any request approved that names no supplied tool, any invalid argument that is accepted, any
tool named like a runtime built-in that is accepted on any path, any host handler that is not
installed verbatim, any session whose permission mode lets the runtime approve without asking the
handler, any instruction, model or working directory that is not carried onto the session — or that
is invented when the host named none — any channel of runtime-injected capability left open, any
session that diverts a large tool result, any resumed session confined differently from the created
one, any agent session built without streaming or engine session built with it, any engine session
left at the runtime's default compaction threshold, any session built on a selection without its
model or chosen effort and tier, any inference option set that was not chosen, any model named
twice or foreign selection accepted, or any selection carrying a context window or an output limit
accepted by `Create` constitutes a failure.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotAgentFactory-DerivesAllowList: The Allow-List Is the Supplied Tools

**Tests**: `AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools`,
`CopilotProviderSessionFactory_BuildSessionConfig_AvailableToolsDerivedFromSeededTools`,
`CopilotAgentFactory_BuildSessionConfig_AllowListIsSourceQualified`

The safety-critical scenario, and the reason the package exists. The first two build session options
from a set of supplied tools and assert the available-tools allow-list is exactly the names of the
published tool set, in the same order and of the same size — the two derived from one collection. A
drift here would silently re-admit a built-in tool an application meant to withhold.

The third asserts every entry carries the `custom:` qualifier. That qualifier is what limits an entry
to a tool this package registered: the live suite's control scenario shows a bare-name allow-list
admitting the runtime's own `view` or `powershell` whenever a listed name matches one, so an entry
that lost its qualifier would widen the session without anything the host wrote changing.

**The first two live in the test files of the callers, deliberately, and are named here rather than
duplicated.** The first is the system-level assertion in `AgentKitAgentsCopilotTests.cs`; the second
is the same assertion on the options a rotation produces, in `CopilotProviderSessionFactoryTests.cs`.
Both reach this unit's single derivation, and both were confirmed to falsify it: emptying the
allow-list this factory assigns fails both.

#### AgentKitAgentsCopilot-CopilotAgentFactory-DefaultPermissionHandler: Safe Without Asking

**Tests**: `AgentKitAgentsCopilot_DefaultPermissionHandler_SuppliedTool_IsApproved`,
`AgentKitAgentsCopilot_DefaultPermissionHandler_UnlistedCustomTool_IsRejected`,
`AgentKitAgentsCopilot_DefaultPermissionHandler_BuiltInTool_IsRejected`,
`CopilotProviderSessionFactory_BuildSessionConfig_InstallsTheDefaultSafePermissionHandler`

The handler this factory installs is invoked directly with the three requests that matter — one
naming a supplied tool, one naming an unlisted custom tool, and a built-in `shell` request — and the
decision it returns is asserted in each case: approve, reject, reject. The fourth repeats the listed
and built-in cases against the options a rotation produces, where no host is present to adjudicate
anything.

As above, these tests live in the system and provider-session-factory test files because that is
where the handler is reached from; they are cited here so this unit's evidence can be found rather
than assumed.

#### AgentKitAgentsCopilot-CopilotAgentFactory-PinsManualPermissionMode: Every Request Reaches the Handler

**Test**: `CopilotAgentFactory_BuildSessionConfig_PermissionMode_IsPinnedToManual`

Builds a created agent session, a resumed agent session and an engine session, and asserts each
carries the `Manual` permission mode. The handler can only decide what it is asked: in the runtime's
other modes, requests are approved without being put to it at all, so a default-deny handler would
sit installed and never be consulted. Pinning the mode on every path is what makes the handler the only
authority.

#### AgentKitAgentsCopilot-CopilotAgentFactory-RejectsInvalidToolList: A Malformed Tool List Is Refused

**Tests**: `CopilotAgentFactory_BuildSessionConfig_NullTools_Throws`,
`CopilotAgentFactory_BuildSessionConfig_EmptyTools_Throws`,
`CopilotAgentFactory_BuildSessionConfig_NullToolEntry_Throws`,
`CopilotAgentFactory_BuildSessionConfig_DuplicateToolNames_Throws`

Four error paths: a null tool list and a null entry are refused with `ArgumentNullException`; an
empty list and two tools sharing a name are refused with `ArgumentException`, the latter naming the
colliding tool. An empty list would produce an empty allow-list, and a duplicate name would make both
the published tool set and the derived allow-list ambiguous.

#### AgentKitAgentsCopilot-CopilotAgentFactory-RefusesBuiltInToolNames: A Built-In's Name Is Refused

**Tests**: `CopilotAgentFactory_BuildSessionConfig_BuiltInToolName_Throws`,
`CopilotAgentFactory_BuildEngineSessionConfig_BuiltInToolName_Throws`

The first is a theory over `view`, a built-in on every platform; `bash` and `powershell`, the POSIX
and Windows shells; `View`, a case variant; and `task_complete`, one of the runtime's isolated
built-ins. Each is refused on the agent path with an `ArgumentException` whose parameter is `tools`
and whose message names the tool in quotes and asks for it to be renamed. Both shells are refused
whatever the platform the tests run on, because an application composed on one platform may be run
on another. The case variant is the boundary: a name differing from a built-in's only by case is
refused too, so a guard that compared names exactly would fail it. The second asserts `grep` is
refused on the engine path and on resumption too, so no session this package builds can carry a
colliding name.

The refusal happens before any session exists, which is its whole value: the runtime refuses such a
session itself, but only on the first run, far from the line the application wrote. Whether the
reserved-name set still covers every built-in the runtime ships is a property of the runtime, checked
on each live run by the live suite's reserved-name scenario.

#### AgentKitAgentsCopilot-CopilotAgentFactory-RejectsNullRuntime: A Missing Runtime Is Refused

**Test**: `CopilotAgentFactory_Create_NullRuntime_Throws`

Error path: an agent with no runtime to run on cannot be built, so a null runtime is refused with
`ArgumentNullException` before any session is requested.

#### AgentKitAgentsCopilot-CopilotAgentFactory-HostHandlerOverride: A Host Handler Is Installed Verbatim

**Test**: `CopilotAgentFactory_BuildSessionConfig_HostHandler_IsInstalled`

Verifies that a host-supplied permission handler is the exact delegate installed on the session, so a
host with its own policy governs the session itself rather than layering over the default. A host
handler replaces the default rather than composing with it, so a host that supplies one takes on the
default-deny policy it replaces.

#### AgentKitAgentsCopilot-CopilotAgentFactory-CarriesInstructions: Instructions Are Carried Onto the System Message

**Test**: `CopilotAgentFactory_BuildSessionConfig_Instructions_CarriedOnSystemMessage`

Verifies that supplied instructions are carried as the content of the session's system message, and
that a session built without instructions carries no system-message content, so a confined agent is
governed as the host intended and the factory adds nothing of its own when the host supplies nothing.
The message itself always exists, because it also carries the removal of the host environment
context; that is asserted under the injected-capability scenario below.

#### AgentKitAgentsCopilot-CopilotAgentFactory-SelectsModel: A Named Model Backs the Session

**Tests**: `CopilotAgentFactory_BuildSessionConfig_Model_CarriedOnSession`,
`CopilotAgentFactory_BuildSessionConfig_NoModel_LeavesSessionModelAtDefault`

Two paths. With a model named, the session carries exactly that name, so an application can choose
which Copilot model backs its agent without building a session itself and losing the suppression and
the default-safe handler. With no model named, the session's model equals that of freshly constructed
session options — the factory set nothing — so the runtime applies its own default and an existing
application sees no change.

The unit tests prove the value is carried; they cannot prove the runtime accepts a given name, since
only the runtime knows which models the signed-in user may use. That acceptance is shown by the live
suite, every scenario of which runs on the model `AGENTKIT_COPILOT_LIVE_MODEL` names.

#### AgentKitAgentsCopilot-CopilotAgentFactory-CarriesWorkingDirectory: A Named Directory Is Carried

**Test**: `CopilotAgentFactory_BuildSessionConfig_WorkingDirectory_IsCarriedOrLeftToTheRuntime`

Three cases. A named working directory is carried onto the session exactly; a blank one and an
omitted one both leave the session's working directory unset, so the runtime chooses. The blank case
is the boundary: an empty or whitespace string carried through would hand the runtime a directory
that does not exist rather than leaving the choice to it.

#### AgentKitAgentsCopilot-CopilotAgentFactory-WithholdsInjectedCapability: Runtime-Injected Capability Is Withheld

**Tests**: `CopilotAgentFactory_BuildSessionConfig_InjectedCapability_IsWithheld`,
`CopilotAgentFactory_BuildSessionConfig_GitHubMcpServer_IsDisabled`,
`CopilotAgentFactory_BuildSessionConfig_ImageGeneration_IsDisabled`,
`CopilotAgentFactory_BuildSessionConfig_EnvironmentContext_IsRemoved`,
`CopilotAgentFactory_BuildSessionConfig_ExperimentalModeAndEmbeddingRetrieval_AreOff`,
`CopilotAgentFactory_BuildSessionConfig_GitCoauthorTrailers_AreOff` The first asserts, on the options the factory
builds, that the runtime's skills are disabled, its custom instructions skipped, tool search and memory disabled, the
cross-session store and repository hooks off, the built-in sub-agents removed, configuration discovery and extensions
off, agent discovery local-only, no agent definitions or selected agent, and no MCP server of the host's configured, so
a confined agent receives only the capability and direction its host attached. The second asserts the runtime's built-in
GitHub MCP server is disabled outright on a created agent session, a resumed one and an engine session; the runtime
starts and authenticates that server for every session even in Empty mode, so filtering it through the allow-list alone
would leave it running. The third asserts image generation is switched off. The fourth asserts, on a created agent
session, a resumed one and an engine session, each with and without instructions, that the system message is in
customize mode with the host environment context section removed and the instructions, or nothing, as its content: the
confinement does not depend on the client's mode, so the context Empty mode would remove is removed here in any mode.
The fifth asserts, on all three, that experimental features are off and embedding retrieval is skipped. The sixth
asserts, on all three, that commit co-author trailers are set off rather than left unset: off is an Empty-mode default,
and the SDK forwards the setting in any other mode only when it is set. Every value is read from the built options
rather than inferred, so any one reverting to the permissive value — which would widen the agent without changing
anything the host wrote — fails its scenario. Disabling every other MCP server and clearing the installed plugins need
the live session and are verified in *CopilotTurnChannel Unit Verification Design*.

#### AgentKitAgentsCopilot-CopilotAgentFactory-DisablesLargeOutputDiversion: Results Stay Inline

**Test**: `CopilotAgentFactory_BuildSessionConfig_LargeOutputDiversion_IsDisabled`

Asserts the runtime's diversion of a large tool result to a temporary file is switched off on an
agent session and on an engine session. A diverted result reaches the model as a file path, and the
only tools a confined session may call cannot read it: the live measurement that motivated this saw
the model fail the task. The system scenario *Tool Results: Every Session Delivers Results Inline*
asserts the same on every kind of session in one run, and the live suite's large-output scenario
shows a thirty-kilobyte result arriving whole on the real runtime.

#### AgentKitAgentsCopilot-CopilotAgentFactory-OneConfinementPath: One Derivation, Three Entry Points

**Tests**: `CopilotAgentFactory_BothPaths_DeriveTheSameConfinement`,
`CopilotAgentFactory_BuildResumeSessionConfig_DerivesTheSameConfinement`,
`CopilotAgentFactory_BuildEngineSessionConfig_EmptyTools_ProducesAnEmptyAllowList`,
`CopilotAgentFactory_BuildSessionConfig_EmptyTools_Throws`,
`CopilotAgentFactory_BuildEngineSessionConfig_DuplicateToolNames_Throws`

The first builds the same tool set through the agent and engine entry points and asserts the
allow-list, the published tool names, the skills setting, the custom-instruction setting, the
configuration-discovery setting and the agent options — the built-in sub-agent list and local-only
discovery — are identical. The second does the same for the agent's created and resumed options,
and asserts the resumed options remove the built-in sub-agents, keep agent discovery local-only and
leave configuration discovery off. Together they are
what make having three entry points safe: the entry points differ in what tool lists they accept, in
the runtime compaction and in the streaming switch, and in nothing that decides what a session may
call. An implementation that forked the derivation would pass every other scenario in this file and
fail only these.

The third asserts the engine path accepts an empty tool list and produces an empty allow-list with
the injection channels still shut — the strongest confinement the factory can express, which is what
a consolidation session needs. The fourth asserts the agent path still refuses the same list, because
an agent publishing no tools is a defect in its host; emptiness is the only rule the paths disagree
about. The fifth asserts the engine path still refuses a duplicated tool name, so relaxing emptiness
did not relax everything else.

#### AgentKitAgentsCopilot-CopilotAgentFactory-ConfinesResumedSessions: A Resumption Is Confined as a Creation

**Test**: `CopilotAgentFactory_BuildResumeSessionConfig_DerivesTheSameConfinement`

Builds creation and resumption options from the same tools, instructions, model and working
directory, and asserts the resumption carries the same allow-list and tool definitions, the skills,
custom instructions, large-output diversion and memory withheld, the built-in sub-agents removed,
agent discovery local-only and configuration discovery off, the same model, working directory
and system message, and a default-deny handler that rejects a built-in request. The runtime is handed
a fresh configuration on every resumption, so a resumption built with less than the creation had
would quietly widen a conversation from its second run on.

#### AgentKitAgentsCopilot-CopilotAgentFactory-EnablesStreamingOnTheAgentPath: Streaming Where It Is Read

**Test**: `CopilotAgentFactory_BuildSessionConfig_AgentPath_EnablesStreaming`

Asserts token streaming is on for an agent's created and resumed sessions and not on for an engine
session. The agent streams its answer to the caller as it arrives, which needs the runtime's deltas;
the engine and the summarizer read only a turn's final answer, so deltas there would be traffic
nothing reads.

#### AgentKitAgentsCopilot-CopilotAgentFactory-LeavesTheAgentPathsCompactionAlone: The Deliberate Asymmetry

**Tests**: `CopilotAgentFactory_BuildSessionConfig_LeavesTheRuntimesCompactionUntouched`,
`CopilotAgentFactory_BuildEngineSessionConfig_HoldsTheRuntimesCompactionWellAboveRotation`

Both sides of the asymmetry are asserted, because it is the one a maintainer is most likely to
"tidy" into consistency. The agent path leaves the runtime's infinite-session setting at whatever
freshly constructed session options carry — a plain agent has no AgentKit compactor behind it, so
changing the runtime's would remove the only protection that session has when its window fills. The
engine path raises the runtime's background-compaction threshold above 0.90 — a session the engine
drives has an AgentKit compactor behind it, the engine rotates at 0.70, and the runtime's default of
0.80 leaves only a tenth of the window between two compactors reading one occupancy signal.

**The threshold is asserted rather than the enablement flag, and that is the point of the scenario.**
Manual measurement against the live runtime established that the flag is ignored and the threshold is
honored; the numbers are recorded in *AgentKitAgentsCopilot System Verification Design*. The flag is
asserted too, because the options still state the intent, but a change that kept the flag and
dropped the threshold would leave the two compactors a tenth of a window apart and is what this
scenario is here to catch. What remains unverified is whether a live conversation ever crosses the
raised threshold before the engine rotates it.

#### AgentKitAgentsCopilot-CopilotAgentFactory-AppliesASelection: The Selection Reaches Every Path

**Tests**:

- `CopilotAgentFactory_BuildSessionConfig_Selection_SetsModelEffortAndTier`
- `CopilotAgentFactory_BuildSessionConfig_SelectionWithoutChoices_LeavesInferenceUntouched`
- `CopilotAgentFactory_ResumeAndEnginePaths_CarryTheSameSelection`

The first builds create options from a selection whose effort and tier are spelled `HIGH` and
`Long_Context`, and asserts the options name the model and carry the effort `high` and the
`long_context` tier — the catalog's own identifiers. The second builds options from a selection that
chose neither, and asserts the model is named and neither inference option is set, so the runtime's
own defaults apply. The third builds resume options and engine options from one selection, and
asserts both carry the same model, effort and tier, so the choice holds when a conversation resumes
and on every engine session.

#### AgentKitAgentsCopilot-CopilotAgentFactory-RefusesAnAmbiguousSelection: One Model, From This Provider

**Test**: `CopilotAgentFactory_Create_ConflictingOrForeignSelection_Throws`

Error paths. `Create` with both a model name and a selection throws `ArgumentException` whose message
says "not both"; `Create` with a selection of an Ollama model throws `ArgumentException` naming
provider 'ollama'.

#### AgentKitAgentsCopilot-CopilotAgentFactory-RefusesWhatItCannotApply: A Window Is Refused

**Test**: `CopilotAgentFactory_Create_SelectionWithAWindow_Throws`

Error path. A selection of the Haiku fixture carrying a 4,000-token window — valid for the model, so
only this path can refuse it — is refused by `Create` with `ArgumentException` naming `selection`,
whose message says `ContextWindowTokens` cannot be applied here and names
`CopilotProviderSessionFactory` as the path that applies a window ceiling.

#### AgentKitAgentsCopilot-CopilotAgentFactory-RefusesWhatItCannotApply: An Output Limit Is Refused

**Test**: `CopilotAgentFactory_Create_SelectionWithAnOutputLimit_Throws`

Error path. A selection of a hand-built Copilot model description that grants an output limit —
which the Copilot catalog never does — carrying 1,000 tokens is refused by `Create` with
`ArgumentException` naming `selection`, whose message says `MaxOutputTokens` cannot be applied on
Copilot.
