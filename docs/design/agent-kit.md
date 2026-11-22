# AgentKit Product Capability Design

AgentKit's product-level requirements state what the toolkit delivers to an application author,
above any one package. Three of them are provider capabilities: AgentKit supports the GitHub Copilot
runtime, AgentKit supports any `Microsoft.Extensions.AI` `IChatClient` provider, and AgentKit
supports a model served by Ollama. All three promise an
agent confined to the tools the application granted it and a conversation that outlives the
provider's context window; the chat-client capability promises one thing more, that what a tool
returned reaches the model intact. Each promise is demonstrated end to end rather than argued from
its parts. A fourth, `AgentKit-Tools-AnyAgentRuntime`, is not a provider capability: it promises that
AgentKit's tools and the controls around them work in an agent runtime the application built itself.
A fifth, `AgentKit-ModelSelection`, is not a provider capability either: it promises that an
application can list a provider's models, filter them by capability, and start a session only on
settings validated against the selected model, on all three providers.

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
  session engine, resting on the GitHub Copilot SDK, `Microsoft.Copilot.SDK`, and on the Agent
  Framework's `Microsoft.Agents.AI.Abstractions` for the agent AgentKit carries over Copilot sessions.
- **`AgentKit-Provider-ChatClient`** — delivered by AgentKitAgentsChatClient over the same session
  engine, resting on `Microsoft.Agents.AI`.
- **`AgentKit-Provider-Ollama`** — delivered by AgentKitAgentsChatClient too, because Ollama reaches
  an application as an `IChatClient`, with AgentKitAgentsOllama supplying the one thing that
  delivery cannot obtain for itself: a context window the Ollama instance is actually running,
  asked for and read back through `OllamaSharp`.

The two promises every capability shares come from two places. Confinement is the adapter's: on
Copilot it is the
derived allow-list and the default-deny permission handler that withhold the runtime's own tools; on
an `IChatClient` there are no built-in tools to withhold, so confinement is simply the tool list the
application supplied. Outliving the window is Core's compaction engine, reached through the
`ProviderSession` contract each adapter implements; the adapter supplies the seeding, the occupancy
reading and the summarizer that engine needs on that provider. See *AgentKitAgentsCopilot System
Design*, *AgentKitAgentsChatClient System Design* and *AgentKitCore System Design*.

The Ollama capability shares both of those with the chat-client one rather than restating them, and
adds only what the shared delivery is missing. An `IChatClient` publishes no window, so a compacting
session must be told one, and on Ollama the figure that matters describes the running instance
rather than the model file — the server decides at load time what the instance will use, and that
decision can be asked for as well as read. See *AgentKitAgentsOllama System Design*.

The chat-client capability carries a third promise, content fidelity, which is neither of those: a
provider reached through an `IChatClient` drops a tool-returned image at the wire, so the adapter
installs the image-promoting decorator beneath the function-invocation loop on every agent and every
session it builds, with no option to omit it. Copilot needs no such promise — its runtime delivers a
tool's binary results to the model itself — which is why the capabilities are not symmetric.

## Tools in an Agent Runtime AgentKit Did Not Build

`AgentKit-Tools-AnyAgentRuntime` is delivered by building blocks rather than by one system, because the
runtime is the application's: AgentKitCore supplies `GuardedToolFactory.AsGuarded` to bound a tool it
did not build, `ToolOutputRetention` as a per-conversation retention handle whose outputs can be
exported and restored, and `AgentKitChatReducer` to consolidate a host-owned history through the
summarizer; AgentKitAgentsChatClient supplies `UseImagePromotion()` for a host-built pipeline and
`ToolPackContextProvider`, which contributes AgentKit tools to every run of a host-built
`ChatClientAgent`, composed once per conversation, with retention kept in the session's state. See
*AgentKitCore System Design* and *AgentKitAgentsChatClient System Design*.

The building blocks are deliberately reduced in scope compared with AgentKit's own agents and
`CompactingAgentSession`: the reducer has one record, one level and no rotation, and the context
provider is honored only by `ChatClientAgent`. On the GitHub Copilot path, whose runtime owns the
history and fixes a session's tools at creation, `CopilotAgentFactory` and `CompactingAgentSession`
remain the recommended route; the user guide describes both.

## Model Selection on Every Provider

`AgentKit-ModelSelection` is delivered jointly, because the provider-neutral half and the
provider-specific half live in different packages:

- **AgentKitCore** describes a model, filters a catalog by capability, caches the listing, validates
  persisted settings at session start, and applies a selection to every `IChatClient` request.
- **AgentKitAgentsChatClient** applies a selection on its agent factory and provider-session factory,
  and offers `UseModelSettings` for a pipeline the host builds.
- **AgentKitAgentsOllama** reads the server's models as a catalog, and takes the window the session
  accounts against from the selection.
- **AgentKitAgentsCopilot** reads the service's models as a catalog, and sets the chosen effort and
  tier on every session it configures.

The offline scenarios linked to the requirement carry the evidence. They run on captured or
hand-built provider descriptions. The opt-in live suites record the live listings, the limits the
Copilot runtime reports per tier, and the `think` and `num_ctx` values an Ollama server receives;
they are supplementary. See *AgentKitCore System Design*, *AgentKitAgentsOllama System Design* and
*AgentKitAgentsCopilot System Design*.

## What the OTS Requirements Are Doing Here

Each capability names among its children the off-the-shelf requirements it depends on — the Copilot
SDK's session options, permission RPC, session lifecycle and event stream, and the Agent Framework's
`AIAgent` base type, for the first; the Agent Framework's `AIAgent` and `ChatClientAgent` for the
second; OllamaSharp's
fetching of a server's loaded-model report, and its carriage of a context-length option onto the
wire, for the third. Those links
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
code — the context window an instance is running, which the chat-client adapter cannot read because
an `IChatClient` publishes none — and that code is what makes the capability demonstrable. The
evidence a capability requires is the whole promise shown, and the half that belongs to Ollama has
two parts: reading what a server actually sent, and asking a server for a size and seeing the
request carry it. The package's suite exercises the precedence on hand-built loaded-model values,
replays payloads captured verbatim from a live Ollama server over HTTP through the real Ollama
client for the reading of one, and reads the requested size back off the body an ordinary chat
request was sent with. The other half, an agent confined to its tools carrying a conversation past
the window, is the chat-client capability's and is cited from there rather than re-demonstrated,
because on Ollama it is the same delivery.

`AgentKit-Provider-Ollama` is worded to say only that. It claims a guarded agent on a model **served
by** Ollama, not an Ollama implementation, and its justification names the chat-client capability as
where the agent and the session come from. A title that claimed more would be claiming a second
session engine this repository deliberately does not ship.
