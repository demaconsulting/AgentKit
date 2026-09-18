# AgentKitAgentsOllama System Design

![AgentKit Ollama Agents Structure](AgentKitAgentsOllamaView.svg)

The AgentKitAgentsOllama system reports the context window a conversation on an Ollama model should
be accounted against, and which of the figures a server can report that came from.

## Purpose

Ollama is reached through AgentKitAgentsChatClient, because an Ollama client is an `IChatClient`.
There is one thing about Ollama that the generic adapter cannot supply: an `IChatClient` publishes
no context window, so `ChatClientProviderSessionFactory` must be told one — and the application has
to obtain it from somewhere.

Getting it wrong is costly in a specific direction. Too low, and the session rotates earlier than it
needed to, which costs summarizer calls. Too high, and it rotates *after* the server has already
discarded the start of the conversation: history is lost, nothing reports an error, and no later
turn can tell. That asymmetry is the reason this system exists rather than a constant.

Ollama will answer, but only through Ollama's own APIs, which is what makes this a package rather
than a helper inside the chat-client adapter. That adapter serves every `IChatClient` provider and
must stay free of any one of them; see *AgentKitAgentsChatClient System Design*.

The system deliberately stops at discovery. It does not wrap
`ChatClientProviderSessionFactory` in a convenience factory: once the window is known, configuring a
session with it is one line of ordinary chat-client use, and a wrapper would add a type without
adding a capability.

## Dependency Isolation

This system carries `OllamaSharp`, and carrying it is its whole justification as a separate package.
AgentKitAgentsChatClient must not acquire a provider-specific dependency — an application reaching
OpenAI or AI Foundry through that adapter would take an Ollama client library it never calls — so
the Ollama-specific code lives here instead. The system references `OllamaSharp` and nothing else:
it takes no reference on AgentKitCore, because the window it reports is an ordinary integer and none
of Core's contracts appear in its surface, and it takes no reference on either provider adapter.

## Architecture

The system is one unit. It reports one fact, from one source, and a subsystem layer would cost a
requirements file, a design document, a verification document and a review-set without removing
anything anyone has to review.

- **OllamaContextWindow (Unit)** — the discovered window and its source, the conservative fallback,
  the reading of the server, and the pure precedence function that chooses among what it reported.

The split inside that unit is the design decision worth naming: reading the server and choosing
among what it said are separate operations, so the whole precedence — which is the part that can be
subtly and silently wrong — is exercisable without a server.

## External Interfaces

- **`OllamaContextWindow.ReadAsync`** — inbound, called by the host. Takes an Ollama client, a model
  name and an optionally stated window. Performs network I/O; tolerates either server query failing;
  propagates cancellation.
- **`OllamaContextWindow.Select`** — inbound, called by the host or by `ReadAsync`. Takes what the
  server reported. Pure; contacts nothing.
- **`OllamaContextWindow`** — outbound. A token count, always positive, and the source it came from.
- **Ollama HTTP API** — outbound, through `OllamaSharp`. The running-models and model-metadata
  queries. Both are optional; neither is required to succeed.

The window is delivered as a plain integer on the returned record, so a host hands it to
`ChatClientProviderSessionFactory` directly. No type of this system's appears in the session the
application goes on to build.

## Dependencies

- **OllamaSharp** — supplies the Ollama client interface, the running-models report and the model
  metadata response carrying the architecture and its published context length; see *OllamaSharp
  Design*.

The system depends on nothing else — not AgentKitCore, not either provider adapter.

## Risk Control Measures

The risk this system is a control for is a session told a window larger than the server enforces,
which loses conversation history with no error raised anywhere. Three measures bound it:

- **The enforced length outranks the published maximum.** The figure the server will act on is
  preferred over the one the model advertises, so the common case — a model loaded well below its
  maximum — does not produce an over-large window.
- **The fallback is conservative and is named as assumed.** When nothing can be read the reported
  figure errs low, and its source says it was not measured, so a host can present or refuse it.
- **A loaded model that is not the named one is never used.** A window borrowed from another model
  would carry the confidence of a reading while being wrong, which is the worst available failure.

No segregation beyond the package boundary is required: the system performs no privileged operation,
writes nothing, and holds no state between calls.

## Data Flow

A stated window short-circuits everything; otherwise both server queries are attempted and whatever
they yielded is handed to the precedence:

```text
stated window ──────────────────────────────────────┐
                                                    ▼
model name ──▶ list running models ──▶ loaded? ──▶ Select ──▶ (tokens, source)
                       │                             ▲
                       └─ show model ──▶ metadata ───┘
```

Either query may fail; a failure contributes nothing and the next source down is used. Cancellation
is not treated as a failure — it stops the read rather than yielding an assumed window.

## Design Constraints

- **Platform**: .NET 8, .NET 9 and .NET 10, on Windows, Linux and macOS.
- **Network**: `ReadAsync` requires a reachable Ollama server to produce anything better than the
  assumed default; `Select` requires none.
- **Security**: no credentials are held or transmitted by this system; the client is the
  application's, configured and owned by it, and is never disposed here.
- **Thread safety**: the reported window is immutable and `Select` holds no state, so both are safe
  for concurrent use; `ReadAsync` is as safe as the client it was handed.
- **Regulatory**: the system's requirements trace to the `AgentKit-Provider-Ollama` capability,
  which promises a guarded agent on a model served by Ollama carrying a conversation past the
  window that server enforces; the agent and the session half of that promise are delivered by
  AgentKitAgentsChatClient, and this system supplies the window figure. See *AgentKit Product
  Capability Design*.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.
