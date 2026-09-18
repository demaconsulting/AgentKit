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

Most tests in this system run with nothing started. The precedence is a pure function of what the
server reported, so hand-built and captured loaded-model and model-metadata values exercise it
completely. The unit-level reading tests are the exception: they start an HTTP server on loopback,
replay the captured payloads from it, and drive the real Ollama client against it. No outbound
network access is made and no Ollama server is contacted — the only server involved is started by
the test and stops with it — but this is where the repository's first mocking library is used, and
why. See *OllamaContextWindow Unit Verification Design*.

**What the system-level scope is, stated honestly.** The system scenario below exercises the
precedence, which is where a wrong window would actually come from. It does not read a server; the
reading of one, including a server that declines a query, is verified at the unit level against
payloads captured verbatim from a live Ollama 0.34.1 server. What is not automated is a run
against a *live* server: the captured payloads are replayed rather than re-fetched, so a future
Ollama that changed the shape of either report would not be detected until the payloads were
captured again.

System tests reside in `AgentKitAgentsOllamaTests.cs` within the
`DemaConsulting.AgentKit.Agents.Ollama.Tests` project.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Ollama server is contacted and **no outbound network access is
  made**; the unit-level reading tests start their own HTTP server on loopback and stop it with the
  test
- **File system**: None
- **Mocking**: None at this level; the server's reports are hand-built `OllamaSharp` values. The
  unit level uses WireMock.Net to replay captured payloads over loopback HTTP
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
