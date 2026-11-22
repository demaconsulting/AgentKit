## ModelSettings

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ModelSettings` record holds the choices a user makes about the model an agent runs on, the
`ModelSelection` class is those choices validated against the model they name, and the
`ModelSettingsException` class reports every reason a choice cannot be applied.

### Purpose

**What an application persists.** A user picks a model and, where the model allows, a reasoning
effort, a context tier, a context window and an output limit. The application saves that choice
and reads it back on the next launch, so the record is plain data — strings and numbers — that
serializes as it stands. It names each choice by the identifier a `ModelInfo` publishes, which is
what lets a choice saved yesterday be checked against what the provider offers today.

**Validated before use.** Provider factories accept a `ModelSelection`, not settings, and only
`Bind` and `ModelCatalogExtensions.SelectAsync` can create one, because its constructor is internal.
Holding a selection therefore shows that the settings were valid for the model, and that the
model reported every capability the application required, when the selection was made. Settings
that name a withdrawn model or an effort the model no longer accepts are refused when the
selection is made, before a session starts, rather than by the provider part-way through a
conversation. `Validate` reports the same problems without throwing, for a picker that wants to
explain them as the user chooses.

**Every problem at once, each worded for the user.** A user who fixes one problem only to be shown
the next has been made to guess, so every rule is checked and every problem collected. Each message
is written to be shown verbatim.

**Nothing the provider would ignore is accepted.** Providers do not reliably refuse a setting they
do not understand — a live Ollama server accepted every think level it was sent — so a mistyped or
stale setting would otherwise be silently dropped. A setting the model does not accept, or a value
it does not offer, is refused, and the refusal lists the values it does offer.

**The requirement is not part of the settings.** What capabilities the application needs is the
application's policy, not the user's choice, so it is passed to validation rather than stored; a
persisted choice therefore cannot carry a weaker requirement than the application now has.

All three types are immutable and safe for concurrent use.

### Data Model

`ModelSettings` is a record with value equality:

| Member | Type | Meaning |
| --- | --- | --- |
| `ModelId` | `string` | The chosen model, as `ModelInfo.Id` names it. Never null or blank. |
| `ReasoningEffort` | `string?` | An effort identifier from `ModelInfo.ReasoningEfforts`, or null. |
| `ContextTier` | `string?` | A tier identifier from `ModelInfo.ContextTiers`, or null. |
| `ContextWindowTokens` | `int?` | The chosen window in tokens, or null. |
| `MaxOutputTokens` | `int?` | The chosen maximum output tokens per response, or null. |

A null member leaves that choice to the provider. `ModelId` is held behind a private field whose
`init` accessor refuses a null or blank value, so the invariant also holds for a copy made with a
`with` expression. What a window does is the provider's: on Ollama it is the exact length the
server is asked to run the model at, and on Copilot a ceiling that only lowers the window a session
accounts against. Either way it may not exceed what the model provides.

`ModelSelection` is a sealed class with an internal constructor:

| Member | Type | Meaning |
| --- | --- | --- |
| `Model` | `ModelInfo` | The model the settings were validated against. |
| `Settings` | `ModelSettings` | The validated settings. |
| `Required` | `ModelCapabilities` | The capabilities the application required. |
| `HostCapabilities` | `HostCapabilities` | Derived from `Model.Capabilities`; see *ModelInfo Unit Design*. |
| `StandardReasoningEffort` | `ReasoningEffort?` | The chosen effort's neutral equivalent, or null. |
| `ContextTierWindowTokens` | `int?` | The chosen tier's window, else the default tier's, or null. |

`ModelSettingsException` derives from `InvalidOperationException`, because the settings are
well-formed but wrong for the model as it now is. It carries `ModelId` and `Problems`, a read-only
copy of every reason, each suitable to show verbatim. Its message is "Model settings for '{id}' are not
valid: " followed by the problems joined with spaces.

### Validation Rules

The rules, with the message each produces. Identifiers are compared ignoring case for efforts and
tiers, and ordinally for the model. Token counts are formatted with invariant-culture group
separators, for example `262,144`.

- **V1 — A model is named.** Enforced at construction and on every copy: "A model id is required."
  (`ArgumentException`).
- **V2 — The settings name this model.** "Settings name model '{settings id}' but were checked
  against model '{model id}'."
- **V3 — The model is usable.** "Model '{id}' cannot be used: {reason}", where the reason is the
  model's `UnusableReason`, or "the provider reports it as unavailable." when it gives none.
- **V4 — Every required capability is reported.** "Model '{id}' does not support {missing}, which
  this application requires." The missing capabilities are named "vision", "tool calling" and
  "reasoning", joined with "and"; a member added later and not named is still reported by its
  enumeration name.
- **V5 — A chosen effort is offered.** "Model '{id}' does not support reasoning effort settings."
  when the model lacks the effort adjustment or offers no effort; otherwise "Model '{id}' does not
  support reasoning effort '{effort}'. Supported: {ids}."
- **V6 — A chosen tier is offered.** "Model '{id}' offers no context tiers." when the model lacks
  the tier adjustment or offers no tier; otherwise "Model '{id}' does not offer context tier
  '{tier}'. Offered: {ids}."
- **V7 — A chosen window is positive and settable.** "A context window must be a positive number of
  tokens." or "Model '{id}' does not allow setting the context window." Either ends the window
  checks.
- **V8 — A window fits a tier.** The tier is the chosen tier when the model offers it and it
  publishes a window, else the default tier when it publishes one. When the stated window exceeds
  that tier's window: "Model '{id}'
  provides at most {tier window} tokens in context tier '{tier}'; a {window}-token window cannot be
  honored."
- **V9 — Otherwise a window fits the model.** When no such tier publishes a window and the model
  reports `ContextWindowTokens`: "Model '{id}' supports at most {limit} tokens; a {window}-token
  window is not supported." A limit the provider does not report is not enforced, because nothing
  could be said about it.
- **V10 — A chosen output limit fits.** "A maximum output must be a positive number of tokens.",
  "Model '{id}' does not allow setting the maximum output." or "Model '{id}' allows at most {limit}
  output tokens."
- **V11 — The provider offers the model.** Raised by `SelectAsync` rather than `Validate`, because
  only a catalog can say so: "Model '{id}' is not available from provider '{provider}'. It may have
  been removed or you may no longer have access."; see *ModelCatalog Unit Design*.

V8 and V9 are one provider-neutral rule held here in Core, not in either provider package: the
same window refusal applies whether the figure will be sent to Ollama as an exact length or applied
to Copilot as a ceiling. A chosen tier that the model offers but that publishes no window gives way
to the default tier's window, and only when neither publishes one does the model's largest window
bound the setting.

### Key Methods

#### ModelSettings(string modelId)

**Purpose:** Create settings naming a model, with every other choice left to the provider.

**Preconditions:** `modelId` is not null or blank (V1).

#### DefaultsFor(ModelInfo model)

**Purpose:** Create the settings a picker starts from: the model with the provider's own defaults.

**Algorithm:** Name the model, and copy `DefaultReasoningEffort` and `DefaultContextTier`. No
window or output limit is set, because a provider's defaults for those are what a session already
gets by saying nothing.

**Postconditions:** Nothing the provider did not publish is set; a model with no published default
effort yields settings with none.

#### Validate(ModelInfo model, ModelCapabilities required)

**Purpose:** List every reason these settings cannot be applied to a model, without throwing.

**Algorithm:** Apply V2 to V10 in order, appending each problem found; no rule stops another except
that the window checks end after a V7 problem.

**Postconditions:** A read-only list, empty when the settings are valid. Holds no state; the same inputs give
the same problems.

#### Bind(ModelInfo model, ModelCapabilities required)

**Purpose:** Validate the settings and produce the selection a provider factory accepts.

**Algorithm:** Call `Validate`; when it reports anything, throw a `ModelSettingsException` carrying
every problem. Otherwise find the chosen effort (ignoring case) and take its `Standard`, take the
window of the chosen tier — or of the default tier when none is chosen or the chosen one publishes
none — and construct the selection.

**Postconditions:** The selection holds these exact settings, the model and the requirement. An
effort with no neutral equivalent, such as Copilot's `max`, yields a null `StandardReasoningEffort`
rather than an approximation.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null or blank model identifier, at construction or in a copy | `ArgumentException` propagates |
| Null `model` passed to `DefaultsFor`, `Validate` or `Bind` | `ArgumentNullException` propagates |
| A model whose identifier is blank, passed to `DefaultsFor` | `ArgumentException` propagates |
| Any of V2 to V10 found by `Validate` | Returned as a problem; nothing is thrown |
| Any of V2 to V10 found by `Bind` | `ModelSettingsException` listing every problem |
| A null efforts or tiers list on a hand-built model | Treated as empty |

### Dependencies

- **ModelInfo** — supplies `ModelInfo`, `ModelCapabilities`, `ModelAdjustments`,
  `ModelReasoningEffort`, `ModelContextTier` and `ToHostCapabilities`; see *ModelInfo Unit Design*.
- **ToolPack** — supplies `HostCapabilities`, the type of `ModelSelection.HostCapabilities`; see
  *ToolPack Unit Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies `ReasoningEffort`; see
  *Microsoft.Extensions.AI.Abstractions Design*.

### Callers

- **ModelCatalog** — `SelectAsync` binds settings after looking the model up; see *ModelCatalog
  Unit Design*.
- **ModelSettingsChatClient** — applies a selection to every request; see *ModelSettingsChatClient
  Unit Design*.
- **ChatClientAgentFactory** and **ChatClientProviderSessionFactory** — accept a selection and
  compose a `ModelSettingsChatClient` from it; see their unit designs.
- **CopilotAgentFactory**, **CopilotProviderSessionFactory** and **CopilotSummarizer** — accept a
  selection naming a Copilot model and apply its model, effort and tier; the provider-session
  factory also applies its window as a ceiling; see their unit designs.
- **OllamaContextWindow** — `ReadAsync(client, selection)` takes the model and the stated window from
  a selection naming an Ollama model; see *OllamaContextWindow Unit Design*.
- **Applications** — persist settings, validate them in a picker, and bind them when a session
  starts. The document-assistant sample is the worked example.
