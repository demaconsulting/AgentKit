## OllamaContextWindow

![AgentKit Ollama Agents Structure](AgentKitAgentsOllamaView.svg)

The `OllamaContextWindow` record carries the context window a conversation on an Ollama model should
be accounted against, together with the `OllamaContextWindowSource` that says where the figure came
from, and holds the reading and the precedence that produce it.

### Purpose

A compacting session is told its window once, where the provider is configured, and answers with it
thereafter. This unit is where an application gets that number for Ollama.

Only the running instance can answer the question. A model file publishes the context length it
could be loaded with, but Ollama decides at load time what the instance will actually use, and that
decision is the one enforced. The published figure is therefore never consulted: it describes the
file, and using it would account a session against a window nothing guarantees. What remains is a
window the application stated — which it also asked the server to run at, so it is true by
construction — then the length the running instance reports, then a conservative default named as an
assumption.

A stated window short-circuits the reading entirely. That is deliberate twice over: asking the
server anyway would invite a reader to wonder which answer won, and asking what a model would be
loaded at is not free of consequence, so discovery must not change the thing it was asked to
observe.

Reading and choosing are separate on purpose. `ReadAsync` does the I/O and tolerates the query
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

- **`Tokens`** (`int`) — the window the conversation is accounted against. Invariant: positive,
  enforced at construction so it holds for every instance that can exist rather than only for the
  ones this unit produced.
- **`Source`** (`OllamaContextWindowSource`) — where that figure came from. Invariant: one of the
  three members below.
- **`AssumedTokens`** (`const int`, 4096) — Ollama's own default context length, the conservative
  fallback. Published rather than private so a caller can detect "nothing was discovered" without
  matching on text.

`OllamaContextWindowSource` has three members, and three only — what Ollama discovery can honestly
produce:

| Member | Meaning |
| -------- | --------- |
| `Stated` | The application supplied it, and asked the server to run at it. |
| `LoadedModel` | Read from the running instance; this is what the server will enforce. |
| `Assumed` | No instance was described; Ollama's own default is assumed. |

The record is immutable, holds no reference to a client, and is safe for concurrent use.

### Key Methods

#### ReadAsync(IOllamaApiClient client, string model, int? stated, CancellationToken cancellationToken)

**Purpose:** Obtain the window from a server, preferring what was stated and then what the running
instance reports.

**Algorithm:** Reject a null client and a null model. If a window was stated, return it immediately
without contacting the server. Otherwise attempt the running-models query through a helper that
converts any failure other than cancellation into "the server did not say", and hand the result to
`Select`.

**Preconditions:** `client` and `model` are not null. `stated` is either null or the window to use;
a value of zero or less is treated as unstated.

**Postconditions:** A window with a positive token count, never null. The client is not disposed.
No model is loaded as a side effect of asking.

**Side effects:** Network I/O against the Ollama server, through the supplied client, only when no
window was stated.

#### Select(int? stated, IEnumerable&lt;RunningModel&gt;? running, string model)

**Purpose:** Choose the window from what was stated and what the server reported.

**Algorithm:** Reject a null model. Take a stated window if there is one. Otherwise take the named
model's loaded context length if it is loaded, then `AssumedTokens`. Each branch reports its own
source.

**Preconditions:** `model` is not null. `running` may be null, meaning the query produced nothing.

**Postconditions:** A window with a positive token count and the matching source, never null.
Contacts nothing; holds no state.

#### FromLoadedModel(IEnumerable&lt;RunningModel&gt;? running, string model)

**Purpose:** Find the context length the named model is currently loaded with.

**Algorithm:** Compare both names the server reports for each loaded model against the wanted name,
each side first given an explicit tag so a bare name and the `latest` tag the server resolved it to
compare equal. Return the matched model's context length, or zero.

**Postconditions:** Zero when the named model is not loaded — never another model's length.

#### Tagged(string model)

**Purpose:** Give a model name an explicit tag so two spellings of one model compare equal.

**Algorithm:** Return the name unchanged when it already carries a tag, otherwise append `:latest`.

#### TryReadAsync&lt;T&gt;(Func&lt;Task&lt;T&gt;&gt; query, CancellationToken cancellationToken)

**Purpose:** Run an optional server query, treating failure as "the server did not say".

**Algorithm:** Await the query. Re-raise `OperationCanceledException` when the caller's token was
canceled; return null for anything else. The question is asked of the token rather than of the
exception type, because an `HttpClient` timeout also arrives as an `OperationCanceledException` and
a server that never answers is precisely the case this tolerance exists for.

### Error Handling

| Condition | Handling |
| ----------- | ---------- |
| Null or empty `client` or `model` | `ArgumentException` family propagates to the caller |
| Construction with a window of zero or less | `ArgumentOutOfRangeException` propagates to the caller |
| The server query fails | Treated as "not reported"; the conservative default is used |
| The server never answers | The client's own timeout ends the query; treated as "not reported" |
| Cancellation during the query | `OperationCanceledException` propagates; no window is returned |
| The named model is not loaded | `AssumedTokens`, sourced as `Assumed` |

A query failure is deliberately not surfaced. Which way it failed — an older server, a proxy, a
model never pulled — changes nothing the caller can act on, and the conversation proceeds on a
conservative window that names itself honestly.

### Dependencies

- **OllamaSharp** — supplies `IOllamaApiClient` and `RunningModel`; see *OllamaSharp Design*.

### Callers

An application calls `ReadAsync` where it configures its Ollama provider, and hands `Tokens` to
`ChatClientProviderSessionFactory`; see *ChatClientProviderSessionFactory Unit Design*. It composes
that same figure onto its chat client through `OllamaContextSizingChatClient` whichever rung
produced it — a window that was only read is no more durable than one that was only claimed — see
*OllamaContextSizingChatClient Unit Design*.
The research-assistant sample is the worked example. Nothing within this system calls this unit.
