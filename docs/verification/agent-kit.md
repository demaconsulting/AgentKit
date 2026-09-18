# AgentKit Product Capability Verification

This document describes how AgentKit's two product-level provider capabilities are verified.

## Verification Approach

A capability requirement is verified by the scenarios that already demonstrate it end to end, not by
new tests written to match its wording. Both capabilities promise an agent confined to the tools the
application granted it and a conversation that outlives the provider's context window, and the
chat-client capability promises one thing more — that what a tool returned reaches the model intact —
so each is traced to the existing system-level scenarios that show those promises holding on that
provider. No test is added for the capability itself: a capability that needed its own test would be
a capability nothing beneath it delivered.

Both capabilities are verified offline. No credential is used, no Copilot CLI is started and no
network access is made; the Copilot runtime is reached through the package's own internal turn-channel
seam and the `IChatClient` family through a recording chat client. The scope boundaries that leaves —
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

## Acceptance Criteria

A capability is satisfied when every scenario listed for it passes. A confinement scenario that fails
means the provider is supported only in the sense that it answers; a rotation scenario that fails
means the conversation is bounded by the provider's window after all; a fidelity scenario that fails
means a tool's answer reaches the model as silence. Any of these outcomes withdraws the capability
rather than degrading it.
