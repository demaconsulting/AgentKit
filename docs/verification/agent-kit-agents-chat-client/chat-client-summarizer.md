## ChatClientSummarizer Unit Verification Design

This document describes the unit-level verification strategy for the `ChatClientSummarizer` class.

### Verification Approach

`ChatClientSummarizer` is verified through unit tests that consolidate real `ConsolidationRequest`
values through the same hand-written recording `IChatClient` the session tests use, and assert on the
request the summarizer produced and the record it returned.

The prompt assertion is made against `ConsolidationPrompt.Compose` itself rather than against a
transcription of its text. That is deliberate: the requirement is that this summarizer sends the
prompt **the library publishes**, so an assertion holding its own copy would keep passing after the
published prompt changed, which is the one way this class can silently stop doing its job. Core's
prompt and request types are therefore used as themselves rather than stubbed.

**What is out of automated scope, stated honestly.** The quality of a consolidation is not verified
here and cannot be: what survives a consolidation is a model's judgment, and the prompt that asks for
it is Core's to specify and Core's design to justify. These tests verify that the right prompt goes
out, that nothing else goes with it, and that what comes back is returned faithfully.

Unit tests reside in `ChatClientSummarizerTests.cs`, with the recording client in
`RecordingChatClient.cs`, both within the `DemaConsulting.AgentKit.Agents.ChatClient.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no model is contacted and **no network access is used**
- **Mocking**: A hand-written recording `IChatClient`; no mocking framework
- **Isolation**: Each test constructs its own client, summarizer and requests; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any prompt other than the composed one, any tool offered, any history carried
between consolidations, any empty answer raised as a failure, any record altered on its way back, or
any invalid argument accepted rather than refused constitutes a failure.

### Test Scenarios

#### AgentKitAgentsChatClient-ChatClientSummarizer-SendsTheConsolidationPrompt: The Published Prompt Goes Out

**Tests**:

- `ChatClientSummarizer_Consolidate_SendsTheComposedConsolidationPrompt`
- `ChatClientSummarizer_Consolidate_ReturnsTheRecordTheModelProduced`

Consolidates a request naming material and an aggressiveness clause, and asserts the client received
exactly one user message carrying exactly the text `ConsolidationPrompt.Compose` produces for that
request. The second scenario asserts the model's answer is what the summarizer returns, unaltered.

#### AgentKitAgentsChatClient-ChatClientSummarizer-CarriesNoToolsOrHistory: A Pure Function of Its Material

**Tests**:

- `ChatClientSummarizer_Consolidate_OffersNoTools`
- `ChatClientSummarizer_Consolidate_SecondCall_CarriesNoHistoryFromTheFirst`

Asserts the request carried no options at all, so no tool was published to the model. The second
scenario consolidates two different bodies of material through one summarizer and asserts the second
request is a single message carrying the second prompt with no trace of the first — the property that
lets one summarizer serve every rotation of every session.

#### AgentKitAgentsChatClient-ChatClientSummarizer-EmptyAnswerIsAnEmptyRecord: An Empty Answer Is Not a Failure

**Test**: `ChatClientSummarizer_Consolidate_EmptyAnswer_ReturnsEmpty`

Boundary value. Scripts a client answering with nothing and asserts an empty record comes back with no
exception. The engine treats a consolidation it could not obtain as material to keep rather than
material to lose, so raising here would fail the turn an application asked for over a step the engine
has already made safe.

#### AgentKitAgentsChatClient-ChatClientSummarizer-RejectsInvalidArguments: Bad Arguments Are Refused

**Tests**:

- `ChatClientSummarizer_Constructor_NullClient_Throws`
- `ChatClientSummarizer_Consolidate_NullRequest_Throws`
- `ChatClientSummarizer_Consolidate_Canceled_ThrowsAndSendsNothing`

Error paths. A missing client and a missing request are refused where the application composed them.
The cancellation scenario asserts not only the `OperationCanceledException` but that the client
received no request, so a canceled consolidation costs no call to what is usually a separate,
separately billed model.
