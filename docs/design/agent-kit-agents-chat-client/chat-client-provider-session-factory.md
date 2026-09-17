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

The class implements `IProviderSessionFactory` and is safe for concurrent use, as that contract
requires: creating a session touches nothing this factory owns beyond reading the client reference
and the window.

### Data Model

- **`_client`** (`IChatClient`) — The client every created session carries its turns on. Owned by
  the application: this factory never disposes it, and neither does any session it creates, because
  the client is shared by all of them and outlives the conversation.
- **`WindowTokens`** (`int`) — The provider's context window in tokens, as supplied at construction
  and passed to every created session. Invariant: positive.

### Key Methods

#### The ChatClientProviderSessionFactory Constructor (IChatClient client, int windowTokens)

**Purpose:** Record the client and the window every created session will carry.

**Algorithm:** Reject a null client and a window that is not positive, then record both.

**Preconditions:** `client` is not null; `windowTokens` is positive.

**Postconditions:** The factory is ready to create sessions and reports the window it was given.

#### CreateAsync(ProviderSessionSeed seed, CancellationToken cancellationToken)

**Purpose:** Create a session already holding the seeded instructions, tools and history.

**Algorithm:** Reject a null seed and a canceled token, then construct a `ChatClientProviderSession`
over the held client, the supplied seed and the held window, and return it as an `IProviderSession`.
The work is synchronous — a stateless provider is not contacted to start a session — so the result
is returned as an already-completed task rather than by awaiting anything.

**Preconditions:** `seed` is not null; cancellation has not been requested.

**Postconditions:** A session that already holds the seeded history, so the first message sent to it
continues the conversation rather than starting one. Ownership passes to the caller, which disposes
it at the next rotation.

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
  `ProviderSessionSeed`; see _ProviderSession Unit Design_.
- **ChatClientProviderSession** — the session this factory creates; see _ChatClientProviderSession
  Unit Design_.
- **Microsoft.Extensions.AI.Abstractions** — supplies `IChatClient`; see
  _Microsoft.Extensions.AI.Abstractions Design_.

### Callers

An application constructs one and hands it to `CompactingAgentSession`, which calls `CreateAsync`
once at creation and again at every rotation; see _CompactingAgentSession Unit Design_. Within this
system nothing else calls it.
