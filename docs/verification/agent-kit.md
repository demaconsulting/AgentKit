# AgentKit Product Capability Verification

This document describes how AgentKit's two product-level provider capabilities are verified.

## Verification Approach

A capability requirement is verified by the scenarios that already demonstrate it end to end, not by
new tests written to match its wording. Each capability promises two things on one provider family —
an agent confined to the tools the application granted it, and a conversation that outlives the
provider's context window — so each is traced to the existing system-level scenarios that show
confinement holding and a rotation completing on that provider. No test is added for the capability
itself: a capability that needed its own test would be a capability nothing beneath it delivered.

Both capabilities are verified offline. No credential is used, no Copilot CLI is started and no
network access is made; the Copilot runtime is reached through the package's own internal turn-channel
seam and the `IChatClient` family through a recording chat client. The scope boundaries that leaves —
principally that a live end-to-end run against either provider is not automated — are stated in
*AgentKitAgentsCopilot System Verification Design* and *AgentKitAgentsChatClient System Verification
Design* and are not restated here.

## Capability Scenarios

### AgentKit-Provider-GitHubCopilot

- `AgentKitAgentsCopilot_BuildSessionConfig_AvailableToolsDerivedFromSuppliedTools` — the agent
  Copilot is given admits the application's tools and nothing else.
- `AgentKitAgentsCopilot_Session_BuiltInToolRequest_IsRejectedOnTheSessionPath` — that confinement
  still holds on the session a rotation produces, which is where a long conversation actually runs.
- `AgentKitAgentsCopilot_Session_RotatesOnTheRuntimesUsage_AndSeedsTheReplacement` — a real
  compacting session crosses the runtime's reported occupancy, consolidates, and continues on a
  replacement seeded from the record. This is the conversation outliving the window.

### AgentKit-Provider-ChatClient

- `AgentKitAgentsChatClient_Create_BuildsAgentCarryingSuppliedName` — an agent is built from a plain
  `IChatClient` and the application's tool list.
- `AgentKitAgentsChatClient_Create_InstallsImagePromotingDecoratorOnTheAgent` — the one guard this
  provider family needs is on every agent built, and cannot be omitted.
- `AgentKitAgentsChatClient_Session_RotatesOnTheProvidersUsage_AndSeedsTheReplacement` — a real
  compacting session rotates on the provider's own reported usage and continues on a replacement
  seeded from the consolidated record.

## Acceptance Criteria

A capability is satisfied when every scenario listed for it passes. A confinement scenario that fails
means the provider is supported only in the sense that it answers; a rotation scenario that fails
means the conversation is bounded by the provider's window after all. Either outcome withdraws the
capability rather than degrading it.
