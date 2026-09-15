## AgentSession

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The `AgentSession` unit publishes `IAgentSession`, the contract an application programs against, and
`AgentSessionResponse`, what one turn reports back.

### Purpose

`IAgentSession` is the whole of what an application needs to know about this system. An application
that wants a long-running agent should not have to learn how the context window is managed in order
to get one, so the contract says nothing about providers, tiers, rotation or summarizers. It says:
send a message, read what the window looks like, see how many times the session has rotated, and
dispose when finished.

Keeping it small is a deliberate bet that it can be extended later without breaking anyone.
Disposal is in the contract rather than left as an implementation detail because an implementation
owns a live provider session, which for some providers is server-side state that keeps being billed
for until it is released.

`AgentSessionResponse` reports compaction rather than hiding it. An application that never looks is
never troubled; an application that does look can log a rotation, act on a saturation signal, or
explain why one answer took longer than the last. Hiding it would make a saturated agent
indistinguishable from a healthy one — precisely the condition that most needs to be visible.

### Data Model

`IAgentSession` members:

- **`Usage`** (`ContextUsage`) — The context usage after the most recent turn. Before the first turn, what the
  freshly seeded session occupies
- **`RotationCount`** (`int`) — How many times the session has replaced its provider session since creation

`AgentSessionResponse` properties, all immutable after construction:

- **`Text`** (`string`) — Never null; may be empty
- **`Usage`** (`ContextUsage`) — Never null
- **`RotationOccurred`** (`bool`) — True when the session rotated during this turn
- **`Saturations`** (`IReadOnlyList<SaturationSignal>`) — Never null; never contains null; empty when the rotation
  reduced normally; a read-only view over a copy taken at construction, so `IsSaturated` cannot change after the turn
  it describes
- **`IsSaturated`** (`bool`) — Derived: `Saturations.Count > 0`

### Key Methods

#### SendAsync(string message, CancellationToken cancellationToken)

Sends one message and returns the answer, compacting first if the window requires it.

**Preconditions:** `message` is not null, empty or blank; the session has not been disposed.

**Postconditions:** the answer, the resulting usage and the rotation flag are reported. Compaction,
when it happens, happens **after** the answer is produced: the turn is served by the session that
was live when it arrived, and the replacement is prepared for the turn after. That ordering means a
caller never waits on a summarizer before receiving an answer the session could already give.

**Throws:** `ArgumentException` for a blank message; `ObjectDisposedException` once disposed;
`OperationCanceledException` on cancellation.

#### The AgentSessionResponse Constructor

Constructs a turn report, validating before assignment so a malformed report never exists even
briefly. A null `saturations` means none, and is normalized to an empty list so a caller never has
to null-check it.

### Error Handling

- **Null response text** — `ArgumentNullException` propagates
- **Null usage figure** — `ArgumentNullException` propagates
- **Null entry in `saturations`** — `ArgumentException` propagates
- **Blank message to `SendAsync`** — `ArgumentException` propagates
- **Use after disposal** — `ObjectDisposedException` propagates

Each rejected condition could only arise from a defect in the calling application or in an adapter,
and each would otherwise surface far from its cause — in the application that displayed the response,
or at a later rotation. Refusing them at construction puts the failure where it can be diagnosed.

### Dependencies

- **ContextUsage** — the usage figure a turn reports; see _ContextUsage Unit Design_.
- **RotationEngine** — supplies `SaturationSignal`; see _RotationEngine Unit Design_.

### Callers

`IAgentSession` is implemented by `CompactingAgentSession` and is the public entry point an
application holds. `AgentSessionResponse` is constructed only by `CompactingAgentSession` and is
consumed by the application.
