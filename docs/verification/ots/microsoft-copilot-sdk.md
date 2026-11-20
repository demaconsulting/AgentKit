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
- MCP and plugin control: discovery of the MCP servers a runtime would start, per-session disabling of
  named servers at creation and on a live session, a listing of a session's effective MCP
  configuration with each server's enablement, and a session-options patch replacing the session's
  installed plugins.

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

**The opt-in live Copilot suite is supplementary evidence that the SDK and its runtime behave as
AgentKit relies on.** It drives the real SDK against the real Copilot runtime and a real model:
the tools a confined session exposes, the names the runtime reserves for its built-ins, the
permission requests the handler is asked, inline delivery of large and image tool results, a
rotation on the runtime's own usage, an agent's streaming, resumption and attachments, and the
closing of an ambient MCP server on a client in CodingAgent mode. It never
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

### Requirements Coverage

The lifecycle entries are qualified, and the qualification is part of the evidence rather than a
footnote to it: the SDK's session and client types cannot be constructed in a test, so those
scenarios exercise the lifecycle only through AgentKit's own seam. They show AgentKit uses these SDK
features correctly; that the SDK and its runtime implement them is shown by the live Copilot suite,
which is supplementary evidence and is not traced. The MCP and plugin control entries are qualified
the same way: the calls reach the runtime, so the offline tests drive AgentKit's policy over the
SDK's own result and patch types. The live suite shows on the real runtime that discovery reports an
ambient server and the create-time disabled list names it (L12a), that the seal's session-scoped
disable closes a server left enabled at creation and the re-listing confirms it (L12c), and that a
session resumed through the channel has the server named before the resume and no server enabled
afterwards (L12d); it shows the plugins patch accepted, not clearing an installed
plugin, because no fixture installs one.

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
  `AgentKitAgentsCopilotLive_Confinement_AmbientMcpServerOnAResumedSession_IsDisabled`
