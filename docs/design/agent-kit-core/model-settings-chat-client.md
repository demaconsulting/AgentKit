## ModelSettingsChatClient

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ModelSettingsChatClient` class applies a validated model selection — the model, the reasoning
effort and the output limit — to every request an `IChatClient` pipeline sends.

### Purpose

**Why a decorator.** An `IChatClient` has no session to configure once; each request carries its
own options. A selection applied to the first request and not the rest would let later requests —
the ones made after a tool ran — run on the provider's defaults with nothing reporting it. The
choice therefore rides on every request, streaming and not.

**Provider-neutral.** The effort is applied as `ChatOptions.Reasoning`, which a provider's client
maps to its own wire option — OllamaSharp sends it as Ollama's `think` — so no provider-specific
option is written here, and the same decorator works in any `IChatClient` pipeline.

**Nothing is dropped silently.** Two parts of a selection cannot travel through an `IChatClient`: a
context tier, which has no meaning there, and an effort with no provider-neutral equivalent, such
as Copilot's `max`. A selection carrying either is refused when the decorator is constructed —
where the pipeline is composed — rather than quietly ignored on every request. Validation has
already refused any setting the model itself does not accept; see *ModelSettings Unit Design*.

**The caller's request is preserved.** A value the caller set on a request decided that request, so
it wins; only what the caller left unset is filled in. The caller's options object is never written
to, because one instance is commonly reused for every request of a session and writing to it would
leak the selection outward.

**The window is confirmed, not applied.** An `IChatClient` has no provider-neutral context window, so
this decorator cannot state one. A provider client that asks for a window on every request — on
Ollama, the context-sizing decorator; see *OllamaContextSizingChatClient Unit Design* — reports the
figure by implementing `IContextWindowChatClient`. A selection carrying `ContextWindowTokens` is
accepted only when `innerClient.GetService<IContextWindowChatClient>()` finds such a client beneath
reporting the same window; a missing or different window is refused at construction. Applying the
window here as well would give the same figure two owners; dropping it would discard the user's
choice with nothing reporting it.

### IContextWindowChatClient

`public interface IContextWindowChatClient : IChatClient` declares one member, `int
ContextWindowTokens { get; }` — the window, in tokens, the client asks its provider for on every
request. It lives in this unit because this unit is its only consumer. A `DelegatingChatClient`
returns itself from `GetService` for an interface it implements and otherwise asks the client it
wraps, so the implementer is found however deep beneath the decorator it sits. A provider whose
window is set some other way implements the interface on its own client or decorator.

The decorator class holds no mutable state and is as safe for concurrent use as the client it wraps.
Disposing it disposes the wrapped client, as every delegating client does.

### Data Model

- **`_selection`** (`ModelSelection`) — the validated selection applied to every request. Set at
  construction and never replaced. Invariant: carries no context tier, carries a reasoning effort
  only when that effort has a provider-neutral equivalent, and carries a context window only when a
  client beneath reported applying that same window at construction.

The wrapped client is held by the base `DelegatingChatClient` and is never replaced.

### Key Methods

#### ModelSettingsChatClient(IChatClient innerClient, ModelSelection selection)

**Purpose:** Wrap a chat client with a selection to apply.

**Algorithm:** The base class rejects a null client. Reject a null selection. Refuse a selection
whose settings name a context tier ("Context tier '{tier}' cannot be applied through an IChatClient,
which has no context tiers."), and one whose settings name an effort while its
`StandardReasoningEffort` is null ("Effort '{effort}' has no provider-neutral equivalent and cannot
be applied through an IChatClient."). When the settings carry `ContextWindowTokens`, ask the inner
client for an `IContextWindowChatClient`: refuse when none is found ("The selection's
ContextWindowTokens ({window}) is not applied by any client beneath: an IChatClient carries no
context window of its own. Compose a client that applies it at the same size, such as
OllamaContextSizingChatClient, or select without a window."), and when the one found reports
another figure ("The selection's ContextWindowTokens ({window}) differs from the {applied} tokens the
client beneath applies; state the same window in both places."). Figures are written with group
separators, culture-invariant. Record the selection.

**Preconditions:** Both arguments are non-null; the selection is one an `IChatClient` can apply,
and any window it carries is applied by a client beneath.

#### GetResponseAsync(IEnumerable&lt;ChatMessage&gt;, ChatOptions?, CancellationToken)

Forwards the request with `Apply(options)` in place of the caller's options. Messages and the token
are forwarded unchanged, and the response is returned as the wrapped client produced it.

#### GetStreamingResponseAsync(IEnumerable&lt;ChatMessage&gt;, ChatOptions?, CancellationToken)

The same contract for a streamed response. The two paths apply the selection identically; a host
that streams would otherwise silently lose it.

#### Apply(ChatOptions? options)

Private helper returning the options to forward.

**Algorithm:**

1. Clone the caller's options, or create new ones when there are none.
2. Set `ModelId` to the selected model's identifier when it is unset.
3. Set `MaxOutputTokens` to the chosen output limit when it is unset; when none was chosen it stays
   unset.
4. When the selection carries a provider-neutral effort: if `Reasoning` is unset, set it to that
   effort; if the caller set `Reasoning` without an effort, replace it with a copy carrying the
   caller's `Output` and the selected effort, so the caller's instance is never written to. When no
   effort was chosen nothing is set, so the model's own default stays in force.

**Postconditions:** A new options object, never null and never the caller's.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `innerClient` or `selection` | `ArgumentNullException` propagates |
| Selection naming a context tier | `ArgumentException` naming `selection` propagates |
| Selection naming an effort with no neutral equivalent | `ArgumentException` naming `selection` propagates |
| Selection window not applied beneath, or applied at another size | `ArgumentException` naming `selection` propagates |
| Failure inside the wrapped client | Propagates unchanged; this unit adds no handling |

All three refusals are composition errors in the host, made where the pipeline is assembled, so they
surface before any request is sent.

### Dependencies

- **ModelSettings** — supplies `ModelSelection`, whose model, settings and neutral effort are
  applied; see *ModelSettings Unit Design*.
- **ModelInfo** — supplies the selected model's identifier; see *ModelInfo Unit Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies `IChatClient`, `DelegatingChatClient`,
  `ChatOptions` and `ReasoningOptions`; see *Microsoft.Extensions.AI.Abstractions Design*.

### Callers

- **ChatClientAgentFactory** — wraps the supplied client in one when given a selection, beneath the
  image-promoting decorator, and offers `UseModelSettings` to install one in a chat client builder;
  see *ChatClientAgentFactory Unit Design*.
- **ChatClientProviderSessionFactory** — places one directly over the supplied client when given a
  selection; see *ChatClientProviderSessionFactory Unit Design*.
- **Applications** — compose one over their own client, for example over the Ollama
  context-sizing decorator. The document-assistant sample is the worked example.

`IContextWindowChatClient` is implemented by **OllamaContextSizingChatClient**, which reports the
`num_ctx` it asks for; see *OllamaContextSizingChatClient Unit Design*.
