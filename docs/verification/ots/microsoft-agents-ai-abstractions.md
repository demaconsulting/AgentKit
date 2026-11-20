## Microsoft.Agents.AI.Abstractions Verification

This document provides the verification evidence for the `Microsoft.Agents.AI.Abstractions` OTS
software item.

### Required Functionality

`Microsoft.Agents.AI.Abstractions` supplies the Microsoft Agent Framework abstractions the Copilot
package's own agent is built on: the `AIAgent` base type, whose session creation, session
serialization and deserialization, run and streaming-run extension points `CopilotAgent` overrides;
the `AgentSession` type with its serializable state bag, which `CopilotAgentSession` extends; and the
`AgentResponseUpdate` and `AgentResponse` types a run streams and returns, together with the
framework's gathering of a stream into one response.

### Verification Approach

Unlike the build-and-verify pipeline tools, this OTS item is a runtime library with no
self-validation CLI. Per `software-items.md`, an OTS library is verified through integration tests
proving the required functionality works. AgentKit's own tests build a `CopilotAgent` through the
factory's internal seam over a scripted Copilot runtime and drive it only through the `AIAgent`
surface an application uses: `CreateSessionAsync`, `RunAsync`, `RunStreamingAsync`,
`SerializeSessionAsync` and `DeserializeSessionAsync`. The framework's own dispatch from those public
methods to the extension points `CopilotAgent` overrides therefore executes, as does its gathering of
the streamed updates into one response and its serialization of a session's state bag. Nothing of the
framework is stubbed; only the Copilot runtime beneath the agent is scripted, as described in
*CopilotAgent Unit Verification Design*. These tests run in the merge gate on every platform,
offline, with no credential.

The opt-in live Copilot suite drives the same `AIAgent` surface against the real Copilot runtime:
it streams text deltas, serializes a conversation and resumes it on a new agent instance, and
delivers an image attachment. It is supplementary evidence outside the trace matrix and is described
in *AgentKitAgentsCopilot System Verification Design*.

### Test Scenarios

#### AgentKitAgentsCopilot_Agent_RunsAndStreamsOnTheRuntime

**Scenario**: An agent the factory builds runs one streaming turn in which the runtime calls a tool
and streams its answer.

**Expected**: The streamed updates carry the function call, the function result and then the answer's
text in order, through the framework's streaming-run entry point.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAIAbstractions-AIAgent`.

#### CopilotAgent_Run_ReturnsTheStreamedContentAsOneResponse

**Scenario**: A non-streaming run over a turn that calls a tool and streams its answer in two deltas.

**Expected**: The response's text is the whole answer and its messages carry the function call, so the
framework's run entry point and its gathering of a stream into one response behave as required.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAIAbstractions-AIAgent`.

#### CopilotAgent_SerializeSession_RoundTripsTheSessionIdAndStateBag

**Scenario**: A conversation that has run once carries a value in its state bag, is serialized, and is
restored on a second agent.

**Expected**: The serialized form carries the session identity and a state bag object; the restored
session carries the same identity and the same state bag value; and a run on it resumes the same
runtime session.

**Requirement coverage**: `AgentKit-OTS-MicrosoftAgentsAIAbstractions-AIAgent`.

### Requirements Coverage

- **`AgentKit-OTS-MicrosoftAgentsAIAbstractions-AIAgent`**:
  AgentKitAgentsCopilot_Agent_RunsAndStreamsOnTheRuntime,
  CopilotAgent_Run_ReturnsTheStreamedContentAsOneResponse,
  CopilotAgent_SerializeSession_RoundTripsTheSessionIdAndStateBag
