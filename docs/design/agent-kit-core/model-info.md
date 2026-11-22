## ModelInfo

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ModelInfo` record describes one model a provider offers in provider-neutral terms, together
with the vocabulary that description is written in: `ModelCapabilities`, `ModelAdjustments`,
`ModelReasoningEffort`, `ModelContextTier`, `ModelNotice` and `ModelNoticeKind`, and the
`ModelCapabilitiesExtensions.ToHostCapabilities` conversion that turns what a model can do into
what a tool pack is gated on.

### Purpose

A model picker and a session-start check need the same facts about a model whichever provider
offers it: what it can do, which settings it accepts, which values of those settings it offers,
whether AgentKit can run an agent on it at all, and what the user should be told about it. This
unit is where those facts are stated, once, in terms no provider owns.

**Produced by a catalog, but deliberately an ordinary record.** A catalog builds these from what
its provider reports; see *ModelCatalog Unit Design*, *OllamaModelCatalog Unit Design* and
*CopilotModelCatalog Unit Design*. An application on a provider no catalog serves — any
`IChatClient` — builds one by hand, and then validates settings and binds a selection exactly as it
would against a catalog's; see *ModelSettings Unit Design*. Nothing in the record depends on where
it came from.

**A capability flag is set only on a positive signal.** A provider that does not say whether a
model can see an image has not said that it can, so the flag stays clear and an application that
requires vision does not offer the model. Conservatism is the safe direction: a model wrongly
withheld costs a user one choice, while a model wrongly offered describes an image it never
received and nothing reports an error. A catalog departs from this only where its provider
documents otherwise and says so in its own design; the one departure is Copilot's treatment of an
unreported tool-calling flag, recorded in *CopilotModelCatalog Unit Design*.

**The model's half of the host declaration.** Whether the image tool family is offered used to be
asserted by the host, which could only guess what its model could see. A model description knows,
so `ToHostCapabilities` derives the host's vision declaration from it. Delegation is not derived:
whether a second agent can be started depends on the application's provider and credentials, not
on the model, so it stays the application's to declare; see *ToolPack Unit Design*.

Every type in the unit is immutable and safe for concurrent use.

### Data Model

`ModelCapabilities` is a flags enumeration of what a model can do:

| Member | Value | Meaning |
| --- | --- | --- |
| `None` | `0` | No capability is required or reported. |
| `ToolCalls` | `1` | The model calls tools through the provider's tool-calling channel. |
| `Vision` | `2` | The model accepts image input. |
| `Reasoning` | `4` | The model thinks before answering, or accepts a reasoning-effort setting. |

`ModelAdjustments` is a flags enumeration of which `ModelSettings` fields the provider can honor for
a model:

| Member | Value | Meaning |
| --- | --- | --- |
| `None` | `0` | No setting beyond the model itself can be honored. |
| `ReasoningEffort` | `1` | An effort from `ReasoningEfforts` can be chosen. |
| `ContextTier` | `2` | A tier from `ContextTiers` can be chosen. |
| `ContextWindow` | `4` | A context window in tokens can be stated. |
| `MaxOutputTokens` | `8` | A maximum number of output tokens per response can be stated. |

Both enumerations follow the rule `HostCapabilities` follows: each member is zero or a single bit,
so a combined requirement such as `Vision | ToolCalls` is satisfied only by a model reporting both,
and no member silently satisfies another. Members are added only when an application needs to
filter on one. What a stated window means is the provider's: on Ollama it is the exact length the
server is asked to load the model at, and on Copilot a ceiling that only lowers the window a session
accounts against.

`ModelNoticeKind` decides how prominently a notice is shown: `Info` (for example a tooltip),
`Warning` (a caution to see before choosing) and `Required` (text the provider says must be shown,
such as a data-retention statement).

Three small records carry the offered values:

| Record | Members | Notes |
| --- | --- | --- |
| `ModelReasoningEffort` | `Id`, `DisplayName`, `Standard` | `Standard` is the `ReasoningEffort` equivalent, or null |
| `ModelContextTier` | `Id`, `DisplayName`, `WindowTokens` | `WindowTokens` is the tier's total window, or null |
| `ModelNotice` | `Kind`, `Message`, `Code` | `Code` is the provider's machine-readable code, or null |

An effort is identified by the provider's own string, not by the provider-neutral scale, because
that scale cannot represent every level a provider offers — Copilot's `max` and `minimal` have no
equivalent on it. `Standard` carries the neutral equivalent when there is one, which is what an
`IChatClient` pipeline applies; see *ModelSettingsChatClient Unit Design*.

`ModelInfo` has the following members:

| Member | Type | Default | Description |
| --- | --- | --- | --- |
| `Provider` | `string` | required | The provider, for example `copilot` or `ollama`. |
| `Id` | `string` | required | The provider's identifier, which `ModelSettings.ModelId` names. |
| `DisplayName` | `string` | required | The name a picker shows. |
| `Vendor` | `string?` | null | The organization that publishes the model. |
| `Capabilities` | `ModelCapabilities` | `None` | What the model can do. |
| `Adjustments` | `ModelAdjustments` | `None` | Which settings fields can be honored. |
| `IsUsable` | `bool` | `true` | Whether AgentKit can run an agent on the model. |
| `UnusableReason` | `string?` | null | Why not, suitable to show verbatim. |
| `ContextWindowTokens` | `int?` | null | The largest window the provider reports — a ceiling. |
| `MaxOutputTokens` | `int?` | null | The most output tokens one response can carry. |
| `ReasoningEfforts` | `IReadOnlyList<ModelReasoningEffort>` | empty | Offered efforts, in picker order. |
| `DefaultReasoningEffort` | `string?` | null | The effort applied when none is chosen. |
| `ContextTiers` | `IReadOnlyList<ModelContextTier>` | empty | Offered tiers, in picker order. |
| `DefaultContextTier` | `string?` | null | The tier applied when none is chosen. |
| `BillingMultiplier` | `double?` | null | The provider's billing multiplier. |
| `Category` | `string?` | null | The provider's picker grouping. |
| `PriceCategory` | `string?` | null | The provider's price band for a badge. |
| `Notices` | `IReadOnlyList<ModelNotice>` | empty | Messages an application should show. |
| `AdditionalProperties` | `IReadOnlyDictionary<string, object?>?` | null | Provider-specific extras. |

Invariants and conventions:

- A hand-built description defaults to usable, with empty lists rather than missing ones, so it can
  be as short as the facts the application knows.
- `ContextWindowTokens` is a ceiling, never a window to account against. On Ollama it is the length
  the model was trained for, which the running instance is usually far below; on Copilot it is the
  model's maximum across its tiers. A stated window above it is refused by validation.
- `DefaultReasoningEffort` is never invented. A provider that publishes no default leaves the choice
  to the model, and an application that sends nothing gets exactly that.
- `UnusableReason` is null exactly when the catalog that built the record found the model usable; a
  hand-built record is trusted as written.
- Each catalog documents the `AdditionalProperties` keys it writes. Nothing in AgentKit reads them.
- Lists and the property dictionary are copied when set and exposed read-only, through
  `Array.AsReadOnly` and `ReadOnlyDictionary`, so a description cannot change after it is built and
  an exposed collection cannot be cast back to a mutable one. Catalogs cache descriptions and share
  them across callers and threads, which is why this matters. A `with` expression copies a replaced
  list. The dictionary's keys compare ordinally ignoring case, as an `AdditionalPropertiesDictionary`'s
  do; its values are not copied, so a catalog stores only immutable values.

### Key Methods

#### Supports(ModelCapabilities required)

**Purpose:** Test whether the model reports every capability in a requirement.

**Algorithm:** `(Capabilities & required) == required`.

**Postconditions:** `true` for `None`, whatever the model reports — requiring nothing is the common
case and needs no special case in the rule. `true` for a combined requirement only when every flag
in it is set.

#### ToHostCapabilities(this ModelCapabilities capabilities)

**Purpose:** Derive the host capabilities a model makes available, for
`ToolPackBuilder.WithHostCapabilities`.

**Algorithm:** Return `HostCapabilities.Vision` when `Vision` is set, otherwise
`HostCapabilities.None`. No other member maps.

**Postconditions:** Never includes `HostCapabilities.Delegation`. The result is the model's half of
the host's declaration; the host combines it with anything else it provides, and its explicit
declaration is retained, so it may still narrow or widen it.

### Error Handling

The two pure functions cannot fail. `Provider`, `Id` and `DisplayName` are `required` members, so
the compiler refuses a description that omits one; their content is not validated here, because a
blank identifier is refused where it is used, by `ModelSettings` and `CachingModelCatalog.GetAsync`.
A list member set to null is taken as empty. A list holding a null entry, or a property dictionary
holding two keys that differ only in case, is refused with `ArgumentException` when the description
is built, rather than failing later inside validation.

### Dependencies

- **ToolPack** — supplies `HostCapabilities`, the vocabulary `ToHostCapabilities` produces; see
  *ToolPack Unit Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies `ReasoningEffort`, and the
  `AdditionalPropertiesDictionary` whose key comparison the property dictionary keeps; see
  *Microsoft.Extensions.AI.Abstractions Design*.

### Callers

- **ModelCatalog** — lists, filters and looks up descriptions; see *ModelCatalog Unit Design*.
- **ModelSettings** — validates settings against a description and binds a `ModelSelection`, whose
  `HostCapabilities` calls `ToHostCapabilities`; see *ModelSettings Unit Design*.
- **ModelSettingsChatClient** — reads the model identifier from the selection's description; see
  *ModelSettingsChatClient Unit Design*.
- **OllamaModelCatalog** and **CopilotModelCatalog** — build descriptions from what their providers
  report; see their unit designs.
- **Applications** — build a description by hand for a provider no catalog serves, show
  descriptions in a picker, and pass a selected model's host capabilities to `ToolPackBuilder`. The
  document-assistant sample is the worked example.
