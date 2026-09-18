## OllamaContextSizingChatClient Unit Verification Design

This document describes the unit-level verification strategy for the `OllamaContextSizingChatClient`
decorator.

### Verification Approach

The decorator's whole job is what it puts on a forwarded request and what it leaves alone, so the
tests are built around a recording chat client: a hand-written `IChatClient` that contacts nothing
and keeps the request settings each call arrived with, in order. That record is exactly what the
assertions need, and a real provider would add a server, a model load and a model's
non-determinism without contributing to any of them.

The recorder is written by hand rather than substituted. Its streaming member returns an
asynchronous sequence a substitute would have to be taught to produce, and the per-request record is
the point of the double rather than an incidental capability. It keeps the settings instance that
arrived rather than a copy, because whether the decorator handed on the caller's own object or a
clone of it is itself under test.

One test is different in kind and is the load-bearing one. The claim that a stated window is true by
construction rests entirely on the context-length option reaching the Ollama server — a behavior of
`OllamaSharp`, not of this repository. That is proven by placing a **real** `OllamaApiClient`
beneath the decorator, pointing it at a WireMock.Net server on loopback, holding an ordinary
exchange, and reading the request body the server received. Asserting it against a hand-written fake
would assert this project's beliefs about OllamaSharp back at it rather than test them, which is the
same doctrine the reading tests follow.

**What remains outside automated scope.** That an Ollama server actually *honors* a requested
context length is not automated: it was measured by hand against a live Ollama 0.34.1 server and is
recorded in *OllamaContextWindow Unit Verification Design*. What the tests here prove is that the
request carries the size, on every request, which is the half this repository controls. The sample's
composition of the decorator is likewise not automated, because reaching that code path requires a
live Ollama host.

Unit tests reside in `OllamaContextSizingChatClientTests.cs`, with the recorder in
`RecordingChatClient.cs`, within the `DemaConsulting.AgentKit.Agents.Ollama.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no Ollama server is contacted and **no outbound network access is
  made**. The wire scenario starts a WireMock.Net server on loopback and stops it with the test
- **Mocking**: A hand-written recording `IChatClient` for the option-shaping scenarios; WireMock.Net
  plus the real `OllamaApiClient` for the wire scenario
- **Isolation**: Each test constructs its own recorder, decorator and, where one is needed, its own
  server; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any forwarded request reaching the provider without the chosen context length,
any request after the first losing it, any streamed request behaving differently from a
whole-response one, any option the caller set failing to reach the provider, any write into the
caller's own settings, any caller-named context length overruled, any non-positive size accepted at
composition, and any request body reaching the server without the size constitutes a failure.

### Test Scenarios

#### AgentKitAgentsOllama-OllamaContextSizingChatClient-EveryRequestCarriesTheSize: Every Request, Not Only the First

**Tests**:

- `OllamaContextSizingChatClient_GetResponseAsync_EveryRequest_CarriesTheStatedContextLength`
- `OllamaContextSizingChatClient_GetResponseAsync_NoCallerOptions_StillCarriesTheContextLength`

Normal operation, and the promise the unit exists for. Two successive requests are sent and both
recorded requests are asserted to name the chosen context length; a second scenario sends a request
with no caller settings at all and asserts the size is named anyway. A decorator that sized only the
opening request would pass the second scenario and fail the first, which is precisely why the first
counts rather than merely checking that sizing happens.

#### AgentKitAgentsOllama-OllamaContextSizingChatClient-StreamedRequestsAreNamedToo: Streaming Behaves Identically

**Test**:
`OllamaContextSizingChatClient_GetStreamingResponseAsync_EveryRequest_CarriesTheStatedContextLength`

Normal operation on the second path. Two streamed requests are drained to completion and both are
asserted to name the size. The sequence is enumerated deliberately: a streaming member does no work
until it is, so an un-drained call would record nothing and the scenario would pass vacuously.

#### AgentKitAgentsOllama-OllamaContextSizingChatClient-PreservesTheCallersRequest: Nothing Else Is Disturbed

**Tests**:

- `OllamaContextSizingChatClient_GetResponseAsync_CallerOptions_ReachTheProviderUnchanged`
- `OllamaContextSizingChatClient_GetResponseAsync_AnyRequest_DoesNotMutateTheCallersOptions`

Normal operation and the invariant beneath it. The first sets a temperature, an output limit and an
unrelated additional property, and asserts all three survive alongside the size. The second asserts
that the caller's own settings instance never acquired the size and was not the instance forwarded —
the clone promise, which matters because a session commonly reuses one settings object across every
request and shares it with sibling clients.

#### AgentKitAgentsOllama-OllamaContextSizingChatClient-DoesNotOverruleAStatedSize: The Caller's Size Stands

**Test**:
`OllamaContextSizingChatClient_GetResponseAsync_CallerSuppliedContextLength_IsNotOverwritten`

Boundary condition where the caller and the decorator disagree. A caller naming a different context
length on the request has it forwarded as it named it. A decorator that overruled the caller would
be the one control in the pipeline that cannot be overridden from above.

#### AgentKitAgentsOllama-OllamaContextSizingChatClient-RejectsASizeNoInstanceCouldRun: A Size No Instance Could Run

**Test**: `OllamaContextSizingChatClient_Constructor_NonPositiveContextLength_Throws`

Error path at composition, covering zero and a negative. No Ollama instance can run at such a
length, so it is a mistake in the application rather than a condition to degrade around, and it is
refused at the line that wrote it rather than at some later request whose failure would name a
server.

#### AgentKit-OTS-OllamaSharp-ContextLengthOption: The Size Reaches the Wire

**Test**:
`OllamaContextSizingChatClient_GetResponseAsync_ThroughTheRealClient_SendsTheContextLengthOnTheWire`

Normal operation through the real Ollama client. The decorator wraps a real `OllamaApiClient`
pointed at a loopback server, an ordinary exchange is held, and the request body the server received
is parsed and asserted to carry the chosen context length in its options block. This is the only
evidence that the option is translated onto the wire at all, and the whole "stated is true by
construction" claim rests on it — which is why it was written and passing before the published
maximum was removed from the precedence.

#### Composition Guard (Deliberately Unlinked)

**Test**: `OllamaContextSizingChatClient_Constructor_NullInnerClient_ThrowsArgumentNullException`

A decorator composed over nothing is a mistake in the application, and the delegating base refuses
it. The check is inherited rather than written here, so this is a defensive regression guard rather
than a promise the unit makes, and it is not linked to a requirement.
