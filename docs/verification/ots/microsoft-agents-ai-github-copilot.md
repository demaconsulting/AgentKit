## Microsoft.Agents.AI.GitHub.Copilot Verification

This document provides the verification evidence for the `Microsoft.Agents.AI.GitHub.Copilot` OTS
software item.

### Required Functionality

`Microsoft.Agents.AI.GitHub.Copilot` supplies the GitHub Copilot SDK the Copilot adapter builds on:
`SessionConfig`, carrying the session's tools and the available-tools allow-list; the session-config
agent-construction path that builds an `AIAgent` from a `CopilotClient` without taking ownership of
it; and the permission RPC — the permission-request hierarchy and the permission-decision type — in
which the default permission handler is expressed.

### Verification Approach

Unlike the build-and-verify pipeline tools, this OTS item is a runtime library with no
self-validation CLI. Per `software-items.md`, an OTS library is verified through integration tests
proving the required functionality works. AgentKit's own tests construct a `SessionConfig` through
`CopilotAgentFactory` and observe its allow-list is derived from the published tools, and construct
permission requests and decisions and observe the default handler approves a supplied tool while
rejecting an unlisted or built-in request. These exercise `SessionConfig` and the permission RPC
directly and offline.

**The agent-construction call itself is out of automated scope**: it reaches an authenticated Copilot
CLI, which is not available in a unit test and which this repository deliberately does not require.
Its offline evidence is that the allow-list-carrying `SessionConfig` it consumes is built correctly,
as recorded in the _AgentKitAgentsCopilot System Verification Design_. The SDK's RID-specific native
runtime, a deployment property rather than a testable behavior, is documented in the
_Microsoft.Agents.AI.GitHub.Copilot Design_.

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

### Requirements Coverage

- **`AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-SessionConfig`**:
  AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools
- **`AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-AsAIAgent`**:
  AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools
- **`AgentKit-OTS-MicrosoftAgentsAIGitHubCopilot-PermissionRpc`**:
  AgentKitAgentsCopilot_DefaultPermissionHandler_SuppliedTool_IsApproved,
  AgentKitAgentsCopilot_DefaultPermissionHandler_UnlistedCustomTool_IsRejected,
  AgentKitAgentsCopilot_DefaultPermissionHandler_BuiltInTool_IsRejected
