## OllamaContextWindow Unit Verification Design

This document describes the unit-level verification strategy for the `OllamaContextWindow` record.

### Verification Approach

`OllamaContextWindow` is verified through unit tests over `Select`, the pure function holding the
whole precedence. Each test hands it real `OllamaSharp` values — a `RunningModel` carrying the
length a server loaded a model with, a `ShowModelResponse` carrying an architecture and its
published length as the `JsonElement` Ollama's metadata deserializes into — and asserts both the
figure chosen and the source reported for it.

Nothing is mocked, and nothing needs to be. `Select` contacts nothing, so the real types are used as
themselves; this repository carries no mocking library and every test double in it is hand-written.
Asserting the source as well as the figure is deliberate: the two failures that matter most —
believing a published maximum over an enforced length, and presenting an assumed figure as a
measured one — both produce a plausible number and are only visible in the source.

**What is out of automated scope, stated honestly.** `ReadAsync` is **not covered by the automated
suite**. Testing it usefully means driving the real Ollama client against real HTTP responses, so
that the client's own parsing runs rather than this project's assumptions about it being asserted
back — which needs an HTTP mocking library the repository does not yet carry, and response payloads
captured from a live server rather than invented. Both arrive together in a later change. Until then
no requirement is written against the reading path, and its tolerance of a server that declines a
query is evidenced by inspection only. The private helpers `FromLoadedModel`, `FromPublishedModel`,
`AsTokenCount` and `Tagged` are not tested directly; each is reached through `Select` by the
scenarios below, which is where their behavior is observable.

Unit tests reside in `OllamaContextWindowTests.cs` within the
`DemaConsulting.AgentKit.Agents.Ollama.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no Ollama server is contacted and **no network access is used**
- **Mocking**: None; hand-built `OllamaSharp` report values
- **Isolation**: Each test constructs its own reports; no state is shared

### Acceptance Criteria

A unit test run passes when every scenario below passes without error or exception beyond those
explicitly asserted. Any stated window overruled, any published maximum preferred over a loaded
length, any bare model name failing to match the tag the server resolved it to, any other model's
length borrowed, any published length the metadata carries as a usable number missed, any value that
is not one reported as a window, any zero or invented figure returned where a source was unusable,
or any invalid argument accepted rather than refused constitutes a failure.

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

#### AgentKitAgentsOllama-OllamaContextWindow-RejectsAMissingModelName: A Missing Model Name Is Refused

**Test**: `OllamaContextWindow_Select_NullModelName_Throws`

Error path. A request naming no model raises `ArgumentNullException` where the composing application
wrote it, rather than matching nothing and returning the assumed default — which would be
indistinguishable from a server that had not answered.

#### Supporting Corner Cases (Deliberately Unlinked)

**Tests**:

- `OllamaContextWindow_Select_StatedWindowOfZero_IsTreatedAsUnstated`
- `OllamaContextWindow_Select_ModelNamedUnderEitherReportedField_IsMatched`

A stated window of zero is an unset option rather than a claim, so it must not short-circuit the
ladder into a window of zero; and a loaded-model report that names the model in only one of its two
name fields must still match. Both are defensive checks on inputs the precedence tolerates rather
than promises about, so neither is linked to a requirement.
