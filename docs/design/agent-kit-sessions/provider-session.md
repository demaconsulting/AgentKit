## ProviderSession

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The `ProviderSession` unit publishes the whole interface between the compaction engine and a
provider adapter: `ProviderSessionSeed`, `ProviderTurn`, `IProviderSession` and
`IProviderSessionFactory`.

### Purpose

The engine never touches a provider's API. It produces a seed, consumes a turn, and disposes a
session; an adapter does everything else. That boundary is what keeps the engine provider-agnostic
and what lets it be verified end to end against a fake with no network access, no credentials and no
model.

The contract is **deliberately minimal**. It says nothing about streaming, tool invocation, retries,
or provider configuration, because the compaction engine needs none of that. Putting any of it here
would tie the engine to a provider shape.

The factory is separate from the session because rotation must create a *replacement* while the
engine holds no reference to a provider's API itself. Disposal is in the contract because it is the
mechanism that discards server-side history: for a provider holding the conversation server-side,
disposal is what actually releases it.

### Data Model

`ProviderSessionSeed` properties, immutable after construction:

- **`Instructions`** (`string?`) — Null when there are none
- **`Tools`** (`IReadOnlyList<AIFunction>`) — Never null; never contains null; may be empty. Identical across every
  rotation of one logical session
- **`History`** (`IReadOnlyList<TranscriptEntry>`) — Never null; never contains null; most stable first, as
  produced by `ContextLayout.BuildSeed`. Empty for the first session of a conversation

The instructions and tools are carried separately from the history because providers accept them
separately — as configuration rather than as messages — which is also why they are accounted for as
fixed overhead rather than as conversation. Rotation replaces history, never capability.

`ProviderTurn` properties, immutable after construction:

- **`ResponseText`** (`string`) — Never null; may be empty
- **`Entries`** (`IReadOnlyList<TranscriptEntry>`) — Never null or empty; never contains null

The answer and the history entries are separate because they answer different questions. The text is
what the application shows or acts on; the entries are what the engine records, and for a tool-using
turn there are several of them — an assistant message, then call and result pairs — none of which is
the answer.

### Key Methods

#### The ProviderSessionSeed Constructor

Validates that neither collection is null and that neither contains a null entry. Rejecting null
entries here rather than leaving an adapter to discover them mid-construction matters because the
failure would otherwise be attributed to the provider rather than to the composition that caused it.

#### ProviderTurn(string responseText, IReadOnlyList&lt;TranscriptEntry&gt;? entries)

When no entries are supplied, the turn is recorded as a single assistant message carrying
`responseText`. That is the correct record for a provider that called no tools, and it spares a
simple adapter from restating its own answer.

#### IProviderSession.SendAsync(string message, CancellationToken cancellationToken)

Sends one message and returns what the provider produced. An implementation may call tools before
answering; the entries it returns should record those calls and their results so the engine's
transcript matches what the provider actually holds — which is what allows a tier boundary to be
snapped correctly.

**Preconditions:** `message` is not null; the session has not been disposed.

**Throws:** `ArgumentNullException`, `ObjectDisposedException`, `OperationCanceledException`.

#### IProviderSessionFactory.CreateAsync(ProviderSessionSeed seed, CancellationToken cancellationToken)

Creates a session already holding the seeded history, so that the first message sent to it continues
the conversation rather than starting one. Ownership of the returned session passes to the caller,
which disposes it at the next rotation.

### Error Handling

- **Null tool list or history in a seed** — `ArgumentNullException` propagates
- **Null entry within a seed's tools or history** — `ArgumentException` propagates
- **Null response text in a turn** — `ArgumentNullException` propagates
- **Null entry within a turn** — `ArgumentException` propagates
- **Send after disposal** — Implementations throw `ObjectDisposedException`

### Design Constraints

- **`IProviderSession` implementations need not be safe for concurrent use.** One session serves one
  conversation, and turns within a conversation are sequential by nature.
- **`IProviderSessionFactory` implementations must be safe for concurrent use**, because an
  application may run more than one session against the same factory.
- **Usage reporting is not part of this contract.** An implementation that can account for its own
  window also implements `IContextUsageReporter`; the engine tests for it and estimates when it is
  absent. See *ContextUsage Unit Design*.

### Dependencies

- **SessionTranscript** — supplies `TranscriptEntry`; see *SessionTranscript Unit Design*.
- **Microsoft.Extensions.AI.Abstractions** — supplies `AIFunction`.

### Callers

`CompactingAgentSession` constructs a seed at creation and at every rotation, calls the factory,
sends every turn through the session, and disposes it. `InMemoryProviderSession` implements both
interfaces. The provider adapters that will implement them for real providers are a later increment.
