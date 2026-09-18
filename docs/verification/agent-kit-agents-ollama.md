# AgentKitAgentsOllama System Verification Design

This document describes the system-level verification strategy for the AgentKitAgentsOllama system.

## Verification Approach

The AgentKitAgentsOllama system is verified through two system-level tests that exercise the package
as a consumer meets it. The first covers reading: given what an Ollama server reported, the package
must answer with the window a conversation should be accounted against and with an honest account of
where that number came from, across the three states a real server moves between over a
conversation's life — the model loaded and running a length, the model known but not loaded, and an
application that stated the size it asked for. The second covers asking: the figure reported as
stated must be the figure every request actually asks the server for, proven by reading it back off
the bodies the real Ollama client sent.

The precedence is verified rather than trusted because its failure mode is silent. A session told a
window larger than the instance is running keeps going until the provider has already truncated the
conversation, and nothing downstream reports an error or can detect it afterwards.

Most tests in this system run with nothing started. The precedence is a pure function of what the
server reported, so hand-built and captured loaded-model values exercise it completely. The tests
that involve a transport — the system-level sizing scenario and the unit-level reading tests — start
an HTTP server on loopback, replay captured payloads from it or record what was sent to it, and
drive the real Ollama client against it. No outbound network access is made and no Ollama server is
contacted — the only server involved is started by the test and stops with it — but this is where
the repository's first mocking library is used, and why. See *OllamaContextWindow Unit Verification
Design* and *OllamaContextSizingChatClient Unit Verification Design*.

**What the system-level scope is, stated honestly.** The first scenario exercises the precedence,
which is where a wrong window would actually come from; it does not read a server. The reading of
one, including a server that declines a query, is verified at the unit level against payloads
captured verbatim from a live Ollama 0.34.1 server. What is not automated is a run against a *live*
server: the captured payloads are replayed rather than re-fetched, so a future Ollama that changed
the shape of the loaded-model report would not be detected until the payloads were captured again.
Nor is it automated that an Ollama server *honors* a requested context length — that was measured by
hand and is recorded in *OllamaContextWindow Unit Verification Design*; what the tests prove is that
the request carries it.

System tests reside in `AgentKitAgentsOllamaTests.cs` within the
`DemaConsulting.AgentKit.Agents.Ollama.Tests` project.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No Ollama server is contacted and **no outbound network access is
  made**; the sizing scenario and the unit-level reading tests start their own HTTP server on
  loopback and stop it with the test
- **File system**: None
- **Mocking**: The precedence scenario uses hand-built `OllamaSharp` values. The sizing scenario and
  the unit level use WireMock.Net on loopback HTTP
- **Isolation**: Each test constructs its own reports and, where one is needed, its own server and
  client; no state is shared

## Acceptance Criteria

A system-level test run passes when both scenarios below pass without error or exception beyond
those explicitly asserted. Any window reported under a source it did not come from, any figure other
than the conservative default when no instance was described, any assumed figure presented as
measured, and any request reaching the server without the size the package reported as stated
constitutes a failure.

## Test Scenarios

### Context Window: The Enforced Window Is Reported, and Every Figure Names Its Source

**Test**: `AgentKitAgentsOllama_ContextWindow_ReportsTheEnforcedWindowAndNamesItsSource`

Verifies the promise an application acts on, across the whole ladder in one scenario. One model is
asked about three times — reported as loaded at a length of the server's choosing, reported as not
loaded at all, and with the application stating the size it asked the server to run at — and the
scenario asserts that the first yields the loaded length sourced as the loaded model, the second the
conservative default sourced as assumed, and the third the stated figure sourced as stated. An
implementation that reported any of the three under the wrong source, or that invented a figure for
a model no instance was running, fails here.

### Context Sizing: Every Request Asks the Server for the Stated Window

**Test**: `AgentKitAgentsOllama_ContextSizing_EveryRequestAsksTheServerForTheStatedWindow`

Verifies that a stated window is true rather than merely claimed, end to end and over the wire. One
size chosen by the application is stated to the reading and composed onto the chat client; the
reading reports it as stated, two ordinary turns are then held through that client, and the scenario
asserts that both request bodies the server received asked for exactly that size. An implementation
that reported a figure it never asked for, or that asked only on the first turn, fails here — and
that second failure is the one nothing downstream could otherwise detect.
