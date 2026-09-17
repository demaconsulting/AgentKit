## Microsoft.Agents.AI Verification

This document provides the verification evidence for the `Microsoft.Agents.AI` OTS software item.

### Required Functionality

`Microsoft.Agents.AI` supplies the Microsoft Agent Framework runtime the ChatClient adapter builds
on: `AIAgent`, the provider-neutral agent abstraction, and `ChatClientAgent`, which builds an
`AIAgent` from an `IChatClient` and a tool list and runs the function-invocation loop above the
client it is given. It also brings the chat-client extensions carrying `FunctionInvokingChatClient`,
the loop itself, which the session factory installs above the client an application supplies.

### Verification Approach

Unlike the build-and-verify pipeline tools, this OTS item is a runtime library with no
self-validation CLI. Per `software-items.md`, an OTS library is verified through integration tests
proving the required functionality works. AgentKit's own tests build an agent through
`ChatClientAgentFactory`, which constructs a `ChatClientAgent` over a scripted client, and exercise
it: one test observes the built agent carries the supplied name, and another sends a tool-returned
image through the client the agent talks to and observes it reach a user message. Together they prove
`AIAgent` and `ChatClientAgent` behave as required. These tests run on every platform with no
platform filter, so a single-OS run satisfies them. A live end-to-end run against a real provider is
out of automated scope, as recorded in the _AgentKitAgentsChatClient System Verification Design_.

### Test Scenarios

#### AgentKitAgentsChatClient_Create_BuildsAgentCarryingSuppliedName

**Scenario**: An agent is built from a scripted client and a tool list through the factory.

**Expected**: A non-null `AIAgent` carrying the supplied name is returned.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAI-Agent`,
`AgentKit-OTS-MicrosoftAgentsAI-ChatClientAgent`.

#### AgentKitAgentsChatClient_ImagePromotion_ToolReturnedImage_ReachesProviderOnUserMessage

**Scenario**: A conversation whose last message is a tool result holding an image is sent through the
client the agent talks to, as the function-invocation loop would.

**Expected**: The client receives an added user message carrying the same image instance.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAI-ChatClientAgent`.

#### ChatClientProviderSessionFactory_CreateAsync_SeededTool_IsInvokedAndItsResultReachesTheTranscript

**Scenario**: A session is created with a tool seeded into it, and a provider is scripted to call
that tool and then answer.

**Expected**: The tool runs, its result goes back to the provider in a further request, and the call
and result are both recorded in the transcript the turn produces.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAI-FunctionInvokingChatClient`.

#### PromptSizeRecordingChatClient_GetResponse_BeneathToolInvocation_KeepsTheLastPromptWhileTheTurnReportsTheSum

**Scenario**: A tool-calling turn is run through a real `FunctionInvokingChatClient` over a provider
scripted to report a different prompt size for each of the two requests it takes.

**Expected**: Two requests are made, and the response the loop returns reports the two input-token
counts added together.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAI-FunctionInvokingChatClient`.

### Requirements Coverage

- **`AgentKit-OTS-MicrosoftAgentsAI-Agent`**:
  AgentKitAgentsChatClient_Create_BuildsAgentCarryingSuppliedName
- **`AgentKit-OTS-MicrosoftAgentsAI-ChatClientAgent`**:
  AgentKitAgentsChatClient_Create_BuildsAgentCarryingSuppliedName,
  AgentKitAgentsChatClient_ImagePromotion_ToolReturnedImage_ReachesProviderOnUserMessage
- **`AgentKit-OTS-MicrosoftAgentsAI-FunctionInvokingChatClient`**:
  ChatClientProviderSessionFactory_CreateAsync_SeededTool_IsInvokedAndItsResultReachesTheTranscript,
  PromptSizeRecordingChatClient_GetResponse_BeneathToolInvocation_KeepsTheLastPromptWhileTheTurnReportsTheSum
