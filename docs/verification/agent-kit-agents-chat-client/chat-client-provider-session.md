## ChatClientProviderSession Unit Verification Design

This document describes the unit-level verification strategy for the `ChatClientProviderSession`
class.

### Verification Approach

`ChatClientProviderSession` is verified through unit tests that exercise it exactly as the session
engine does — construct it from a seed, take turns, read the occupancy, release it — against a
hand-written `IChatClient` that contacts nothing, records every request it was handed, and answers
from a script the test wrote.

The double is written by hand rather than substituted for two reasons. The streaming member returns
an asynchronous sequence a substitute would have to be taught to produce, and the request record is
the point of the double rather than an incidental capability: what this class sends is half of what
is under verification, and it is observable nowhere else. **Usage is scripted because it is the fact
under test.** A real provider reports the input tokens it counted for the request it just answered,
and this class's account of the window comes from nowhere else, so the double reports figures the
test chose — including, deliberately, no figure at all, which is the condition the class refuses
rather than estimates around.

Core's session types — `ProviderSessionSeed`, `ProviderTurn`, `TranscriptEntry` and `ContextUsage` —
are used as themselves rather than stubbed. They are the contract this class implements, and
substituting them would verify the substitute.

**What is out of automated scope, stated honestly.** Two things are not asserted directly. A live run
against a real provider is not automated: no assertion here needs a network, credentials or a model's
non-determinism, and the tokenizer differences between providers are exactly what this class declines
to reason about. And the clearing of the conversation on release is observable only through the
refusal that follows it, because the class publishes no view of what it holds; the tests assert the
refusal, that the session reports itself released, and that nothing further reaches the provider.

Unit tests reside in `ChatClientProviderSessionTests.cs`, with the recording client in
`RecordingChatClient.cs`, both within the `DemaConsulting.AgentKit.Agents.ChatClient.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider is contacted and **no network access is used**
- **Mocking**: A hand-written recording `IChatClient`; no mocking framework
- **File system**: None
- **Isolation**: Each test constructs its own client, seed and session; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any seeded message list in the wrong order, any turn that fails to resend the
accumulated conversation, any occupancy figure that is not the provider's own or is not taken against
the supplied window, any non-zero reading before a first turn, any answer accepted without a usage
report, any tool call recorded without its identifier, any answer recorded twice, any released session
that accepts a turn, or any disposal that reaches the caller's client constitutes a failure.

### Test Scenarios

#### AgentKitAgentsChatClient-ChatClientProviderSession-SeedsAMessageList: A Seed Becomes a Message List

**Tests**:

- `ChatClientProviderSession_Send_Seed_ProducesInstructionsHistoryAndToolsInOrder`
- `ChatClientProviderSession_Send_SeedWithoutInstructions_SendsNoSystemMessage`

Seeds a session with instructions, one tool, and a history holding every entry kind a rotation can
produce — a user message, an answer, and a tool call with its result — then takes a turn and asserts
the whole conversation the provider received, in order: the system message first, the history next,
and the new message last, with the seeded tool offered as a tool rather than as conversation. The
second scenario is the boundary: a seed carrying no instructions contributes no system message, and a
seed carrying no tools sends no options at all.

#### AgentKitAgentsChatClient-ChatClientProviderSession-SendsTheWholeConversationEachTurn: The Whole Conversation Again

**Test**: `ChatClientProviderSession_Send_SecondTurn_SendsTheWholeConversationAgain`

Takes two turns against a client answering each distinguishably, and asserts the second request
repeats the first message and the answer the provider gave to it before the new message. A session
that sent only the new message would pass every other scenario here and still present a stateless
provider with an isolated turn, so the accumulated conversation is asserted as a whole rather than by
its length.

#### AgentKitAgentsChatClient-ChatClientProviderSession-ReportsProviderUsage: The Occupancy Is the Provider's Own

**Tests**:

- `ChatClientProviderSession_CurrentUsage_AfterTurn_ReportsTheProvidersInputTokens`
- `ChatClientProviderSession_CurrentUsage_ProviderOverrunsTheWindow_ReportsFull`

Scripts input-token counts no estimate would arrive at, takes two turns, and asserts each reading is
the figure reported for that turn, taken against the supplied window and attributed wholly to the
conversation. The boundary scenario scripts a provider reporting more input than the window holds and
asserts the reading is the window itself — the usage shape refuses anything larger, and being at the
limit is both truthful and the reading that rotates.

#### AgentKitAgentsChatClient-ChatClientProviderSession-ZeroBeforeTheFirstTurn: Nothing Sent Means Nothing Occupied

**Test**: `ChatClientProviderSession_CurrentUsage_BeforeFirstTurn_IsZero`

Constructs a session from the seed a rotation produces — a consolidated record and a verbatim turn —
and reads the occupancy before any turn, asserting it is zero out of the supplied window and that the
provider was contacted not at all. The seed is deliberately non-empty: the reading is taken in the
state every replacement session is created in, which is the state a naive implementation would report
the seed's size for.

#### AgentKitAgentsChatClient-ChatClientProviderSession-RefusesUnreportedUsage: An Answer Without Usage Is Refused

**Test**: `ChatClientProviderSession_Send_ProviderReportsNoUsage_Throws`

Error path. Scripts a client answering normally while reporting no usage at all, and asserts the turn
fails with an `InvalidOperationException` whose message names the missing fact and the remedy. The
message is asserted rather than only the exception type, because a refusal an application cannot act
on is no better than the guess this class declines to make.

#### AgentKitAgentsChatClient-ChatClientProviderSession-RecordsToolTraffic: Paired Entries, Answer Recorded Once

**Tests**:

- `ChatClientProviderSession_Send_ToolCallAndResult_AreRecordedAsAPairCarryingTheCallId`
- `ChatClientProviderSession_Send_AnswerAlongsideAToolCall_IsNotRecordedTwice`
- `ChatClientProviderSession_Send_ToolCallWithoutArguments_RecordsTheBareCall`

Scripts a provider answering as a function-invocation loop does — a call, its result, then the answer
— and asserts the entries the turn produced: the call and the result paired under the provider's own
identifier, the call rendered by name and arguments, and the answer closing the turn.

The second scenario is the one that pins the rule. The provider puts its answer on the same message as
the call it is making, **and a tool result arrives after that message**, so the answer is not the
final entry the mapping produces. An implementation that recorded the message's text there would
produce two assistant entries carrying the same answer, which is what the scenario asserts against.
Scripting the call message last instead would pass either way, because `ProviderTurn` declines to
append an answer already standing as the final entry — so the scenario is arranged precisely to
distinguish them. The third is the boundary: a call taking no arguments is recorded as the bare call.

#### AgentKitAgentsChatClient-ChatClientProviderSession-ReleasesWithoutDisposingTheClient: Release Ends the Session

**Tests**:

- `ChatClientProviderSession_Dispose_MarksReleasedAndRejectsLaterTurns`
- `ChatClientProviderSession_Dispose_DoesNotDisposeTheSuppliedClient`
- `ChatClientProviderSession_Dispose_Twice_IsPermitted`

Releases a session that has taken a turn and asserts it reports itself released, refuses a further
turn with `ObjectDisposedException`, and sends nothing more. The second scenario counts disposals on
the client and asserts none: it then creates a replacement over the same client and takes a turn on
it, which is what a rotation does and what a session that disposed the client would have made
impossible. The third asserts repeated release is permitted, because a rotation and a disposal may
both reach the same session.

#### AgentKitAgentsChatClient-ChatClientProviderSession-RejectsInvalidArguments: Bad Arguments Are Refused

**Tests**:

- `ChatClientProviderSession_Constructor_NullClient_Throws`
- `ChatClientProviderSession_Constructor_NullSeed_Throws`
- `ChatClientProviderSession_Constructor_NonPositiveWindow_Throws`
- `ChatClientProviderSession_Send_NullMessage_Throws`
- `ChatClientProviderSession_Send_Canceled_ThrowsAndSendsNothing`

Error paths. A missing client, a missing seed, a window of zero or a negative window, and a missing
message are each refused where the application wrote them. The cancellation scenario asserts not only
the `OperationCanceledException` but that the client received no request at all, so a canceled turn
costs no call and leaves no message the provider saw but the transcript does not record.
