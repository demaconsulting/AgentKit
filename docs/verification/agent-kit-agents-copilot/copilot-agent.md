## CopilotAgent Unit Verification Design

This document describes the unit-level verification strategy for the `CopilotAgent` unit — the
internal `CopilotAgent` class and the public `CopilotAgentSession` it creates.

### Verification Approach

`CopilotAgent` is verified through unit tests that drive it only through the `AIAgent` surface an
application uses — create a session, run, run streaming, serialize and deserialize — over a scripted
Copilot runtime that contacts nothing.

**The agent under test is built through `CopilotAgentFactory.CreateAgent`, the internal seam behind
the public `Create`.** That seam takes the channel openers (one keeping a conversation's session,
one deleting the session of a run made without a conversation) and the channel resumer as parameters
in place of a live client, so the agent is constructed exactly as an application's would be, with the
factory's real configuration builders. Its creation and resumption options are therefore the
production ones, and the confinement assertions below test what an application actually gets rather
than a configuration a test assembled itself.

**The runtime is scripted by `FakeCopilotRuntime`.** Its `Opener` and `Resumer` record every
configuration a session was created with and every identifier and configuration a session was
resumed with, and hand back a scripted channel per run. Each channel records the prompts and the
whole messages it was sent, including attachments, and counts its releases. Each turn is a
`ScriptedTurn` that dispatches a list of events into the handler on the configuration, then returns
an answer, throws a chosen failure, or runs a test-supplied step that can hold the turn open until
released or canceled. Identifiers are allocated in order, so the first session is always
`session-1`.

**Every event replayed is a real SDK event object.** The assistant message, its deltas, the tool
execution start and completion, the per-request usage and the session error are constructed as the
SDK's own types and dispatched through the agent's own mapping, and a sub-agent's events are
produced by setting the SDK's own agent attribution on them. Nothing about the event shapes is
simulated.

**What is out of automated scope, stated honestly.** The SDK's session and client types cannot be
constructed in a test, so nothing here proves that the runtime keeps a released session resumable,
that it accepts an in-memory attachment, or that it emits deltas when streaming is on. Those are
shown on the real runtime by the opt-in live Copilot suite —
`AgentKitAgentsCopilotLive_Agent_Streams_TextDeltas`,
`AgentKitAgentsCopilotLive_Agent_SerializedSession_ResumesOnANewAgent` and
`AgentKitAgentsCopilotLive_Agent_ImageAttachment_ReachesTheModel` — which is supplementary evidence
outside the trace matrix, described in *AgentKitAgentsCopilot System Verification Design*. The
agent's intentional gaps — no typed resume-by-identifier method, no pass-through of runtime events
other than those mapped, no mapping of a URI or hosted-file content, and run options ignored — are
design decisions recorded in *CopilotAgent Design*, not untested behavior.

Unit tests reside in `CopilotAgentTests.cs`, with the scripted runtime in `FakeCopilotTurnChannel.cs`,
both within the `DemaConsulting.AgentKit.Agents.Copilot.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Copilot runtime is started, **no credential is used**, and no
  network access is made
- **Mocking**: The hand-written `FakeCopilotRuntime` opener and resumer, replaying the SDK's own
  event types; no mocking framework
- **File system**: None
- **Isolation**: Each test constructs its own runtime and agent; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any conversation whose first run does not create one confined session and record
its identity, any later run that creates instead of resuming or resumes under a different
confinement, any run that leaves its session unreleased on success, failure or cancellation, any text
streamed twice or not at all, any tool call or result streamed without the runtime's call identifier,
any sub-agent event streamed, any usage figure not carried, any root session error that does not fail
the run, any non-streaming response that differs from the stream, any inline data not sent as an
attachment, any serialized form that loses the identity or the state bag, any concurrent runs that
open two runtime sessions for one conversation, any turn or open failure that reaches the caller as
the SDK's own exception or carries a token anywhere in its chain, or any foreign session accepted
constitutes a failure.

### Test Scenarios

#### AgentKitAgentsCopilot-CopilotAgent-OpensOrResumesPerRun: Create Once, Resume Thereafter

**Tests**:

- `CopilotAgent_Run_FirstRun_CreatesAConfinedSessionAndRecordsItsId`
- `CopilotAgent_Run_LaterRun_ResumesTheRecordedSessionWithTheSameConfinement`

The first runs a fresh conversation once and asserts exactly one session was created, with the
custom-qualified allow-list and a permission handler on its configuration, that nothing was resumed,
and that the conversation now carries the created session's identity. The second runs the same
conversation twice and asserts one creation followed by one resumption **of that same session**,
whose configuration carries the same allow-list, the same tool definitions, the built-in GitHub MCP
server disabled and a permission handler, and that the second prompt reached the resumed session. A
resumption is the one moment the runtime could be handed a looser configuration than the session was
created with, so the confinement is re-asserted there rather than assumed to carry over.

#### AgentKitAgentsCopilot-CopilotAgent-ReleasesAfterEveryRun: Released After Every Run

**Tests**:

- `CopilotAgent_Run_AfterEveryRun_ReleasesTheSessionAndKeepsItResumable`
- `CopilotAgent_Run_WithoutASession_UsesASessionDeletedAfterTheRun`
- `CopilotAgent_Run_OnFailure_ReleasesTheSession`
- `CopilotAgent_Run_OnCancellation_ReleasesTheSession`

The first asserts each run's channel was released exactly once and that the conversation still names the session the
first run created, so the next run resumes it rather than replacing it. The second builds the agent over separate
scripted runtimes behind the keeping and the deleting opener, runs it without a conversation, and asserts the session
was opened through the deleting opener only and released once. The third scripts a turn the runtime fails and asserts
the failure reaches the caller unchanged, the channel was released once, and the session identity is still recorded, so
a failed run does not orphan the conversation. The fourth holds a turn open until the caller cancels it and asserts the
cancellation reaches the caller and the channel was released once. That a release does not delete the session is
asserted on the channel itself, in *CopilotTurnChannel Unit Verification Design*; the scripted channel used here has no
delete step, and no test could detect a deletion through it.

#### AgentKitAgentsCopilot-CopilotAgent-StreamsUpdates: What the Root Agent Produces, as It Arrives

**Tests**:

- `CopilotAgent_RunStreaming_TextDeltas_AreStreamedAsTextContent`
- `CopilotAgent_RunStreaming_MessageWithoutDeltas_IsDeliveredAsText`
- `CopilotAgent_RunStreaming_ToolCallAndResult_AreStreamedWithTheRuntimesCallId`
- `CopilotAgent_RunStreaming_ToolFailure_StreamsTheErrorAsTheResult`
- `CopilotAgent_RunStreaming_NestedAgentEvents_AreNotStreamed`
- `CopilotAgent_RunStreaming_AssistantUsage_IsStreamedAsUsageContent`

The first replays two deltas and then the complete message they belong to, and asserts the text
arrives exactly once, as the two deltas, under the message's identifier and the agent's identity —
the final message must not repeat what already streamed. The second is the boundary: a final message
with no deltas before it is still delivered as text, so a runtime that did not stream loses nothing.
The third asserts a tool call streams under the runtime's call identifier with its name and parsed
arguments, and its result streams under the same identifier, in the tool role, with the result's
content. The fourth asserts a failed tool streams the runtime's error as its result, and a fixed
message when the runtime gave none.

The fifth replays a sub-agent's call, result, delta, message and session error and asserts nothing at
all is streamed and the run does not fail: none of it was part of this conversation, and a sub-agent's
error is the sub-agent's to report. The sixth asserts the runtime's per-request usage is streamed as
usage content carrying the input, output, total and cached-input counts and the cache-write count.

#### AgentKitAgentsCopilot-CopilotAgent-ReturnsTheStreamAsOneResponse: The Same Content, Gathered

**Test**: `CopilotAgent_Run_ReturnsTheStreamedContentAsOneResponse`

Replays a tool call, its result and an answer in two deltas, runs the agent without streaming, and
asserts the response's text is the whole answer and its messages carry the function call. A
non-streaming run that built its response another way could drift from what the stream reports.

#### AgentKitAgentsCopilot-CopilotAgent-DeliversDataContentAsAttachments: Inline Data, in Memory

**Tests**:

- `CopilotAgent_Run_DataContent_IsSentAsABlobAttachment`
- `CopilotAgent_Run_PromptJoinsTheInputMessagesText`

The first runs on a message carrying text and an unnamed PNG and asserts the runtime was sent one
message whose prompt is the text and whose single attachment is an in-memory blob carrying the
image's base64 data, its media type and the generated name `attachment-1.png`. In memory is the
point: an attachment written to a temporary file would need a read permission the default-deny
handler correctly refuses. The second runs on two text messages and asserts the prompt is their text
joined by a newline and that no attachment list is sent when the input carries no data.

#### AgentKitAgentsCopilot-CopilotAgent-SerializesTheSession: Identity and State Bag, Round-Tripped

**Tests**:

- `CopilotAgent_SerializeSession_RoundTripsTheSessionIdAndStateBag`
- `CopilotAgent_DeserializeSession_NonObject_Throws`
- `CopilotAgent_DeserializeSession_NonObjectStateBag_Throws`
- `CopilotAgent_SerializeSession_DuringARun_ReturnsWithoutWaitingForTheRun`

The first runs a conversation once, stores a value in its state bag, serializes it, restores it on a
**second** agent and runs there. It asserts the serialized form carries `sessionId` and a `stateBag`
object, that the restored session carries the same identity and the same state bag value, and that
the second agent's run resumed the same runtime session. The second asserts a serialized form that is
a JSON array is refused with `ArgumentException`.

The third asserts a serialized form whose `stateBag` is present but a string is refused with
`ArgumentException`, rather than restored with its state silently replaced by an empty bag, and that
a form whose `stateBag` is `null` is still restored, carrying its session identity.

The fourth holds a first run's turn open after it has opened its runtime session, serializes the
conversation, and asserts the serialization had already completed before the run was released and
carries the identifier the run recorded. It pins the decision that serialization is a snapshot: a run
holds the conversation's gate across its whole streamed enumeration, so a serialization that waited
on it would deadlock a host serializing from inside its own streaming loop.

#### AgentKitAgentsCopilot-CopilotAgent-SerializesRunsOnASession: One Conversation, One Runtime Session

**Test**: `CopilotAgent_Run_ConcurrentRunsOnOneSession_AreSerialized`

Holds a first run's turn open, starts a second run on the same conversation, and asserts that while
the first held the conversation no second channel was opened; then releases the first and asserts
there was exactly one creation and that the second run resumed the first run's session. Without the
gate, the second run would see no recorded identity and create a second runtime session, forking the
conversation.

#### AgentKitAgentsCopilot-CopilotAgent-ReportsRuntimeErrors: A Root Session Error Fails the Run

**Tests**:

- `CopilotAgent_RunStreaming_SessionError_FailsTheRunNamingTheError`
- `CopilotAgent_RunStreaming_SessionErrorQuotingAToken_IsRedacted`
- `CopilotAgent_RunStreaming_TurnFailureQuotingAToken_IsRelayedRedacted`
- `CopilotAgent_Run_OpenFailureQuotingAToken_IsRelayedRedacted`

Replays a root session error and asserts the run fails with `InvalidOperationException` naming the
runtime's message, and that the channel was still released once. The sub-agent case, which must not
fail the run, is asserted in the streaming scenario above. An agent built with a sign-in that knows a
secret of no GitHub token shape replays a session error quoting that secret and a `gho_` token: the
run fails with exactly `Session error: refused <redacted> and gho_<redacted>`, neither value appears
in the exception's string form, and no cause is attached.

The third scripts the turn itself failing, with no session error event before it, so the failure
reaches the run only through the send: an `InvalidOperationException` in the SDK's `Session error: …`
shape quoting both values, over an `IOException` quoting the secret. The run fails with a new
`InvalidOperationException`, not the scripted one, reading exactly
`Session error: refused <redacted> gho_<redacted>`, with a `CopilotRedactedException` as its cause; a
walk of its whole chain — every inner exception and every inner exception of an aggregate — finds
neither value, and the channel was released once.

The fourth builds an agent whose opener fails with the SDK's lost-connection shape — an `IOException`
reading `CLI process exited unexpectedly.` followed by standard error quoting both values — and whose
resumer fails with an RPC-shaped exception of a type outside the base class library, quoting the
secret in its message and its error data. A run on a new conversation fails with a new `IOException`
reading the redacted text; a run on a conversation that already names a session fails with a new
`InvalidOperationException` reading `resume refused for <redacted>`, whose cause names the RPC-shaped
type; and the walk finds neither value in either chain.

#### AgentKitAgentsCopilot-CopilotAgent-RejectsForeignSessions: Another Agent's Session Is Refused

**Tests**:

- `CopilotAgent_Run_ForeignSession_Throws`
- `CopilotAgent_SerializeSession_ForeignSession_Throws`
- `CopilotAgent_Name_DefaultsWhenNoneSupplied`

The first two pass a session some other kind of agent would have created and assert
`InvalidOperationException` from a run and from serialization; the run also asserts no session was
created, so the refusal happens before the runtime is touched. The third asserts an agent the host
names nothing reports the default name and description, and an agent the host names reports that
name.
