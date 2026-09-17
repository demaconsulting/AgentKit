## InMemoryProviderSession

![AgentKit Core Structure](AgentKitCoreView.svg)

### Purpose

`InMemoryProviderSession` is the built-in provider-session implementation that contacts nothing. It
lets repository tests exercise creation, usage reporting, rotation evidence and disposal without a live
provider.

### Data Model

`InMemoryProviderSession` public surface:

- **`Seed`** (`ProviderSessionSeed`) — The seed used to create this session.
- **`WindowTokens`** (`int`) — Pretend provider context window.
- **`FixedOverheadTokens`** (`int`) — This session's own count of what its seeded instructions and
  tool declarations occupy.
- **`History`** (`IReadOnlyList<TranscriptEntry>`) — Live read-only view of seeded and accepted
  entries.
- **`TurnCount`** (`int`) — Number of accepted turns.
- **`IsDisposed`** (`bool`) — Whether disposal has occurred.

`InMemoryProviderSessionFactory` public surface:

- **`DefaultWindowTokens`** (`const int`) — The window created sessions pretend to have when none is
  given: large enough that a test not about the window does not accidentally rotate, small enough
  that one which is can reach it cheaply.
- **`WindowTokens`** (`int`) — Window passed to created sessions.
- **`Sessions`** (`IReadOnlyList<InMemoryProviderSession>`) — Snapshot of created sessions, oldest
  first.

The responder delegate maps an incoming message to a `ProviderTurn`. If none is supplied, the default
responder acknowledges the message.

### Key Methods

#### The InMemoryProviderSession Constructor

**Purpose:** Create an in-memory provider session from a seed and responder.

**Algorithm:** Validate seed, responder and positive window. Copy seed history into the live history
list. Count the fixed overhead from the seeded instructions and tool declarations with this session's
own counter - four characters to the token, plus a flat per-item allowance standing in for the
framing a real provider wraps around a declaration.

**Preconditions:** `seed` and `responder` are not null; `windowTokens` is positive.

**Postconditions:** The session is ready to accept messages and can answer for its own window.

#### CurrentUsage

**Purpose:** Answer for this session's own window, as every adapter does.

**Algorithm:** Count the tokens of the current history with this session's own counter, add the fixed
overhead, saturate each figure at the largest token count so the total is never below the
conversation it contains, and return a `ContextUsage.FromProvider` reading carrying both.

**Preconditions:** None beyond construction invariants.

**Postconditions:** The reading is produced through `FromProvider` because, from the engine's point
of view, that is exactly what it is: a session answering for its own window in the shape a real
adapter uses.

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
- **ContextUsage** — The shape this session answers `CurrentUsage` with.
- **TranscriptEntry** — Stores in-memory history.

### Callers

The repository's tests use the in-memory provider to verify lifecycle and rotation behavior without a
model. It is part of the public surface rather than a test fixture, because an application author
writing a summarizer, choosing a verbatim tail length or acting on the reported compaction level
needs the same ability to exercise a session without a live model.
