## OllamaSharp Verification

This document provides the verification evidence for the `OllamaSharp` OTS software item.

### Required Functionality

`OllamaSharp` supplies the types an Ollama server's reports arrive as: `RunningModel`, carrying the
context length a model was loaded with, and `ShowModelResponse`/`ModelInfo`, carrying the
architecture and the architecture-keyed published context length. AgentKit implements no Ollama wire
protocol; the AgentKitAgentsOllama package reads these shapes and chooses among them.

### Verification Approach

Unlike the build-and-verify pipeline tools, this OTS item is a runtime library with no
self-validation CLI. Per `software-items.md`, an OTS library is verified through integration tests
proving the required functionality works. AgentKit's own offline tests construct real `RunningModel`
and `ShowModelResponse` values and read `ContextLength`, `Architecture` and `ExtraInfo` back through
the precedence, which proves the shapes are present and carry what the discovery depends on. These
tests run on every platform with no platform filter, so a single-OS run satisfies them.

**What is out of automated scope, stated honestly.** The library's **query** APIs —
`ListRunningModelsAsync` and `ShowModelAsync` — are **not covered by the automated suite**, and no
requirement is written against them here. Evidencing them means driving the real client against real
HTTP responses so its own parsing runs, which needs an HTTP mocking library the repository does not
yet carry and payloads captured from a live Ollama server rather than invented. That evidence, and
the requirement it supports, arrive together in a later change; see *AgentKitAgentsOllama System
Verification Design*.

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

### Requirements Coverage

- **`AgentKit-OTS-OllamaSharp-ModelReports`**:
  OllamaContextWindow_Select_ModelLoaded_PrefersTheLengthTheServerEnforces,
  OllamaContextWindow_Select_PublishedLengthAsJsonNumber_IsRead
