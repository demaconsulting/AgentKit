## SessionTranscript

![AgentKit Sessions Structure](AgentKitSessionsView.svg)

The `SessionTranscript` unit publishes `TranscriptEntry`, one indivisible item of session history,
and `SessionTranscript`, the append-only verbatim record the engine keeps out of session.

### Purpose

The engine keeps its own transcript for two reasons. First, consolidation must run **out of
session**: asking a live session to summarize itself spends that session's own context on the
summary and triggers the provider's built-in compactor, which is self-defeating. Handing the
material to a separate stateless call requires having the material. Second, the transcript is still
available when a provider session has been disposed, which is exactly when a fresh one must be
seeded.

**Append-only between rotations, by design.** Nothing already sent to a provider is ever rewritten
while a session is live. That is what preserves prompt caching: a provider that recognizes an
unchanged prefix charges less for it, and an in-place edit anywhere in the history invalidates that
prefix for every following turn. All reshaping happens in one batch at rotation, where the cache is
invalidated anyway.

`SplitAtBudget` is where tier zero's boundary is decided, and is the unit's most consequential
behavior.

### Data Model

`TranscriptEntryKind` values: `UserMessage`, `AssistantMessage`, `ToolCall`, `ToolResult`,
`ContextRecord`. The last is a consolidated record of older history, produced by the summarizer and
seeded into a fresh provider session rather than produced within one.

`TranscriptEntry` properties, immutable after construction:

- **`Kind`** (`TranscriptEntryKind`) — —
- **`Text`** (`string`) — Never null; may be empty, because a tool returning nothing still occupies a turn
- **`ToolCallId`** (`string?`) — Non-blank for `ToolCall` and `ToolResult`; null for every other kind
- **`EstimatedTokens`** (`int`) — Computed once at construction: the text estimate plus the framing allowance

**The identifier asymmetry is deliberate.** Pairing a call with its result is what lets a tier
boundary be snapped so the two never separate, and an unidentified call could not be paired. An
identifier on a plain message would be meaningless and is refused rather than ignored, so a caller
that supplies one learns it misunderstood the model.

`SessionTranscript` properties:

- **`Entries`** (`IReadOnlyList<TranscriptEntry>`) — Oldest first; never contains null; a read-only view over the
  transcript's own array, so an entry cannot be replaced behind the cached token total
- **`EstimatedTokens`** (`int`) — The sum of every entry's estimate, computed once at construction

`Empty` is a single shared instance, safe because the type is immutable.

### Key Methods

#### Append(TranscriptEntry entry) and Append(IEnumerable&lt;TranscriptEntry&gt; entries)

Return a new transcript with the entry or entries at the end; this one is unchanged. The sequence
overload exists because one turn of a tool-using agent produces a run of entries — an assistant
message, then call and result pairs — and appending them one at a time would allocate a new array
for each. An empty sequence returns the same instance.

Immutability is what makes the rotation engine a pure function of its inputs and lets a test hold a
before-and-after pair.

#### SplitAtBudget(int budgetTokens)

Splits the transcript into the newest entries that fit a verbatim budget and the older entries to
consolidate.

**Algorithm:**

1. Walk backwards from the newest entry, accumulating estimated tokens, stopping when the next entry
   would not fit. The boundary index is the oldest retained entry.
2. While the retained set begins with a `ToolResult`, move the boundary one entry later, pushing
   that result into the overflow.
3. If nothing overflowed, return this very transcript and an empty overflow.
4. Otherwise return the suffix as a new transcript and the prefix, oldest first, as the overflow.

**Why newest-first.** Recency is what tier zero is for, so the retained set is always a contiguous
suffix: the most recent turns, held verbatim.

**Why the boundary snaps later rather than earlier.** A boundary falling between a call and its
result would leave the retained set beginning with a result whose call is gone. Some providers reject
that outright, and a model presented with it cannot tell what was asked. Moving later can only
shrink the retained set, so snapping can never push it back over budget — whereas moving earlier to
recover the call could.

**An entry larger than the whole budget retains nothing.** That is reported honestly rather than
papered over: the oversized entry is consolidated like any other overflow and the caller sees an
empty retained set. Retaining it anyway would silently break the bound the budget exists to enforce.

**Preconditions:** `budgetTokens` is not negative; zero retains nothing.

#### ToTranscriptLine() and Render(IEnumerable&lt;TranscriptEntry&gt; entries)

Render entries as the labeled text a stateless summarizer is handed: `USER:`, `ASSISTANT:`,
`TOOL CALL [id]:`, `TOOL RESULT [id]:` or `RECORD:`, one per line. The labeling is fixed and
mechanical rather than prose so that the same history always renders to the same string, which is
what lets a fake summarizer in a test assert on exactly what the engine asked it to consolidate.

### Error Handling

- **Null entry text** — `ArgumentNullException` propagates
- **`ToolCall` or `ToolResult` with a null or blank identifier** — `ArgumentException` propagates
- **Any other kind carrying an identifier** — `ArgumentException` propagates, naming the kind
- **Null entry appended** — `ArgumentNullException` propagates
- **Null entry within an appended sequence** — `ArgumentException` propagates
- **Negative split budget** — `ArgumentOutOfRangeException` propagates

A null in the transcript would fail later, at a rotation, far from the code that put it there, so
every entry is validated as it arrives.

### Dependencies

- **TokenEstimator** — supplies the character ratio and the per-entry framing allowance; see
  _TokenEstimator Unit Design_.

### Callers

`ContextLayout` holds a transcript as its tier zero and includes it in the seed. `RotationEngine`
calls `SplitAtBudget` at the start of every rotation and `Render` to build the material handed to
the summarizer. `CompactingAgentSession` appends the outgoing message and everything a turn
produced.
