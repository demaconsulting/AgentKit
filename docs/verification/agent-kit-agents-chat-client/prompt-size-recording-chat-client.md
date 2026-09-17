## PromptSizeRecordingChatClient Unit Verification Design

This document describes the unit-level verification strategy for the
`PromptSizeRecordingChatClient` class.

### Verification Approach

`PromptSizeRecordingChatClient` is verified through unit tests that use it as its callers do — as an
`IChatClient` — rather than by reaching past that interface. The class is internal and is reachable
here only because the package makes its internals visible to this test project; it is nonetheless
exercised through the two members a chat client has, so a test proves what a caller would observe.

**The decisive scenario runs it in the arrangement it exists for.** The class is placed beneath a
real `FunctionInvokingChatClient`, a tool is offered, and a provider is scripted to call that tool
and then answer — which is two requests reporting two different prompt sizes. The scenario asserts
both figures in one place: the response the tool-invoking layer returns reports their **sum**, and
the recorded figure is the **last** request's prompt. Asserting only the second would leave the
reader to take the aggregation on trust, and the difference between the two numbers is the whole
reason the class exists.

The tool-invoking layer is used as itself rather than simulated. Whether it aggregates usage is a
property of that off-the-shelf component and is exactly what is under verification here; a stand-in
would verify the stand-in. The provider beneath it is the hand-written recording `IChatClient` the
rest of this system's unit tests use, which contacts nothing and answers from a script — and whose
usage figures are scripted because usage is the fact under test.

**What is out of automated scope, stated honestly.** A live run against a real provider is not
automated: no assertion here needs a network, credentials or a model's non-determinism, and a live
model would contribute only its own tokenizer's arithmetic to a figure this class copies rather than
computes.

Unit tests reside in `PromptSizeRecordingChatClientTests.cs`, with the recording client in
`RecordingChatClient.cs`, both within the `DemaConsulting.AgentKit.Agents.ChatClient.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no provider is contacted and **no network access is used**
- **Mocking**: A hand-written recording `IChatClient`; no mocking framework
- **File system**: None
- **Isolation**: Each test constructs its own provider and recorder; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any recorded figure that is a total rather than one request's prompt, any figure
reported before a request has reported one, any figure cleared by a later request that reported none,
any streamed usage that fails to be recorded, and any update the caller fails to receive constitutes
a failure.

### Test Scenarios

#### AgentKitAgentsChatClient-PromptSizeRecordingChatClient-RecordsEachRequestsPrompt: One Prompt, Not a Total

**Tests**:

- `PromptSizeRecordingChatClient_GetResponse_SeveralRequests_KeepsTheMostRecentPrompt`
- `PromptSizeRecordingChatClient_GetResponse_BeneathToolInvocation_KeepsTheLastPromptWhileTheTurnReportsTheSum`

The first scenario makes two requests reporting different prompt sizes and asserts the reading after
each is that request's own figure, so the second replaced the first rather than being added to it.

The second is the scenario that pins the rule, and it is arranged as the defect was. A tool is
offered, a provider is scripted to call it and then answer, and the class sits beneath a real
tool-invoking layer. Two requests are made; the response the layer returns reports 250, and the
recorded figure is 150. Both are asserted, because the point is the difference: 250 is what a session
reading `ChatResponse.Usage` would take as occupancy, and it is larger than the conversation ever
was. The scripted sizes are unequal and neither is a factor of their sum, so no reading can be
mistaken for another by coincidence.

#### AgentKitAgentsChatClient-PromptSizeRecordingChatClient-ReportsNothingUntilOneIsReported: Nothing Is Not Zero

**Tests**:

- `PromptSizeRecordingChatClient_LastPromptTokens_BeforeAnyRequest_IsNull`
- `PromptSizeRecordingChatClient_GetResponse_RequestReportingNoUsage_KeepsThePreviousReading`

Boundary values. The first reads the figure before any request has been made and asserts there is
none, rather than a figure of zero — a session reads the difference to tell a conversation that has
not started from one occupying nothing. The second scripts a provider reporting a prompt size and
then reporting none, and asserts the figure that was reported still stands: a tool-calling turn makes
several requests and a provider need not report usage on every one of them, so clearing it there
would make a session that had been told the truth refuse the turn that followed.

#### AgentKitAgentsChatClient-PromptSizeRecordingChatClient-RecordsStreamedUsage: Usage Arrives as an Update

**Test**: `PromptSizeRecordingChatClient_GetStreamingResponse_RecordsTheStreamedUsageAndPassesUpdatesThrough`

Consumes a whole streamed answer and asserts two things together: the usage carried within the stream
was recorded, and the answer's content still reached the caller. The second assertion is not
incidental — a decorator that consumed what it read would leave its caller with an answer missing its
content, and no assertion about the recorded figure would notice.
