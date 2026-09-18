## OllamaSharp

### Purpose

`OllamaSharp` is a .NET client for the Ollama HTTP API. It is used because the context window an
Ollama server will enforce for a model is only reachable through Ollama's own reporting, and
AgentKit implements no wire protocols of its own. It was chosen rather than hand-written HTTP
because the same library is already the ordinary way a .NET application reaches Ollama as an
`IChatClient`, so an application taking this package takes nothing it would not otherwise have. It
is a runtime NuGet dependency of the AgentKitAgentsOllama package only, deliberately kept out of
AgentKitAgentsChatClient, which serves every `IChatClient` provider and must stay free of any one of
them.

### Features Used

- `IOllamaApiClient` — the client abstraction the discovery is handed, so a host supplies its own
  configured client rather than this package constructing one
- `ListRunningModelsAsync` — the running-models query, and `RunningModel` with its `Name`,
  `ModelName` and `ContextLength`, which is the length the server will actually enforce
- `ShowModelAsync`, `ShowModelRequest` — the model-metadata query
- `ShowModelResponse` and `ModelInfo` with `Architecture` and `ExtraInfo` — the published context
  length, keyed by architecture, which is what makes the key addressable without knowing the model

Not used: the chat, embedding, pull and generation surfaces. An application reaching Ollama as an
`IChatClient` uses those directly through its own client; this package reads reports.

### Integration Pattern

`OllamaContextWindow` in AgentKitAgentsOllama references `OllamaSharp` as a **direct**
`PackageReference`. The client is supplied by the host and is never constructed, configured or
disposed here — it is usually the same client the application already built for its chat and
embedding work, so the discovery adds no connection, no timeout policy and no lifetime of its own.

Both queries are treated as optional. A server that refuses either — an older build, a proxy, a
model that has never been loaded — yields a window from the next source down rather than a failed
run, so no initialization ordering or availability check is required of the host before calling.
