## AgentKitChatReducer Unit Verification Design

This document describes the unit-level verification strategy for the `AgentKitChatReducer` class.

### Verification Approach

`AgentKitChatReducer` is verified through unit tests that hand it a history and assert on the history
it returns and on the consolidation request it made. The summarizer is the deterministic
`FakeSummarizer` already used for the rotation engine, which records every request; for cancellation
the `InattentiveSummarizer`, which never observes its token, proves the reducer checks the token
itself. A real model would add non-determinism and observe nothing more: the reducer's behavior is the
returned list and the request. `TranscriptEntry`, `ConsolidationRequest` and `ConsolidationPrompt` are
used as real dependencies, documented in *AgentKitChatReducer Unit Design*. Tail messages are asserted
by reference, so a change that copied or rewrote them would fail.

Unit tests reside in `AgentKitChatReducerTests.cs` within the `DemaConsulting.AgentKit.Core.Tests`
project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK, using asynchronous test methods
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: `Microsoft.Extensions.AI.Abstractions`; no model, file system or network access
- **Mocking**: The hand-written `FakeSummarizer` and `InattentiveSummarizer`; no mocking framework
- **Isolation**: Each test constructs its own reducer and summarizer

### Acceptance Criteria

A unit test run passes when all thirteen scenarios below pass. Any history within the threshold that is
changed or consolidated, any system message or kept turn lost, altered or reordered, any tool call
separated from its result, any request with the wrong tier, clause or material, any previous record
kept beside the new one, any ordinary assistant message treated as a record, any material lost on a
blank record, any null record accepted, any image bytes in the material, any invalid construction
accepted, or any cancellation ignored constitutes a failure.

### Test Scenarios

#### AgentKitCore-AgentKitChatReducer-UnderThresholdUnchanged: A History Within the Threshold Is Unchanged

**Test**: `AgentKitChatReducer_ReduceAsync_UnderThreshold_ReturnsMessagesUnchanged`

A three-turn history under a 10,000-character threshold is returned as the same messages in order, and
the summarizer is not called.

#### AgentKitCore-AgentKitChatReducer-KeepsSystemAndVerbatimTail: The System Message, a Record and the Tail

**Test**: `AgentKitChatReducer_ReduceAsync_OverThreshold_KeepsSystemMessagesAndVerbatimTail`

A system message and six turns over a 50-character threshold with two verbatim turns. Asserts the
result is the same system message, an assistant message that is exactly the header and the record, and
the last four messages as the same instances; the summarizer is called once.

#### AgentKitCore-AgentKitChatReducer-KeepsSystemAndVerbatimTail: Nothing Older Than the Tail Is Unchanged

**Test**: `AgentKitChatReducer_ReduceAsync_NotEnoughTurns_ReturnsMessagesUnchanged`

Two turns with two kept, over the threshold: returned unchanged and the summarizer is not called.

#### AgentKitCore-AgentKitChatReducer-ToolPairsIndivisible: A Tool Call Stays With Its Result

**Test**: `AgentKitChatReducer_ReduceAsync_TailBoundary_StartsAtUserMessage_NeverSplitsToolPair`

The last turn holds a user message, a function call, its result and an answer; with one turn kept,
asserts those four follow the record unchanged.

#### AgentKitCore-AgentKitChatReducer-ConsolidatesThroughSummarizer: The Request Uses the Prompt for the Level

**Test**: `AgentKitChatReducer_ReduceAsync_ConsolidationRequest_UsesConsolidationPromptForLevel`

At the high level with one turn kept over three turns, asserts one request at tier one with
`ConsolidationPrompt.HighInstruction` and the material exactly
`USER: question 1\nASSISTANT: answer 1\nUSER: question 2\nASSISTANT: answer 2`.

#### AgentKitCore-AgentKitChatReducer-FoldsPreviousRecord: A Previous Record Folds

**Test**: `AgentKitChatReducer_ReduceAsync_PreviousRecord_IsFoldedIntoMaterial`

A history opening with a previous record. Asserts the material opens with `RECORD: first record`, the
result holds exactly one record message, and it carries the new record.

#### AgentKitCore-AgentKitChatReducer-RecognizesOnlyItsOwnRecords: A Reply Beginning With the Header Is Assistant Text

**Test**: `AgentKitChatReducer_ReduceAsync_AssistantTextStartingWithHeader_IsConsolidatedAsAssistantText`

Older messages hold an assistant reply that begins with the header words without a line break, and an
assistant message whose text is the header, a line break and more text alongside a tool call. Asserts
the material carries both as `ASSISTANT:` lines, keeps the `TOOL CALL [c1]` line, and holds no
`RECORD:` line.

#### AgentKitCore-AgentKitChatReducer-RecognizesOnlyItsOwnRecords: A Lone Reply Beginning With the Header Is Consolidated

**Test**: `AgentKitChatReducer_ReduceAsync_OnlyOlderMessageStartsWithHeader_IsStillConsolidated`

The only message older than the kept turn is an assistant reply beginning with the header words.
Asserts the summarizer is called once with that reply as an `ASSISTANT:` line, the result opens with
the new record, and the kept turn follows as the same instances.

#### AgentKitCore-AgentKitChatReducer-BlankRecordKeepsHistory: A Blank Record Keeps the History

**Test**: `AgentKitChatReducer_ReduceAsync_BlankRecord_ReturnsMessagesUnchanged`

A summarizer answering whitespace; asserts the history is returned unchanged.

#### AgentKitCore-AgentKitChatReducer-BlankRecordKeepsHistory: A Null Record Is Reported

**Test**: `AgentKitChatReducer_ReduceAsync_NullRecord_ThrowsInvalidOperationException`

A summarizer answering null; asserts `InvalidOperationException`.

#### AgentKitCore-AgentKitChatReducer-NoImageBytesInMaterial: Images Are Described, Not Carried

**Test**: `AgentKitChatReducer_ReduceAsync_ImageContent_RenderedAsPlaceholderInMaterial`

An older tool result holding a caption and a 5,000-byte PNG. Asserts the material holds
`TOOL RESULT [c1]: A chart.` followed by `[image/png, 5000 bytes]` and no base64.

#### AgentKitCore-AgentKitChatReducer-RejectsInvalidArguments: Invalid Construction Is Refused

**Test**: `AgentKitChatReducer_Constructor_InvalidArguments_Throw`

A null summarizer, a zero threshold, zero verbatim turns and an undefined level each throw.

#### AgentKitCore-AgentKitChatReducer-HonorsCancellation: Cancellation Is Honored

**Test**: `AgentKitChatReducer_ReduceAsync_Cancelled_Throws`

A token canceled before the call, and a token canceled by a summarizer that ignores it, each produce
an `OperationCanceledException`.
