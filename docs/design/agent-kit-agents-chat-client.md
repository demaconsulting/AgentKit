# AgentKitAgentsChatClient System Design

![AgentKit ChatClient Agents Structure](AgentKitAgentsChatClientView.svg)

The AgentKitAgentsChatClient system builds a Microsoft Agent Framework agent from any
`Microsoft.Extensions.AI` `IChatClient` and a supplied tool list, installing AgentKit Core's
image-promoting decorator automatically and unconditionally, and carries AgentKit Core's compacting
session over that same `IChatClient`.

## Purpose

AgentKitAgentsChatClient exists so that an application can turn any `IChatClient` provider into a
tool-using agent with a single call, without learning the one thing that would otherwise break
images silently. A provider reached through an `IChatClient` preserves an image a tool returns all
the way through the framework and then drops it at the wire, because that interface accepts images
on messages rather than in tool responses. The model answers anyway, describing an image it never
received, and nothing in the exchange reports an error.

The remedy is Core's `ImagePromotingChatClient`, which carries a tool-returned image onto a
following user message. Core deliberately makes that decorator opt-in and supplies no agent loop to
install it into. This system is the place that installs it: it wraps the supplied client in the
decorator on **every** agent it builds and exposes no parameter to disable it. Omission is made
impossible rather than merely discouraged, because a guard a caller can forget is a guard that will
be forgotten and whose absence produces no error to notice.

The system is also where Core's session engine meets a stateless provider. Core keeps a long-running
agent alive by compacting context, and it holds no token arithmetic of its own: it asks the provider
session how full it is and out of how much, and believes the answer. This system supplies that
answer for the whole stateless family — Ollama, OpenAI and AI Foundry all arrive as an `IChatClient`
and none of them keeps the conversation — and supplies the summarizer the engine consolidates
through. One adapter serves all of them because none of them holds the conversation: seeding a
session is starting a message list, and releasing one is forgetting it.

The system is a thin adapter throughout. Beyond validation, the unconditional decorator, and the
mapping between this library's transcript entries and chat messages, its only jobs are to construct
the framework's own `ChatClientAgent` and to answer the engine's one token question. It implements no
agent runtime, no tool-calling loop, no provider abstraction and no compaction policy — those belong
to Microsoft Agent Framework and to Core, on both of which it depends.

## Dependency Isolation

This system carries `Microsoft.Agents.AI`, the Microsoft Agent Framework runtime, whose size and
monthly churn bar it from Core, which takes exactly one runtime dependency; see _AgentKitCore
System Design_. Isolating it in its own
package is exactly what that dependency justifies: Core stays small and slow-moving, and an
application that never builds an agent through this adapter never takes the dependency. The system
references Core (for `ImagePromotingChatClient` and the session contracts) and `Microsoft.Agents.AI`
(for `AIAgent` and `ChatClientAgent`) and nothing else. It shares no code with, and takes no
reference on, the Copilot adapter.

## Structure

The system is flat: four classes, no subsystem layer, mirroring how a small system is modeled
elsewhere in the repository. A subsystem layer would add artifacts without reducing the units anyone
has to review. The agent factory and the session adapter share a package rather than a code path:
both exist to make one `IChatClient` usable, and neither calls the other.

- **ChatClientAgentFactory (Unit)** — the static factory that validates its arguments, wraps the
  supplied client in the image-promoting decorator, and builds a `ChatClientAgent`.
- **ChatClientProviderSession (Unit)** — one Core session over an `IChatClient`: the seeded message
  list, the conversation resent on every turn, the transcript entries a turn produces, and the
  provider's own occupancy reported against a supplied window.
- **ChatClientProviderSessionFactory (Unit)** — holds the client and the window, and creates a
  session from a seed at the start of a conversation and again at every rotation.
- **ChatClientSummarizer (Unit)** — consolidates history through an `IChatClient` of the
  application's choosing, out of the session being compacted.

## Folder Layout

```text
src/DemaConsulting.AgentKit.Agents.ChatClient/
├── ChatClientAgentFactory.cs              — builds an agent from an IChatClient, decorator always installed
├── ChatClientProviderSession.cs           — one Core session over an IChatClient
├── ChatClientProviderSessionFactory.cs    — creates those sessions, holding the client and the window
└── ChatClientSummarizer.cs                — consolidates history through an IChatClient
```

## Dependencies

- **AgentKitCore** — supplies `ImagePromotingChatClient`, the decorator the factory installs, and the
  session contracts the adapter implements: `IProviderSession`, `IProviderSessionFactory`,
  `ISummarizer`, `ProviderSessionSeed`, `ProviderTurn`, `TranscriptEntry`, `ContextUsage` and
  `ConsolidationPrompt`; see _AgentKitCore System Design_.
- **Microsoft.Agents.AI** — supplies `AIAgent` and `ChatClientAgent`; see _Microsoft.Agents.AI
  Design_.
- **Microsoft.Extensions.AI.Abstractions** — supplies `IChatClient` and `AIFunction`; reached
  transitively through Core and directly through `Microsoft.Agents.AI`.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.
