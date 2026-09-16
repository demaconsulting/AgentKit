## CompactionPolicy

### Purpose

`CompactionPolicy` carries the one compaction control an application can configure: the maximum
number of recent turns to keep verbatim. All other shape values are internal constants because an
application cannot tune them from an observable symptom.

### Data Model

Immutable public members:

- **`DefaultVerbatimTurns`** (`int`) — The default maximum verbatim tail length, 20 turns.
- **`Default`** (`CompactionPolicy`) — Shared validated default instance.
- **`VerbatimTurns`** (`int`) — Positive maximum number of recent turns kept word for word.

`VerbatimTurns` is a maximum, not a quota. The session keeps fewer turns when the current
`CompactionLevel` is Medium or High, and a rotation keeps at most one turn fewer than the tail holds
so that it always moves something.

Internal shape constants live in `ContextLayout` and `CompactingAgentSession`: four slots per tier,
three tiers, a 0.70 rotation threshold, and hysteresis windows based on `VerbatimTurns`.

### Key Methods

#### CompactionPolicy(int verbatimTurns = DefaultVerbatimTurns)

**Purpose:** Create an immutable policy with a positive maximum verbatim tail.

**Algorithm:** Reject a non-positive count, then store it.

**Preconditions:** `verbatimTurns` is positive.

**Postconditions:** The policy is immutable and safe to share.

### Error Handling

- **Zero or negative `verbatimTurns`** — `ArgumentOutOfRangeException` propagates.

There is no runtime recovery for a malformed policy because it is a host configuration defect.

### Dependencies

N/A - `CompactionPolicy` has no collaborator dependencies. `AgentSessionOptions` stores it,
`RotationEngine` receives its value as a count, and `CompactingAgentSession` derives hysteresis
windows from it.

### Callers

Applications construct `CompactionPolicy` when they want a maximum verbatim tail other than the
default. `AgentSessionOptions` selects `CompactionPolicy.Default` when the application supplies none.
