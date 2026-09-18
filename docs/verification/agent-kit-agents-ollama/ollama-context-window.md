## OllamaContextWindow Unit Verification Design

This document describes the unit-level verification strategy for the `OllamaContextWindow` record.

### Verification Approach

`OllamaContextWindow` is verified at two levels, because it does two separable things. The
precedence is verified through unit tests over `Select`, the pure function holding it. Each test
hands it real `OllamaSharp` values — a `RunningModel` carrying the
length a server loaded a model with, a `ShowModelResponse` carrying an architecture and its
published length as the `JsonElement` Ollama's metadata deserializes into — and asserts both the
figure chosen and the source reported for it.

Nothing is mocked there, and nothing needs to be: `Select` contacts nothing, so the real types are
used as themselves.
Asserting the source as well as the figure is deliberate: the two failures that matter most —
believing a published maximum over an enforced length, and presenting an assumed figure as a
measured one — both produce a plausible number and are only visible in the source.

`ReadAsync` is verified through a second set of tests that stand up an HTTP server on loopback,
replay the captured payloads from the two endpoints the client really calls — `GET /api/ps` and
`POST /api/show` — and drive a **real** `OllamaApiClient` against it. Using the real client is the
whole point of the arrangement: a hand-faked `IOllamaApiClient` would assert this project's beliefs
about OllamaSharp back at it rather than test them, and this repository has already shipped one
defect of exactly that kind. These are the only tests in the repository that use a mocking library,
and this is the reason it was taken.

**What these tests reach that the precedence tests cannot.** `Select` is only ever handed what
survived a query, so nothing beneath the reading path can show that a server declining a query costs
a rung of the ladder rather than the run. The reading tests cover a declined loaded-model query, a
declined metadata query, both declined, and a canceled read — the last proving cancellation is not
swallowed by the tolerance that makes the other three work. They also pin the short-circuit: a
stated window is answered without the server being asked at all, asserted on the server having
logged no request.

**What remains outside automated scope.** The payloads are replayed rather than re-fetched, so a
future Ollama that changed the shape of either report would not be detected until the payloads were
captured again. The private helpers `FromLoadedModel`, `FromPublishedModel`,
`AsTokenCount` and `Tagged` are not tested directly; each is reached through `Select` by the
scenarios below, which is where their behavior is observable.

Unit tests reside in `OllamaContextWindowTests.cs` and
`OllamaContextWindowReadTests.cs` within the
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
explicitly asserted. Any stated window overruled, any published maximum preferred over a loaded
length, any bare model name failing to match the tag the server resolved it to, any other model's
length borrowed, any published length the metadata carries as a usable number missed, any value that
is not one reported as a window, any zero or invented figure returned where a source was unusable,
or any invalid argument accepted rather than refused constitutes a failure. On the reading path, any
query issued when the application stated a window, any declined query that fails the read rather
than costing it a rung, and any cancellation that yields a window instead of surfacing likewise
constitutes a failure.

### Test Scenarios

#### AgentKitAgentsOllama-OllamaContextWindow-StatedWindowWins: A Stated Window Settles It

**Test**: `OllamaContextWindow_Select_StatedWindow_WinsOverWhatTheServerReports`

Normal operation. A server reporting both a loaded length and a different published maximum is
overruled by a window the application stated, and the result is sourced as stated. An application
that states a window has usually done so to correct a reading, so a precedence that let the server
win would make that flag have no effect.

#### AgentKitAgentsOllama-OllamaContextWindow-PrefersTheLengthTheServerEnforces: The Enforced Length Wins Over the Maximum

**Test**: `OllamaContextWindow_Select_ModelLoaded_PrefersTheLengthTheServerEnforces`

Normal operation, and the scenario the unit exists for. A model loaded far below the maximum it
publishes yields the loaded length, sourced as the loaded model. This is the common real
configuration: preferring the maximum here would produce a window the server never intended to
honor.

#### AgentKitAgentsOllama-OllamaContextWindow-MatchesAnUntaggedModelName: A Bare Name Finds Its Own Model

**Test**: `OllamaContextWindow_Select_UntaggedModelName_MatchesTheLoadedLatestTag`

Boundary condition on name spelling. A model named without a tag is matched against the `latest` tag
the server reported, so the loaded length is found. A literal comparison would miss in the ordinary
case and fall silently to a lower-precedence source.

#### AgentKitAgentsOllama-OllamaContextWindow-DoesNotBorrowAnotherModelsWindow: Another Model's Window Is Not Taken

**Test**: `OllamaContextWindow_Select_DifferentModelLoaded_DoesNotBorrowItsWindow`

Error path. With some other model loaded and the named model publishing a maximum, the published
maximum is reported rather than the loaded model's length. A borrowed window would be specific, look
measured, and be wrong — the worst of the available failures.

#### AgentKitAgentsOllama-OllamaContextWindow-ReadsThePublishedLength: The Published Maximum Is Read

**Test**: `OllamaContextWindow_Select_PublishedLengthAsJsonNumber_IsRead`

Normal operation on the third rung of the ladder. With nothing loaded, the length the model
publishes — supplied as the `JsonElement` the server's metadata deserializes into — is reported,
sourced as the published maximum. Falling past a figure the server did offer would size every long
conversation on this model against the conservative default instead.

#### AgentKitAgentsOllama-OllamaContextWindow-FallsThroughMetadataCarryingNoContextLength: Falls Through

**Test**: `OllamaContextWindow_Select_MetadataWithoutAContextLength_FallsThrough`

Error path. Metadata naming an architecture but carrying only unrelated keys yields the assumed
default rather than zero or a neighboring value. Zero is a figure no session could be accounted
against, and a neighboring value would be a window invented from something that is not one.

#### AgentKitAgentsOllama-OllamaContextWindow-FallsThroughMetadataCarryingNoContextLength: An Unreadable Value Falls Through

**Test**: `OllamaContextWindow_Select_PublishedLengthNotAUsableNumber_FallsThrough`

Error path on the value rather than the key. Metadata carrying a context length as text, or as a
figure beyond what a token count can hold, yields the assumed default. Reading such a value as a
window — or rounding one into existence — would hand a session a figure that was never a context
length, and it would look measured.

#### AgentKitAgentsOllama-OllamaContextWindow-AssumesTheDefaultWhenNothingIsReported: The Conservative Default

**Test**: `OllamaContextWindow_Select_NothingReported_AssumesTheOllamaDefault`

Boundary condition at the bottom of the ladder. With nothing stated and nothing reported, Ollama's
own default is returned, sourced as assumed. The low figure is the deliberate choice: rotating
earlier than necessary costs summarizer calls, while rotating later loses history the provider has
already discarded.

#### AgentKitAgentsOllama-OllamaContextWindow-ToleratesADeclinedQuery: The Loaded-Model Query Is Declined

**Test**: `OllamaContextWindow_ReadAsync_RunningModelsQueryFails_ReportsThePublishedMaximum`

Error path on the transport, reached through the real Ollama client. The server answers the
loaded-model query with an error and the metadata query normally; the published maximum is reported
rather than the read failing. The reading is called unconditionally wherever a provider is
configured, so a server that declines this query — an older build, a proxy in front of it — must
cost a rung of the ladder and not the run.

#### AgentKitAgentsOllama-OllamaContextWindow-ToleratesADeclinedQuery: The Metadata Query Is Declined

**Test**: `OllamaContextWindow_ReadAsync_ModelMetadataQueryFails_ReportsTheEnforcedLength`

Error path, the mirror of the previous one and the more valuable direction. With the metadata query
failing and the loaded-model query answering, the enforced length still reaches the caller. The
figure that survives here is the one the server will actually enforce, so the tolerance must not be
implemented as "any failure drops to the bottom".

#### AgentKitAgentsOllama-OllamaContextWindow-ToleratesADeclinedQuery: Neither Query Is Answered

**Test**: `OllamaContextWindow_ReadAsync_NeitherQueryAnswers_AssumesTheOllamaDefault`

Boundary condition at the bottom of the ladder, reached through the transport rather than through
the precedence. A server that declines both queries still lets the application start, on Ollama's
own default named as assumed rather than on an exception the application would have to start around.

#### AgentKitAgentsOllama-OllamaContextWindow-RejectsAMissingModelName: A Missing Model Name Is Refused

**Test**: `OllamaContextWindow_Select_NullModelName_Throws`

Error path. A request naming no model raises `ArgumentNullException` where the composing application
wrote it, rather than matching nothing and returning the assumed default — which would be
indistinguishable from a server that had not answered.

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
- `OllamaContextWindow_ReadAsync_NothingLoaded_ReportsThePublishedMaximum`
- `OllamaContextWindow_ReadAsync_StatedWindow_DoesNotQueryTheServer`
- `OllamaContextWindow_ReadAsync_CanceledToken_PropagatesTheCancellation`

Four reading-path scenarios that are not linked here, because the promises they touch are already
made and evidenced at the level that owns them. The first two walk the top two rungs of the ladder
over HTTP — a loaded model yielding 65,536 while the same exchange publishes a maximum of 262,144,
and an empty loaded-model report falling to that maximum — which
`AgentKitAgentsOllama-OllamaContextWindow-PrefersTheLengthTheServerEnforces` and
`...-ReadsThePublishedLength` already promise, and which `Select` proves without a transport. The
first is cited instead by `AgentKit-Provider-Ollama` and by `AgentKit-OTS-OllamaSharp-Queries`,
where going through the wire is the point being made.

The third asserts that a stated window is answered without the server being asked at all: both
endpoints are mapped and would have produced a different figure, and the assertion is that the
server logged no request. That is a property of the short-circuit rather than a separate promise;
`...-StatedWindowWins` states it at the level that matters.

The fourth is the one that exists purely as a guard. The failure tolerance the three linked
scenarios above depend on is exactly what could swallow a cancellation, and a canceled read that
quietly returned an assumed window would have the caller continue on a figure it never asked for.
The token is canceled before the call, so the check happens before anything is sent and the scenario
is deterministic rather than timing-dependent.
