## OllamaModelCatalog

![AgentKit Ollama Agents Structure](AgentKitAgentsOllamaView.svg)

The `OllamaModelCatalog` class describes the models an Ollama server holds in the provider-neutral
terms the Core model catalog defines, so an application can filter them, show them in a picker and
validate settings against them.

### Purpose

**Where the description comes from.** The server's model list names what is installed; each model
is then described by the server's own show endpoint, a few at a time. The per-model read is needed
because the list does not carry what the catalog must know through this client: current Ollama
servers do include capabilities and a context length in the list response, but OllamaSharp's
`Model` type does not expose them. A model the server will not describe is still listed — unusable,
saying so — because one model's problem should not hide every other model from the picker.

**Capabilities are the server's.** A current server's description lists what a model can do —
completion, vision, tools, thinking — and that report is used as it stands, including its silence
about a capability. Using the report rather than a test of the model's projector matters: a newer
engine embeds vision in the model itself with no separate projector, and a projector test would hide
such a model from a vision application. A model is usable only when it reports both completion and
tools, because every AgentKit agent is a tool-using chat agent.

**An older server is described conservatively.** A server too old to report capabilities still
serves models an application may want to see. Vision can be inferred from the model's metadata —
its projector or its architecture's vision keys — but tool calling cannot, and an agent offered to a
model that cannot call tools fails on its first turn. Such a model is therefore listed as unusable,
with a notice that its capabilities were inferred.

**The trained length is a ceiling, never a window.** `ModelInfo.ContextWindowTokens` is the context
length the model file publishes. It is what a stated window is checked against and refused above,
because a window above it is not one the model can honor. It is never a window to account against:
the running instance is usually far below it, which is why *OllamaContextWindow Unit Design* never
reads it.

**Efforts match what reaches the wire.** OllamaSharp maps `ChatOptions.Reasoning` to Ollama's
`think`: `None` becomes `false`, and `Low`, `Medium` and `High` become `"low"`, `"medium"` and
`"high"`. A thinking model is therefore offered exactly those four — `none` (shown as "Off"), `low`,
`medium` and `high`. `ExtraHigh` is not offered, because it would collapse to `high` on the wire and
the user would be given something other than what was chosen. A GPT-OSS model cannot stop thinking,
so it is not offered `none`. No default effort is published: a thinking model thinks unless told
otherwise. A level is a hint the model interprets; Ollama does not validate it.

**Identifiers are the server's, with the server's resolution.** Models are identified by the name
the server lists and sorted by it. A lookup by a bare name finds the `latest` tag, as the server
itself resolves it, using the same tagging rule as *OllamaContextWindow Unit Design*.

Reading, caching and single-flight behavior are inherited; see *ModelCatalog Unit Design*. Safe for
concurrent use.

### Data Model

- **`ProviderId`** (`const string`, `ollama`) — the provider name every described model carries,
  and the name a selection is checked against by the Ollama window reading.
- **`DescribeParallelism`** (`const int`, 4) — how many models are described at once: enough to keep
  a listing of a dozen models quick, few enough not to crowd a server also serving a conversation.
- **`_client`** (`IOllamaApiClient`) — the client the server is read through. Its selected model is
  neither used nor changed, and the catalog never disposes it.

Each described `ModelInfo` is filled as follows:

| Field | Value |
| --- | --- |
| `Provider` | `ollama` |
| `Id`, `DisplayName` | The name the server lists, for example `gemma4:26b` |
| `Capabilities` | From the reported capabilities, or vision inferred for an older server |
| `Adjustments` | `ContextWindow` and `MaxOutputTokens`, plus `ReasoningEffort` when efforts exist |
| `IsUsable`, `UnusableReason` | From the reported capabilities; see the table below |
| `ContextWindowTokens` | The model's `{architecture}.context_length` metadata, or null |
| `MaxOutputTokens` | Not reported; null |
| `ReasoningEfforts` | For a thinking model, `none`, `low`, `medium`, `high`; else empty |
| `DefaultReasoningEffort` | Always null |
| `Notices` | The inferred-capabilities notice for an older server; else empty |
| `AdditionalProperties` | `family`, `parameter_size`, `quantization_level`, `size_bytes` when listed |

Reported capabilities map to flags case-insensitively: `vision` to `Vision`, `tools` to
`ToolCalls`, `thinking` to `Reasoning`. Usability is decided as follows:

| Server report | Usable | Reason |
| --- | --- | --- |
| `completion` and `tools` | Yes | — |
| No `completion`, but `embedding` | No | "embedding-only model" |
| Neither `completion` nor `embedding` | No | "Model does not support chat completion." |
| `completion` without `tools` | No | "Model does not support tool calling." |
| No capability list (older server) | No | "Ollama 0.6.4 or later is required to confirm tool support." |
| No description at all | No | "Ollama did not describe this model." |

A model described by an older server also carries an `Info` notice, "Ollama did not report
capabilities (server older than 0.6.4); capabilities were inferred." A model with no description
carries no capabilities and no adjustments.

Efforts, each with its provider-neutral equivalent:

| Id | Display name | Standard | Offered to GPT-OSS |
| --- | --- | --- | --- |
| `none` | Off | `ReasoningEffort.None` | No |
| `low` | Low | `ReasoningEffort.Low` | Yes |
| `medium` | Medium | `ReasoningEffort.Medium` | Yes |
| `high` | High | `ReasoningEffort.High` | Yes |

A model is treated as GPT-OSS when its listed family (else its described family) or its architecture
is `gptoss`, ignoring case.

### Key Methods

#### OllamaModelCatalog(IOllamaApiClient client, TimeSpan? lifetime, TimeProvider? timeProvider)

**Purpose:** Create a catalog over a client. Reads nothing; see *ModelCatalog Unit Design* for the
lifetime and the clock.

**Preconditions:** `client` is not null; `lifetime`, when supplied, is positive.

#### FetchAsync(CancellationToken cancellationToken)

**Purpose:** Read the server's listing and describe every model in it.

**Algorithm:** List the installed models, treating a null listing as empty and dropping any entry
with no name. Describe the models with at most `DescribeParallelism` show requests in flight,
writing each result into the slot of the model it describes, and map each listed model with its
description. Return the models sorted by identifier, ordinal ignoring case.

**Postconditions:** One description per named installed model. A failure to list fails the read; a
failure to describe one model costs only that model. Performs network I/O.

#### TryShowAsync(string model, CancellationToken cancellationToken)

**Purpose:** Ask the server to describe one model, treating failure as no answer.

**Algorithm:** Return the show response. Rethrow `OperationCanceledException` when the caller's token
was canceled; return null for any other failure, including an error status or a model removed while
the listing was being read.

#### Map(Model listed, ShowModelResponse? shown)

**Purpose:** Describe one model from what the server listed and showed. Internal and pure, so every
mapping rule can be exercised on captured payloads without a server.

**Algorithm:**

1. With no description, return an unusable model saying "Ollama did not describe this model.",
   carrying only its identity and listed properties.
2. With a capability list, map the flags and decide usability from it.
3. Without one, infer vision — from a projector, or from any metadata key starting with
   `{architecture}.vision.` — mark the model unusable and add the inferred-capabilities notice. A
   projector alone would miss a model whose newer engine embeds vision, so the vision keys count too.
4. Offer efforts only to a model with `Reasoning`, omitting `none` for GPT-OSS.
5. Read the trained length from `{architecture}.context_length`, accepting any numeric or numeric
   string shape the loosely typed metadata produces, and only when it is positive and fits an `int`.

#### IdEquals(string requested, string catalogId)

**Purpose:** Let a bare name and its `latest` tag name the same model.

**Algorithm:** Give each side an explicit tag with `OllamaContextWindow.Tagged`, then compare
ordinal ignoring case.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `client` | `ArgumentNullException` propagates |
| Zero or negative lifetime | `ArgumentOutOfRangeException` propagates |
| The model listing fails | Propagates unchanged — `HttpRequestException` when unreachable; not cached |
| The `HttpClient` times out during the listing | `TaskCanceledException` propagates; not cached |
| One model's description fails | That model is listed as unusable; the listing succeeds |
| The caller cancels during a description | `OperationCanceledException` propagates; nothing is cached |
| One description times out with no caller cancellation | That model is listed as unusable |
| Metadata without a usable context length | `ContextWindowTokens` is null; no ceiling is enforced |
| A server older than the capability report | Listed unusable, with an inferred-capabilities notice |

### Dependencies

- **ModelCatalog** — supplies `CachingModelCatalog`, the base this class derives from; see
  *ModelCatalog Unit Design*.
- **ModelInfo** — supplies `ModelInfo`, `ModelCapabilities`, `ModelAdjustments`,
  `ModelReasoningEffort` and `ModelNotice`; see *ModelInfo Unit Design*.
- **OllamaContextWindow** — supplies `Tagged`, so a lookup and the window reading resolve a bare name
  the same way; see *OllamaContextWindow Unit Design*.
- **OllamaSharp** — supplies `IOllamaApiClient`, `Model` and `ShowModelResponse`, and maps the
  neutral reasoning option to Ollama's `think`; see *OllamaSharp Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies `ReasoningEffort` and
  `AdditionalPropertiesDictionary`; see *Microsoft.Extensions.AI.Abstractions Design*.

### Callers

- **OllamaContextWindow** — checks that a selection names a model from `ProviderId` before reading
  the window for it; see *OllamaContextWindow Unit Design*.
- **Applications** — construct one over their Ollama client, list models for a picker, and call
  `SelectAsync` when a session starts. The document-assistant sample is the worked example.
