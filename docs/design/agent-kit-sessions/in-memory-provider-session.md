## InMemoryProviderSession

### Purpose

`InMemoryProviderSession` is the built-in provider-session implementation that contacts nothing. It
lets repository tests exercise creation, usage reporting, rotation evidence and disposal without a live
provider.

### Data Model

`InMemoryProviderSession` internal state:

- **`Seed`** (`ProviderSessionSeed`) — The seed used to create this session.
- **`WindowTokens`** (`int`) — Pretend provider context window.
- **`FixedOverheadTokens`** (`int`) — Estimated instruction and tool declaration overhead.
- **`History`** (`IReadOnlyList<TranscriptEntry>`) — Live read-only view of seeded and accepted
  entries.
- **`TurnCount`** (`int`) — Number of accepted turns.
- **`IsDisposed`** (`bool`) — Whether disposal has occurred.

`InMemoryProviderSessionFactory` internal state:

- **`WindowTokens`** (`int`) — Window passed to created sessions.
- **`ReportsUsage`** (`bool`) — Whether created sessions return usage or simulate a silent provider.
- **`Sessions`** (`IReadOnlyList<InMemoryProviderSession>`) — Snapshot of created sessions, oldest
  first.

The responder delegate maps an incoming message to a `ProviderTurn`. If none is supplied, the default
responder acknowledges the message.

### Key Methods

#### The InMemoryProviderSession Constructor

**Purpose:** Create an in-memory provider session from a seed and responder.

**Algorithm:** Validate seed, responder and positive window. Copy seed history into the live history
list. Estimate fixed overhead from seed instructions and tools.

**Preconditions:** `seed` and `responder` are not null; `windowTokens` is positive.

**Postconditions:** The session is ready to accept messages and can report usage when configured to do
so.

#### CurrentUsage

**Purpose:** Return provider-like usage for the in-memory history.

**Algorithm:** If usage reporting is disabled, return null. Otherwise sum estimated entry tokens for
the current history, add fixed overhead, cap narrowed totals at the largest token count, and return a
`ContextUsage.FromProvider` reading with the conversation split.

**Preconditions:** None beyond construction invariants.

**Postconditions:** The reading is marked provider-reported because it stands in for a provider's own
figures from the engine's point of view.

#### SendAsync(string message, CancellationToken cancellationToken)

**Purpose:** Accept a message, produce a provider turn, and append the whole exchange to memory.

**Algorithm:** Reject null message, disposed state and cancellation. Run the responder before editing
history. Check cancellation again. Append the user message and turn entries together, then increment
`TurnCount`.

**Preconditions:** `message` is not null; the session is not disposed; cancellation has not been
requested.

**Postconditions:** On success, history contains both the user message and response entries. If the
responder throws or cancellation is observed, history is unchanged.

#### DisposeAsync()

**Purpose:** Mark the session disposed and clear observable history.

**Algorithm:** Set `IsDisposed` true and clear the backing history list. Repeated disposal is allowed.

**Preconditions:** None.

**Postconditions:** Later sends fail, and tests can observe that the session was released.

#### InMemoryProviderSessionFactory.CreateAsync(ProviderSessionSeed seed, CancellationToken cancellationToken)

**Purpose:** Create and remember an in-memory session.

**Algorithm:** Validate seed and cancellation, create the session, append it to the factory's session
record under a lock, and return it as `IProviderSession`.

**Preconditions:** `seed` is not null; cancellation has not been requested.

**Postconditions:** `Sessions` includes the created session in creation order.

### Error Handling

- **Null seed or responder** — `ArgumentNullException` propagates.
- **Non-positive window** — `ArgumentOutOfRangeException` propagates.
- **Responder returns null** — `InvalidOperationException` propagates.
- **Disposed session send** — `ObjectDisposedException` propagates.
- **Cancellation before or after responder execution** — `OperationCanceledException` propagates and
  history is unchanged.

### Dependencies

- **ProviderSession** — Implements `IProviderSession` and uses seeds and turns.
- **ContextUsage** — Reports provider-style usage when enabled.
- **TokenEstimator** — Estimates fixed overhead and history size.
- **TranscriptEntry** — Stores in-memory history.

### Callers

The repository's tests use the in-memory provider to verify lifecycle and rotation behavior without a
model. The implementation is internal and is not part of the public API.
