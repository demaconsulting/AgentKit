## AgentSession

![AgentKit Core Structure](AgentKitCoreView.svg)

### Purpose

`AgentSession` defines the public contract for one compacting conversation and the immutable response
returned from each accepted turn. It hides provider details, rotation mechanics and summarizer work
behind a small application-facing surface.

### Data Model

`AgentSessionResponse` properties, immutable after construction:

- **`Text`** (`string`) — The provider's answer; never null and may be empty.
- **`Usage`** (`ContextUsage`) — The usage reading after the turn; never null.
- **`RotationOccurred`** (`bool`) — True when the session replaced its provider session after the
  answer was produced.
- **`Level`** (`CompactionLevel`) — The compaction level after the turn.
- **`MaterialDropped`** (`bool`) — True when the turn discarded preserved material rather than
  reducing it: a tier that displaced its oldest slot to make room for an arriving one, or a slot —
  or, with none left, the oldest verbatim turn — binned under sustained pressure. A consolidation
  that simply came back blank is not a drop: the material stays where it is.

`CompactionLevel` members, ordered from the gentlest to the tersest:

- **`Low`** — The full verbatim tail, and the "summarize concisely" clause. The level every session
  starts at and relaxes back to.
- **`Medium`** — Half the verbatim tail, and the "decisions, facts and open threads only" clause.
- **`High`** — A quarter of the verbatim tail, and the "one or two lines" clause. The tersest level;
  a session already here answers further pressure by binning its oldest card instead of escalating.

`IAgentSession` properties:

- **`Usage`** (`ContextUsage`) — The current usage; before the first turn this describes the newly
  created provider session.
- **`RotationCount`** (`int`) — The number of completed provider-session replacements.
- **`Level`** (`CompactionLevel`) — Current adaptive compaction level.

The interface inherits `IAsyncDisposable` because a live provider session may hold server-side state
that must be released.

### Key Methods

#### SendAsync(string message, CancellationToken cancellationToken)

**Purpose:** Send one non-blank message, return the provider's answer, and compact after the answer
when the window requires it.

**Algorithm:** The implementation sends the message to the live provider session. If the provider
accepts the turn, the implementation records the whole exchange, reads the provider session's usage,
rotates when needed, and returns an `AgentSessionResponse` carrying the answer and compaction state.

**Preconditions:** `message` is not null, empty or blank; the session has not been disposed.

**Postconditions:** On success, the accepted turn is part of the session history. If rotation was
needed and completed, the response reports it and the session is ready for the next turn against the
replacement provider session.

#### The AgentSessionResponse Constructor

**Purpose:** Create an immutable report for one turn.

**Algorithm:** Validate `text`, `usage` and `level`, then store the answer, usage, rotation flag,
compaction level and dropped-material flag.

**Preconditions:** `text` and `usage` are not null; `level` is a defined `CompactionLevel` member.

**Postconditions:** The response is immutable and safe to share.

### Error Handling

- **Null or blank message** — The implementation throws `ArgumentException`.
- **Disposed session** — The implementation throws `ObjectDisposedException`.
- **Cancellation** — The implementation propagates `OperationCanceledException` and does not record a
  turn the provider did not accept.
- **Null response text or usage in `AgentSessionResponse`** — `ArgumentNullException` propagates.
- **Undefined compaction level** — `ArgumentOutOfRangeException` propagates.

### Dependencies

- **ContextUsage** — Reported after every turn; see _ContextUsage Unit Design_.
- **CompactionLevel** — Public fidelity state reported by session and response.
- **CompactingAgentSession** — Implements the interface; see _CompactingAgentSession Unit Design_.

### Callers

Applications consume `IAgentSession` to run long conversations without knowing the provider seam.
Provider adapters do not call this unit; they implement `ProviderSession` contracts below it.
