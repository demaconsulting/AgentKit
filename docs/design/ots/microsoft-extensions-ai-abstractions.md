## Microsoft.Extensions.AI.Abstractions

### Purpose

`Microsoft.Extensions.AI.Abstractions` is used because an AgentKit tool *is* an
`AIFunction`, the provider-neutral shape that the GitHub Copilot SDK, the Microsoft Agent
Framework, and any `Microsoft.Extensions.AI` `IChatClient` all consume. It was chosen — rather
than a provider, runtime, or agent-loop package — so that one guarded tool can be offered to every
provider without AgentKit choosing a provider on the application's behalf. It is the only runtime
NuGet dependency AgentKit Core carries.

### Features Used

- `AIFunction` — the type every guarded tool is presented to a model as
- `AIFunctionFactory.Create` — builds an `AIFunction` from a delegate, used by
  `GuardedToolFactory` as the single supported construction path
- `AIFunctionFactoryOptions` and its `MarshalResult` hook — the control through which
  `GuardedToolFactory` installs its result-delivery guard, so a refusal reaches the model as a
  string and typed content passes through intact
- The `AIContent` type hierarchy — `AIContent`, `TextContent`, and `DataContent` — which carries
  binary and image results to the provider in a typed form rather than as serialized JSON
- `DelegatingAIFunction` — the transparent wrapper `GuardedToolFactory` returns every tool as, which
  forwards the name, description, schemas, serializer options, underlying method, additional
  properties and service lookup to the inner function while letting the wrapper resolve an
  oversized-output marker and, when retention of large outputs is composed, retain the output
- `IChatReducer` — the contract `AgentKitChatReducer` implements so a host-owned chat history can be
  reduced through AgentKit's consolidation in whatever reducer hook the host already has
- `IChatClient`, `DelegatingChatClient` and `ChatMessage` — the chat abstractions
  `ImagePromotingChatClient` and `ModelSettingsChatClient` decorate and `AgentKitChatReducer` reads
- `ChatOptions` — its `ModelId`, `MaxOutputTokens` and `Reasoning` members, with `ReasoningOptions`
  and the `ReasoningEffort` scale, which carry a model selection on every request
  `ModelSettingsChatClient` forwards; and `AdditionalPropertiesDictionary`, in which the provider
  catalogs build the provider-specific facts a `ModelInfo` exposes read-only

### Integration Pattern

`Microsoft.Extensions.AI.Abstractions` is consumed as the tool currency by `GuardedToolFactory`
in `AgentKitCore`, which references it as a **direct** `PackageReference`.
`AgentKitTools` reaches the same abstraction **transitively** through its `ProjectReference` to
`AgentKitCore`; it takes no direct package reference to *this* library of its own, so a tool
family composes through the same currency Core publishes without restating a dependency Core
already owns. There is no
initialization, configuration object, or disposal step: tools are constructed once through the
factory and handed to the host as an `IReadOnlyList<AIFunction>`.
