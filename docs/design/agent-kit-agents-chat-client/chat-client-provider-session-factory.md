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
`PromptSizeRecordingChatClient`, wraps *that* in an `ImagePromotingChatClient`, and wraps *that* in a
`FunctionInvokingChatClient`. All three placements matter, and getting any of them wrong is silent:

- **The recorder must sit underneath.** A turn that calls tools makes several requests, and the
  response the tool-calling loop finally returns carries usage summed across all of them. Read as
  occupancy that figure would have a tool-using agent — which is every agent this library exists for
  — believe its window was full on its first turn. Underneath the loop each request is seen
  separately, and the last one is the conversation. See *PromptSizeRecordingChatClient Unit Design*.
- **The image promoter must sit between the two.** Beneath the function-invocation loop, because that
  is the only place a tool result exists for it to observe and promote; above the recorder, so a
  message it promoted is counted in the occupancy the recorder reads. A provider whose tool-result
  channel cannot carry an image drops one silently and the model answers anyway, and a session is as
  exposed to that as an agent is — so the decorator is installed unconditionally, for the same reason
  `ChatClientAgentFactory` installs it. See *ImagePromotingChatClient Unit Design*.
- **The tool-calling loop must sit above.** A session seeds its tools into every request it sends, so
  a bare client will emit tool calls that nothing answers: the model waits for a result that never
  comes, and the transcript records a call with no result beside it.

An application asked to compose that itself would sometimes compose something that looks right and
is wrong, with no error to notice. Owning all three placements here is what makes that impossible,
and is why `ChatClientProviderSession`'s constructor is internal.

The class implements `IProviderSessionFactory` and is safe for concurrent use, as that contract
requires: creating a session reads the client reference and the window, and builds a pipeline of the
session's own.

### Data Model

- **`_client`** (`IChatClient`) — The client the application talks to its provider with, which every
  pipeline built here wraps — itself wrapped once, at construction, in Core's `ModelSettingsChatClient`
  when a selection was supplied. Owned by the application: this factory never disposes it, and neither
  does any session it creates, because the client is shared by all of them and outlives the
  conversation. The settings decorator is never disposed either, because disposing it would dispose
  the application's client, and it holds nothing of its own.
- **`WindowTokens`** (`int`) — The provider's context window in tokens, as supplied at construction
  and passed to every created session. Invariant: positive.

The factory holds no pipeline of its own, deliberately. A recorder shared between sessions would hand
a replacement the figure its predecessor left behind — so a session that had sent nothing would
report a full window and rotate again immediately — and two sessions run at once would overwrite each
other's reading, which the concurrency this contract promises does not allow.

### Key Methods

#### The ChatClientProviderSessionFactory Constructor (IChatClient client, int windowTokens, ModelSelection? selection)

**Purpose:** Record the client and the window every created session will carry, and apply the
user's model selection to that client when one is supplied.

**Algorithm:** Reject a null client and a window that is not positive. When `selection` is supplied,
wrap the client once in `new ModelSettingsChatClient(client, selection)`, directly over the supplied
client and so beneath the recorder of every pipeline built later; then record the client and the
window.

The window stays explicit even with a selection. The figure a session accounts against is the
provider's to report and the application's to state, not the model's largest window, so it is never
taken from the selection.

Wrapping here rather than per session costs nothing, because the decorator holds no per-conversation
state, and it refuses a selection the client cannot apply where the host composed it: a context
tier, or an effort with no provider-neutral equivalent, raises `ArgumentException` from the
constructor.

This is where the application's provider configuration is checked, because it is where the
application wrote it. The session it creates cannot be constructed from outside this class, so these
are the only checks an application can trip.

**Preconditions:** `client` is not null; `windowTokens` is positive; `selection`, when supplied,
carries no context tier and no effort without a provider-neutral equivalent.

**Postconditions:** The factory is ready to create sessions and reports the window it was given.

#### CreateAsync(ProviderSessionSeed seed, CancellationToken cancellationToken)

**Purpose:** Create a session already holding the seeded instructions, tools and history, on a
pipeline of its own.

**Algorithm:** Reject a null seed and a canceled token. Build a `PromptSizeRecordingChatClient`
around the held client, an `ImagePromotingChatClient` around that, and a `FunctionInvokingChatClient`
around that, then construct a `ChatClientProviderSession` over the pipeline, the recorder within it,
the supplied seed and the held window, and return it as an `IProviderSession`. The work is
synchronous — a stateless provider is not contacted to start a session — so the result is returned as
an already-completed task rather than by awaiting anything.

The image promoter is installed unconditionally rather than on a capability check, because there is
no capability to check: a provider that cannot carry an image on its tool-result channel says
nothing, it simply drops the content.

The pipeline is built per call rather than once, for the reason recorded under the data model: the
recorded prompt size belongs to one conversation.

**Preconditions:** `seed` is not null; cancellation has not been requested.

**Postconditions:** A session that already holds the seeded history, occupies nothing until it has
sent something, and invokes the tools its seed carried. Ownership passes to the caller, which
disposes it at the next rotation.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `client` | `ArgumentNullException` propagates |
| `windowTokens` not positive | `ArgumentOutOfRangeException` propagates |
| Selection carrying a context tier, or an effort with no neutral equivalent | `ArgumentException` propagates |
| Null `seed` | `ArgumentNullException` propagates |
| Cancellation before creation | `OperationCanceledException` propagates |

Every condition is a defect in the composing application or a cancellation the caller asked for, so
each is surfaced where it arose. A canceled creation yields no session, so a canceled rotation leaves
nothing unowned.

### Dependencies

- **AgentKitCore** — supplies `IProviderSessionFactory`, `IProviderSession` and
  `ProviderSessionSeed`; see *ProviderSession Unit Design*; and `ModelSelection` and
  `ModelSettingsChatClient`, which apply a user's model choice; see *ModelSettingsChatClient Unit
  Design*.
- **ChatClientProviderSession** — the session this factory creates; see *ChatClientProviderSession
  Unit Design*.
- **PromptSizeRecordingChatClient** — the layer this factory installs at the bottom of the pipeline;
  see *PromptSizeRecordingChatClient Unit Design*.
- **ImagePromotingChatClient** — the layer this factory installs between the tool-calling loop and
  the recorder; see *ImagePromotingChatClient Unit Design*.
- **Microsoft.Agents.AI** — brings the chat-client extensions supplying `FunctionInvokingChatClient`,
  the tool-calling loop this factory installs at the top of the pipeline; see
  *Microsoft.Agents.AI Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies `IChatClient`; see
  *Microsoft.Extensions.AI.Abstractions Design*.

### Callers

An application constructs one and hands it to `CompactingAgentSession`, which calls `CreateAsync`
once at creation and again at every rotation; see *CompactingAgentSession Unit Design*. Within this
system nothing else calls it.
