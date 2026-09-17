## ChatClientProviderSession

![AgentKit ChatClient Agents Structure](AgentKitAgentsChatClientView.svg)

The `ChatClientProviderSession` class carries one AgentKit Core session over any
`Microsoft.Extensions.AI` `IChatClient`, for providers that hold no conversation of their own.

### Purpose

`ChatClientProviderSession` is the one implementation serving the whole stateless provider family.
Ollama, OpenAI and AI Foundry all reach this library as an `IChatClient`, and none of them keeps the
conversation: every turn sends the whole message list again. Seeding a session is therefore starting
a list, and releasing one is forgetting it. Writing that once means the mapping between this
library's transcript entries and chat messages — the part most likely to rot, because tool-calling
shapes differ between providers — exists in a single place.

It is also where the session engine's one token question is answered. The engine holds no token
arithmetic: it asks how full the session is and out of how much, and believes the answer. This class
answers with the input-token count the provider itself reported for the request it last answered,
taken against the window it was given at construction.

The class implements `IProviderSession`. Instances are not safe for concurrent use, consistent with
that contract: one session serves one conversation, and turns within a conversation are sequential
by nature.

### Data Model

- **`_client`** (`IChatClient`) — The client carrying each turn. Supplied by the application, never
  disposed by this session, and shared with every other session the same factory creates.
- **`_options`** (`ChatOptions?`) — The options every turn is sent with, carrying the seeded tools.
  Null when the seed carried no tools, so a session offering nothing sends no options at all.
- **`_messages`** (`List<ChatMessage>`) — The whole conversation, resent on every turn because the
  provider holds none of it. Invariant: it holds the seeded system message and history from
  construction, and thereafter grows by the message sent and everything the provider produced.
- **`_reportedInputTokens`** (`long?`) — The provider's own count of the input it last answered, or
  null when no turn has been taken. Invariant: set only from a provider's own report, never
  estimated.
- **`WindowTokens`** (`int`) — The provider's context window in tokens, as supplied at construction.
  Invariant: positive.
- **`IsReleased`** (`bool`) — Whether this session has been released. Invariant: once true, never
  false again.

### Key Methods

#### The ChatClientProviderSession Constructor (IChatClient client, ProviderSessionSeed seed, int windowTokens)

**Purpose:** Turn a seed into the message list a stateless provider expects.

**Algorithm:** Reject a null client, a null seed and a window that is not positive. Record the client
and the window. Where the seed carries instructions, add them as a leading system message. Render
each seeded history entry as a chat message, in order. Where the seed carries tools, build the
`ChatOptions` every turn will be sent with.

The window is a parameter rather than a reading, because an `IChatClient` publishes none: the
abstraction exposes a provider name, a provider URI and a default model identifier, and nothing about
limits. One layer down the number is nearly always available — Ollama reports the loaded model's
context length and takes a configured one, and for a hosted model the window is a published property
of the model the application has already chosen — so it is asked for once, where the provider is
configured, rather than guessed at here.

**Preconditions:** `client` and `seed` are not null; `windowTokens` is positive.

**Postconditions:** The session holds the seeded conversation and is ready to take its first turn.
Nothing has been sent to the provider.

#### CurrentUsage

**Purpose:** Answer the engine's one token question for this provider.

**Algorithm:** Where no turn has been taken, report nothing occupied out of the supplied window: a
stateless provider receives the conversation with the request, so a session that has made no request
occupies nothing — including immediately after a rotation, when the replacement holds a seed the
provider has not seen yet. Otherwise report the provider's own input-token count, clamped to the
window, as both the tokens used and the conversation. The clamp exists because a provider that has
already overrun its own window would otherwise produce a figure the usage shape refuses; being at the
limit is the truthful reading in that case, and it is the one that rotates.

The whole reading is attributed to the conversation because a chat client reports no split between
the system prompt, the tool declarations and the exchange. That credits the session with no overhead
allowance, which rotates strictly earlier than a correct split would and so cannot let the session
run past the provider's own compactor.

**Preconditions:** None beyond construction invariants.

**Postconditions:** A `ContextUsage` produced through `ContextUsage.FromProvider`, because that is
exactly what it is: a figure the provider reported. Reading it contacts nothing and cannot fail.

#### SendAsync(string message, CancellationToken cancellationToken)

**Purpose:** Take one turn and record what it produced.

**Algorithm:** Reject a null message, a released session and a canceled token. Append the message to
the conversation and send the whole conversation, with the seeded tool options, to the client. Append
every message the provider produced, so the next turn sends again a history the provider has already
seen. Record the reported input tokens, or refuse the turn where the provider reported none. Map each
produced message to the transcript entries it contributes and return them with the provider's answer.

The usage is recorded before the entries are built, so a usage read between turns reflects the turn
that just happened rather than the one before it.

**Preconditions:** `message` is not null; the session is not released; cancellation has not been
requested.

**Postconditions:** On success, the conversation holds the message and everything the provider
produced, the reported occupancy is the provider's own, and the returned `ProviderTurn` carries the
answer and the entries that led to it.

#### DisposeAsync()

**Purpose:** Release the session without releasing the application's client.

**Algorithm:** Mark the session released and clear the conversation. The client is deliberately not
disposed: it was supplied by the caller, is shared by every session a rotation creates, and outlives
all of them. Repeated disposal is permitted, because a rotation and a disposal may both reach the
same session.

**Preconditions:** None.

**Postconditions:** `IsReleased` is true and later turns are refused.

#### ToChatMessage(TranscriptEntry entry)

**Purpose:** Render one seeded transcript entry as the chat message a provider expects.

**Algorithm:** A user message becomes a user message, a tool result becomes a tool message, and
everything else — an answer, a tool call, a consolidated record — becomes an assistant message. A
tool call and its result are carried as assistant and tool messages rather than as function-call
content, because a seeded history is a record of what happened rather than a live exchange: the call
has already been answered, and replaying it as a pending call invites a provider to answer it again.

**Preconditions:** `entry` is not null, which the seed guarantees.

**Postconditions:** One chat message carrying the entry's text under the role its kind implies.

#### ToTranscriptEntries(ChatMessage message)

**Purpose:** Record what one message the provider produced contributed to the history.

**Algorithm:** Walk the message's content. A function call yields a tool-call entry carrying the
provider's own call identifier and the call rendered by name and arguments; a function result yields
a tool-result entry under the same identifier, so a rotation keeps the pair together. Where the
message carried neither, and has text, it yields the assistant entry it is. Where it carried tool
traffic, no assistant entry is yielded, so an answer delivered alongside a call is not recorded
twice — `ProviderTurn` appends the turn's answer as the final entry regardless.

**Preconditions:** `message` is not null.

**Postconditions:** The entries the message contributes, in order.

#### FormatArguments(IDictionary&lt;string, object?&gt;? arguments)

**Purpose:** Render a tool call's arguments compactly for the transcript.

**Algorithm:** No arguments render as the empty string; otherwise each pair is rendered as its name
and value, joined by commas. The transcript is read by a summarizer rather than executed, so the
arguments are recorded for what they say about the call rather than to be parsed back.

**Preconditions:** None.

**Postconditions:** A single line of rendered arguments.

### Error Handling

| Condition                                  | Handling                                    |
|--------------------------------------------|---------------------------------------------|
| Null `client` or `seed`                    | `ArgumentNullException` propagates          |
| `windowTokens` not positive                | `ArgumentOutOfRangeException` propagates    |
| Null `message`                             | `ArgumentNullException` propagates          |
| Turn on a released session                 | `ObjectDisposedException` propagates        |
| Cancellation before the turn               | `OperationCanceledException` propagates     |
| Provider answered without reporting usage  | `InvalidOperationException` propagates      |

The refusal of an answer carrying no usage is the one condition here that is not a programming error,
and it is deliberately not softened: knowing when the window is filling is the one thing this library
needs a token count for, so an absent figure means something the application can see and fix. The
message names the missing fact and the two remedies — use a chat client that reports usage, or wrap
one in an implementation that does.

### Dependencies

- **AgentKitCore** — supplies `IProviderSession`, `ProviderSessionSeed`, `ProviderTurn`,
  `TranscriptEntry` and `ContextUsage`; see _ProviderSession Unit Design_ and _ContextUsage Unit
  Design_.
- **Microsoft.Extensions.AI.Abstractions** — supplies `IChatClient`, `ChatMessage`, `ChatOptions`,
  `ChatResponse`, `FunctionCallContent` and `FunctionResultContent`; see
  _Microsoft.Extensions.AI.Abstractions Design_.

### Callers

`ChatClientProviderSessionFactory` constructs one at the start of a conversation and again at every
rotation; see _ChatClientProviderSessionFactory Unit Design_. An application may construct one
directly for a session it drives itself. Within this system nothing else calls it.
