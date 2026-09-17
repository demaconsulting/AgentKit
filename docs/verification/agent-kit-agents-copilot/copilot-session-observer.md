## CopilotSessionObserver Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotSessionObserver` class.

### Verification Approach

`CopilotSessionObserver` is verified by dispatching session events into it directly and reading what
it holds. No channel, no factory and no session is involved, because none is needed: the class is a
pure event handler over a small amount of guarded state, and driving it directly is what makes each
branch of its matching independently falsifiable.

**Every event driven through it is a real SDK event object.** Each session event type the adapter
matches on has a public constructor taking no arguments and a settable payload, so the tests construct
genuine instances and dispatch them through the production matching code. Nothing about the event
shapes is simulated, which is what distinguishes a worthwhile offline test of this class from one
that only verifies a local imitation.

Core's `TranscriptEntry` is used as itself rather than stubbed. It is the contract this class
produces, and substituting it would verify the substitute — in particular, its refusal of a tool
entry with no identifier is exactly why this class skips one.

**What is out of automated scope, stated honestly.** Four things.

- **The threading is not exercised.** The SDK documents that handlers are invoked serially, in
  arrival order, on a background thread, and never concurrently with each other on one session, so
  the lock here guards cross-thread *reads* rather than concurrent writes. A test that raced its own
  assertions would prove nothing about the class and would be non-deterministic; the lock is
  justified by the SDK's documented contract and reviewed rather than measured.
- **Which events a live runtime actually emits, and when, is unverified.** The tests establish what
  this class does with each event; they cannot establish that a live Copilot session emits a usage
  event before it goes idle, or emits a compaction event when it compacts.
- **The registration is not verified here.** That the handler reaches the session before it is
  created is `CopilotProviderSessionFactory`'s doing and is verified there.
- **The per-turn usage answer is not verified here.** It is observable only through the refusal it
  governs, which belongs to the provider session; see the per-turn usage scenario below.

Unit tests reside in `CopilotSessionObserverTests.cs`, with the event builders in
`FakeCopilotTurnChannel.cs`, both within the `DemaConsulting.AgentKit.Agents.Copilot.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Copilot CLI is started, **no credential is used**, and no network
  access is made
- **Mocking**: None; the SDK's own event types are constructed directly
- **File system**: None
- **Isolation**: Each test constructs its own observer; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any usage figure not held as reported, any reading cleared by a turn boundary,
any entry recorded out of arrival order, any entry recorded twice, any previous turn's work carried
into the next, any nested agent's traffic recorded, any unidentified tool call recorded, any empty
assistant message recorded, any failed tool recorded without its error, any reported error surviving
a turn boundary, any compaction or truncation that fails to mark the history rewritten, or any
exception escaping the handler constitutes a failure.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotSessionObserver-HoldsTheLatestUsageReading: The Runtime's Figures, Unchanged

**Tests**:

- `CopilotSessionObserver_OnEvent_UsageInfo_IsHeldAsTheLatestReading`
- `CopilotSessionObserver_OnEvent_SecondUsageInfo_ReplacesTheFirst`
- `CopilotSessionObserver_OnEvent_UsageWithoutSplit_HoldsNoConversationFigure`
- `CopilotSessionObserver_BeginTurn_KeepsTheUsageReading`

Dispatches a usage event carrying three distinct figures and asserts all three are held exactly as
they arrived; then dispatches a second and asserts it replaces the first, because Copilot's usage is
cumulative session state and the newest reading is simply the truth. The third asserts a reading with
no conversation split is held as such rather than given an invented one, so the decision about what
to do with the absence is made where it can be reasoned about.

The fourth is the deliberate difference from the stateless adapter's per-request recorder, and it is
asserted rather than left to inspection: a reading survives a turn boundary. Clearing it each turn
would make a session that had already reported indistinguishable from one that never had, and the
session above would refuse a turn it could perfectly well measure.

#### AgentKitAgentsCopilot-CopilotSessionObserver-RecordsTheTurnsWork: Arrival Order, Once, This Turn Only

**Tests**:

- `CopilotSessionObserver_DrainEntries_ReturnsArrivalOrder`
- `CopilotSessionObserver_DrainEntries_Twice_ReturnsNothingTheSecondTime`
- `CopilotSessionObserver_BeginTurn_ClearsThePreviousTurnsEntries`
- `CopilotSessionObserver_OnEvent_UnrelatedEvent_IsIgnored`

Dispatches a whole turn's worth of events — an announcement, a call, its result, an answer — and
asserts the four entries in the order they arrived, with the pair sharing the runtime's identifier.
Arrival order is what makes the record a description of the turn rather than a set of things that
happened during it.

The second asserts draining empties the buffer, so a turn's work is recorded exactly once even if a
caller asks twice. The third asserts a turn begins by discarding what the last one left behind, which
is what stops the work of a turn that failed part-way being attributed to the next. The fourth is the
negative case: an event this adapter has no use for changes nothing at all — not the reading, not the
entries, not the rewritten flag, not the error — because the runtime emits a great deal that is not
part of a conversation.

#### AgentKitAgentsCopilot-CopilotSessionObserver-RecordsToolOutcomes: Failures, Gaps and Children

**Tests**:

- `CopilotSessionObserver_OnEvent_ToolFailure_RecordsTheErrorAsTheResult`
- `CopilotSessionObserver_OnEvent_ToolCallWithoutAnIdentifier_IsSkipped`
- `CopilotSessionObserver_OnEvent_EmptyAssistantMessage_IsNotRecorded`
- `CopilotSessionObserver_OnEvent_NestedAgentEvents_AreNotRecorded`

The first asserts a failed tool is recorded with the runtime's error as that call's result. The model
saw the failure and reasoned from it, so a history showing the call succeeding — or showing no result
at all — would seed a future session with a conversation that did not happen.

The second dispatches a tool start with a null identifier and one with a blank identifier, and
asserts neither is recorded and nothing is thrown. The SDK marks the identifier required, but the
runtime's events arrive by deserialization, which sets members directly and does not enforce that
modifier — so the absence is representable rather than theoretical, and throwing on the runtime's own
dispatch thread would surface as an SDK fault rather than an AgentKit one.

The third asserts an empty assistant message is not recorded: the runtime emits assistant messages in
phases, and an empty one is not something the model said.

The fourth is the judgment call, asserted so it is a decision rather than an accident. A delegated run
is scripted: this conversation's call, the child's own call, result and message under it, and then
this conversation's result. The test asserts exactly two entries — the outer call and its result — so
the child's three events are demonstrably excluded while the call that started the child is
demonstrably kept.

#### AgentKitAgentsCopilot-CopilotSessionObserver-DetectsRewrittenHistory: Both Ways the Runtime Rewrites

**Tests**:

- `CopilotSessionObserver_OnEvent_TruncationEvent_MarksHistoryRewritten`
- `CopilotSessionObserver_OnEvent_CompactionEvent_MarksHistoryRewritten`
- `CopilotSessionObserver_OnEvent_UnrelatedEvent_IsIgnored`

Compaction and truncation are different runtime behaviors with the same consequence for AgentKit —
the transcript the engine holds stops describing the conversation the provider does — so both are
asserted, and the negative case asserts the flag is not set by anything else. This is the only
evidence available offline that a runtime which ignores the request to stop compacting can be
detected at all; whether a live runtime ever emits these events while AgentKit is driving it is
stated as unverified in *AgentKitAgentsCopilot System Verification Design*.

#### AgentKitAgentsCopilot-CopilotSessionObserver-AnswersWhetherThisTurnReportedUsage: This Turn, Not the Session

**Test**: `CopilotProviderSession_Send_UsageReportedThenOmitted_RefusesTheLaterTurn`

**This requirement's evidence sits one level up, and that is stated rather than disguised.** The
per-turn answer is observable only through the refusal it governs, and the refusal is the provider
session's; a test here could only read the property back after driving the events that set it, which
would restate the implementation rather than falsify it. The scenario that does falsify it reports
usage on a first turn and omits it on a second: the kept reading is non-null when the second turn is
judged, so only a per-turn answer refuses it. A session-wide question would pass, occupancy would sit
frozen at the first turn's figure while the conversation kept growing, and the engine would rotate
later and later against a number that had stopped moving — which is the defect the sibling ChatClient
adapter shipped with. It is described in full in *CopilotProviderSession Unit Verification Design*.

#### AgentKitAgentsCopilot-CopilotSessionObserver-RemembersTheRuntimesError: The Cause of a Silent Turn

**Tests**:

- `CopilotSessionObserver_OnEvent_SessionError_IsRemembered`
- `CopilotSessionObserver_BeginTurn_ClearsTheReportedError`

The first asserts a session error's message is held, which is what lets the provider session's refusal
of a silent turn name a cause an application can act on instead of reporting only that nothing came
back. The second asserts the message does not outlive the turn that reported it — while the usage
reading beside it does — because an error carried into a later turn would name the wrong cause in the
one message whose whole job is to name the right one, and would read as authoritative for saying the
runtime reported it.
