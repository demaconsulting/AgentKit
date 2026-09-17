## ProviderSession

### Purpose

`ProviderSession` defines the provider-adapter seam. The compaction engine never calls a provider API
directly; it creates seeds, sends messages through a provider session, records provider turns, and
asks a factory for replacements.

### Data Model

`ProviderSessionSeed` properties:

- **`Instructions`** (`string?`) — System instructions, or null for none.
- **`Tools`** (`IReadOnlyList<AIFunction>`) — Owned read-only copy of the tools.
- **`History`** (`IReadOnlyList<TranscriptEntry>`) — Owned read-only copy of seed history, most
  stable first.

`ProviderTurn` properties:

- **`ResponseText`** (`string`) — Provider answer; never null and may be empty.
- **`Entries`** (`IReadOnlyList<TranscriptEntry>`) — History entries produced by the turn, always
  ending with the answer exactly once.

`IProviderSession` is one live provider conversation and inherits `IAsyncDisposable`. It answers
`CurrentUsage` — how much of its provider's window it occupies and out of how much —
without contacting the provider. `IProviderSessionFactory` creates a live provider session from a
seed.

### Key Methods

#### The ProviderSessionSeed Constructor

**Purpose:** Capture the complete state a replacement provider session needs.

**Algorithm:** Require tools and history, reject null entries, copy both collections into owned
read-only storage, and store instructions.

**Preconditions:** `tools` and `history` are not null and contain no null entries.

**Postconditions:** The seed is immutable. Later caller mutations cannot change what an adapter sees.

#### ProviderTurn(string responseText, IReadOnlyList&lt;TranscriptEntry&gt;? entries)

**Purpose:** Record what a provider turn produced and ensure the answer is present exactly once.

**Algorithm:** Require `responseText`, reject null supplied entries, and inspect the final supplied
entry. If it is an assistant entry whose text equals the response, reuse the supplied entries;
otherwise append an assistant entry carrying the response.

**Preconditions:** `responseText` is not null; supplied entries contain no null entries.

**Postconditions:** `Entries` is never empty and ends with the answer exactly once.

#### IProviderSession.SendAsync(string message, CancellationToken cancellationToken)

**Purpose:** Send one message to the provider and return the provider turn.

**Algorithm:** Adapter-specific. The implementation should include any tool calls and results in the
returned entries so the engine's transcript matches the provider conversation.

**Preconditions:** `message` is not null and the provider session has not been disposed.

**Postconditions:** On success, the provider session has accepted the turn and the returned
`ProviderTurn` describes it.

#### IProviderSession.CurrentUsage

**Purpose:** Report how much of the provider's context window this session occupies and out of how
much.

**Algorithm:** Adapter-specific, and answered from what the last exchange already revealed rather than
by contacting the provider, so reading it is free and cannot fail. An adapter whose provider publishes
current and limit counts passes them through; one whose provider publishes only a context length
counts occupancy against the window it was told at construction; one whose provider reveals nothing
estimates and marks the reading `Estimated`. An adapter that can distinguish the conversation from the
system prompt and tool declarations passes that split, rather than leaving it to be inferred.

**Preconditions:** The session has been created and not yet disposed.

**Postconditions:** The reading is the one token figure the engine consumes. Because the window, the
overhead and the conversation all come from it, the rotation trigger is computed in a single currency.

#### IProviderSessionFactory.CreateAsync(ProviderSessionSeed seed, CancellationToken cancellationToken)

**Purpose:** Create a live provider session carrying the seeded instructions, tools and history.

**Algorithm:** Adapter-specific. Ownership of the returned session passes to the caller.

**Preconditions:** `seed` is not null.

**Postconditions:** The returned provider session is ready for the next message and must eventually be
disposed by the caller.

### Error Handling

- **Null tools, history, response text, message or seed** — `ArgumentNullException` propagates.
- **Null entry inside tools, history or turn entries** — `ArgumentException` propagates.
- **Disposed provider session** — The adapter throws `ObjectDisposedException`.
- **Cancellation** — The adapter propagates `OperationCanceledException`.

### Design Constraints

- The factory must be safe for concurrent use.
- A provider session serves one conversation and need not be safe for concurrent use.
- Every provider session answers for its own context window, however its provider allows: reading the
  figures from a native API, counting against a window it was told once, or estimating and owning
  that choice.
- The seed history is most-stable-first: coarse records first, then verbatim turns.

### Dependencies

- **TranscriptEntry** — Shared history currency.
- **ContextUsage** — The shape `CurrentUsage` answers with.
- **Microsoft.Extensions.AI.Abstractions** — Supplies `AIFunction`.

### Callers

`CompactingAgentSession` creates seeds, sends messages through `IProviderSession`, and calls the
factory on creation and rotation. Provider adapters implement this unit.
