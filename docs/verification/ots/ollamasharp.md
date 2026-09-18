## OllamaSharp Verification

This document provides the verification evidence for the `OllamaSharp` OTS software item.

### Required Functionality

`OllamaSharp` supplies the types an Ollama server's reports arrive as: `RunningModel`, carrying the
context length a model was loaded with, and `ShowModelResponse`/`ModelInfo`, carrying the
architecture and the architecture-keyed published context length. It also supplies the queries that
fetch them — `ListRunningModelsAsync` and `ShowModelAsync` — over the Ollama HTTP API. AgentKit
implements no Ollama wire protocol, names no endpoint and parses no response; the
AgentKitAgentsOllama package asks for these reports and chooses among them.

### Verification Approach

Unlike the build-and-verify pipeline tools, this OTS item is a runtime library with no
self-validation CLI. Per `software-items.md`, an OTS library is verified through integration tests
proving the required functionality works. AgentKit's own offline tests construct real `RunningModel`
and `ShowModelResponse` values and read `ContextLength`, `Architecture` and `ExtraInfo` back through
the precedence, which proves the shapes are present and carry what the discovery depends on. These
tests run on every platform with no platform filter, so a single-OS run satisfies them.

The query APIs are verified separately, and could not be verified the same way: what is at stake is
the library's own request shaping and its own deserialization, which a hand-built value cannot
exercise because the test author picks its type. AgentKit drives a real `OllamaApiClient` against an
HTTP server started on loopback, replaying payloads captured verbatim from a live Ollama 0.34.1
server from the endpoints the client really calls — `GET /api/ps` and `POST /api/show`. The client
issues its own requests and parses its own responses, and the context lengths that come back out of
it are the ones the live server reported.

**What remains outside automated scope.** The payloads are replayed rather than re-fetched, so a
future Ollama release that changed the shape of either report would not be detected until the
payloads were captured again. No outbound network access is made and no Ollama server is contacted.

### Test Scenarios

#### OllamaContextWindow_Select_ModelLoaded_PrefersTheLengthTheServerEnforces

**Scenario**: A loaded-model report and a model-metadata response are constructed for one model,
carrying different context lengths, and handed to the precedence.

**Expected**: The loaded model's `ContextLength` is the figure reported, which requires that member
to be present and to carry what was set.

**Requirement coverage**: `AgentKit-OTS-OllamaSharp-ModelReports`.

#### OllamaContextWindow_Select_PublishedLengthAsJsonNumber_IsRead

**Scenario**: Model metadata is constructed naming an architecture and carrying its published
context length under the architecture-keyed entry, as a JSON number.

**Expected**: The published length is read, which requires `Architecture` and `ExtraInfo` to be
present and to carry what was set.

**Requirement coverage**: `AgentKit-OTS-OllamaSharp-ModelReports`.

#### OllamaContextWindow_ReadAsync_ModelLoaded_ReportsTheLengthTheServerEnforces

**Scenario**: A real `OllamaApiClient` is pointed at a loopback HTTP server replaying the captured
`/api/ps` and `/api/show` payloads from a live Ollama 0.34.1 server, and asked for the window of a
model that server holds loaded.

**Expected**: `ListRunningModelsAsync` issues `GET /api/ps` and deserializes the reply to a loaded
model carrying a context length of 65,536, which is the figure reported back.

**Requirement coverage**: `AgentKit-OTS-OllamaSharp-Queries`.

#### OllamaContextWindow_ReadAsync_NothingLoaded_ReportsThePublishedMaximum

**Scenario**: The same arrangement, with the captured `/api/ps` payload for a server holding nothing
resident.

**Expected**: `ShowModelAsync` issues `POST /api/show` and deserializes the reply to metadata
carrying the architecture and its published context length of 262,144, which is the figure reported
back.

**Requirement coverage**: `AgentKit-OTS-OllamaSharp-Queries`.

### Requirements Coverage

- **`AgentKit-OTS-OllamaSharp-ModelReports`**:
  OllamaContextWindow_Select_ModelLoaded_PrefersTheLengthTheServerEnforces,
  OllamaContextWindow_Select_PublishedLengthAsJsonNumber_IsRead
- **`AgentKit-OTS-OllamaSharp-Queries`**:
  OllamaContextWindow_ReadAsync_ModelLoaded_ReportsTheLengthTheServerEnforces,
  OllamaContextWindow_ReadAsync_NothingLoaded_ReportsThePublishedMaximum
