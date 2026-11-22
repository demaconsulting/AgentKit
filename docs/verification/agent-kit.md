# AgentKit Product Capability Verification

This document describes how AgentKit's three product-level provider capabilities, its capability
for tools in an agent runtime AgentKit did not build, and its model-selection capability are verified.

## Verification Approach

A capability requirement is verified by the scenarios that already demonstrate it end to end, not by
new tests written to match its wording. All three capabilities promise an agent confined to the tools
the application granted it and a conversation that outlives the provider's context window, and the
chat-client capability promises one thing more — that what a tool returned reaches the model intact —
so each is traced to the existing system-level scenarios that show those promises holding on that
provider. No test is added for the capability itself: a capability that needed its own test would be
a capability nothing beneath it delivered.

The Ollama capability is the one case where a scenario is deliberately borrowed. Ollama is reached
as an `IChatClient`, so the confinement and rotation half of its promise is the chat-client
capability's own scenario, cited here rather than re-demonstrated; what is specific to Ollama is
making the accounted window the one the instance is actually running, and that is what its
remaining scenarios show.

All three capabilities are verified without a provider. No credential is used, no Copilot runtime is
started and no Ollama server is contacted; the Copilot runtime is reached through the package's own
internal turn-channel seam and the `IChatClient` family through a recording chat client. The Ollama
scenarios are the closest of the three to a live provider: two of them go over HTTP through the real
Ollama client against a server the test starts on loopback and stops with it, so the client's own
request shaping and deserialization execute — one replaying payloads captured verbatim from a live
Ollama 0.34.1 server, the other reading back the body the request was sent with. The precedence
itself is a pure function and is exercised on hand-built loaded-model values. One fact about this
capability is nonetheless a manual measurement
rather than a scenario — that a real Ollama server loads a model at the `num_ctx` a request names —
and it is recorded in *OllamaContextWindow Unit Verification Design* with the figures it was taken
from. The scope boundaries this leaves —
principally that a live end-to-end run against either provider is not automated — are stated in
*AgentKitAgentsCopilot System Verification Design* and *AgentKitAgentsChatClient System Verification
Design* and are not restated here. The one exception is the Copilot capability's own promise to run
**on the GitHub Copilot runtime**, which no offline scenario can demonstrate; the live evidence for it
comes from the opt-in live Copilot suite and is summarized below.

## Capability Scenarios

### AgentKit-Provider-GitHubCopilot

- `AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools` — the session
  configuration Copilot is given admits the application's tools and nothing else.
- `AgentKitAgentsCopilot_Session_BuiltInToolRequest_IsRejectedOnTheSessionPath` — that confinement
  still holds on the session a rotation produces, which is where a long conversation actually runs.
- `AgentKitAgentsCopilot_Session_RotatesOnTheRuntimesUsage_AndSeedsTheReplacement` — a real
  compacting session crosses the runtime's reported occupancy, consolidates, and continues on a
  replacement seeded from the record. This is the conversation outliving the window.
- `AgentKitAgentsCopilot_Agent_RunsAndStreamsOnTheRuntime` — an agent the package builds runs a
  turn confined to the supplied tools, streams the tool traffic and the answer as they happen, and
  releases its runtime session. This is the guarded agent the capability promises.

**That the capability holds on the live runtime is shown by the opt-in live Copilot suite, not by the
traced scenarios.** On the real runtime, a session the package confines exposes exactly the supplied
tools; an AgentKit tool reads inside its path policy and is denied outside it; and a compacting
session with a 2,500-token window ceiling rotates on the runtime's own usage figures and still
recalls a codeword stated only before the rotation. The suite, its gating and its recorded results
are described in *AgentKitAgentsCopilot System Verification Design*. It is supplementary evidence:
it never runs in the pipeline that builds the trace matrix, so none of its tests is linked to this
capability.

### AgentKit-Provider-ChatClient

- `AgentKitAgentsChatClient_Create_BuildsAgentCarryingSuppliedName` — an agent is built from a plain
  `IChatClient` and the application's tool list, confined to it.
- `AgentKitAgentsChatClient_Create_InstallsImagePromotingDecoratorOnTheAgent` — content fidelity: the
  image-promoting decorator is on every agent built and cannot be omitted, so a tool-returned image
  reaches the model rather than being dropped at the wire.
- `AgentKitAgentsChatClient_Session_RotatesOnTheProvidersUsage_AndSeedsTheReplacement` — a real
  compacting session rotates on the provider's own reported usage and continues on a replacement
  seeded from the consolidated record.

### AgentKit-Provider-Ollama

- `AgentKitAgentsOllama_ContextWindow_ReportsTheEnforcedWindowAndNamesItsSource` — the window a
  conversation on an Ollama model is accounted against is reported across the three states a real
  server moves between, and every figure names the source it came from. This is the capability as an
  application meets it.
- `AgentKitAgentsOllama_ContextSizing_EveryRequestAsksTheServerForTheStatedWindow` — one size chosen
  by the application is reported as the window and is then read back off the body of every request
  the real Ollama client sent. This is what makes a stated window true rather than claimed, and the
  "every request" half is the part nothing downstream could otherwise detect.
- `OllamaContextWindow_ReadAsync_ModelLoaded_ReportsTheLengthTheServerEnforces` — the length a
  running instance reports, judged on verbatim output from a live server and replayed over HTTP to
  the real Ollama client, so the library's own request shaping and deserialization execute. A
  session sized on what the model file publishes instead — 262,144 against an instance measured at
  4,096 — would rotate long after the server had discarded the start of the conversation.
- `AgentKitAgentsChatClient_Session_RotatesOnTheProvidersUsage_AndSeedsTheReplacement` — borrowed
  from the chat-client capability, which is where the conversation half of this promise is
  delivered: Ollama reaches an application as an `IChatClient`, and no part of rotation is
  Ollama-specific.

### AgentKit-Tools-AnyAgentRuntime

- `AgentKitCore_SystemForeignTool_GuardedOversizedResult_IsRefused` — a tool built with the plain
  function factory, unbounded on its own, is refused when bounded by AgentKit's ceilings.
- `AgentKitCore_SystemToolOutput_RestoredRetention_AnswersAnEarlierReference` — a retention handle
  exported to JSON and restored answers a reference issued before.
- `AgentKitAgentsChatClient_ContextProvider_ToolsReachTheProviderAndRun` — a host-built
  `ChatClientAgent` with no tools of its own is offered AgentKit's tools through the context provider
  and runs one.
- `AgentKitAgentsChatClient_ContextProvider_DeserializedSession_PagesAnEarlierOutput` — a
  conversation's retained output survives the agent serializing and deserializing its session.
- `AgentKitAgentsChatClient_ImagePromotion_BuilderExtension_PromotesBeneathFunctionInvocation` — a
  host-built agent over `UseImagePromotion()` delivers a tool-returned image on a user message.
- `AgentKitAgentsChatClient_ChatReducer_InMemoryHistoryProvider_ReducesStoredHistory` — AgentKit's
  reducer consolidates the stored history of a host-built agent through the framework's hook.

That these building blocks hold against a live model is shown by the opt-in live Ollama suite
described in *AgentKitAgentsChatClient System Verification Design*; like the live Copilot suite, it is
supplementary and none of its tests is linked here.

### AgentKit-ModelSelection

- `AgentKitCore_SystemModelSelection_VisionOnlyApp_SelectsAndGatesTheImageFamily` — a vision-only
  application lists only the models that can see, selects one with its settings, and offers the image
  family because the selected model can see. This is the provider-neutral half of the capability.
- `AgentKitAgentsChatClient_ModelSelection_EveryRequestOfATurnCarriesTheSelection` — on any
  `IChatClient` provider, every request of a tool-calling turn carries the chosen model and effort.
- `AgentKitAgentsOllama_ModelSelection_ChosenModelRunsWithItsWindowAndEffort` — an Ollama model chosen
  from the catalog runs at the chosen window and effort, read back off the wire through the real
  Ollama client.
- `AgentKitAgentsCopilot_ModelSelection_AgentRunsOnTheSelectedModelAndSettings` — a Copilot model
  chosen from the catalog backs the agent's session with the chosen effort and tier.

The live listings, the limits the Copilot runtime reports per tier, and the `think` and `num_ctx`
values a real Ollama server receives are recorded by the opt-in live suites, described in
*CopilotModelCatalog Unit Verification Design* and *OllamaModelCatalog Unit Verification Design*.
They are supplementary, and none of their tests is linked here.

## Acceptance Criteria

A capability is satisfied when every scenario listed for it passes. A confinement scenario that fails
means the provider is supported only in the sense that it answers; a rotation scenario that fails
means the conversation is bounded by the provider's window after all; a fidelity scenario that fails
means a tool's answer reaches the model as silence; a window-discovery scenario that fails means the
window a conversation is accounted against on Ollama is a guess rather than the window the instance
is running. A model-selection scenario that fails means a stale or incapable model choice could start
a session. Any
of these outcomes withdraws the capability rather than degrading it.
