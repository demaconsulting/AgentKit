## AgentKitChatReducer

![AgentKit Core Structure](AgentKitCoreView.svg)

The public `AgentKitChatReducer` class is an `IChatReducer` that consolidates the older part of a
host-owned chat history into one record through the injected `ISummarizer` and `ConsolidationPrompt`,
keeping system messages and a verbatim tail of whole turns.

### Purpose

Not every host adopts `CompactingAgentSession`. A Microsoft Agent Framework `ChatClientAgent` owns its
history through a chat history provider whose `InMemoryChatHistoryProvider` accepts an `IChatReducer`,
and a raw `IChatClient` pipeline can reduce through a reducing client. Both hooks take any
`IChatReducer`. This unit lets such a host keep a long conversation within its window using AgentKit's
consolidation prompt — which preserves named files, values, decisions, errors and outstanding work —
by plugging in one object, with no other part of the session engine.

It is deliberately a reduced-fidelity option. `IChatReducer` sees only a list of messages and returns
a list of messages, so everything the session engine does with the provider's own account of its
window, and with a fresh provider session, is out of reach.

**What it loses compared with `CompactingAgentSession`:**

| `CompactingAgentSession` | `AgentKitChatReducer` |
| --- | --- |
| Tiers 1..n with a verbatim tier 0; older tiers coarser | One record, re-consolidated (a record of a record) |
| Levels chosen from occupancy, with hysteresis | One fixed level, no feedback; a reduction can grow the history |
| Rotation into a fresh, seeded provider session | None; the history is trimmed in place |
| Provider-reported usage (`ContextUsage`, `ContextLayout`) | Characters estimated; no token usage is visible |
| Detects and refuses a runtime-side rewrite of the history | Not applicable |
| `SessionTranscript` of everything consolidated | None |
| Works where the runtime owns the history (GitHub Copilot) | Not available there: Copilot exposes no reducer hook |

The live spike that motivated the unit observed a reduction that grew the history (562 to 760
characters) because the record was longer than the four short messages it replaced — the absence of
feedback in practice. **`CompactingAgentSession` is recommended for the GitHub Copilot path, which has
no alternative, and wherever full fidelity matters;** the reducer is for a host that already owns its
history and wants AgentKit's consolidation in the hook it has.

### Data Model

| Member | Type | Description |
| --- | --- | --- |
| `DefaultVerbatimTurns` | `const int` | 4 — the turns kept word for word when none is specified. |
| `RecordHeader` | `const string` | The record message's first line (see the constant). |
| `_summarizer` | `ISummarizer` | Writes each record; injected, never null. |
| `ThresholdCharacters` | `int` | The estimated size above which a reduction is attempted; greater than zero. |
| `VerbatimTurns` | `int` | The most recent turns kept unchanged; one or greater. |
| `Level` | `CompactionLevel` | The level whose aggressiveness clause each consolidation asks for. |

Every member is set at construction and never changes, so an instance is as safe for concurrent use
as its summarizer.

### Key Methods

#### AgentKitChatReducer(ISummarizer summarizer, int thresholdCharacters, int verbatimTurns, CompactionLevel level)

Rejects a null summarizer (`ArgumentNullException`), a threshold of zero or less, fewer than one
verbatim turn, or an undefined level (`ArgumentOutOfRangeException`).

#### ReduceAsync(IEnumerable&lt;ChatMessage&gt; messages, CancellationToken cancellationToken)

**Algorithm:**

1. Reject null messages; throw if the token is already canceled.
2. Materialize the messages once. Estimate their size: text characters, a function call's name plus
   its arguments serialized to JSON, a function result's text or JSON (binary content counted by its
   bytes), and `DataContent` by its bytes. At or under `ThresholdCharacters`, return the materialized
   list unchanged.
3. Split the system messages from the conversation.
4. Find the tail: the user message opening the last `VerbatimTurns` turns, a turn starting at each
   user message. A tool call and its result always follow their user message, so the boundary can
   never split them. With no more turns than are kept, the tail starts at the first turn.
5. When every message before the tail is a previous record (or there is none), return unchanged.
6. Render the older messages as `TranscriptEntry` lines (`ToTranscriptLine`): a previous record — an
   assistant message opening with `RecordHeader` — as a `ContextRecord`, so records fold; text by role
   as `User` or `Assistant`; a function call as `ToolCall(callId, name(arguments))`; a function result
   as `ToolResult(callId, text)`; image and binary content as `[media type, N bytes]`; other content
   not at all. A blank call identifier is rendered as `unidentified`.
7. Ask the summarizer once: `ConsolidationRequest(1, material, ConsolidationPrompt.InstructionFor(Level))`.
   Throw if the token was canceled by the time it answers, because the summarizer contract only
   permits an implementation to observe cancellation.
8. A null record throws `InvalidOperationException`, as `RotationEngine` does; a blank record returns
   the history unchanged, so the material is kept rather than lost.
9. Return the system messages, then `Assistant(RecordHeader + "\n" + record)`, then the tail as the
   same message instances. The assistant role matches how `ChatClientProviderSession` seeds a context
   record into a fresh session.

**Postconditions:** the messages the caller supplied are never modified; a returned tail message is the
same instance the caller supplied.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null summarizer or messages | `ArgumentNullException` |
| Invalid threshold, verbatim turns or level | `ArgumentOutOfRangeException` |
| Summarizer returns null | `InvalidOperationException` |
| Summarizer returns blank | Not an error; the history is returned unchanged |
| Canceled token | `OperationCanceledException` |
| A value the serializer cannot describe | Not an error; its string form is used in the material |
| A summarizer exception | Propagates unchanged |

### Dependencies

- **Summarizer** — `ISummarizer`, `ConsolidationRequest` and `ConsolidationPrompt`; see *Summarizer
  Unit Design*.
- **SessionTranscript** — `TranscriptEntry` and its `ToTranscriptLine` rendering; see *SessionTranscript
  Unit Design*.
- **CompactionLevel** — the level enumeration, documented with *AgentSession Unit Design*.
- **Microsoft.Extensions.AI.Abstractions** — `IChatReducer`, `ChatMessage` and the content types;
  `AIJsonUtilities.DefaultOptions` serializes call arguments and structured results.

### Callers

A public API entry point: a host passes an instance to the reducer hook it already has — for example
`InMemoryChatHistoryProviderOptions.ChatReducer` in the Microsoft Agent Framework, with
`ReducerTriggerEvent = BeforeMessagesRetrieval` — or calls `ReduceAsync` itself. Nothing within this
system calls it.
