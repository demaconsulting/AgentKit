# AgentKitAgentsOllama System Verification Design

This document describes the system-level verification strategy for the AgentKitAgentsOllama system.

## Verification Approach

The AgentKitAgentsOllama system is verified through a system-level test that exercises the package
as a consumer meets it: given what an Ollama server reported, it must answer with the window a
conversation should be accounted against and with an honest account of where that number came from.
One scenario walks the whole precedence for a single model across the three states a real server
moves between over a conversation's life — the model loaded and enforcing a length, the model known
but not loaded, and a server that answered nothing — asserting both the figure and its source at
every step.

The precedence is verified rather than trusted because its failure mode is silent. A session told a
window larger than the server enforces keeps going until the provider has already truncated the
conversation, and nothing downstream reports an error or can detect it afterwards.

Every test in this system runs offline. The precedence is a pure function of what the server
reported, so hand-built loaded-model and model-metadata values exercise it completely without a
server, a network, or a mocking framework — this repository uses none, and every test double in it
is hand-written.

**What is out of automated scope, stated honestly.** `ReadAsync` — the reading of the server — is
**not covered by the automated suite**. Verifying it means running the real Ollama client against
real HTTP responses, so that the library's own parsing executes rather than this project's
assumptions about it being asserted back. That requires two things this change does not have: an
HTTP mocking library, which the repository currently carries none of, and response payloads captured
from a live Ollama server rather than invented. Until both exist, the reading path's behavior is
deliberately unclaimed: no requirement is written against it, and the tolerance it implements for a
server that declines a query is evidenced by inspection only. The precedence beneath it, which is
where a wrong window would actually come from, is fully covered.

System tests reside in `AgentKitAgentsOllamaTests.cs` within the
`DemaConsulting.AgentKit.Agents.Ollama.Tests` project.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Ollama server is contacted and **no network access is used**
- **File system**: None
- **Mocking**: None; the server's reports are hand-built `OllamaSharp` values
- **Isolation**: The test constructs its own reports; no state is shared

## Acceptance Criteria

A system-level test run passes when the scenario below passes without error or exception beyond
those explicitly asserted. Any published maximum reported while the model is loaded, any window
reported under a source it did not come from, any figure other than the conservative default when
the server reported nothing, and any assumed figure presented as measured constitutes a failure.

## Test Scenarios

### Context Window: The Enforced Window Is Reported, and Every Figure Names Its Source

**Test**: `AgentKitAgentsOllama_ContextWindow_ReportsTheEnforcedWindowAndNamesItsSource`

Verifies the promise an application acts on, across the whole ladder in one scenario. One model is
reported as loaded with a context length far below the maximum its metadata publishes. The scenario
asks for the window three times — with the model loaded, with it known but not loaded, and with
nothing reported at all — and asserts that the first yields the loaded length sourced as the loaded
model, the second the published maximum sourced as the published maximum, and the third the
conservative default sourced as assumed. An implementation that preferred the maximum, or that
reported any of the three under the wrong source, fails here.
