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
- **`Tools`** (`IReadOnlyList<AIFunction>`) — Never null; never contains null; may be empty; a read-only view over a
  copy taken at construction. Identical across every rotation of one logical session
- **`History`** (`IReadOnlyList<TranscriptEntry>`) — Never null; never contains null; a read-only view over a copy
  taken at construction; most stable first, as produced by `ContextLayout.BuildSeed`. Empty for the first session of
  a conversation

Both lists are copied because a seed is an immutable snapshot an adapter may hold across a rotation:
retaining the caller's lists would let the adapter observe a later mutation and start a session from
something other than what was validated here.

The instructions and tools are carried separately from the history because providers accept them
separately — as configuration rather than as messages — which is also why they are accounted for as
fixed overhead rather than as conversation. Rotation replaces history, never capability.

`ProviderTurn` properties, immutable after construction:

- **`ResponseText`** (`string`) — Never null; may be empty
- **`Entries`** (`IReadOnlyList<TranscriptEntry>`) — Never null or empty; never contains null; always **ends with an
  assistant message carrying `ResponseText`**; a read-only view over a copy taken at construction, for the same reason
  the seed copies its lists

The answer and the history entries are separate because they answer different questions. The text is
what the application shows or acts on; the entries are what every consumer records, and for a
tool-using turn there are several of them — an assistant message announcing the work, then call and
result pairs.

**The answer is recorded exactly once, by `ProviderTurn` itself, on both paths.** An adapter
describes what happened *before* the answer and the turn appends the answer as the final entry: as
the only entry when no others were supplied, and after the supplied ones when they were. Doing it
here rather than in the compaction engine is deliberate, because `Entries` is what *every* consumer
records — the engine's transcript, and any session keeping its own history from the turns it
produced, `InMemoryProviderSession` among them. An answer appended by one consumer would be missing
from the others, and the two records would then describe different conversations. The consequence of
leaving it out of `Entries` altogether was worse still: an agent that uses tools on nearly every turn
would have almost none of its own output in the history a rotation seeds the replacement session
from, which is exactly the material the compaction engine exists to preserve.

An adapter that mapped a provider's own message list straight across has already ended with the
answer; it is not made to strip it. A trailing assistant entry whose text is the answer *is* the
answer and is not repeated, so either adapter style yields exactly one copy.

### Key Methods

#### The ProviderSessionSeed Constructor

Validates that neither collection is null and that neither contains a null entry. Rejecting null
entries here rather than leaving an adapter to discover them mid-construction matters because the
failure would otherwise be attributed to the provider rather than to the composition that caused it.

#### ProviderTurn(string responseText, IReadOnlyList&lt;TranscriptEntry&gt;? entries)

Builds the entries the turn records, ending with the answer exactly once. When no entries are
supplied, the turn is recorded as a single assistant message carrying `responseText` — the correct
record for a provider that called no tools. When entries are supplied, they are carried through
unchanged and an assistant message carrying `responseText` is appended after them, unless the last
supplied entry is already an assistant message carrying exactly that text, in which case it is taken
to be the answer and is not duplicated. Either way a simple adapter is spared from restating its own
answer, and a faithful one is spared from stripping it.

#### IProviderSession.SendAsync(string message, CancellationToken cancellationToken)

Sends one message and returns what the provider produced. An implementation may call tools before
answering; the entries it returns should record those calls and their results so the engine's
transcript matches what the provider actually holds — which is what allows a tier boundary to be
snapped correctly. The answer itself need not be among them, because `ProviderTurn` records it as
the final entry either way.

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
