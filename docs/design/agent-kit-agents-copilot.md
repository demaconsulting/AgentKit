# AgentKitAgentsCopilot System Design

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The AgentKitAgentsCopilot system builds a Microsoft Agent Framework agent from a GitHub Copilot
`CopilotClient` and a supplied tool list, suppressing the built-in tools the Copilot runtime would
otherwise inject, and carries an AgentKit Core session — with its tiered context compaction — over
the same runtime.

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

**The system carries a second job: an AgentKit provider session.** Core's compaction engine is what
lets a long-running agent behave the same way on every provider, and it reaches a provider through
an adapter. Until this increment there was one adapter, for the stateless `IChatClient` family — and
the Copilot SDK exposes no `IChatClient` anywhere, so the engine could not run on Copilot at all.
That mattered more than the count suggests: Copilot is the provider this project's continuous
integration can reach, so the engine's behavior against a runnable provider had never been
exercised. `CopilotProviderSession`, its factory and `CopilotSummarizer` close that gap natively.

The system implements no agent runtime and no compaction engine of its own; it adapts the GitHub
Copilot SDK into the Microsoft Agent Framework `AIAgent` abstraction and into Core's session
contracts. It shares no code with, and takes no reference on, the ChatClient adapter.

## Seeding History Into the System Message

A rotation produces a seed carrying instructions, tools and a rewritten history, and the adapter has
to get that history into a fresh Copilot session. **Copilot's session configuration has no history
or messages field of any kind** — every property of the configuration and its base was enumerated
against the shipped assembly. So the history is rendered as a labeled record and appended to the
session's system message, after the instructions, between two fixed delimiter lines.

That channel was chosen because it is the only one the runtime offers that exists at
session-creation time, is carried verbatim, and has **no role vocabulary**. The last property is
decisive. The defect that shipped to review on the stateless path was a seeded tool result rendered
as a text-only tool-role message, a shape the OpenAI wire mapping discards without an error — and
fifty-five green tests and a live sample run never noticed. Here that whole class of defect is
unrepresentable: the record is text in the instructions channel, and no provider can drop part of it
without dropping the instructions.

A priming message, conversational attachments, session resumption, the session RPC's message queue
and the runtime's own prompt-section overrides were each considered and rejected, with the evidence
against each recorded in _CopilotProviderSessionFactory Unit Design_.

Two consequences are stated rather than hidden. The record is charged to the runtime's system-token
count, so it appears as fixed overhead and the session rotates progressively **earlier** as records
accumulate — the safe direction, and well-defined at the limit because Core's rotation threshold is
never below one token. And it reaches the model through the instructions channel rather than the
conversation, so the model may weight it differently from turns it lived through; that cannot be
verified without a live run and is recorded as unverified rather than asserted.

## The Runtime's Own Compaction Is Disabled

Copilot compacts its own sessions: its infinite-session configuration defaults to enabled, with
background compaction at 0.80 of the window and buffer exhaustion at 0.95. AgentKit rotates at a
threshold of the same order, against the same occupancy signal. This is therefore not a theoretical
conflict.

Left on, both compactors would act on one conversation. The runtime would rewrite history underneath
a session whose transcript the engine believes it owns, and the occupancy the engine reads afterwards
would move for reasons it cannot see — so the engine would seed a replacement from a history the
provider has already discarded, and the two accounts of the conversation would diverge silently.
Every session the engine drives is therefore created with the runtime's compaction switched off.
**A future maintainer must not "helpfully" turn it back on.**

**The agent path is deliberately asymmetric.** A plain Copilot agent has no AgentKit compactor behind
it, so disabling the runtime's would remove protection rather than prevent a conflict. The agent path
leaves the setting untouched, and a test pins each side of the asymmetry so neither can be tidied
into the other by accident.

**Whether the runtime honors the request is not verifiable offline.** The SDK carries the setting to
the wire unchanged, which is all this library can establish without a live connection. So the request
is not trusted on its own: the session observer watches for the runtime's own compaction and
truncation events, and the provider session refuses the next turn if one arrives, naming the cause.
That converts a silent divergence into a diagnosable failure, which is the convention this library
already follows wherever it cannot know something it needs.

## Occupancy Comes From the Runtime

Core's session engine asks one question — how full, out of how much — and believes the answer. The
stateless adapter has to be _told_ its window, because an `IChatClient` publishes none. Copilot is
the opposite: it reports the tokens it currently holds, the limit it will hold them to, and how much
of the total the conversation accounts for, all in one event and all counted with the same
tokenizer.

So this system takes **no** window from the application. Asking for a figure the provider already
knows would create a second source of truth for one fact, which is exactly what the engine's
single-reading design retired. The conversation's share is passed through rather than inferred,
which keeps every threshold comparison downstream in Copilot's own tokens. Nothing is estimated, and
a turn the runtime answered without reporting its usage is refused rather than guessed around.

Before the runtime has reported anything — which includes the moment the engine reads a freshly
adopted session — the session reports nothing occupied out of a placeholder window that cannot
trigger a rotation and cannot be mistaken for a measurement.

## Why a Turn-Channel Seam Exists

`CopilotSession` is sealed, its constructor is not public, and none of its methods is virtual;
`CopilotClient` is sealed too. Neither can be faked, subclassed or constructed in a test, and
exercising a turn otherwise needs a live runtime and credentials, which CI does not have and which
this repository deliberately does not require.

Without a seam, the whole session adapter — the seeding, the usage arithmetic, the refusals, the
ownership discipline — would ship with no automated evidence at all, on the one provider the project
can otherwise reach. `ICopilotTurnChannel` is therefore one interface with one method, and the
production implementation behind it forwards two calls and owns one disposal. Every decision worth
testing lives above that line; what lies below it is stated plainly as untested in _CopilotTurnChannel
Unit Verification Design_ rather than implied to be covered.

Every session _event_ type, by contrast, is trivially constructable, so the tests drive the SDK's own
event objects through the adapter's own matching code. The seam is narrow, and what crosses it is
real.

## No Image-Promoting Decorator

Unlike the ChatClient adapter, this system deliberately does **not** install Core's
`ImagePromotingChatClient`. The Copilot runtime already delivers an image a tool returns to the
model — its runtime converts a binary tool result into content the model sees — as verified in the
spike that preceded this work, and corroborated by the SDK's tool-completion result carrying binary
results for the model explicitly. Installing the decorator here would carry the image a second time
onto a user message, duplicating content the model already received. This absence is intentional,
applies to the session path as much as to the agent path, and is recorded here and in both units so
a future maintainer does not "helpfully" add it.

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

The system holds five units: the agent factory that ships today, and the four that carry a Core
session over the runtime.

- **CopilotAgentFactory (Unit)** — the static factory that validates its arguments, derives the
  allow-list from the supplied tools, installs a default-safe permission handler, and builds the
  agent without taking ownership of the client. It is also the **single** place a Copilot session
  configuration is built, for the agent path and for both session-engine paths.
- **CopilotProviderSession (Unit)** — an `IProviderSession` over one Copilot session: sends a turn,
  refuses a turn that reported no usage or whose history the runtime rewrote, records tool traffic
  as identified pairs, reports the runtime's occupancy, and owns and releases its session.
- **CopilotProviderSessionFactory (Unit)** — an `IProviderSessionFactory`: composes the instructions
  and the rendered record, reuses the one confinement path, disables the runtime's compaction,
  registers the observer before the session is created, and guards the ownership window.
- **CopilotSessionObserver (Unit)** — watches the runtime's event stream and holds the latest usage
  reading, the current turn's entries, and whether the runtime rewrote history.
- **CopilotSummarizer (Unit)** — an `ISummarizer` on a short-lived, tool-free Copilot session, using
  Core's consolidation prompt and releasing its session on every path.
- **CopilotTurnChannel (Unit)** — the internal seam over the sealed SDK session types, and the
  production implementation that owns one runtime session.

## Data Flow

```text
CompactingAgentSession
        │  seed (instructions, tools, history)
        ▼
CopilotProviderSessionFactory ──► CopilotAgentFactory.BuildEngineSessionConfig
        │                                   (allow-list, skills off, handler, compaction off)
        │  SessionConfig (+ observer as OnEvent)
        ▼
CopilotTurnChannel ──► CopilotSession (runtime)
        │  turn                              │  events
        ▼                                    ▼
CopilotProviderSession ◄──────────── CopilotSessionObserver
        │  ProviderTurn + ContextUsage
        ▼
CompactingAgentSession ──► CopilotSummarizer ──► a separate, tool-free CopilotSession
```

## Folder Layout

```text
src/DemaConsulting.AgentKit.Agents.Copilot/
├── CopilotAgentFactory.cs            — builds a Copilot agent, and every session configuration
├── CopilotProviderSession.cs         — an AgentKit session over one Copilot session
├── CopilotProviderSessionFactory.cs  — creates one seeded Copilot session per rotation
├── CopilotSessionObserver.cs         — watches the runtime's event stream
├── CopilotSummarizer.cs              — consolidates on a separate tool-free session
└── CopilotTurnChannel.cs             — the seam over the sealed SDK session types
```

## Dependencies

- **AgentKitCore** — supplies the family-prefix tool convention and the `AIFunction` currency the
  supplied tools are built from, and the session contracts the adapter implements —
  `IProviderSession`, `IProviderSessionFactory`, `ISummarizer`, `ProviderSessionSeed`,
  `ProviderTurn`, `TranscriptEntry`, `ContextUsage` and `ConsolidationPrompt`; see _AgentKitCore
  System Design_.
- **Microsoft.Agents.AI.GitHub.Copilot** — supplies `CopilotClient`, `CopilotSession`,
  `SessionConfig`, the session-config agent-construction path, the permission RPC, and the session
  event stream; see _Microsoft.Agents.AI.GitHub.Copilot Design_. Carries a RID-specific native
  runtime, as noted above.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.
