## ToolPackContextProvider Unit Verification Design

This document describes the unit-level verification strategy for the `ToolPackContextProvider` class
and its options type.

### Verification Approach

`ToolPackContextProvider` is verified through unit tests that run a real `ChatClientAgent` whose only
tools come from the provider, over the scripted `RecordingChatClient` this package's tests share. What
the provider promises is only observable through an agent: that the agent offers its tools on each
request, that the agent's own function-invocation loop invokes them, and that retention travels with a
session the agent serializes and deserializes through its real state bag. The recording client is the
documented test double; the agent, the session, the state bag, `ToolPackBuilder` and
`ToolOutputRetention` are real.

Tools come from a small pack written in the test file — a 300-line report tool and an echo tool — so
the scenarios depend on this unit, Core and the Agent Framework only. Results are read from the
function results the agent sent back to the recording client, which is what a provider would receive.

The refusal for an agent that is not a chat-client agent is exercised by calling the provider's
`InvokingAsync` directly with a minimal stub agent. The framework marks the invocation context's
constructor as evaluation-only, because in production only an agent run constructs it; the test project
acknowledges that diagnostic in its project file for this one use.

Unit tests reside in `ToolPackContextProviderTests.cs`, with the fake in `RecordingChatClient.cs`, both
within the `DemaConsulting.AgentKit.Agents.ChatClient.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK, using asynchronous test methods
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider is contacted and no network access is used
- **File system**: None beyond naming the temporary directory as the policy's working directory; no
  file is read or written
- **Isolation**: Each test constructs its own client, provider, agent and sessions

### Acceptance Criteria

A unit test run passes when all ten scenarios below pass. Any tool or instruction not contributed, any
recomposition within a conversation, any reference readable across conversations, any retained output
lost or re-issued across a reload, any retained text persisted after opting out, any foreign tool left
unbounded, any non-chat-client agent accepted, any undeclared state key, or any invalid configuration
accepted constitutes a failure.

### Test Scenarios

#### AgentKitAgentsChatClient-ToolPackContextProvider-ContributesToolsAndInstructions: Tools and Instructions Reach the Client

**Test**: `ToolPackContextProvider_Run_ToolsAndInstructionsReachTheChatClient`

A tool-less agent with the provider runs once. Asserts the request offered exactly `report_build` and
`report_echo` and its instructions contain the provider's.

**Test**: `ToolPackContextProvider_WithoutToolOutput_NoReaderTools`

Without retention configured, asserts no `tool_output_*` tool is offered.

#### AgentKitAgentsChatClient-ToolPackContextProvider-ComposesOncePerSession: One Composition per Conversation

**Test**: `ToolPackContextProvider_TwoRunsOneSession_ComposeOnce`

A counting composition; two runs of one session and one run of another compose twice.

#### AgentKitAgentsChatClient-ToolPackContextProvider-RetentionPerSession: Conversations Are Isolated

**Test**: `ToolPackContextProvider_TwoSessions_HaveIsolatedRetention`

The first session retains `output-1`; reading it from the second is answered "never retained".

#### AgentKitAgentsChatClient-ToolPackContextProvider-PersistsRetention: A Reloaded Conversation Pages an Earlier Output

**Test**: `ToolPackContextProvider_SerializeDeserializeSession_RetainedOutputPagesAfterRestore`

A session retains the report, is serialized to JSON text — which contains the state key — parsed and
deserialized; reading line 300 of `output-1` returns exactly `300| report row 300`.

#### AgentKitAgentsChatClient-ToolPackContextProvider-PersistsCounterWhenContentsOptedOut: Only the Counter Is Persisted

**Test**: `ToolPackContextProvider_PersistDisabled_EarlierReferenceAnsweredNoLongerRetained`

With persistence of outputs disabled, the serialized session holds `"lastIssued":1,"outputs":[]` and
the reloaded conversation's read of `output-1` is answered "no longer retained".

#### AgentKitAgentsChatClient-ToolPackContextProvider-GuardsForeignTools: A Foreign Tool Is Bounded

**Test**: `ToolPackContextProvider_DelegateConstructor_ForeignToolIsGuarded`

A plain `AIFunctionFactory` tool returning far more than a 1,500-character ceiling; the result sent to
the provider is a `ResourceTooLarge` refusal.

#### AgentKitAgentsChatClient-ToolPackContextProvider-RefusesOtherAgents: Another Agent Is Refused

**Test**: `ToolPackContextProvider_NonChatClientAgent_ThrowsInvalidOperationException`

A stub agent that is not a chat-client agent; `InvokingAsync` throws `InvalidOperationException`
naming `ChatClientAgent`.

#### AgentKitAgentsChatClient-ToolPackContextProvider-DeclaresStateKey: The State Key Is Declared

**Test**: `ToolPackContextProvider_StateKeys_ContainsConfiguredKey`

A custom key is the provider's only declared state key.

#### AgentKitAgentsChatClient-ToolPackContextProvider-RejectsInvalidConfiguration: Invalid Configuration Is Refused

**Test**: `ToolPackContextProvider_Constructor_InvalidArguments_Throw`

Null policy, packs, function list and limits; an empty pack list; a pack requiring a capability the
host lacks (so nothing is composed); a blank state key; and retention over a 500-character ceiling are
each refused at construction.
