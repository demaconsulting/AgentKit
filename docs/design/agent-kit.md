# AgentKit Product Capability Design

AgentKit's product-level requirements state what the toolkit delivers to an application author,
above any one package. Three of them are provider capabilities: AgentKit supports the GitHub Copilot
runtime, AgentKit supports any `Microsoft.Extensions.AI` `IChatClient` provider, and AgentKit
supports a model served by Ollama. All three promise an
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
- **`AgentKit-Provider-Ollama`** — delivered by AgentKitAgentsChatClient too, because Ollama reaches
  an application as an `IChatClient`, with AgentKitAgentsOllama supplying the one thing that
  delivery cannot obtain for itself: the window the Ollama server will actually enforce, resting on
  `OllamaSharp` to fetch it.

The two promises every capability shares come from two places. Confinement is the adapter's: on
Copilot it is the
derived allow-list and the default-safe permission handler that withhold the runtime's own tools; on
an `IChatClient` there are no built-in tools to withhold, so confinement is simply the tool list the
application supplied. Outliving the window is Core's compaction engine, reached through the
`ProviderSession` contract each adapter implements; the adapter supplies the seeding, the occupancy
reading and the summarizer that engine needs on that provider. See *AgentKitAgentsCopilot System
Design*, *AgentKitAgentsChatClient System Design* and *AgentKitCore System Design*.

The Ollama capability shares both of those with the chat-client one rather than restating them, and
adds only what the shared delivery is missing. An `IChatClient` publishes no window, so a compacting
session must be told one, and on Ollama the figure that matters is not the maximum the model
advertises — the server loads the model with a length of its own choosing, smaller by default. See
*AgentKitAgentsOllama System Design*.

The chat-client capability carries a third promise, content fidelity, which is neither of those: a
provider reached through an `IChatClient` drops a tool-returned image at the wire, so the adapter
installs the image-promoting decorator beneath the function-invocation loop on every agent and every
session it builds, with no option to omit it. Copilot needs no such promise — its runtime delivers a
tool's binary results to the model itself — which is why the capabilities are not symmetric.

## What the OTS Requirements Are Doing Here

Each capability names among its children the off-the-shelf requirements it depends on — the Copilot
SDK's session configuration, agent construction, permission RPC, session lifecycle and event stream
for the first; the Agent Framework's `AIAgent` and `ChatClientAgent` for the second; OllamaSharp's
fetching of a server's loaded-model and model-metadata reports for the third. Those links
carry the OTS traceability that two deleted "built on" requirements used to hold. Which SDK a
package is built on is a technology fact rather than a promise an application can act on, so the
fact is gone and the traceability it existed for now hangs from the capability that needs it. See
*OTS Integration Design*.

## Which Providers Get a Capability

A provider gets a capability of its own when a shipped package carries code for it, because that
code is also what makes the capability demonstrable. OpenAI and AI Foundry are reached through
`AgentKit-Provider-ChatClient` and are not claimed individually, because no shipped package holds
anything specific to either: nothing would be promised that the chat-client capability does not
already promise, and nothing would be demonstrated that its scenarios do not already demonstrate.

Ollama meets the rule. `AgentKitAgentsOllama` is a shipped package carrying Ollama-specific
code — the context window a server will enforce, which the chat-client adapter cannot read because
an `IChatClient` publishes none — and that code is what makes the capability demonstrable. The
evidence a capability requires is the whole promise shown, and the half that belongs to Ollama is
the reading of what a server actually sent: the package's suite replays payloads captured verbatim
from a live Ollama 0.34.1 server, both through the precedence directly and over HTTP through the
real Ollama client. The other half, an agent confined to its tools carrying a conversation past the
window, is the chat-client capability's and is cited from there rather than re-demonstrated, because
on Ollama it is the same delivery.

`AgentKit-Provider-Ollama` is worded to say only that. It claims a guarded agent on a model **served
by** Ollama, not an Ollama implementation, and its justification names the chat-client capability as
where the agent and the session come from. A title that claimed more would be claiming a second
session engine this repository deliberately does not ship.
