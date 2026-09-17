## Microsoft.Agents.AI.GitHub.Copilot Verification

This document provides the verification evidence for the `Microsoft.Agents.AI.GitHub.Copilot` OTS
software item.

### Required Functionality

`Microsoft.Agents.AI.GitHub.Copilot` supplies the GitHub Copilot SDK the Copilot adapter builds on:
`SessionConfig`, carrying the session's tools and the available-tools allow-list; the session-config
agent-construction path that builds an `AIAgent` from a `CopilotClient` without taking ownership of
it; the permission RPC — the permission-request hierarchy and the permission-decision type — in
which the default permission handler is expressed; the session lifecycle the provider session is one
of; and the session event stream the adapter's occupancy figure, tool-traffic record and
divergence detection all come from.

### Verification Approach

Unlike the build-and-verify pipeline tools, this OTS item is a runtime library with no
self-validation CLI. Per `software-items.md`, an OTS library is verified through integration tests
proving the required functionality works. AgentKit's own tests construct a `SessionConfig` through
`CopilotAgentFactory` and observe its allow-list is derived from the published tools, construct
permission requests and decisions and observe the default handler approves a supplied tool while
rejecting an unlisted or built-in request, and run a real `CompactingAgentSession` to a rotation over
the adapter's turn-channel seam, driving the SDK's **own** session event objects through the
adapter's matching code. These exercise `SessionConfig`, the permission RPC and the event payload
shapes directly and offline.

**Two things are out of automated scope, stated plainly.** The agent-construction call and the
session lifecycle calls reach an authenticated Copilot CLI, which is not available in a unit test and
which this repository deliberately does not require; their offline evidence is that the
allow-list-carrying `SessionConfig` they consume is built correctly and that every session AgentKit
opens is released on every path. And **whether a live runtime dispatches each event when expected
cannot be established offline**: the SDK is observed to carry the configuration to the wire unchanged,
and the adapter watches for the runtime's own compaction and truncation events so that a rewrite is
refused rather than silently absorbed. How the SDK treats the infinite-session configuration is no
longer open: manual measurement against the live runtime showed the enablement flag is ignored and
the background-compaction threshold is honored, so AgentKit raises the threshold and does not rely on
the flag. The numbers are recorded in the _AgentKitAgentsCopilot System Verification Design_, which
also records both boundaries above. The SDK's RID-specific native runtime, a deployment property
rather than a testable behavior, is documented in the _Microsoft.Agents.AI.GitHub.Copilot Design_.

### Test Scenarios

#### AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools

**Scenario**: A session configuration is built from a set of supplied tools.

**Expected**: The available-tools allow-list is exactly the names of the published tools, derived from
the same collection.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-SessionConfig`,
`AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-AsAIAgent`.

#### AgentKitAgentsCopilot_DefaultPermissionHandler_SuppliedTool_IsApproved

**Scenario**: The default handler is invoked with a custom-tool request naming a supplied tool.

**Expected**: The returned decision is an approval.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-PermissionRpc`.

#### AgentKitAgentsCopilot_DefaultPermissionHandler_UnlistedCustomTool_IsRejected

**Scenario**: The default handler is invoked with a custom-tool request whose name was not supplied.

**Expected**: The returned decision is a rejection.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-PermissionRpc`.

#### AgentKitAgentsCopilot_DefaultPermissionHandler_BuiltInTool_IsRejected

**Scenario**: The default handler is invoked with a built-in request that is not a supplied custom
tool.

**Expected**: The returned decision is a rejection.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-PermissionRpc`.

#### AgentKitAgentsCopilot_Session_AnswersAndReportsTheRuntimesOccupancy

**Scenario**: A real `CompactingAgentSession` runs one turn over a Copilot session whose event
stream reports an assistant message and a usage reading.

**Expected**: The answer reaches the caller, and the occupancy, limit and conversation split the
engine accounts against are exactly the figures the runtime reported.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-SessionLifecycle`,
`AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-SessionEvents`.

#### AgentKitAgentsCopilot_Session_RotatesOnTheRuntimesUsage_AndSeedsTheReplacement

**Scenario**: The runtime reports an occupancy past the rotation threshold, so the engine
consolidates, creates a replacement session and releases the one it replaced.

**Expected**: A second session is created carrying the consolidated record, and the superseded
session is released exactly once.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-SessionLifecycle`.

#### AgentKitAgentsCopilot_Session_RuntimeCompactionIsDisabledOnEverySessionItBuilds

**Scenario**: A run that rotates once, so a first session and a replacement are both created.

**Expected**: Both are created with the engine path's infinite-session configuration — the
background-compaction threshold raised clear of AgentKit's rotation point — rather than the SDK's
default.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-SessionEvents`.

### Requirements Coverage

- **`AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-SessionConfig`**:
  AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools
- **`AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-AsAIAgent`**:
  AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools
- **`AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-PermissionRpc`**:
  AgentKitAgentsCopilot_DefaultPermissionHandler_SuppliedTool_IsApproved,
  AgentKitAgentsCopilot_DefaultPermissionHandler_UnlistedCustomTool_IsRejected,
  AgentKitAgentsCopilot_DefaultPermissionHandler_BuiltInTool_IsRejected
- **`AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-SessionLifecycle`**:
  AgentKitAgentsCopilot_Session_AnswersAndReportsTheRuntimesOccupancy,
  AgentKitAgentsCopilot_Session_RotatesOnTheRuntimesUsage_AndSeedsTheReplacement
- **`AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-SessionEvents`**:
  AgentKitAgentsCopilot_Session_AnswersAndReportsTheRuntimesOccupancy,
  AgentKitAgentsCopilot_Session_RuntimeCompactionIsDisabledOnEverySessionItBuilds
