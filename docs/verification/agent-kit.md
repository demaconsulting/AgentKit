# AgentKit Product Capability Verification

This document describes how AgentKit's three product-level provider capabilities are verified.

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

All three capabilities are verified without a provider. No credential is used, no Copilot CLI is
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
is recorded below in the same form as the other manual measurements this project relies on.

## Capability Scenarios

### AgentKit-Provider-GitHubCopilot

- `AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools` — the session
  configuration Copilot is given admits the application's tools and nothing else.
- `AgentKitAgentsCopilot_Session_BuiltInToolRequest_IsRejectedOnTheSessionPath` — that confinement
  still holds on the session a rotation produces, which is where a long conversation actually runs.
- `AgentKitAgentsCopilot_Session_RotatesOnTheRuntimesUsage_AndSeedsTheReplacement` — a real
  compacting session crosses the runtime's reported occupancy, consolidates, and continues on a
  replacement seeded from the record. This is the conversation outliving the window.

**That the capability holds on the live runtime is verified by manual measurement, not by the
suite.** Run against the live runtime with `gpt-5.4-mini` and an 11,000-token window ceiling. A
four-turn conversation rotated three times, escalating compaction Low → Medium → High under sustained
pressure. A fact stated only in conversation and never written to disk — a tracking reference — was
recalled correctly after a rotation with no tool calls, so the answer could only have come from the
seeded record. This measurement is not automated and is not re-run by the suite; it is recorded here
because the offline scenarios above reach the adapter, not the runtime the capability names.

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

## Acceptance Criteria

A capability is satisfied when every scenario listed for it passes. A confinement scenario that fails
means the provider is supported only in the sense that it answers; a rotation scenario that fails
means the conversation is bounded by the provider's window after all; a fidelity scenario that fails
means a tool's answer reaches the model as silence; a window-discovery scenario that fails means the
window a conversation is accounted against on Ollama is a guess rather than the window the instance
is running. Any
of these outcomes withdraws the capability rather than degrading it.
