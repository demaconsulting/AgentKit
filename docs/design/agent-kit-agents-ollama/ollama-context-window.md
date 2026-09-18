## OllamaContextWindow

![AgentKit Ollama Agents Structure](AgentKitAgentsOllamaView.svg)

The `OllamaContextWindow` record carries the context window a conversation on an Ollama model should
be accounted against, together with the `OllamaContextWindowSource` that says where the figure came
from, and holds the reading and the precedence that produce it.

### Purpose

A compacting session is told its window once, where the provider is configured, and answers with it
thereafter. This unit is where an application gets that number for Ollama.

Two figures exist and they are not the same figure. A model publishes the context length it was
trained for; Ollama loads it with a context length of its own choosing, which is smaller by default.
The loaded figure is what the server will enforce, so it wins, and the published figure is used only
when nothing is loaded — with the source saying so, rather than a maximum being presented as the
limit in force.

Reading and choosing are separate on purpose. `ReadAsync` does the I/O and tolerates either query
failing; `Select` is a pure function holding the whole precedence. That split is why the precedence
— the part that can be wrong quietly — is testable without a server.

#### The Type Boundary With the Sample's Banner

The research-assistant sample has its own `ContextWindow` and `ContextWindowSource`, and maps this
unit's result onto them. The two are not duplicates. The sample's enum carries a `Ceiling` member
that belongs to its **Copilot** path, and its `Describe` method is banner prose. Publishing either
here would mean shipping a value Ollama discovery can never produce, fixing the sample's wording as
a public API contract, and giving the sample's Copilot path a dependency on the Ollama package. So
this unit publishes only what discovery can report, and presentation stays with the presenter.

### Data Model

- **`Tokens`** (`int`) — the window the conversation is accounted against. Invariant: positive.
- **`Source`** (`OllamaContextWindowSource`) — where that figure came from. Invariant: one of the
  four members below.
- **`AssumedTokens`** (`const int`, 4096) — Ollama's own default context length, the conservative
  fallback. Published rather than private so a caller can detect "nothing was discovered" without
  matching on text.
- **`ContextLengthKeySuffix`** (`private const string`, `.context_length`) — the metadata key
  beneath the architecture name. Ollama keys model metadata by architecture, so the architecture the
  same response names is what makes the key addressable without knowing the model.

`OllamaContextWindowSource` has four members, and four only — what Ollama discovery can actually
produce:

| Member | Meaning |
| -------- | --------- |
| `Stated` | The application supplied it; nothing was measured. |
| `LoadedModel` | Read from the loaded model; this is what the server will enforce. |
| `PublishedModel` | The model's published maximum; the server may have loaded it below this. |
| `Assumed` | Nothing was readable; Ollama's own default is assumed. |

The record is immutable, holds no reference to a client, and is safe for concurrent use.

### Key Methods

#### ReadAsync(IOllamaApiClient client, string model, int? stated, CancellationToken cancellationToken)

**Purpose:** Obtain the window from a server, preferring what was stated and then what the server
reports.

**Algorithm:** Reject a null client and a null model. If a window was stated, return it immediately
without contacting the server — asking anyway would only invite a reader to wonder which answer won.
Otherwise attempt the running-models query and then the model-metadata query, each through a helper
that converts any failure other than cancellation into "the server did not say", and hand both
results to `Select`.

**Preconditions:** `client` and `model` are not null. `stated` is either null or the window to use;
a value of zero or less is treated as unstated.

**Postconditions:** A window with a positive token count, never null. The client is not disposed.

**Side effects:** Network I/O against the Ollama server, through the supplied client.

#### Select(int? stated, IEnumerable&lt;RunningModel&gt;? running, ShowModelResponse? published, string model)

**Purpose:** Choose the window from what was stated and what the server reported.

**Algorithm:** Reject a null model. Take a stated window if there is one. Otherwise take the named
model's loaded context length if it is loaded, then the published maximum if the metadata carries
one, then `AssumedTokens`. Each branch reports its own source.

**Preconditions:** `model` is not null. `running` and `published` may each be null, meaning that
query produced nothing.

**Postconditions:** A window with a positive token count and the matching source, never null.
Contacts nothing; holds no state.

#### FromLoadedModel(IEnumerable&lt;RunningModel&gt;? running, string model)

**Purpose:** Find the context length the named model is currently loaded with.

**Algorithm:** Compare both names the server reports for each loaded model against the wanted name,
each side first given an explicit tag so a bare name and the `latest` tag the server resolved it to
compare equal. Return the matched model's context length, or zero.

**Postconditions:** Zero when the named model is not loaded — never another model's length.

#### FromPublishedModel(ShowModelResponse? published)

**Purpose:** Read the published context length out of a model's metadata.

**Algorithm:** Take the architecture the metadata names, look up that architecture plus
`ContextLengthKeySuffix` in the extra information, and convert the value found. Missing metadata, a
missing architecture, or a missing key all yield zero.

**Postconditions:** Zero when no published length could be read.

#### AsTokenCount(object? value)

**Purpose:** Convert a loosely typed metadata value into a token count.

**Algorithm:** Accept a `JsonElement` holding a number, an `int`, an in-range `long` or `double`, or
parsable text. Anything else yields zero. Converting rather than casting is what keeps this from
becoming a silent zero whenever a serializer materializes the value differently.

#### Tagged(string model)

**Purpose:** Give a model name an explicit tag so two spellings of one model compare equal.

**Algorithm:** Return the name unchanged when it already carries a tag, otherwise append `:latest`.

#### TryReadAsync&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt; query)

**Purpose:** Run an optional server query, treating failure as "the server did not say".

**Algorithm:** Await the query. Re-raise `OperationCanceledException`; return null for anything
else. Cancellation is deliberately not swallowed, because a canceled call must stop rather than
quietly continue on an assumed window.

### Error Handling

| Condition | Handling |
| ----------- | ---------- |
| Null `client` or `model` | `ArgumentNullException` propagates to the caller |
| Either server query fails | Treated as "not reported"; the next source down is used |
| Cancellation during a query | `OperationCanceledException` propagates; no window is returned |
| Metadata carries no context length | Falls through to the next source |
| Metadata value is not a usable number | Falls through to the next source |
| Nothing is readable at all | `AssumedTokens`, sourced as `Assumed` |

A query failure is deliberately not surfaced. Which way it failed — an older server, a proxy, a
model never pulled — changes nothing the caller can act on, and the conversation proceeds on a
window from a lower-precedence source that names itself honestly.

### Dependencies

- **OllamaSharp** — supplies `IOllamaApiClient`, `RunningModel`, `ShowModelRequest`,
  `ShowModelResponse` and `ModelInfo`; see *OllamaSharp Design*.
- **System.Text.Json** — supplies `JsonElement`, one of the shapes a metadata value arrives in.

### Callers

An application calls `ReadAsync` where it configures its Ollama provider, and hands `Tokens` to
`ChatClientProviderSessionFactory`; see *ChatClientProviderSessionFactory Unit Design*. The
research-assistant sample is the worked example. Nothing within this system calls it: the system is
this one unit.
