## CopilotSessionObserver

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotSessionObserver` class watches one Copilot session's event stream and holds what the
adapter above it cannot otherwise see.

### Purpose

**A Copilot turn reveals itself through events, not through a return value.** The runtime's wait
hands back the final assistant message and nothing else; the tool calls, the tool results, and —
decisively — the session's occupancy and its window arrive as session events. This class is where
they are caught. It is handed to the session configuration's event handler at creation time, which
the SDK registers on the session *before* the create request is issued, so the very first turn is
covered.

**It is the Copilot analogue of the stateless adapter's prompt-size recorder, with one deliberate
difference.** That recorder is cleared before every turn, because an `IChatClient` reports a figure
for one request and a figure left standing would freeze occupancy forever. Copilot's usage event is
cumulative *session* state, so the latest reading is simply the truth and is **not** cleared per
turn — a turn that reported none would otherwise be indistinguishable from a session that has never
reported one, and the session above would refuse a turn it could perfectly well measure. What is
cleared per turn is the state that really is per-turn: the entry buffer, the last reported error,
and whether *this* turn reported its usage.

**Thread safety.** The SDK invokes handlers serially, in arrival order, on a background thread, and
never concurrently with each other on one session. So no handler can run while another is — but
the reads happen on the caller's thread while that background thread may be writing, so every field
is guarded by one lock.

The class is internal: it exists to serve `CopilotProviderSession`, and an application has no use
for it.

### Data Model

- **`_gate`** (`object`) — The single lock guarding every field.
- **`_entries`** (`List<TranscriptEntry>`) — What the current turn produced, in arrival order.
  Invariant: emptied when a turn begins and when it is drained.
- **`_latestUsage`** (`CopilotUsageReading?`) — The most recent reading, or null when the runtime has
  reported none. Invariant: never cleared — it is cumulative session state.
- **`_usageReportedThisTurn`** (`bool`) — Whether a reading arrived during the turn now in flight.
  Invariant: set with `_latestUsage` in one action, so a reader never sees a turn that has reported
  while the reading is still absent; cleared when a turn begins.
- **`_providerRewroteHistory`** (`bool`) — Whether the runtime has compacted or truncated the
  session. Invariant: once true, never false again; a rewrite cannot be undone.
- **`_lastErrorMessage`** (`string?`) — The message of the last session error seen. Invariant:
  cleared when a turn begins, so a refusal names this turn's cause and not an earlier one's.

Each field is published by a property of the same name that reads it under the lock: `LatestUsage`,
`UsageReportedThisTurn`, `ProviderRewroteHistory` and `LastErrorMessage`.

**Why a per-turn flag sits beside the cumulative reading.** The reading is deliberately kept across
a turn boundary, so a check written against it asks whether the *session* has ever reported. A
runtime that answered a later turn without reporting would pass such a check on the previous turn's
figure, leaving occupancy frozen while the conversation kept growing and the engine rotating later
and later against a number that had stopped moving. That is the defect the ChatClient adapter
shipped with. The flag is the per-turn question, and `CopilotProviderSession` asks it.

`CopilotUsageReading` is a small immutable snapshot of one usage event — the occupancy, the limit and
the conversation's share, in the runtime's own `long` currency. It is a snapshot rather than a
reference to the SDK's event data, because that data is mutable and belongs to the runtime; and it
keeps the wider currency so the narrowing happens in one visible place, in
`CopilotProviderSession.CurrentUsage`.

### Key Methods

#### OnEvent(SessionEvent sessionEvent)

**Purpose:** Record one event from the session's stream.

**Algorithm:** Match the event against the kinds this adapter has a use for and record the
corresponding state change under the lock. A usage event becomes the latest reading and marks the
turn as having reported, both in one action so the two are never read out of step. A tool
execution start becomes a tool-call entry rendered by name and arguments; its completion becomes a
tool-result entry carrying the result's content, or the runtime's error message where the tool
failed, or the empty string where it reported neither. An assistant message becomes an assistant
entry. A compaction or truncation event marks the history rewritten. A session error is remembered.
Everything else is ignored.

**Events carrying a parent tool call are skipped.** A nested agent the runtime ran on this session's
behalf has its own thread of tool traffic and its own messages, tagged with the call that started
it. Those were never part of this conversation and the model was never shown them as such, so
recording them would seed a future session with a history it does not recognize. The call that
*started* the nested agent is recorded, because that one happened in this conversation. This is a
judgment call, recorded rather than assumed.

**A tool call with no usable identifier is skipped**, because a transcript entry requires one so a
call can be paired with its result, and an unidentified call could not be paired. The runtime's
events arrive by deserialization, which sets members directly and does not enforce the ones the SDK
marks required, so the absence is representable rather than theoretical. An empty assistant message
is skipped for a related reason: the runtime emits assistant messages in phases, and an empty one is
not something the model said.

**This method never throws.** It runs on the runtime's dispatch thread, where an exception would be
attributed to the SDK rather than to this library, so the matching is total by construction: every
branch either records something well-formed or does nothing.

**Preconditions:** None.

**Postconditions:** At most one state change, applied under the lock.

#### BeginTurn()

**Purpose:** Start a turn, discarding anything the previous one left behind.

**Algorithm:** Clear the three per-turn fields under one lock: the entry buffer, the last reported
error, and whether this turn has reported its usage. Called before the prompt is sent, so what is
read afterwards belongs to this turn and no other's. A turn that failed mid-flight leaves entries
and an error message here; clearing at the start rather than at the end is what stops them being
attributed to the next turn — and the error matters most, because it is read only to name the cause
when a turn goes idle without an answer, so a stale one would name the wrong cause in the one
message whose whole job is to name the right one. The usage *reading* is deliberately not cleared —
see *Purpose* and *Data Model*.

**Preconditions:** None.

**Postconditions:** The entry buffer is empty, no error is remembered, and this turn has reported no
usage; the kept reading and the rewritten-history flag are unchanged.

#### DrainEntries()

**Purpose:** Take what this turn produced and empty the buffer.

**Algorithm:** Copy the entries out under the lock and clear them. Draining rather than reading is
what guarantees a turn's work is recorded exactly once even if a caller asks twice.

**Preconditions:** None.

**Postconditions:** The entries in arrival order; the buffer is empty.

#### DescribeCall(ToolExecutionStartData start) and DescribeResult(ToolExecutionCompleteData complete)

**Purpose:** Render a call and its outcome for the transcript.

**Algorithm:** A call renders as its name and its arguments; an unnamed tool renders under a fixed
placeholder, because a call with no name is still a call that happened and dropping it would leave
its result paired with nothing. A result renders as the tool's content, else the runtime's error
message, else the empty string. The transcript is read by a summarizer rather than executed, so the
arguments are recorded for what they say about the call rather than to be parsed back; and a tool
that failed is recorded with its error because the model saw that failure and reasoned from it.

**Preconditions:** None beyond the event's own shape.

**Postconditions:** One line of transcript text.

#### Record(Action mutation)

**Purpose:** Apply a state change under the lock.

**Algorithm:** Take the lock and invoke the mutation. A named helper rather than a lock statement in
each branch, so the matching above reads as the mapping it is and no branch can be added that
forgets to take the lock.

**Preconditions:** None.

**Postconditions:** The mutation has been applied exclusively.

### Error Handling

| Condition                                    | Handling                                       |
|----------------------------------------------|------------------------------------------------|
| Event kind this adapter has no use for       | Ignored                                        |
| Tool event with no usable call identifier    | Skipped; nothing recorded                      |
| Assistant message with no text               | Skipped; nothing recorded                      |
| Event belonging to a nested agent            | Skipped; nothing recorded                      |
| Tool reported neither result nor error       | Recorded as an empty result                    |

Nothing here throws, by design. This class runs on the runtime's own dispatch thread, and an
exception raised there would surface as an SDK fault rather than as an AgentKit one — so every
condition that could produce a malformed entry produces no entry instead. What that costs is
visible: a tool call the runtime failed to identify is absent from the seeded history. What it buys
is that the runtime's event loop is never destabilized by this library.

### Dependencies

- **AgentKitCore** — supplies `TranscriptEntry`; see *SessionTranscript Unit Design*.
- **Microsoft.Agents.AI.GitHub.Copilot** — supplies the session event hierarchy and its payloads; see
  *Microsoft.Agents.AI.GitHub.Copilot Design*.

### Callers

`CopilotProviderSessionFactory` constructs one per session and registers its handler on the session
configuration; `CopilotProviderSession` reads it on every turn. Nothing else uses it.
