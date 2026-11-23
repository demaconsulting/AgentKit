## CopilotModelCatalog

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotModelCatalog` class describes the models the GitHub Copilot service offers the signed-in
user in the provider-neutral terms the Core model catalog defines, so an application can filter
them, show them in a picker and validate settings against them.

### Purpose

**Where the description comes from.** The listing depends on who is signed in, so only the runtime
AgentKit started can read it. The catalog reads the service's own model listing through that
runtime's client, `Rpc.Models.ListAsync`, rather than through `CopilotClient.ListModelsAsync`. The
latter caches its answer for the life of the connection, which would stop `RefreshAsync` from
showing a model the user was just granted; the uncached query has no such cache. The runtime's
client is read at every read and never kept, and the runtime is checked for disposal first, so a
runtime its host disposed is refused rather than restarted behind the host's back — the SDK restarts
a disposed client on demand. See *CopilotRuntime Unit Design*.

The uncached query was confirmed on AgentKit's own runtime by the opt-in live suite, which read 24
entries through it, the same count `ListModelsAsync` returned. A fallback to `ListModelsAsync` was
therefore not needed.

**Capabilities — a provider-specific rule for tool calling.** Vision is set only when the service
reports it; a missing or false vision flag is not vision, because a model wrongly offered as able to
see describes an image it never received. Reasoning is set when the service reports a
reasoning-effort setting or thinking, or lists any effort. Tool calling is the one documented
exception to setting a flag only on a positive signal: it is treated as **supported unless the
service says otherwise**. The service reports no tool-calling flag for any model, and the Copilot
runtime is itself a tool-calling agent runtime, so reading silence as absence would make every
Copilot model fail a vision-and-tools requirement. An explicit refusal is still honored: a model the
service reports as unable to call tools carries no tool-calling flag and is unusable.

**Context tiers come from billing.** The service publishes no tier list for most models; it
publishes token prices. A model with token prices offers the `default` tier, labeled "Standard",
and one with long-context prices also offers `long_context`, labeled "Long context"; the service
publishes no labels, so AgentKit supplies them. Any tier the model declares directly is added once,
labeled by its identifier unless it is one of those two, with no published window. A tier's window
is its prompt budget plus the model's output limit, capped at the model's largest window, because
the sum can exceed what any tier provides.

**Efforts are the service's own.** They are offered by the service's identifiers and in its order,
because the runtime applies an effort by that identifier — including levels such as `max` and
`minimal` that have no provider-neutral equivalent, which are still offered. A default is reported
only when the service names one it offers; the service names none for any model observed, and
inventing one would send a setting the user never chose.

**Usability follows policy.** A model is usable when its policy is enabled or absent and it is not
reported as unable to call tools. A model the organization disabled, or one not yet enabled for the
account, is unusable with a reason; the latter also carries the service's terms as a notice, because
enabling it is an action the user takes on GitHub.

**The automatic choice is not listed.** The service lists an `auto` entry that lets it choose a
model per request. It cannot promise a capability the application requires, such as vision, so
listing it would offer a choice validation could not stand behind. It is skipped, and a lookup of it
finds nothing. An "Auto" pseudo-model built from the service's automatic-choice description is a
possible future extension; it is not implemented, because it still could not guarantee a required
capability.

**No reasoning-summary setting is offered.** The neutral settings carry no such choice, and nothing
in an application's model choice needs one.

Reading, caching and single-flight behavior are inherited; see *ModelCatalog Unit Design*. Safe for
concurrent use.

#### Context Tiers and the Reported Limit

What the service's largest-window figure means was settled by the opt-in live suite rather than
assumed. For `claude-haiku-5.5` the service reported a `MaxContextWindowTokens` of 1,000,000, a
`MaxOutputTokens` of 128,000, a standard prompt budget (`TokenPrices.MaxPromptTokens`) of 100,000
and a long-context prompt budget of 936,000. The catalog's tier windows are therefore 228,000 for
`default` and 1,000,000 for `long_context`, and the model's `ContextWindowTokens` is 1,000,000.

A live session created in the `default` tier reported a usage token limit of 100,000; one created in
`long_context` reported 872,000, which is 1,000,000 less the 128,000 output limit. Two conclusions
follow:

- `MaxContextWindowTokens` is the model's maximum total window, which is the long-context tier's,
  so it is reported verbatim as the model's ceiling.
- The limit a session accounts against is the prompt limit the runtime reports, which is lower than
  the catalog's tier window. That difference is safe: a selection's window is only a ceiling, and a
  ceiling can only ever lower the window a session accounts against, never raise it. Window
  accounting therefore stays on the runtime's usage events, as *CopilotProviderSession Unit Design*
  describes, and the tier window serves only to bound what a user may state.

### Data Model

- **`ProviderId`** (`const string`, `copilot`) — the provider name every described model carries,
  and the name a selection is checked against by the Copilot factories.
- **`AutoModelId`** (`const string`, `auto`) — the automatic choice, which is skipped.
- **`DefaultTier`** and **`LongContextTier`** (`const string`, `default` and `long_context`) — the
  two tiers derived from billing.
- **`SignInMessage`** (`const string`) — "The Copilot runtime could not list models: it is not
  connected or not signed in to GitHub. Sign it in with a GitHub token (CopilotRuntimeOptions'
  gitHubToken or gitHubTokenProvider, COPILOT_GITHUB_TOKEN, GH_TOKEN or GITHUB_TOKEN), 'copilot
  login' or 'gh auth login', with CopilotRuntimeOptions' gitHubHost for a GitHub Enterprise Cloud
  account."
- **`_read`** (`Func<CancellationToken, Task<IList<Rpc.Model>>>`) — reads the service's listing.
  Built over the runtime in production, and supplied directly by the test seam.

Each described `ModelInfo` is filled as follows:

| Field | Value |
| --- | --- |
| `Provider` | `copilot` |
| `Id` | The service's identifier |
| `DisplayName` | The service's name, or the identifier when the name is blank |
| `Vendor` | The service's vendor, or null when blank |
| `Capabilities` | `ToolCalls` unless denied; `Vision` when reported; `Reasoning` as above |
| `Adjustments` | `ContextWindow`; plus `ReasoningEffort` with efforts and `ContextTier` with tiers |
| `IsUsable`, `UnusableReason` | From the policy, then the tool-calling flag; see the table below |
| `ContextWindowTokens` | `Limits.MaxContextWindowTokens`, verbatim |
| `MaxOutputTokens` | `Limits.MaxOutputTokens` |
| `ReasoningEfforts` | The service's efforts, distinct ignoring case, in its order |
| `DefaultReasoningEffort` | The service's default when it is one of the efforts; else null |
| `ContextTiers` | Billing tiers, then declared tiers not already present |
| `DefaultContextTier` | `default` when that tier is offered; else null |
| `BillingMultiplier` | `Billing.Multiplier` |
| `Category`, `PriceCategory` | The service's picker category and price category |
| `Notices` | See below |
| `AdditionalProperties` | See below |

The output limit is reported but not adjustable: `MaxOutputTokens` is not among the adjustments, so
a stated output limit is refused by validation. A token count the service reports as missing or not
positive is treated as unknown, and one above the largest `int` is clamped. A tier window is unknown
when its prompt budget or the model's output limit is.

Efforts and their provider-neutral equivalents, matched ignoring case:

| Service id | Display name | Standard |
| --- | --- | --- |
| `none` | None | `ReasoningEffort.None` |
| `minimal` | Minimal | none |
| `low` | Low | `ReasoningEffort.Low` |
| `medium` | Medium | `ReasoningEffort.Medium` |
| `high` | High | `ReasoningEffort.High` |
| `xhigh` | Extra high | `ReasoningEffort.ExtraHigh` |
| `max` | Maximum | none |
| any other | The identifier | none |

Usability, decided in this order:

| Condition | Reason |
| --- | --- |
| Policy `disabled` | "Disabled by your organization's Copilot policy." |
| Policy `unconfigured` | "Not enabled for your account; enable it in GitHub Copilot settings." |
| Any other policy state but `enabled` | "Not available under your Copilot policy ({state})." |
| Policy enabled or absent, tool calling reported false | "Model does not support tool calling." |
| Otherwise | Usable |

Notices, in this order: each information message as `Info` and each warning message as `Warning`,
with the service's code, skipping blank messages; the data-retention text as `Required` with code
`data_retention`; and, for an unconfigured policy with terms, the terms as `Info` with code
`policy_terms`.

Additional properties, written where the service reports them: `max_prompt_tokens`,
`vision_max_prompt_images`, `vision_max_prompt_image_size`, `vision_supported_media_types` and
`policy_terms`. Every value is immutable: numbers and strings, and the media types as an
`IReadOnlyList<string>` wrapping a fresh array, so nothing reachable from a cached description can be
altered.

### Key Methods

#### CopilotModelCatalog(CopilotRuntime runtime, TimeSpan? lifetime, TimeProvider? timeProvider)

**Purpose:** Create a catalog over a runtime AgentKit started. Reads nothing; see *ModelCatalog Unit
Design* for the lifetime and the clock.

**Algorithm:** Reject a null runtime, check it is not disposed (`CopilotRuntime.ThrowIfDisposed`),
and build the production read: at each read, check the runtime again, read its `Client`, call
`Rpc.Models.ListAsync` and return its `Models`, or an empty list when there are none. Any failure
from the client becomes `ExplainReadFailure` of it and the runtime's `SignIn`, thrown in its place —
or rethrown with its own stack when it is returned unchanged. An `InvalidOperationException` other
than `ObjectDisposedException` becomes `ExplainListFailure` of it: an `InvalidOperationException`
carrying `SignInMessage`, whose cause is a redacted copy of the SDK's failure
(`CopilotSignIn.RedactFailure`: its type name, message and stack trace, with every token the
runtime's sign-in knows removed), never the original. The SDK raises it for a client that is not
connected or not authenticated, and a runtime AgentKit started was already refused at start if it
was not signed in.

**Preconditions:** `runtime` is not null and not disposed; `lifetime`, when supplied, is positive.

**Postconditions:** The catalog never disposes the runtime.

#### ExplainListFailure(InvalidOperationException failure, CopilotSignInPlan? signIn)

**Purpose:** Build the failure a read the client cannot make fails with. Internal, so the redaction
can be tested without a live service.

**Algorithm:** Return a new `InvalidOperationException` with `SignInMessage` and
`CopilotSignIn.RedactFailure(failure, signIn)` as its inner exception.

#### ExplainReadFailure(Exception failure, CopilotSignInPlan? signIn)

**Purpose:** Build the failure a read fails with in place of the client's own. Internal, so every
branch can be tested without a live service.

**Algorithm:** An `InvalidOperationException` that is not an `ObjectDisposedException` goes to
`ExplainListFailure`. Anything else goes to `CopilotSignIn.RedactRelayed`: a cancellation or disposal
carrying nothing to redact is returned unchanged, and any other failure — such as an `IOException`
the SDK raises for a lost connection, whose message quotes the runtime's standard error, or an RPC
error carrying the service's data — becomes a new exception of the same kind (a type outside the base
class library becoming `InvalidOperationException`) with a redacted message and a redacted copy as its
cause. The client's own exception is never kept.

#### CopilotModelCatalog(Func&lt;CancellationToken, Task&lt;IList&lt;Rpc.Model&gt;&gt;&gt;, TimeSpan?, TimeProvider?)

**Purpose:** The internal test seam. The SDK's model query is a sealed class with no public
constructor, so the mapping and caching could not be exercised without a live service unless the
read were injectable.

**Preconditions:** `read` is not null.

#### FetchAsync(CancellationToken cancellationToken)

**Purpose:** Read the listing and describe every model in it.

**Algorithm:** Call the read, treating a null result as empty. Drop null entries and entries with a
blank identifier, skip the automatic choice (`auto`, ignoring case), and map the rest, keeping the
service's order. Performs network I/O.

#### Map(Rpc.Model model)

**Purpose:** Describe one model. Internal and pure, so every mapping rule can be exercised on
hand-built SDK models.

**Algorithm:** Build the efforts, read the largest window and the output limit, and derive the
tiers; set the capability flags; set the adjustments; decide usability; collect notices and
additional properties — each as the tables above give.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `runtime` or `read` | `ArgumentNullException` propagates |
| Runtime disposed before construction | `ObjectDisposedException` propagates |
| Runtime disposed after construction | `ObjectDisposedException` from the next read; nothing restarted |
| Client not connected or not signed in | `InvalidOperationException`, `SignInMessage`; redacted cause; not cached |
| Cancellation or disposal carrying nothing to redact | Propagates unchanged; not cached |
| Any other failure | Relayed as a redacted copy of the same kind (`ExplainReadFailure`); not cached |
| Zero or negative lifetime | `ArgumentOutOfRangeException` propagates |
| A null or unnamed entry in the listing | Skipped |

### Dependencies

- **ModelCatalog** — supplies `CachingModelCatalog`, the base this class derives from; see
  *ModelCatalog Unit Design*.
- **ModelInfo** — supplies `ModelInfo`, `ModelCapabilities`, `ModelAdjustments`,
  `ModelReasoningEffort`, `ModelContextTier`, `ModelNotice` and `ModelNoticeKind`; see *ModelInfo
  Unit Design*.
- **CopilotRuntime** — supplies the client the listing is read through, its sign-in, and
  `ThrowIfDisposed`; see *CopilotRuntime Unit Design*.
- **CopilotSignIn** — makes the redacted copy of the client's failure kept as the cause
  (`RedactFailure`), and relays any other read failure redacted (`RedactRelayed`); see *CopilotSignIn
  Unit Design*.
- **Microsoft.Copilot.SDK** — supplies `CopilotClient` and the `Rpc` model types; see
  *Microsoft.Copilot.SDK Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies `ReasoningEffort` and
  `AdditionalPropertiesDictionary`; see *Microsoft.Extensions.AI.Abstractions Design*.

### Callers

- **CopilotAgentFactory**, **CopilotProviderSessionFactory** and **CopilotSummarizer** — check that a
  selection names a model from `ProviderId` before applying it; see their unit designs.
- **Applications** — construct one over the runtime they started, list models for a picker, and call
  `SelectAsync` when a session starts. The document-assistant sample is the worked example.
