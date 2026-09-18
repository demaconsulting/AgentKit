## OllamaContextWindow Unit Verification Design

This document describes the unit-level verification strategy for the `OllamaContextWindow` record.

### Verification Approach

`OllamaContextWindow` is verified at two levels, because it does two separable things. The
precedence is verified through unit tests over `Select`, the pure function holding it. Each test
hands it real `OllamaSharp` values — a `RunningModel` carrying the length an instance is running —
and asserts both the figure chosen and the source reported for it.

Nothing is mocked there, and nothing needs to be: `Select` contacts nothing, so the real types are
used as themselves. Asserting the source as well as the figure is deliberate: the failure that
matters most — presenting an assumed figure as a measured one — produces a plausible number and is
only visible in the source.

`ReadAsync` is verified through a second set of tests that stand up an HTTP server on loopback,
replay the captured payloads from the one endpoint the client really calls, `GET /api/ps`, and drive
a **real** `OllamaApiClient` against it. Using the real client is the whole point of the
arrangement: a hand-faked `IOllamaApiClient` would assert this project's beliefs about OllamaSharp
back at it rather than test them, and this repository has already shipped one defect of exactly that
kind.

**What these tests reach that the precedence tests cannot.** `Select` is only ever handed what
survived a query, so nothing beneath the reading path can show that a server declining the query
costs a rung of the ladder rather than the run. The reading tests cover a declined query, a server
that accepts the connection and never answers, and a canceled read — the last proving cancellation
is not swallowed by the tolerance that makes the other two work. They also pin the short-circuit: a
stated window is answered without the server being asked at all, asserted on the server having
logged no request. And one test pins a *negative*: that the model-metadata endpoint is never called,
whatever the server would have said.

**What remains outside automated scope.** The payloads are replayed rather than re-fetched, so a
future Ollama that changed the shape of the loaded-model report would not be detected until the
payloads were captured again. The private helpers `FromLoadedModel` and `Tagged` are not tested
directly; each is reached through `Select` by the scenarios below, which is where their behavior is
observable.

#### The Measurements This Precedence Rests On

The removal of a published-maximum rung from this precedence was decided by measurement against a
live Ollama 0.34.1 server, not by argument. For `qwen3.5:9b`:

| Source | Tokens |
| --- | --- |
| `/api/show` published maximum | 262,144 |
| `/api/ps` while actually loaded (observed within one session) | 65,536, then 8,192, then 4,096 |

The published maximum is what the model file could support. What the instance uses is decided at
load time, and was observed at three different values in a single session. Accounting against
262,144 while the instance ran at 4,096 — a figure thirty-two times too large — would mean never
rotating until the server had already discarded most of the conversation, with nothing reporting it.

Two further measurements against the same server established that the size can be asked for rather
than guessed at, which is what made removing the rung safe:

- Loading with `options.num_ctx = 8192` produced `/api/ps` reporting a `context_length` of 8,192.
- A `num_ctx` of 4,096 named as an additional chat option on an ordinary whole-response request
  caused the model to reload, after which `/api/ps` reported 4,096. This is the path AgentKit's
  session factory drives, which is why the control could be built as a chat-client decorator.

These figures are recorded here rather than in the design documents because they are observations of
a particular server at a particular version, not statements of intent.

Unit tests reside in `OllamaContextWindowTests.cs` and `OllamaContextWindowReadTests.cs` within the
`DemaConsulting.AgentKit.Agents.Ollama.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no Ollama server is contacted and **no outbound network access is
  made**. The reading tests start a WireMock.Net server on loopback and stop it with the test that
  started it
- **Mocking**: The precedence tests use hand-built `OllamaSharp` report values. The reading tests
  use WireMock.Net to replay payloads captured verbatim from a live Ollama 0.34.1 server, so that
  the real `OllamaApiClient` executes its own request shaping and deserialization
- **Isolation**: Each test constructs its own reports and, where one is needed, its own server and
  client; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any stated window overruled, any bare model name failing to match the tag the
server resolved it to, any other model's length borrowed, any figure taken from what a model file
publishes, any zero or invented figure returned where no instance was described, or any invalid
argument accepted rather than refused constitutes a failure. On the reading path, any query issued
when the application stated a window, any request to the model-metadata endpoint at all, any
declined or unanswered query that fails the read rather than costing it a rung, and any cancellation
that yields a window instead of surfacing likewise constitutes a failure.

### Test Scenarios

#### AgentKitAgentsOllama-OllamaContextWindow-StatedWindowWins: A Stated Window Settles It

**Test**: `OllamaContextWindow_Select_StatedWindow_WinsOverWhatTheServerReports`

Normal operation. A server reporting a loaded length is overruled by a window the application
stated, and the result is sourced as stated. An application that states a window has also asked the
server to run at it on every request, so the stated figure is the one that describes the instance
about to answer.

#### AgentKitAgentsOllama-OllamaContextWindow-PrefersTheLengthTheServerEnforces: The Running Instance's Length Is Reported

**Test**: `OllamaContextWindow_Select_ModelLoaded_PrefersTheLengthTheServerEnforces`

Normal operation, and the scenario the unit exists for. A model the server reports as loaded yields
the length that instance is running, sourced as the loaded model. This is the only measurement the
unit can make, and it is the only figure that describes the instance rather than the model file.

#### AgentKitAgentsOllama-OllamaContextWindow-MatchesAnUntaggedModelName: A Bare Name Finds Its Own Model

**Test**: `OllamaContextWindow_Select_UntaggedModelName_MatchesTheLoadedLatestTag`

Boundary condition on name spelling. A model named without a tag is matched against the `latest` tag
the server reported, so the loaded length is found. A literal comparison would miss in the ordinary
case and fall silently to the conservative default.

#### AgentKitAgentsOllama-OllamaContextWindow-DoesNotBorrowAnotherModelsWindow: Another Model's Window Is Not Taken

**Test**: `OllamaContextWindow_Select_DifferentModelLoaded_DoesNotBorrowItsWindow`

Error path. With some other model loaded, the conservative default is reported rather than that
model's length. A borrowed window would be specific, look measured, and be wrong — the worst of the
available failures.

#### AgentKitAgentsOllama-OllamaContextWindow-NeverConsultsAPublishedMaximum: The Metadata Endpoint Is Never Asked

**Test**: `OllamaContextWindow_ReadAsync_ModelNotLoaded_NeverAsksForThePublishedMaximum`

Error path stated as a negative, reached through the real Ollama client. A server holding nothing
loaded also serves the model-metadata endpoint, which would answer; the scenario asserts the
conservative default *and* that no request was made to that endpoint. Asserting the absence of the
call pins the rung's removal rather than merely the fact that nothing currently reaches it: a future
change that reintroduced the query would fail here even if the figure it produced happened to be
discarded.

#### AgentKitAgentsOllama-OllamaContextWindow-AssumesTheDefaultWhenNothingIsReported: The Conservative Default

**Tests**:

- `OllamaContextWindow_Select_NothingReported_AssumesTheOllamaDefault`
- `OllamaContextWindow_ReadAsync_NothingLoaded_AssumesTheOllamaDefault`

Boundary condition at the bottom of the ladder, exercised through the precedence and again through
the transport against the empty loaded-model report a real server returns before anything is
resident. With nothing stated and no instance described, Ollama's own default is returned, sourced
as assumed. The low figure is the deliberate choice: rotating earlier than necessary costs
summarizer calls, while rotating later loses history the provider has already discarded.

#### AgentKitAgentsOllama-OllamaContextWindow-ToleratesADeclinedQuery: The Server Declines, or Never Answers

**Tests**:

- `OllamaContextWindow_ReadAsync_RunningModelsQueryFails_AssumesTheOllamaDefault`
- `OllamaContextWindow_ReadAsync_ServerNeverAnswers_AssumesTheOllamaDefault`

Error paths on the transport, reached through the real Ollama client. In the first the server
answers with an error; in the second it accepts the connection and never replies, so the client's
own timeout ends the query — which arrives as the same exception type a caller's cancellation would,
and is told apart only by the token. Both yield the conservative default rather than failing the
read. The reading is called unconditionally wherever a provider is configured, so a server that
declines or hangs must cost a rung of the ladder and not the run.

The second scenario sets a 60-second server delay against a 4-second client timeout. The margin is
one-sided now that a single endpoint is involved: nothing else has to be served inside the timeout,
so the only requirement is that the delay dwarfs it and the hang is never a race under a parallel
suite.

#### AgentKitAgentsOllama-OllamaContextWindow-RejectsAMissingModelName: A Missing Model Name Is Refused

**Test**: `OllamaContextWindow_Select_ModelNameThatNamesNothing_Throws`

Error path, covering both a null name and an empty one. A request naming no model raises from the
`ArgumentException` family where the composing application wrote it, rather than matching nothing
and returning the assumed default — which would be indistinguishable from a server that had not
answered. The empty case is the one that mattered: it became `":latest"`, matched nothing, and
returned a plausible figure.

#### AgentKitAgentsOllama-OllamaContextWindow-RefusesAWindowNoSessionCouldUse: A Window No Session Could Use Is Refused

**Test**: `OllamaContextWindow_Construct_WindowOfZeroOrLess_Throws`

Error path, on the public record rather than the ladder. Zero, minus one and `int.MinValue` are each
rejected with `ArgumentOutOfRangeException` naming `Tokens`. The reading's own paths cannot produce
such a window — every rung guards its figure — so this scenario exists because the record is public
and a caller can construct an instance that contradicts the type's own documentation. The consumers
a bad window would reach do re-check it, but an invariant that holds only because every consumer
re-checks is a habit rather than a guarantee. Refusing it where it is claimed is what makes the
documented promise true of every instance that can exist.

#### Supporting Corner Cases (Deliberately Unlinked)

**Tests**:

- `OllamaContextWindow_Select_StatedWindowOfZero_IsTreatedAsUnstated`
- `OllamaContextWindow_Select_UntaggedLoadedModelName_MatchesATaggedRequest`
- `OllamaContextWindow_Select_ModelNamedUnderEitherReportedField_IsMatched`

A stated window of zero is an unset option rather than a claim, so it must not short-circuit the
ladder into a window of zero; a loaded-model report that names the model bare, where the caller
named the tag, must still match — the mirror of the ordinary direction, tagging the reported side
rather than the asked one; and a loaded-model report that names the model in only one of its two
name fields must still match. All three are defensive checks on inputs the precedence tolerates
rather than promises about, so none is linked to a requirement.

#### Reading the Server Over HTTP (Deliberately Unlinked)

**Tests**:

- `OllamaContextWindow_ReadAsync_ModelLoaded_ReportsTheLengthTheServerEnforces`
- `OllamaContextWindow_ReadAsync_StatedWindow_DoesNotQueryTheServer`
- `OllamaContextWindow_ReadAsync_CanceledToken_PropagatesTheCancellation`

Three reading-path scenarios that are not linked here, because the promises they touch are already
made and evidenced at the level that owns them. The first walks the top rung of the ladder over
HTTP — a loaded model yielding 65,536 out of a payload a real server sent — which
`AgentKitAgentsOllama-OllamaContextWindow-PrefersTheLengthTheServerEnforces` already promises and
which `Select` proves without a transport. It is cited instead by `AgentKit-Provider-Ollama` and by
`AgentKit-OTS-OllamaSharp-Queries`, where going through the wire is the point being made.

The second asserts that a stated window is answered without the server being asked at all: the
endpoint is mapped and would have produced a different figure, and the assertion is that the server
logged no request. That is a property of the short-circuit rather than a separate promise;
`...-StatedWindowWins` states it at the level that matters. It is also what keeps discovery from
loading a model as a side effect of asking.

The third is the one that exists purely as a guard. The failure tolerance the linked scenarios above
depend on is exactly what could swallow a cancellation, and a canceled read that quietly returned an
assumed window would have the caller continue on a figure it never asked for. The token is canceled
before the call, so the check happens before anything is sent and the scenario is deterministic
rather than timing-dependent.
