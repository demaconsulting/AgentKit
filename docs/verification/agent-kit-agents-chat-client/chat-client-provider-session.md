## ChatClientProviderSession Unit Verification Design

This document describes the unit-level verification strategy for the `ChatClientProviderSession`
class.

### Verification Approach

`ChatClientProviderSession` is verified through unit tests that exercise it exactly as the session
engine does — obtain it from `ChatClientProviderSessionFactory`, take turns, read the occupancy,
release it — against a hand-written `IChatClient` that contacts nothing, records every request it was
handed, and answers from a script the test wrote.

**Every session under test is obtained from the factory, because that is the only way one exists.**
The constructor is internal and takes a pipeline only the factory builds, so a test that constructed
a session around a bare client would exercise an arrangement no application can have — one that
records no prompt size and invokes no tool. The recording client therefore sits at the bottom of the
factory's pipeline, which is where a provider sits, and what it observes is what a provider would.

The double is written by hand rather than substituted for two reasons. The streaming member returns
an asynchronous sequence a substitute would have to be taught to produce, and the request record is
the point of the double rather than an incidental capability: what this class sends is half of what
is under verification, and it is observable nowhere else. **Usage is scripted because it is the fact
under test.** A provider reports the input tokens it counted for the prompt it was sent, and this
class's account of the window comes from nowhere else, so the double reports figures the test chose —
including, deliberately, no figure at all, which is the condition the class refuses rather than
estimates around.

Core's session types — `ProviderSessionSeed`, `ProviderTurn`, `TranscriptEntry` and `ContextUsage` —
are used as themselves rather than stubbed. They are the contract this class implements, and
substituting them would verify the substitute.

**What is out of automated scope, stated honestly.** Three things are not asserted here. A live run
against a real provider is not automated: no assertion here needs a network, credentials or a model's
non-determinism, and the tokenizer differences between providers are exactly what this class declines
to reason about. The clearing of the conversation on release is observable only through the refusal
that follows it, because the class publishes no view of what it holds; the tests assert the refusal,
that the session reports itself released, and that nothing further reaches the provider. And the
argument checks in the internal constructor are not exercised directly — an application reaches them
through the factory, which refuses the same arguments first, so they are a guard on an internal
contract rather than behavior an application can observe. The factory's own tests cover what an
application can trip.

Unit tests reside in `ChatClientProviderSessionTests.cs`, with the recording client in
`RecordingChatClient.cs`, both within the `DemaConsulting.AgentKit.Agents.ChatClient.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider is contacted and **no network access is used**
- **Mocking**: A hand-written recording `IChatClient`; no mocking framework
- **File system**: None
- **Isolation**: Each test constructs its own client, factory, seed and session; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any seeded message list in the wrong order, any turn that fails to resend the
accumulated conversation, any occupancy figure that is not the last prompt the provider was sent or
is not taken against the supplied window, any non-zero reading before a first turn, any turn accepted
with no usage reported at all, any tool call recorded without its identifier, any answer recorded
twice, any released session that accepts a turn, or any disposal that reaches the caller's client
constitutes a failure.

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

#### AgentKitAgentsChatClient-ChatClientProviderSession-ReportsProviderUsage: The Occupancy Is the Last Prompt

**Tests**:

- `ChatClientProviderSession_CurrentUsage_AfterTurn_ReportsTheProvidersInputTokens`
- `ChatClientProviderSession_CurrentUsage_ProviderOverrunsTheWindow_ReportsFull`
- `ChatClientProviderSessionFactory_CreateAsync_ToolUsingTurn_ReportsTheLastRequestsPromptNotTheSum`

Scripts input-token counts no estimate would arrive at, takes two turns, and asserts each reading is
the figure reported for that turn, taken against the supplied window and attributed wholly to the
conversation. The second scenario scripts a provider reporting more input than the window holds and
asserts the reading carries the overrun rather than being flattened to the window, because a session
reporting full tells its caller nothing about how far past the limit it went.

The third is the scenario that distinguishes the figure this class reports from the one it must not.
A turn that calls a tool is two requests reporting two different prompt sizes, and the response the
tool-calling layer returns reports their sum; the scenario asserts the occupancy is the last
request's prompt and, explicitly, that it is not the sum. It is placed with the factory's tests
because the factory owns the placement that makes it true, and it is the scenario that fails against
the defect this reading was changed to fix.

#### AgentKitAgentsChatClient-ChatClientProviderSession-ZeroBeforeTheFirstTurn: Nothing Sent Means Nothing Occupied

**Tests**:

- `ChatClientProviderSession_CurrentUsage_BeforeFirstTurn_IsZero`
- `ChatClientProviderSessionFactory_CreateAsync_AfterAnEarlierSessionSpoke_TheReplacementOccupiesNothing`

Constructs a session from the seed a rotation produces — a consolidated record and a verbatim turn —
and reads the occupancy before any turn, asserting it is zero out of the supplied window and that the
provider was contacted not at all. The seed is deliberately non-empty: the reading is taken in the
state every replacement session is created in, which is the state a naive implementation would report
the seed's size for.

The second scenario takes the same reading after an earlier session over the same factory has already
spoken, which is the state a rotation actually produces. A session whose recording pipeline were
shared with its predecessor would report the figure that provoked the rotation and rotate again
immediately; the scenario asserts the predecessor's figure and the replacement's zero together, so
the two cannot be confused.

#### AgentKitAgentsChatClient-ChatClientProviderSession-RefusesUnreportedUsage: A Turn Reporting No Usage Is Refused

**Test**: `ChatClientProviderSession_Send_ProviderReportsNoUsage_Throws`

Error path. Scripts a client answering normally while reporting no usage at all, and asserts the turn
fails with an `InvalidOperationException` whose message names the missing fact and the remedy. The
message is asserted rather than only the exception type, because a refusal an application cannot act
on is no better than the guess this class declines to make. The refusal is reached on the first turn,
which is where a misconfigured client is worth catching; the boundary on the other side — one request
within a turn reporting none, when an earlier one reported a figure — belongs to the recorder and is
covered in _PromptSizeRecordingChatClient Unit Verification Design_.

#### AgentKitAgentsChatClient-ChatClientProviderSession-RecordsToolTraffic: Paired Entries, Answer Recorded Once

**Tests**:

- `ChatClientProviderSession_Send_ToolCallAndResult_AreRecordedAsAPairCarryingTheCallId`
- `ChatClientProviderSession_Send_AnswerAlongsideAToolCall_IsNotRecordedTwice`
- `ChatClientProviderSession_Send_ToolCallWithoutArguments_RecordsTheBareCall`

Scripts a provider answering as a function-invocation loop does — a call, its result, then the answer
— and asserts the entries the turn produced: the call and the result paired under the provider's own
identifier, the call rendered by name and arguments, and the answer closing the turn. Each is
scripted with its result beside it, because that is the only shape a turn can have once the factory
has installed function invocation: a call left unanswered is one the tool-invoking layer would go
back to the provider about, and no turn this session sees ever ends holding one.

The second scenario is the one that pins the rule. The provider puts its answer on the same message as
the call it is making, **and a tool result arrives after that message**, so the answer is not the
final entry the mapping produces. An implementation that recorded the message's text there would
produce two assistant entries carrying the same answer, which is what the scenario asserts against.
Scripting the call message last instead would pass either way, because `ProviderTurn` declines to
append an answer already standing as the final entry — so the scenario is arranged precisely to
distinguish them. The third is the boundary: a call taking no arguments is recorded as the bare call.

A tool that is actually _run_, rather than one whose result is scripted, is exercised in
`ChatClientProviderSessionFactory_CreateAsync_SeededTool_IsInvokedAndItsResultReachesTheTranscript`;
see _ChatClientProviderSessionFactory Unit Verification Design_. The placement that makes it run is
the factory's, so that is where it is proven.

#### AgentKitAgentsChatClient-ChatClientProviderSession-ReleasesWithoutDisposingTheClient: Release Ends the Session

**Tests**:

- `ChatClientProviderSession_Dispose_MarksReleasedAndRejectsLaterTurns`
- `ChatClientProviderSession_Dispose_DoesNotDisposeTheSuppliedClient`
- `ChatClientProviderSession_Dispose_Twice_IsPermitted`

Releases a session that has taken a turn and asserts it reports itself released, refuses a further
turn with `ObjectDisposedException`, and sends nothing more. The second scenario counts disposals on
the client and asserts none: it then creates a replacement from the same factory and takes a turn on
it, which is what a rotation does and what a session that disposed the client would have made
impossible. The third asserts repeated release is permitted, because a rotation and a disposal may
both reach the same session.

#### AgentKitAgentsChatClient-ChatClientProviderSession-RejectsInvalidArguments: Bad Arguments Are Refused

**Tests**:

- `ChatClientProviderSession_Send_NullMessage_Throws`
- `ChatClientProviderSession_Send_Canceled_ThrowsAndSendsNothing`

Error paths. A missing message is refused where the application wrote it. The cancellation scenario
asserts not only the `OperationCanceledException` but that the client received no request at all, so
a canceled turn costs no call and leaves no message the provider saw but the transcript does not
record.

The arguments a session is _constructed_ from are refused by the factory rather than here, and are
covered in _ChatClientProviderSessionFactory Unit Verification Design_. An application supplies a
client and a window there and reaches this constructor through nothing else, so a scenario asserting
the same refusals twice would describe a path no application can take.
