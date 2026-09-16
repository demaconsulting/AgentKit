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

**An undefined kind is refused before the pairing rules are applied.** It would otherwise satisfy
every one of them — it is neither a call nor a result, so no identifier is required and none is
rejected — be accepted, and then render through the default labeling branch as though it were a
consolidated record. That is how malformed input would become part of the context an agent is seeded
from. `ToolResult.Denied` in AgentKit Core sets the precedent, and the same
`ArgumentOutOfRangeException` follows it.

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
for each. An empty sequence returns the same instance. `CompactingAgentSession` records a whole
turn — the outgoing message and everything the turn produced — through that one overload for the
same reason: an append copies the whole backing array, so a caller making two appends per turn pays
twice the copying over a window that is filling.

Immutability is what makes the rotation engine a pure function of its inputs and lets a test hold a
before-and-after pair.

**The cached total is accumulated wide and refused where it cannot be represented.** One entry fits
a token count on its own — the runtime's string limit caps a single entry a long way below one — but
nine entries carrying the longest string that can exist do not. An `int` accumulator wraps that to a
negative figure, and `EstimatedTokens` is what every rotation decision is taken from: a negative
total compares below every threshold, so the transcript that most needed to rotate would be the one
that never did. Construction is where the total first becomes computable, so an unrepresentable one
is rejected there rather than at each later site that reads it, exactly as the tool declarations
are. An append is the only way a transcript grows, so it is the only operation that can raise it.

#### SplitAtBudget(int budgetTokens)

Splits the transcript into the newest entries that fit a verbatim budget and the older entries to
consolidate.

**Algorithm:**

1. Walk backwards from the newest entry, accumulating estimated tokens, stopping when the next entry
   would not fit — asked as *does the candidate exceed the budget less what is used*, never as *does
   the used total plus the candidate exceed the budget*. The boundary index is the oldest retained
   entry.
2. While any retained entry is a `ToolResult` with no matching `ToolCall` earlier in the retained
   set, move the boundary to one past that result, pushing it and everything before it into the
   overflow. This repeats, because dropping the calls before an orphan can orphan a result that was
   paired a moment ago.
3. If nothing overflowed, return this very transcript and an empty overflow.
4. Otherwise return the suffix as a new transcript and the prefix, oldest first, as the overflow.

**Why newest-first.** Recency is what tier zero is for, so the retained set is always a contiguous
suffix: the most recent turns, held verbatim.

**Why the fit test is a subtraction.** `used + candidate.EstimatedTokens > budgetTokens` is `int`
arithmetic. A policy may budget tier zero at the largest representable token count, and a transcript
may accumulate near it, at which point the sum wraps negative, compares below the budget, and the
entry is retained — carrying the retained set past the very bound this split documents, silently and
in the one direction the arrangement cannot tolerate. Asking whether the candidate exceeds the
budget less what is used cannot wrap: the loop only continues while the used total is within the
budget and both are non-negative, so the difference is a non-negative token count and the comparison
is exact at every budget a policy can express.

**Why the whole retained window is validated, not just its first entry.** A retained set holding a
result whose call is gone is an orphan wherever it sits. Checking only the first entry missed the
interleaved shape a parallel tool turn produces constantly — `call c1, call c2, result c1,
result c2` — where the boundary can land on c2's call, which is not a result and so passes that
check, while c1's result stays retained with its call in the overflow.

**Why the boundary snaps later rather than earlier.** Some providers reject an orphaned result
outright, and a model presented with one cannot tell what was asked. Moving later can only
shrink the retained set, so snapping can never push it back over budget — whereas moving earlier to
recover the call could. Where no orphan-free suffix fits the budget, nothing is retained and the
whole run is consolidated together, which is the only split that keeps every pair intact.

**An entry larger than the whole budget retains nothing.** That is reported honestly rather than
papered over: the oversized entry is consolidated like any other overflow and the caller sees an
empty retained set. Retaining it anyway would silently break the bound the budget exists to enforce.

**The overflow is published as a genuine read-only view** over a slice the transcript owns, for the
same reason `Entries` is: it is the material a consolidation is about to be given, so a caller able
to cast it back to an array could change what the summarizer sees after the split decided it.

**Preconditions:** `budgetTokens` is not negative; zero retains nothing.

#### ToTranscriptLine() and Render(IEnumerable&lt;TranscriptEntry&gt; entries)

Render entries as the labeled text a stateless summarizer is handed: `USER:`, `ASSISTANT:`,
`TOOL CALL [id]:`, `TOOL RESULT [id]:` or `RECORD:`, one per line. The labeling is fixed and
mechanical rather than prose so that the same history always renders to the same string, which is
what lets a fake summarizer in a test assert on exactly what the engine asked it to consolidate.

**`Render` rejects a null element rather than dereferencing it.** It is public, and it was the one
entry point here taking a sequence without checking its contents: a null among them produced a
`NullReferenceException` from inside the projection — undocumented, and naming neither the argument
nor the position at fault. `Append`, `ProviderSessionSeed` and `ProviderTurn` all reject a null
element with an `ArgumentException` naming the parameter, so `Render` does the same. The sequence is
materialized once rather than enumerated twice, because a caller may supply a lazy one and its
generator must not be asked to produce the same run again for the validation.

**Preconditions:** `entries` is not null and contains no null entry; an empty sequence renders as an
empty string.

### Error Handling

- **Null entry text** — `ArgumentNullException` propagates
- **Undefined entry kind** — `ArgumentOutOfRangeException` propagates, naming the kind
- **`ToolCall` or `ToolResult` with a null or blank identifier** — `ArgumentException` propagates
- **Any other kind carrying an identifier** — `ArgumentException` propagates, naming the kind
- **Null entry appended** — `ArgumentNullException` propagates
- **Null entry within an appended sequence** — `ArgumentException` propagates
- **Null entry within a rendered sequence** — `ArgumentException` propagates
- **Negative split budget** — `ArgumentOutOfRangeException` propagates

A null in the transcript would fail later, at a rotation, far from the code that put it there, so
every entry is validated as it arrives.

### Dependencies

- **TokenEstimator** — supplies the character ratio and the per-entry framing allowance; see
  *TokenEstimator Unit Design*.

### Callers

`ContextLayout` holds a transcript as its tier zero and includes it in the seed. `RotationEngine`
calls `SplitAtBudget` at the start of every rotation and `Render` to build the material handed to
the summarizer. `CompactingAgentSession` appends the outgoing message and everything a turn
produced.
