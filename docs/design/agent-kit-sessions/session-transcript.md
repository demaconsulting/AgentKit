## SessionTranscript

### Purpose

`SessionTranscript` stores the engine's out-of-session record as whole turns. A turn is one accepted
exchange: the user message, the answer, and every tool call and result between them. The transcript
is append-only between rotations and exposes the operations needed to keep or age whole turns.

### Data Model

Public provider-seam entries:

- **`TranscriptEntryKind`** — `UserMessage`, `AssistantMessage`, `ToolCall`, `ToolResult` and
  `ContextRecord`.
- **`TranscriptEntry`** — Immutable entry with `Kind`, `Text`, optional `ToolCallId` and cached
  `EstimatedTokens`.

Internal transcript structures:

- **`SessionTurn`** — Immutable group of entries from one exchange, with cached estimated tokens.
- **`SessionTranscript`** — Immutable ordered list of turns, oldest first.

`TranscriptEntry` requires a non-blank tool-call identifier for tool call and result entries and
rejects identifiers on non-tool entries. A context record is a consolidated slot seeded into a fresh
provider session.

### Key Methods

#### AppendTurn(IEnumerable&lt;TranscriptEntry&gt; entries)

**Purpose:** Return a transcript with one additional whole turn at the end.

**Algorithm:** Require a non-empty sequence, reject null entries, copy the entries, wrap them in one
`SessionTurn`, and return a new transcript containing the appended turn.

**Preconditions:** `entries` is not null, contains at least one entry, and contains no null entries.

**Postconditions:** The original transcript is unchanged; the new transcript has one more turn.

#### SplitAtTail(int keepTurns)

**Purpose:** Split the transcript at a turn boundary for rotation.

**Algorithm:** Keep the newest `keepTurns` turns verbatim and return all older turns, oldest first, as
whole turns rather than as their flattened entries. If the transcript holds no more than `keepTurns`
turns, return no older turns and retain the whole transcript.

**Preconditions:** `keepTurns` is not negative.

**Postconditions:** Boundaries are turn-granular on both sides of the split. A tool call and its
result cannot be separated, because they are inside the same turn and that turn is handed on whole —
which is what keeps consolidation chunking from placing them in different summarizer calls.

#### DropOldestTurn()

**Purpose:** Return the transcript without its oldest verbatim turn.

**Algorithm:** Reject an empty transcript; otherwise return a new transcript without the first turn.

**Preconditions:** The transcript holds at least one turn.

**Postconditions:** The original transcript is unchanged.

#### ToTranscriptLine() and Render(IEnumerable&lt;TranscriptEntry&gt; entries)

**Purpose:** Render entries as deterministic labeled material for a stateless summarizer.

**Algorithm:** Each entry renders as a single line with a mechanical label: `USER`, `ASSISTANT`,
`TOOL CALL`, `TOOL RESULT` or `RECORD`. `Render` validates a sequence and joins the lines with
newlines.

**Preconditions:** The sequence passed to `Render` is not null and contains no null entries.

**Postconditions:** The same entries always render to the same material text.

### Error Handling

- **Undefined `TranscriptEntryKind`** — `ArgumentOutOfRangeException` propagates.
- **Null entry text** — `ArgumentNullException` propagates.
- **Missing tool-call identifier for a tool entry** — `ArgumentException` propagates.
- **Identifier supplied for a non-tool entry** — `ArgumentException` propagates.
- **Null or empty turn entries** — `ArgumentNullException` or `ArgumentException` propagates.
- **Negative tail count** — `ArgumentOutOfRangeException` propagates.
- **Dropping from an empty transcript** — `InvalidOperationException` propagates.

### Dependencies

- **TokenEstimator** — Supplies cached entry and turn estimates.
- **ProviderSession** — Uses `TranscriptEntry` in seeds and turns; see _ProviderSession Unit Design_.
- **RotationEngine** — Calls `SplitAtTail` and `Render` during rotation.

### Callers

`CompactingAgentSession` appends one turn after every accepted provider turn. `RotationEngine` splits
and renders transcript material while building a replacement layout.
