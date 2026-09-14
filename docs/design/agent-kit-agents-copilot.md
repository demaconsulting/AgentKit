# AgentKitAgentsCopilot System Design

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The AgentKitAgentsCopilot system builds a Microsoft Agent Framework agent from a GitHub Copilot
`CopilotClient` and a supplied tool list, suppressing the built-in tools the Copilot runtime would
otherwise inject.

## Purpose

AgentKitAgentsCopilot exists because Copilot arrives as a complete agent runtime carrying its own
tools — shell, fetch, file reading and writing, and more. Handing it an application's own tools is
not enough: unless the built-ins are suppressed, a document assistant silently retains command
execution. The essential job of this system is that suppression, and it performs it by deriving the
session's available-tools allow-list from the **same** collection it publishes as the session's
tools, so the published tool set and the allow-list cannot drift apart. That derivation is the
safety-critical behavior of the whole increment.

The system also wires a permission handler that is safe by default — approving exactly the supplied
tools and rejecting every other request — while letting a host supply its own. And it fixes the
ownership contract: the host constructs, starts, and disposes the `CopilotClient`; the adapter takes
no ownership of it.

The system is a thin adapter. It implements no agent runtime; it adapts the GitHub Copilot SDK into
the Microsoft Agent Framework `AIAgent` abstraction. It shares no code with, and takes no reference
on, the ChatClient adapter.

## No Image-Promoting Decorator

Unlike the ChatClient adapter, this system deliberately does **not** install Core's
`ImagePromotingChatClient`. The Copilot runtime already delivers an image a tool returns to the
model — its runtime converts a binary tool result into content the model sees — as verified in the
spike that preceded this work. Installing the decorator here would carry the image a second time
onto a user message, duplicating content the model already received. This absence is intentional and
recorded here so a future maintainer does not "helpfully" add it.

## Deployment Consequence: RID-Specific Native Runtime

`Microsoft.Agents.AI.GitHub.Copilot` carries a **RID-specific native runtime** through its SDK
dependency: the package brings a native component that is selected per runtime identifier rather
than a single managed assembly that runs anywhere. Consumers should be aware of the deployment
consequence — a self-contained or RID-targeted publish resolves and ships the native component for
the target runtime identifier, and a portable publish defers that resolution to the target machine.
This is a property of the off-the-shelf SDK, not of AgentKit's code, and it is the reason this
dependency is isolated in its own package rather than admitted anywhere broader. It is recorded here
and in the _Microsoft.Agents.AI.GitHub.Copilot Design_.

## Dependency Isolation

This system carries `Microsoft.Agents.AI.GitHub.Copilot` (and, transitively, the Microsoft Agent
Framework runtime and the native Copilot SDK). Core admits none of that weight: it takes exactly
one runtime dependency, as recorded in _AgentKitCore System Design_.
Isolating it in its own package is exactly what the dependency justifies: an application that never
builds a Copilot agent never takes the dependency or its native runtime. The system references Core
and `Microsoft.Agents.AI.GitHub.Copilot` and nothing else.

## Structure

The system is flat: it is one class, `CopilotAgentFactory`.

- **CopilotAgentFactory (Unit)** — the static factory that validates its arguments, derives the
  allow-list from the supplied tools, installs a default-safe permission handler, and builds the
  agent without taking ownership of the client.

## Folder Layout

```text
src/DemaConsulting.AgentKit.Agents.Copilot/
└── CopilotAgentFactory.cs   — builds a Copilot agent with the built-in tools suppressed
```

## Dependencies

- **AgentKitCore** — supplies the family-prefix tool convention and the `AIFunction` currency the
  supplied tools are built from; see _AgentKitCore System Design_.
- **Microsoft.Agents.AI.GitHub.Copilot** — supplies `CopilotClient`, `SessionConfig`, the
  session-config agent-construction path, and the permission RPC; see
  _Microsoft.Agents.AI.GitHub.Copilot Design_. Carries a RID-specific native runtime, as noted
  above.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.
