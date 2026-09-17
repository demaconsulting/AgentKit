## CopilotProviderSession Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotProviderSession` class.

### Verification Approach

`CopilotProviderSession` is verified through unit tests that exercise it exactly as the session
engine does — obtain it from `CopilotProviderSessionFactory`, take turns, read the occupancy, release
it — against a scripted turn channel that contacts nothing, records every prompt it was handed, and
replays a sequence of session events the test wrote.

**Every session under test is obtained from the factory, because that is the only way one exists.**
The constructor is internal and takes a channel whose session was created with a matching observer
already registered as its event handler, so a test that constructed a session around a bare channel
would exercise an arrangement no application can have — one that answers turns and reports no
occupancy at all.

**The events driven through it are the SDK's own event objects.** Every session event type the
adapter matches on has a public constructor taking no arguments and a settable payload, so the tests
construct genuine `SessionUsageInfoEvent`, `ToolExecutionStartEvent`, `ToolExecutionCompleteEvent`,
`AssistantMessageEvent`, `SessionTruncationEvent`, `SessionCompactionStartEvent` and
`SessionErrorEvent` instances and dispatch them through the adapter's own matching code. Nothing
about the event shapes is simulated, which is what makes an offline test of this adapter worth
running.

**Usage is scripted because it is the fact under test.** Copilot's occupancy, limit and conversation
split arrive as an event and this class's account of the window comes from nowhere else, so the
scripted runtime reports figures the test chose — including, deliberately, no figure at all, which is
the condition the class refuses rather than estimates around, and figures beyond the representable
range, which is the condition it clamps rather than throws on.

Core's session types — `ProviderSessionSeed`, `ProviderTurn`, `TranscriptEntry` and `ContextUsage` —
are used as themselves rather than stubbed. They are the contract this class implements, and
substituting them would verify the substitute.

**What is out of automated scope, stated honestly.** A live run against the Copilot runtime is not
automated: no assertion here needs a network, a credential or a model's non-determinism. Two
behaviors follow from that and are stated rather than implied. **Whether a live conversation ever
crosses the raised compaction threshold before the engine rotates it is unverified** — the threshold
is a margin rather than a guarantee, and what is verified is that a session which sees the runtime
compact or truncate refuses the next turn, naming the cause. And **whether a live runtime emits its
usage event before the session goes idle is unverified** — what is verified is that a turn reporting
no usage is refused rather than reported with a stale or invented figure. The argument checks in the
internal constructor are not exercised directly either: an application reaches this class only
through the factory, so they are a guard on an internal contract rather than behavior an application
can trip.

Unit tests reside in `CopilotProviderSessionTests.cs`, with the scripted runtime in
`FakeCopilotTurnChannel.cs`, both within the `DemaConsulting.AgentKit.Agents.Copilot.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Copilot CLI is started, **no credential is used**, and no network
  access is made
- **Mocking**: A hand-written scripted turn channel replaying the SDK's own event types; no mocking
  framework
- **File system**: None
- **Isolation**: Each test constructs its own runtime, factory, seed and session; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any answer recorded twice or not at all, any tool call recorded without the
runtime's own identifier, any nested agent's traffic entering the transcript, any occupancy figure
that is not the one the runtime reported, any non-zero reading before a first turn, any turn accepted
with no usage reported, any turn accepted after the runtime rewrote the history, any empty answer
recorded in place of a runtime failure, any released session that accepts a turn, or any disposal
that reaches the caller's client constitutes a failure.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotProviderSession-SendsOneTurnPerMessage: One Prompt, One Answer, Recorded Once

**Test**: `CopilotProviderSession_Send_Answer_IsReturnedAndRecordedOnce`

Scripts a turn that reports its occupancy and produces one assistant message, and asserts the answer
comes back, that the recorded history holds it exactly once, and that exactly one prompt reached the
runtime. Recording it once is the assertion that matters: the runtime delivers its final assistant
message **twice** — as an event the observer collects and as the value the wait returns — so an
implementation that recorded both would show the model saying the same thing twice and would bill the
conversation for it at every rotation.

#### AgentKitAgentsCopilot-CopilotProviderSession-RecordsToolTraffic: Identified Pairs, and Nothing a Child Did

**Tests**:

- `CopilotProviderSession_Send_ToolCallAndResult_AreRecordedAsAPairCarryingTheRuntimesCallId`
- `CopilotProviderSession_Send_NestedAgentEvents_AreNotRecorded`

Scripts a turn that calls a tool, receives a result, and then answers, and asserts the three entries
in order with the call and its result sharing the runtime's own identifier — which is what lets a
rotation keep them together. The second scenario scripts a delegated run: the runtime starts a nested
agent, which produces tool traffic and a message of its own under the call that started it. The test
asserts this conversation's call and its result are recorded and the child's three events are not.
That distinction is worth its own scenario because a transcript that absorbed a child's traffic would
seed a future session with a conversation the model was never shown.

#### AgentKitAgentsCopilot-CopilotProviderSession-ReportsTheRuntimesUsage: The Runtime's Figures, Passed Through

**Tests**:

- `CopilotProviderSession_CurrentUsage_AfterTurn_ReportsTheRuntimesOccupancyAndLimit`
- `CopilotProviderSession_CurrentUsage_ConversationSplit_IsPassedThroughNotInferred`
- `CopilotProviderSession_CurrentUsage_NoSplitReported_CreditsItAllToTheConversation`
- `CopilotProviderSession_CurrentUsage_LongFiguresBeyondIntRange_AreClamped`
- `CopilotProviderSession_CurrentUsage_ConversationBeyondTheTotal_IsHeldToIt`

Scripts occupancy and limit figures no estimate would arrive at and asserts both come back unchanged;
then scripts a session whose overhead is most of what it occupies and asserts the conversation is the
runtime's own figure and the overhead is the difference, rather than the whole reading being treated
as conversation. The third is the boundary on the other side: a runtime reporting no split is not
given an invented one — the whole reading is credited to the conversation, which allows no overhead
at all and so rotates strictly earlier than a correct split would.

The last two are the arithmetic this class must not get wrong in the direction that throws. A runtime
reporting figures beyond the representable range is clamped, because reading usage is documented as
unable to fail; a runtime reporting a conversation larger than its own total is held to the total,
because `ContextUsage` refuses that combination and this property must not throw. Both are scripted
explicitly rather than argued, because both are exactly the kind of boundary that is reasoned about
once and then broken by a later edit.

#### AgentKitAgentsCopilot-CopilotProviderSession-ZeroBeforeTheFirstTurn: Nothing Sent Means Nothing Occupied

**Test**: `CopilotProviderSession_CurrentUsage_BeforeFirstTurn_OccupiesNothing`

Constructs a session from the seed a rotation produces — a consolidated record and a verbatim turn —
and reads the occupancy before any turn, asserting nothing is occupied, that the window is
representable, and that the runtime was contacted not at all. The seed is deliberately non-empty:
the reading is taken in the state every replacement session is created in, which is the state a naive
implementation would report the seed's size for and rotate on immediately.

#### AgentKitAgentsCopilot-CopilotProviderSession-RefusesUnreportedUsage: A Turn Reporting No Usage Is Refused

**Test**: `CopilotProviderSession_Send_NoUsageReported_Throws`

Error path. Scripts a runtime answering normally while reporting no usage at all, and asserts the turn
fails with a message naming the missing fact. The message is asserted rather than only the exception
type, because a refusal an application cannot act on is no better than the guess this class declines
to make.

#### AgentKitAgentsCopilot-CopilotProviderSession-RefusesARewrittenHistory: A Rewritten History Ends the Session

**Tests**:

- `CopilotProviderSession_Send_ProviderCompactedOrTruncated_RefusesTheTurn`
- `CopilotProviderSession_Send_AfterARewrite_RefusesWithoutSendingTheTurn`

Error path, and the one guarding a defect that is otherwise invisible. Scripts a turn during which
the runtime truncates the history itself — the runtime then answers normally and reports its
occupancy, so **every other assertion in this file would pass**. The test asserts the turn is refused
and that the message names both the truncation and the infinite-session configuration the session was
created with, because that is what an application would need to act on. Without this scenario, two
compactors acting on one conversation would produce no exception anywhere: the engine would simply
start seeding replacements from a history the provider had discarded.

The second proves the refusal is raised *before* the next turn is sent, by asserting the runtime
received no further prompt. That distinction is the whole value of the latch: detecting the first
rewrite can only happen after the fact, but a later turn that reached the runtime would let the model
run the application's tools, with their real side effects, against a conversation the engine no longer
describes — and a host retrying an `InvalidOperationException`, which is the ordinary response, would
cause it again on every attempt. A test asserting only the exception would pass against a session that
reported the divergence and then kept working.

#### AgentKitAgentsCopilot-CopilotProviderSession-RefusesASilentSession: A Silent Session Is Not an Empty Answer

**Tests**:

- `CopilotProviderSession_Send_NoAssistantMessage_ThrowsNamingTheRuntimeError`
- `CopilotProviderSession_Send_NoAssistantMessageAndNoError_ThrowsSayingSo`
- `CopilotProviderSession_Send_ErrorFromAnEarlierTurn_IsNotNamedAsTheCause`

Error paths. The first scripts a runtime that reports an error and produces no assistant message, and
asserts the refusal carries the runtime's own message — recording an empty answer there would present
a runtime failure as a model with nothing to say, and the engine would seed the next session from a
turn that never happened. The second is the boundary: a session that went idle with no answer and no
error is still refused, and says so rather than naming a cause it does not have. The third proves the
named cause belongs to this turn: an error reported during an earlier turn must not be presented as
the explanation for a later silence, because the message states that the runtime reported it and would
therefore read as authoritative while being wrong.

#### AgentKitAgentsCopilot-CopilotProviderSession-RejectsInvalidArguments: Bad Arguments and Failed Turns

**Tests**:

- `CopilotProviderSession_Send_NullMessage_Throws`
- `CopilotProviderSession_Send_Canceled_SendsNothing`
- `CopilotProviderSession_Send_ChannelFails_LeavesNoGhostEntries`

Error paths. A missing message is refused where the application wrote it. The cancellation scenario
asserts not only the exception but that the runtime received no prompt at all, so a canceled turn
costs no call and leaves no message the provider saw but the transcript does not record — and the
scripted channel deliberately does **not** refuse a canceled turn of its own accord, so the assertion
falsifies this class's own check rather than the fake's.

The third scenario is the one that pins the ordering of the turn. A first turn produces tool traffic
and then fails; a second turn then succeeds, and the test asserts the second turn's record holds its
own work and nothing of the first. An implementation that cleared its buffer at the end of a turn
rather than at the start would attribute the abandoned work to the next turn, which is a defect
visible only across two turns.

#### AgentKitAgentsCopilot-CopilotProviderSession-ReleasesItsSessionNotTheClient: Release Ends the Session, Not the Client

**Tests**:

- `CopilotProviderSession_Dispose_DisposesTheRuntimeSession_AndNotTheClient`
- `CopilotProviderSession_Dispose_Twice_IsPermittedAndRejectsLaterTurns`

Releases a session and asserts the runtime session was released exactly once and that a replacement
can still be created on the same runtime afterwards — which a session that had disposed the client
would have made impossible, and which is exactly what a rotation does. The second asserts repeated
release is permitted, that the session reports itself released, that a later turn is refused, and
that nothing further reached the runtime.

#### AgentKitAgentsCopilot-CopilotProviderSession-HonorsAWindowCeilingDownwardOnly: A Ceiling Only Lowers

**Tests**:

- `CopilotProviderSession_CurrentUsage_StatedCeilingBelowTheRuntimes_LowersTheWindow`
- `CopilotProviderSession_CurrentUsage_StatedCeilingAboveTheRuntimes_IsIgnored`

A pair, and the second is the one that matters. The first shows a ceiling below the runtime's limit
becomes the window the session accounts against, which is what makes a rotation reachable on a
runtime whose smallest window is larger than any conversation a test would produce. The second shows
a ceiling above the limit is discarded — the direction in which a mistake would have the engine
believe it has room the provider will not give, and rotate too late, losing material rather than
merely costing money. A single test of the first kind would pass against an implementation that
assigned the ceiling unconditionally.

Verified live as well as in the fake: a Copilot run with a 14,000-token ceiling against a runtime
reporting 272,000 rotated on its fifth turn, and the sixth turn answered correctly from material the
replacement session had never read.
