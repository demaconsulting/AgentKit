## ChatClientAgentFactory

![AgentKit ChatClient Agents Structure](AgentKitAgentsChatClientView.svg)

The `ChatClientAgentFactory` class builds a Microsoft Agent Framework `AIAgent` from any
`IChatClient` and a supplied tool list, installing the image-promoting decorator on every agent it
builds.

### Purpose

`ChatClientAgentFactory` is the whole of the AgentKitAgentsChatClient system. A provider reached
through an `IChatClient` carries no tools of its own, so handing it the supplied tools is the whole
job of turning it into an agent — except for the one thing that would otherwise break images
silently. The factory absorbs that concern so an application never has to know it exists: it wraps
the supplied client in Core's `ImagePromotingChatClient` before constructing the agent, on every
path, with no option to omit it.

A host that builds its own agent or pipeline — composing AgentKit's tools into its own
`ChatClientAgent`, or a raw `IChatClient` pipeline — bypasses `Create`. The factory therefore also
publishes `UseImagePromotion`, a `ChatClientBuilder` extension that installs the same decorator in a
pipeline the host builds. It lives here rather than in Core because `ChatClientBuilder` belongs to
`Microsoft.Extensions.AI`, which this package receives through `Microsoft.Agents.AI` and which Core
does not reference: placing it in Core would add that library and its own dependencies to the contract
package. A host that references only Core can install the decorator with
`builder.Use(inner => new ImagePromotingChatClient(inner))`.

The class is static and holds no state.

### Key Methods

#### Create(IChatClient client, IList&lt;AIFunction&gt; tools, string? instructions, string? name, ModelSelection? selection)

The single public entry point. Validates its arguments, wraps `client` in the image-promoting
decorator — over Core's `ModelSettingsChatClient` when a selection is supplied — and constructs a
`ChatClientAgent` over the wrapped client carrying `instructions`, `name`, and the supplied tools.

**Algorithm:**

1. Reject a null `client`.
2. Validate `tools` (see `ValidateTools`).
3. When `selection` is supplied, wrap `client` in `new ModelSettingsChatClient(client, selection)`,
   directly over the provider's client and beneath image promotion, so every request the agent's loop
   sends — including those after a tool ran — carries the model, the effort and the output limit.
4. Wrap the result through `WrapWithImagePromotion`.
5. Construct and return a `ChatClientAgent` over the wrapped client, passing the instructions, the
   name, and the tools cast to the framework's tool type.

**Refusal of a selection:** `ModelSettingsChatClient` refuses a selection that carries a context
tier, with "Context tier '{tier}' cannot be applied through an IChatClient, which has no context
tiers.", and one whose effort has no provider-neutral equivalent, with "Effort '{effort}' has no
provider-neutral equivalent and cannot be applied through an IChatClient." A selection carrying a
context window is accepted only when `client` applies it — an `IContextWindowChatClient` at or
beneath it, such as the Ollama context-sizing decorator, reports the same window — and is refused
otherwise with a message naming `ContextWindowTokens`; see *ModelSettingsChatClient Unit Design*.
Each refusal is an `ArgumentException` raised from `Create`, so a setting the client cannot apply is
never dropped silently.

**Ownership:** the caller retains ownership of the client it supplied. The factory wraps that
client but constructs nothing else the caller must dispose; the returned agent's lifetime, and the
client beneath it, remain the host's to manage.

**Throws:** `ArgumentNullException` when `client` or `tools` is null, or a tool is null;
`ArgumentException` when `tools` is empty, two tools share a name, or `selection` carries a setting
an `IChatClient` cannot apply or a window `client` does not apply at the same size.

#### UseImagePromotion(this ChatClientBuilder builder)

Public extension installing the decorator in a host-built pipeline.

**Algorithm:**

1. Reject a null `builder`.
2. Register a factory with `builder.Use`. `ChatClientBuilder` composes outermost first, so the factory
   receives the client beneath the decorator's position.
3. When that client answers a service lookup for `FunctionInvokingChatClient`, the decorator would sit
   above the function-invocation loop and never see a tool result, so throw
   `InvalidOperationException` naming the correct order. This happens when the host calls `Build()`.
4. Otherwise wrap the client through `WrapWithImagePromotion`.

**Placement:** `client.AsBuilder().UseImagePromotion().Build()` for a `ChatClientAgent`, which adds
its function-invocation loop above the client it is given; `.UseFunctionInvocation().UseImagePromotion()`
for a raw pipeline.

**When it is needed:** required where the adapter cannot deliver a tool-result image — Ollama through
OllamaSharp, which serializes it as base64 text in the tool message; not needed on the GitHub Copilot
SDK, which delivers tool-result images natively.

**Throws:** `ArgumentNullException` for a null `builder`; `InvalidOperationException` from `Build()`
for a decorator placed above function invocation.

#### UseModelSettings(this ChatClientBuilder builder, ModelSelection selection)

Public extension installing Core's `ModelSettingsChatClient` in a host-built pipeline, so a validated
model selection rides on every request.

**Algorithm:**

1. Reject a null `builder` and a null `selection`.
2. Register `inner => new ModelSettingsChatClient(inner, selection)` with `builder.Use`.

**Placement:** a value already set on a request wins over the selection wherever the decorator sits,
and placing it last — nearest the provider, but above any client added to apply a context window —
keeps every request the pipeline makes under the
selection: `.UseFunctionInvocation().UseImagePromotion().UseModelSettings(selection)`. A selection
carrying a context window also needs the client that applies the window — an
`IContextWindowChatClient` such as the Ollama context-sizing decorator — beneath this decorator, or
the selection is refused when the pipeline is built.
A host that references only Core installs the same decorator with
`builder.Use(inner => new ModelSettingsChatClient(inner, selection))`.

**Throws:** `ArgumentNullException` for a null `builder` or `selection`; `ArgumentException` from
`Build()` for a selection carrying a context tier or an effort with no provider-neutral equivalent,
or a context window no client beneath the decorator applies at the same size.

#### WrapWithImagePromotion(IChatClient client)

Internal seam returning `new ImagePromotingChatClient(client)`. It exists as a named, internal
method for one reason: so a test can assert the decorator is installed — the guarantee this package
exists for — without reaching inside a constructed agent, whose chat client the framework may
further wrap with its own function-invocation middleware. The wrap is unconditional; there is no
code path in the factory that produces an agent whose chat client is not image-promoting.

**Throws:** `ArgumentNullException` when `client` is null.

#### ValidateTools(IList&lt;AIFunction&gt; tools)

Internal helper rejecting a tool list a well-formed agent could not be built from: a null list, an
empty list, a list containing a null entry, or a list carrying two tools of the same name. Names are
compared with ordinal string comparison. The collision is refused here, where the application
composed its tools, rather than discovered from a model's undefined choice between two identically
named tools later.

### Design Constraints

**The decorator must be installed unconditionally and beneath the loop.** `ChatClientAgent` runs
its function-invocation loop above the `IChatClient` it is given, so wrapping the client before
handing it to the agent places the decorator beneath that loop — exactly where a tool result, and
the image it may carry, has already been appended to the conversation. There is no parameter to
disable the wrap, because the entire value of the package is that a caller cannot forget it.

**No image re-encoding or agent runtime of its own.** The factory adds a decorator and constructs
the framework's agent; it does not resize, caption, or re-encode an image, and it does not
implement a tool-calling loop. Those behaviors belong to Core's decorator and to Microsoft Agent
Framework respectively.

**Thin by intent.** The factory validates, wraps, and constructs. Any behavior beyond that would
duplicate the framework it adapts.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `client` | `ArgumentNullException` propagates |
| Null `tools`, or a null tool entry | `ArgumentNullException` propagates |
| Empty `tools` | `ArgumentException` propagates |
| Two tools sharing a name | `ArgumentException` propagates |
| Null `builder` to `UseImagePromotion` | `ArgumentNullException` propagates |
| Decorator placed above function invocation | `InvalidOperationException` when the pipeline is built |
| Selection with a tier, or an effort with no neutral equivalent | `ArgumentException` from `Create` |
| Selection with a window `client` does not apply at the same size | `ArgumentException` from `Create` |
| Null `builder` or `selection` to `UseModelSettings` | `ArgumentNullException` propagates |
| Selection given to `UseModelSettings` that cannot be applied | `ArgumentException` when the pipeline is built |

Every rejected condition is a defect in the composing application, surfaced where the host wrote it
rather than at the first conversation.

### Dependencies

- **AgentKitCore** — supplies `ImagePromotingChatClient`; see *ImagePromotingChatClient Unit
  Design*; and `ModelSelection` and `ModelSettingsChatClient`; see *ModelSettings Unit Design* and
  *ModelSettingsChatClient Unit Design*.
- **Microsoft.Agents.AI** — supplies `AIAgent` and `ChatClientAgent`, and brings
  `Microsoft.Extensions.AI`'s `ChatClientBuilder` and `FunctionInvokingChatClient`; see
  *Microsoft.Agents.AI Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies `IChatClient`, `AIFunction`, and `AITool`.

### Callers

`ChatClientAgentFactory.Create` is a public API entry point. An application calls it once to obtain
an agent for a provider reached through an `IChatClient`. `UseImagePromotion` and `UseModelSettings`
are public API entry points a host calls while building its own pipeline. Within this system
`ChatClientProviderSessionFactory`
calls `WrapWithImagePromotion`; nothing else calls the factory.
