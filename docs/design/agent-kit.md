# AgentKit Product Capability Design

AgentKit's product-level requirements state what the toolkit delivers to an application author,
above any one package. Two of them are provider capabilities: AgentKit supports the GitHub Copilot
runtime, and AgentKit supports any `Microsoft.Extensions.AI` `IChatClient` provider. Both promise an
agent confined to the tools the application granted it and a conversation that outlives the
provider's context window; the chat-client capability promises one thing more, that what a tool
returned reaches the model intact. Each promise is demonstrated end to end rather than argued from
its parts.

## Why the Capabilities Sit Above the Packages

A provider adapter's requirements are mechanism: where an allow-list is derived from, which channel
a rotation's record travels on, how occupancy is read. Each is worth having only because of a
capability it delivers, and none of them, stated alone, says AgentKit supports the provider at all.
Stating the capability above them makes that claim once, gives it its own evidence, and turns the
adapter requirements into the decomposition of something rather than a list of independent facts.

The capabilities belong to no software item. There is no product-level class, assembly or package:
a capability is delivered by a system, and the system is where its design lives. This chapter adds
no structure — it records which system delivers which capability and what each one rests on.

## How Each Capability Is Delivered

- **`AgentKit-Provider-GitHubCopilot`** — delivered by AgentKitAgentsCopilot over AgentKitCore's
  session engine, resting on the `Microsoft.Agents.AI.GitHub.Copilot` SDK.
- **`AgentKit-Provider-ChatClient`** — delivered by AgentKitAgentsChatClient over the same session
  engine, resting on `Microsoft.Agents.AI`.

The two promises both capabilities share come from two places. Confinement is the adapter's: on
Copilot it is the
derived allow-list and the default-safe permission handler that withhold the runtime's own tools; on
an `IChatClient` there are no built-in tools to withhold, so confinement is simply the tool list the
application supplied. Outliving the window is Core's compaction engine, reached through the
`ProviderSession` contract each adapter implements; the adapter supplies the seeding, the occupancy
reading and the summarizer that engine needs on that provider. See *AgentKitAgentsCopilot System
Design*, *AgentKitAgentsChatClient System Design* and *AgentKitCore System Design*.

The chat-client capability carries a third promise, content fidelity, which is neither of those: a
provider reached through an `IChatClient` drops a tool-returned image at the wire, so the adapter
installs the image-promoting decorator beneath the function-invocation loop on every agent and every
session it builds, with no option to omit it. Copilot needs no such promise — its runtime delivers a
tool's binary results to the model itself — which is why the two capabilities are not symmetric.

## What the OTS Requirements Are Doing Here

Each capability names among its children the off-the-shelf requirements it depends on — the Copilot
SDK's session configuration, agent construction, permission RPC, session lifecycle and event stream
for the first; the Agent Framework's `AIAgent` and `ChatClientAgent` for the second. Those links
carry the OTS traceability that two deleted "built on" requirements used to hold. Which SDK a
package is built on is a technology fact rather than a promise an application can act on, so the
fact is gone and the traceability it existed for now hangs from the capability that needs it. See
*OTS Integration Design*.

## Which Providers Get a Capability

A provider gets a capability of its own when a shipped package carries code for it, because that
code is also what makes the capability demonstrable. Ollama, OpenAI and AI Foundry are reached
through `AgentKit-Provider-ChatClient` and are not claimed individually: no shipped package holds
anything specific to any of them. Where such a piece does exist — the Ollama context-window
discovery in the research-assistant sample — it lives in a sample, which is not a software item and
carries no requirement.
