## ChatClientProviderSessionFactory

![AgentKit ChatClient Agents Structure](AgentKitAgentsChatClientView.svg)

The `ChatClientProviderSessionFactory` class creates `ChatClientProviderSession` instances, once at
the start of a conversation and again at every rotation.

### Purpose

`ChatClientProviderSessionFactory` holds the two facts a rotating session must not have to ask for
again: the chat client the application configured, and the context window that client's provider
has. An application states both once, where it configures its provider, and every session the
compaction engine is later handed answers against the same window over the same client.

Passing the window here is what lets the session engine ask one question — how full, out of how much
— and believe the answer. Read it from the provider wherever the provider will say: Ollama publishes
the loaded model's context length, an application that sets the context size itself already knows
the number it chose, and for a hosted model the window is a published property of the model the
application selected.

**It also owns the pipeline each session runs on, and that is the point of it.** An application hands
over the client it talks to its provider with; this factory wraps that client in a
`PromptSizeRecordingChatClient`, and wraps *that* in a `FunctionInvokingChatClient`. Both placements
matter, and getting either wrong is silent:

- **The recorder must sit underneath.** A turn that calls tools makes several requests, and the
  response the tool-calling loop finally returns carries usage summed across all of them. Read as
  occupancy that figure would have a tool-using agent — which is every agent this library exists for
  — believe its window was full on its first turn. Underneath the loop each request is seen
  separately, and the last one is the conversation. See *PromptSizeRecordingChatClient Unit Design*.
- **The tool-calling loop must sit above.** A session seeds its tools into every request it sends, so
  a bare client will emit tool calls that nothing answers: the model waits for a result that never
  comes, and the transcript records a call with no result beside it.

An application asked to compose that itself would sometimes compose something that looks right and
is wrong, with no error to notice. Owning both placements here is what makes that impossible, and is
why `ChatClientProviderSession`'s constructor is internal.

The class implements `IProviderSessionFactory` and is safe for concurrent use, as that contract
requires: creating a session reads the client reference and the window, and builds a pipeline of the
session's own.

### Data Model

- **`_client`** (`IChatClient`) — The client the application talks to its provider with, which every
  pipeline built here wraps. Owned by the application: this factory never disposes it, and neither
  does any session it creates, because the client is shared by all of them and outlives the
  conversation.
- **`WindowTokens`** (`int`) — The provider's context window in tokens, as supplied at construction
  and passed to every created session. Invariant: positive.

The factory holds no pipeline of its own, deliberately. A recorder shared between sessions would hand
a replacement the figure its predecessor left behind — so a session that had sent nothing would
report a full window and rotate again immediately — and two sessions run at once would overwrite each
other's reading, which the concurrency this contract promises does not allow.

### Key Methods

#### The ChatClientProviderSessionFactory Constructor (IChatClient client, int windowTokens)

**Purpose:** Record the client and the window every created session will carry.

**Algorithm:** Reject a null client and a window that is not positive, then record both.

This is where the application's provider configuration is checked, because it is where the
application wrote it. The session it creates cannot be constructed from outside this class, so these
are the only checks an application can trip.

**Preconditions:** `client` is not null; `windowTokens` is positive.

**Postconditions:** The factory is ready to create sessions and reports the window it was given.

#### CreateAsync(ProviderSessionSeed seed, CancellationToken cancellationToken)

**Purpose:** Create a session already holding the seeded instructions, tools and history, on a
pipeline of its own.

**Algorithm:** Reject a null seed and a canceled token. Build a `PromptSizeRecordingChatClient`
around the held client and a `FunctionInvokingChatClient` around that, then construct a
`ChatClientProviderSession` over the pipeline, the recorder within it, the supplied seed and the held
window, and return it as an `IProviderSession`. The work is synchronous — a stateless provider is not
contacted to start a session — so the result is returned as an already-completed task rather than by
awaiting anything.

The pipeline is built per call rather than once, for the reason recorded under the data model: the
recorded prompt size belongs to one conversation.

**Preconditions:** `seed` is not null; cancellation has not been requested.

**Postconditions:** A session that already holds the seeded history, occupies nothing until it has
sent something, and invokes the tools its seed carried. Ownership passes to the caller, which
disposes it at the next rotation.

### Error Handling

| Condition                        | Handling                                 |
|----------------------------------|------------------------------------------|
| Null `client`                    | `ArgumentNullException` propagates       |
| `windowTokens` not positive      | `ArgumentOutOfRangeException` propagates |
| Null `seed`                      | `ArgumentNullException` propagates       |
| Cancellation before creation     | `OperationCanceledException` propagates  |

Every condition is a defect in the composing application or a cancellation the caller asked for, so
each is surfaced where it arose. A canceled creation yields no session, so a canceled rotation leaves
nothing unowned.

### Dependencies

- **AgentKitCore** — supplies `IProviderSessionFactory`, `IProviderSession` and
  `ProviderSessionSeed`; see *ProviderSession Unit Design*.
- **ChatClientProviderSession** — the session this factory creates; see *ChatClientProviderSession
  Unit Design*.
- **PromptSizeRecordingChatClient** — the layer this factory installs beneath the tool-calling loop;
  see *PromptSizeRecordingChatClient Unit Design*.
- **Microsoft.Agents.AI** — brings the chat-client extensions supplying `FunctionInvokingChatClient`,
  the tool-calling loop this factory installs above the recorder; see *Microsoft.Agents.AI Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies `IChatClient`; see
  *Microsoft.Extensions.AI.Abstractions Design*.

### Callers

An application constructs one and hands it to `CompactingAgentSession`, which calls `CreateAsync`
once at creation and again at every rotation; see *CompactingAgentSession Unit Design*. Within this
system nothing else calls it.
