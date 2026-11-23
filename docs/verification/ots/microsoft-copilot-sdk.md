## Microsoft.Copilot.SDK Verification

This document provides the verification evidence for the `Microsoft.Copilot.SDK` OTS software item.

### Required Functionality

`Microsoft.Copilot.SDK` supplies the GitHub Copilot SDK the Copilot package builds on:

- the session options shared by session creation and resumption, which carry the session's tool
  definitions, the source-qualified available-tools allow-list, a switch for every channel of
  runtime-injected capability, the permission mode and handler, the event handler and the streaming
  switch, the model, the working directory and the system message with its section overrides;
- the permission RPC, the permission-request hierarchy and the permission-decision type in which the
  default permission handler is expressed;
- the session lifecycle: create a session from options, resume a session by its identifier under new
  options, send one message with optional in-memory attachments and wait for the session to go idle
  under a caller-supplied deadline, release a session handle while preserving its state, and delete
  a session;
- the session event stream, which carries token usage against a limit, tool executions and their
  outcomes, assistant messages and their streamed deltas, per-request usage, session errors, and the
  runtime's own compaction, truncation, context clearing and snapshot rewind, each attributed to the
  agent that produced it;
- MCP and plugin control: discovery of a runtime's MCP servers from the sources it documents,
  per-session disabling of named servers at creation and on a live session, a listing of a session's
  effective MCP configuration with each server's enablement, and a session-options patch replacing
  the session's installed plugins;
- an Empty-mode client that spawns the runtime it provisions as a child process with a supplied
  replacement environment and a supplied data directory, and stops it when disposed;
- the model listing: an uncached query on a started client returning, for each model the service
  offers the signed-in user, its capabilities, limits, billing, reasoning efforts, policy and
  messages;
- per-session inference options carrying a reasoning effort and a context tier on the options a
  session is created and resumed with;
- GitHub authentication: a client-level token passed to the runtime it spawns, a CodingAgent-mode
  client on a supplied home whose runtime reads the system credential store and lists its accounts
  with their tokens, and a session-level token provider asked for each session's token with the host
  and whether it is a refresh.

### Verification Approach

Unlike the build-and-verify pipeline tools, this OTS item is a runtime library with no
self-validation CLI. Per `software-items.md`, an OTS library is verified through integration tests
proving the required functionality works. Two bodies of evidence are used, and they are kept apart.

**AgentKit's own integration tests, over the package's seam, are the traced evidence.** They build
the SDK's real session options through `CopilotAgentFactory` and read back what was set; they
construct the SDK's real permission requests and decisions and observe the default handler's
verdicts; and they run a real `CompactingAgentSession` and a real `CopilotAgent` over a scripted
runtime that opens, resumes and releases sessions through the package's own turn-channel seam and
replays the SDK's **own** session event objects into AgentKit's handlers. The SDK's session and
client types are sealed with non-public constructors and cannot be constructed in a test, so the
lifecycle scenarios show AgentKit driving the lifecycle correctly rather than the SDK implementing
it; the options, the permission types and the event types are all constructable, so nothing about
their shapes is simulated. These tests run in the merge gate on every platform, offline, with no
credential.

The model listing is verified the same way. The SDK's model description types are constructable, so
the tests hand-build them as the service sends them and read every member the catalog depends on back
through the catalog's mapping. The query itself is a sealed type, so the tests drive it only on a
client that was never started, which shows the SDK refuses rather than answers there. The inference
options are constructable too, and are read back from the options the factory builds.

**The opt-in live Copilot suite is supplementary evidence that the SDK and its runtime behave as
AgentKit relies on.** It drives the real SDK against the real Copilot runtime and a real model:
the tools a confined session exposes, the names the runtime reserves for its built-ins, the
permission requests the handler is asked, inline delivery of large and image tool results, a
rotation on the runtime's own usage, an agent's streaming, resumption and attachments, the
disabling of an ambient MCP server on a client in CodingAgent mode, the runtime AgentKit starts
ignoring the host's Copilot variables, and which MCP sources start on that runtime. It never
runs in the pipeline that builds the trace matrix, so none of its tests is linked to a requirement
below. Its scenarios, gating, evidence location and recorded results are described in
*AgentKitAgentsCopilot System Verification Design*.

Two runtime behaviors AgentKit depends on are settled by the live suite rather than by the offline
tests. The runtime ignores the request to disable its own infinite-session compaction but honors the
background-compaction threshold, so AgentKit raises the threshold rather than relying on the flag.
And a bare-name allow-list admits a built-in tool whose name it matches, so AgentKit qualifies every
entry as a custom tool. The native runtime the SDK provisions at build time is a deployment property
rather than a testable behavior, and is documented in the *Microsoft.Copilot.SDK Design*.

### Test Scenarios

#### AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools

**Scenario**: Session options are built from a set of supplied tools.

**Expected**: The tool definitions are the supplied tools, and the available-tools allow-list is
exactly their names, each qualified as a custom tool, in the same order.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-SessionOptions`.

#### CopilotAgentFactory_BuildSessionConfig_InjectedCapability_IsWithheld

**Scenario**: Session options are built for an agent.

**Expected**: The options carry skills disabled, custom instructions skipped, large-output diversion,
tool search and memory disabled, the cross-session store and repository hooks off, no built-in
sub-agents, no MCP servers of the host's, and the runtime's built-in GitHub MCP server disabled.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-SessionOptions`.

#### CopilotAgentFactory_BuildSessionConfig_PermissionMode_IsPinnedToManual

**Scenario**: Options are built for a created agent session, a resumed agent session and an engine
session.

**Expected**: Each carries the permission mode in which every request is put to the handler.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-SessionOptions`.

#### CopilotAgentFactory_BuildResumeSessionConfig_DerivesTheSameConfinement

**Scenario**: Creation options and resumption options are built from the same tools, instructions,
model and working directory.

**Expected**: The resumption options carry the same allow-list, tool definitions, withheld channels,
model, working directory and system message, and a default-deny handler that rejects a built-in
request. This shows the shared options base carries every setting AgentKit needs on both paths.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-SessionOptions`.

#### CopilotAgentFactory_BuildSessionConfig_EnvironmentContext_IsRemoved

**Scenario**: Options are built for a created agent session, a resumed agent session and an engine
session, each with and without instructions.

**Expected**: Each carries a system message in customize mode whose section overrides remove the host
environment context section, with the instructions, or nothing, as its content.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-SessionOptions`.

#### AgentKitAgentsCopilot_DefaultPermissionHandler_SuppliedTool_IsApproved

**Scenario**: The default handler is invoked with a custom-tool request naming a supplied tool.

**Expected**: The returned decision is an approval.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-PermissionRpc`.

#### AgentKitAgentsCopilot_DefaultPermissionHandler_UnlistedCustomTool_IsRejected

**Scenario**: The default handler is invoked with a custom-tool request whose name was not supplied.

**Expected**: The returned decision is a rejection.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-PermissionRpc`.

#### AgentKitAgentsCopilot_DefaultPermissionHandler_BuiltInTool_IsRejected

**Scenario**: The default handler is invoked with a built-in request that is not a supplied custom
tool.

**Expected**: The returned decision is a rejection.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-PermissionRpc`.

#### AgentKitAgentsCopilot_Session_AnswersAndReportsTheRuntimesOccupancy

**Scenario**: A real `CompactingAgentSession` runs one turn over a Copilot session whose event
stream reports an assistant message and a usage reading.

**Expected**: The answer reaches the caller, and the occupancy, limit and conversation split the
engine accounts against are exactly the figures the runtime reported.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-SessionLifecycle` (above the seam only)
and `AgentKit-OTS-MicrosoftCopilotSDK-SessionEvents`.

#### AgentKitAgentsCopilot_Session_RotatesOnTheRuntimesUsage_AndSeedsTheReplacement

**Scenario**: The runtime reports an occupancy past the rotation threshold, so the engine
consolidates, creates a replacement session and releases the one it replaced.

**Expected**: A second session is created, governed by the application's instructions alone, the
consolidated record arrives on its first message, and the superseded session is released exactly
once.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-SessionLifecycle` (above the seam only).

#### AgentKitAgentsCopilot_Agent_SerializedSession_ResumesUnderTheSameConfinement

**Scenario**: A conversation runs once on a first agent, is serialized, and is restored and run on a
second agent.

**Expected**: The second agent resumes the same runtime session by its identifier, under options
that re-apply the allow-list and the default-deny handler.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-SessionLifecycle` (above the seam only).

#### CopilotAgent_Run_DataContent_IsSentAsABlobAttachment

**Scenario**: An agent runs on a message carrying text and an unnamed PNG image.

**Expected**: The runtime is sent one message whose prompt is the text and whose single attachment is
an in-memory blob carrying the image's base64 data, its media type and a generated name.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-SessionLifecycle` (above the seam only).

#### AgentKitAgentsCopilot_Session_RuntimeCompactionIsHeldClearOfRotationOnEverySessionItBuilds

**Scenario**: A run that rotates once, so a first session and a replacement are both created.

**Expected**: Both are created with the engine path's infinite-session configuration, with the
background-compaction threshold raised clear of AgentKit's rotation point, rather than the SDK's
default.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-SessionEvents`.

#### CopilotAgent_RunStreaming_TextDeltas_AreStreamedAsTextContent

**Scenario**: The runtime dispatches two assistant message deltas and then the complete message they
belong to.

**Expected**: The text is streamed once, as the two deltas, under the message's identifier.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-SessionEvents`.

#### CopilotAgent_RunStreaming_AssistantUsage_IsStreamedAsUsageContent

**Scenario**: The runtime dispatches a per-request usage event.

**Expected**: The input, output, total and cached-input token counts and the cache-write count are
streamed as usage content.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-SessionEvents`.

#### CopilotSessionObserver_OnEvent_ContextClearedOrRewound_MarksHistoryRewritten

**Scenario**: The SDK's context-cleared event and its snapshot-rewind event are dispatched to an
observer each.

**Expected**: Both observers report the history rewritten.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-SessionEvents`.

#### CopilotSessionChannel_Seal_ServerStillEnabledAfterCreation_IsDisabledThenVerified

**Scenario**: The SDK's configured-server type reports a non-GitHub server enabled on a session's
first listing and disabled on the second.

**Expected**: Only that server is disabled, the configuration is listed again, and the session is
accepted. This shows the SDK's listing type carries the name and enablement the seal acts on.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-McpAndPluginControl`.

#### CopilotSessionChannel_Seal_ServerThatStaysEnabled_RefusesNamingIt

**Scenario**: The SDK's configured-server type keeps reporting a server enabled after it was disabled.

**Expected**: The session is refused with an error naming the server.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-McpAndPluginControl`.

#### CopilotSessionChannel_NoInstalledPluginsPatch_ReplacesTheListWithAnEmptyOne

**Scenario**: The plugin patch the seal sends is built.

**Expected**: The SDK's plugin-options type carries an empty installed list rather than none, which
would be omitted from the patch.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-McpAndPluginControl`.

#### AgentKitAgentsCopilot_EverySession_AmbientMcpServersAreClosed

**Scenario**: The factory's engine and agent options are opened through the package's confining
protocol with a discovery reporting a non-GitHub server and a listing that keeps it enabled.

**Expected**: The server is on the options' disabled servers when the session is opened, the
installed plugins are cleared, the server is disabled, and the session is refused and abandoned.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-McpAndPluginControl`.

#### AgentKitAgentsCopilot_Runtime_ClientOptionsAreConfined

**Scenario**: The runtime's client options are built from a hostile host environment on a fresh data
directory, for the installed CLI and for the bundled runtime.

**Expected**: The SDK's options carry Empty mode and a spawned stdio connection — to the resolved CLI
by path, or with no path for the provisioned runtime — with no arguments, the prepared data directory
as its base directory, and a replacement environment free of `COPILOT_` and `GITHUB_COPILOT_`
variables.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-EmptyModeSpawnedRuntime`.

#### CopilotRuntime_BuildClientOptions_InstalledCli_SpawnsTheResolvedCliOverStdio

**Scenario**: The client options are built for an installed CLI path and for the bundled runtime from
the same host environment.

**Expected**: The SDK's spawned stdio connection carries exactly the CLI path, and otherwise the same
mode, base directory and replacement environment as the bundled runtime's.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-EmptyModeSpawnedRuntime`.

#### CopilotRuntime_DisposeAsync_IsIdempotentAndClientIsThenUnavailable

**Scenario**: An Empty-mode `CopilotClient` is constructed on a spawned connection with a base
directory, wrapped in a runtime and disposed twice.

**Expected**: The SDK accepts the Empty-mode options, which it rejects without a base directory, and
the client is disposed once.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-EmptyModeSpawnedRuntime`.

#### CopilotModelCatalog_Map_BillingTiers_AreDerivedLabeledAndCapped

**Scenario**: An SDK `Rpc.Model` is hand-built as the service describes `claude-haiku-5.5` — its
limits, its billing token prices with a long-context price, and its efforts — and mapped by the
catalog.

**Expected**: The limits, `Billing.TokenPrices.MaxPromptTokens` and `TokenPrices.LongContext` are
read, giving a `default` tier of 228,000 tokens and a `long_context` tier capped at 1,000,000, a
largest window of 1,000,000 and an output limit of 128,000.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-ModelListing`.

#### CopilotModelCatalog_Map_Policy_DecidesUsability

**Scenario**: The same model is mapped with a disabled policy, with an unconfigured policy carrying
terms, and with an enabled one.

**Expected**: `Policy.State` and `Policy.Terms` are read: the disabled model is unusable with
"Disabled by your organization's Copilot policy.", the unconfigured one with "Not enabled for your
account; enable it in GitHub Copilot settings." and a notice carrying the terms, and the enabled one
is usable.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-ModelListing`.

#### CopilotModelCatalog_ListAsync_ClientNotConnected_FailsWithTheSignInMessage

**Scenario**: The catalog lists models through the uncached query on a runtime whose client was
never started.

**Expected**: The SDK refuses with an `InvalidOperationException` rather than answering, and the
catalog surfaces it as its sign-in message with a redacted copy of the SDK's failure, naming its
type, as the inner exception.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-ModelListing`.

#### CopilotAgentFactory_BuildSessionConfig_Selection_SetsModelEffortAndTier

**Scenario**: Create options are built from a selection choosing an effort and the long-context tier.

**Expected**: The options' `Inference` carries the effort as `ReasoningEffort` and the tier as
`ContextTier`, which requires both members to be present on the create options and settable.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-InferenceOptions`.

#### CopilotAgentFactory_ResumeAndEnginePaths_CarryTheSameSelection

**Scenario**: Resume options and engine create options are built from one selection.

**Expected**: Both carry the same `Inference.ReasoningEffort` and `Inference.ContextTier`, which
requires the inference options on the shared session-options base.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-InferenceOptions`.

#### CopilotRuntime_BuildClientOptions_SignInPlan_CarriesTheTokenAndHost

**Scenario**: The client options are built from a sign-in plan carrying a token and a host name.

**Expected**: `ClientGitHubOptions.Token` carries the token, and the spawned connection's replacement
environment carries `COPILOT_GH_HOST` with the bare host name, in Empty mode.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-GitHubAuthentication`.

#### CopilotSignIn_BuildHelperOptions_RunsTheSameExecutableWithNothingElse

**Scenario**: The helper's client options are built for an installed CLI path, an environment and a
home.

**Expected**: The SDK accepts `CopilotClientMode.CodingAgent` on a spawned stdio connection with that
path, environment and base directory, and no token, arguments or capability channel.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-GitHubAuthentication`.

#### CopilotSignIn_CreateSessionTokenProvider_AsksTheHostsProvider

**Scenario**: The session token provider AgentKit builds is called with the SDK's own
`GitHubTokenProviderArgs`, for `Initial` and `Refresh`.

**Expected**: The SDK's argument type carries the host and the reason, and its result and token types
carry the access token and its lifetime in seconds.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-GitHubAuthentication`.

#### CopilotSignIn_ResolveAsync_AccountList_TakesOnlyTheAccountsUserEntry

**Scenario**: The account list is hand-built from the SDK's `AccountAllUsers` and `AuthInfoUser`
types, with another login's entry before the account's.

**Expected**: The SDK's types carry the host, login and token AgentKit reads, and only the account's
own entry's token is taken.

**Requirement coverage**: `AgentKit-OTS-MicrosoftCopilotSDK-GitHubAuthentication`.

### Requirements Coverage

The GitHub authentication entries are qualified the same way: the offline tests build the client
token, the helper's CodingAgent options on the same executable and the session token provider, and
map the SDK's account list; the live suite shows the runtime honors them — the Copilot CLI account's
token read through the helper and accepted with its host (L-S1, L-S2), an explicit token with its
host (L-S3), and a token provider asked at start and again for a session on a client started with its
token (L-S4). The account list's deserialization defect for accounts other than a stored sign-in was
observed live with the GitHub CLI visible to the helper, which is why the helper hides it.

The lifecycle entries are qualified, and the qualification is part of the evidence rather than a
footnote to it: the SDK's session and client types cannot be constructed in a test, so those
scenarios exercise the lifecycle only through AgentKit's own seam. They show AgentKit uses these SDK
features correctly; that the SDK and its runtime implement them is shown by the live Copilot suite,
which is supplementary evidence and is not traced. The MCP and plugin control entries are qualified
the same way: the calls reach the runtime, so the offline tests drive AgentKit's policy over the
SDK's own result and patch types. The live suite shows on the real runtime that discovery reports an
ambient server and the create-time disabled list names it (L12a), that the seal's session-scoped
disable turns off a server left enabled at creation and the re-listing confirms it (L12c), and that a
session resumed through the channel has the server named before the resume and no server enabled
afterwards (L12d); that a data-directory server discovery reports is never executed through the channel
while it is executed without it (L14c, L14d); and that the listing does not report the built-in GitHub
server while it is connected, so that server is prevented by the create-time disabled list (L14a,
L14b). It shows the plugins patch accepted, not clearing an installed plugin, because no fixture
installs one. The Empty-mode spawned runtime is qualified the same way: the offline tests show the SDK
accepts the options AgentKit builds, and the live suite shows the runtime honors them — every scenario
runs on such a runtime, and one started under a bogus runtime-path variable and an approve-all switch
opens a confined session while an in-process client under the same environment fails to start (L11).
That the SDK spawns a supplied CLI path with the same arguments, environment and data directory,
refuses a runtime on another protocol at start, and reports the started runtime's version, protocol
and authentication, is likewise shown live: the suite runs whole on an installed standalone CLI and on
the provisioned runtime, L0 reads the version and protocol each reports, and a run against a CLI on
the older protocol is refused (recorded in *AgentKitAgentsCopilot System Verification Design*).

- **`AgentKit-OTS-MicrosoftCopilotSDK-SessionOptions`**:
  AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools,
  CopilotAgentFactory_BuildSessionConfig_InjectedCapability_IsWithheld,
  CopilotAgentFactory_BuildSessionConfig_PermissionMode_IsPinnedToManual,
  CopilotAgentFactory_BuildResumeSessionConfig_DerivesTheSameConfinement,
  CopilotAgentFactory_BuildSessionConfig_EnvironmentContext_IsRemoved
- **`AgentKit-OTS-MicrosoftCopilotSDK-PermissionRpc`**:
  AgentKitAgentsCopilot_DefaultPermissionHandler_SuppliedTool_IsApproved,
  AgentKitAgentsCopilot_DefaultPermissionHandler_UnlistedCustomTool_IsRejected,
  AgentKitAgentsCopilot_DefaultPermissionHandler_BuiltInTool_IsRejected
- **`AgentKit-OTS-MicrosoftCopilotSDK-SessionLifecycle`** _(evidence above the seam only)_:
  AgentKitAgentsCopilot_Session_AnswersAndReportsTheRuntimesOccupancy,
  AgentKitAgentsCopilot_Session_RotatesOnTheRuntimesUsage_AndSeedsTheReplacement,
  AgentKitAgentsCopilot_Agent_SerializedSession_ResumesUnderTheSameConfinement,
  CopilotAgent_Run_DataContent_IsSentAsABlobAttachment
- **`AgentKit-OTS-MicrosoftCopilotSDK-SessionEvents`**:
  AgentKitAgentsCopilot_Session_AnswersAndReportsTheRuntimesOccupancy,
  AgentKitAgentsCopilot_Session_RuntimeCompactionIsHeldClearOfRotationOnEverySessionItBuilds,
  CopilotAgent_RunStreaming_TextDeltas_AreStreamedAsTextContent,
  CopilotAgent_RunStreaming_AssistantUsage_IsStreamedAsUsageContent,
  CopilotSessionObserver_OnEvent_ContextClearedOrRewound_MarksHistoryRewritten
- **`AgentKit-OTS-MicrosoftCopilotSDK-McpAndPluginControl`** _(evidence above the runtime only)_:
  CopilotSessionChannel_Seal_ServerStillEnabledAfterCreation_IsDisabledThenVerified,
  CopilotSessionChannel_Seal_ServerThatStaysEnabled_RefusesNamingIt,
  CopilotSessionChannel_NoInstalledPluginsPatch_ReplacesTheListWithAnEmptyOne,
  AgentKitAgentsCopilot_EverySession_AmbientMcpServersAreClosed
- **`AgentKit-OTS-MicrosoftCopilotSDK-EmptyModeSpawnedRuntime`** _(evidence above the runtime only)_:
  AgentKitAgentsCopilot_Runtime_ClientOptionsAreConfined,
  CopilotRuntime_DisposeAsync_IsIdempotentAndClientIsThenUnavailable,
  CopilotRuntime_BuildClientOptions_InstalledCli_SpawnsTheResolvedCliOverStdio
- **`AgentKit-OTS-MicrosoftCopilotSDK-ModelListing`** _(the query is driven only on a client not started)_:
  CopilotModelCatalog_Map_BillingTiers_AreDerivedLabeledAndCapped,
  CopilotModelCatalog_Map_Policy_DecidesUsability,
  CopilotModelCatalog_ListAsync_ClientNotConnected_FailsWithTheSignInMessage
- **`AgentKit-OTS-MicrosoftCopilotSDK-InferenceOptions`**:
  CopilotAgentFactory_BuildSessionConfig_Selection_SetsModelEffortAndTier,
  CopilotAgentFactory_ResumeAndEnginePaths_CarryTheSameSelection
- **`AgentKit-OTS-MicrosoftCopilotSDK-GitHubAuthentication`** _(evidence above the runtime only)_:
  CopilotRuntime_BuildClientOptions_SignInPlan_CarriesTheTokenAndHost,
  CopilotSignIn_BuildHelperOptions_RunsTheSameExecutableWithNothingElse,
  CopilotSignIn_CreateSessionTokenProvider_AsksTheHostsProvider,
  CopilotSignIn_ResolveAsync_AccountList_TakesOnlyTheAccountsUserEntry

The live suite's supplementary evidence for the same functionality, by scenario:

- **Session options**: `AgentKitAgentsCopilotLive_Confinement_EmptyModeCustomAllowList_ExposesOnlySuppliedTools`,
  `AgentKitAgentsCopilotLive_Confinement_BareAllowList_WouldAdmitBuiltIns`,
  `AgentKitAgentsCopilotLive_BuiltInToolNames_AreAllReserved`,
  `AgentKitAgentsCopilotLive_CollisionGuard_ToolNamedLikeABuiltIn_IsRefusedBeforeSessionCreation`,
  `AgentKitAgentsCopilotLive_LargeOutput_ThirtyKilobyteResult_IsDeliveredInline`
- **Permission RPC**: `AgentKitAgentsCopilotLive_CustomTool_ReadsInsideAndIsDeniedOutside`,
  `AgentKitAgentsCopilotLive_DefaultDeny_OnlyCustomToolRequestsAreApproved`
- **Session lifecycle**: `AgentKitAgentsCopilotLive_Agent_SerializedSession_ResumesOnANewAgent`,
  `AgentKitAgentsCopilotLive_Agent_ImageAttachment_ReachesTheModel`,
  `AgentKitAgentsCopilotLive_Usage_RotationOnTheRuntimesUsage_PreservesTheCodeword`
- **Session events**: `AgentKitAgentsCopilotLive_Agent_Streams_TextDeltas`,
  `AgentKitAgentsCopilotLive_ImageDelivery_ImageReadResult_ReachesTheModel`,
  `AgentKitAgentsCopilotLive_Usage_RotationOnTheRuntimesUsage_PreservesTheCodeword`
- **MCP and plugin control**: `AgentKitAgentsCopilotLive_Confinement_AmbientMcpServerOnACodingAgentClient_IsDisabled`,
  `AgentKitAgentsCopilotLive_Confinement_AmbientMcpServerWithoutTheChannel_IsEnabled`,
  `AgentKitAgentsCopilotLive_Confinement_AmbientMcpServerEnabledAtCreation_IsDisabledByTheSeal`,
  `AgentKitAgentsCopilotLive_Confinement_AmbientMcpServerOnAResumedSession_IsDisabled`,
  `AgentKitAgentsCopilotLive_OwnedRuntime_BuiltInGitHubMcpServer_IsNeverStarted`,
  `AgentKitAgentsCopilotLive_OwnedRuntime_BuiltInGitHubMcpServer_StartsWhenNotNamed`,
  `AgentKitAgentsCopilotLive_OwnedRuntime_UserMcpServerInItsDataDirectory_IsNeverExecuted`,
  `AgentKitAgentsCopilotLive_OwnedRuntime_UserMcpServerWithoutTheChannel_IsExecuted`,
  `AgentKitAgentsCopilotLive_OwnedRuntime_WorkspaceMcpServer_IsNeverExecuted`,
  `AgentKitAgentsCopilotLive_OwnedRuntime_WorkspaceMcpServerWithConfigDiscovery_IsExecuted`,
  `AgentKitAgentsCopilotLive_OwnedRuntime_AmbientChannels_AreAbsent`
- **Empty-mode spawned runtime**: `AgentKitAgentsCopilotLive_Runtime_AmbientCopilotEnvironment_DoesNotReachTheRuntime`,
  `AgentKitAgentsCopilotLive_Runtime_ReportsASupportedRuntime`, and the fixture every scenario runs
  on, on the installed CLI and on the provisioned runtime
- **Model listing**: `AgentKitAgentsCopilotLive_Catalog_ListsFiltersAndRefreshes`,
  `AgentKitAgentsCopilotLive_Selection_UnofferedEffort_IsRefusedBeforeAnySession`
- **Inference options**: `AgentKitAgentsCopilotLive_Selection_SessionRunsOnTheChosenEffortAndTier`
- **GitHub authentication**: `AgentKitAgentsCopilotLive_SignIn_DefaultOptions_UseTheCopilotCliAccount`,
  `AgentKitAgentsCopilotLive_SignIn_GitHubCliHidden_StillUsesTheCopilotCliAccount`,
  `AgentKitAgentsCopilotLive_SignIn_ExplicitTokenAndHost_AreUsed`,
  `AgentKitAgentsCopilotLive_SignIn_TokenProvider_SignsInTheRuntimeAndItsSessions`
