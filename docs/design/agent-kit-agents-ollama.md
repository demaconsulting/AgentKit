# AgentKitAgentsOllama System Design

![AgentKit Ollama Agents Structure](AgentKitAgentsOllamaView.svg)

The AgentKitAgentsOllama system makes the context window a conversation on an Ollama model is
accounted against the window the Ollama instance is actually using — by asking the server to run at
a size the application chose, and by reporting the size a running instance is using when none was
chosen.

## Purpose

Ollama is reached through AgentKitAgentsChatClient, because an Ollama client is an `IChatClient`.
There is one thing about Ollama that the generic adapter cannot supply: an `IChatClient` publishes
no context window, so `ChatClientProviderSessionFactory` must be told one — and the application has
to obtain it from somewhere.

Getting it wrong is costly in a specific direction. Too low, and the session rotates earlier than it
needed to, which costs summarizer calls. Too high, and it rotates *after* the server has already
discarded the start of the conversation: history is lost, nothing reports an error, and no later
turn can tell. That asymmetry is the reason this system exists rather than a constant.

The decisive fact about Ollama is that a model has two context lengths and only one of them is
enforced. The model file publishes a maximum; the server decides at load time what the running
instance will use. The published figure describes the file and is routinely far larger than what the
instance runs at, so this system never consults it. Only two figures are honest: the one the
application asked the server to run at, and the one the running instance reports. Where neither is
available, a conservative default is reported and named as an assumption.

Ollama will answer, and will take direction, but only through Ollama's own APIs — which is what
makes this a package rather than a helper inside the chat-client adapter. That adapter serves every
`IChatClient` provider and must stay free of any one of them; see *AgentKitAgentsChatClient System
Design*.

The system deliberately stops at these two jobs. It does not wrap `ChatClientProviderSessionFactory`
in a convenience factory: once the window is known, configuring a session with it is one line of
ordinary chat-client use, and a wrapper would add a type without adding a capability.

## Dependency Isolation

This system carries `OllamaSharp`, and carrying it is its whole justification as a separate package.
AgentKitAgentsChatClient must not acquire a provider-specific dependency — an application reaching
OpenAI or AI Foundry through that adapter would take an Ollama client library it never calls — so
the Ollama-specific code lives here instead. The system references `OllamaSharp` and the
provider-neutral chat abstractions it decorates, and nothing else: it takes no reference on
AgentKitCore, because the window it reports is an ordinary integer and none of Core's contracts
appear in its surface, and it takes no reference on either provider adapter.

## Architecture

The system is two units, one for each half of the promise. Neither needs the other, and a subsystem
layer between them would cost a requirements file, a design document, a verification document and a
review-set without removing anything anyone has to review.

- **OllamaContextWindow (Unit)** — the discovered window and its source, the conservative fallback,
  the reading of the server, and the pure precedence function that chooses among what it reported.
- **OllamaContextSizingChatClient (Unit)** — a decorator over the application's chat client that
  names the chosen context length on every request it forwards, so the instance the server runs is
  the one the session accounts against.

The two meet only in the application that composes them, and only through one integer: the window the
application ends up with — stated by it, or read back from the running instance, or assumed — is
handed to the decorator and, where it was stated, to the reading as well, which is why the reading
reports such a figure as `Stated` without asking the server about it.

The split inside `OllamaContextWindow` is the other design decision worth naming: reading the server
and choosing among what it said are separate operations, so the whole precedence — which is the part
that can be subtly and silently wrong — is exercisable without a server.

## External Interfaces

- **`OllamaContextWindow.ReadAsync`** — inbound, called by the host. Takes an Ollama client, a model
  name and an optionally stated window. Performs network I/O; tolerates the server query failing;
  propagates cancellation.
- **`OllamaContextWindow.Select`** — inbound, called by the host or by `ReadAsync`. Takes what the
  server reported. Pure; contacts nothing. Its signature no longer carries a model-metadata
  argument, which is a breaking change from the previous shape and is the point of this design
  rather than a consequence of it.
- **`OllamaContextWindow`** — outbound. A token count, always positive, and the source it came from.
- **`OllamaContextSizingChatClient`** — inbound, constructed by the host around the client it
  already owns. Presents `IChatClient`, so it composes anywhere that interface is accepted.
- **Ollama HTTP API** — outbound, through `OllamaSharp`. The running-models query, which is
  optional, and the chat requests the application sends, which carry the chosen context length.

The window is delivered as a plain integer on the returned record, so a host hands it to
`ChatClientProviderSessionFactory` directly. The decorator presents only `IChatClient`. None of this
system's types appear in the session the application goes on to build.

## Dependencies

- **OllamaSharp** — supplies the Ollama client interface, the running-models report, and the
  translation of a named context-length option into the Ollama request's own options block; see
  *OllamaSharp Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies `IChatClient`, `ChatOptions` and the
  delegating chat-client base the sizing decorator is built on. Referenced directly because these
  types appear on this system's own public surface.

The system depends on nothing else — not AgentKitCore, not either provider adapter.

## Risk Control Measures

The risk this system is a control for is a session told a window larger than the server enforces,
which loses conversation history with no error raised anywhere. Four measures bound it:

- **A published maximum is never used.** The only figure the system will report from the server is
  one describing the running instance. What the model file could support is not asked for at all,
  so it cannot leak into the account through any path.
- **The reported size is asked for on every request.** Whichever rung produced it, the window the
  application will account against is put on each request the run sends, so the instance is
  loaded at it and reloaded at it, and the reported figure is true rather than merely asserted. Why
  a discovered window needs this as much as a stated one is argued in *OllamaContextSizingChatClient
  Unit Design*.
- **The fallback is conservative and is named as assumed.** When nothing can be read the reported
  figure errs low, and its source says it was not measured, so a host can present or refuse it.
- **A loaded model that is not the named one is never used.** A window borrowed from another model
  would carry the confidence of a reading while being wrong, which is the worst available failure.

No segregation beyond the package boundary is required: the system performs no privileged operation,
writes nothing, and holds no state between calls.

## Data Flow

One integer reaches the session factory, and the same integer is put on every chat request. Where the
application stated it, the reading short-circuits; where it did not, the running instance is asked:

```text
stated size ──▶ ReadAsync (short-circuits) ──▶ (tokens, Stated) ──┐
                                                                  ├──▶ session factory
no stated size ──▶ model name ──▶ list running models ──▶ Select ─┘
                                        │
                                        └──▶ (tokens, LoadedModel | Assumed)

(tokens, any source) ──▶ sizing decorator ──▶ every chat request names the context length
```

The running-models query may fail; a failure contributes nothing and the conservative default is
reported as assumed. Cancellation is not treated as a failure — it stops the read rather than
yielding an assumed window. A stated size is never confirmed against the server at run time: asking
what a model would be loaded at is not free of consequence, and discovery must not change the thing
it was asked to observe.

## Design Constraints

- **Platform**: .NET 8, .NET 9 and .NET 10, on Windows, Linux and macOS.
- **Network**: `ReadAsync` requires a reachable Ollama server to produce anything better than the
  assumed default; `Select` requires none; the decorator performs no I/O of its own.
- **Security**: no credentials are held or transmitted by this system; the client is the
  application's, configured and owned by it, and is never disposed by the reading.
- **Thread safety**: the reported window is immutable and `Select` holds no state, so both are safe
  for concurrent use; `ReadAsync` and the decorator are as safe as the client they were handed.
- **Regulatory**: the system's requirements trace to the `AgentKit-Provider-Ollama` capability,
  which promises a guarded agent on a model served by Ollama carrying a conversation past the
  window that server enforces; the agent and the session half of that promise are delivered by
  AgentKitAgentsChatClient, and this system supplies the window figure and makes it true. See
  *AgentKit Product Capability Design*.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.
