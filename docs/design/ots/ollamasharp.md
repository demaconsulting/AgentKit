## OllamaSharp

### Purpose

`OllamaSharp` is a .NET client for the Ollama HTTP API. It is used because the context window an
Ollama instance is running — and the option that decides it — are only reachable through Ollama's
own API, and AgentKit implements no wire protocols of its own. It was chosen rather than
hand-written HTTP because the same library is already the ordinary way a .NET application reaches
Ollama as an `IChatClient`, so an application taking this package takes nothing it would not
otherwise have. It is a runtime NuGet dependency of the AgentKitAgentsOllama package only,
deliberately kept out of AgentKitAgentsChatClient, which serves every `IChatClient` provider and
must stay free of any one of them.

### Features Used

- `IOllamaApiClient` — the client abstraction the discovery is handed, so a host supplies its own
  configured client rather than this package constructing one
- `ListRunningModelsAsync` — the running-models query, and `RunningModel` with its `Name`,
  `ModelName` and `ContextLength`, which is the length the instance is actually running
- The `IChatClient` implementation's carriage of a named chat option into the Ollama request's own
  options block — how the chosen context length reaches the server on every request

Not used: the model-metadata, embedding, pull and generation surfaces. The metadata query is
deliberately not used: it reports what a model file could support, which is not what the instance is
running. An application reaching Ollama as an `IChatClient` uses the chat surface directly through
its own client; this package reads one report and annotates requests.

### Integration Pattern

`OllamaContextWindow` in AgentKitAgentsOllama references `OllamaSharp` as a **direct**
`PackageReference`. The client is supplied by the host and is never constructed, configured or
disposed by the reading — it is usually the same client the application already built for its chat
and embedding work, so the discovery adds no connection, no timeout policy and no lifetime of its
own.

`OllamaContextSizingChatClient` does not call `OllamaSharp` at all. It decorates whatever
`IChatClient` the host supplies and names the context length as an additional request option; the
translation of that option into Ollama's wire format is `OllamaSharp`'s, which is why it carries an
off-the-shelf requirement of its own.

The running-models query is treated as optional. A server that refuses it — an older build, a proxy,
one that never answers — yields the conservative default rather than a failed run, so no
initialization ordering or availability check is required of the host before calling.
