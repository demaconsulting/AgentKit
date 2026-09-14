# AgentKitAgentsChatClient System Design

![AgentKit ChatClient Agents Structure](AgentKitAgentsChatClientView.svg)

The AgentKitAgentsChatClient system builds a Microsoft Agent Framework agent from any
`Microsoft.Extensions.AI` `IChatClient` and a supplied tool list, installing AgentKit Core's
image-promoting decorator automatically and unconditionally.

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

The system is a thin adapter. Beyond validation and the unconditional decorator, its only job is to
construct the framework's own `ChatClientAgent`. It implements no agent runtime, no tool-calling
loop, and no provider abstraction — those belong to Microsoft Agent Framework, on which it depends.

## Dependency Isolation

This system carries `Microsoft.Agents.AI`, the Microsoft Agent Framework runtime, whose size and
monthly churn bar it from Core, which takes exactly one runtime dependency; see _AgentKitCore
System Design_. Isolating it in its own
package is exactly what that dependency justifies: Core stays small and slow-moving, and an
application that never builds an agent through this adapter never takes the dependency. The system
references Core (for `ImagePromotingChatClient`) and `Microsoft.Agents.AI` (for `AIAgent` and
`ChatClientAgent`) and nothing else. It shares no code with, and takes no reference on, the Copilot
adapter.

## Structure

The system is flat: it is one class, `ChatClientAgentFactory`, mirroring how a single-class system
is modeled elsewhere in the repository. A subsystem layer would add artifacts without reducing the
one unit anyone has to review.

- **ChatClientAgentFactory (Unit)** — the static factory that validates its arguments, wraps the
  supplied client in the image-promoting decorator, and builds a `ChatClientAgent`.

## Folder Layout

```text
src/DemaConsulting.AgentKit.Agents.ChatClient/
└── ChatClientAgentFactory.cs   — builds an agent from an IChatClient, decorator always installed
```

## Dependencies

- **AgentKitCore** — supplies `ImagePromotingChatClient`, the decorator the factory installs; see
  _AgentKitCore System Design_.
- **Microsoft.Agents.AI** — supplies `AIAgent` and `ChatClientAgent`; see _Microsoft.Agents.AI
  Design_.
- **Microsoft.Extensions.AI.Abstractions** — supplies `IChatClient` and `AIFunction`; reached
  transitively through Core and directly through `Microsoft.Agents.AI`.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.
