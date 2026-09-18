## OllamaSharp Verification

This document provides the verification evidence for the `OllamaSharp` OTS software item.

### Required Functionality

`OllamaSharp` supplies three things AgentKit depends on and does not implement: `RunningModel`,
carrying the context length a running instance was loaded with; `ListRunningModelsAsync`, which
fetches that report over the Ollama HTTP API; and, through its `IChatClient` implementation, the
carriage of a named context-length chat option into the Ollama request's own options block on the
wire. AgentKit implements no Ollama wire protocol, names no endpoint and parses no response; the
AgentKitAgentsOllama package reads this report and annotates requests.

The model-metadata query is deliberately no longer used. It reports what a model file could support
rather than what an instance is running, and against a live server the two differed by a factor of
thirty-two in the direction that loses conversation history.

### Verification Approach

Unlike the build-and-verify pipeline tools, this OTS item is a runtime library with no
self-validation CLI. Per `software-items.md`, an OTS library is verified through integration tests
proving the required functionality works. AgentKit's own offline tests construct real `RunningModel`
values and read `Name`, `ModelName` and `ContextLength` back through the precedence, which proves
the shape is present and carries what the discovery depends on. These tests run on every platform
with no platform filter, so a single-OS run satisfies them.

The query API and the option carriage are verified separately, and could not be verified the same
way: what is at stake in both is the library's own request shaping and its own deserialization,
which a hand-built value cannot exercise because the test author picks its type. AgentKit drives a
real `OllamaApiClient` against an HTTP server started on loopback — replaying payloads captured
verbatim from a live Ollama 0.34.1 server for the query, and reading back the request body the
client sent for the option. The client issues its own requests and parses its own responses.

**What remains outside automated scope.** The payloads are replayed rather than re-fetched, so a
future Ollama release that changed the shape of the loaded-model report would not be detected until
the payloads were captured again. That a real Ollama server *acts* on a `num_ctx` option was
measured by hand and is recorded in *OllamaContextWindow Unit Verification Design*; what is
automated here is that the library puts it on the wire. No outbound network access is made and no
Ollama server is contacted.

### Test Scenarios

#### OllamaContextWindow_Select_ModelLoaded_PrefersTheLengthTheServerEnforces

**Scenario**: A loaded-model report is constructed for one model, carrying a context length, and
handed to the precedence.

**Expected**: The loaded model's `ContextLength` is the figure reported, which requires that member
to be present and to carry what was set.

**Requirement coverage**: `AgentKit-OTS-OllamaSharp-ModelReports`.

#### OllamaContextWindow_Select_ModelNamedUnderEitherReportedField_IsMatched

**Scenario**: Two loaded-model reports are constructed, each naming the model in only one of the two
name fields the type carries.

**Expected**: Each is matched and yields its own context length, which requires both `Name` and
`ModelName` to be present and independently settable.

**Requirement coverage**: `AgentKit-OTS-OllamaSharp-ModelReports`.

#### OllamaContextWindow_ReadAsync_ModelLoaded_ReportsTheLengthTheServerEnforces

**Scenario**: A real `OllamaApiClient` is pointed at a loopback HTTP server replaying the captured
`/api/ps` payload from a live Ollama 0.34.1 server, and asked for the window of a model that server
holds loaded.

**Expected**: `ListRunningModelsAsync` issues `GET /api/ps` and deserializes the reply to a loaded
model carrying a context length of 65,536, which is the figure reported back.

**Requirement coverage**: `AgentKit-OTS-OllamaSharp-Queries`.

#### OllamaContextWindow_ReadAsync_NothingLoaded_AssumesTheOllamaDefault

**Scenario**: The same arrangement, with the captured `/api/ps` payload for a server holding nothing
resident.

**Expected**: `ListRunningModelsAsync` issues the same request and deserializes an empty report
without error, so the reading falls to the conservative default rather than failing.

**Requirement coverage**: `AgentKit-OTS-OllamaSharp-Queries`.

#### OllamaContextSizingChatClient_GetResponseAsync_ThroughTheRealClient_SendsTheContextLengthOnTheWire

**Scenario**: A real `OllamaApiClient` is used as an `IChatClient` beneath AgentKit's sizing
decorator, pointed at a loopback HTTP server, and an ordinary whole-response exchange is held. The
decorator names a context length of 8,192 as an additional chat option.

**Expected**: The request body the server receives carries `options.num_ctx` set to 8,192, which
requires the library to translate the provider-neutral option into Ollama's own request shape.

**Requirement coverage**: `AgentKit-OTS-OllamaSharp-ContextLengthOption`.

### Requirements Coverage

- **`AgentKit-OTS-OllamaSharp-ModelReports`**:
  OllamaContextWindow_Select_ModelLoaded_PrefersTheLengthTheServerEnforces,
  OllamaContextWindow_Select_ModelNamedUnderEitherReportedField_IsMatched
- **`AgentKit-OTS-OllamaSharp-Queries`**:
  OllamaContextWindow_ReadAsync_ModelLoaded_ReportsTheLengthTheServerEnforces,
  OllamaContextWindow_ReadAsync_NothingLoaded_AssumesTheOllamaDefault
- **`AgentKit-OTS-OllamaSharp-ContextLengthOption`**:
  OllamaContextSizingChatClient_GetResponseAsync_ThroughTheRealClient_SendsTheContextLengthOnTheWire
