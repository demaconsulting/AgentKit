## OllamaContextSizingChatClient

![AgentKit Ollama Agents Structure](AgentKitAgentsOllamaView.svg)

The `OllamaContextSizingChatClient` decorator asks an Ollama server to run the model at a context
length the application chose, by naming that length on every request it forwards.

### Purpose

An Ollama instance runs at a context length the server decides when it loads the model, not at the
maximum the model file publishes. Before this unit existed, an application could only guess what
that length was and hope; with it, the application can ask for one and then account against what it
asked for.

The promise is narrow and load-bearing: **every** request names the length. Ollama reloads a model
when a request names a different context length, so a single un-annotated request would silently
resize the instance beneath a session still accounting against the old figure — a failure that
reports nothing and cannot be detected afterwards. A decorator that annotated only the opening
request would therefore be worse than none, because the mistake would be invisible.

Everything else the caller set is left alone. The decorator adds one option and changes nothing
else; a context length the caller named on a particular request is forwarded as the caller named it,
because that caller has already decided and is the only party that can know why.

The decorator writes to a copy rather than to the caller's own request settings. One settings object
is commonly built once and reused for every request of a session, and shared with sibling clients;
writing into it would leak this unit's choice outward to clients that were never meant to carry it.

This unit performs no I/O and knows nothing about the window a session accounts against. The
connection between the two is made by the application, which hands one integer to both this
decorator and the reading; see *OllamaContextWindow Unit Design*.

### Data Model

- **`_contextLength`** (`private readonly int`) — the context length every forwarded request asks
  for. Invariant: positive, enforced at construction.
- **`ContextLengthOption`** (`private const string`, `num_ctx`) — the Ollama option naming the
  context length a model is loaded with. Carried as an additional request property because it is
  Ollama's own option rather than one the provider-neutral chat abstraction models.

The decorator holds no mutable state and is as safe for concurrent use as the client it wraps.

### Key Methods

#### OllamaContextSizingChatClient(IChatClient innerClient, int contextLength)

**Purpose:** Compose the decorator over the client the application already owns.

**Algorithm:** Delegate the inner client to the base, which rejects a null one, then reject a
context length of zero or less.

**Preconditions:** `innerClient` is not null; `contextLength` is greater than zero.

**Postconditions:** A decorator that forwards to `innerClient` and owns it for disposal, as the
delegating base defines.

#### GetResponseAsync(IEnumerable&lt;ChatMessage&gt; messages, ChatOptions? options, CancellationToken cancellationToken)

**Purpose:** Forward a whole-response request, having asked for the chosen context length.

**Algorithm:** Rewrite the request settings and forward everything else unchanged.

**Postconditions:** The forwarded request names the context length, unless the caller named one
itself.

#### GetStreamingResponseAsync(IEnumerable&lt;ChatMessage&gt; messages, ChatOptions? options, CancellationToken cancellationToken)

**Purpose:** Forward a streaming request, having asked for the chosen context length.

**Algorithm:** Identical to the whole-response path. The two must not differ: the difference between
them is how the answer is delivered, not what the request asks for, and a control that held on one
path and not the other would leave a streaming host silently unprotected.

#### WithContextLength(ChatOptions? options)

**Purpose:** Produce the request settings to forward, carrying the context length.

**Algorithm:** Return the caller's settings untouched when they already name the context length.
Otherwise clone them — or create empty settings when the caller supplied none — and set the context
length on the copy.

**Postconditions:** Never null. Never the caller's own instance except in the case where the caller
had already named the context length, in which case nothing needed changing.

### Error Handling

| Condition | Handling |
| ----------- | ---------- |
| Null inner client | `ArgumentNullException` from the delegating base, at composition |
| Context length of zero or less | `ArgumentOutOfRangeException`, at composition |
| Caller already named the context length | Forwarded unchanged; not an error |
| The provider rejects the request | Propagates unchanged; this unit interprets no response |

Nothing is caught here. A decorator that swallowed a provider failure would hide the very condition
an application needs to see, and this unit has no fallback of its own to offer.

### Dependencies

- **Microsoft.Extensions.AI.Abstractions** — supplies `IChatClient`, `ChatOptions` and the
  delegating chat-client base this unit extends.
- **OllamaSharp** — not called by this unit, which names the option and forwards; it is what
  translates that option into the Ollama request's own options block on the wire, which is why the
  unit's tests compose a real client over a loopback server to read it; see *OllamaSharp Design*.

### Callers

An application composes this decorator around its Ollama client where it configures its provider,
using the window `OllamaContextWindow.ReadAsync` returned — whichever rung produced it, not only a
figure the application stated. Every client that talks to the conversation's model is composed this
way, including a summarizer sharing that model, because an un-annotated request from any of them
resizes the same instance. The research-assistant sample is the worked example. Nothing within this
system calls this unit.
