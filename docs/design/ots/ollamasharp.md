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
- The `IChatClient` implementation's mapping of chat content to the request: a tool result's text
  becomes the tool message's content, and a user message's image content becomes that message's
  `images`. Image promotion in AgentKitAgentsChatClient relies on both. OllamaSharp writes image
  content found in a tool result into the tool message as base64 text rather than `images` — the
  reason promotion leaves only a placeholder there. A test-only reference in the ChatClient tests
  exercises this mapping on the wire
- The `IChatClient` implementation's mapping of the provider-neutral request options AgentKit's model
  selection travels on: `ChatOptions.Reasoning.Effort` becomes Ollama's `think` (`None` as `false`;
  `Low`, `Medium` and `High` as the matching string; `ExtraHigh` as `"high"`), and `ChatOptions.ModelId`
  becomes the request's `model`. This was read from OllamaSharp's `AbstractionMapper` at tag 5.5.2,
  the version referenced
- For `OllamaModelCatalog` only: `ListLocalModelsAsync`, the server's model listing (`/api/tags`),
  and `ShowModelAsync`, each model's description (`/api/show`). Of `ShowModelResponse` the catalog
  reads `Capabilities`; `Info.ExtraInfo`, for `{arch}.context_length` and the `{arch}.vision.*` keys;
  `Projector`; and `Details` (`Family`, `ParameterSize` and `QuantizationLevel`)

Not used: the embedding, pull and generation surfaces. The model-metadata query is deliberately not
used **for the window**: it reports what a model file could support, which is not what the instance is
running. The catalog reads it, but only for capabilities and for the trained length as a ceiling a
stated window is refused above; reading the window never calls it. An application reaching Ollama as
an `IChatClient` uses the chat surface directly through its own client; the window reading reads one
report and annotates requests.

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

`OllamaModelCatalog` takes the same host-supplied client and never changes its selected model. It
lists the server's models, then describes them a few at a time; a model the server will not describe
is still listed, as unusable, rather than failing the whole listing.
